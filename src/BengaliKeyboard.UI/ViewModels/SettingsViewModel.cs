using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.UI.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BengaliKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel driving the 8-section Settings &amp; Customization Window (PRD.md Section 3.9, Design.md Section 5.7):
/// General, Keyboard, Appearance, Prediction, Emoji, Clipboard, Shortcuts, and Privacy.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string SectionGeneral = "General";
    public const string SectionKeyboard = "Keyboard";
    public const string SectionAppearance = "Appearance";
    public const string SectionPrediction = "Prediction";
    public const string SectionEmoji = "Emoji";
    public const string SectionClipboard = "Clipboard";
    public const string SectionShortcuts = "Shortcuts";
    public const string SectionPrivacy = "Privacy";

    public static IReadOnlyList<string> OrderedSections { get; } =
    [
        SectionGeneral,
        SectionKeyboard,
        SectionAppearance,
        SectionPrediction,
        SectionEmoji,
        SectionClipboard,
        SectionShortcuts,
        SectionPrivacy
    ];

    public static IReadOnlyList<LanguageMode> AvailableLanguages { get; } =
    [
        LanguageMode.Bangla,
        LanguageMode.English
    ];

    public static IReadOnlyList<KeyboardMode> AvailableWindowModes { get; } =
    [
        KeyboardMode.Expanded,
        KeyboardMode.Compact,
        KeyboardMode.MinimalIcon,
        KeyboardMode.HiddenToTray
    ];

    public static IReadOnlyList<AppTheme> AvailableThemes { get; } =
    [
        AppTheme.Dark,
        AppTheme.Light,
        AppTheme.System
    ];

    public static IReadOnlyList<int> AvailableSuggestionCounts { get; } = [3, 4, 5];

    private readonly ISettingsService _settingsService;
    private readonly ThemeManager _themeManager;
    private readonly IStartupService? _startupService;
    private readonly ILayoutManager? _layoutManager;
    private readonly IBengaliInputEngine? _inputEngine;
    private readonly IWindowManagerService? _windowManagerService;
    private readonly IPredictionService? _predictionService;
    private readonly IEmojiService? _emojiService;
    private readonly IClipboardService? _clipboardService;
    private readonly Func<CancellationToken, Task>? _clearAllUserDataCallback;
    private bool _isLoadingFromModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsKeyboardSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsPredictionSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmojiSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsClipboardSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsShortcutsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsPrivacySectionVisible))]
    private string _selectedSection = SectionGeneral;

    public bool IsGeneralSectionVisible => string.Equals(SelectedSection, SectionGeneral, StringComparison.Ordinal);
    public bool IsKeyboardSectionVisible => string.Equals(SelectedSection, SectionKeyboard, StringComparison.Ordinal);
    public bool IsAppearanceSectionVisible => string.Equals(SelectedSection, SectionAppearance, StringComparison.Ordinal);
    public bool IsPredictionSectionVisible => string.Equals(SelectedSection, SectionPrediction, StringComparison.Ordinal);
    public bool IsEmojiSectionVisible => string.Equals(SelectedSection, SectionEmoji, StringComparison.Ordinal);
    public bool IsClipboardSectionVisible => string.Equals(SelectedSection, SectionClipboard, StringComparison.Ordinal);
    public bool IsShortcutsSectionVisible => string.Equals(SelectedSection, SectionShortcuts, StringComparison.Ordinal);
    public bool IsPrivacySectionVisible => string.Equals(SelectedSection, SectionPrivacy, StringComparison.Ordinal);

    // 1. General
    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _launchMinimized;

    [ObservableProperty]
    private LanguageMode _activeLanguage = LanguageMode.Bangla;

    [ObservableProperty]
    private string _activeBanglaLayoutId = "Bangla.Primary";

    [ObservableProperty]
    private string _activeEnglishLayoutId = "English.Qwerty";

    // 2. Keyboard
    [ObservableProperty]
    private KeyboardMode _defaultWindowMode = KeyboardMode.Expanded;

    [ObservableProperty]
    private double _keyboardWidth = WindowStateConfig.DefaultExpandedWidth;

    [ObservableProperty]
    private double _keyboardHeight = WindowStateConfig.DefaultExpandedHeight;

    [ObservableProperty]
    private bool _alwaysOnTop = true;

    [ObservableProperty]
    private bool _physicalKeyboardHookEnabled = true;

    [ObservableProperty]
    private double _keySpacing = 4.0;

    [ObservableProperty]
    private int _keyRepeatDelayMs = 400;

    [ObservableProperty]
    private int _keyRepeatIntervalMs = 45;

    // 3. Appearance
    [ObservableProperty]
    private AppTheme _selectedTheme = AppTheme.Dark;

    [ObservableProperty]
    private double _opacity = AppSettings.DefaultOpacity;

    [ObservableProperty]
    private double _keyCornerRadius = 6.0;

    [ObservableProperty]
    private string _customAccentColorHex = string.Empty;

    [ObservableProperty]
    private string _customKeyBackgroundHex = string.Empty;

    // 4. Prediction
    [ObservableProperty]
    private bool _enableWordCompletion = true;

    [ObservableProperty]
    private bool _enableNextWordPrediction = true;

    [ObservableProperty]
    private int _suggestionCount = 4;

    // 5. Emoji
    [ObservableProperty]
    private int _emojiHistoryLimit = 40;

    // 6. Clipboard
    [ObservableProperty]
    private bool _enableClipboardHistory = true;

    [ObservableProperty]
    private int _clipboardHistoryLimit = 100;

    [ObservableProperty]
    private int _clipboardRetentionDays = 30;

    // 7. Shortcuts
    [ObservableProperty]
    private string _toggleLanguageHotkey = "Ctrl+Space";

    [ObservableProperty]
    private string _toggleKeyboardVisibilityHotkey = "Ctrl+Shift+K";

    [ObservableProperty]
    private string _openEmojiPanelHotkey = "Ctrl+Shift+E";

    [ObservableProperty]
    private string _openClipboardPanelHotkey = "Ctrl+Shift+V";

    // 8. Privacy & Status
    public string LocalDataDirectoryPath { get; }

    [ObservableProperty]
    private bool _isResetConfirmationPending;

    [ObservableProperty]
    private string _statusMessage = "All settings saved locally (100% offline).";

    public event EventHandler? HotkeysReloadRequested;

    public event EventHandler<bool>? PhysicalKeyboardHookToggled;

    public SettingsViewModel(
        ISettingsService settingsService,
        ThemeManager themeManager,
        IStartupService? startupService = null,
        ILayoutManager? layoutManager = null,
        IBengaliInputEngine? inputEngine = null,
        IWindowManagerService? windowManagerService = null,
        IPredictionService? predictionService = null,
        IEmojiService? emojiService = null,
        IClipboardService? clipboardService = null,
        string? localDataDirectoryPath = null,
        Func<CancellationToken, Task>? clearAllUserDataCallback = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(themeManager);

        _settingsService = settingsService;
        _themeManager = themeManager;
        _startupService = startupService;
        _layoutManager = layoutManager;
        _inputEngine = inputEngine;
        _windowManagerService = windowManagerService;
        _predictionService = predictionService;
        _emojiService = emojiService;
        _clipboardService = clipboardService;
        _clearAllUserDataCallback = clearAllUserDataCallback;

        LocalDataDirectoryPath = string.IsNullOrWhiteSpace(localDataDirectoryPath)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BengaliKeyboard")
            : localDataDirectoryPath;

        ReloadFromCurrentSettings();
    }

    /// <summary>
    /// Synchronizes all ViewModel properties from <see cref="ISettingsService.Current"/> without triggering redundant writes.
    /// </summary>
    public void ReloadFromCurrentSettings()
    {
        _isLoadingFromModel = true;
        try
        {
            var s = _settingsService.Current;
            s.NormalizeAndClamp();

            StartWithWindows = _startupService?.IsStartupEnabled() ?? s.StartWithWindows;
            LaunchMinimized = s.LaunchMinimized;
            ActiveLanguage = s.ActiveLanguage;
            ActiveBanglaLayoutId = s.ActiveBanglaLayoutId;
            ActiveEnglishLayoutId = s.ActiveEnglishLayoutId;

            DefaultWindowMode = s.WindowState.Mode;
            KeyboardWidth = s.WindowState.Width;
            KeyboardHeight = s.WindowState.Height;
            AlwaysOnTop = s.WindowState.AlwaysOnTop;
            PhysicalKeyboardHookEnabled = s.PhysicalKeyboardHookEnabled;
            KeySpacing = s.KeySpacing;
            KeyRepeatDelayMs = s.KeyRepeatDelayMs;
            KeyRepeatIntervalMs = s.KeyRepeatIntervalMs;

            SelectedTheme = s.Theme;
            Opacity = s.Opacity;
            KeyCornerRadius = s.KeyCornerRadius;
            CustomAccentColorHex = s.CustomAccentColorHex ?? string.Empty;
            CustomKeyBackgroundHex = s.CustomKeyBackgroundHex ?? string.Empty;

            EnableWordCompletion = s.EnableWordCompletion;
            EnableNextWordPrediction = s.EnableNextWordPrediction;
            SuggestionCount = s.SuggestionCount;

            EmojiHistoryLimit = s.EmojiHistoryLimit;

            EnableClipboardHistory = s.EnableClipboardHistory;
            ClipboardHistoryLimit = s.ClipboardHistoryLimit;
            ClipboardRetentionDays = s.ClipboardRetentionDays;

            ToggleLanguageHotkey = s.ToggleLanguageHotkey;
            ToggleKeyboardVisibilityHotkey = s.ToggleKeyboardVisibilityHotkey;
            OpenEmojiPanelHotkey = s.OpenEmojiPanelHotkey;
            OpenClipboardPanelHotkey = s.OpenClipboardPanelHotkey;
        }
        finally
        {
            _isLoadingFromModel = false;
        }
    }

    [RelayCommand]
    public void SelectSection(string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName) || !OrderedSections.Contains(sectionName))
        {
            return;
        }

        SelectedSection = sectionName;
    }

    [RelayCommand]
    public async Task SaveAndApplyAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoadingFromModel)
        {
            return;
        }

        var s = _settingsService.Current;

        // 1. General
        if (_startupService is not null && s.StartWithWindows != StartWithWindows)
        {
            _ = _startupService.SetStartupEnabled(StartWithWindows);
        }
        s.StartWithWindows = StartWithWindows;
        s.LaunchMinimized = LaunchMinimized;

        if (s.ActiveLanguage != ActiveLanguage)
        {
            s.ActiveLanguage = ActiveLanguage;
            _layoutManager?.SetLanguage(ActiveLanguage);
            _inputEngine?.SetLanguage(ActiveLanguage);
        }
        s.ActiveBanglaLayoutId = string.IsNullOrWhiteSpace(ActiveBanglaLayoutId) ? "Bangla.Primary" : ActiveBanglaLayoutId.Trim();
        s.ActiveEnglishLayoutId = string.IsNullOrWhiteSpace(ActiveEnglishLayoutId) ? "English.Qwerty" : ActiveEnglishLayoutId.Trim();

        // 2. Keyboard
        var previousMode = s.WindowState.Mode;
        var previousWidth = s.WindowState.Width;
        var previousHeight = s.WindowState.Height;
        var previousAlwaysOnTop = s.WindowState.AlwaysOnTop;
        var previousHookEnabled = s.PhysicalKeyboardHookEnabled;
        s.WindowState.Mode = DefaultWindowMode;
        if (DefaultWindowMode is KeyboardMode.Expanded or KeyboardMode.Compact)
        {
            s.WindowState.LastVisibleMode = DefaultWindowMode;
        }
        s.WindowState.Width = KeyboardWidth;
        s.WindowState.Height = KeyboardHeight;
        s.WindowState.AlwaysOnTop = AlwaysOnTop;
        s.PhysicalKeyboardHookEnabled = PhysicalKeyboardHookEnabled;
        s.KeySpacing = KeySpacing;
        s.KeyRepeatDelayMs = KeyRepeatDelayMs;
        s.KeyRepeatIntervalMs = KeyRepeatIntervalMs;

        // 3. Appearance
        s.Theme = SelectedTheme;
        s.Opacity = Opacity;
        s.KeyCornerRadius = KeyCornerRadius;
        s.CustomAccentColorHex = string.IsNullOrWhiteSpace(CustomAccentColorHex) ? null : CustomAccentColorHex.Trim();
        s.CustomKeyBackgroundHex = string.IsNullOrWhiteSpace(CustomKeyBackgroundHex) ? null : CustomKeyBackgroundHex.Trim();

        // 4. Prediction
        s.EnableWordCompletion = EnableWordCompletion;
        s.EnableNextWordPrediction = EnableNextWordPrediction;
        s.SuggestionCount = SuggestionCount;

        // 5. Emoji
        s.EmojiHistoryLimit = EmojiHistoryLimit;

        // 6. Clipboard
        s.EnableClipboardHistory = EnableClipboardHistory;
        s.ClipboardHistoryLimit = ClipboardHistoryLimit;
        s.ClipboardRetentionDays = ClipboardRetentionDays;
        if (_clipboardService is not null)
        {
            _clipboardService.IsMonitoringEnabled = EnableClipboardHistory;
        }

        // 7. Shortcuts
        var shortcutsChanged =
            !string.Equals(s.ToggleLanguageHotkey, ToggleLanguageHotkey, StringComparison.Ordinal) ||
            !string.Equals(s.ToggleKeyboardVisibilityHotkey, ToggleKeyboardVisibilityHotkey, StringComparison.Ordinal) ||
            !string.Equals(s.OpenEmojiPanelHotkey, OpenEmojiPanelHotkey, StringComparison.Ordinal) ||
            !string.Equals(s.OpenClipboardPanelHotkey, OpenClipboardPanelHotkey, StringComparison.Ordinal);

        s.ToggleLanguageHotkey = ToggleLanguageHotkey;
        s.ToggleKeyboardVisibilityHotkey = ToggleKeyboardVisibilityHotkey;
        s.OpenEmojiPanelHotkey = OpenEmojiPanelHotkey;
        s.OpenClipboardPanelHotkey = OpenClipboardPanelHotkey;

        s.NormalizeAndClamp();
        await _settingsService.SaveAsync(s, cancellationToken).ConfigureAwait(true);

        _ = _themeManager.ApplyCurrentSettingsTheme();

        var windowGeometryOrModeChanged =
            previousMode != s.WindowState.Mode ||
            Math.Abs(previousWidth - s.WindowState.Width) > 0.5 ||
            Math.Abs(previousHeight - s.WindowState.Height) > 0.5 ||
            previousAlwaysOnTop != s.WindowState.AlwaysOnTop;

        if (windowGeometryOrModeChanged)
        {
            _windowManagerService?.ApplyWindowMode(s.WindowState.Mode);
        }

        if (previousHookEnabled != s.PhysicalKeyboardHookEnabled)
        {
            PhysicalKeyboardHookToggled?.Invoke(this, s.PhysicalKeyboardHookEnabled);
        }

        if (shortcutsChanged)
        {
            HotkeysReloadRequested?.Invoke(this, EventArgs.Empty);
        }

        ReloadFromCurrentSettings();
        StatusMessage = "Settings applied and saved to settings.json.";
    }

    [RelayCommand]
    public async Task ClearUserDictionaryAsync(CancellationToken cancellationToken = default)
    {
        if (_predictionService is not null)
        {
            await _predictionService.ClearUserDictionaryAsync(cancellationToken).ConfigureAwait(true);
        }

        StatusMessage = "Learned user dictionary words cleared.";
    }

    [RelayCommand]
    public async Task ClearEmojiHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_emojiService is not null)
        {
            await _emojiService.ClearHistoryAsync(cancellationToken).ConfigureAwait(true);
        }

        StatusMessage = "Frequently used emoji history cleared.";
    }

    [RelayCommand]
    public async Task ClearUnpinnedClipboardAsync(CancellationToken cancellationToken = default)
    {
        if (_clipboardService is not null)
        {
            await _clipboardService.ClearUnpinnedHistoryAsync(cancellationToken).ConfigureAwait(true);
        }

        StatusMessage = "Unpinned clipboard items cleared (pinned items kept).";
    }

    [RelayCommand]
    public async Task ClearClipboardHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_clipboardService is not null)
        {
            await _clipboardService.ClearAllHistoryAsync(cancellationToken).ConfigureAwait(true);
        }

        StatusMessage = "All clipboard history cleared.";
    }

    [RelayCommand]
    public async Task ClearAllApplicationDataAsync(CancellationToken cancellationToken = default)
    {
        if (_emojiService is not null)
        {
            await _emojiService.ClearHistoryAsync(cancellationToken).ConfigureAwait(true);
        }

        if (_clipboardService is not null)
        {
            await _clipboardService.ClearAllHistoryAsync(cancellationToken).ConfigureAwait(true);
        }

        if (_predictionService is not null)
        {
            await _predictionService.ClearUserDictionaryAsync(cancellationToken).ConfigureAwait(true);
        }

        if (_clearAllUserDataCallback is not null)
        {
            await _clearAllUserDataCallback(cancellationToken).ConfigureAwait(true);
        }

        StatusMessage = "All local user data (Emoji, Clipboard, User Dictionary, and HMAC keys) purged.";
    }

    [RelayCommand]
    public void RequestResetConfirmation()
    {
        IsResetConfirmationPending = true;
        StatusMessage = "Please confirm resetting all settings to factory defaults.";
    }

    [RelayCommand]
    public void CancelResetConfirmation()
    {
        IsResetConfirmationPending = false;
        StatusMessage = "Reset to defaults cancelled.";
    }

    [RelayCommand]
    public async Task ResetSettingsToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        IsResetConfirmationPending = false;
        await _settingsService.ResetToDefaultsAsync(cancellationToken).ConfigureAwait(true);

        var defaults = _settingsService.Current;
        _startupService?.SetStartupEnabled(defaults.StartWithWindows);
        _layoutManager?.SetLanguage(defaults.ActiveLanguage);
        _inputEngine?.SetLanguage(defaults.ActiveLanguage);
        if (_clipboardService is not null)
        {
            _clipboardService.IsMonitoringEnabled = defaults.EnableClipboardHistory;
        }

        ReloadFromCurrentSettings();
        _ = _themeManager.ApplyCurrentSettingsTheme();
        _windowManagerService?.ApplyWindowMode(defaults.WindowState.Mode);
        HotkeysReloadRequested?.Invoke(this, EventArgs.Empty);
        PhysicalKeyboardHookToggled?.Invoke(this, defaults.PhysicalKeyboardHookEnabled);

        StatusMessage = "All settings restored to factory defaults.";
    }
}
