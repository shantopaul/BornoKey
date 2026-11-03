using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Events;

/// <summary>
/// Broadcast when the floating keyboard transitions between Expanded, Compact, MinimalIcon, or HiddenToTray modes.
/// </summary>
public sealed record WindowModeChangedMessage(KeyboardMode PreviousMode, KeyboardMode NewMode);
