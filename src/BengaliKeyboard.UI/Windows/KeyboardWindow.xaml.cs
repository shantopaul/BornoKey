using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.UI.Controls;
using BengaliKeyboard.UI.ViewModels;

namespace BengaliKeyboard.UI.Windows;

/// <summary>
/// Main floating Ridmik-style virtual keyboard window.
/// Applies Win32 WS_EX_NOACTIVATE and WM_MOUSEACTIVATE (MA_NOACTIVATE) so clicking on-screen keys
/// never steals focus from the user's active target application (Architecture.md Section 5.1).
/// Supports proportional Viewbox scaling, 8-edge/corner interactive drag resizing, header [ − | 100% | + ]
/// scale controls, Ctrl+MouseWheel zoom, and 4-mode transitions.
/// </summary>
public partial class KeyboardWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private readonly KeyboardViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly IWindowManagerService? _windowManagerService;
    private readonly NonActivatingWindowResizer _windowResizer;
    private HwndSource? _hwndSource;
    private bool _allowInteractiveActivation;
    private bool _isApplyingGeometry;

    public KeyboardViewModel ViewModel => _viewModel;

    public event EventHandler<WindowStateConfig>? WindowGeometryChanged;

    public event EventHandler? SettingsWindowRequested;

    public KeyboardWindow(
        KeyboardViewModel viewModel,
        ISettingsService settingsService,
        IWindowManagerService? windowManagerService = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settingsService);

        _viewModel = viewModel;
        _settingsService = settingsService;
        _windowManagerService = windowManagerService;
        DataContext = _viewModel;

        InitializeComponent();

        _windowResizer = new NonActivatingWindowResizer(
            this,
            onResizeCompleted: PersistCurrentGeometryAfterResize,
            onSizeChangedLive: (w, h) => ApplyResponsiveMetrics(w, h));

        ApplySavedWindowGeometry(_settingsService.Current.WindowState);
        Opacity = _settingsService.Current.Opacity;
        Topmost = _settingsService.Current.WindowState.AlwaysOnTop;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
        SizeChanged += OnWindowSizeChanged;
        DpiChanged += OnWindowDpiChanged;
        RebuildKeyboardGrid(_viewModel.CurrentLayout);
        ApplyResponsiveMetrics(Width, Height);
    }

    public void ApplyAppearanceFromSettings()
    {
        var settings = _settingsService.Current;
        Opacity = Math.Clamp(settings.Opacity, AppSettings.MinOpacity, AppSettings.MaxOpacity);
        Topmost = settings.WindowState.AlwaysOnTop;

        foreach (var child in KeyboardGridHost.Children)
        {
            if (child is Grid rowGrid)
            {
                foreach (var keyElement in rowGrid.Children)
                {
                    if (keyElement is KeyboardKeyControl keyControl)
                    {
                        keyControl.ApplyKeyAppearance(
                            settings.KeyCornerRadius,
                            settings.KeySpacing,
                            settings.KeyRepeatDelayMs,
                            settings.KeyRepeatIntervalMs);
                    }
                }
            }
        }
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

        _isApplyingGeometry = true;
        try
        {
            var isCompact = state.Mode == KeyboardMode.Compact;
            HeaderToolbar.SetCompactButtonState(isCompact);

            if (isCompact)
            {
                MinWidth = WindowStateConfig.MinCompactWidth;
                MaxWidth = WindowStateConfig.MaxCompactWidth;
                MinHeight = WindowStateConfig.MinCompactHeight;
                MaxHeight = WindowStateConfig.MaxCompactHeight;
                Width = state.CompactWidth;
                Height = state.CompactHeight;
            }
            else
            {
                MinWidth = WindowStateConfig.MinExpandedWidth;
                MaxWidth = WindowStateConfig.MaxExpandedWidth;
                MinHeight = WindowStateConfig.MinExpandedHeight;
                MaxHeight = WindowStateConfig.MaxExpandedHeight;
                Width = state.Width;
                Height = state.Height;
            }

            Left = state.Left;
            Top = state.Top;
            Topmost = state.AlwaysOnTop;
            ApplyResponsiveMetrics(Width, Height);
        }
        finally
        {
            _isApplyingGeometry = false;
        }
    }

    public WindowStateConfig CaptureCurrentWindowGeometry()
    {
        var current = _settingsService.Current.WindowState;
        var activeWidth = ActualWidth > 0 ? ActualWidth : Width;
        var activeHeight = ActualHeight > 0 ? ActualHeight : Height;

        if (current.Mode == KeyboardMode.Compact)
        {
            current.CompactWidth = activeWidth;
            current.CompactHeight = activeHeight;
        }
        else
        {
            current.Width = activeWidth;
            current.Height = activeHeight;
        }

        current.Left = Left;
        current.Top = Top;
        current.AlwaysOnTop = Topmost;
        current.NormalizeAndClamp();
        return current;
    }

    /// <summary>
    /// Dynamically constructs proportional star-weighted rows and columns from the active KeyboardLayoutModel.
    /// </summary>
    public void RebuildKeyboardGrid(KeyboardLayoutModel? layout)
    {
        KeyboardGridHost.Children.Clear();
        KeyboardGridHost.RowDefinitions.Clear();

        if (layout?.Rows is null || layout.Rows.Count == 0)
        {
            return;
        }

        for (var r = 0; r < layout.Rows.Count; r++)
        {
            var rowModel = layout.Rows[r];
            var heightRatio = rowModel.HeightRatio > 0 ? rowModel.HeightRatio : 1.0;
            KeyboardGridHost.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(heightRatio, GridUnitType.Star)
            });

            var rowGrid = new Grid();
            for (var c = 0; c < rowModel.Keys.Count; c++)
            {
                var keyDef = rowModel.Keys[c];
                var widthRatio = keyDef.WidthRatio > 0 ? keyDef.WidthRatio : 1.0;
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(widthRatio, GridUnitType.Star)
                });

                var keyControl = new KeyboardKeyControl
                {
                    KeyModel = keyDef,
                    KeyCommand = _viewModel.PressKeyCommand
                };
                var currentSettings = _settingsService.Current;
                keyControl.ApplyKeyAppearance(
                    currentSettings.KeyCornerRadius,
                    currentSettings.KeySpacing,
                    currentSettings.KeyRepeatDelayMs,
                    currentSettings.KeyRepeatIntervalMs);
                keyControl.ApplyResponsiveScale(ComputeUniformScale(
                    ActualWidth > 0 ? ActualWidth : Width,
                    ActualHeight > 0 ? ActualHeight : Height));

                Grid.SetColumn(keyControl, c);
                rowGrid.Children.Add(keyControl);
            }

            Grid.SetRow(rowGrid, r);
            KeyboardGridHost.Children.Add(rowGrid);
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
        _windowResizer.Detach();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        SizeChanged -= OnWindowSizeChanged;
        DpiChanged -= OnWindowDpiChanged;

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        base.OnClosed(e);
    }

    private void OnSettingsChanged(object? sender, AppSettings e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyAppearanceFromSettings();
        }
        else
        {
            _ = Dispatcher.InvokeAsync(ApplyAppearanceFromSettings);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(KeyboardViewModel.CurrentLayout), StringComparison.Ordinal))
        {
            RebuildKeyboardGrid(_viewModel.CurrentLayout);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveMetrics(e.NewSize.Width, e.NewSize.Height);

        if (_isApplyingGeometry || !IsLoaded)
        {
            return;
        }

        var updated = CaptureCurrentWindowGeometry();
        WindowGeometryChanged?.Invoke(this, updated);
    }

    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e)
    {
        if (_isApplyingGeometry || !IsLoaded)
        {
            return;
        }

        var updated = CaptureCurrentWindowGeometry();
        updated.DpiScale = Math.Clamp(e.NewDpi.DpiScaleX, 0.5, 4.0);
        WindowGeometryChanged?.Invoke(this, updated);
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

    private static double ComputeUniformScale(double width, double height)
    {
        var w = width > 0 ? width : WindowStateConfig.DefaultExpandedWidth;
        var h = height > 0 ? height : WindowStateConfig.DefaultExpandedHeight;
        var wRatio = w / WindowStateConfig.DefaultExpandedWidth;
        var hRatio = h / WindowStateConfig.DefaultExpandedHeight;
        return Math.Clamp(Math.Min(wRatio, hRatio), 0.55, 1.85);
    }

    private void ApplyResponsiveMetrics(double width, double height)
    {
        var pct = NonActivatingWindowResizer.CalculateScalePercentage(width, WindowStateConfig.DefaultExpandedWidth);
        HeaderToolbar.SetScalePercentage(pct);

        var uniformScale = ComputeUniformScale(width, height);
        HeaderToolbar.ApplyResponsiveLayout(width, uniformScale);
        SuggestionBar.ApplyResponsiveLayout(uniformScale);

        foreach (var child in KeyboardGridHost.Children)
        {
            if (child is Grid rowGrid)
            {
                foreach (var keyElement in rowGrid.Children)
                {
                    if (keyElement is KeyboardKeyControl keyControl)
                    {
                        keyControl.ApplyResponsiveScale(uniformScale);
                    }
                }
            }
        }
    }

    private void PersistCurrentGeometryAfterResize()
    {
        var updatedState = CaptureCurrentWindowGeometry();
        WindowGeometryChanged?.Invoke(this, updatedState);
    }

    private void OnCornerResizeGripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _windowResizer.BeginBottomRightGripDrag(e);
    }

    private void OnHeaderDecreaseSizeRequested(object? sender, EventArgs e)
    {
        _windowResizer.AdjustScaleByStep(-NonActivatingWindowResizer.StepScaleFactor);
    }

    private void OnHeaderIncreaseSizeRequested(object? sender, EventArgs e)
    {
        _windowResizer.AdjustScaleByStep(NonActivatingWindowResizer.StepScaleFactor);
    }

    private void OnHeaderResetSizeRequested(object? sender, EventArgs e)
    {
        var isCompact = _settingsService.Current.WindowState.Mode == KeyboardMode.Compact;
        _windowResizer.ResetToDefaultSize(isCompact);
    }

    private void OnHeaderDragScaleDeltaRequested(object? sender, double scaleDelta)
    {
        _windowResizer.AdjustScaleByStep(scaleDelta);
    }

    private void OnHeaderSettingsRequested(object? sender, EventArgs e)
    {
        SettingsWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnHeaderEmojiPanelRequested(object? sender, EventArgs e)
    {
        await _viewModel.PressKeyAsync(new KeyDefinition
        {
            Id = "menu_emoji",
            DisplayLabel = "😀",
            KeyType = KeyType.EmojiSwitch
        }).ConfigureAwait(true);
    }

    private async void OnHeaderClipboardPanelRequested(object? sender, EventArgs e)
    {
        await _viewModel.PressKeyAsync(new KeyDefinition
        {
            Id = "menu_clipboard",
            DisplayLabel = "📋",
            KeyType = KeyType.ClipboardSwitch
        }).ConfigureAwait(true);
    }

    private async void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_windowResizer.IsDragging || e.Handled)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            var updatedState = CaptureCurrentWindowGeometry();
            WindowGeometryChanged?.Invoke(this, updatedState);

            if (_windowManagerService is not null)
            {
                await _windowManagerService.SaveWindowStateAsync(updatedState).ConfigureAwait(true);
            }
            else
            {
                _settingsService.Current.WindowState = updatedState;
                await _settingsService.SaveAsync(_settingsService.Current).ConfigureAwait(true);
            }
        }
    }

    private async void OnHeaderCompactModeRequested(object? sender, EventArgs e)
    {
        var state = _settingsService.Current.WindowState;
        var nextMode = state.Mode == KeyboardMode.Compact
            ? KeyboardMode.Expanded
            : KeyboardMode.Compact;

        if (_windowManagerService is not null)
        {
            _windowManagerService.ApplyWindowMode(nextMode);
            return;
        }

        state.Mode = nextMode;
        state.LastVisibleMode = nextMode;
        ApplySavedWindowGeometry(state);
        await _settingsService.SaveAsync(_settingsService.Current).ConfigureAwait(true);
    }

    private void OnHeaderMinimalIconModeRequested(object? sender, EventArgs e)
    {
        if (_windowManagerService is not null)
        {
            _windowManagerService.ApplyWindowMode(KeyboardMode.MinimalIcon);
            return;
        }

        Hide();
    }

    private void OnHeaderMinimizeRequested(object? sender, EventArgs e)
    {
        if (_windowManagerService is not null)
        {
            _windowManagerService.ApplyWindowMode(KeyboardMode.HiddenToTray);
            return;
        }

        Hide();
    }

    private async void OnHeaderCloseRequested(object? sender, EventArgs e)
    {
        var updatedState = CaptureCurrentWindowGeometry();
        if (_windowManagerService is not null)
        {
            await _windowManagerService.SaveWindowStateAsync(updatedState).ConfigureAwait(true);
            _windowManagerService.ApplyWindowMode(KeyboardMode.HiddenToTray);
            return;
        }

        _settingsService.Current.WindowState = updatedState;
        await _settingsService.SaveAsync(_settingsService.Current).ConfigureAwait(true);
        Hide();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
