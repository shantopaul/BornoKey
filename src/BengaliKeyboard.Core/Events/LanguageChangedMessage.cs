using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Events;

/// <summary>
/// Broadcast when the active typing language switches between Bangla and English.
/// </summary>
public sealed record LanguageChangedMessage(LanguageMode Language);
