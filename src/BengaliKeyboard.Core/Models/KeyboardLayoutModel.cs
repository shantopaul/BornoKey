using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Represents a single horizontal row of keys within a keyboard layout.
/// </summary>
public sealed class KeyboardRowModel
{
    public int RowIndex { get; init; }

    public double HeightRatio { get; init; } = 1.0;

    public IReadOnlyList<KeyDefinition> Keys { get; init; } = [];
}

/// <summary>
/// Complete data-driven keyboard layout for a specific language and layer.
/// </summary>
public sealed class KeyboardLayoutModel
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public LanguageMode Language { get; init; } = LanguageMode.Bangla;

    public LayoutLayer Layer { get; init; } = LayoutLayer.Primary;

    public IReadOnlyList<KeyboardRowModel> Rows { get; init; } = [];
}
