using System.Collections.ObjectModel;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BengaliKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel driving <c>EmojiWindow</c> with 10 icon-led categories, instant offline keyword search,
/// popular quick-access fallback, live emoji hover preview, multi-codepoint Unicode injection,
/// and SQLite-backed Frequently Used history management.
/// </summary>
public sealed partial class EmojiViewModel : ObservableObject
{
    public const string FrequentlyUsedCategoryName = "Frequently Used";
    public const string DefaultFallbackCategoryName = "Smileys & Emotions";

    private static readonly string[] DefaultPopularGlyphs =
    [
        "😀", "😂", "🤣", "😊", "🥰", "😍", "😘", "😎",
        "😭", "🤔", "🥳", "🫡", "👍", "🙏", "👏", "🤝",
        "🫶", "❤️", "🔥", "✨", "💯", "🎉", "🌹", "🇧🇩"
    ];

    private readonly IEmojiService _emojiService;
    private readonly ITextInjector? _textInjector;
    private CancellationTokenSource? _searchCts;
    private bool _suppressSearchReload;

    public ObservableCollection<string> Categories { get; } = [];

    public ObservableCollection<EmojiCategoryTab> CategoryTabs { get; } = [];

    public ObservableCollection<EmojiItem> DisplayedEmojis { get; } = [];

    public ObservableCollection<EmojiItem> PopularEmojis { get; } = [];

    [ObservableProperty]
    private string _selectedCategory = DefaultFallbackCategoryName;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _hasSearchQuery;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _currentSectionIcon = "😀";

    [ObservableProperty]
    private string _currentSectionTitle = "Smileys & Emotions";

    [ObservableProperty]
    private bool _isFrequentlyUsedSelected;

    [ObservableProperty]
    private bool _showPopularFallbackSection;

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    [ObservableProperty]
    private string _previewEmoji = "😀";

    [ObservableProperty]
    private string _previewTitle = "Grinning Face";

    [ObservableProperty]
    private string _previewKeywords = "smile, happy, হাসি, খুশি";

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
            var (icon, shortTitle) = EmojiCategoryTab.ResolveMetadata(category);
            CategoryTabs.Add(new EmojiCategoryTab(
                category,
                icon,
                shortTitle,
                string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var frequent = await _emojiService.GetFrequentlyUsedAsync(limit: 40, cancellationToken).ConfigureAwait(true);
        if (frequent.Count > 0)
        {
            SelectedCategory = FrequentlyUsedCategoryName;
            IsFrequentlyUsedSelected = true;
            UpdateSelectedTabHighlight(FrequentlyUsedCategoryName);
            await RefreshPopularFallbackAsync(frequent, cancellationToken).ConfigureAwait(true);
            ApplyDisplayedEmojis(frequent);
        }
        else
        {
            SelectedCategory = DefaultFallbackCategoryName;
            IsFrequentlyUsedSelected = false;
            UpdateSelectedTabHighlight(DefaultFallbackCategoryName);
            PopularEmojis.Clear();
            ShowPopularFallbackSection = false;

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
            HasSearchQuery = false;
        }
        finally
        {
            _suppressSearchReload = false;
        }

        SelectedCategory = category;
        IsFrequentlyUsedSelected = string.Equals(category, FrequentlyUsedCategoryName, StringComparison.OrdinalIgnoreCase);
        UpdateSelectedTabHighlight(category);

        var items = await _emojiService.GetByCategoryAsync(category, cancellationToken).ConfigureAwait(true);
        if (IsFrequentlyUsedSelected)
        {
            await RefreshPopularFallbackAsync(items, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            PopularEmojis.Clear();
            ShowPopularFallbackSection = false;
        }

        ApplyDisplayedEmojis(items);
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    public async Task SearchEmojisAsync(string? query, CancellationToken cancellationToken = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        HasSearchQuery = !string.IsNullOrEmpty(trimmed);

        if (string.IsNullOrEmpty(trimmed))
        {
            var categoryItems = await _emojiService
                .GetByCategoryAsync(SelectedCategory, cancellationToken)
                .ConfigureAwait(true);

            if (IsFrequentlyUsedSelected)
            {
                await RefreshPopularFallbackAsync(categoryItems, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                PopularEmojis.Clear();
                ShowPopularFallbackSection = false;
            }

            ApplyDisplayedEmojis(categoryItems);
            return;
        }

        PopularEmojis.Clear();
        ShowPopularFallbackSection = false;

        var searchResults = await _emojiService
            .SearchAsync(trimmed, maxResults: 80, cancellationToken)
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

        SetHoveredEmoji(item);
        _textInjector?.SendUnicodeText(item.Emoji);
        await _emojiService.RecordUsageAsync(item.Emoji, cancellationToken).ConfigureAwait(true);

        item.UsageCount++;
        item.LastUsedUtc = DateTimeOffset.UtcNow;

        if (IsFrequentlyUsedSelected && string.IsNullOrWhiteSpace(SearchQuery))
        {
            var refreshed = await _emojiService
                .GetFrequentlyUsedAsync(limit: 40, cancellationToken)
                .ConfigureAwait(true);
            await RefreshPopularFallbackAsync(refreshed, cancellationToken).ConfigureAwait(true);
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
            await RefreshPopularFallbackAsync([], cancellationToken).ConfigureAwait(true);
            IsEmptyStateVisible = false;
            StatusMessage = "No frequently used emojis yet.";
            return;
        }

        foreach (var item in DisplayedEmojis)
        {
            item.UsageCount = 0;
            item.LastUsedUtc = null;
        }
    }

    [RelayCommand]
    public void BackToKeyboard()
    {
        BackToKeyboardRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetHoveredEmoji(EmojiItem? item)
    {
        if (item is null)
        {
            return;
        }

        PreviewEmoji = item.Emoji;
        PreviewTitle = string.IsNullOrWhiteSpace(item.Name) ? item.Category : item.Name;
        PreviewKeywords = item.Keywords is { Count: > 0 }
            ? string.Join(", ", item.Keywords.Take(5))
            : item.Category;
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        UpdateSelectedTabHighlight(value);
    }

    partial void OnSearchQueryChanged(string value)
    {
        HasSearchQuery = !string.IsNullOrWhiteSpace(value);

        if (_suppressSearchReload)
        {
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = RunLatestSearchAsync(value, token);
    }

    private async Task RunLatestSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await SearchEmojisAsync(query, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search query
        }
    }

    private void UpdateSelectedTabHighlight(string activeCategory)
    {
        foreach (var tab in CategoryTabs)
        {
            tab.IsSelected = string.Equals(tab.CategoryName, activeCategory, StringComparison.OrdinalIgnoreCase);
        }

        var (icon, _) = EmojiCategoryTab.ResolveMetadata(activeCategory);
        CurrentSectionIcon = icon;
        CurrentSectionTitle = activeCategory;
    }

    private async Task RefreshPopularFallbackAsync(
        IReadOnlyList<EmojiItem> frequentItems,
        CancellationToken cancellationToken)
    {
        PopularEmojis.Clear();

        if (!IsFrequentlyUsedSelected || !string.IsNullOrWhiteSpace(SearchQuery) || frequentItems.Count >= 16)
        {
            ShowPopularFallbackSection = false;
            return;
        }

        var existingGlyphs = new HashSet<string>(frequentItems.Select(x => x.Emoji), StringComparer.Ordinal);
        var smileys = await _emojiService.GetByCategoryAsync("Smileys & Emotions", cancellationToken).ConfigureAwait(true);
        var people = await _emojiService.GetByCategoryAsync("People & Body", cancellationToken).ConfigureAwait(true);
        var symbols = await _emojiService.GetByCategoryAsync("Symbols", cancellationToken).ConfigureAwait(true);
        var nature = await _emojiService.GetByCategoryAsync("Animals & Nature", cancellationToken).ConfigureAwait(true);
        var flags = await _emojiService.GetByCategoryAsync("Flags", cancellationToken).ConfigureAwait(true);

        var lookup = smileys
            .Concat(people)
            .Concat(symbols)
            .Concat(nature)
            .Concat(flags)
            .GroupBy(x => x.Emoji, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var glyph in DefaultPopularGlyphs)
        {
            if (!existingGlyphs.Contains(glyph) && lookup.TryGetValue(glyph, out var emojiItem))
            {
                PopularEmojis.Add(emojiItem);
            }
        }

        ShowPopularFallbackSection = PopularEmojis.Count > 0;
    }

    private void ApplyDisplayedEmojis(IReadOnlyList<EmojiItem> items)
    {
        DisplayedEmojis.Clear();
        foreach (var item in items)
        {
            DisplayedEmojis.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            CurrentSectionIcon = "🔍";
            CurrentSectionTitle = $"Search Results for \"{SearchQuery.Trim()}\"";
        }
        else
        {
            var (icon, _) = EmojiCategoryTab.ResolveMetadata(SelectedCategory);
            CurrentSectionIcon = icon;
            CurrentSectionTitle = SelectedCategory;
        }

        if (DisplayedEmojis.Count > 0)
        {
            SetHoveredEmoji(DisplayedEmojis[0]);
            IsEmptyStateVisible = false;
            StatusMessage = ShowPopularFallbackSection && PopularEmojis.Count > 0
                ? $"{DisplayedEmojis.Count} recent • {PopularEmojis.Count} popular"
                : $"{DisplayedEmojis.Count} emojis";
        }
        else if (PopularEmojis.Count > 0)
        {
            SetHoveredEmoji(PopularEmojis[0]);
            IsEmptyStateVisible = false;
            StatusMessage = "No frequently used emojis yet.";
        }
        else
        {
            IsEmptyStateVisible = true;
            StatusMessage = !string.IsNullOrWhiteSpace(SearchQuery)
                ? $"No emojis found for \"{SearchQuery.Trim()}\"."
                : IsFrequentlyUsedSelected
                    ? "No frequently used emojis yet."
                    : "No emojis in this category.";
        }
    }
}
