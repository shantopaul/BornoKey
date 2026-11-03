using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using BengaliKeyboard.Core.Interfaces;

namespace BengaliKeyboard.Input.TSF;

/// <summary>
/// Direct Win32 <c>SendInput</c> (<c>KEYEVENTF_UNICODE</c>) text and backspace injector.
/// Tags all synthesized events with <see cref="BornoKeyInjectedExtraInfo"/> so low-level keyboard hooks
/// never re-intercept BornoKey's own output, and detects Windows UIPI elevation boundaries gracefully.
/// </summary>
public sealed class Win32SendInputInjector : ITextInjector
{
    /// <summary>
    /// Marker placed in <c>KEYBDINPUT.dwExtraInfo</c> to identify synthetic keystrokes emitted by BornoKey.
    /// </summary>
    public static readonly nint BornoKeyInjectedExtraInfo = unchecked((nint)0x424F524E4F4B4559L);

    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;

    private const ushort VkBack = 0x08;
    private const ushort VkReturn = 0x0D;
    private const ushort VkSpace = 0x20;

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationInfoClass = 20;
    private const int ErrorAccessDenied = 5;
    private const int MaxEventsPerBatch = 256;

    private readonly bool _isCurrentProcessElevated;
    private bool _isTargetWindowElevated;

    public bool IsTargetWindowElevated => _isTargetWindowElevated;

    public event EventHandler<bool>? TargetElevationChanged;

    public Win32SendInputInjector()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        _isCurrentProcessElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (CheckIfForegroundWindowBlocksInput())
        {
            return false;
        }

        var inputs = new List<Input>(text.Length * 2);
        foreach (var utf16Unit in text)
        {
            AppendUnicodeCharInputs(inputs, utf16Unit);
        }

        return DispatchBatchedInputs(inputs);
    }

    public bool SendBackspaces(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (CheckIfForegroundWindowBlocksInput())
        {
            return false;
        }

        var clampedCount = Math.Min(count, 128);
        var inputs = new List<Input>(clampedCount * 2);
        for (var i = 0; i < clampedCount; i++)
        {
            AppendVirtualKeyInputs(inputs, VkBack);
        }

        return DispatchBatchedInputs(inputs);
    }

    public bool ReplaceText(int backspaceCount, string replacementText)
    {
        if (backspaceCount <= 0 && string.IsNullOrEmpty(replacementText))
        {
            return true;
        }

        if (CheckIfForegroundWindowBlocksInput())
        {
            return false;
        }

        var clampedBackspaces = Math.Clamp(backspaceCount, 0, 128);
        var safeText = replacementText ?? string.Empty;
        var inputs = new List<Input>((clampedBackspaces + safeText.Length) * 2);

        for (var i = 0; i < clampedBackspaces; i++)
        {
            AppendVirtualKeyInputs(inputs, VkBack);
        }

        foreach (var utf16Unit in safeText)
        {
            AppendUnicodeCharInputs(inputs, utf16Unit);
        }

        return DispatchBatchedInputs(inputs);
    }

    public bool SendEnter()
    {
        if (CheckIfForegroundWindowBlocksInput())
        {
            return false;
        }

        var inputs = new List<Input>(2);
        AppendVirtualKeyInputs(inputs, VkReturn);
        return DispatchBatchedInputs(inputs);
    }

    public bool SendSpace()
    {
        if (CheckIfForegroundWindowBlocksInput())
        {
            return false;
        }

        var inputs = new List<Input>(2);
        AppendVirtualKeyInputs(inputs, VkSpace);
        return DispatchBatchedInputs(inputs);
    }

    private static void AppendUnicodeCharInputs(List<Input> list, char utf16CodeUnit)
    {
        list.Add(new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeybdInput
                {
                    wVk = 0,
                    wScan = utf16CodeUnit,
                    dwFlags = KeyEventFUnicode,
                    time = 0,
                    dwExtraInfo = BornoKeyInjectedExtraInfo
                }
            }
        });

        list.Add(new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeybdInput
                {
                    wVk = 0,
                    wScan = utf16CodeUnit,
                    dwFlags = KeyEventFUnicode | KeyEventFKeyUp,
                    time = 0,
                    dwExtraInfo = BornoKeyInjectedExtraInfo
                }
            }
        });
    }

    private static void AppendVirtualKeyInputs(List<Input> list, ushort virtualKey)
    {
        list.Add(new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeybdInput
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = BornoKeyInjectedExtraInfo
                }
            }
        });

        list.Add(new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeybdInput
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = KeyEventFKeyUp,
                    time = 0,
                    dwExtraInfo = BornoKeyInjectedExtraInfo
                }
            }
        });
    }

    private bool DispatchBatchedInputs(List<Input> allInputs)
    {
        if (allInputs.Count == 0)
        {
            return true;
        }

        var structSize = Marshal.SizeOf<Input>();
        var offset = 0;

        while (offset < allInputs.Count)
        {
            var count = Math.Min(MaxEventsPerBatch, allInputs.Count - offset);
            var batch = allInputs.GetRange(offset, count).ToArray();

            var sent = SendInput((uint)batch.Length, batch, structSize);
            if (sent == 0)
            {
                var lastError = Marshal.GetLastWin32Error();
                if (lastError == ErrorAccessDenied)
                {
                    UpdateElevationState(true);
                }

                return false;
            }

            offset += count;
        }

        UpdateElevationState(false);
        return true;
    }

    /// <summary>
    /// Detects whether the current foreground window belongs to an elevated Administrator process
    /// while BornoKey is running as a standard user (<c>asInvoker</c>), which triggers Windows UIPI blocking.
    /// </summary>
    public bool CheckIfForegroundWindowBlocksInput()
    {
        if (_isCurrentProcessElevated)
        {
            UpdateElevationState(false);
            return false;
        }

        var foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundHwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == nint.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorAccessDenied)
            {
                UpdateElevationState(true);
                return true;
            }

            return false;
        }

        try
        {
            if (!OpenProcessToken(processHandle, TokenQuery, out var tokenHandle) || tokenHandle == nint.Zero)
            {
                return false;
            }

            try
            {
                var elevationSize = Marshal.SizeOf<TokenElevation>();
                var elevationPtr = Marshal.AllocHGlobal(elevationSize);
                try
                {
                    if (GetTokenInformation(tokenHandle, TokenElevationInfoClass, elevationPtr, (uint)elevationSize, out _))
                    {
                        var elevation = Marshal.PtrToStructure<TokenElevation>(elevationPtr);
                        var isElevated = elevation.TokenIsElevated != 0;
                        UpdateElevationState(isElevated);
                        return isElevated;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(elevationPtr);
                }
            }
            finally
            {
                _ = CloseHandle(tokenHandle);
            }
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }

        UpdateElevationState(false);
        return false;
    }

    private void UpdateElevationState(bool isElevated)
    {
        if (_isTargetWindowElevated == isElevated)
        {
            return;
        }

        _isTargetWindowElevated = isElevated;
        TargetElevationChanged?.Invoke(this, isElevated);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mi;

        [FieldOffset(0)]
        public KeybdInput ki;

        [FieldOffset(0)]
        public HardwareInput hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public uint TokenIsElevated;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        nint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
