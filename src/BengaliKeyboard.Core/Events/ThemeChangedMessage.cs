using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.Core.Events;

/// <summary>
/// Broadcast when the visual theme (Dark, Light, System) or opacity settings change.
/// </summary>
public sealed record ThemeChangedMessage(AppTheme Theme, double Opacity);
