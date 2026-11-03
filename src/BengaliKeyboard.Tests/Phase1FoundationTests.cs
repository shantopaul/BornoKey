using System.IO;
using BengaliKeyboard.App;
using BengaliKeyboard.App.Logging;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.Storage.Security;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class Phase1FoundationTests : IDisposable
{
    private readonly string _tempTestRoot;

    public Phase1FoundationTests()
    {
        _tempTestRoot = Path.Combine(
            Path.GetTempPath(),
            "BornoKey_Tests_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void CoreModels_ToString_RedactsSensitiveTextAndValidatesBounds()
    {
        var secretClip = new ClipboardEntry
        {
            Id = 42,
            Content = "SuperSecretPassword_বাংলা_12345",
            ContentHash = "ABCDEF0123456789",
            IsPinned = true
        };

        var clipString = secretClip.ToString();
        Assert.DoesNotContain("SuperSecretPassword", clipString, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDEF0123456789", clipString, StringComparison.Ordinal);
        Assert.Contains("Id=42", clipString, StringComparison.Ordinal);

        var candidate = new SuggestionCandidate
        {
            Word = "আমার",
            Score = 0.95,
            IsNextWordPrediction = false,
            ReplacePrefixLength = 3
        };
        Assert.DoesNotContain("আমার", candidate.ToString(), StringComparison.Ordinal);

        var validKey = new KeyDefinition
        {
            Id = "bn_k",
            DisplayLabel = "ক",
            UnicodeOutput = "ক",
            AlternateOutput = "খ",
            WidthRatio = 1.0,
            RowIndex = 1,
            ColumnIndex = 5
        };
        Assert.True(validKey.IsValid());
        Assert.DoesNotContain("ক", validKey.ToString(), StringComparison.Ordinal);

        var invalidKey = new KeyDefinition
        {
            Id = "oversized",
            UnicodeOutput = "১২৩৪৫৬৭৮৯০ অতিরিক্ত",
            WidthRatio = 1.0
        };
        Assert.False(invalidKey.IsValid());
    }

    [Fact]
    public void AppSettings_NormalizeAndClamp_EnforcesSecurityAndGeometryBounds()
    {
        var settings = new AppSettings
        {
            Opacity = 0.0, // Attempt invisible window
            KeyCornerRadius = 99.0,
            KeySpacing = -5.0,
            SuggestionCount = 50,
            EmojiHistoryLimit = 2,
            ClipboardHistoryLimit = 99999,
            ClipboardRetentionDays = 0,
            WindowState = new WindowStateConfig
            {
                Width = -100,
                Height = double.NaN,
                CompactWidth = 5000,
                CompactHeight = 10,
                Left = double.PositiveInfinity,
                Top = -99999,
                LastVisibleMode = KeyboardMode.HiddenToTray
            }
        };

        settings.NormalizeAndClamp();

        Assert.Equal(AppSettings.MinOpacity, settings.Opacity);
        Assert.Equal(14.0, settings.KeyCornerRadius);
        Assert.Equal(2.0, settings.KeySpacing);
        Assert.Equal(5, settings.SuggestionCount);
        Assert.Equal(10, settings.EmojiHistoryLimit);
        Assert.Equal(500, settings.ClipboardHistoryLimit);
        Assert.Equal(1, settings.ClipboardRetentionDays);

        Assert.Equal(WindowStateConfig.MinExpandedWidth, settings.WindowState.Width);
        Assert.Equal(WindowStateConfig.DefaultExpandedHeight, settings.WindowState.Height);
        Assert.Equal(WindowStateConfig.MaxCompactWidth, settings.WindowState.CompactWidth);
        Assert.Equal(WindowStateConfig.MinCompactHeight, settings.WindowState.CompactHeight);
        Assert.Equal(400.0, settings.WindowState.Left);
        Assert.Equal(650.0, settings.WindowState.Top);
        Assert.Equal(KeyboardMode.Expanded, settings.WindowState.LastVisibleMode);
    }

    [Fact]
    public void LocalDataProtector_EncryptsWithDpapi_AndComputesKeyedHmac()
    {
        const string bengaliSecret = "আমি বাংলা লিখতে পারি — SecretApiKey_98765!";

        var protectedBase64 = LocalDataProtector.ProtectString(bengaliSecret);
        Assert.NotEqual(bengaliSecret, protectedBase64);

        var decrypted = LocalDataProtector.UnprotectString(protectedBase64);
        Assert.Equal(bengaliSecret, decrypted);

        var key1 = LocalDataProtector.GenerateProtectedHmacKey();
        var key2 = LocalDataProtector.GenerateProtectedHmacKey();

        var hash1A = LocalDataProtector.ComputeKeyedHash(bengaliSecret, key1);
        var hash1B = LocalDataProtector.ComputeKeyedHash(bengaliSecret, key1);
        var hash2 = LocalDataProtector.ComputeKeyedHash(bengaliSecret, key2);

        Assert.Equal(64, hash1A.Length);
        Assert.Equal(hash1A, hash1B);
        Assert.NotEqual(hash1A, hash2);
    }

    [Fact]
    public async Task JsonSettingsService_SavesAtomically_AndRecoversFromCorruptedFile()
    {
        var paths = new AppPaths(_tempTestRoot);
        var service = new JsonSettingsService(paths);

        var initial = await service.LoadAsync();
        Assert.Equal(LanguageMode.Bangla, initial.ActiveLanguage);
        Assert.True(File.Exists(paths.SettingsFilePath));
        Assert.False(File.Exists($"{paths.SettingsFilePath}.tmp"));

        initial.ActiveLanguage = LanguageMode.English;
        initial.WindowState.Width = 920;
        await service.SaveAsync(initial);

        var reloadedService = new JsonSettingsService(paths);
        var reloaded = await reloadedService.LoadAsync();
        Assert.Equal(LanguageMode.English, reloaded.ActiveLanguage);
        Assert.Equal(920, reloaded.WindowState.Width);

        // Write malformed JSON to verify corruption backup & recovery (Rules.md #3)
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{ \"Opacity\": 0.0, CORRUPTED_JSON ");

        var recovered = await reloadedService.LoadAsync();
        Assert.Equal(AppSettings.DefaultOpacity, recovered.Opacity);
        Assert.True(File.Exists(Path.Combine(paths.ConfigDirectory, "settings.corrupt.bak")));
    }

    [Fact]
    public async Task DatabaseInitializer_CreatesSchemas_EnforcesReadOnlyDictionary_AndRecoversFromCorruption()
    {
        var paths = new AppPaths(_tempTestRoot);
        var factory = new SqliteConnectionFactory(paths);
        var initializer = new DatabaseInitializer(paths, factory);

        await initializer.InitializeAsync();

        Assert.True(File.Exists(paths.UserDatabasePath));
        Assert.True(File.Exists(paths.DictionaryDatabasePath));

        var hmacKey1 = await initializer.GetOrCreateClipboardHmacKeyAsync();
        var hmacKey2 = await initializer.GetOrCreateClipboardHmacKeyAsync();
        Assert.False(string.IsNullOrWhiteSpace(hmacKey1));
        Assert.Equal(hmacKey1, hmacKey2);

        // Verify ReadOnly enforcement on dictionary.db during normal runtime (CHK-1.10)
        await using (var dictReadOnlyConn = await factory.CreateDictionaryConnectionAsync(readOnly: true))
        await using (var writeAttemptCmd = dictReadOnlyConn.CreateCommand())
        {
            writeAttemptCmd.CommandText = "INSERT INTO BengaliWords (Word, Prefix2, Frequency) VALUES ('আমার', 'আম', 100);";
            await Assert.ThrowsAsync<SqliteException>(() => writeAttemptCmd.ExecuteNonQueryAsync());
        }

        SqliteConnection.ClearAllPools();

        // Corrupt keyboard.db with random non-SQLite bytes and verify automatic backup & recovery (CHK-1.20)
        await File.WriteAllBytesAsync(paths.UserDatabasePath, "NOT_A_VALID_SQLITE_HEADER_1234567890"u8.ToArray());

        await initializer.InitializeAsync();

        var backupFiles = Directory.GetFiles(paths.DatabaseDirectory, "keyboard.db.corrupt.*.bak");
        Assert.NotEmpty(backupFiles);
    }

    [Fact]
    public void PrivacyDestructuringPolicy_StripsSensitiveFieldsFromStructuredLogEvents()
    {
        var capturedEvents = new List<LogEvent>();
        var sink = new CapturingLogEventSink(capturedEvents);

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Destructure.With<PrivacyDestructuringPolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var entry = new ClipboardEntry
        {
            Id = 7,
            Content = "TopSecretClipboardToken_বাংলা",
            ContentHash = "SECRET_HASH_VALUE",
            IsPinned = true
        };

        var candidate = new SuggestionCandidate
        {
            Word = "বাংলাদেশ",
            Score = 42.5,
            IsNextWordPrediction = true,
            ReplacePrefixLength = 0
        };

        logger.Information("Processed {@Entry} and {@Candidate}", entry, candidate);

        Assert.Single(capturedEvents);
        var rendered = capturedEvents[0].RenderMessage();
        var propertiesText = string.Join(";", capturedEvents[0].Properties.Select(kvp => $"{kvp.Key}={kvp.Value}"));

        Assert.DoesNotContain("TopSecretClipboardToken", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("TopSecretClipboardToken", propertiesText, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_HASH_VALUE", propertiesText, StringComparison.Ordinal);
        Assert.DoesNotContain("বাংলাদেশ", propertiesText, StringComparison.Ordinal);
        Assert.Contains("ContentLength", propertiesText, StringComparison.Ordinal);
        Assert.Contains("WordLength", propertiesText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleInstanceManager_BlocksSecondInstance_AndDeliversWakeupSignal()
    {
        var testScope = "UnitTest_" + Guid.NewGuid().ToString("N");
        var wakeupReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var primary = new SingleInstanceManager(testScope);
        Assert.True(primary.IsFirstInstance);
        Assert.StartsWith("Local\\BornoKey_SingleInstance_", primary.MutexName, StringComparison.Ordinal);

        primary.StartWakeupListener(() => wakeupReceived.TrySetResult(true));

        using var secondary = new SingleInstanceManager(testScope);
        Assert.False(secondary.IsFirstInstance);

        var signaled = await secondary.SignalFirstInstanceAsync(timeoutMs: 2000);
        Assert.True(signaled);

        var completedTask = await Task.WhenAny(wakeupReceived.Task, Task.Delay(2000));
        Assert.Same(wakeupReceived.Task, completedTask);
        Assert.True(await wakeupReceived.Task);
    }

    [Fact]
    public async Task Bootstrapper_ConfiguresDependencyInjectionAndInitializesStorage()
    {
        using var bootstrapper = new Bootstrapper();
        var provider = bootstrapper.ConfigureServices(_tempTestRoot);

        await bootstrapper.InitializeCoreServicesAsync();

        Assert.NotNull(provider.GetRequiredService<AppPaths>());
        Assert.NotNull(provider.GetRequiredService<SqliteConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<DatabaseInitializer>());
        Assert.NotNull(provider.GetRequiredService<ISettingsService>());
        Assert.NotNull(provider.GetRequiredService<IMessenger>());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempTestRoot))
            {
                Directory.Delete(_tempTestRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup lock in temp folder
        }
    }

    private sealed class CapturingLogEventSink(List<LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
