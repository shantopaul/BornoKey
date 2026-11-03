using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Manages the 4 floating window presentation modes, multi-monitor clamping, and persistent geometry.
/// </summary>
public interface IWindowManagerService
{
    KeyboardMode CurrentMode { get; }

    void ApplyWindowMode(KeyboardMode mode);

    void RestoreLastVisibleMode();

    void ToggleVisibility();

    WindowStateConfig ValidateAndClampBounds(WindowStateConfig state);

    Task SaveWindowStateAsync(WindowStateConfig state, CancellationToken cancellationToken = default);
}
