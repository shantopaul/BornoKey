using System.Windows.Threading;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Input;

/// <summary>
/// Processes virtual on-screen key clicks and manages bounded press-and-hold repeat timers
/// for Backspace and Spacebar (Architecture.md Section 3.1 &amp; security/security-checklist.md Section 1.8).
/// </summary>
public sealed class OnScreenInputService : IDisposable
{
    public const int MaxContinuousRepeats = 120;

    private readonly IBengaliInputEngine _inputEngine;
    private readonly ISettingsService _settingsService;
    private DispatcherTimer? _repeatTimer;
    private KeyDefinition? _repeatingKey;
    private int _currentRepeatCount;
    private bool _disposed;

    public bool IsRepeating => _repeatTimer?.IsEnabled == true;

    public int CurrentRepeatCount => _currentRepeatCount;

    public OnScreenInputService(IBengaliInputEngine inputEngine, ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(inputEngine);
        ArgumentNullException.ThrowIfNull(settingsService);

        _inputEngine = inputEngine;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Dispatches a single virtual key press and optionally starts a bounded repeat timer
    /// if the user holds down Backspace or Space.
    /// </summary>
    public async Task HandleKeyDownAsync(KeyDefinition key, bool isShifted = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(key);

        await _inputEngine.ProcessKeyAsync(key, isShifted, cancellationToken).ConfigureAwait(false);

        if (key.KeyType is KeyType.Backspace or KeyType.Space)
        {
            StartRepeat(key);
        }
    }

    /// <summary>
    /// Stops any active key repeat timer immediately when the mouse button is released,
    /// the cursor leaves the keycap, or the keyboard window hides.
    /// </summary>
    public void StopRepeat()
    {
        _repeatingKey = null;
        _currentRepeatCount = 0;

        if (_repeatTimer is not null)
        {
            _repeatTimer.Stop();
            _repeatTimer.Tick -= OnRepeatTimerTick;
            _repeatTimer = null;
        }
    }

    private void StartRepeat(KeyDefinition key)
    {
        StopRepeat();

        _repeatingKey = key;
        _currentRepeatCount = 0;

        var initialDelayMs = Math.Clamp(_settingsService.Current.KeyRepeatDelayMs, 150, 1000);
        _repeatTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(initialDelayMs)
        };
        _repeatTimer.Tick += OnRepeatTimerTick;
        _repeatTimer.Start();
    }

    private async void OnRepeatTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || _repeatTimer is null || _repeatingKey is null)
        {
            StopRepeat();
            return;
        }

        _currentRepeatCount++;
        if (_currentRepeatCount > MaxContinuousRepeats)
        {
            StopRepeat();
            return;
        }

        var repeatIntervalMs = Math.Clamp(_settingsService.Current.KeyRepeatIntervalMs, 20, 250);
        _repeatTimer.Interval = TimeSpan.FromMilliseconds(repeatIntervalMs);

        await _inputEngine.ProcessKeyAsync(_repeatingKey, isShifted: false).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRepeat();
    }
}
