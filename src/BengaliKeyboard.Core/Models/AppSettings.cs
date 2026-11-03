using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Root application settings persisted to %LOCALAPPDATA%\BengaliKeyboard\Config\settings.json.
/// Covers all 8 settings sections defined in PRD.md and Design.md.
/// </summary>
public sealed class AppSettings
{
    public const double MinOpacity = 0.60;
    public const double MaxOpacity = 1.00;
    public const double DefaultOpacity = 0.98;

    // 1. General
    public bool StartWithWindows { get; set; }

    public bool LaunchMinimized { get; set; }

    public LanguageMode ActiveLanguage { get; set; } = LanguageMode.Bangla;

    public string ActiveBanglaLayoutId { get; set; } = "Bangla.Primary";

    public string ActiveEnglishLayoutId { get; set; } = "English.Qwerty";

    // 2. Keyboard & Window State
    public WindowStateConfig WindowState { get; set; } = new();

    public double KeySpacing { get; set; } = 4.0;

    public int KeyRepeatDelayMs { get; set; } = 400;

    public int KeyRepeatIntervalMs { get; set; } = 45;

    // 3. Appearance
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public double Opacity { get; set; } = DefaultOpacity;

    public double KeyCornerRadius { get; set; } = 6.0;

    public string? CustomAccentColorHex { get; set; }

    public string? CustomKeyBackgroundHex { get; set; }

    // 4. Prediction
    public bool EnableWordCompletion { get; set; } = true;

    public bool EnableNextWordPrediction { get; set; } = true;

    public int SuggestionCount { get; set; } = 4;

    // 5. Emoji
    public int EmojiHistoryLimit { get; set; } = 40;

    // 6. Clipboard
    public bool EnableClipboardHistory { get; set; } = true;

    public int ClipboardHistoryLimit { get; set; } = 100;

    public int ClipboardRetentionDays { get; set; } = 30;

    // 7. Shortcuts
    public string ToggleLanguageHotkey { get; set; } = "Ctrl+Space";

    public string ToggleKeyboardVisibilityHotkey { get; set; } = "Ctrl+Shift+K";

    public string OpenEmojiPanelHotkey { get; set; } = "Ctrl+Shift+E";

    public string OpenClipboardPanelHotkey { get; set; } = "Ctrl+Shift+V";

    /// <summary>
    /// Validates and clamps all configuration properties to safe operational ranges (AS-03 / CHK-06).
    /// </summary>
    public void NormalizeAndClamp()
    {
        if (!Enum.IsDefined(ActiveLanguage))
        {
            ActiveLanguage = LanguageMode.Bangla;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.Dark;
        }

        if (string.IsNullOrWhiteSpace(ActiveBanglaLayoutId))
        {
            ActiveBanglaLayoutId = "Bangla.Primary";
        }

        if (string.IsNullOrWhiteSpace(ActiveEnglishLayoutId))
        {
            ActiveEnglishLayoutId = "English.Qwerty";
        }

        WindowState ??= new WindowStateConfig();
        WindowState.NormalizeAndClamp();

        Opacity = ClampFinite(Opacity, DefaultOpacity, MinOpacity, MaxOpacity);
        KeyCornerRadius = ClampFinite(KeyCornerRadius, 6.0, 2.0, 14.0);
        KeySpacing = ClampFinite(KeySpacing, 4.0, 2.0, 8.0);
        KeyRepeatDelayMs = Math.Clamp(KeyRepeatDelayMs, 150, 1000);
        KeyRepeatIntervalMs = Math.Clamp(KeyRepeatIntervalMs, 15, 250);
        SuggestionCount = Math.Clamp(SuggestionCount, 3, 5);
        EmojiHistoryLimit = Math.Clamp(EmojiHistoryLimit, 10, 200);
        ClipboardHistoryLimit = Math.Clamp(ClipboardHistoryLimit, 10, 500);
        ClipboardRetentionDays = Math.Clamp(ClipboardRetentionDays, 1, 365);

        if (string.IsNullOrWhiteSpace(ToggleLanguageHotkey))
        {
            ToggleLanguageHotkey = "Ctrl+Space";
        }

        if (string.IsNullOrWhiteSpace(ToggleKeyboardVisibilityHotkey))
        {
            ToggleKeyboardVisibilityHotkey = "Ctrl+Shift+K";
        }

        if (string.IsNullOrWhiteSpace(OpenEmojiPanelHotkey))
        {
            OpenEmojiPanelHotkey = "Ctrl+Shift+E";
        }

        if (string.IsNullOrWhiteSpace(OpenClipboardPanelHotkey))
        {
            OpenClipboardPanelHotkey = "Ctrl+Shift+V";
        }
    }

    private static double ClampFinite(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
