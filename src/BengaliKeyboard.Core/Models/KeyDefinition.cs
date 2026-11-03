using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Models;

/// <summary>
/// Data-driven definition for a single key in a keyboard layout.
/// </summary>
public sealed class KeyDefinition
{
    public const int MaxUnicodeOutputLength = 8;
    public const double MinWidthRatio = 0.5;
    public const double MaxWidthRatio = 6.0;

    public string Id { get; init; } = string.Empty;

    public string DisplayLabel { get; init; } = string.Empty;

    public string UnicodeOutput { get; init; } = string.Empty;

    public string? AlternateOutput { get; init; }

    public KeyType KeyType { get; init; } = KeyType.Character;

    public double WidthRatio { get; init; } = 1.0;

    public int RowIndex { get; init; }

    public int ColumnIndex { get; init; }

    public string? Tooltip { get; init; }

    public string? IconGlyph { get; init; }

    public LayoutLayer? TargetLayer { get; init; }

    /// <summary>
    /// Validates that the key definition adheres to safe length and geometry bounds (CHK-06 / AS-03).
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return false;
        }

        if (UnicodeOutput.Length > MaxUnicodeOutputLength)
        {
            return false;
        }

        if (AlternateOutput is not null && AlternateOutput.Length > MaxUnicodeOutputLength)
        {
            return false;
        }

        if (double.IsNaN(WidthRatio) || double.IsInfinity(WidthRatio) ||
            WidthRatio < MinWidthRatio || WidthRatio > MaxWidthRatio)
        {
            return false;
        }

        return RowIndex >= 0 && ColumnIndex >= 0;
    }

    /// <summary>
    /// Privacy-safe string representation that never logs raw character output (SEC-04 / CHK-07).
    /// </summary>
    public override string ToString() =>
        $"KeyDefinition[Id={Id}, Type={KeyType}, Row={RowIndex}, Col={ColumnIndex}, WidthRatio={WidthRatio:F2}]";
}
