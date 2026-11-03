using System.Runtime.InteropServices;
using System.Windows;

namespace BengaliKeyboard.Windows;

/// <summary>
/// Describes a connected physical display monitor, its logical working area, and its DPI scale factor.
/// </summary>
public sealed record MonitorBoundsInfo
{
    public required string DeviceName { get; init; }

    public required bool IsPrimary { get; init; }

    public required Rect MonitorBounds { get; init; }

    public required Rect WorkingArea { get; init; }

    public double DpiScale { get; init; } = 1.0;
}

/// <summary>
/// Enumerates connected displays via <c>EnumDisplayMonitors</c> and <c>GetDpiForMonitor</c>,
/// converting physical pixel bounds to WPF device-independent units (DIPs) and evaluating
/// the 60% intersection rule (Architecture.md Section 5.3).
/// </summary>
public sealed class MonitorService
{
    public const double MinimumRequiredIntersectionRatio = 0.60;

    private readonly Func<IReadOnlyList<MonitorBoundsInfo>>? _customMonitorProvider;

    public MonitorService(Func<IReadOnlyList<MonitorBoundsInfo>>? customMonitorProvider = null)
    {
        _customMonitorProvider = customMonitorProvider;
    }

    /// <summary>
    /// Returns all currently connected monitors in logical WPF device-independent units (DIPs).
    /// </summary>
    public IReadOnlyList<MonitorBoundsInfo> GetConnectedMonitors()
    {
        if (_customMonitorProvider is not null)
        {
            var custom = _customMonitorProvider();
            if (custom is { Count: > 0 })
            {
                return custom;
            }
        }

        var monitors = new List<MonitorBoundsInfo>();

        try
        {
            NativeMethods.MonitorEnumProc callback = (nint hMonitor, nint _, ref NativeMethods.NativeRect _, nint _) =>
            {
                var info = new NativeMethods.MonitorInfoEx
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                    szDevice = string.Empty
                };

                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    var dpiScale = 1.0;
                    if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
                    {
                        dpiScale = Math.Clamp(dpiX / 96.0, 0.5, 4.0);
                    }

                    var monitorRect = ConvertToLogicalRect(info.rcMonitor, dpiScale);
                    var workRect = ConvertToLogicalRect(info.rcWork, dpiScale);

                    if (workRect.Width > 0 && workRect.Height > 0)
                    {
                        monitors.Add(new MonitorBoundsInfo
                        {
                            DeviceName = string.IsNullOrWhiteSpace(info.szDevice)
                                ? $"DISPLAY_{monitors.Count + 1}"
                                : info.szDevice.Trim(),
                            IsPrimary = (info.dwFlags & NativeMethods.MonitorInfoFPrimary) != 0,
                            MonitorBounds = monitorRect,
                            WorkingArea = workRect,
                            DpiScale = dpiScale
                        });
                    }
                }

                return true;
            };

            _ = NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        }
        catch
        {
            // Fall through to SystemParameters fallback
        }

        if (monitors.Count == 0)
        {
            var fallbackWorkArea = SystemParameters.WorkArea;
            if (fallbackWorkArea.Width <= 0 || fallbackWorkArea.Height <= 0)
            {
                fallbackWorkArea = new Rect(0, 0, 1920, 1040);
            }

            monitors.Add(new MonitorBoundsInfo
            {
                DeviceName = @"\\.\DISPLAY1",
                IsPrimary = true,
                MonitorBounds = new Rect(0, 0, fallbackWorkArea.Width, fallbackWorkArea.Height + 40),
                WorkingArea = fallbackWorkArea,
                DpiScale = 1.0
            });
        }

        return monitors;
    }

    public MonitorBoundsInfo GetPrimaryMonitor()
    {
        var monitors = GetConnectedMonitors();
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Computes the fraction of <paramref name="windowRect"/>'s area (`0.0` to `1.0`) that lies inside
    /// <paramref name="workingArea"/>.
    /// </summary>
    public static double CalculateIntersectionRatio(Rect windowRect, Rect workingArea)
    {
        if (windowRect.Width <= 0 || windowRect.Height <= 0 || workingArea.Width <= 0 || workingArea.Height <= 0)
        {
            return 0.0;
        }

        var intersection = Rect.Intersect(windowRect, workingArea);
        if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0.0;
        }

        var windowArea = windowRect.Width * windowRect.Height;
        var intersectionArea = intersection.Width * intersection.Height;
        return Math.Clamp(intersectionArea / windowArea, 0.0, 1.0);
    }

    /// <summary>
    /// Finds a connected monitor whose <see cref="MonitorBoundsInfo.WorkingArea"/> overlaps at least
    /// <see cref="MinimumRequiredIntersectionRatio"/> (60%) of <paramref name="windowRect"/>.
    /// </summary>
    public MonitorBoundsInfo? FindValidMonitorForWindow(Rect windowRect, string? preferredDeviceId = null)
    {
        var monitors = GetConnectedMonitors();

        // 1. Check preferred saved monitor first if still connected
        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            var preferred = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredDeviceId, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null &&
                CalculateIntersectionRatio(windowRect, preferred.WorkingArea) >= MinimumRequiredIntersectionRatio)
            {
                return preferred;
            }
        }

        // 2. Check all connected monitors for the highest intersection >= 60%
        MonitorBoundsInfo? bestMatch = null;
        var bestRatio = 0.0;

        foreach (var monitor in monitors)
        {
            var ratio = CalculateIntersectionRatio(windowRect, monitor.WorkingArea);
            if (ratio >= MinimumRequiredIntersectionRatio && ratio > bestRatio)
            {
                bestRatio = ratio;
                bestMatch = monitor;
            }
        }

        return bestMatch;
    }

    private static Rect ConvertToLogicalRect(NativeMethods.NativeRect physical, double dpiScale)
    {
        var scale = dpiScale > 0 ? dpiScale : 1.0;
        var left = physical.Left / scale;
        var top = physical.Top / scale;
        var width = Math.Max(1.0, physical.Width / scale);
        var height = Math.Max(1.0, physical.Height / scale);
        return new Rect(left, top, width, height);
    }
}
