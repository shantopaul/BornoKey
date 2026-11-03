using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Emoji;

/// <summary>
/// Provides instant offline keyword, name, alias, and Bengali/English tag search across the emoji catalog.
/// </summary>
public sealed class EmojiSearchService
{
    /// <summary>
    /// Searches the supplied emoji items by query string and returns ranked matches up to <paramref name="maxResults"/>.
    /// </summary>
    public IReadOnlyList<EmojiItem> Search(
        IEnumerable<EmojiItem> catalogItems,
        string? query,
        int maxResults = 60)
    {
        ArgumentNullException.ThrowIfNull(catalogItems);
        var clampedMax = Math.Clamp(maxResults, 1, 250);

        if (string.IsNullOrWhiteSpace(query))
        {
            return catalogItems
                .Take(clampedMax)
                .Select(EmojiRepository.CloneItem)
                .ToList();
        }

        var normalizedQuery = query.Trim();
        var tokens = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return [];
        }

        var scored = new List<(EmojiItem Item, double Score, int OriginalIndex)>();
        var index = 0;

        foreach (var item in catalogItems)
        {
            var currentIndex = index++;
            var score = ComputeMatchScore(item, normalizedQuery, tokens);
            if (score > 0)
            {
                scored.Add((EmojiRepository.CloneItem(item), score, currentIndex));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.UsageCount)
            .ThenBy(x => x.OriginalIndex)
            .Take(clampedMax)
            .Select(x => x.Item)
            .ToList();
    }

    private static double ComputeMatchScore(EmojiItem item, string fullQuery, string[] tokens)
    {
        // Direct emoji glyph match
        if (string.Equals(item.Emoji, fullQuery, StringComparison.Ordinal))
        {
            return 500.0 + Math.Min(item.UsageCount, 50);
        }

        var totalScore = 0.0;

        foreach (var token in tokens)
        {
            var tokenScore = ComputeSingleTokenScore(item, token);
            if (tokenScore <= 0)
            {
                // All search tokens must match at least one field of the emoji item
                return 0.0;
            }

            totalScore += tokenScore;
        }

        // Usage frequency boost so the user's favorite matching emojis surface first
        var usageBonus = Math.Min(item.UsageCount, 50) * 0.5;
        return totalScore + usageBonus;
    }

    private static double ComputeSingleTokenScore(EmojiItem item, string token)
    {
        var strippedAliasToken = token.Trim(':');

        // 1. Exact keyword match (highest keyword priority, e.g. "smile", "heart", "food", "fire")
        foreach (var keyword in item.Keywords)
        {
            if (string.Equals(keyword, token, StringComparison.OrdinalIgnoreCase))
            {
                return 120.0;
            }
        }

        // 2. Exact alias match (e.g. ":smile:" or "smile")
        foreach (var alias in item.Aliases)
        {
            var cleanAlias = alias.Trim(':');
            if (string.Equals(alias, token, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanAlias, strippedAliasToken, StringComparison.OrdinalIgnoreCase))
            {
                return 115.0;
            }
        }

        // 3. Keyword prefix match
        foreach (var keyword in item.Keywords)
        {
            if (keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return 90.0;
            }
        }

        // 4. Name exact or word-prefix match
        if (string.Equals(item.Name, token, StringComparison.OrdinalIgnoreCase))
        {
            return 100.0;
        }

        if (item.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return 80.0;
        }

        var nameWords = item.Name.Split(' ', '-', '_');
        foreach (var word in nameWords)
        {
            if (word.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return 70.0;
            }
        }

        // 5. Substring match in keywords, aliases, name, or category
        foreach (var keyword in item.Keywords)
        {
            if (keyword.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return 45.0;
            }
        }

        foreach (var alias in item.Aliases)
        {
            if (alias.Contains(strippedAliasToken, StringComparison.OrdinalIgnoreCase))
            {
                return 40.0;
            }
        }

        if (item.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 35.0;
        }

        if (item.Category.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 20.0;
        }

        return 0.0;
    }
}
