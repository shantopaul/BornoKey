namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Represents a ranked word completion or next-word prediction candidate shown in the suggestion bar.
/// </summary>
public sealed class SuggestionCandidate
{
    public string Word { get; init; } = string.Empty;

    public double Score { get; init; }

    public bool IsNextWordPrediction { get; init; }

    public int ReplacePrefixLength { get; init; }

    /// <summary>
    /// Privacy-safe string representation that avoids logging user-typed words (SEC-04 / CHK-07).
    /// </summary>
    public override string ToString() =>
        $"SuggestionCandidate[IsNextWord={IsNextWordPrediction}, Score={Score:F2}, ReplaceLen={ReplacePrefixLength}, WordLen={Word?.Length ?? 0}]";
}
