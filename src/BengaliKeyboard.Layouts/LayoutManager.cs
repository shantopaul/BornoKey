using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using CommunityToolkit.Mvvm.Messaging;

namespace BengaliKeyboard.Layouts;

/// <summary>
/// Manages all loaded Bengali and English keyboard layouts and coordinates instant language and layer switching.
/// </summary>
public sealed class LayoutManager : ILayoutManager
{
    private readonly LayoutLoader _layoutLoader;
    private readonly IMessenger _messenger;
    private readonly Dictionary<(LanguageMode Language, LayoutLayer Layer), KeyboardLayoutModel> _layouts = [];

    private LanguageMode _activeLanguage = LanguageMode.Bangla;
    private LayoutLayer _activeLayer = LayoutLayer.Primary;
    private KeyboardLayoutModel _currentLayout = new();

    public LanguageMode ActiveLanguage => _activeLanguage;

    public LayoutLayer ActiveLayer => _activeLayer;

    public KeyboardLayoutModel CurrentLayout => _currentLayout;

    public event EventHandler<KeyboardLayoutModel>? LayoutChanged;

    public LayoutManager(LayoutLoader layoutLoader, IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(layoutLoader);
        _layoutLoader = layoutLoader;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var banglaPrimary = await _layoutLoader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("Bangla", "PrimaryLayout.json"),
            "Bangla.PrimaryLayout.json",
            cancellationToken).ConfigureAwait(false);

        var banglaShift = await _layoutLoader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("Bangla", "ShiftLayout.json"),
            "Bangla.ShiftLayout.json",
            cancellationToken).ConfigureAwait(false);

        var banglaNumbers = await _layoutLoader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("Bangla", "NumberLayout.json"),
            "Bangla.NumberLayout.json",
            cancellationToken).ConfigureAwait(false);

        var banglaSymbols = await _layoutLoader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("Bangla", "SymbolLayout.json"),
            "Bangla.SymbolLayout.json",
            cancellationToken).ConfigureAwait(false);

        var englishQwerty = await _layoutLoader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("English", "QwertyLayout.json"),
            "English.QwertyLayout.json",
            cancellationToken).ConfigureAwait(false);

        var englishSymbols = await _layoutLoader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("English", "SymbolLayout.json"),
            "English.SymbolLayout.json",
            cancellationToken).ConfigureAwait(false);

        var englishShift = LayoutLoader.CreateShiftedEnglishLayout(englishQwerty);

        _layouts[(LanguageMode.Bangla, LayoutLayer.Primary)] = banglaPrimary;
        _layouts[(LanguageMode.Bangla, LayoutLayer.Shift)] = banglaShift;
        _layouts[(LanguageMode.Bangla, LayoutLayer.Numbers)] = banglaNumbers;
        _layouts[(LanguageMode.Bangla, LayoutLayer.Symbols)] = banglaSymbols;

        _layouts[(LanguageMode.English, LayoutLayer.Primary)] = englishQwerty;
        _layouts[(LanguageMode.English, LayoutLayer.Shift)] = englishShift;
        _layouts[(LanguageMode.English, LayoutLayer.Numbers)] = englishSymbols;
        _layouts[(LanguageMode.English, LayoutLayer.Symbols)] = englishSymbols;

        UpdateCurrentLayout(notifyLanguageChanged: false);
    }

    public void SetLanguage(LanguageMode language)
    {
        if (!Enum.IsDefined(language))
        {
            language = LanguageMode.Bangla;
        }

        var languageChanged = _activeLanguage != language;
        _activeLanguage = language;
        _activeLayer = LayoutLayer.Primary;
        UpdateCurrentLayout(notifyLanguageChanged: languageChanged);
    }

    public void SetLayer(LayoutLayer layer)
    {
        if (!Enum.IsDefined(layer))
        {
            layer = LayoutLayer.Primary;
        }

        if (_activeLayer == layer)
        {
            return;
        }

        _activeLayer = layer;
        UpdateCurrentLayout(notifyLanguageChanged: false);
    }

    public void ToggleLanguage()
    {
        var nextLanguage = _activeLanguage == LanguageMode.Bangla
            ? LanguageMode.English
            : LanguageMode.Bangla;

        SetLanguage(nextLanguage);
    }

    public void ToggleShift()
    {
        var nextLayer = _activeLayer == LayoutLayer.Shift
            ? LayoutLayer.Primary
            : LayoutLayer.Shift;

        SetLayer(nextLayer);
    }

    private void UpdateCurrentLayout(bool notifyLanguageChanged)
    {
        if (_layouts.TryGetValue((_activeLanguage, _activeLayer), out var matchedLayout))
        {
            _currentLayout = matchedLayout;
        }
        else if (_layouts.TryGetValue((_activeLanguage, LayoutLayer.Primary), out var fallbackPrimary))
        {
            _activeLayer = LayoutLayer.Primary;
            _currentLayout = fallbackPrimary;
        }

        if (notifyLanguageChanged)
        {
            _messenger.Send(new LanguageChangedMessage(_activeLanguage));
        }

        LayoutChanged?.Invoke(this, _currentLayout);
    }
}
