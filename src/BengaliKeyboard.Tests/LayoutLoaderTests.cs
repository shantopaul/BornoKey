using System.IO;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Layouts;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class LayoutLoaderTests : IDisposable
{
    private readonly string _tempRoot;

    public LayoutLoaderTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "BornoKey_LayoutTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task LoadAllEmbeddedLayouts_ValidatesAuthenticRidmikStructureAndKeyPositions()
    {
        var banglaPrimary = await LayoutLoader.LoadFromEmbeddedResourceAsync("Bangla.PrimaryLayout.json");
        Assert.Equal("Bangla.Primary", banglaPrimary.Id);
        Assert.Equal(LanguageMode.Bangla, banglaPrimary.Language);
        Assert.Equal(LayoutLayer.Primary, banglaPrimary.Layer);
        Assert.Equal(5, banglaPrimary.Rows.Count);

        // Row 0: Dedicated Bangla Number Row (১-০)
        Assert.Equal(10, banglaPrimary.Rows[0].Keys.Count);
        Assert.Equal("১", banglaPrimary.Rows[0].Keys[0].UnicodeOutput);
        Assert.Equal("০", banglaPrimary.Rows[0].Keys[9].UnicodeOutput);

        // Verify Ridmik character and vowel sign presence
        var allPrimaryKeys = banglaPrimary.Rows.SelectMany(r => r.Keys).ToList();
        Assert.Contains(allPrimaryKeys, k => k.UnicodeOutput == "ক" && k.AlternateOutput == "খ");
        Assert.Contains(allPrimaryKeys, k => k.UnicodeOutput == "া" && k.KeyType == KeyType.VowelSign);
        Assert.Contains(allPrimaryKeys, k => k.UnicodeOutput == "্" && k.KeyType == KeyType.Hasanta);
        Assert.Contains(allPrimaryKeys, k => k.UnicodeOutput == "।" && k.KeyType == KeyType.Punctuation);
        Assert.Contains(allPrimaryKeys, k => k.KeyType == KeyType.LanguageSwitch && k.DisplayLabel == "ENG");

        var banglaShift = await LayoutLoader.LoadFromEmbeddedResourceAsync("Bangla.ShiftLayout.json");
        Assert.Equal(LayoutLayer.Shift, banglaShift.Layer);
        Assert.Equal(5, banglaShift.Rows.Count);

        var banglaNumbers = await LayoutLoader.LoadFromEmbeddedResourceAsync("Bangla.NumberLayout.json");
        Assert.Equal(LayoutLayer.Numbers, banglaNumbers.Layer);
        Assert.Equal(4, banglaNumbers.Rows.Count);

        var banglaSymbols = await LayoutLoader.LoadFromEmbeddedResourceAsync("Bangla.SymbolLayout.json");
        Assert.Equal(LayoutLayer.Symbols, banglaSymbols.Layer);
        Assert.Equal(4, banglaSymbols.Rows.Count);

        var englishQwerty = await LayoutLoader.LoadFromEmbeddedResourceAsync("English.QwertyLayout.json");
        Assert.Equal(LanguageMode.English, englishQwerty.Language);
        Assert.Equal(5, englishQwerty.Rows.Count);
        Assert.Equal("q", englishQwerty.Rows[1].Keys[0].UnicodeOutput);
        Assert.Equal("Q", englishQwerty.Rows[1].Keys[0].AlternateOutput);

        var englishSymbols = await LayoutLoader.LoadFromEmbeddedResourceAsync("English.SymbolLayout.json");
        Assert.Equal(LayoutLayer.Symbols, englishSymbols.Layer);
        Assert.Equal(4, englishSymbols.Rows.Count);
    }

    [Fact]
    public async Task LayoutLoader_RejectsPathTraversalAttempts()
    {
        var loader = new LayoutLoader(_tempRoot);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => loader.LoadFromFileAsync(Path.Combine("..", "..", "README.md")));
    }

    [Fact]
    public async Task LayoutLoader_FallsBackToEmbeddedResource_WhenDiskFileIsMissingOrCorrupted()
    {
        var banglaDir = Path.Combine(_tempRoot, "Bangla");
        Directory.CreateDirectory(banglaDir);
        await File.WriteAllTextAsync(Path.Combine(banglaDir, "PrimaryLayout.json"), "{ INVALID CORRUPTED JSON");

        var loader = new LayoutLoader(_tempRoot);
        var recovered = await loader.LoadWithEmbeddedFallbackAsync(
            Path.Combine("Bangla", "PrimaryLayout.json"),
            "Bangla.PrimaryLayout.json");

        Assert.NotNull(recovered);
        Assert.Equal("Bangla.Primary", recovered.Id);
        Assert.Equal(5, recovered.Rows.Count);
    }

    [Fact]
    public async Task LayoutManager_TogglesLanguageAndLayers_AndNotifiesMessenger()
    {
        var messenger = new StrongReferenceMessenger();
        LanguageMode? broadcastLanguage = null;
        messenger.Register<LayoutLoaderTests, LanguageChangedMessage>(
            this,
            (_, msg) => broadcastLanguage = msg.Language);

        var loader = new LayoutLoader(_tempRoot);
        var manager = new LayoutManager(loader, messenger);
        await manager.InitializeAsync();

        Assert.Equal(LanguageMode.Bangla, manager.ActiveLanguage);
        Assert.Equal(LayoutLayer.Primary, manager.ActiveLayer);
        Assert.Equal("Bangla.Primary", manager.CurrentLayout.Id);

        manager.ToggleShift();
        Assert.Equal(LayoutLayer.Shift, manager.ActiveLayer);
        Assert.Equal("Bangla.Shift", manager.CurrentLayout.Id);

        manager.SetLayer(LayoutLayer.Numbers);
        Assert.Equal(LayoutLayer.Numbers, manager.ActiveLayer);
        Assert.Equal("Bangla.Numbers", manager.CurrentLayout.Id);

        manager.ToggleLanguage();
        Assert.Equal(LanguageMode.English, manager.ActiveLanguage);
        Assert.Equal(LayoutLayer.Primary, manager.ActiveLayer);
        Assert.Equal("English.Qwerty", manager.CurrentLayout.Id);
        Assert.Equal(LanguageMode.English, broadcastLanguage);

        // Verify English Shift promotes uppercase letters
        manager.ToggleShift();
        Assert.Equal(LayoutLayer.Shift, manager.ActiveLayer);
        Assert.Equal("English.Shift", manager.CurrentLayout.Id);
        Assert.Equal("Q", manager.CurrentLayout.Rows[1].Keys[0].UnicodeOutput);
    }

    [Fact]
    public async Task KeyboardViewModel_HandlesOneShotShift_LayerTransitions_AndLanguagePersistence()
    {
        var appPaths = new AppPaths(_tempRoot);
        var settingsService = new JsonSettingsService(appPaths);
        await settingsService.LoadAsync();

        var loader = new LayoutLoader(_tempRoot);
        var layoutManager = new LayoutManager(loader, new StrongReferenceMessenger());
        await layoutManager.InitializeAsync();

        using var vm = new KeyboardViewModel(layoutManager, settingsService, inputEngine: null, new StrongReferenceMessenger());

        var dispatchedKeys = new List<KeyDefinition>();
        vm.KeyDispatched += (_, key) => dispatchedKeys.Add(key);

        // 1. Activate Shift via Shift key
        var shiftKey = vm.CurrentLayout.Rows[3].Keys[0];
        Assert.Equal(KeyType.Shift, shiftKey.KeyType);
        await vm.PressKeyAsync(shiftKey);
        Assert.Equal(LayoutLayer.Shift, vm.ActiveLayer);
        Assert.Equal("Bangla.Shift", vm.CurrentLayout.Id);

        // 2. Press a shifted character ('খ') -> should dispatch key AND auto-return to Primary layer (one-shot Shift)
        var shiftedCharKey = vm.CurrentLayout.Rows[2].Keys[5];
        Assert.Equal("খ", shiftedCharKey.UnicodeOutput);
        await vm.PressKeyAsync(shiftedCharKey);

        Assert.Single(dispatchedKeys);
        Assert.Equal("খ", dispatchedKeys[0].UnicodeOutput);
        Assert.Equal(LayoutLayer.Primary, vm.ActiveLayer);
        Assert.Equal("Bangla.Primary", vm.CurrentLayout.Id);

        // 3. Switch to Numbers layer via ?১২৩ key
        var numSwitchKey = vm.CurrentLayout.Rows[4].Keys[0];
        Assert.Equal(KeyType.LayoutSwitch, numSwitchKey.KeyType);
        await vm.PressKeyAsync(numSwitchKey);
        Assert.Equal(LayoutLayer.Numbers, vm.ActiveLayer);
        Assert.Equal("Bangla.Numbers", vm.CurrentLayout.Id);

        // 4. Switch to English via LanguageSwitch key and verify persistence
        var langSwitchKey = vm.CurrentLayout.Rows[3].Keys[3];
        Assert.Equal(KeyType.LanguageSwitch, langSwitchKey.KeyType);
        await vm.PressKeyAsync(langSwitchKey);

        Assert.Equal(LanguageMode.English, vm.ActiveLanguage);
        Assert.Equal(LanguageMode.English, settingsService.Current.ActiveLanguage);
    }

    [Fact]
    public void FormatCombiningGlyphForDisplay_AddsDottedCircleToVowelSignsAndHasanta_AndRejectsDuplicateKeyIds()
    {
        Assert.Equal("\u25CCা", BengaliKeyboard.UI.Controls.KeyboardKeyControl.FormatCombiningGlyphForDisplay("া"));
        Assert.Equal("\u25CCি", BengaliKeyboard.UI.Controls.KeyboardKeyControl.FormatCombiningGlyphForDisplay("ি"));
        Assert.Equal("\u25CC্", BengaliKeyboard.UI.Controls.KeyboardKeyControl.FormatCombiningGlyphForDisplay("্"));
        Assert.Equal("ক", BengaliKeyboard.UI.Controls.KeyboardKeyControl.FormatCombiningGlyphForDisplay("ক"));

        var duplicateIdLayout = new KeyboardLayoutModel
        {
            Id = "Invalid.Duplicate",
            Rows =
            [
                new KeyboardRowModel
                {
                    RowIndex = 0,
                    HeightRatio = 1.0,
                    Keys =
                    [
                        new KeyDefinition { Id = "dup_1", DisplayLabel = "ক", UnicodeOutput = "ক" },
                        new KeyDefinition { Id = "dup_1", DisplayLabel = "খ", UnicodeOutput = "খ" }
                    ]
                }
            ]
        };

        Assert.Throws<InvalidDataException>(() =>
            LayoutLoader.ValidateLayoutModel(duplicateIdLayout, "DuplicateTest"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup lock
        }
    }
}
