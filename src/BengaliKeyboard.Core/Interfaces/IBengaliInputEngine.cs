using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Core Bengali & English input engine shared by both virtual on-screen keys and physical keyboard hooks.
/// </summary>
public interface IBengaliInputEngine
{
    LanguageMode CurrentLanguage { get; }

    string CurrentWordBuffer { get; }

    void SetLanguage(LanguageMode language);

    void ToggleLanguage();

    Task ProcessKeyAsync(KeyDefinition key, bool isShifted = false, CancellationToken cancellationToken = default);

    Task CommitCandidateAsync(SuggestionCandidate candidate, CancellationToken cancellationToken = default);

    void ResetBuffer();
}
