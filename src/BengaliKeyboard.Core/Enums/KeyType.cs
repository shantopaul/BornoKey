namespace BengaliKeyboard.Core.Enums;

/// <summary>
/// Functional classification of a virtual keyboard key.
/// </summary>
public enum KeyType
{
    /// <summary>
    /// Standard vowel or consonant character key.
    /// </summary>
    Character = 0,

    /// <summary>
    /// Dependent Bengali vowel sign (Kar) or diacritic mark.
    /// </summary>
    VowelSign = 1,

    /// <summary>
    /// Bengali Virama / Hasanta (্) used to form conjunct characters (Juktakkhor).
    /// </summary>
    Hasanta = 2,

    /// <summary>
    /// Bengali or English numeral key.
    /// </summary>
    Number = 3,

    /// <summary>
    /// Punctuation or symbol character key (e.g., Dari ।, comma, period).
    /// </summary>
    Punctuation = 4,

    /// <summary>
    /// Shift / alternate layer modifier key.
    /// </summary>
    Shift = 5,

    /// <summary>
    /// Grapheme-aware backspace deletion key (supports press-and-hold repeat).
    /// </summary>
    Backspace = 6,

    /// <summary>
    /// Enter / newline / action submission key.
    /// </summary>
    Enter = 7,

    /// <summary>
    /// Spacebar key.
    /// </summary>
    Space = 8,

    /// <summary>
    /// Instant language switch key ([ বাংলা ] / [ ENG ]).
    /// </summary>
    LanguageSwitch = 9,

    /// <summary>
    /// Layout layer switch key (e.g., ?123, Symbols, ABC).
    /// </summary>
    LayoutSwitch = 10,

    /// <summary>
    /// Opens or closes the integrated Emoji panel.
    /// </summary>
    EmojiSwitch = 11,

    /// <summary>
    /// Opens or closes the integrated Clipboard History panel.
    /// </summary>
    ClipboardSwitch = 12
}
