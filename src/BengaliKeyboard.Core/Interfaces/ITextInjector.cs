namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// System-wide Unicode text injection contract (implemented via Windows TSF and Win32 SendInput fallback).
/// </summary>
public interface ITextInjector
{
    bool IsTargetWindowElevated { get; }

    bool SendUnicodeText(string text);

    bool SendBackspaces(int count);

    bool ReplaceText(int backspaceCount, string replacementText);

    bool SendEnter();

    bool SendSpace();
}
