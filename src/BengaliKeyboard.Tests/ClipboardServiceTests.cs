using System.IO;
using System.Windows;
using BengaliKeyboard.Clipboard;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.UI.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class ClipboardServiceTests : IDisposable
{
    private readonly string _tempRootDir;
    private readonly AppPaths _appPaths;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly JsonSettingsService _settingsService;
    private readonly List<ClipboardHistoryService> _createdServices = [];

    public ClipboardServiceTests()
    {
        _tempRootDir = Path.Combine(
            Path.GetTempPath(),
            "BornoKey_Phase7Tests_" + Guid.NewGuid().ToString("N"));

        _appPaths = new AppPaths(_tempRootDir);
        _appPaths.EnsureDirectoriesCreated();

        _connectionFactory = new SqliteConnectionFactory(_appPaths);
        _databaseInitializer = new DatabaseInitializer(_appPaths, _connectionFactory);
        _settingsService = new JsonSettingsService(_appPaths);
    }

    [Fact]
    public async Task CaptureTextAsync_EncryptsContentWithDpapiAtRest_AndDeduplicatesByKeyedHmac()
    {
        await _databaseInitializer.InitializeAsync();
        await _settingsService.LoadAsync();

        var service = CreateClipboardService();

        var first = await service.CaptureTextAsync("আমার সোনার বাংলা");
        var second = await service.CaptureTextAsync("Hello BornoKey Desktop");
        var duplicateFirst = await service.CaptureTextAsync("আমার সোনার বাংলা");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(duplicateFirst);
        Assert.Equal(first.Id, duplicateFirst.Id);

        var history = await service.GetHistoryAsync();
        Assert.Equal(2, history.Count);
        // Most recently copied ("আমার সোনার বাংলা") is first
        Assert.Equal("আমার সোনার বাংলা", history[0].Content);
        Assert.Equal("Hello BornoKey Desktop", history[1].Content);

        // Verify raw SQLite column is DPAPI ciphertext, never cleartext (CHK-01 / SEC-02)
        await using var connection = await _connectionFactory.CreateUserDatabaseConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Content FROM ClipboardHistory;";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var rawStored = reader.GetString(0);
            Assert.DoesNotContain("আমার সোনার বাংলা", rawStored, StringComparison.Ordinal);
            Assert.DoesNotContain("Hello BornoKey Desktop", rawStored, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PinUnpin_Search_ClearUnpinned_And_Delete_PersistsAcrossRestarts()
    {
        await _databaseInitializer.InitializeAsync();
        await _settingsService.LoadAsync();

        var firstSession = CreateClipboardService();

        var clip1 = await firstSession.CaptureTextAsync("প্রথম ক্লিপ: বাংলা কীবোর্ড");
        var clip2 = await firstSession.CaptureTextAsync("দ্বিতীয় ক্লিপ: গুরুত্বপূর্ণ তথ্য");
        var clip3 = await firstSession.CaptureTextAsync("Third clip: temporary note");

        Assert.NotNull(clip1);
        Assert.NotNull(clip2);
        Assert.NotNull(clip3);

        // Pin clip1 so it stays at the top even though clip3 was copied later
        await firstSession.SetPinnedAsync(clip1.Id, isPinned: true);

        // Simulate restart with a new service instance over the same SQLite database
        var secondSession = CreateClipboardService();
        var allEntries = await secondSession.GetHistoryAsync();

        Assert.Equal(3, allEntries.Count);
        Assert.True(allEntries[0].IsPinned);
        Assert.Equal("প্রথম ক্লিপ: বাংলা কীবোর্ড", allEntries[0].Content);

        // Real-time search in Bengali
        var searchResults = await secondSession.GetHistoryAsync("গুরুত্বপূর্ণ");
        Assert.Single(searchResults);
        Assert.Equal("দ্বিতীয় ক্লিপ: গুরুত্বপূর্ণ তথ্য", searchResults[0].Content);

        // Clear unpinned history -> only pinned clip1 remains
        await secondSession.ClearUnpinnedHistoryAsync();
        var afterClearUnpinned = await secondSession.GetHistoryAsync();
        Assert.Single(afterClearUnpinned);
        Assert.Equal(clip1.Id, afterClearUnpinned[0].Id);
        Assert.True(afterClearUnpinned[0].IsPinned);

        // Delete individual entry
        await secondSession.DeleteEntryAsync(clip1.Id);
        Assert.Empty(await secondSession.GetHistoryAsync());
    }

    [Fact]
    public void ShouldIgnoreClipboardDataObject_HonorsAllFourPasswordManagerExclusionFormats()
    {
        var normalData = new FakeDataObject(new Dictionary<string, object?>
        {
            [DataFormats.UnicodeText] = "Normal copied text"
        });
        Assert.False(ClipboardMonitor.ShouldIgnoreClipboardDataObject(normalData));

        var excludeMonitorData = new FakeDataObject(new Dictionary<string, object?>
        {
            [DataFormats.UnicodeText] = "SecretPassword123",
            [ClipboardMonitor.FormatExcludeFromMonitorProcessing] = new byte[] { 1 }
        });
        Assert.True(ClipboardMonitor.ShouldIgnoreClipboardDataObject(excludeMonitorData));

        var viewerIgnoreData = new FakeDataObject(new Dictionary<string, object?>
        {
            [DataFormats.UnicodeText] = "KeePassSecret",
            [ClipboardMonitor.FormatClipboardViewerIgnore] = true
        });
        Assert.True(ClipboardMonitor.ShouldIgnoreClipboardDataObject(viewerIgnoreData));

        var historyZeroData = new FakeDataObject(new Dictionary<string, object?>
        {
            [DataFormats.UnicodeText] = "BitwardenSecret",
            [ClipboardMonitor.FormatCanIncludeInClipboardHistory] = BitConverter.GetBytes(0)
        });
        Assert.True(ClipboardMonitor.ShouldIgnoreClipboardDataObject(historyZeroData));

        var cloudZeroData = new FakeDataObject(new Dictionary<string, object?>
        {
            [DataFormats.UnicodeText] = "1PasswordSecret",
            [ClipboardMonitor.FormatCanUploadToCloudClipboard] = 0
        });
        Assert.True(ClipboardMonitor.ShouldIgnoreClipboardDataObject(cloudZeroData));
    }

    [Fact]
    public async Task ClipboardViewModel_InsertAndPauseMonitoring_WorksEndToEnd()
    {
        await _databaseInitializer.InitializeAsync();
        await _settingsService.LoadAsync();

        var service = CreateClipboardService();
        var fakeInjector = new FakeTextInjector();
        using var viewModel = new ClipboardViewModel(service, _settingsService, fakeInjector);

        await service.CaptureTextAsync("আমি বাংলায় গান গাই");
        await viewModel.InitializeAsync();

        Assert.Single(viewModel.Entries);
        viewModel.InsertEntryCommand.Execute(viewModel.Entries[0]);

        Assert.Single(fakeInjector.InjectedTexts);
        Assert.Equal("আমি বাংলায় গান গাই", fakeInjector.InjectedTexts[0]);

        // Pause monitoring via ViewModel command
        await viewModel.ToggleMonitoringCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsMonitoringEnabled);
        Assert.False(service.IsMonitoringEnabled);

        // Capturing while paused is ignored
        var ignored = await service.CaptureTextAsync("Should not be captured while paused");
        Assert.Null(ignored);
        Assert.Single(await service.GetHistoryAsync());
    }

    private ClipboardHistoryService CreateClipboardService()
    {
        var repository = new ClipboardRepository(_connectionFactory, _databaseInitializer);
        var monitor = new ClipboardMonitor();
        var service = new ClipboardHistoryService(repository, monitor, _settingsService);
        _createdServices.Add(service);
        return service;
    }

    public void Dispose()
    {
        foreach (var service in _createdServices)
        {
            service.Dispose();
        }

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
        public List<string> InjectedTexts { get; } = [];

        public bool IsTargetWindowElevated => false;

        public bool SendUnicodeText(string text)
        {
            InjectedTexts.Add(text);
            return true;
        }

        public bool SendBackspaces(int count) => true;

        public bool ReplaceText(int backspaceCount, string replacementText)
        {
            InjectedTexts.Add(replacementText);
            return true;
        }

        public bool SendEnter() => true;

        public bool SendSpace() => true;
    }

    private sealed class FakeDataObject : IDataObject
    {
        private readonly IReadOnlyDictionary<string, object?> _formats;

        public FakeDataObject(IReadOnlyDictionary<string, object?> formats)
        {
            _formats = formats;
        }

        public object? GetData(string format) =>
            _formats.TryGetValue(format, out var val) ? val : null;

        public object? GetData(string format, bool autoConvert) => GetData(format);

        public object? GetData(Type format) => GetData(format.FullName ?? string.Empty);

        public bool GetDataPresent(string format) => _formats.ContainsKey(format);

        public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);

        public bool GetDataPresent(Type format) => GetDataPresent(format.FullName ?? string.Empty);

        public string[] GetFormats() => _formats.Keys.ToArray();

        public string[] GetFormats(bool autoConvert) => GetFormats();

        public void SetData(object data) { }

        public void SetData(string format, object data) { }

        public void SetData(string format, object data, bool autoConvert) { }

        public void SetData(Type format, object data) { }
    }
}
