using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Event-driven privacy-first clipboard history service.
/// </summary>
public interface IClipboardService
{
    bool IsMonitoringEnabled { get; set; }

    event EventHandler? HistoryChanged;

    Task<IReadOnlyList<ClipboardEntry>> GetHistoryAsync(
        string? searchQuery = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<ClipboardEntry?> CaptureTextAsync(
        string content,
        CancellationToken cancellationToken = default);

    Task SetPinnedAsync(
        long id,
        bool isPinned,
        CancellationToken cancellationToken = default);

    Task DeleteEntryAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task ClearUnpinnedHistoryAsync(CancellationToken cancellationToken = default);

    Task ClearAllHistoryAsync(CancellationToken cancellationToken = default);
}
