using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;

namespace BengaliKeyboard.UI.Themes;

/// <summary>
/// Manages dynamic switching between Dark (<c>Colors.Dark.xaml</c>), Light (<c>Colors.Light.xaml</c>),
/// and Windows System theme (<c>AppsUseLightTheme</c> registry inspection), applies custom hex color
/// overrides, and broadcasts <see cref="ThemeChangedMessage"/> (Design.md Section 2).
/// </summary>
public sealed class ThemeManager
{
    public const string DarkThemePackUri = "pack://application:,,,/BengaliKeyboard.UI;component/Themes/Colors.Dark.xaml";
    public const string LightThemePackUri = "pack://application:,,,/BengaliKeyboard.UI;component/Themes/Colors.Light.xaml";
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    static ThemeManager()
    {
        if (!UriParser.IsKnownScheme("pack"))
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            _ = Application.Current;
        }
    }

    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly Func<bool> _systemLightThemeDetector;
    private readonly List<WeakReference<Window>> _trackedWindows = [];

    public AppTheme ActiveEffectiveTheme { get; private set; } = AppTheme.Dark;

    public ThemeManager(
        ISettingsService settingsService,
        IMessenger? messenger = null,
        Func<bool>? systemLightThemeDetector = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _systemLightThemeDetector = systemLightThemeDetector ?? IsWindowsSystemUsingLightTheme;
    }

    /// <summary>
    /// Registers a WPF <see cref="Window"/> so its <c>Resources.MergedDictionaries</c> and <c>Opacity</c>
    /// are updated automatically whenever <see cref="ApplyCurrentSettingsTheme"/> is invoked.
    /// </summary>
    public void RegisterWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_trackedWindows)
        {
            _trackedWindows.RemoveAll(wr => !wr.TryGetTarget(out var target) || ReferenceEquals(target, window));
            _trackedWindows.Add(new WeakReference<Window>(window));
        }

        ApplyThemeToDictionary(window.Resources, _settingsService.Current, out _);
        ApplyWindowAppearance(window, _settingsService.Current);
    }

    /// <summary>
    /// Applies the currently configured theme, custom hex colors, and window opacity across all registered windows
    /// and broadcasts <see cref="ThemeChangedMessage"/>.
    /// </summary>
    public AppTheme ApplyCurrentSettingsTheme()
    {
        var settings = _settingsService.Current;
        var effectiveTheme = ResolveEffectiveTheme(settings.Theme, _systemLightThemeDetector);
        ActiveEffectiveTheme = effectiveTheme;

        if (Application.Current is { Resources: not null } app)
        {
            ApplyThemeToDictionary(app.Resources, settings, out _);
        }

        List<Window> liveWindows = [];
        lock (_trackedWindows)
        {
            for (var i = _trackedWindows.Count - 1; i >= 0; i--)
            {
                if (_trackedWindows[i].TryGetTarget(out var window))
                {
                    liveWindows.Add(window);
                }
                else
                {
                    _trackedWindows.RemoveAt(i);
                }
            }
        }

        foreach (var window in liveWindows)
        {
            ApplyThemeToDictionary(window.Resources, settings, out _);
            ApplyWindowAppearance(window, settings);
        }

        var clampedOpacity = Math.Clamp(settings.Opacity, AppSettings.MinOpacity, AppSettings.MaxOpacity);
        _messenger.Send(new ThemeChangedMessage(effectiveTheme, clampedOpacity));
        return effectiveTheme;
    }

    /// <summary>
    /// Applies the specified <see cref="AppSettings"/> theme and custom color overrides directly to a target
    /// <see cref="ResourceDictionary"/>. Used by both live windows and deterministic unit tests.
    /// </summary>
    public AppTheme ApplyThemeToDictionary(
        ResourceDictionary targetResources,
        AppSettings settings,
        out ResourceDictionary appliedThemeDictionary)
    {
        ArgumentNullException.ThrowIfNull(targetResources);
        ArgumentNullException.ThrowIfNull(settings);

        var effectiveTheme = ResolveEffectiveTheme(settings.Theme, _systemLightThemeDetector);
        ActiveEffectiveTheme = effectiveTheme;

        var packUri = effectiveTheme == AppTheme.Light ? LightThemePackUri : DarkThemePackUri;
        appliedThemeDictionary = new ResourceDictionary
        {
            Source = new Uri(packUri, UriKind.Absolute)
        };

        ApplyCustomHexOverrides(appliedThemeDictionary, settings);

        var merged = targetResources.MergedDictionaries;
        var existingThemeIndex = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is not null &&
                (source.EndsWith("Themes/Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                 source.EndsWith("Themes/Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                existingThemeIndex = i;
                break;
            }
        }

        if (existingThemeIndex >= 0)
        {
            merged[existingThemeIndex] = appliedThemeDictionary;
        }
        else
        {
            merged.Insert(0, appliedThemeDictionary);
        }

        return effectiveTheme;
    }

    /// <summary>
    /// Resolves <see cref="AppTheme.Dark"/>, <see cref="AppTheme.Light"/>, or <see cref="AppTheme.System"/>
    /// into a concrete <see cref="AppTheme.Dark"/> or <see cref="AppTheme.Light"/> palette.
    /// </summary>
    public static AppTheme ResolveEffectiveTheme(
        AppTheme configuredTheme,
        Func<bool>? systemLightThemeDetector = null)
    {
        return configuredTheme switch
        {
            AppTheme.Light => AppTheme.Light,
            AppTheme.Dark => AppTheme.Dark,
            AppTheme.System => (systemLightThemeDetector ?? IsWindowsSystemUsingLightTheme)()
                ? AppTheme.Light
                : AppTheme.Dark,
            _ => AppTheme.Dark
        };
    }

    /// <summary>
    /// Inspects <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>
    /// (Design.md Section 2.2). Returns <see langword="true"/> when Windows apps use Light theme.
    /// </summary>
    public static bool IsWindowsSystemUsingLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, writable: false);
            var rawValue = key?.GetValue(AppsUseLightThemeValueName);
            if (rawValue is int intVal)
            {
                return intVal == 1;
            }
        }
        catch
        {
            // Default to Dark theme if Windows Personalize registry key is unavailable
        }

        return false;
    }

    /// <summary>
    /// Parses a <c>#RRGGBB</c> or <c>#AARRGGBB</c> hex color string safely without throwing exceptions.
    /// </summary>
    public static bool TryParseHexColor(string? hexString, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hexString))
        {
            return false;
        }

        var trimmed = hexString.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 6 &&
            uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            var r = (byte)((rgb >> 16) & 0xFF);
            var g = (byte)((rgb >> 8) & 0xFF);
            var b = (byte)(rgb & 0xFF);
            color = Color.FromRgb(r, g, b);
            return true;
        }

        if (trimmed.Length == 8 &&
            uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            var a = (byte)((argb >> 24) & 0xFF);
            var r = (byte)((argb >> 16) & 0xFF);
            var g = (byte)((argb >> 8) & 0xFF);
            var b = (byte)(argb & 0xFF);
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    private static void ApplyCustomHexOverrides(ResourceDictionary themeDict, AppSettings settings)
    {
        if (TryParseHexColor(settings.CustomAccentColorHex, out var customAccent))
        {
            themeDict["Color.Accent.Primary"] = customAccent;
            var accentBrush = new SolidColorBrush(customAccent);
            accentBrush.Freeze();
            themeDict["Brush.Accent.Primary"] = accentBrush;
        }

        if (TryParseHexColor(settings.CustomKeyBackgroundHex, out var customKeyBg))
        {
            themeDict["Color.Key.Regular.Background"] = customKeyBg;
            var keyBrush = new SolidColorBrush(customKeyBg);
            keyBrush.Freeze();
            themeDict["Brush.Key.Regular.Background"] = keyBrush;
        }
    }

    private static void ApplyWindowAppearance(Window window, AppSettings settings)
    {
        // Keep the standalone Settings dialog at full 1.0 opacity for readability;
        // apply user-configured floating opacity (0.60 - 1.00) to all overlay windows.
        if (!string.Equals(window.GetType().Name, "SettingsWindow", StringComparison.Ordinal))
        {
            window.Opacity = Math.Clamp(settings.Opacity, AppSettings.MinOpacity, AppSettings.MaxOpacity);
            window.Topmost = settings.WindowState.AlwaysOnTop;
        }
    }
}
