using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Events;

/// <summary>
/// Broadcast when the active word completion or next-word suggestion list updates.
/// </summary>
public sealed record SuggestionsUpdatedMessage(IReadOnlyList<SuggestionCandidate> Candidates);
