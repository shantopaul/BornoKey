namespace BengaliKeyboard.Core.Enums;

/// <summary>
/// Presentation modes supported by the BornoKey floating window system.
/// </summary>
public enum KeyboardMode
{
    /// <summary>
    /// Mode A: Full floating virtual keyboard with suggestion bar, number row, and character grid.
    /// </summary>
    Expanded = 0,

    /// <summary>
    /// Mode B: Compact floating virtual keyboard with reduced dimensions.
    /// </summary>
    Compact = 1,

    /// <summary>
    /// Mode C: Draggable minimal floating icon badge ([ বাংলা ]).
    /// </summary>
    MinimalIcon = 2,

    /// <summary>
    /// Mode D: Hidden from desktop; running in background and accessible via the Windows system tray.
    /// </summary>
    HiddenToTray = 3
}
