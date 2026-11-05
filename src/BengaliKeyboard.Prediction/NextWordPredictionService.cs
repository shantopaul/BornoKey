using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Prediction;

/// <summary>
/// Context-aware next-word prediction service using a Trigram → Bigram → Unigram backoff model
/// for both Bengali and English contexts (PRD.md Section 3.4 Capability 2).
/// </summary>
public sealed class NextWordPredictionService
{
    private readonly NGramModel _nGramModel;

    public NextWordPredictionService(NGramModel nGramModel)
    {
        ArgumentNullException.ThrowIfNull(nGramModel);
        _nGramModel = nGramModel;
    }

    public async Task<IReadOnlyList<SuggestionCandidate>> GetNextWordsAsync(
        string? previousWord1,
        string? previousWord2 = null,
        int maxResults = 4,
        CancellationToken cancellationToken = default)
    {
        var clampedMax = Math.Clamp(maxResults, 1, 10);
        var seenWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<SuggestionCandidate>(clampedMax);

        var hasW1 = !string.IsNullOrWhiteSpace(previousWord1);
        var hasW2 = !string.IsNullOrWhiteSpace(previousWord2);
        var isEnglishContext = (hasW2 && WordCompletionService.IsAsciiWord(previousWord2!.Trim()))
            || (hasW1 && WordCompletionService.IsAsciiWord(previousWord1!.Trim()));

        // 1. Trigram lookup when two context words are present
        if (hasW1 && hasW2)
        {
            // Support both chronological ("আমি", "বাংলা" / "how", "are") and reverse-chronological call conventions
            var trigramMatches = await _nGramModel
                .QueryTrigramsAsync(previousWord1!, previousWord2!, clampedMax, cancellationToken)
                .ConfigureAwait(false);

            if (trigramMatches.Count == 0)
            {
                trigramMatches = await _nGramModel
                    .QueryTrigramsAsync(previousWord2!, previousWord1!, clampedMax, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (trigramMatches.Count > 0)
            {
                var maxTrigramFreq = Math.Max(1.0, trigramMatches.Max(x => x.Frequency));
                foreach (var (word, freq) in trigramMatches)
                {
                    if (seenWords.Add(word))
                    {
                        ranked.Add(new SuggestionCandidate
                        {
                            Word = word,
                            Score = Math.Round(Math.Clamp(freq / maxTrigramFreq, 0.1, 1.0), 4),
                            IsNextWordPrediction = true,
                            ReplacePrefixLength = 0
                        });

                        if (ranked.Count >= clampedMax)
                        {
                            return ranked;
                        }
                    }
                }
            }
        }

        // 2. Bigram backoff on the most immediate context word
        string? primaryBigramContext = hasW1 && hasW2 ? previousWord2 : (hasW1 ? previousWord1 : previousWord2);
        string? secondaryBigramContext = hasW1 && hasW2 ? previousWord1 : null;

        if (!string.IsNullOrWhiteSpace(primaryBigramContext))
        {
            var bigramMatches = await _nGramModel
                .QueryBigramsAsync(primaryBigramContext, clampedMax, cancellationToken)
                .ConfigureAwait(false);

            if (bigramMatches.Count == 0 && !string.IsNullOrWhiteSpace(secondaryBigramContext))
            {
                bigramMatches = await _nGramModel
                    .QueryBigramsAsync(secondaryBigramContext, clampedMax, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (bigramMatches.Count > 0)
            {
                var maxBigramFreq = Math.Max(1.0, bigramMatches.Max(x => x.Frequency));
                foreach (var (word, freq) in bigramMatches)
                {
                    if (seenWords.Add(word))
                    {
                        ranked.Add(new SuggestionCandidate
                        {
                            Word = word,
                            Score = Math.Round(Math.Clamp((freq / maxBigramFreq) * 0.85, 0.1, 0.85), 4),
                            IsNextWordPrediction = true,
                            ReplacePrefixLength = 0
                        });

                        if (ranked.Count >= clampedMax)
                        {
                            return ranked;
                        }
                    }
                }
            }
        }

        // 3. Script-aware Unigram fallback if still under maxResults
        var unigramMatches = await _nGramModel
            .QueryTopUnigramsAsync(clampedMax * 2, preferEnglish: isEnglishContext, cancellationToken)
            .ConfigureAwait(false);

        if (unigramMatches.Count > 0)
        {
            var maxUniFreq = Math.Max(1.0, unigramMatches.Max(x => x.Frequency));
            foreach (var (word, freq) in unigramMatches)
            {
                if (seenWords.Add(word))
                {
                    ranked.Add(new SuggestionCandidate
                    {
                        Word = word,
                        Score = Math.Round(Math.Clamp((freq / maxUniFreq) * 0.60, 0.05, 0.60), 4),
                        IsNextWordPrediction = true,
                        ReplacePrefixLength = 0
                    });

                    if (ranked.Count >= clampedMax)
                    {
                        break;
                    }
                }
            }
        }

        return ranked;
    }
}
