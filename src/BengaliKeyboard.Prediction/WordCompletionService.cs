using System.Text;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Prediction;

/// <summary>
/// Provides real-time partial Bengali word completion in ≤ 50 ms off the UI thread (PRD.md Section 3.4 Capability 1).
/// Example: Typing <c>আমা</c> surfaces <c>[আমার] [আমাকে] [আমাদের] [আমায়]</c>.
/// </summary>
public sealed class WordCompletionService
{
    private readonly DictionaryService _dictionaryService;

    public WordCompletionService(DictionaryService dictionaryService)
    {
        ArgumentNullException.ThrowIfNull(dictionaryService);
        _dictionaryService = dictionaryService;
    }

    public async Task<IReadOnlyList<SuggestionCandidate>> GetCompletionsAsync(
        string prefix,
        int maxResults = 4,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return [];
        }

        var normalizedPrefix = prefix.Trim().Normalize(NormalizationForm.FormC);
        var clampedMax = Math.Clamp(maxResults, 1, 10);
        var replaceScalarLength = CountUnicodeScalars(normalizedPrefix);

        var matches = await _dictionaryService
            .QueryPrefixMatchesAsync(normalizedPrefix, clampedMax * 2, cancellationToken)
            .ConfigureAwait(false);

        if (matches.Count == 0)
        {
            // Always surface at least the literal prefix so the user can commit their custom word with 1 click
            return
            [
                new SuggestionCandidate
                {
                    Word = normalizedPrefix,
                    Score = 1.0,
                    IsNextWordPrediction = false,
                    ReplacePrefixLength = replaceScalarLength
                }
            ];
        }

        var maxFrequency = Math.Max(1.0, matches.Max(m => m.Frequency));
        var candidates = new List<SuggestionCandidate>(clampedMax);

        foreach (var (word, frequency, isUserWord) in matches)
        {
            var baseScore = frequency / maxFrequency;
            if (isUserWord)
            {
                baseScore = Math.Min(1.0, baseScore + 0.10);
            }

            candidates.Add(new SuggestionCandidate
            {
                Word = word,
                Score = Math.Round(Math.Clamp(baseScore, 0.05, 1.0), 4),
                IsNextWordPrediction = false,
                ReplacePrefixLength = replaceScalarLength
            });

            if (candidates.Count >= clampedMax)
            {
                break;
            }
        }

        return candidates;
    }

    private static int CountUnicodeScalars(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
