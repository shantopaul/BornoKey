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

        await rankingService.RecordAcceptedWordAsync("বর্ণকী");
        await rankingService.RecordAcceptedWordAsync("বর্ণকী");

        var completions = await rankingService.GetWordCompletionsAsync("বর্ণ", maxResults: 4);
        Assert.NotEmpty(completions);
        Assert.Equal("বর্ণকী", completions[0].Word);
    }

    private SuggestionRankingService CreatePredictionService()
    {
        var dictionaryService = new DictionaryService(_connectionFactory);
        var completionService = new WordCompletionService(dictionaryService);
        var nGramModel = new NGramModel(_connectionFactory, dictionaryService);
        var nextWordService = new NextWordPredictionService(nGramModel);
        return new SuggestionRankingService(dictionaryService, completionService, nextWordService);
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
