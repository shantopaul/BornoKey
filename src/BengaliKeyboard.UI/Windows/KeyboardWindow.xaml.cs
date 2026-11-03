using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.UI.Windows;

/// <summary>
/// Main floating Ridmik-style virtual keyboard window.
/// Applies Win32 WS_EX_NOACTIVATE and WM_MOUSEACTIVATE (MA_NOACTIVATE) so clicking on-screen keys
/// never steals focus from the user's active target application (Architecture.md Section 5.1).
/// </summary>
public partial class KeyboardWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private readonly ISettingsService _settingsService;
    private HwndSource? _hwndSource;
    private bool _allowInteractiveActivation;

    public KeyboardWindow(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;

        InitializeComponent();
        ApplySavedWindowGeometry(_settingsService.Current.WindowState);
        Opacity = _settingsService.Current.Opacity;
        Topmost = _settingsService.Current.WindowState.AlwaysOnTop;
    }

    /// <summary>
    /// Temporarily toggles whether clicking inside the window can take foreground focus
    /// (used only when typing inside Emoji or Clipboard search boxes per Architecture.md Section 5.1).
    /// </summary>
    public void SetInteractiveFocusAllowed(bool allowActivation)
    {
        _allowInteractiveActivation = allowActivation;
        if (_hwndSource is not null)
        {
            ApplyExtendedWindowStyles(_hwndSource.Handle, enableNoActivate: !allowActivation);
        }
    }

    public void ApplySavedWindowGeometry(WindowStateConfig state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.NormalizeAndClamp();

        Width = state.Width;
        Height = state.Height;
        Left = state.Left;
        Top = state.Top;
        Topmost = state.AlwaysOnTop;
    }

    public WindowStateConfig CaptureCurrentWindowGeometry()
    {
        var current = _settingsService.Current.WindowState;
        current.Width = ActualWidth > 0 ? ActualWidth : Width;
        current.Height = ActualHeight > 0 ? ActualHeight : Height;
        current.Left = Left;
        current.Top = Top;
        current.AlwaysOnTop = Topmost;
        current.NormalizeAndClamp();
        return current;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (_hwndSource is not null)
        {
            ApplyExtendedWindowStyles(_hwndSource.Handle, enableNoActivate: true);
            _hwndSource.AddHook(WndProc);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate && !_allowInteractiveActivation)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private static void ApplyExtendedWindowStyles(IntPtr hwnd, bool enableNoActivate)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var currentStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var updatedStyle = enableNoActivate
            ? currentStyle | WsExNoActivate | WsExToolWindow
            : (currentStyle & ~WsExNoActivate) | WsExToolWindow;

        _ = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updatedStyle));
    }

    private async void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            var updatedState = CaptureCurrentWindowGeometry();
            _settingsService.Current.WindowState = updatedState;
            await _settingsService.SaveAsync(_settingsService.Current).ConfigureAwait(true);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e)
    {
        var updatedState = CaptureCurrentWindowGeometry();
        _settingsService.Current.WindowState = updatedState;
        await _settingsService.SaveAsync(_settingsService.Current).ConfigureAwait(true);
        Application.Current?.Shutdown();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
