namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Represents a single item in the user's local clipboard history.
/// </summary>
public sealed class ClipboardEntry
{
    public const int MaxAllowedContentCharacters = 65_536;
    public const int PreviewMaxCharacters = 140;

    public long Id { get; init; }

    public string Content { get; init; } = string.Empty;

    public string ContentHash { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsPinned { get; set; }

    /// <summary>
    /// Compact single-line preview for UI rendering without modifying the underlying content.
    /// </summary>
    public string PreviewText
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
            {
                return string.Empty;
            }

            var normalized = Content
                .Replace("\r\n", " ↵ ", StringComparison.Ordinal)
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();

            return normalized.Length <= PreviewMaxCharacters
                ? normalized
                : string.Concat(normalized.AsSpan(0, PreviewMaxCharacters), "…");
        }
    }

    /// <summary>
    /// Privacy-safe string representation that strictly redacts Content and ContentHash from logs (SEC-04 / CHK-07).
    /// </summary>
    public override string ToString() =>
        $"ClipboardEntry[Id={Id}, IsPinned={IsPinned}, Length={Content?.Length ?? 0}, CreatedUtc={CreatedUtc:O}]";
}
