using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.Messaging;

namespace BengaliKeyboard.Input;

/// <summary>
/// Core Bengali &amp; English input engine shared by both virtual on-screen keys and physical keyboard hooks.
/// Coordinates <see cref="UnicodeProcessor"/>, <see cref="ITextInjector"/>, <see cref="IPredictionService"/>,
/// and <see cref="IMessenger"/> while canceling stale in-flight prediction queries on rapid keystrokes.
/// </summary>
public sealed class BengaliInputEngine : IBengaliInputEngine, IDisposable
{
    private const int MaxBufferLength = 64;
    private const int MaxContextWords = 2;

    private static readonly IReadOnlyList<SuggestionCandidate> DefaultInitialCandidates =
    [
        new SuggestionCandidate { Word = "আমার", Score = 1.0, ReplacePrefixLength = 0, IsNextWordPrediction = true },
        new SuggestionCandidate { Word = "আমি", Score = 0.95, ReplacePrefixLength = 0, IsNextWordPrediction = true },
        new SuggestionCandidate { Word = "বাংলা", Score = 0.90, ReplacePrefixLength = 0, IsNextWordPrediction = true },
        new SuggestionCandidate { Word = "ভালো", Score = 0.85, ReplacePrefixLength = 0, IsNextWordPrediction = true }
    ];

    private static readonly IReadOnlyList<SuggestionCandidate> DefaultEnglishInitialCandidates =
    [
        new SuggestionCandidate { Word = "I", Score = 1.0, ReplacePrefixLength = 0, IsNextWordPrediction = true },
        new SuggestionCandidate { Word = "Hello", Score = 0.95, ReplacePrefixLength = 0, IsNextWordPrediction = true },
        new SuggestionCandidate { Word = "Thank", Score = 0.90, ReplacePrefixLength = 0, IsNextWordPrediction = true },
        new SuggestionCandidate { Word = "The", Score = 0.85, ReplacePrefixLength = 0, IsNextWordPrediction = true }
    ];

    public static IReadOnlyList<SuggestionCandidate> GetDefaultCandidatesForLanguage(LanguageMode language, int maxResults = 4)
    {
        var source = language == LanguageMode.English
            ? DefaultEnglishInitialCandidates
            : DefaultInitialCandidates;

        return source.Take(Math.Clamp(maxResults, 1, 5)).ToList();
    }

    private readonly UnicodeProcessor _unicodeProcessor;
    private readonly ITextInjector _textInjector;
    private readonly IPredictionService? _predictionService;
    private readonly ISettingsService? _settingsService;
    private readonly IMessenger _messenger;
    private readonly List<string> _recentWords = new(MaxContextWords);

    private CancellationTokenSource? _predictionCts;
    private LanguageMode _currentLanguage = LanguageMode.Bangla;
    private string _currentWordBuffer = string.Empty;
    private bool _disposed;

    public LanguageMode CurrentLanguage => _currentLanguage;

    public string CurrentWordBuffer => _currentWordBuffer;

    public IReadOnlyList<string> RecentWords => _recentWords;

    public BengaliInputEngine(
        UnicodeProcessor unicodeProcessor,
        ITextInjector textInjector,
        IPredictionService? predictionService = null,
        ISettingsService? settingsService = null,
        IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(unicodeProcessor);
        ArgumentNullException.ThrowIfNull(textInjector);

        _unicodeProcessor = unicodeProcessor;
        _textInjector = textInjector;
        _predictionService = predictionService;
        _settingsService = settingsService;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        if (_settingsService is not null)
        {
            _currentLanguage = _settingsService.Current.ActiveLanguage;
        }
    }

    public void SetLanguage(LanguageMode language)
    {
        if (_currentLanguage == language)
        {
            return;
        }

        _currentLanguage = language;
        _recentWords.Clear();
        ResetBuffer();

        var maxResults = Math.Clamp(_settingsService?.Current.SuggestionCount ?? 4, 3, 5);
        _messenger.Send(new LanguageChangedMessage(language));
        _messenger.Send(new SuggestionsUpdatedMessage(GetDefaultCandidatesForLanguage(language, maxResults)));
    }

    public void ToggleLanguage()
    {
        var nextLanguage = _currentLanguage == LanguageMode.Bangla
            ? LanguageMode.English
            : LanguageMode.Bangla;

        SetLanguage(nextLanguage);
    }

    public async Task ProcessKeyAsync(KeyDefinition key, bool isShifted = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        switch (key.KeyType)
        {
            case KeyType.Backspace:
                await HandleBackspaceAsync(cancellationToken).ConfigureAwait(false);
                return;

            case KeyType.Enter:
                CommitCurrentWordToContext();
                _recentWords.Clear();
                _textInjector.SendEnter();
                await UpdatePredictionsAsync(cancellationToken).ConfigureAwait(false);
                return;

            case KeyType.Space:
                await HandleSpaceAsync(cancellationToken).ConfigureAwait(false);
                return;

            case KeyType.Shift:
            case KeyType.LanguageSwitch:
            case KeyType.LayoutSwitch:
            case KeyType.EmojiSwitch:
            case KeyType.ClipboardSwitch:
                return;
        }

        var rawOutput = ResolveKeyUnicodeOutput(key, isShifted);
        if (string.IsNullOrEmpty(rawOutput))
        {
            return;
        }

        // Allow mid-word apostrophe inside English contractions (e.g., "don't", "I'm", "it's")
        var isEnglishMidWordApostrophe = _currentLanguage == LanguageMode.English
            && !string.IsNullOrEmpty(_currentWordBuffer)
            && rawOutput == "'";

        // Check if this key is punctuation or a word boundary (e.g., Dari '।', comma, period)
        if (!isEnglishMidWordApostrophe &&
            (key.KeyType == KeyType.Punctuation || (rawOutput.Length == 1 && UnicodeProcessor.IsWordBoundary(rawOutput[0]))))
        {
            var previousWordBeforeCommit = _recentWords.Count >= 1 ? _recentWords[^1] : null;
            var completedWord = CommitCurrentWordToContext();
            if (!string.IsNullOrWhiteSpace(completedWord) && _predictionService is not null)
            {
                await _predictionService.RecordAcceptedWordAsync(completedWord, previousWordBeforeCommit, cancellationToken).ConfigureAwait(false);
            }

            if (rawOutput.Length == 1 && rawOutput[0] is '।' or '.' or '!' or '?')
            {
                _recentWords.Clear();
            }

            _textInjector.SendUnicodeText(rawOutput);
            await UpdatePredictionsAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_currentLanguage == LanguageMode.Bangla)
        {
            var composition = _unicodeProcessor.Compose(_currentWordBuffer, rawOutput);
            _currentWordBuffer = TrimBufferIfOversized(composition.UpdatedBuffer);
            _textInjector.ReplaceText(composition.BackspaceCount, composition.TextToInject);
        }
        else
        {
            _currentWordBuffer = TrimBufferIfOversized(_currentWordBuffer + rawOutput);
            _textInjector.SendUnicodeText(rawOutput);
        }

        await UpdatePredictionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitCandidateAsync(SuggestionCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(candidate.Word))
        {
            return;
        }

        var normalizedWord = UnicodeProcessor.NormalizeBengaliText(candidate.Word.Trim());
        var backspacesToSend = !string.IsNullOrEmpty(_currentWordBuffer)
            ? UnicodeProcessor.CountUnicodeScalars(_currentWordBuffer)
            : Math.Max(0, candidate.ReplacePrefixLength);

        _textInjector.ReplaceText(backspacesToSend, normalizedWord + " ");

        var previousWordBeforeCommit = _recentWords.Count >= 1 ? _recentWords[^1] : null;
        _currentWordBuffer = string.Empty;
        PushRecentWord(normalizedWord);

        if (_predictionService is not null)
        {
            await _predictionService.RecordAcceptedWordAsync(normalizedWord, previousWordBeforeCommit, cancellationToken).ConfigureAwait(false);
        }

        await UpdatePredictionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ResetBuffer()
    {
        CancelInFlightPrediction();
        _currentWordBuffer = string.Empty;
    }

    private async Task HandleBackspaceAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentWordBuffer))
        {
            _textInjector.SendBackspaces(1);
            return;
        }

        var diff = _unicodeProcessor.Backspace(_currentWordBuffer, deleteEntireGraphemeCluster: false);
        _currentWordBuffer = diff.UpdatedBuffer;
        _textInjector.ReplaceText(diff.BackspaceCount, diff.TextToInject);

        await UpdatePredictionsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSpaceAsync(CancellationToken cancellationToken)
    {
        var previousWordBeforeCommit = _recentWords.Count >= 1 ? _recentWords[^1] : null;
        var completedWord = CommitCurrentWordToContext();
        _textInjector.SendSpace();

        if (!string.IsNullOrWhiteSpace(completedWord) && _predictionService is not null)
        {
            await _predictionService.RecordAcceptedWordAsync(completedWord, previousWordBeforeCommit, cancellationToken).ConfigureAwait(false);
        }

        await UpdatePredictionsAsync(cancellationToken).ConfigureAwait(false);
    }

    private string? CommitCurrentWordToContext()
    {
        if (string.IsNullOrWhiteSpace(_currentWordBuffer))
        {
            _currentWordBuffer = string.Empty;
            return null;
        }

        var committed = UnicodeProcessor.NormalizeBengaliText(_currentWordBuffer);
        _currentWordBuffer = string.Empty;
        PushRecentWord(committed);
        return committed;
    }

    private void PushRecentWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        if (_recentWords.Count >= MaxContextWords)
        {
            _recentWords.RemoveAt(0);
        }

        _recentWords.Add(word);
    }

    private string ResolveKeyUnicodeOutput(KeyDefinition key, bool isShifted)
    {
        if (_currentLanguage == LanguageMode.English && isShifted)
        {
            if (!string.IsNullOrEmpty(key.AlternateOutput) &&
                key.AlternateOutput.Length == 1 &&
                char.IsAsciiLetterUpper(key.AlternateOutput[0]))
            {
                return key.AlternateOutput;
            }

            if (key.UnicodeOutput.Length == 1 && char.IsAsciiLetterLower(key.UnicodeOutput[0]))
            {
                return key.UnicodeOutput.ToUpperInvariant();
            }
        }

        return key.UnicodeOutput;
    }

    private static string TrimBufferIfOversized(string buffer)
    {
        if (buffer.Length <= MaxBufferLength)
        {
            return buffer;
        }

        return buffer[^MaxBufferLength..];
    }

    private async Task UpdatePredictionsAsync(CancellationToken externalToken)
    {
        CancelInFlightPrediction();
        _predictionCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = _predictionCts.Token;

        var maxResults = Math.Clamp(_settingsService?.Current.SuggestionCount ?? 4, 3, 5);
        var wordCompletionEnabled = _settingsService?.Current.EnableWordCompletion ?? true;
        var nextWordEnabled = _settingsService?.Current.EnableNextWordPrediction ?? true;

        try
        {
            if (_predictionService is null)
            {
                PublishFallbackSuggestions(maxResults);
                return;
            }

            IReadOnlyList<SuggestionCandidate> candidates = [];

            if (!string.IsNullOrEmpty(_currentWordBuffer) && wordCompletionEnabled)
            {
                candidates = await _predictionService
                    .GetWordCompletionsAsync(_currentWordBuffer, maxResults, token)
                    .ConfigureAwait(false);
            }
            else if (string.IsNullOrEmpty(_currentWordBuffer) && nextWordEnabled)
            {
                if (_recentWords.Count == 0)
                {
                    candidates = GetDefaultCandidatesForLanguage(_currentLanguage, maxResults);
                }
                else
                {
                    string? word1 = null;
                    string? word2 = null;

                    if (_recentWords.Count >= 2)
                    {
                        word1 = _recentWords[^2];
                        word2 = _recentWords[^1];
                    }
                    else if (_recentWords.Count == 1)
                    {
                        word1 = _recentWords[0];
                    }

                    candidates = await _predictionService
                        .GetNextWordPredictionsAsync(word1, word2, maxResults, token)
                        .ConfigureAwait(false);
                }
            }

            if (!token.IsCancellationRequested)
            {
                _messenger.Send(new SuggestionsUpdatedMessage(candidates));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke; ignore cleanly.
        }
    }

    private void PublishFallbackSuggestions(int maxResults)
    {
        var defaultPool = GetDefaultCandidatesForLanguage(_currentLanguage, maxResults: 5);
        var comparison = _currentLanguage == LanguageMode.English
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.IsNullOrEmpty(_currentWordBuffer))
        {
            var prefixScalarCount = UnicodeProcessor.CountUnicodeScalars(_currentWordBuffer);
            var matching = defaultPool
                .Where(c => c.Word.StartsWith(_currentWordBuffer, comparison))
                .Select(c => new SuggestionCandidate
                {
                    Word = c.Word,
                    Score = c.Score,
                    ReplacePrefixLength = prefixScalarCount,
                    IsNextWordPrediction = false
                })
                .ToList();

            if (matching.Count == 0)
            {
                matching.Add(new SuggestionCandidate
                {
                    Word = _currentWordBuffer,
                    Score = 1.0,
                    ReplacePrefixLength = prefixScalarCount,
                    IsNextWordPrediction = false
                });
            }

            _messenger.Send(new SuggestionsUpdatedMessage(matching.Take(maxResults).ToList()));
            return;
        }

        _messenger.Send(new SuggestionsUpdatedMessage(defaultPool.Take(maxResults).ToList()));
    }

    private void CancelInFlightPrediction()
    {
        if (_predictionCts is null)
        {
            return;
        }

        try
        {
            _predictionCts.Cancel();
            _predictionCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _predictionCts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelInFlightPrediction();
    }
}
