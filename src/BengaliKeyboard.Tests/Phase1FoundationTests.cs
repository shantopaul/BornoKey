using System.IO;
using System.Windows;
using System.Windows.Media;
using BengaliKeyboard.App;
using BengaliKeyboard.App.Logging;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.Storage.Security;
using BengaliKeyboard.UI.Themes;
using BengaliKeyboard.UI.ViewModels;
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
        Assert.NotNull(provider.GetRequiredService<ThemeManager>());
        Assert.NotNull(provider.GetRequiredService<SettingsViewModel>());
    }

    [Fact]
    public void ThemeManager_ResolvesDarkLightAndSystemThemes_AndAppliesCustomHexColors()
    {
        var paths = new AppPaths(_tempTestRoot);
        var settingsService = new JsonSettingsService(paths);
        var messenger = new WeakReferenceMessenger();

        ThemeChangedMessage? lastMessage = null;
        messenger.Register<Phase1FoundationTests, ThemeChangedMessage>(this, (_, msg) => lastMessage = msg);

        Assert.Equal(AppTheme.Dark, ThemeManager.ResolveEffectiveTheme(AppTheme.Dark));
        Assert.Equal(AppTheme.Light, ThemeManager.ResolveEffectiveTheme(AppTheme.Light));
        Assert.Equal(AppTheme.Light, ThemeManager.ResolveEffectiveTheme(AppTheme.System, () => true));
        Assert.Equal(AppTheme.Dark, ThemeManager.ResolveEffectiveTheme(AppTheme.System, () => false));

        Assert.True(ThemeManager.TryParseHexColor("#3B82F6", out var blue));
        Assert.Equal(Color.FromRgb(0x3B, 0x82, 0xF6), blue);
        Assert.False(ThemeManager.TryParseHexColor("not-a-color", out _));

        var themeManager = new ThemeManager(settingsService, messenger, () => true);
        var targetDict = new ResourceDictionary();

        // 1. Default Dark Theme
        settingsService.Current.Theme = AppTheme.Dark;
        var appliedDark = themeManager.ApplyThemeToDictionary(targetDict, settingsService.Current, out var darkDict);
        Assert.Equal(AppTheme.Dark, appliedDark);
        Assert.Equal(Color.FromRgb(0x17, 0x19, 0x1F), (Color)darkDict["Color.Window.Background"]);

        // 2. Switch to Light Theme + Custom Accent Color Override
        settingsService.Current.Theme = AppTheme.Light;
        settingsService.Current.CustomAccentColorHex = "#10B981";
        settingsService.Current.Opacity = 0.85;
        var appliedLight = themeManager.ApplyThemeToDictionary(targetDict, settingsService.Current, out var lightDict);
        Assert.Equal(AppTheme.Light, appliedLight);
        Assert.Single(targetDict.MergedDictionaries);
        Assert.Equal(Color.FromRgb(0xEC, 0xEE, 0xF2), (Color)lightDict["Color.Window.Background"]);
        Assert.Equal(Color.FromRgb(0x10, 0xB9, 0x81), (Color)lightDict["Color.Accent.Primary"]);

        _ = themeManager.ApplyCurrentSettingsTheme();
        Assert.NotNull(lastMessage);
        Assert.Equal(AppTheme.Light, lastMessage!.Theme);
        Assert.Equal(0.85, lastMessage.Opacity);
    }

    [Fact]
    public async Task SettingsViewModel_All8Sections_ModifyLiveSettings_AndExecutePrivacyPurgeAndFactoryReset()
    {
        using var bootstrapper = new Bootstrapper();
        var provider = bootstrapper.ConfigureServices(_tempTestRoot);
        await bootstrapper.InitializeCoreServicesAsync();

        var settingsVm = provider.GetRequiredService<SettingsViewModel>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        var emojiService = provider.GetRequiredService<IEmojiService>();
        var clipboardService = provider.GetRequiredService<IClipboardService>();
        var predictionService = provider.GetRequiredService<IPredictionService>();
        var dbInitializer = provider.GetRequiredService<DatabaseInitializer>();

        // 1. Verify all 8 navigation sections
        Assert.Equal(8, SettingsViewModel.OrderedSections.Count);
        foreach (var section in SettingsViewModel.OrderedSections)
        {
            settingsVm.SelectSectionCommand.Execute(section);
            Assert.Equal(section, settingsVm.SelectedSection);
        }

        // 2. Modify settings across sections and SaveAndApply
        var hotkeysReloaded = false;
        settingsVm.HotkeysReloadRequested += (_, _) => hotkeysReloaded = true;

        settingsVm.LaunchMinimized = true;
        settingsVm.ActiveLanguage = LanguageMode.English;
        settingsVm.KeyboardWidth = 920;
        settingsVm.SelectedTheme = AppTheme.Light;
        settingsVm.Opacity = 0.82;
        settingsVm.KeyCornerRadius = 10.0;
        settingsVm.CustomAccentColorHex = "#2563EB";
        settingsVm.SuggestionCount = 5;
        settingsVm.EmojiHistoryLimit = 60;
        settingsVm.ClipboardHistoryLimit = 150;
        settingsVm.ToggleLanguageHotkey = "Alt+Shift+B";

        await settingsVm.SaveAndApplyCommand.ExecuteAsync(null);

        Assert.True(hotkeysReloaded);
        Assert.True(settingsService.Current.LaunchMinimized);
        Assert.Equal(LanguageMode.English, settingsService.Current.ActiveLanguage);
        Assert.Equal(920, settingsService.Current.WindowState.Width);
        Assert.Equal(AppTheme.Light, settingsService.Current.Theme);
        Assert.Equal(0.82, settingsService.Current.Opacity);
        Assert.Equal(10.0, settingsService.Current.KeyCornerRadius);
        Assert.Equal("#2563EB", settingsService.Current.CustomAccentColorHex);
        Assert.Equal(5, settingsService.Current.SuggestionCount);
        Assert.Equal(60, settingsService.Current.EmojiHistoryLimit);
        Assert.Equal(150, settingsService.Current.ClipboardHistoryLimit);
        Assert.Equal("Alt+Shift+B", settingsService.Current.ToggleLanguageHotkey);

        // 3. Seed Emoji, Clipboard, and UserDictionary data, then execute ClearAllApplicationDataCommand
        var initialHmacKey = await dbInitializer.GetOrCreateClipboardHmacKeyAsync();
        await emojiService.RecordUsageAsync("😀");
        _ = await clipboardService.CaptureTextAsync("গোপন ক্লিপবোর্ড টেক্সট");
        await predictionService.RecordAcceptedWordAsync("স্বাধীনতা", "আমার");

        Assert.NotEmpty(await emojiService.GetFrequentlyUsedAsync());
        Assert.NotEmpty(await clipboardService.GetHistoryAsync());

        await settingsVm.ClearAllApplicationDataCommand.ExecuteAsync(null);

        Assert.Empty(await emojiService.GetFrequentlyUsedAsync());
        Assert.Empty(await clipboardService.GetHistoryAsync());
        var rotatedHmacKey = await dbInitializer.GetOrCreateClipboardHmacKeyAsync();
        Assert.NotEqual(initialHmacKey, rotatedHmacKey);

        // 4. Verify Two-Step Confirmation Guard & Factory Reset
        settingsVm.RequestResetConfirmationCommand.Execute(null);
        Assert.True(settingsVm.IsResetConfirmationPending);

        settingsVm.CancelResetConfirmationCommand.Execute(null);
        Assert.False(settingsVm.IsResetConfirmationPending);

        settingsVm.RequestResetConfirmationCommand.Execute(null);
        await settingsVm.ResetSettingsToDefaultsCommand.ExecuteAsync(null);

        Assert.False(settingsVm.IsResetConfirmationPending);
        Assert.Equal(LanguageMode.Bangla, settingsService.Current.ActiveLanguage);
        Assert.Equal(AppTheme.Dark, settingsService.Current.Theme);
        Assert.Equal(AppSettings.DefaultOpacity, settingsService.Current.Opacity);
        Assert.Equal("Ctrl+Space", settingsService.Current.ToggleLanguageHotkey);
    }

    [Fact]
    public async Task EndToEnd_FullTypingPredictionEmojiClipboardThemeAndWindowLifecycle_WorksSeamlessly()
    {
        using var bootstrapper = new Bootstrapper();
        var provider = bootstrapper.ConfigureServices(_tempTestRoot);
        await bootstrapper.InitializeCoreServicesAsync();

        var layoutManager = provider.GetRequiredService<ILayoutManager>();
        var keyboardVm = provider.GetRequiredService<KeyboardViewModel>();
        var suggestionVm = provider.GetRequiredService<SuggestionBarViewModel>();
        var emojiVm = provider.GetRequiredService<EmojiViewModel>();
        var clipboardVm = provider.GetRequiredService<ClipboardViewModel>();
        var windowManager = provider.GetRequiredService<IWindowManagerService>();
        var settingsVm = provider.GetRequiredService<SettingsViewModel>();

        // 1. Verify Bangla Primary layout and one-shot Shift layer return
        Assert.Equal(LanguageMode.Bangla, keyboardVm.ActiveLanguage);
        Assert.Equal(LayoutLayer.Primary, keyboardVm.ActiveLayer);

        await keyboardVm.PressKeyCommand.ExecuteAsync(new KeyDefinition
        {
            Id = "shift_key",
            KeyType = KeyType.Shift,
            TargetLayer = LayoutLayer.Shift
        });
        Assert.Equal(LayoutLayer.Shift, keyboardVm.ActiveLayer);

        // Typing a character on Shift layer automatically returns to Primary layer
        await keyboardVm.PressKeyCommand.ExecuteAsync(new KeyDefinition
        {
            Id = "bn_a",
            DisplayLabel = "আ",
            UnicodeOutput = "আ",
            KeyType = KeyType.Character
        });
        Assert.Equal(LayoutLayer.Primary, keyboardVm.ActiveLayer);

        // 2. Continue typing 'ম' + 'া' ('আমা') and verify live word completions in SuggestionBarViewModel
        await keyboardVm.PressKeyCommand.ExecuteAsync(new KeyDefinition
        {
            Id = "bn_m",
            DisplayLabel = "ম",
            UnicodeOutput = "ম",
            KeyType = KeyType.Character
        });
        await keyboardVm.PressKeyCommand.ExecuteAsync(new KeyDefinition
        {
            Id = "bn_aa_kar",
            DisplayLabel = "া",
            UnicodeOutput = "া",
            KeyType = KeyType.VowelSign
        });

        Assert.Contains(suggestionVm.Candidates, c => c.Word == "আমার");

        // Commit 'আমার' and verify next-word prediction candidates populate
        var amarCandidate = suggestionVm.Candidates.First(c => c.Word == "আমার");
        await suggestionVm.SelectCandidateCommand.ExecuteAsync(amarCandidate);
        Assert.NotEmpty(suggestionVm.Candidates);
        Assert.All(suggestionVm.Candidates, c => Assert.True(c.IsNextWordPrediction));

        // 3. Verify EmojiViewModel initialization and Bengali keyword search
        await emojiVm.InitializeAsync();
        Assert.NotEmpty(emojiVm.Categories);
        emojiVm.SearchQuery = "হাসি";
        await Task.Delay(60);
        Assert.NotEmpty(emojiVm.DisplayedEmojis);

        // 4. Verify ClipboardViewModel capture, pin, and search
        var clipService = provider.GetRequiredService<IClipboardService>();
        var entry = await clipService.CaptureTextAsync("আমার সোনার বাংলা — EndToEnd Clip");
        Assert.NotNull(entry);
        await clipboardVm.InitializeAsync();
        Assert.Contains(clipboardVm.Entries, e => e.Id == entry!.Id);

        // 5. Verify 4-Mode Window State Machine transitions
        windowManager.ApplyWindowMode(KeyboardMode.Compact);
        Assert.Equal(KeyboardMode.Compact, windowManager.CurrentMode);
        windowManager.ApplyWindowMode(KeyboardMode.MinimalIcon);
        Assert.Equal(KeyboardMode.MinimalIcon, windowManager.CurrentMode);
        windowManager.RestoreLastVisibleMode();
        Assert.Equal(KeyboardMode.Compact, windowManager.CurrentMode);

        // 6. Verify Language Toggle & Settings Persistence
        await keyboardVm.ToggleLanguageCommand.ExecuteAsync(null);
        Assert.Equal(LanguageMode.English, layoutManager.ActiveLanguage);
        settingsVm.ReloadFromCurrentSettings();
        Assert.Equal(LanguageMode.English, settingsVm.ActiveLanguage);
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
