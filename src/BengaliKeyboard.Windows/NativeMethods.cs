using System.Runtime.InteropServices;

namespace BengaliKeyboard.Windows;

/// <summary>
/// Win32 P/Invoke declarations for <c>user32.dll</c>, <c>shcore.dll</c>, and <c>dwmapi.dll</c>
/// used by multi-monitor geometry clamping, per-monitor DPI scaling, non-activating floating windows,
/// and UIPI window message filtering.
/// </summary>
public static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTopmost = 0x00000008;

    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public const int WmNcHitTest = 0x0084;
    public const int HtClient = 1;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    public const uint MonitorDefaultToNull = 0x00000000;
    public const uint MonitorDefaultToPrimary = 0x00000001;
    public const uint MonitorDefaultToNearest = 0x00000002;
    public const uint MonitorInfoFPrimary = 0x00000001;

    public const int MdtEffectiveDpi = 0;
    public const uint MsgFltAllow = 1;
    public const uint MsgFltReset = 0;

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref NativeRect lprcMonitor, nint dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ChangeFilterStruct
    {
        public uint cbSize;
        public uint ExtStatus;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        MonitorEnumProc lpfnEnum,
        nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(
        nint hMonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChangeWindowMessageFilterEx(
        nint hWnd,
        uint message,
        uint action,
        ref ChangeFilterStruct pChangeFilterStruct);
}
