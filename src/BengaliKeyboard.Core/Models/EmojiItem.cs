namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Metadata and usage tracking model for a Unicode emoji entry.
/// </summary>
public sealed class EmojiItem
{
    public string Emoji { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public int UsageCount { get; set; }

    public DateTimeOffset? LastUsedUtc { get; set; }
}
