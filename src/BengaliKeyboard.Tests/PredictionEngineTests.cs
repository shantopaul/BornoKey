using System.Diagnostics;
using System.IO;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Input;
using BengaliKeyboard.Prediction;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class PredictionEngineTests : IDisposable
{
    private readonly string _tempRootDir;
    private readonly AppPaths _appPaths;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly JsonSettingsService _settingsService;

    public PredictionEngineTests()
    {
        _tempRootDir = Path.Combine(
            Path.GetTempPath(),
            "BornoKey_Phase8Tests_" + Guid.NewGuid().ToString("N"));

        _appPaths = new AppPaths(_tempRootDir);
        _appPaths.EnsureDirectoriesCreated();

        _connectionFactory = new SqliteConnectionFactory(_appPaths);
        _databaseInitializer = new DatabaseInitializer(_appPaths, _connectionFactory);
        _settingsService = new JsonSettingsService(_appPaths);
    }

    [Fact]
    public async Task GetWordCompletionsAsync_TypingAmaPrefix_SurfacesExpectedCompletionsUnder50Ms()
    {
        await _databaseInitializer.InitializeAsync();
        var rankingService = CreatePredictionService();
        await rankingService.InitializeAsync();

        // Warm up connection pool once, then measure actual query latency
        _ = await rankingService.GetWordCompletionsAsync("আ", maxResults: 4);

        var stopwatch = Stopwatch.StartNew();
        var candidates = await rankingService.GetWordCompletionsAsync("আমা", maxResults: 4);
        stopwatch.Stop();

        Assert.Equal(4, candidates.Count);
        var words = candidates.Select(c => c.Word).ToList();
        Assert.Contains("আমার", words);
        Assert.Contains("আমাকে", words);
        Assert.Contains("আমাদের", words);
        Assert.Contains("আমায়", words);
        Assert.All(candidates, c =>
        {
            Assert.False(c.IsNextWordPrediction);
            Assert.Equal(3, c.ReplacePrefixLength);
        });

        Assert.True(
            stopwatch.ElapsedMilliseconds <= 50,
            $"Expected word completion query <= 50ms, but took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task GetNextWordPredictionsAsync_BigramAndTrigramContext_ReturnsExpectedRankedWords()
    {
        await _databaseInitializer.InitializeAsync();
        var rankingService = CreatePredictionService();
        await rankingService.InitializeAsync();

        // 1. Single-word Bigram context: "আমি" -> [বাংলা, একটি, আজ, তোমাকে, ভালো]
        var bigramCandidates = await rankingService.GetNextWordPredictionsAsync("আমি", previousWord2: null, maxResults: 5);
        var bigramWords = bigramCandidates.Select(c => c.Word).ToList();

        Assert.Equal(5, bigramWords.Count);
        Assert.Equal("বাংলা", bigramWords[0]);
        Assert.Contains("একটি", bigramWords);
        Assert.Contains("আজ", bigramWords);
        Assert.Contains("তোমাকে", bigramWords);
        Assert.Contains("ভালো", bigramWords);

        // 2. Two-word Trigram context: "আমি বাংলা" -> [লিখতে, ভাষা, শিখছি, পড়তে]
        var trigramCandidates = await rankingService.GetNextWordPredictionsAsync("আমি", "বাংলা", maxResults: 4);
        var trigramWords = trigramCandidates.Select(c => c.Word).ToList();

        Assert.Equal(4, trigramWords.Count);
        Assert.Equal("লিখতে", trigramWords[0]);
        Assert.Equal("ভাষা", trigramWords[1]);
        Assert.Equal("শিখছি", trigramWords[2]);
        Assert.Equal("পড়তে", trigramWords[3]);
        Assert.All(trigramCandidates, c => Assert.True(c.IsNextWordPrediction));
    }

    [Fact]
    public async Task BengaliInputEngine_And_SuggestionBarViewModel_ReplacesPartialPrefixAndAdvancesToNextWordPrediction()
    {
        await _databaseInitializer.InitializeAsync();
        await _settingsService.LoadAsync();

        var rankingService = CreatePredictionService();
        await rankingService.InitializeAsync();

        var messenger = new StrongReferenceMessenger();
        var unicodeProcessor = new UnicodeProcessor();
        var fakeInjector = new FakeTextInjector();

        using var inputEngine = new BengaliInputEngine(
            unicodeProcessor,
            fakeInjector,
            rankingService,
            _settingsService,
            messenger);

        using var suggestionBarVm = new SuggestionBarViewModel(inputEngine, messenger);

        // Type "আ", "ম", "া" -> buffer is "আমা"
        await inputEngine.ProcessKeyAsync(new KeyDefinition { Id = "A", UnicodeOutput = "আ", KeyType = KeyType.Character });
        await inputEngine.ProcessKeyAsync(new KeyDefinition { Id = "M", UnicodeOutput = "ম", KeyType = KeyType.Character });
        await inputEngine.ProcessKeyAsync(new KeyDefinition { Id = "KarA", UnicodeOutput = "া", KeyType = KeyType.VowelSign });

        Assert.Equal("আমা", inputEngine.CurrentWordBuffer);
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "আমার");

        var amarCandidate = suggestionBarVm.Candidates.First(c => c.Word == "আমার");
        await suggestionBarVm.SelectCandidateCommand.ExecuteAsync(amarCandidate);

        // Selecting "আমার" replaces the 3 scalars of "আমা" with "আমার "
        Assert.Equal(3, fakeInjector.LastReplaceBackspaceCount);
        Assert.Equal("আমার ", fakeInjector.LastReplacementText);
        Assert.Equal(string.Empty, inputEngine.CurrentWordBuffer);

        // Next-word predictions for "আমার" should now be active in SuggestionBarViewModel
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "সোনার" && c.IsNextWordPrediction);
    }

    [Fact]
    public async Task RecordAcceptedWordAsync_PersistsCustomWordInUserDictionary_AndSurfacesInCompletions()
    {
        await _databaseInitializer.InitializeAsync();
        var rankingService = CreatePredictionService();
        await rankingService.InitializeAsync();

        await rankingService.RecordAcceptedWordAsync("বর্ণকী", previousWord: "আমি");
        await rankingService.RecordAcceptedWordAsync("বর্ণকী", previousWord: "আমি");
        await rankingService.RecordAcceptedWordAsync("বর্ণকী", previousWord: "আমি");

        var completions = await rankingService.GetWordCompletionsAsync("বর্ণ", maxResults: 4);
        Assert.NotEmpty(completions);
        Assert.Equal("বর্ণকী", completions[0].Word);

        // Learned user bigram ("আমি" -> "বর্ণকী") also boosts "বর্ণকী" into next-word predictions after "আমি"
        var nextAfterAmi = await rankingService.GetNextWordPredictionsAsync("আমি", maxResults: 4);
        Assert.Contains(nextAfterAmi, c => c.Word == "বর্ণকী");
    }

    [Fact]
    public async Task EnglishPrediction_CompletionsContractionsCasingAndNextWordNGrams_WorkSeamlessly()
    {
        await _databaseInitializer.InitializeAsync();
        await _settingsService.LoadAsync();

        var rankingService = CreatePredictionService();
        await rankingService.InitializeAsync();

        var messenger = new StrongReferenceMessenger();
        var unicodeProcessor = new UnicodeProcessor();
        var fakeInjector = new FakeTextInjector();

        using var inputEngine = new BengaliInputEngine(
            unicodeProcessor,
            fakeInjector,
            rankingService,
            _settingsService,
            messenger);

        using var suggestionBarVm = new SuggestionBarViewModel(inputEngine, messenger);

        // 1. Switch to English mode and verify starter suggestions switch to English
        inputEngine.SetLanguage(LanguageMode.English);
        Assert.Equal(LanguageMode.English, inputEngine.CurrentLanguage);
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "I");
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "Hello");
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "Thank");

        // 2. Lowercase prefix "hel" -> ["hello", "help", "helpful"]
        var lowerCompletions = await rankingService.GetWordCompletionsAsync("hel", maxResults: 4);
        var lowerWords = lowerCompletions.Select(c => c.Word).ToList();
        Assert.Contains("hello", lowerWords);
        Assert.Contains("help", lowerWords);
        Assert.Contains("helpful", lowerWords);

        // 3. Title Case prefix "Hel" -> ["Hello", "Help", "Helpful"] and ALL CAPS "HEL" -> ["HELLO", "HELP", "HELPFUL"]
        var titleCompletions = await rankingService.GetWordCompletionsAsync("Hel", maxResults: 4);
        Assert.Contains(titleCompletions, c => c.Word == "Hello");
        Assert.Contains(titleCompletions, c => c.Word == "Help");

        var upperCompletions = await rankingService.GetWordCompletionsAsync("HEL", maxResults: 4);
        Assert.Contains(upperCompletions, c => c.Word == "HELLO");

        // 4. Contractions: "dont" -> "don't", "im" -> "I'm"
        var dontCompletions = await rankingService.GetWordCompletionsAsync("dont", maxResults: 4);
        Assert.Equal("don't", dontCompletions[0].Word);

        var imCompletions = await rankingService.GetWordCompletionsAsync("im", maxResults: 4);
        Assert.Equal("I'm", imCompletions[0].Word);

        // 5. Type "T", "h", "a" in English mode and select "Thank"
        await inputEngine.ProcessKeyAsync(new KeyDefinition { Id = "T", UnicodeOutput = "t", AlternateOutput = "T", KeyType = KeyType.Character }, isShifted: true);
        await inputEngine.ProcessKeyAsync(new KeyDefinition { Id = "H", UnicodeOutput = "h", AlternateOutput = "H", KeyType = KeyType.Character });
        await inputEngine.ProcessKeyAsync(new KeyDefinition { Id = "A", UnicodeOutput = "a", AlternateOutput = "A", KeyType = KeyType.Character });

        Assert.Equal("Tha", inputEngine.CurrentWordBuffer);
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "Thank");

        var thankCandidate = suggestionBarVm.Candidates.First(c => c.Word == "Thank");
        await suggestionBarVm.SelectCandidateCommand.ExecuteAsync(thankCandidate);

        Assert.Equal(3, fakeInjector.LastReplaceBackspaceCount);
        Assert.Equal("Thank ", fakeInjector.LastReplacementText);

        // Next-word predictions after "Thank" (case-insensitive bigram lookup for "thank") should surface "you"
        Assert.Contains(suggestionBarVm.Candidates, c => c.Word == "you" && c.IsNextWordPrediction);

        // 6. Trigram next-word prediction: "How" + "are" -> "you"
        var trigramNext = await rankingService.GetNextWordPredictionsAsync("How", "are", maxResults: 4);
        Assert.NotEmpty(trigramNext);
        Assert.Equal("you", trigramNext[0].Word);
    }

    private SuggestionRankingService CreatePredictionService()
    {
        var dictionaryService = new DictionaryService(_connectionFactory);
        var completionService = new WordCompletionService(dictionaryService);
        var nGramModel = new NGramModel(_connectionFactory, dictionaryService);
        var nextWordService = new NextWordPredictionService(nGramModel);
        return new SuggestionRankingService(dictionaryService, completionService, nextWordService, nGramModel);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempRootDir))
            {
                Directory.Delete(_tempRootDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup race conditions
        }
    }

    private sealed class FakeTextInjector : ITextInjector
    {
        public int LastReplaceBackspaceCount { get; private set; }

        public string LastReplacementText { get; private set; } = string.Empty;

        public bool IsTargetWindowElevated => false;

        public bool SendUnicodeText(string text) => true;

        public bool SendBackspaces(int count) => true;

        public bool ReplaceText(int backspaceCount, string replacementText)
        {
            LastReplaceBackspaceCount = backspaceCount;
            LastReplacementText = replacementText;
            return true;
        }

        public bool SendEnter() => true;

        public bool SendSpace() => true;
    }
}
