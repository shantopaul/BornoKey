using System.Diagnostics;
using System.Runtime.InteropServices;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Input.TSF;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;

namespace BengaliKeyboard.Input;

/// <summary>
/// Routes physical keyboard presses through <see cref="IBengaliInputEngine"/> when Bangla mode is active.
/// Dynamically installs <c>WH_KEYBOARD_LL</c> only during <see cref="LanguageMode.Bangla"/>,
/// automatically unhooks during <see cref="LanguageMode.English"/> or Windows Session Lock
/// (security/security-checklist.md Finding CHK-03), and guarantees hook cleanup on disposal/exit.
/// </summary>
public sealed class PhysicalKeyboardService : IRecipient<LanguageChangedMessage>, IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const uint LlkhfInjected = 0x00000010;

    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12; // Alt
    private const int VkSpace = 0x20;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;

    // Direct Ridmik layout mapping for physical keyboard virtual keys (Unshifted -> Primary Bangla, Shifted -> Shift Bangla)
    private static readonly IReadOnlyDictionary<int, (string Primary, string Shifted, KeyType PrimaryType, KeyType ShiftedType)> RidmikPhysicalKeyMap =
        new Dictionary<int, (string, string, KeyType, KeyType)>
        {
            // Number row (VK_0 .. VK_9)
            [0x31] = ("১", "!", KeyType.Number, KeyType.Punctuation),
            [0x32] = ("২", "@", KeyType.Number, KeyType.Punctuation),
            [0x33] = ("৩", "#", KeyType.Number, KeyType.Punctuation),
            [0x34] = ("৪", "৳", KeyType.Number, KeyType.Punctuation),
            [0x35] = ("৫", "%", KeyType.Number, KeyType.Punctuation),
            [0x36] = ("৬", "ৃ", KeyType.Number, KeyType.VowelSign),
            [0x37] = ("৭", "ঁ", KeyType.Number, KeyType.Character),
            [0x38] = ("৮", "*", KeyType.Number, KeyType.Punctuation),
            [0x39] = ("৯", "(", KeyType.Number, KeyType.Punctuation),
            [0x30] = ("০", ")", KeyType.Number, KeyType.Punctuation),

            // Top letter row (Q W E R T Y U I O P)
            [0x51] = ("ঙ", "ং", KeyType.Character, KeyType.Character),       // Q
            [0x57] = ("য", "য়", KeyType.Character, KeyType.Character),      // W
            [0x45] = ("ড", "ঢ", KeyType.Character, KeyType.Character),       // E
            [0x52] = ("প", "ফ", KeyType.Character, KeyType.Character),       // R
            [0x54] = ("ট", "ঠ", KeyType.Character, KeyType.Character),       // T
            [0x59] = ("চ", "ছ", KeyType.Character, KeyType.Character),       // Y
            [0x55] = ("জ", "ঝ", KeyType.Character, KeyType.Character),       // U
            [0x49] = ("হ", "ঞ", KeyType.Character, KeyType.Character),       // I
            [0x4F] = ("গ", "ঘ", KeyType.Character, KeyType.Character),       // O
            [0x50] = ("ড়", "ঢ়", KeyType.Character, KeyType.Character),     // P

            // Home letter row (A S D F G H J K L)
            [0x41] = ("া", "অ", KeyType.VowelSign, KeyType.Character),       // A
            [0x53] = ("ি", "ী", KeyType.VowelSign, KeyType.VowelSign),       // S
            [0x44] = ("ু", "ূ", KeyType.VowelSign, KeyType.VowelSign),       // D
            [0x46] = ("্", "অ", KeyType.Hasanta, KeyType.Character),         // F
            [0x47] = ("ে", "ৈ", KeyType.VowelSign, KeyType.VowelSign),       // G
            [0x48] = ("ো", "ৌ", KeyType.VowelSign, KeyType.VowelSign),       // H
            [0x4A] = ("ক", "খ", KeyType.Character, KeyType.Character),       // J
            [0x4B] = ("ত", "থ", KeyType.Character, KeyType.Character),       // K
            [0x4C] = ("দ", "ধ", KeyType.Character, KeyType.Character),       // L

            // Bottom letter row (Z X C V B N M)
            [0x5A] = ("্র", "্য", KeyType.Character, KeyType.Character),     // Z (Ra-phala / Ya-phala)
            [0x58] = ("শ", "ষ", KeyType.Character, KeyType.Character),       // X
            [0x43] = ("স", "ৎ", KeyType.Character, KeyType.Character),       // C
            [0x56] = ("র", "ল", KeyType.Character, KeyType.Character),       // V
            [0x42] = ("ন", "ণ", KeyType.Character, KeyType.Character),       // B
            [0x4E] = ("ম", "ঃ", KeyType.Character, KeyType.Character),       // N
            [0x4D] = ("ব", "ভ", KeyType.Character, KeyType.Character),       // M

            // Punctuation (OEM period -> Dari '।', OEM comma -> ',', OEM question -> '?')
            [0xBE] = ("।", ".", KeyType.Punctuation, KeyType.Punctuation),   // VK_OEM_PERIOD
            [0xBC] = (",", "<", KeyType.Punctuation, KeyType.Punctuation),   // VK_OEM_COMMA
            [0xBF] = ("/", "?", KeyType.Punctuation, KeyType.Punctuation)    // VK_OEM_2
        };

    private readonly IBengaliInputEngine _inputEngine;
    private readonly IMessenger _messenger;
    private readonly LowLevelKeyboardProc _hookProcDelegate;
    private nint _hookHandle = nint.Zero;
    private bool _isEnabled;
    private bool _isSessionLocked;
    private bool _disposed;

    /// <summary>
    /// True when the low-level Win32 keyboard hook (<c>WH_KEYBOARD_LL</c>) is currently installed.
    /// </summary>
    public bool IsHookInstalled => _hookHandle != nint.Zero;

    public bool IsEnabled => _isEnabled;

    public bool IsSessionLocked => _isSessionLocked;

    public PhysicalKeyboardService(IBengaliInputEngine inputEngine, IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(inputEngine);

        _inputEngine = inputEngine;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _hookProcDelegate = HookCallback;

        _messenger.Register(this);
        SystemEvents.SessionSwitch += OnSystemSessionSwitch;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>
    /// Enables physical keyboard routing and synchronizes the OS hook state with the active language.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isEnabled = true;
        UpdateHookLifecycleState();
    }

    /// <summary>
    /// Disables physical keyboard routing and immediately removes any installed OS hook.
    /// </summary>
    public void Stop()
    {
        _isEnabled = false;
        UninstallHookSafe();
    }

    public void Receive(LanguageChangedMessage message)
    {
        UpdateHookLifecycleState();
    }

    /// <summary>
    /// Maps a Win32 virtual key code and Shift state to a Ridmik <see cref="KeyDefinition"/> when in Bangla mode.
    /// Exposed for deterministic unit testing without requiring an active OS hook.
    /// </summary>
    public static bool TryMapVirtualKeyToKeyDefinition(int virtualKeyCode, bool isShifted, out KeyDefinition? keyDefinition)
    {
        keyDefinition = null;

        switch (virtualKeyCode)
        {
            case VkBack:
                keyDefinition = new KeyDefinition
                {
                    Id = "phys_backspace",
                    DisplayLabel = "⌫",
                    UnicodeOutput = "\b",
                    KeyType = KeyType.Backspace
                };
                return true;

            case VkSpace:
                keyDefinition = new KeyDefinition
                {
                    Id = "phys_space",
                    DisplayLabel = "বাংলা",
                    UnicodeOutput = " ",
                    KeyType = KeyType.Space
                };
                return true;

            case VkReturn:
                keyDefinition = new KeyDefinition
                {
                    Id = "phys_enter",
                    DisplayLabel = "↵",
                    UnicodeOutput = "\n",
                    KeyType = KeyType.Enter
                };
                return true;
        }

        if (RidmikPhysicalKeyMap.TryGetValue(virtualKeyCode, out var entry))
        {
            var output = isShifted ? entry.Shifted : entry.Primary;
            var keyType = isShifted ? entry.ShiftedType : entry.PrimaryType;

            keyDefinition = new KeyDefinition
            {
                Id = $"phys_vk_{virtualKeyCode:X2}",
                DisplayLabel = output,
                UnicodeOutput = output,
                AlternateOutput = isShifted ? entry.Primary : entry.Shifted,
                KeyType = keyType
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Simulates a session lock/unlock transition and updates the hook state accordingly.
    /// </summary>
    public void SetSessionLockState(bool isLocked)
    {
        _isSessionLocked = isLocked;
        UpdateHookLifecycleState();
    }

    private void UpdateHookLifecycleState()
    {
        var shouldBeHooked = !_disposed
            && _isEnabled
            && !_isSessionLocked
            && _inputEngine.CurrentLanguage == LanguageMode.Bangla;

        if (shouldBeHooked)
        {
            InstallHookIfNeeded();
        }
        else
        {
            UninstallHookSafe();
        }
    }

    private void InstallHookIfNeeded()
    {
        if (_hookHandle != nint.Zero)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule is not null
            ? GetModuleHandle(currentModule.ModuleName)
            : nint.Zero;

        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProcDelegate, moduleHandle, 0);
    }

    private void UninstallHookSafe()
    {
        var handle = Interlocked.Exchange(ref _hookHandle, nint.Zero);
        if (handle != nint.Zero)
        {
            _ = UnhookWindowsHookEx(handle);
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
        {
            var hookStruct = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

            // 1. Never intercept synthetic keystrokes injected by BornoKey or other input tools
            if (hookStruct.dwExtraInfo == Win32SendInputInjector.BornoKeyInjectedExtraInfo ||
                (hookStruct.flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // 2. Only process when Bangla mode is active and session is unlocked
            if (_inputEngine.CurrentLanguage != LanguageMode.Bangla || _isSessionLocked)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var vkCode = (int)hookStruct.vkCode;

            // 3. Pass through modifier keys and OS/app shortcuts (Ctrl+C, Ctrl+V, Alt+Tab, Win+V, etc.)
            if (IsModifierKey(vkCode) || AreShortcutModifiersDown())
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var isShiftDown = IsKeyDown(VkShift) || IsKeyDown(VkLShift) || IsKeyDown(VkRShift);
            if (TryMapVirtualKeyToKeyDefinition(vkCode, isShiftDown, out var mappedKey) && mappedKey is not null)
            {
                _ = _inputEngine.ProcessKeyAsync(mappedKey, isShiftDown);

                // Suppress raw English keystroke when mapped to a Bengali character/vowel sign/hasanta/number/backspace
                if (mappedKey.KeyType is KeyType.Character or KeyType.VowelSign or KeyType.Hasanta or KeyType.Number or KeyType.Punctuation or KeyType.Backspace or KeyType.Space or KeyType.Enter)
                {
                    return new nint(1);
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(int vkCode) =>
        vkCode is VkShift or VkLShift or VkRShift
            or VkControl or VkLControl or VkRControl
            or VkMenu or VkLMenu or VkRMenu
            or VkLWin or VkRWin;

    private static bool AreShortcutModifiersDown() =>
        IsKeyDown(VkControl) || IsKeyDown(VkLControl) || IsKeyDown(VkRControl)
        || IsKeyDown(VkMenu) || IsKeyDown(VkLMenu) || IsKeyDown(VkRMenu)
        || IsKeyDown(VkLWin) || IsKeyDown(VkRWin);

    private static bool IsKeyDown(int vkCode) =>
        (GetAsyncKeyState(vkCode) & 0x8000) != 0;

    private void OnSystemSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect)
        {
            SetSessionLockState(true);
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect)
        {
            SetSessionLockState(false);
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        UninstallHookSafe();
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
            _isEnabled = false;
            SystemEvents.SessionSwitch -= OnSystemSessionSwitch;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            _messenger.UnregisterAll(this);
        }
        finally
        {
            UninstallHookSafe();
        }
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
