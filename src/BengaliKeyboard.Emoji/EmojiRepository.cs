using System.IO;
using System.Reflection;
using System.Text.Json;
using BengaliKeyboard.Core.Models;
using Serilog;

namespace BengaliKeyboard.Emoji;

/// <summary>
/// Loads and indexes the offline emoji catalog from <c>data/Emoji/emoji_catalog.json</c>
/// with embedded resource fallback if the external file is missing or corrupted (Rules.md Scenario 5).
/// </summary>
public sealed class EmojiRepository
{
    public const string FrequentlyUsedCategory = "Frequently Used";
    public const long MaxCatalogFileBytes = 512 * 1024; // 512 KB cap (CHK-06)
    private const string EmbeddedCatalogResourceName = "BengaliKeyboard.Emoji.data.Emoji.emoji_catalog.json";

    private static readonly IReadOnlyList<string> OrderedCategories =
    [
        FrequentlyUsedCategory,
        "Smileys & Emotions",
        "People & Body",
        "Animals & Nature",
        "Food & Drink",
        "Travel & Places",
        "Activities",
        "Objects",
        "Symbols",
        "Flags"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16
    };

    private readonly string? _customCatalogFilePath;
    private readonly object _syncLock = new();
    private List<EmojiItem>? _allEmojis;
    private Dictionary<string, EmojiItem>? _byEmoji;
    private Dictionary<string, List<EmojiItem>>? _byCategory;

    public EmojiRepository(string? customCatalogFilePath = null)
    {
        _customCatalogFilePath = customCatalogFilePath;
    }

    /// <summary>
    /// Returns the 10 standard emoji categories defined in PRD.md Section 3.5.
    /// </summary>
    public IReadOnlyList<string> GetCategories() => OrderedCategories;

    /// <summary>
    /// Returns cloned instances of all emojis in the offline catalog.
    /// </summary>
    public IReadOnlyList<EmojiItem> GetAll()
    {
        EnsureLoaded();
        return _allEmojis!.Select(CloneItem).ToList();
    }

    /// <summary>
    /// Returns cloned instances of all emojis belonging to the specified static category.
    /// </summary>
    public IReadOnlyList<EmojiItem> GetByCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        EnsureLoaded();
        if (_byCategory!.TryGetValue(category.Trim(), out var list))
        {
            return list.Select(CloneItem).ToList();
        }

        return [];
    }

    /// <summary>
    /// Looks up a catalog emoji by its Unicode glyph sequence.
    /// </summary>
    public bool TryGetByEmoji(string emoji, out EmojiItem? item)
    {
        item = null;
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return false;
        }

        EnsureLoaded();
        if (_byEmoji!.TryGetValue(emoji.Trim(), out var found))
        {
            item = CloneItem(found);
            return true;
        }

        return false;
    }

    private void EnsureLoaded()
    {
        if (_allEmojis is not null)
        {
            return;
        }

        lock (_syncLock)
        {
            if (_allEmojis is not null)
            {
                return;
            }

            var loaded = LoadCatalogItems();
            var allList = new List<EmojiItem>(loaded.Count);
            var emojiMap = new Dictionary<string, EmojiItem>(StringComparer.Ordinal);
            var categoryMap = new Dictionary<string, List<EmojiItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in loaded)
            {
                if (string.IsNullOrWhiteSpace(raw.Emoji) || string.IsNullOrWhiteSpace(raw.Category))
                {
                    continue;
                }

                var normalizedEmoji = raw.Emoji.Trim();
                if (emojiMap.ContainsKey(normalizedEmoji))
                {
                    continue;
                }

                var sanitized = new EmojiItem
                {
                    Emoji = normalizedEmoji,
                    Name = (raw.Name ?? string.Empty).Trim(),
                    Category = raw.Category.Trim(),
                    Keywords = raw.Keywords?
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Select(k => k.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? [],
                    Aliases = raw.Aliases?
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Select(a => a.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? []
                };

                allList.Add(sanitized);
                emojiMap[sanitized.Emoji] = sanitized;

                if (!categoryMap.TryGetValue(sanitized.Category, out var bucket))
                {
                    bucket = [];
                    categoryMap[sanitized.Category] = bucket;
                }

                bucket.Add(sanitized);
            }

            _allEmojis = allList;
            _byEmoji = emojiMap;
            _byCategory = categoryMap;
        }
    }

    private List<EmojiItem> LoadCatalogItems()
    {
        foreach (var candidatePath in GetCandidateCatalogPaths())
        {
            try
            {
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                var info = new FileInfo(candidatePath);
                if (info.Length <= 0 || info.Length > MaxCatalogFileBytes)
                {
                    Log.Warning(
                        "Emoji catalog at {CatalogPath} has invalid size ({Length} bytes); falling back.",
                        candidatePath,
                        info.Length);
                    continue;
                }

                var json = File.ReadAllText(candidatePath);
                var parsed = JsonSerializer.Deserialize<List<EmojiItem>>(json, SerializerOptions);
                if (parsed is { Count: > 0 })
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read emoji catalog from {CatalogPath}; falling back to embedded resource.", candidatePath);
            }
        }

        return LoadFromEmbeddedResource();
    }

    private IEnumerable<string> GetCandidateCatalogPaths()
    {
        if (!string.IsNullOrWhiteSpace(_customCatalogFilePath))
        {
            yield return _customCatalogFilePath;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "data", "Emoji", "emoji_catalog.json");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "Emoji", "emoji_catalog.json");
    }

    private static List<EmojiItem> LoadFromEmbeddedResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(EmbeddedCatalogResourceName);
            if (stream is null)
            {
                return [];
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<List<EmojiItem>>(json, SerializerOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load embedded fallback emoji catalog.");
            return [];
        }
    }

    public static EmojiItem CloneItem(EmojiItem source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new EmojiItem
        {
            Emoji = source.Emoji,
            Name = source.Name,
            Category = source.Category,
            Keywords = source.Keywords,
            Aliases = source.Aliases,
            UsageCount = source.UsageCount,
            LastUsedUtc = source.LastUsedUtc
        };
    }
}
