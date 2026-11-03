using System.Windows;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;

namespace BengaliKeyboard.Windows;

/// <summary>
/// Implements the 4-mode floating keyboard state machine (<c>Expanded</c>, <c>Compact</c>,
/// <c>MinimalIcon</c>, <c>HiddenToTray</c>), multi-monitor 60%-intersection clamping,
/// DPI scaling, and 300ms debounced atomic persistence (Architecture.md Sections 2.3 &amp; 5.3).
/// </summary>
public sealed class WindowPositionService : IWindowManagerService, IDisposable
{
    public const double BottomTaskbarClearancePx = 40.0;
    public const double MinimalIconWidth = 72.0;
    public const double MinimalIconHeight = 40.0;
    public const int DebounceSaveDelayMs = 300;

    private readonly ISettingsService _settingsService;
    private readonly MonitorService _monitorService;
    private readonly IMessenger _messenger;
    private readonly object _debounceLock = new();

    private Action<KeyboardMode, WindowStateConfig>? _modePresenterCallback;
    private CancellationTokenSource? _debounceCts;
    private KeyboardMode _currentMode;
    private bool _disposed;

    public KeyboardMode CurrentMode => _currentMode;

    public WindowPositionService(
        ISettingsService settingsService,
        MonitorService monitorService,
        IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(monitorService);

        _settingsService = settingsService;
        _monitorService = monitorService;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        _currentMode = _settingsService.Current.WindowState.Mode;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Attaches the UI presenter callback that shows/hides <c>KeyboardWindow</c> and <c>FloatingIconWindow</c>
    /// whenever the presentation mode or clamped geometry changes.
    /// </summary>
    public void RegisterModePresenter(Action<KeyboardMode, WindowStateConfig> presenterCallback)
    {
        ArgumentNullException.ThrowIfNull(presenterCallback);
        _modePresenterCallback = presenterCallback;
    }

    public void ApplyWindowMode(KeyboardMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var state = _settingsService.Current.WindowState;
        var previousMode = _currentMode;

        if (mode is KeyboardMode.Expanded or KeyboardMode.Compact)
        {
            state.LastVisibleMode = mode;
        }
        else if (previousMode is KeyboardMode.Expanded or KeyboardMode.Compact)
        {
            state.LastVisibleMode = previousMode;
        }

        state.Mode = mode;
        _currentMode = mode;

        var clamped = ValidateAndClampBounds(state);
        _settingsService.Current.WindowState = clamped;

        _modePresenterCallback?.Invoke(mode, clamped);

        if (previousMode != mode)
        {
            _messenger.Send(new WindowModeChangedMessage(previousMode, mode));
        }

        ScheduleDebouncedSave(clamped);
    }

    public void RestoreLastVisibleMode()
    {
        var state = _settingsService.Current.WindowState;
        var targetMode = state.LastVisibleMode is KeyboardMode.Compact
            ? KeyboardMode.Compact
            : KeyboardMode.Expanded;

        ApplyWindowMode(targetMode);
    }

    public void ToggleVisibility()
    {
        if (_currentMode == KeyboardMode.HiddenToTray)
        {
            RestoreLastVisibleMode();
        }
        else
        {
            ApplyWindowMode(KeyboardMode.HiddenToTray);
        }
    }

    /// <summary>
    /// Validates and clamps <paramref name="state"/> against connected monitors using the 60% intersection rule.
    /// If the previous monitor was disconnected or coordinates are off-screen, anchors the keyboard centered
    /// horizontally and <c>40px</c> above the taskbar on the primary monitor's <c>WorkingArea</c>
    /// (Architecture.md Section 5.3).
    /// </summary>
    public WindowStateConfig ValidateAndClampBounds(WindowStateConfig state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.NormalizeAndClamp();

        var primaryMonitor = _monitorService.GetPrimaryMonitor();
        var primaryWorkArea = primaryMonitor.WorkingArea;

        // Clamp dimensions to both mode bounds and visible monitor working area
        var maxAllowedExpandedWidth = Math.Max(WindowStateConfig.MinExpandedWidth, Math.Min(WindowStateConfig.MaxExpandedWidth, primaryWorkArea.Width));
        var maxAllowedExpandedHeight = Math.Max(WindowStateConfig.MinExpandedHeight, Math.Min(WindowStateConfig.MaxExpandedHeight, primaryWorkArea.Height));
        state.Width = Math.Clamp(state.Width, WindowStateConfig.MinExpandedWidth, maxAllowedExpandedWidth);
        state.Height = Math.Clamp(state.Height, WindowStateConfig.MinExpandedHeight, maxAllowedExpandedHeight);

        var maxAllowedCompactWidth = Math.Max(WindowStateConfig.MinCompactWidth, Math.Min(WindowStateConfig.MaxCompactWidth, primaryWorkArea.Width));
        var maxAllowedCompactHeight = Math.Max(WindowStateConfig.MinCompactHeight, Math.Min(WindowStateConfig.MaxCompactHeight, primaryWorkArea.Height));
        state.CompactWidth = Math.Clamp(state.CompactWidth, WindowStateConfig.MinCompactWidth, maxAllowedCompactWidth);
        state.CompactHeight = Math.Clamp(state.CompactHeight, WindowStateConfig.MinCompactHeight, maxAllowedCompactHeight);

        var useCompactDimensions = state.Mode == KeyboardMode.Compact
            || (state.Mode is KeyboardMode.MinimalIcon or KeyboardMode.HiddenToTray
                && state.LastVisibleMode == KeyboardMode.Compact);

        var activeWidth = useCompactDimensions ? state.CompactWidth : state.Width;
        var activeHeight = useCompactDimensions ? state.CompactHeight : state.Height;

        var candidateRect = new Rect(state.Left, state.Top, activeWidth, activeHeight);
        var validMonitor = _monitorService.FindValidMonitorForWindow(candidateRect, state.MonitorDeviceId);

        if (validMonitor is not null)
        {
            state.MonitorDeviceId = validMonitor.DeviceName;
            state.DpiScale = validMonitor.DpiScale;

            var work = validMonitor.WorkingArea;
            if (useCompactDimensions)
            {
                state.CompactWidth = Math.Clamp(state.CompactWidth, WindowStateConfig.MinCompactWidth, Math.Max(WindowStateConfig.MinCompactWidth, Math.Min(WindowStateConfig.MaxCompactWidth, work.Width)));
                state.CompactHeight = Math.Clamp(state.CompactHeight, WindowStateConfig.MinCompactHeight, Math.Max(WindowStateConfig.MinCompactHeight, Math.Min(WindowStateConfig.MaxCompactHeight, work.Height)));
                activeWidth = state.CompactWidth;
                activeHeight = state.CompactHeight;
            }
            else
            {
                state.Width = Math.Clamp(state.Width, WindowStateConfig.MinExpandedWidth, Math.Max(WindowStateConfig.MinExpandedWidth, Math.Min(WindowStateConfig.MaxExpandedWidth, work.Width)));
                state.Height = Math.Clamp(state.Height, WindowStateConfig.MinExpandedHeight, Math.Max(WindowStateConfig.MinExpandedHeight, Math.Min(WindowStateConfig.MaxExpandedHeight, work.Height)));
                activeWidth = state.Width;
                activeHeight = state.Height;
            }

            state.Left = Math.Clamp(state.Left, work.Left, Math.Max(work.Left, work.Right - activeWidth));
            state.Top = Math.Clamp(state.Top, work.Top, Math.Max(work.Top, work.Bottom - activeHeight));
        }
        else
        {
            // Anchor centered horizontally and 40px above the taskbar on the primary monitor's WorkingArea
            state.MonitorDeviceId = primaryMonitor.DeviceName;
            state.DpiScale = primaryMonitor.DpiScale;
            state.Left = Math.Round(primaryWorkArea.Left + Math.Max(0.0, (primaryWorkArea.Width - activeWidth) / 2.0));
            state.Top = Math.Round(Math.Max(
                primaryWorkArea.Top,
                primaryWorkArea.Bottom - activeHeight - BottomTaskbarClearancePx));
        }

        // Also validate MinimalIcon badge coordinates (72x40) so the floating badge is never stranded off-screen
        var iconCandidateRect = new Rect(state.IconLeft, state.IconTop, MinimalIconWidth, MinimalIconHeight);
        var validIconMonitor = _monitorService.FindValidMonitorForWindow(iconCandidateRect, state.MonitorDeviceId);
        if (validIconMonitor is not null)
        {
            var iconWork = validIconMonitor.WorkingArea;
            state.IconLeft = Math.Clamp(state.IconLeft, iconWork.Left, Math.Max(iconWork.Left, iconWork.Right - MinimalIconWidth));
            state.IconTop = Math.Clamp(state.IconTop, iconWork.Top, Math.Max(iconWork.Top, iconWork.Bottom - MinimalIconHeight));
        }
        else
        {
            state.IconLeft = Math.Round(Math.Max(primaryWorkArea.Left, primaryWorkArea.Right - MinimalIconWidth - 24.0));
            state.IconTop = Math.Round(Math.Max(primaryWorkArea.Top, primaryWorkArea.Bottom - MinimalIconHeight - BottomTaskbarClearancePx));
        }

        return state;
    }

    public async Task SaveWindowStateAsync(WindowStateConfig state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var clamped = ValidateAndClampBounds(state);
        _settingsService.Current.WindowState = clamped;
        _currentMode = clamped.Mode;
        await _settingsService.SaveAsync(_settingsService.Current, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Debounces rapid window move/resize saves by 300ms (Rules.md Scenario 1 &amp; security-checklist.md Section 1.8).
    /// </summary>
    public void ScheduleDebouncedSave(WindowStateConfig state)
    {
        ArgumentNullException.ThrowIfNull(state);

        CancellationToken token;
        lock (_debounceLock)
        {
            if (_disposed)
            {
                return;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceSaveDelayMs, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    await SaveWindowStateAsync(state, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var clamped = ValidateAndClampBounds(_settingsService.Current.WindowState);
        _settingsService.Current.WindowState = clamped;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => _modePresenterCallback?.Invoke(_currentMode, clamped));
        }
        else
        {
            _modePresenterCallback?.Invoke(_currentMode, clamped);
        }

        ScheduleDebouncedSave(clamped);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }
}
