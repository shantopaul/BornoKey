using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using CommunityToolkit.Mvvm.Messaging;

namespace BengaliKeyboard.UI.Windows;

/// <summary>
/// Mode C — Minimal Floating Icon Mode (<c>94 × 40 px</c> draggable <c>[ • বাংলা  ✕ ]</c> pill badge).
/// Uses <c>WS_EX_NOACTIVATE</c> so clicking or dragging never steals focus from external apps.
/// Clicking the badge restores the previous Expanded or Compact keyboard; dragging repositions the badge;
/// right-clicking toggles <c>বাংলা</c> ↔ <c>ENG</c>; clicking the small <c>✕</c> button hides the badge
/// into the System Tray while BornoKey continues running in the background.
/// </summary>
public partial class FloatingIconWindow : Window, IRecipient<LanguageChangedMessage>
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const double DragThresholdPixels = 4.0;

    private static readonly SolidColorBrush CloseIdleBackground = new(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush CloseHoverBackground = new(Color.FromArgb(0xCC, 0xEF, 0x44, 0x44));

    private readonly IMessenger _messenger;
    private HwndSource? _hwndSource;
    private bool _isPointerDown;
    private bool _isDragging;
    private Point _pointerDownScreenPoint;
    private double _initialWindowLeft;
    private double _initialWindowTop;
    private LanguageMode _activeLanguage = LanguageMode.Bangla;

    public event EventHandler? RestoreRequested;

    public event EventHandler? LanguageToggleRequested;

    public event EventHandler? CloseToTrayRequested;

    public event EventHandler<(double Left, double Top)>? BadgePositionChanged;

    public LanguageMode ActiveLanguage
    {
        get => _activeLanguage;
        set
        {
            _activeLanguage = value;
            UpdateBadgeVisuals();
        }
    }

    public FloatingIconWindow(IMessenger? messenger = null)
    {
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        InitializeComponent();
        _messenger.Register(this);
        UpdateBadgeVisuals();
    }

    public void Receive(LanguageChangedMessage message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ActiveLanguage = message.Language);
            return;
        }

        ActiveLanguage = message.Language;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (_hwndSource is not null)
        {
            var currentStyle = GetWindowLongPtr(_hwndSource.Handle, GwlExStyle).ToInt64();
            var updatedStyle = currentStyle | WsExNoActivate | WsExToolWindow;
            _ = SetWindowLongPtr(_hwndSource.Handle, GwlExStyle, new IntPtr(updatedStyle));
            _hwndSource.AddHook(WndProc);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _messenger.UnregisterAll(this);
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        base.OnClosed(e);
    }

    private void UpdateBadgeVisuals()
    {
        if (_activeLanguage == LanguageMode.Bangla)
        {
            BadgeLabelText.Text = "বাংলা";
            if (TryFindResource("Brush.Status.Success") is Brush greenBrush)
            {
                StatusDot.Fill = greenBrush;
            }
        }
        else
        {
            BadgeLabelText.Text = "ENG";
            if (TryFindResource("Brush.Accent.Primary") is Brush blueBrush)
            {
                StatusDot.Fill = blueBrush;
            }
        }
    }

    private void OnCloseButtonMouseEnter(object sender, MouseEventArgs e)
    {
        CloseBadgeButton.Background = CloseHoverBackground;
        CloseBadgeIcon.Foreground = Brushes.White;
    }

    private void OnCloseButtonMouseLeave(object sender, MouseEventArgs e)
    {
        CloseBadgeButton.Background = CloseIdleBackground;
        if (TryFindResource("Brush.Text.Secondary") is Brush secondaryBrush)
        {
            CloseBadgeIcon.Foreground = secondaryBrush;
        }
    }

    private void OnCloseButtonMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPointerDown = false;
        e.Handled = true;
    }

    private void OnCloseButtonMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPointerDown = false;
        e.Handled = true;
        CloseToTrayRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBadgeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPointerDown = true;
        _isDragging = false;
        _pointerDownScreenPoint = PointToScreen(e.GetPosition(this));
        _initialWindowLeft = Left;
        _initialWindowTop = Top;
        BadgeBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnBadgeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDown)
        {
            return;
        }

        var currentScreenPoint = PointToScreen(e.GetPosition(this));
        var deltaX = currentScreenPoint.X - _pointerDownScreenPoint.X;
        var deltaY = currentScreenPoint.Y - _pointerDownScreenPoint.Y;

        if (!_isDragging && (Math.Abs(deltaX) > DragThresholdPixels || Math.Abs(deltaY) > DragThresholdPixels))
        {
            _isDragging = true;
        }

        if (_isDragging)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            Left = _initialWindowLeft + (deltaX / scaleX);
            Top = _initialWindowTop + (deltaY / scaleY);
        }
    }

    private void OnBadgeMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerDown)
        {
            return;
        }

        _isPointerDown = false;
        BadgeBorder.ReleaseMouseCapture();
        e.Handled = true;

        if (_isDragging)
        {
            _isDragging = false;
            BadgePositionChanged?.Invoke(this, (Left, Top));
        }
        else
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBadgeMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        LanguageToggleRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
