using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.UI.Windows;

public enum ResizeEdge
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 3,
    Bottom = 4,
    TopLeft = 5,
    TopRight = 6,
    BottomLeft = 7,
    BottomRight = 8
}

/// <summary>
/// Enables smooth 8-edge/corner drag resizing, bottom-right grip dragging, and proportional
/// increase/decrease scaling on borderless <c>WS_EX_NOACTIVATE</c> windows (<c>ResizeMode="NoResize"</c>)
/// without stealing focus from the user's active target application.
/// </summary>
public sealed class NonActivatingWindowResizer
{
    public const double DefaultBorderGripDip = 7.0;
    public const double StepScaleFactor = 0.08;

    private readonly Window _window;
    private readonly Action? _onResizeCompleted;
    private readonly Action<double, double>? _onSizeChangedLive;
    private ResizeEdge _activeEdge = ResizeEdge.None;
    private Point _dragStartScreenDip;
    private Rect _dragStartBounds;
    private bool _isDragging;

    public bool IsDragging => _isDragging;

    public NonActivatingWindowResizer(
        Window window,
        Action? onResizeCompleted = null,
        Action<double, double>? onSizeChangedLive = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _onResizeCompleted = onResizeCompleted;
        _onSizeChangedLive = onSizeChangedLive;

        _window.PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
        _window.PreviewMouseMove += OnWindowPreviewMouseMove;
        _window.PreviewMouseLeftButtonUp += OnWindowPreviewMouseLeftButtonUp;
        _window.PreviewMouseWheel += OnWindowPreviewMouseWheel;
        _window.LostMouseCapture += OnWindowLostMouseCapture;
    }

    public void Detach()
    {
        _window.PreviewMouseLeftButtonDown -= OnWindowPreviewMouseLeftButtonDown;
        _window.PreviewMouseMove -= OnWindowPreviewMouseMove;
        _window.PreviewMouseLeftButtonUp -= OnWindowPreviewMouseLeftButtonUp;
        _window.PreviewMouseWheel -= OnWindowPreviewMouseWheel;
        _window.LostMouseCapture -= OnWindowLostMouseCapture;
    }

    /// <summary>
    /// Starts an interactive bottom-right corner drag resize (invoked from the visible ◢ corner grip).
    /// </summary>
    public void BeginBottomRightGripDrag(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        StartDragResize(ResizeEdge.BottomRight);
        e.Handled = true;
    }

    /// <summary>
    /// Proportionally increases or decreases the window's dimensions around its top-left anchor,
    /// clamped safely between the window's configured minimum and maximum bounds.
    /// </summary>
    public void AdjustScaleByStep(double stepMultiplier)
    {
        var minW = ResolveBound(_window.MinWidth, WindowStateConfig.MinExpandedWidth);
        var maxW = ResolveBound(_window.MaxWidth, WindowStateConfig.MaxExpandedWidth);
        var minH = ResolveBound(_window.MinHeight, WindowStateConfig.MinExpandedHeight);
        var maxH = ResolveBound(_window.MaxHeight, WindowStateConfig.MaxExpandedHeight);

        var currentW = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        var currentH = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;

        var (newW, newH) = CalculateSteppedSize(
            currentW,
            currentH,
            stepMultiplier,
            minW,
            maxW,
            minH,
            maxH);

        _window.Width = newW;
        _window.Height = newH;
        _onSizeChangedLive?.Invoke(newW, newH);
        _onResizeCompleted?.Invoke();
    }

    /// <summary>
    /// Resets the window dimensions to the standard default size for the current mode.
    /// </summary>
    public void ResetToDefaultSize(bool isCompactMode = false)
    {
        var targetW = isCompactMode ? WindowStateConfig.DefaultCompactWidth : WindowStateConfig.DefaultExpandedWidth;
        var targetH = isCompactMode ? WindowStateConfig.DefaultCompactHeight : WindowStateConfig.DefaultExpandedHeight;

        _window.Width = targetW;
        _window.Height = targetH;
        _onSizeChangedLive?.Invoke(targetW, targetH);
        _onResizeCompleted?.Invoke();
    }

    public static (double Width, double Height) CalculateSteppedSize(
        double currentWidth,
        double currentHeight,
        double scaleDelta,
        double minWidth,
        double maxWidth,
        double minHeight,
        double maxHeight)
    {
        var factor = Math.Clamp(1.0 + scaleDelta, 0.5, 2.0);
        var newW = Math.Clamp(Math.Round(currentWidth * factor), minWidth, maxWidth);
        var newH = Math.Clamp(Math.Round(currentHeight * factor), minHeight, maxHeight);
        return (newW, newH);
    }

    public static int CalculateScalePercentage(double currentWidth, double baselineWidth = WindowStateConfig.DefaultExpandedWidth)
    {
        if (baselineWidth <= 0 || double.IsNaN(currentWidth) || currentWidth <= 0)
        {
            return 100;
        }

        return (int)Math.Clamp(Math.Round((currentWidth / baselineWidth) * 100.0), 40, 250);
    }

    public static ResizeEdge HitTestResizeEdge(
        Point clientPoint,
        double windowWidth,
        double windowHeight,
        double gripThickness = DefaultBorderGripDip)
    {
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return ResizeEdge.None;
        }

        var onLeft = clientPoint.X >= 0 && clientPoint.X <= gripThickness;
        var onRight = clientPoint.X <= windowWidth && clientPoint.X >= windowWidth - gripThickness;
        var onTop = clientPoint.Y >= 0 && clientPoint.Y <= gripThickness;
        var onBottom = clientPoint.Y <= windowHeight && clientPoint.Y >= windowHeight - gripThickness;

        // Give corners a slightly larger 14px target zone for comfortable diagonal resizing
        var cornerZone = gripThickness * 2.0;
        var nearLeft = clientPoint.X >= 0 && clientPoint.X <= cornerZone;
        var nearRight = clientPoint.X <= windowWidth && clientPoint.X >= windowWidth - cornerZone;
        var nearTop = clientPoint.Y >= 0 && clientPoint.Y <= cornerZone;
        var nearBottom = clientPoint.Y <= windowHeight && clientPoint.Y >= windowHeight - cornerZone;

        if ((onTop && nearLeft) || (onLeft && nearTop)) return ResizeEdge.TopLeft;
        if ((onTop && nearRight) || (onRight && nearTop)) return ResizeEdge.TopRight;
        if ((onBottom && nearLeft) || (onLeft && nearBottom)) return ResizeEdge.BottomLeft;
        if ((onBottom && nearRight) || (onRight && nearBottom)) return ResizeEdge.BottomRight;
        if (onLeft) return ResizeEdge.Left;
        if (onRight) return ResizeEdge.Right;
        if (onTop) return ResizeEdge.Top;
        if (onBottom) return ResizeEdge.Bottom;

        return ResizeEdge.None;
    }

    public static Rect CalculateResizedBounds(
        ResizeEdge edge,
        Rect startBounds,
        Vector deltaDip,
        double minWidth,
        double maxWidth,
        double minHeight,
        double maxHeight)
    {
        var left = startBounds.Left;
        var top = startBounds.Top;
        var width = startBounds.Width;
        var height = startBounds.Height;

        if (edge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight)
        {
            width = Math.Clamp(startBounds.Width + deltaDip.X, minWidth, maxWidth);
        }
        else if (edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft)
        {
            var targetWidth = Math.Clamp(startBounds.Width - deltaDip.X, minWidth, maxWidth);
            left = startBounds.Right - targetWidth;
            width = targetWidth;
        }

        if (edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight)
        {
            height = Math.Clamp(startBounds.Height + deltaDip.Y, minHeight, maxHeight);
        }
        else if (edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight)
        {
            var targetHeight = Math.Clamp(startBounds.Height - deltaDip.Y, minHeight, maxHeight);
            top = startBounds.Bottom - targetHeight;
            height = targetHeight;
        }

        return new Rect(left, top, width, height);
    }

    private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        var height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        var pos = e.GetPosition(_window);

        var edge = HitTestResizeEdge(pos, width, height);
        if (edge != ResizeEdge.None)
        {
            StartDragResize(edge);
            e.Handled = true;
        }
    }

    private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _activeEdge != ResizeEdge.None)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDragResize();
                return;
            }

            var currentScreenDip = GetCursorPositionDip();
            var delta = currentScreenDip - _dragStartScreenDip;

            var minW = ResolveBound(_window.MinWidth, WindowStateConfig.MinExpandedWidth);
            var maxW = ResolveBound(_window.MaxWidth, WindowStateConfig.MaxExpandedWidth);
            var minH = ResolveBound(_window.MinHeight, WindowStateConfig.MinExpandedHeight);
            var maxH = ResolveBound(_window.MaxHeight, WindowStateConfig.MaxExpandedHeight);

            var updated = CalculateResizedBounds(
                _activeEdge,
                _dragStartBounds,
                delta,
                minW,
                maxW,
                minH,
                maxH);

            _window.Left = updated.Left;
            _window.Top = updated.Top;
            _window.Width = updated.Width;
            _window.Height = updated.Height;

            _onSizeChangedLive?.Invoke(updated.Width, updated.Height);
            e.Handled = true;
            return;
        }

        var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        var height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        var pos = e.GetPosition(_window);
        var hoverEdge = HitTestResizeEdge(pos, width, height);
        UpdateWindowCursor(hoverEdge);
    }

    private void OnWindowPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            EndDragResize();
            e.Handled = true;
        }
    }

    private void OnWindowLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _activeEdge = ResizeEdge.None;
            _window.Cursor = null;
            _onResizeCompleted?.Invoke();
        }
    }

    private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Support Ctrl + MouseWheel to smoothly increase or decrease keyboard/panel size
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Delta != 0)
        {
            var deltaScale = e.Delta > 0 ? StepScaleFactor : -StepScaleFactor;
            AdjustScaleByStep(deltaScale);
            e.Handled = true;
        }
    }

    private void StartDragResize(ResizeEdge edge)
    {
        var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        var height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;

        _activeEdge = edge;
        _isDragging = true;
        _dragStartScreenDip = GetCursorPositionDip();
        _dragStartBounds = new Rect(_window.Left, _window.Top, width, height);

        UpdateWindowCursor(edge);
        _ = Mouse.Capture(_window, CaptureMode.SubTree);
    }

    private void EndDragResize()
    {
        _isDragging = false;
        _activeEdge = ResizeEdge.None;
        if (ReferenceEquals(Mouse.Captured, _window))
        {
            Mouse.Capture(null);
        }

        _window.Cursor = null;
        _onResizeCompleted?.Invoke();
    }

    private void UpdateWindowCursor(ResizeEdge edge)
    {
        _window.Cursor = edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
            ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
            _ => null
        };
    }

    private Point GetCursorPositionDip()
    {
        if (!GetCursorPos(out var pt))
        {
            return new Point(_window.Left, _window.Top);
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        return new Point(pt.X / scaleX, pt.Y / scaleY);
    }

    private static double ResolveBound(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return fallback;
        }

        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point lpPoint);
}
