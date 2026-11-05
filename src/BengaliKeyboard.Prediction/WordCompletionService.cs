using System.Text;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Prediction;

/// <summary>
/// Provides real-time partial Bengali and English word completion in ≤ 50 ms off the UI thread (PRD.md Section 3.4 Capability 1).
/// Supports smart English capitalization (Title Case / ALL CAPS preservation) and common English contraction expansion.
/// </summary>
public sealed class WordCompletionService
{
    private static readonly IReadOnlyDictionary<string, string> EnglishContractions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["im"] = "I'm",
            ["ive"] = "I've",
            ["ill"] = "I'll",
            ["id"] = "I'd",
            ["dont"] = "don't",
            ["cant"] = "can't",
            ["wont"] = "won't",
            ["didnt"] = "didn't",
            ["doesnt"] = "doesn't",
            ["isnt"] = "isn't",
            ["arent"] = "aren't",
            ["wasnt"] = "wasn't",
            ["werent"] = "weren't",
            ["havent"] = "haven't",
            ["hasnt"] = "hasn't",
            ["wouldnt"] = "wouldn't",
            ["couldnt"] = "couldn't",
            ["shouldnt"] = "shouldn't",
            ["its"] = "it's",
            ["thats"] = "that's",
            ["whats"] = "what's",
            ["lets"] = "let's",
            ["youre"] = "you're",
            ["theyre"] = "they're",
            ["were"] = "we're",
            ["weve"] = "we've"
        };

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
        var isEnglishPrefix = IsAsciiWord(normalizedPrefix);

        var matches = await _dictionaryService
            .QueryPrefixMatchesAsync(normalizedPrefix, clampedMax * 2, cancellationToken)
            .ConfigureAwait(false);

        var seenFormattedWords = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<SuggestionCandidate>(clampedMax);

        // 1. Check for exact English contraction match (e.g., "dont" or "don't" -> "don't", "im" or "i'm" -> "I'm")
        var strippedContractionKey = isEnglishPrefix
            ? normalizedPrefix.Replace("'", string.Empty, StringComparison.Ordinal)
            : normalizedPrefix;

        if (isEnglishPrefix && EnglishContractions.TryGetValue(strippedContractionKey, out var contraction))
        {
            var formattedContraction = FormatEnglishCompletionCasing(normalizedPrefix, contraction);
            if (seenFormattedWords.Add(formattedContraction))
            {
                candidates.Add(new SuggestionCandidate
                {
                    Word = formattedContraction,
                    Score = 1.0,
                    IsNextWordPrediction = false,
                    ReplacePrefixLength = replaceScalarLength
                });
            }
        }

        // 2. Add ranked dictionary & user-learned prefix matches
        if (matches.Count > 0)
        {
            var maxFrequency = Math.Max(1.0, matches.Max(m => m.Frequency));

            foreach (var (word, frequency, isUserWord) in matches)
            {
                if (candidates.Count >= clampedMax)
                {
                    break;
                }

                var formattedWord = isEnglishPrefix
                    ? FormatEnglishCompletionCasing(normalizedPrefix, word)
                    : word;

                if (!seenFormattedWords.Add(formattedWord))
                {
                    continue;
                }

                var baseScore = frequency / maxFrequency;
                if (isUserWord)
                {
                    baseScore = Math.Min(1.0, baseScore + 0.10);
                }

                candidates.Add(new SuggestionCandidate
                {
                    Word = formattedWord,
                    Score = Math.Round(Math.Clamp(baseScore, 0.05, 1.0), 4),
                    IsNextWordPrediction = false,
                    ReplacePrefixLength = replaceScalarLength
                });
            }
        }

        // 3. Also surface contraction prefix matches for 3+ character English prefixes (e.g., "don" or "don'" -> "don't")
        if (isEnglishPrefix && strippedContractionKey.Length >= 3 && candidates.Count < clampedMax)
        {
            foreach (var (key, contractionValue) in EnglishContractions)
            {
                if (candidates.Count >= clampedMax)
                {
                    break;
                }

                if (key.StartsWith(strippedContractionKey, StringComparison.OrdinalIgnoreCase))
                {
                    var formattedContraction = FormatEnglishCompletionCasing(normalizedPrefix, contractionValue);
                    if (seenFormattedWords.Add(formattedContraction))
                    {
                        candidates.Add(new SuggestionCandidate
                        {
                            Word = formattedContraction,
                            Score = 0.82,
                            IsNextWordPrediction = false,
                            ReplacePrefixLength = replaceScalarLength
                        });
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            // Always surface at least the literal prefix so the user can commit their custom word with 1 click
            var fallbackWord = string.Equals(normalizedPrefix, "i", StringComparison.OrdinalIgnoreCase)
                ? "I"
                : normalizedPrefix;

            return
            [
                new SuggestionCandidate
                {
                    Word = fallbackWord,
                    Score = 1.0,
                    IsNextWordPrediction = false,
                    ReplacePrefixLength = replaceScalarLength
                }
            ];
        }

        return candidates;
    }

    public static string FormatEnglishCompletionCasing(string userPrefix, string candidateWord)
    {
        if (string.IsNullOrEmpty(candidateWord) || string.IsNullOrEmpty(userPrefix))
        {
            return candidateWord;
        }

        if (string.Equals(candidateWord, "i", StringComparison.OrdinalIgnoreCase))
        {
            return "I";
        }

        if (candidateWord.StartsWith("I'", StringComparison.OrdinalIgnoreCase))
        {
            return "I'" + candidateWord[2..];
        }

        var hasUpperStart = char.IsAsciiLetterUpper(userPrefix[0]);
        if (!hasUpperStart)
        {
            return candidateWord;
        }

        // Check if user typed >= 2 letters in ALL CAPS (e.g. "HEL" -> "HELLO")
        var letterCount = 0;
        var allUpper = true;
        foreach (var ch in userPrefix)
        {
            if (char.IsAsciiLetter(ch))
            {
                letterCount++;
                if (!char.IsAsciiLetterUpper(ch))
                {
                    allUpper = false;
                    break;
                }
            }
        }

        if (letterCount >= 2 && allUpper)
        {
            return candidateWord.ToUpperInvariant();
        }

        // Title Case preservation (e.g. "Hel" -> "Hello", "Ban" -> "Bangladesh")
        return char.ToUpperInvariant(candidateWord[0]) + candidateWord[1..];
    }

    public static bool IsAsciiWord(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch > 0x7F)
            {
                return false;
            }
        }

        return true;
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
