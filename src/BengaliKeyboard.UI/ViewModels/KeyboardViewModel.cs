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
/// Primary ViewModel driving KeyboardWindow, layout switching, language toggles, and suggestion selection.
/// </summary>
public sealed partial class KeyboardViewModel : ObservableObject, IRecipient<SuggestionsUpdatedMessage>, IDisposable
{
    private readonly ILayoutManager _layoutManager;
    private readonly ISettingsService _settingsService;
    private readonly IBengaliInputEngine? _inputEngine;
    private readonly IMessenger _messenger;
    private bool _disposed;

    [ObservableProperty]
    private KeyboardLayoutModel _currentLayout;

    [ObservableProperty]
    private LanguageMode _activeLanguage;

    [ObservableProperty]
    private LayoutLayer _activeLayer;

    public ObservableCollection<SuggestionCandidate> Suggestions { get; } = [];

    public event EventHandler<KeyDefinition>? KeyDispatched;

    public event EventHandler? EmojiPanelRequested;

    public event EventHandler? ClipboardPanelRequested;

    public KeyboardViewModel(
        ILayoutManager layoutManager,
        ISettingsService settingsService,
        IBengaliInputEngine? inputEngine = null,
        IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(layoutManager);
        ArgumentNullException.ThrowIfNull(settingsService);

        _layoutManager = layoutManager;
        _settingsService = settingsService;
        _inputEngine = inputEngine;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        _currentLayout = _layoutManager.CurrentLayout;
        _activeLanguage = _layoutManager.ActiveLanguage;
        _activeLayer = _layoutManager.ActiveLayer;

        _layoutManager.LayoutChanged += OnLayoutManagerLayoutChanged;
        _messenger.Register(this);

        InitializeDefaultSuggestions();
    }

    [RelayCommand]
    public async Task PressKeyAsync(KeyDefinition? key, CancellationToken cancellationToken = default)
    {
        if (key is null)
        {
            return;
        }

        switch (key.KeyType)
        {
            case KeyType.Shift:
                if (key.TargetLayer.HasValue)
                {
                    _layoutManager.SetLayer(key.TargetLayer.Value);
                }
                else
                {
                    _layoutManager.ToggleShift();
                }
                return;

            case KeyType.LanguageSwitch:
                await ToggleLanguageAsync(cancellationToken).ConfigureAwait(true);
                return;

            case KeyType.LayoutSwitch:
                _layoutManager.SetLayer(key.TargetLayer ?? LayoutLayer.Numbers);
                return;

            case KeyType.EmojiSwitch:
                EmojiPanelRequested?.Invoke(this, EventArgs.Empty);
                return;

            case KeyType.ClipboardSwitch:
                ClipboardPanelRequested?.Invoke(this, EventArgs.Empty);
                return;
        }

        var wasShifted = _layoutManager.ActiveLayer == LayoutLayer.Shift;

        if (_inputEngine is not null)
        {
            await _inputEngine.ProcessKeyAsync(key, isShifted: wasShifted, cancellationToken).ConfigureAwait(true);
        }

        KeyDispatched?.Invoke(this, key);

        // One-shot Shift return to Primary layer after typing a character/vowel sign (standard Ridmik mobile behavior)
        if (wasShifted && key.KeyType is KeyType.Character or KeyType.VowelSign or KeyType.Hasanta or KeyType.Punctuation)
        {
            _layoutManager.SetLayer(LayoutLayer.Primary);
        }
    }

    [RelayCommand]
    public async Task ToggleLanguageAsync(CancellationToken cancellationToken = default)
    {
        _layoutManager.ToggleLanguage();
        _inputEngine?.SetLanguage(_layoutManager.ActiveLanguage);

        _settingsService.Current.ActiveLanguage = _layoutManager.ActiveLanguage;
        await _settingsService.SaveAsync(_settingsService.Current, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SelectCandidateAsync(SuggestionCandidate? candidate, CancellationToken cancellationToken = default)
    {
        if (candidate is null)
        {
            return;
        }

        if (_inputEngine is not null)
        {
            await _inputEngine.CommitCandidateAsync(candidate, cancellationToken).ConfigureAwait(true);
        }
    }

    public void Receive(SuggestionsUpdatedMessage message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplySuggestions(message.Candidates));
            return;
        }

        ApplySuggestions(message.Candidates);
    }

    private void ApplySuggestions(IReadOnlyList<SuggestionCandidate> candidates)
    {
        Suggestions.Clear();
        foreach (var candidate in candidates)
        {
            Suggestions.Add(candidate);
        }
    }

    private void OnLayoutManagerLayoutChanged(object? sender, KeyboardLayoutModel updatedLayout)
    {
        CurrentLayout = updatedLayout;
        ActiveLanguage = _layoutManager.ActiveLanguage;
        ActiveLayer = _layoutManager.ActiveLayer;
    }

    private void InitializeDefaultSuggestions()
    {
        Suggestions.Clear();
        Suggestions.Add(new SuggestionCandidate { Word = "আমার", Score = 1.0, ReplacePrefixLength = 0 });
        Suggestions.Add(new SuggestionCandidate { Word = "আমি", Score = 0.95, ReplacePrefixLength = 0 });
        Suggestions.Add(new SuggestionCandidate { Word = "বাংলা", Score = 0.90, ReplacePrefixLength = 0 });
        Suggestions.Add(new SuggestionCandidate { Word = "ভালো", Score = 0.85, ReplacePrefixLength = 0 });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _layoutManager.LayoutChanged -= OnLayoutManagerLayoutChanged;
        _messenger.UnregisterAll(this);
    }
}
