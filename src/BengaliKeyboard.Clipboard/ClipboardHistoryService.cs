using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Clipboard;

/// <summary>
/// Implements <see cref="IClipboardService"/> combining event-driven <see cref="ClipboardMonitor"/>,
/// DPAPI-encrypted <see cref="ClipboardRepository"/>, and user-configurable privacy settings.
/// </summary>
public sealed class ClipboardHistoryService : IClipboardService, IDisposable
{
    private readonly ClipboardRepository _repository;
    private readonly ClipboardMonitor _monitor;
    private readonly ISettingsService _settingsService;
    private bool _isMonitoringEnabled;
    private bool _disposed;

    public bool IsMonitoringEnabled
    {
        get => _isMonitoringEnabled;
        set
        {
            if (_isMonitoringEnabled == value)
            {
                return;
            }

            _isMonitoringEnabled = value;
            _settingsService.Current.EnableClipboardHistory = value;

            if (value)
            {
                _monitor.Start();
            }
            else
            {
                _monitor.Stop();
            }
        }
    }

    public event EventHandler? HistoryChanged;

    public ClipboardHistoryService(
        ClipboardRepository repository,
        ClipboardMonitor monitor,
        ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settingsService);

        _repository = repository;
        _monitor = monitor;
        _settingsService = settingsService;
        _isMonitoringEnabled = _settingsService.Current.EnableClipboardHistory;

        _monitor.ClipboardTextCaptured += OnMonitorClipboardTextCaptured;
    }

    public void InitializeMonitoring()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isMonitoringEnabled = _settingsService.Current.EnableClipboardHistory;

        if (_isMonitoringEnabled)
        {
            _monitor.Start();
        }
        else
        {
            _monitor.Stop();
        }
    }

    public async Task<IReadOnlyList<ClipboardEntry>> GetHistoryAsync(
        string? searchQuery = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var effectiveLimit = limit > 0
            ? Math.Clamp(limit, 1, 500)
            : Math.Clamp(_settingsService.Current.ClipboardHistoryLimit, 10, 500);

        var retentionDays = Math.Clamp(_settingsService.Current.ClipboardRetentionDays, 1, 365);

        return await _repository
            .GetEntriesAsync(searchQuery, effectiveLimit, retentionDays, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClipboardEntry?> CaptureTextAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        if (!_isMonitoringEnabled ||
            string.IsNullOrWhiteSpace(content) ||
            content.Length > ClipboardEntry.MaxAllowedContentCharacters)
        {
            return null;
        }

        var maxLimit = Math.Clamp(_settingsService.Current.ClipboardHistoryLimit, 10, 500);
        var retentionDays = Math.Clamp(_settingsService.Current.ClipboardRetentionDays, 1, 365);

        var saved = await _repository
            .UpsertEntryAsync(content, maxLimit, retentionDays, cancellationToken)
            .ConfigureAwait(false);

        if (saved is not null)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        return saved;
    }

    public async Task SetPinnedAsync(
        long id,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        await _repository.SetPinnedAsync(id, isPinned, cancellationToken).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteEntryAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await _repository.DeleteEntryAsync(id, cancellationToken).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearUnpinnedHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _repository.ClearUnpinnedAsync(cancellationToken).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAllHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _repository.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnMonitorClipboardTextCaptured(object? sender, string text)
    {
        try
        {
            _ = await CaptureTextAsync(text).ConfigureAwait(false);
        }
        catch
        {
            // Ignore non-fatal background clipboard persistence errors
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.ClipboardTextCaptured -= OnMonitorClipboardTextCaptured;
        _monitor.Dispose();
    }
}
