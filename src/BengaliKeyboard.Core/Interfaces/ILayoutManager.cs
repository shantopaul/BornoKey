using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Manages configurable JSON keyboard layouts and active language/layer state.
/// </summary>
public interface ILayoutManager
{
    LanguageMode ActiveLanguage { get; }

    LayoutLayer ActiveLayer { get; }

    KeyboardLayoutModel CurrentLayout { get; }

    event EventHandler<KeyboardLayoutModel>? LayoutChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void SetLanguage(LanguageMode language);

    void SetLayer(LayoutLayer layer);

    void ToggleLanguage();

    void ToggleShift();
}
