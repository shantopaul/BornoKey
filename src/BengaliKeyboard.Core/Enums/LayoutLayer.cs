namespace BengaliKeyboard.Core.Enums;

/// <summary>
/// Keyboard layout layer within the active language mode.
/// </summary>
public enum LayoutLayer
{
    /// <summary>
    /// Default primary character layer.
    /// </summary>
    Primary = 0,

    /// <summary>
    /// Shifted / alternate character layer.
    /// </summary>
    Shift = 1,

    /// <summary>
    /// Dedicated numeric and mathematical symbol layer.
    /// </summary>
    Numbers = 2,

    /// <summary>
    /// Extended punctuation and special symbols layer.
    /// </summary>
    Symbols = 3
}
