using System.IO;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Emoji;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.UI.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class EmojiUsageTests : IDisposable
{
    private readonly string _tempRootDir;
    private readonly AppPaths _appPaths;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly JsonSettingsService _settingsService;

    public EmojiUsageTests()
    {
        _tempRootDir = Path.Combine(
            Path.GetTempPath(),
            "BornoKey_Phase6Tests_" + Guid.NewGuid().ToString("N"));

        _appPaths = new AppPaths(_tempRootDir);
        _appPaths.EnsureDirectoriesCreated();

        _connectionFactory = new SqliteConnectionFactory(_appPaths);
        _databaseInitializer = new DatabaseInitializer(_appPaths, _connectionFactory);
        _settingsService = new JsonSettingsService(_appPaths);
    }

    [Fact]
    public async Task GetCategories_And_GetByCategoryAsync_ReturnsAllTenStandardCategories()
    {
        await _databaseInitializer.InitializeAsync();
        var service = CreateEmojiService();

        var categories = service.GetCategories();

        Assert.Equal(10, categories.Count);
        Assert.Equal("Frequently Used", categories[0]);
        Assert.Equal("Smileys & Emotions", categories[1]);
        Assert.Equal("People & Body", categories[2]);
        Assert.Equal("Animals & Nature", categories[3]);
        Assert.Equal("Food & Drink", categories[4]);
        Assert.Equal("Travel & Places", categories[5]);
        Assert.Equal("Activities", categories[6]);
        Assert.Equal("Objects", categories[7]);
        Assert.Equal("Symbols", categories[8]);
        Assert.Equal("Flags", categories[9]);

        for (var i = 1; i < categories.Count; i++)
        {
            var items = await service.GetByCategoryAsync(categories[i]);
            Assert.NotEmpty(items);
        }
    }

    [Fact]
    public async Task SearchAsync_EnglishAndBengaliKeywords_FiltersEmojisInRealTime()
    {
        await _databaseInitializer.InitializeAsync();
        var service = CreateEmojiService();

        var smileResults = await service.SearchAsync("smile");
        var smileGlyphs = smileResults.Select(x => x.Emoji).ToList();
        Assert.Contains("😀", smileGlyphs);
        Assert.Contains("😃", smileGlyphs);
        Assert.Contains("😄", smileGlyphs);
        Assert.Contains("😁", smileGlyphs);
        Assert.Contains("😊", smileGlyphs);

        var heartResults = await service.SearchAsync("heart");
        var heartGlyphs = heartResults.Select(x => x.Emoji).ToList();
        Assert.Contains("❤️", heartGlyphs);
        Assert.Contains("💙", heartGlyphs);
        Assert.Contains("💚", heartGlyphs);
        Assert.Contains("💛", heartGlyphs);
        Assert.Contains("💜", heartGlyphs);

        var foodResults = await service.SearchAsync("food");
        var foodGlyphs = foodResults.Select(x => x.Emoji).ToList();
        Assert.Contains("🍎", foodGlyphs);
        Assert.Contains("🍕", foodGlyphs);
        Assert.Contains("🍔", foodGlyphs);
        Assert.Contains("🍟", foodGlyphs);

        var fireResults = await service.SearchAsync("fire");
        var fireGlyphs = fireResults.Select(x => x.Emoji).ToList();
        Assert.Contains("🔥", fireGlyphs);
        Assert.Contains("❤️‍🔥", fireGlyphs);

        var banglaResults = await service.SearchAsync("বাংলাদেশ");
        Assert.Contains("🇧🇩", banglaResults.Select(x => x.Emoji));
    }

    [Fact]
    public async Task RecordUsageAsync_PersistsAcrossRestartsWithoutDuplicates_AndRanksByFrequency()
    {
        await _databaseInitializer.InitializeAsync();
        await _settingsService.LoadAsync();

        var firstSessionService = CreateEmojiService();

        // Record usage: 🔥 (3 times), ❤️ (2 times), 😀 (1 time)
        await firstSessionService.RecordUsageAsync("😀");
        await firstSessionService.RecordUsageAsync("❤️");
        await firstSessionService.RecordUsageAsync("❤️");
        await firstSessionService.RecordUsageAsync("🔥");
        await firstSessionService.RecordUsageAsync("🔥");
        await firstSessionService.RecordUsageAsync("🔥");

        // Simulate application restart with a fresh service instance over the same SQLite database
        var secondSessionService = CreateEmojiService();
        var frequent = await secondSessionService.GetFrequentlyUsedAsync();

        Assert.Equal(3, frequent.Count);
        Assert.Equal("🔥", frequent[0].Emoji);
        Assert.Equal(3, frequent[0].UsageCount);
        Assert.NotNull(frequent[0].LastUsedUtc);

        Assert.Equal("❤️", frequent[1].Emoji);
        Assert.Equal(2, frequent[1].UsageCount);

        Assert.Equal("😀", frequent[2].Emoji);
        Assert.Equal(1, frequent[2].UsageCount);
    }

    [Fact]
    public async Task ClearHistoryAsync_DeletesAllEmojiUsageRowsCleanly()
    {
        await _databaseInitializer.InitializeAsync();
        var service = CreateEmojiService();

        await service.RecordUsageAsync("🎉");
        await service.RecordUsageAsync("🚀");
        Assert.Equal(2, (await service.GetFrequentlyUsedAsync()).Count);

        await service.ClearHistoryAsync();

        var afterClear = await service.GetFrequentlyUsedAsync();
        Assert.Empty(afterClear);
    }

    [Fact]
    public async Task EmojiViewModel_SelectEmojiCommand_InjectsMultiCodepointEmojiAndUpdatesHistory()
    {
        await _databaseInitializer.InitializeAsync();
        var service = CreateEmojiService();
        var fakeInjector = new FakeTextInjector();
        var viewModel = new EmojiViewModel(service, fakeInjector);

        await viewModel.InitializeAsync();
        Assert.Equal(EmojiViewModel.DefaultFallbackCategoryName, viewModel.SelectedCategory);

        await viewModel.SearchEmojisAsync("fire");
        var heartOnFire = viewModel.DisplayedEmojis.First(x => x.Emoji == "❤️‍🔥");

        await viewModel.SelectEmojiCommand.ExecuteAsync(heartOnFire);

        Assert.Single(fakeInjector.InjectedStrings);
        Assert.Equal("❤️‍🔥", fakeInjector.InjectedStrings[0]);

        // Re-initializing ViewModel now defaults to "Frequently Used" since history is non-empty
        var restartedViewModel = new EmojiViewModel(service, fakeInjector);
        await restartedViewModel.InitializeAsync();

        Assert.Equal(EmojiViewModel.FrequentlyUsedCategoryName, restartedViewModel.SelectedCategory);
        Assert.Single(restartedViewModel.DisplayedEmojis);
        Assert.Equal("❤️‍🔥", restartedViewModel.DisplayedEmojis[0].Emoji);
        Assert.Equal(1, restartedViewModel.DisplayedEmojis[0].UsageCount);

        // Switching to "Symbols" and executing ClearHistoryCommand resets UsageCount on displayed items as well
        await restartedViewModel.SelectCategoryCommand.ExecuteAsync("Symbols");
        var symbolFire = restartedViewModel.DisplayedEmojis.First(x => x.Emoji == "❤️‍🔥");
        Assert.Equal(1, symbolFire.UsageCount);

        await restartedViewModel.ClearHistoryCommand.ExecuteAsync(null);
        Assert.Equal(0, symbolFire.UsageCount);
        Assert.Null(symbolFire.LastUsedUtc);
    }

    [Fact]
    public void EmojiRepository_CorruptedExternalCatalog_FallsBackToEmbeddedResource()
    {
        var corruptedFile = Path.Combine(_tempRootDir, "corrupted_emoji_catalog.json");
        File.WriteAllText(corruptedFile, "{ invalid json [");

        var repository = new EmojiRepository(corruptedFile);
        var allEmojis = repository.GetAll();

        Assert.NotEmpty(allEmojis);
        Assert.True(repository.TryGetByEmoji("😀", out var grinning));
        Assert.NotNull(grinning);
    }

    private EmojiUsageService CreateEmojiService()
    {
        var repository = new EmojiRepository();
        var searchService = new EmojiSearchService();
        return new EmojiUsageService(repository, searchService, _connectionFactory, _settingsService);
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
        public List<string> InjectedStrings { get; } = [];

        public bool IsTargetWindowElevated => false;

        public bool SendUnicodeText(string text)
        {
            InjectedStrings.Add(text);
            return true;
        }

        public bool SendBackspaces(int count) => true;

        public bool ReplaceText(int backspaceCount, string replacementText)
        {
            InjectedStrings.Add(replacementText);
            return true;
        }

        public bool SendEnter() => true;

        public bool SendSpace() => true;
    }
}
