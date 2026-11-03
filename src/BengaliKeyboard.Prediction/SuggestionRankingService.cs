using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Prediction;

/// <summary>
/// Implements <see cref="IPredictionService"/> by orchestrating <see cref="DictionaryService"/>,
/// <see cref="WordCompletionService"/>, and <see cref="NextWordPredictionService"/>.
/// </summary>
public sealed class SuggestionRankingService : IPredictionService
{
    private readonly DictionaryService _dictionaryService;
    private readonly WordCompletionService _wordCompletionService;
    private readonly NextWordPredictionService _nextWordPredictionService;

    public SuggestionRankingService(
        DictionaryService dictionaryService,
        WordCompletionService wordCompletionService,
        NextWordPredictionService nextWordPredictionService)
    {
        ArgumentNullException.ThrowIfNull(dictionaryService);
        ArgumentNullException.ThrowIfNull(wordCompletionService);
        ArgumentNullException.ThrowIfNull(nextWordPredictionService);

        _dictionaryService = dictionaryService;
        _wordCompletionService = wordCompletionService;
        _nextWordPredictionService = nextWordPredictionService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _dictionaryService.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SuggestionCandidate>> GetWordCompletionsAsync(
        string prefix,
        int maxResults = 4,
        CancellationToken cancellationToken = default)
    {
        return await _wordCompletionService
            .GetCompletionsAsync(prefix, maxResults, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SuggestionCandidate>> GetNextWordPredictionsAsync(
        string? previousWord1,
        string? previousWord2 = null,
        int maxResults = 4,
        CancellationToken cancellationToken = default)
    {
        return await _nextWordPredictionService
            .GetNextWordsAsync(previousWord1, previousWord2, maxResults, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordAcceptedWordAsync(
        string word,
        string? previousWord = null,
        CancellationToken cancellationToken = default)
    {
        await _dictionaryService.RecordUserWordAsync(word, cancellationToken).ConfigureAwait(false);
    }
}
