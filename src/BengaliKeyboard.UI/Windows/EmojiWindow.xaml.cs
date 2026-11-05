using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.UI.ViewModels;

namespace BengaliKeyboard.UI.Windows;

/// <summary>
/// Floating non-activating Emoji Keyboard overlay (<c>WS_EX_NOACTIVATE</c>).
/// Clicking emojis injects Unicode codepoints into the external target window without stealing focus;
/// clicking the search box temporarily permits activation so the user can type search queries (Architecture.md Section 5.1).
/// </summary>
public partial class EmojiWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private readonly EmojiViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private HwndSource? _hwndSource;
    private bool _allowInteractiveActivation;

    public EmojiViewModel ViewModel => _viewModel;

    public event EventHandler<(double Left, double Top)>? PositionChanged;

    public EmojiWindow(EmojiViewModel viewModel, ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settingsService);

        _viewModel = viewModel;
        _settingsService = settingsService;
        DataContext = _viewModel;

        InitializeComponent();

        Opacity = _settingsService.Current.Opacity;
        Topmost = _settingsService.Current.WindowState.AlwaysOnTop;
    }

    /// <summary>
    /// Aligns the Emoji panel footprint with the main floating keyboard window.
    /// </summary>
    public void SyncBoundsWithKeyboard(Window keyboardWindow)
    {
        ArgumentNullException.ThrowIfNull(keyboardWindow);

        Left = keyboardWindow.Left;
        Top = keyboardWindow.Top;
        Width = keyboardWindow.ActualWidth > 0 ? keyboardWindow.ActualWidth : keyboardWindow.Width;
        Height = keyboardWindow.ActualHeight > 0 ? keyboardWindow.ActualHeight : keyboardWindow.Height;
        Topmost = keyboardWindow.Topmost;
        Opacity = keyboardWindow.Opacity;
    }

    /// <summary>
    /// Temporarily toggles whether clicking inside the window can take foreground focus
    /// (enabled only when clicking the search box per Architecture.md Section 5.1).
    /// </summary>
    public void SetInteractiveFocusAllowed(bool allowActivation)
    {
        _allowInteractiveActivation = allowActivation;
        if (_hwndSource is not null)
        {
            ApplyExtendedWindowStyles(_hwndSource.Handle, enableNoActivate: !allowActivation);
        }
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

    private void OnSearchBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetInteractiveFocusAllowed(true);
        _ = Activate();
        _ = SearchTextBox.Focus();
    }

    private void OnSearchBoxLostFocus(object sender, RoutedEventArgs e)
    {
        SetInteractiveFocusAllowed(false);
    }

    private void OnNonSearchActionClick(object sender, RoutedEventArgs e)
    {
        if (_allowInteractiveActivation)
        {
            Keyboard.ClearFocus();
            SetInteractiveFocusAllowed(false);
        }
    }

    private void OnEmojiTileMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EmojiItem hoveredItem })
        {
            _viewModel.SetHoveredEmoji(hoveredItem);
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            DragMove();
            PositionChanged?.Invoke(this, (Left, Top));
        }
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
