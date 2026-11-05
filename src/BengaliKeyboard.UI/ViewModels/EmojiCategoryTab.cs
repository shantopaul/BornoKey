using CommunityToolkit.Mvvm.ComponentModel;

namespace BengaliKeyboard.UI.ViewModels;

/// <summary>
/// Represents one of the 10 standard emoji category tabs in <c>EmojiWindow</c>,
/// pairing a category icon and compact label with an observable selection state.
/// </summary>
public sealed partial class EmojiCategoryTab : ObservableObject
{
    public string CategoryName { get; }

    public string Icon { get; }

    public string ShortTitle { get; }

    public string DisplayTooltip => $"{Icon}  {CategoryName}";

    [ObservableProperty]
    private bool _isSelected;

    public EmojiCategoryTab(string categoryName, string icon, string shortTitle, bool isSelected = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        CategoryName = categoryName;
        Icon = icon;
        ShortTitle = shortTitle;
        _isSelected = isSelected;
    }

    public static (string Icon, string ShortTitle) ResolveMetadata(string categoryName)
    {
        return categoryName switch
        {
            "Frequently Used" => ("🕒", "Recent"),
            "Smileys & Emotions" => ("😀", "Smileys"),
            "People & Body" => ("👋", "People"),
            "Animals & Nature" => ("🐻", "Nature"),
            "Food & Drink" => ("🍔", "Food"),
            "Travel & Places" => ("✈️", "Travel"),
            "Activities" => ("⚽", "Activity"),
            "Objects" => ("💡", "Objects"),
            "Symbols" => ("❤️", "Symbols"),
            "Flags" => ("🇧🇩", "Flags"),
            _ => ("✨", categoryName)
        };
    }
}
