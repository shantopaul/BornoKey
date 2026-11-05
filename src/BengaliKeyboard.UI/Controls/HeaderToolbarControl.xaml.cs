using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// Top draggable header bar containing the [ বাংলা | ENG ] language toggle,
/// interactive [ − | 100% | + ] size increase/decrease &amp; drag-zoom controls,
/// Quick Settings button, and 4-mode window controls.
/// </summary>
public partial class HeaderToolbarControl : UserControl
{
    public static readonly DependencyProperty ActiveLanguageProperty =
        DependencyProperty.Register(
            nameof(ActiveLanguage),
            typeof(LanguageMode),
            typeof(HeaderToolbarControl),
            new PropertyMetadata(LanguageMode.Bangla, OnActiveLanguageChanged));

    public static readonly DependencyProperty ToggleLanguageCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleLanguageCommand),
            typeof(ICommand),
            typeof(HeaderToolbarControl),
            new PropertyMetadata(null, OnToggleLanguageCommandChanged));

    private bool _isDraggingScaleBadge;
    private bool _hasMovedDuringScaleDrag;
    private Point _scaleDragLastPoint;

    public event EventHandler? CompactModeRequested;

    public event EventHandler? MinimalIconModeRequested;

    public event EventHandler? MinimizeRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? IncreaseSizeRequested;

    public event EventHandler? DecreaseSizeRequested;

    public event EventHandler? ResetSizeRequested;

    public event EventHandler<double>? DragScaleDeltaRequested;

    public LanguageMode ActiveLanguage
    {
        get => (LanguageMode)GetValue(ActiveLanguageProperty);
        set => SetValue(ActiveLanguageProperty, value);
    }

    public ICommand? ToggleLanguageCommand
    {
        get => (ICommand?)GetValue(ToggleLanguageCommandProperty);
        set => SetValue(ToggleLanguageCommandProperty, value);
    }

    public HeaderToolbarControl()
    {
        InitializeComponent();
    }

    public void SetCompactButtonState(bool isCompact)
    {
        CompactModeButton.Content = isCompact ? "⤡ Expand" : "⤢ Compact";
    }

    public void SetScalePercentage(int percentage)
    {
        var clamped = Math.Clamp(percentage, 40, 250);
        ScalePercentTextBlock.Text = $"{clamped}%";
    }

    private static void OnActiveLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeaderToolbarControl control && e.NewValue is LanguageMode mode)
        {
            control.LanguageToggle.ActiveLanguage = mode;
        }
    }

    private static void OnToggleLanguageCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeaderToolbarControl control)
        {
            control.LanguageToggle.ToggleCommand = e.NewValue as ICommand;
        }
    }

    private void OnDecreaseSizeClick(object sender, RoutedEventArgs e) =>
        DecreaseSizeRequested?.Invoke(this, EventArgs.Empty);

    private void OnIncreaseSizeClick(object sender, RoutedEventArgs e) =>
        IncreaseSizeRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnCompactClick(object sender, RoutedEventArgs e) =>
        CompactModeRequested?.Invoke(this, EventArgs.Empty);

    private void OnMinimalIconClick(object sender, RoutedEventArgs e) =>
        MinimalIconModeRequested?.Invoke(this, EventArgs.Empty);

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        MinimizeRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnScaleBadgeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingScaleBadge = true;
        _hasMovedDuringScaleDrag = false;
        _scaleDragLastPoint = e.GetPosition(this);
        _ = ScaleDragBadge.CaptureMouse();
        e.Handled = true;
    }

    private void OnScaleBadgeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingScaleBadge || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _scaleDragLastPoint.X;
        if (Math.Abs(deltaX) >= 3.0)
        {
            _hasMovedDuringScaleDrag = true;
            _scaleDragLastPoint = currentPoint;
            // Every 100px of horizontal drag scales by ~20%
            var scaleDelta = Math.Clamp(deltaX / 500.0, -0.12, 0.12);
            DragScaleDeltaRequested?.Invoke(this, scaleDelta);
        }

        e.Handled = true;
    }

    private void OnScaleBadgeMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingScaleBadge)
        {
            return;
        }

        _isDraggingScaleBadge = false;
        ScaleDragBadge.ReleaseMouseCapture();

        if (!_hasMovedDuringScaleDrag)
        {
            ResetSizeRequested?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void OnScaleBadgeLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDraggingScaleBadge = false;
    }
}
