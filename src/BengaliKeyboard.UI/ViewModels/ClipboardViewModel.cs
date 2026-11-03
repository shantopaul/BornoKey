using System.Collections.ObjectModel;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BengaliKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel driving <c>ClipboardWindow</c> with pinned-first ordering (<c>📌</c>),
/// instant real-time search, single-click Unicode paste into focused external applications,
/// individual pin/delete actions, clear unpinned history, and live monitoring pause/resume.
/// </summary>
public sealed partial class ClipboardViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboardService;
    private readonly ISettingsService _settingsService;
    private readonly ITextInjector? _textInjector;
    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    public ObservableCollection<ClipboardEntry> Entries { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isMonitoringEnabled;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event EventHandler<ClipboardEntry>? EntryInserted;

    public event EventHandler? BackToKeyboardRequested;

    public ClipboardViewModel(
        IClipboardService clipboardService,
        ISettingsService settingsService,
        ITextInjector? textInjector = null)
    {
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(settingsService);

        _clipboardService = clipboardService;
        _settingsService = settingsService;
        _textInjector = textInjector;
        _isMonitoringEnabled = _clipboardService.IsMonitoringEnabled;

        _clipboardService.HistoryChanged += OnClipboardServiceHistoryChanged;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoringEnabled = _clipboardService.IsMonitoringEnabled;
        await ReloadEntriesAsync(SearchQuery, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SearchEntriesAsync(string? query, CancellationToken cancellationToken = default)
    {
        await ReloadEntriesAsync(query, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public void InsertEntry(ClipboardEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.Content))
        {
            return;
        }

        _textInjector?.SendUnicodeText(entry.Content);
        EntryInserted?.Invoke(this, entry);
    }

    [RelayCommand]
    public async Task TogglePinAsync(ClipboardEntry? entry, CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            return;
        }

        var nextPinned = !entry.IsPinned;
        await _clipboardService.SetPinnedAsync(entry.Id, nextPinned, cancellationToken).ConfigureAwait(true);
        await ReloadEntriesAsync(SearchQuery, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task DeleteEntryAsync(ClipboardEntry? entry, CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            return;
        }

        await _clipboardService.DeleteEntryAsync(entry.Id, cancellationToken).ConfigureAwait(true);
        await ReloadEntriesAsync(SearchQuery, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ClearUnpinnedAsync(CancellationToken cancellationToken = default)
    {
        await _clipboardService.ClearUnpinnedHistoryAsync(cancellationToken).ConfigureAwait(true);
        await ReloadEntriesAsync(SearchQuery, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await _clipboardService.ClearAllHistoryAsync(cancellationToken).ConfigureAwait(true);
        await ReloadEntriesAsync(SearchQuery, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ToggleMonitoringAsync(CancellationToken cancellationToken = default)
    {
        var nextState = !IsMonitoringEnabled;
        _clipboardService.IsMonitoringEnabled = nextState;
        IsMonitoringEnabled = nextState;

        _settingsService.Current.EnableClipboardHistory = nextState;
        await _settingsService.SaveAsync(_settingsService.Current, cancellationToken).ConfigureAwait(true);
        UpdateStatusMessage();
    }

    [RelayCommand]
    public void BackToKeyboard()
    {
        BackToKeyboardRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = RunLatestSearchAsync(value, token);
    }

    private async Task RunLatestSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await ReloadEntriesAsync(query, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search query
        }
    }

    private async Task ReloadEntriesAsync(string? query, CancellationToken cancellationToken)
    {
        var limit = _settingsService.Current.ClipboardHistoryLimit;
        var items = await _clipboardService
            .GetHistoryAsync(query, limit, cancellationToken)
            .ConfigureAwait(true);

        ApplyEntries(items);
    }

    private void ApplyEntries(IReadOnlyList<ClipboardEntry> items)
    {
        Entries.Clear();
        foreach (var item in items)
        {
            Entries.Add(item);
        }

        UpdateStatusMessage();
    }

    private void UpdateStatusMessage()
    {
        var monitoringTag = IsMonitoringEnabled ? "Monitoring On" : "Monitoring Paused";
        if (Entries.Count == 0)
        {
            StatusMessage = !string.IsNullOrWhiteSpace(SearchQuery)
                ? $"No clips matching \"{SearchQuery.Trim()}\" ({monitoringTag})"
                : $"Clipboard history is empty ({monitoringTag})";
            return;
        }

        var pinnedCount = Entries.Count(x => x.IsPinned);
        StatusMessage = pinnedCount > 0
            ? $"{Entries.Count} clips ({pinnedCount} pinned • {monitoringTag})"
            : $"{Entries.Count} clips ({monitoringTag})";
    }

    private void OnClipboardServiceHistoryChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(async () => await ReloadEntriesAsync(SearchQuery, CancellationToken.None).ConfigureAwait(true));
            return;
        }

        _ = ReloadEntriesAsync(SearchQuery, CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clipboardService.HistoryChanged -= OnClipboardServiceHistoryChanged;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
