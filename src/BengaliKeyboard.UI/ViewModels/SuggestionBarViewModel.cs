using System.Collections.ObjectModel;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BengaliKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel managing the 3–5 ranked word completion and next-word prediction candidates
/// displayed in <c>SuggestionBarControl</c> directly above the top key row (PRD.md Section 3.4).
/// Automatically switches starter suggestions between Bengali and English modes.
/// </summary>
public sealed partial class SuggestionBarViewModel :
    ObservableObject,
    IRecipient<SuggestionsUpdatedMessage>,
    IRecipient<LanguageChangedMessage>,
    IDisposable
{
    private readonly IBengaliInputEngine _inputEngine;
    private readonly IMessenger _messenger;
    private bool _disposed;

    public ObservableCollection<SuggestionCandidate> Candidates { get; } = [];

    [ObservableProperty]
    private bool _hasCandidates;

    public SuggestionBarViewModel(
        IBengaliInputEngine inputEngine,
        IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(inputEngine);

        _inputEngine = inputEngine;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _messenger.RegisterAll(this);

        ApplyDefaultCandidatesForLanguage(_inputEngine.CurrentLanguage);
    }

    [RelayCommand]
    public async Task SelectCandidateAsync(SuggestionCandidate? candidate, CancellationToken cancellationToken = default)
    {
        if (candidate is null)
        {
            return;
        }

        await _inputEngine.CommitCandidateAsync(candidate, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SelectCandidateByIndexAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= Candidates.Count)
        {
            return;
        }

        await _inputEngine.CommitCandidateAsync(Candidates[index], cancellationToken).ConfigureAwait(true);
    }

    public void Receive(SuggestionsUpdatedMessage message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyCandidates(message.Candidates));
            return;
        }

        ApplyCandidates(message.Candidates);
    }

    public void Receive(LanguageChangedMessage message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyDefaultCandidatesForLanguage(message.Language));
            return;
        }

        ApplyDefaultCandidatesForLanguage(message.Language);
    }

    private void ApplyDefaultCandidatesForLanguage(LanguageMode language)
    {
        if (language == LanguageMode.English)
        {
            ApplyCandidates(
            [
                new SuggestionCandidate { Word = "I", Score = 1.0, ReplacePrefixLength = 0, IsNextWordPrediction = true },
                new SuggestionCandidate { Word = "Hello", Score = 0.95, ReplacePrefixLength = 0, IsNextWordPrediction = true },
                new SuggestionCandidate { Word = "Thank", Score = 0.90, ReplacePrefixLength = 0, IsNextWordPrediction = true },
                new SuggestionCandidate { Word = "The", Score = 0.85, ReplacePrefixLength = 0, IsNextWordPrediction = true }
            ]);
            return;
        }

        ApplyCandidates(
        [
            new SuggestionCandidate { Word = "আমার", Score = 1.0, ReplacePrefixLength = 0, IsNextWordPrediction = true },
            new SuggestionCandidate { Word = "আমি", Score = 0.95, ReplacePrefixLength = 0, IsNextWordPrediction = true },
            new SuggestionCandidate { Word = "বাংলা", Score = 0.90, ReplacePrefixLength = 0, IsNextWordPrediction = true },
            new SuggestionCandidate { Word = "ভালো", Score = 0.85, ReplacePrefixLength = 0, IsNextWordPrediction = true }
        ]);
    }

    private void ApplyCandidates(IReadOnlyList<SuggestionCandidate> candidates)
    {
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            Candidates.Add(candidate);
        }

        HasCandidates = Candidates.Count > 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messenger.UnregisterAll(this);
    }
}
