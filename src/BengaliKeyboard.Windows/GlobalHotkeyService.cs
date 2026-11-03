using System.Windows.Interop;
using BengaliKeyboard.Core.Interfaces;

namespace BengaliKeyboard.Windows;

/// <summary>
/// Registers conflict-safe system-wide keyboard shortcuts via Win32 <c>RegisterHotKey</c>
/// for toggling language (<c>বাংলা</c> ↔ <c>ENG</c>), showing/hiding the keyboard,
/// and opening the Emoji or Clipboard panels (PRD.md Section 3.3 &amp; 3.9).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public const int HotkeyIdToggleLanguage = 9001;
    public const int HotkeyIdToggleVisibility = 9002;
    public const int HotkeyIdOpenEmoji = 9003;
    public const int HotkeyIdOpenClipboard = 9004;

    private readonly ISettingsService _settingsService;
    private readonly HashSet<int> _registeredIds = [];
    private HwndSource? _messageHwndSource;
    private bool _disposed;

    public IReadOnlyCollection<int> RegisteredHotkeyIds => _registeredIds;

    public event EventHandler? LanguageToggleRequested;

    public event EventHandler? VisibilityToggleRequested;

    public event EventHandler? EmojiPanelRequested;

    public event EventHandler? ClipboardPanelRequested;

    public GlobalHotkeyService(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
    }

    /// <summary>
    /// Creates a lightweight Win32 message receiver window and registers all configured global hotkeys.
    /// Automatically falls back from <c>Ctrl+Space</c> to <c>Alt+Shift+B</c> if another process holds <c>Ctrl+Space</c>.
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureMessageWindowCreated();
        ReloadHotkeysFromSettings();
    }

    public void ReloadHotkeysFromSettings()
    {
        if (_disposed || _messageHwndSource is null)
        {
            return;
        }

        UnregisterAllHotkeys();

        var settings = _settingsService.Current;

        // 1. Toggle Language (with conflict-safe fallback to Alt+Shift+B)
        if (!TryRegisterShortcut(HotkeyIdToggleLanguage, settings.ToggleLanguageHotkey))
        {
            _ = TryRegisterShortcut(HotkeyIdToggleLanguage, "Alt+Shift+B");
        }

        // 2. Show / Hide Keyboard
        _ = TryRegisterShortcut(HotkeyIdToggleVisibility, settings.ToggleKeyboardVisibilityHotkey);

        // 3. Open Emoji Panel
        _ = TryRegisterShortcut(HotkeyIdOpenEmoji, settings.OpenEmojiPanelHotkey);

        // 4. Open Clipboard History
        _ = TryRegisterShortcut(HotkeyIdOpenClipboard, settings.OpenClipboardPanelHotkey);
    }

    /// <summary>
    /// Parses a shortcut string such as <c>"Ctrl+Space"</c>, <c>"Ctrl+Shift+K"</c>, or <c>"Alt+Shift+B"</c>
    /// into Win32 <c>MOD_*</c> flags and Virtual Key code.
    /// </summary>
    public static bool TryParseHotkeyString(string? shortcut, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var token = parts[i];
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= NativeMethods.ModControl;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= NativeMethods.ModAlt;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= NativeMethods.ModShift;
            }
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= NativeMethods.ModWin;
            }
            else
            {
                return false;
            }
        }

        if (modifiers == 0)
        {
            return false;
        }

        var keyToken = parts[^1];
        if (!TryParseKeyToken(keyToken, out virtualKey))
        {
            return false;
        }

        modifiers |= NativeMethods.ModNoRepeat;
        return true;
    }

    /// <summary>
    /// Dispatches a hotkey ID directly (also used for deterministic unit testing).
    /// </summary>
    public bool DispatchHotkeyById(int hotkeyId)
    {
        switch (hotkeyId)
        {
            case HotkeyIdToggleLanguage:
                LanguageToggleRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case HotkeyIdToggleVisibility:
                VisibilityToggleRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case HotkeyIdOpenEmoji:
                EmojiPanelRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case HotkeyIdOpenClipboard:
                ClipboardPanelRequested?.Invoke(this, EventArgs.Empty);
                return true;

            default:
                return false;
        }
    }

    private bool TryRegisterShortcut(int hotkeyId, string? shortcut)
    {
        if (_messageHwndSource is null || _messageHwndSource.Handle == nint.Zero)
        {
            return false;
        }

        if (!TryParseHotkeyString(shortcut, out var modifiers, out var virtualKey))
        {
            return false;
        }

        if (NativeMethods.RegisterHotKey(_messageHwndSource.Handle, hotkeyId, modifiers, virtualKey))
        {
            _registeredIds.Add(hotkeyId);
            return true;
        }

        return false;
    }

    private void EnsureMessageWindowCreated()
    {
        if (_messageHwndSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("BornoKey_HotkeySink")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ParentWindow = new nint(-3) // HWND_MESSAGE
        };

        _messageHwndSource = new HwndSource(parameters);
        _messageHwndSource.AddHook(WndProc);
    }

    private void UnregisterAllHotkeys()
    {
        if (_messageHwndSource is null || _messageHwndSource.Handle == nint.Zero)
        {
            _registeredIds.Clear();
            return;
        }

        foreach (var id in _registeredIds)
        {
            _ = NativeMethods.UnregisterHotKey(_messageHwndSource.Handle, id);
        }

        _registeredIds.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey)
        {
            var hotkeyId = wParam.ToInt32();
            if (DispatchHotkeyById(hotkeyId))
            {
                handled = true;
            }
        }

        return nint.Zero;
    }

    private static bool TryParseKeyToken(string keyToken, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(keyToken))
        {
            return false;
        }

        if (keyToken.Equals("Space", StringComparison.OrdinalIgnoreCase) ||
            keyToken.Equals("Spacebar", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 0x20;
            return true;
        }

        if (keyToken.Length == 1)
        {
            var ch = char.ToUpperInvariant(keyToken[0]);
            if (ch is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                virtualKey = ch;
                return true;
            }
        }

        if (keyToken.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(keyToken.AsSpan(1), out var fnNumber) &&
            fnNumber is >= 1 and <= 12)
        {
            virtualKey = (uint)(0x70 + (fnNumber - 1));
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _disposed = true;
            UnregisterAllHotkeys();
        }
        finally
        {
            if (_messageHwndSource is not null)
            {
                _messageHwndSource.RemoveHook(WndProc);
                _messageHwndSource.Dispose();
                _messageHwndSource = null;
            }
        }
    }
}
