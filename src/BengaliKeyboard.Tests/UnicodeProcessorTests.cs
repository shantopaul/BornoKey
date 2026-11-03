using System.IO;
using System.Text;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Input;
using BengaliKeyboard.Input.TSF;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class UnicodeProcessorTests
{
    [Fact]
    public void Compose_ShouldTransformBengaliVowelSigns_AndComputeExactBackspaceDiffs()
    {
        var processor = new UnicodeProcessor();

        // 1. অ + া -> আ (1 backspace to replace অ with আ)
        var step1 = processor.Compose("", "অ");
        Assert.Equal("অ", step1.UpdatedBuffer);
        Assert.Equal(0, step1.BackspaceCount);
        Assert.Equal("অ", step1.TextToInject);

        var step2 = processor.Compose(step1.UpdatedBuffer, "া");
        Assert.Equal("আ", step2.UpdatedBuffer);
        Assert.Equal(1, step2.BackspaceCount);
        Assert.Equal("আ", step2.TextToInject);

        // 2. ক + ে + া -> কো (NFC normalized \u0995\u09CB)
        var ke = processor.Compose("", "কে");
        var ko = processor.Compose(ke.UpdatedBuffer, "া");
        Assert.Equal("কো", ko.UpdatedBuffer);
        Assert.Equal(1, ko.BackspaceCount);
        Assert.Equal("ো", ko.TextToInject);

        // 3. ক + ি + ি -> কী (double-tap short i-kar upgrades to long ii-kar)
        var ki = processor.Compose("", "কি");
        var kii = processor.Compose(ki.UpdatedBuffer, "ি");
        Assert.Equal("কী", kii.UpdatedBuffer);
        Assert.Equal(1, kii.BackspaceCount);
        Assert.Equal("ী", kii.TextToInject);

        // 4. Replacing a mistyped Kar: কা + ি -> কি (never stacks invalid *কাি)
        var ka = processor.Compose("", "কা");
        var replaced = processor.Compose(ka.UpdatedBuffer, "ি");
        Assert.Equal("কি", replaced.UpdatedBuffer);
        Assert.Equal(1, replaced.BackspaceCount);
        Assert.Equal("ি", replaced.TextToInject);
    }

    [Fact]
    public void Compose_ShouldFormJuktakhorConjuncts_AndPreventDuplicateHasanta()
    {
        var processor = new UnicodeProcessor();

        // ক্ষ (ক + ্ + ষ)
        var k = processor.Compose("", "ক");
        var kh = processor.Compose(k.UpdatedBuffer, "্");
        var khDup = processor.Compose(kh.UpdatedBuffer, "্");
        Assert.Equal("ক্", khDup.UpdatedBuffer);
        Assert.Equal(0, khDup.BackspaceCount);
        Assert.Empty(khDup.TextToInject);

        var ksha = processor.Compose(khDup.UpdatedBuffer, "ষ");
        Assert.Equal("ক্ষ", ksha.UpdatedBuffer);
        Assert.Equal(1, UnicodeProcessor.CountGraphemeClusters(ksha.UpdatedBuffer));

        // জ্ঞ (জ + ্ + ঞ), ত্র (ত + ্ + র), ম্প (ম + ্ + প), স্ত্র (স + ্ + ত + ্ + র)
        Assert.Equal("জ্ঞ", processor.Compose("জ্", "ঞ").UpdatedBuffer);
        Assert.Equal("ত্র", processor.Compose("ত্", "র").UpdatedBuffer);
        Assert.Equal("ম্প", processor.Compose("ম্", "প").UpdatedBuffer);
        Assert.Equal(1, UnicodeProcessor.CountGraphemeClusters("স্ত্র"));
        Assert.Equal(2, UnicodeProcessor.CountGraphemeClusters("বাংলা"));
    }

    [Fact]
    public void Backspace_ShouldSupportBothModifierLevel_AndFullGraphemeClusterDeletion()
    {
        var processor = new UnicodeProcessor();

        // Modifier-level backspace on "কা" removes 'া' leaving "ক"
        var afterKarDelete = processor.Backspace("কা", deleteEntireGraphemeCluster: false);
        Assert.Equal("ক", afterKarDelete.UpdatedBuffer);
        Assert.Equal(1, afterKarDelete.BackspaceCount);
        Assert.Empty(afterKarDelete.TextToInject);

        // Grapheme-cluster backspace on "ক্ষ" removes the entire 3-codepoint conjunct cluster (3 scalar backspaces)
        var afterClusterDelete = processor.Backspace("ক্ষ", deleteEntireGraphemeCluster: true);
        Assert.Empty(afterClusterDelete.UpdatedBuffer);
        Assert.Equal(3, afterClusterDelete.BackspaceCount);
        Assert.Empty(afterClusterDelete.TextToInject);
    }

    [Fact]
    public async Task BengaliInputEngine_ShouldComposeWords_AndReplacePrefixWhenCandidateCommitted()
    {
        var processor = new UnicodeProcessor();
        var fakeInjector = new FakeTextInjector();
        using var engine = new BengaliInputEngine(processor, fakeInjector);

        await engine.ProcessKeyAsync(new KeyDefinition { Id = "k1", DisplayLabel = "আ", UnicodeOutput = "আ", KeyType = KeyType.Character });
        await engine.ProcessKeyAsync(new KeyDefinition { Id = "k2", DisplayLabel = "ম", UnicodeOutput = "ম", KeyType = KeyType.Character });
        await engine.ProcessKeyAsync(new KeyDefinition { Id = "k3", DisplayLabel = "া", UnicodeOutput = "া", KeyType = KeyType.VowelSign });

        Assert.Equal("আমা", engine.CurrentWordBuffer);
        Assert.Equal("আমা", fakeInjector.SimulatedDocument.ToString());

        await engine.CommitCandidateAsync(new SuggestionCandidate
        {
            Word = "আমার",
            Score = 1.0,
            ReplacePrefixLength = 3,
            IsNextWordPrediction = false
        });

        Assert.Empty(engine.CurrentWordBuffer);
        var recent = Assert.Single(engine.RecentWords);
        Assert.Equal("আমার", recent);
        Assert.Equal("আমার ", fakeInjector.SimulatedDocument.ToString());
    }

    [Fact]
    public void PhysicalKeyboardService_ShouldMapRidmikKeys_AndUnhookInEnglishOrLockedState()
    {
        // Verify Ridmik virtual key mappings
        Assert.True(PhysicalKeyboardService.TryMapVirtualKeyToKeyDefinition(0x4A, isShifted: false, out var keyJ));
        Assert.Equal("ক", keyJ!.UnicodeOutput);

        Assert.True(PhysicalKeyboardService.TryMapVirtualKeyToKeyDefinition(0x4A, isShifted: true, out var keyShiftJ));
        Assert.Equal("খ", keyShiftJ!.UnicodeOutput);

        Assert.True(PhysicalKeyboardService.TryMapVirtualKeyToKeyDefinition(0x41, isShifted: false, out var keyA));
        Assert.Equal("া", keyA!.UnicodeOutput);
        Assert.Equal(KeyType.VowelSign, keyA.KeyType);

        Assert.True(PhysicalKeyboardService.TryMapVirtualKeyToKeyDefinition(0x46, isShifted: false, out var keyF));
        Assert.Equal("্", keyF!.UnicodeOutput);
        Assert.Equal(KeyType.Hasanta, keyF.KeyType);

        var processor = new UnicodeProcessor();
        var fakeInjector = new FakeTextInjector();
        using var engine = new BengaliInputEngine(processor, fakeInjector);
        using var physicalService = new PhysicalKeyboardService(engine);

        engine.SetLanguage(LanguageMode.English);
        physicalService.Start();
        Assert.False(physicalService.IsHookInstalled);

        physicalService.SetSessionLockState(true);
        engine.SetLanguage(LanguageMode.Bangla);
        Assert.False(physicalService.IsHookInstalled);
    }

    [Fact]
    public void TsfBridgeService_ShouldValidateIpcMessages_AndVerifyTrustedProgramFilesPaths()
    {
        var validJson = Encoding.UTF8.GetBytes("{\"command\":\"REPLACE\",\"text\":\"বাংলা\",\"backspaceCount\":3}");
        Assert.True(TsfBridgeService.TryParseValidatedIpcMessage(validJson, out var envelope));
        Assert.Equal("REPLACE", envelope!.Command);
        Assert.Equal("বাংলা", envelope.Text);
        Assert.Equal(3, envelope.BackspaceCount);

        var invalidCommandJson = Encoding.UTF8.GetBytes("{\"command\":\"EXEC_SHELL\",\"text\":\"cmd\",\"backspaceCount\":0}");
        Assert.False(TsfBridgeService.TryParseValidatedIpcMessage(invalidCommandJson, out _));

        var oversizedPayload = new byte[TsfBridgeService.MaxIpcMessageBytes + 1];
        Assert.False(TsfBridgeService.TryParseValidatedIpcMessage(oversizedPayload, out _));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var trustedDll = Path.Combine(programFiles, "BornoKey", "BengaliKeyboard.TSF.dll");
        Assert.True(TsfBridgeService.IsTrustedProgramFilesPath(trustedDll));

        var untrustedAppDataDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BengaliKeyboard",
            "BengaliKeyboard.TSF.dll");
        Assert.False(TsfBridgeService.IsTrustedProgramFilesPath(untrustedAppDataDll));

        var traversalDll = Path.Combine(programFiles, "BornoKey", "..", "..", "Users", "Public", "evil.dll");
        Assert.False(TsfBridgeService.IsTrustedProgramFilesPath(traversalDll));
    }

    private sealed class FakeTextInjector : ITextInjector
    {
        public StringBuilder SimulatedDocument { get; } = new();

        public bool IsTargetWindowElevated => false;

        public bool SendUnicodeText(string text)
        {
            SimulatedDocument.Append(text);
            return true;
        }

        public bool SendBackspaces(int count)
        {
            for (var i = 0; i < count && SimulatedDocument.Length > 0; i++)
            {
                SimulatedDocument.Length--;
            }
            return true;
        }

        public bool ReplaceText(int backspaceCount, string replacementText)
        {
            SendBackspaces(backspaceCount);
            SendUnicodeText(replacementText);
            return true;
        }

        public bool SendEnter()
        {
            SimulatedDocument.Append('\n');
            return true;
        }

        public bool SendSpace()
        {
            SimulatedDocument.Append(' ');
            return true;
        }
    }
}
