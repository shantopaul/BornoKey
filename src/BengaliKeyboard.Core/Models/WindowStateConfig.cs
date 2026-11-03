using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Persisted window geometry, monitor affinity, and presentation mode state.
/// </summary>
public sealed class WindowStateConfig
{
    public const double DefaultExpandedWidth = 850.0;
    public const double DefaultExpandedHeight = 310.0;
    public const double MinExpandedWidth = 520.0;
    public const double MaxExpandedWidth = 1600.0;
    public const double MinExpandedHeight = 210.0;
    public const double MaxExpandedHeight = 600.0;

    public const double DefaultCompactWidth = 620.0;
    public const double DefaultCompactHeight = 230.0;
    public const double MinCompactWidth = 440.0;
    public const double MaxCompactWidth = 900.0;
    public const double MinCompactHeight = 180.0;
    public const double MaxCompactHeight = 360.0;

    public KeyboardMode Mode { get; set; } = KeyboardMode.Expanded;

    public KeyboardMode LastVisibleMode { get; set; } = KeyboardMode.Expanded;

    public double Width { get; set; } = DefaultExpandedWidth;

    public double Height { get; set; } = DefaultExpandedHeight;

    public double CompactWidth { get; set; } = DefaultCompactWidth;

    public double CompactHeight { get; set; } = DefaultCompactHeight;

    public double Left { get; set; } = 400.0;

    public double Top { get; set; } = 650.0;

    public double IconLeft { get; set; } = 1200.0;

    public double IconTop { get; set; } = 650.0;

    public string? MonitorDeviceId { get; set; }

    public double DpiScale { get; set; } = 1.0;

    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Enforces safe geometric bounds and sanitizes NaN/Infinity values (AS-03 / CHK-06).
    /// </summary>
    public void NormalizeAndClamp()
    {
        if (!Enum.IsDefined(Mode))
        {
            Mode = KeyboardMode.Expanded;
        }

        if (LastVisibleMode is not (KeyboardMode.Expanded or KeyboardMode.Compact))
        {
            LastVisibleMode = KeyboardMode.Expanded;
        }

        Width = ClampFinite(Width, DefaultExpandedWidth, MinExpandedWidth, MaxExpandedWidth);
        Height = ClampFinite(Height, DefaultExpandedHeight, MinExpandedHeight, MaxExpandedHeight);
        CompactWidth = ClampFinite(CompactWidth, DefaultCompactWidth, MinCompactWidth, MaxCompactWidth);
        CompactHeight = ClampFinite(CompactHeight, DefaultCompactHeight, MinCompactHeight, MaxCompactHeight);

        Left = SanitizeCoordinate(Left, 400.0);
        Top = SanitizeCoordinate(Top, 650.0);
        IconLeft = SanitizeCoordinate(IconLeft, 1200.0);
        IconTop = SanitizeCoordinate(IconTop, 650.0);
        DpiScale = ClampFinite(DpiScale, 1.0, 0.5, 4.0);
    }

    private static double ClampFinite(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static double SanitizeCoordinate(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < -32_000.0 || value > 32_000.0)
        {
            return fallback;
        }

        return value;
    }
}
