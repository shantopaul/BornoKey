using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Offline emoji search, category retrieval, and SQLite-backed Frequently Used history service.
/// </summary>
public interface IEmojiService
{
    IReadOnlyList<string> GetCategories();

    Task<IReadOnlyList<EmojiItem>> GetByCategoryAsync(
        string category,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmojiItem>> SearchAsync(
        string query,
        int maxResults = 60,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmojiItem>> GetFrequentlyUsedAsync(
        int limit = 40,
        CancellationToken cancellationToken = default);

    Task RecordUsageAsync(
        string emoji,
        CancellationToken cancellationToken = default);

    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
}
