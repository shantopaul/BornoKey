using System.Globalization;
using System.Text;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Storage.Database;

namespace BengaliKeyboard.Emoji;

/// <summary>
/// Implements <see cref="IEmojiService"/> with offline catalog browsing, real-time keyword search,
/// and SQLite-backed Frequently Used emoji tracking (<c>EmojiUsage</c> table in <c>keyboard.db</c>).
/// </summary>
public sealed class EmojiUsageService : IEmojiService
{
    private const int MaxEmojiGlyphLength = 32;

    private readonly EmojiRepository _repository;
    private readonly EmojiSearchService _searchService;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISettingsService _settingsService;

    public EmojiUsageService(
        EmojiRepository repository,
        EmojiSearchService searchService,
        SqliteConnectionFactory connectionFactory,
        ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(settingsService);

        _repository = repository;
        _searchService = searchService;
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
    }

    public IReadOnlyList<string> GetCategories() => _repository.GetCategories();

    public async Task<IReadOnlyList<EmojiItem>> GetByCategoryAsync(
        string category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        if (string.Equals(category.Trim(), EmojiRepository.FrequentlyUsedCategory, StringComparison.OrdinalIgnoreCase))
        {
            var configuredLimit = _settingsService.Current.EmojiHistoryLimit;
            return await GetFrequentlyUsedAsync(configuredLimit, cancellationToken).ConfigureAwait(false);
        }

        var categoryItems = _repository.GetByCategory(category);
        if (categoryItems.Count == 0)
        {
            return [];
        }

        var usageMap = await LoadUsageMapAsync(cancellationToken).ConfigureAwait(false);
        ApplyUsageMetadata(categoryItems, usageMap);
        return categoryItems;
    }

    public async Task<IReadOnlyList<EmojiItem>> SearchAsync(
        string query,
        int maxResults = 60,
        CancellationToken cancellationToken = default)
    {
        var allItems = _repository.GetAll();
        var usageMap = await LoadUsageMapAsync(cancellationToken).ConfigureAwait(false);
        ApplyUsageMetadata(allItems, usageMap);

        return _searchService.Search(allItems, query, maxResults);
    }

    public async Task<IReadOnlyList<EmojiItem>> GetFrequentlyUsedAsync(
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        var effectiveLimit = limit > 0
            ? Math.Clamp(limit, 1, 200)
            : Math.Clamp(_settingsService.Current.EmojiHistoryLimit, 10, 200);

        var usageRecords = await LoadUsageMapAsync(cancellationToken).ConfigureAwait(false);
        if (usageRecords.Count == 0)
        {
            return [];
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var rankedItems = new List<(EmojiItem Item, double CompositeScore)>(usageRecords.Count);

        foreach (var (emoji, (usageCount, lastUsedUtc)) in usageRecords)
        {
            if (!_repository.TryGetByEmoji(emoji, out var item) || item is null)
            {
                item = new EmojiItem
                {
                    Emoji = emoji,
                    Name = emoji,
                    Category = EmojiRepository.FrequentlyUsedCategory
                };
            }

            item.UsageCount = usageCount;
            item.LastUsedUtc = lastUsedUtc;

            var score = CalculateCompositeFrequencyRecencyScore(usageCount, lastUsedUtc, nowUtc);
            rankedItems.Add((item, score));
        }

        return rankedItems
            .OrderByDescending(x => x.CompositeScore)
            .ThenByDescending(x => x.Item.UsageCount)
            .ThenByDescending(x => x.Item.LastUsedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Item.Emoji, StringComparer.Ordinal)
            .Take(effectiveLimit)
            .Select(x => x.Item)
            .ToList();
    }

    public async Task RecordUsageAsync(
        string emoji,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return;
        }

        var normalizedEmoji = emoji.Trim().Normalize(NormalizationForm.FormC);
        if (normalizedEmoji.Length == 0 || normalizedEmoji.Length > MaxEmojiGlyphLength)
        {
            return;
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var maxRetainedRows = Math.Clamp(_settingsService.Current.EmojiHistoryLimit, 10, 200);

        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var upsertCmd = connection.CreateCommand())
                {
                    upsertCmd.CommandText = """
                        INSERT INTO EmojiUsage (Emoji, UsageCount, LastUsedUtc)
                        VALUES (@emoji, 1, @lastUsedUtc)
                        ON CONFLICT(Emoji) DO UPDATE SET
                            UsageCount = EmojiUsage.UsageCount + 1,
                            LastUsedUtc = excluded.LastUsedUtc;
                        """;
                    upsertCmd.Parameters.AddWithValue("@emoji", normalizedEmoji);
                    upsertCmd.Parameters.AddWithValue("@lastUsedUtc", nowIso);
                    await upsertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Prune oldest low-frequency entries beyond the configured history limit
                await using (var pruneCmd = connection.CreateCommand())
                {
                    pruneCmd.CommandText = """
                        DELETE FROM EmojiUsage
                        WHERE Emoji NOT IN (
                            SELECT Emoji
                            FROM EmojiUsage
                            ORDER BY UsageCount DESC, LastUsedUtc DESC
                            LIMIT @maxLimit
                        );
                        """;
                    pruneCmd.Parameters.AddWithValue("@maxLimit", maxRetainedRows);
                    await pruneCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.CommandText = "DELETE FROM EmojiUsage;";
                    await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Flush WAL pages immediately so cleared emoji usage leaves no residual WAL frames (CHK-10)
                await using (var checkpointCmd = connection.CreateCommand())
                {
                    checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    await checkpointCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the composite Frequency + Recency score for Frequently Used ranking (PRD.md Section 3.5).
    /// Higher usage count dominates while recent usage breaks ties and boosts actively used emojis.
    /// </summary>
    public static double CalculateCompositeFrequencyRecencyScore(
        int usageCount,
        DateTimeOffset? lastUsedUtc,
        DateTimeOffset referenceNowUtc)
    {
        var safeCount = Math.Max(1, usageCount);
        if (!lastUsedUtc.HasValue)
        {
            return safeCount;
        }

        var elapsedHours = Math.Max(0.0, (referenceNowUtc - lastUsedUtc.Value).TotalHours);
        var recencyBoost = 1.0 / (1.0 + (elapsedHours / 24.0));
        return (safeCount * 10.0) + (recencyBoost * 5.0);
    }

    private async Task<Dictionary<string, (int UsageCount, DateTimeOffset? LastUsedUtc)>> LoadUsageMapAsync(
        CancellationToken cancellationToken)
    {
        return await SqliteConnectionFactory.ExecuteWithRetryAsync(
            async ct =>
            {
                var map = new Dictionary<string, (int UsageCount, DateTimeOffset? LastUsedUtc)>(StringComparer.Ordinal);

                await using var connection = await _connectionFactory
                    .CreateUserDatabaseConnectionAsync(ct)
                    .ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Emoji, UsageCount, LastUsedUtc
                    FROM EmojiUsage
                    ORDER BY UsageCount DESC, LastUsedUtc DESC;
                    """;

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var emoji = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    var rawTimestamp = reader.IsDBNull(2) ? null : reader.GetString(2);

                    DateTimeOffset? parsedTimestamp = null;
                    if (!string.IsNullOrWhiteSpace(rawTimestamp) &&
                        DateTimeOffset.TryParse(
                            rawTimestamp,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var dto))
                    {
                        parsedTimestamp = dto;
                    }

                    map[emoji] = (Math.Max(1, count), parsedTimestamp);
                }

                return map;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyUsageMetadata(
        IEnumerable<EmojiItem> items,
        IReadOnlyDictionary<string, (int UsageCount, DateTimeOffset? LastUsedUtc)> usageMap)
    {
        if (usageMap.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            if (usageMap.TryGetValue(item.Emoji, out var usage))
            {
                item.UsageCount = usage.UsageCount;
                item.LastUsedUtc = usage.LastUsedUtc;
            }
        }
    }
}
