using System.Collections.ObjectModel;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BengaliKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel driving <c>EmojiWindow</c> with 10 categories, instant offline keyword search,
/// multi-codepoint Unicode injection, and SQLite-backed Frequently Used history management.
/// </summary>
public sealed partial class EmojiViewModel : ObservableObject
{
    public const string FrequentlyUsedCategoryName = "Frequently Used";
    public const string DefaultFallbackCategoryName = "Smileys & Emotions";

    private readonly IEmojiService _emojiService;
    private readonly ITextInjector? _textInjector;
    private bool _suppressSearchReload;

    public ObservableCollection<string> Categories { get; } = [];

    public ObservableCollection<EmojiItem> DisplayedEmojis { get; } = [];

    [ObservableProperty]
    private string _selectedCategory = DefaultFallbackCategoryName;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isFrequentlyUsedSelected;

    public event EventHandler<EmojiItem>? EmojiSelected;

    public event EventHandler? BackToKeyboardRequested;

    public EmojiViewModel(IEmojiService emojiService, ITextInjector? textInjector = null)
    {
        ArgumentNullException.ThrowIfNull(emojiService);
        _emojiService = emojiService;
        _textInjector = textInjector;

        foreach (var category in _emojiService.GetCategories())
        {
            Categories.Add(category);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var frequent = await _emojiService.GetFrequentlyUsedAsync(limit: 40, cancellationToken).ConfigureAwait(true);
        if (frequent.Count > 0)
        {
            SelectedCategory = FrequentlyUsedCategoryName;
            IsFrequentlyUsedSelected = true;
            ApplyDisplayedEmojis(frequent);
        }
        else
        {
            SelectedCategory = DefaultFallbackCategoryName;
            IsFrequentlyUsedSelected = false;
            var initial = await _emojiService
                .GetByCategoryAsync(DefaultFallbackCategoryName, cancellationToken)
                .ConfigureAwait(true);
            ApplyDisplayedEmojis(initial);
        }
    }

    [RelayCommand]
    public async Task SelectCategoryAsync(string? category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        _suppressSearchReload = true;
        try
        {
            SearchQuery = string.Empty;
        }
        finally
        {
            _suppressSearchReload = false;
        }

        SelectedCategory = category;
        IsFrequentlyUsedSelected = string.Equals(category, FrequentlyUsedCategoryName, StringComparison.OrdinalIgnoreCase);

        var items = await _emojiService.GetByCategoryAsync(category, cancellationToken).ConfigureAwait(true);
        ApplyDisplayedEmojis(items);
    }

    [RelayCommand]
    public async Task SearchEmojisAsync(string? query, CancellationToken cancellationToken = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            var categoryItems = await _emojiService
                .GetByCategoryAsync(SelectedCategory, cancellationToken)
                .ConfigureAwait(true);
            ApplyDisplayedEmojis(categoryItems);
            return;
        }

        var searchResults = await _emojiService
            .SearchAsync(trimmed, maxResults: 60, cancellationToken)
            .ConfigureAwait(true);
        ApplyDisplayedEmojis(searchResults);
    }

    [RelayCommand]
    public async Task SelectEmojiAsync(EmojiItem? item, CancellationToken cancellationToken = default)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Emoji))
        {
            return;
        }

        _textInjector?.SendUnicodeText(item.Emoji);
        await _emojiService.RecordUsageAsync(item.Emoji, cancellationToken).ConfigureAwait(true);

        item.UsageCount++;
        item.LastUsedUtc = DateTimeOffset.UtcNow;

        if (IsFrequentlyUsedSelected && string.IsNullOrWhiteSpace(SearchQuery))
        {
            var refreshed = await _emojiService
                .GetFrequentlyUsedAsync(limit: 40, cancellationToken)
                .ConfigureAwait(true);
            ApplyDisplayedEmojis(refreshed);
        }

        EmojiSelected?.Invoke(this, item);
    }

    [RelayCommand]
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _emojiService.ClearHistoryAsync(cancellationToken).ConfigureAwait(true);
        if (IsFrequentlyUsedSelected && string.IsNullOrWhiteSpace(SearchQuery))
        {
            DisplayedEmojis.Clear();
            StatusMessage = "No frequently used emojis yet.";
        }
    }

    [RelayCommand]
    public void BackToKeyboard()
    {
        BackToKeyboardRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (_suppressSearchReload)
        {
            return;
        }

        _ = SearchEmojisAsync(value);
    }

    private void ApplyDisplayedEmojis(IReadOnlyList<EmojiItem> items)
    {
        DisplayedEmojis.Clear();
        foreach (var item in items)
        {
            DisplayedEmojis.Add(item);
        }

        if (DisplayedEmojis.Count == 0)
        {
            StatusMessage = !string.IsNullOrWhiteSpace(SearchQuery)
                ? $"No emojis found for \"{SearchQuery.Trim()}\"."
                : IsFrequentlyUsedSelected
                    ? "No frequently used emojis yet."
                    : "No emojis in this category.";
        }
        else
        {
            StatusMessage = $"{DisplayedEmojis.Count} emojis";
        }
    }
}
