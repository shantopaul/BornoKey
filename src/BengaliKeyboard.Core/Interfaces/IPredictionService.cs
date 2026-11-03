using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Offline Bengali word completion and context-aware bigram/trigram next-word prediction service.
/// </summary>
public interface IPredictionService
{
    Task<IReadOnlyList<SuggestionCandidate>> GetWordCompletionsAsync(
        string prefix,
        int maxResults = 4,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SuggestionCandidate>> GetNextWordPredictionsAsync(
        string? previousWord1,
        string? previousWord2 = null,
        int maxResults = 4,
        CancellationToken cancellationToken = default);

    Task RecordAcceptedWordAsync(
        string word,
        string? previousWord = null,
        CancellationToken cancellationToken = default);
}
