using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using BengaliKeyboard.Core.Interfaces;
using Microsoft.Win32;

namespace BengaliKeyboard.Input.TSF;

/// <summary>
/// Fixed-schema IPC message exchanged between BornoKey's managed input engine and the native TSF COM service.
/// </summary>
public sealed record TsfIpcEnvelope
{
    public required string Command { get; init; }

    public string Text { get; init; } = string.Empty;

    public int BackspaceCount { get; init; }
}

/// <summary>
/// Dual-engine text injection orchestrator and authenticated local Named Pipe bridge to <c>BengaliKeyboard.TSF.dll</c>.
/// Automatically falls back to <see cref="Win32SendInputInjector"/> when TSF COM registration is unavailable
/// (Architecture.md Section 1.2 &amp; Rules.md Scenario 8).
/// </summary>
public sealed class TsfBridgeService : ITextInjector, IDisposable
{
    /// <summary>
    /// CLSID of the native BornoKey TSF Text Service (<c>{8F4E2D1A-5C3B-4E9A-9D2B-7A1E6C8F0B3D}</c>).
    /// </summary>
    public static readonly Guid TsfClsid = new("8F4E2D1A-5C3B-4E9A-9D2B-7A1E6C8F0B3D");

    public const int MaxIpcMessageBytes = 4096;

    private static readonly JsonSerializerOptions IpcJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 8
    };

    private readonly ITextInjector _fallbackInjector;
    private readonly string _pipeName;
    private bool _disposed;

    public string PipeName => _pipeName;

    public bool IsNativeTsfRegisteredAndTrusted { get; private set; }

    public bool IsTargetWindowElevated => _fallbackInjector.IsTargetWindowElevated;

    public TsfBridgeService(ITextInjector? fallbackInjector = null, string? customPipeName = null)
    {
        _fallbackInjector = fallbackInjector ?? new Win32SendInputInjector();

        var userSid = GetCurrentUserSidString();
        _pipeName = string.IsNullOrWhiteSpace(customPipeName)
            ? $"BornoKey_TsfBridge_{userSid}"
            : customPipeName;

        IsNativeTsfRegisteredAndTrusted = VerifyRegisteredNativeTsfTrust();
    }

    public bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        return _fallbackInjector.SendUnicodeText(text);
    }

    public bool SendBackspaces(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        return _fallbackInjector.SendBackspaces(count);
    }

    public bool ReplaceText(int backspaceCount, string replacementText)
    {
        return _fallbackInjector.ReplaceText(backspaceCount, replacementText);
    }

    public bool SendEnter() => _fallbackInjector.SendEnter();

    public bool SendSpace() => _fallbackInjector.SendSpace();

    /// <summary>
    /// Validates and parses an incoming IPC payload against the strict 4096-byte fixed schema
    /// (security/security-checklist.md Section 1.5 &amp; 1.7).
    /// </summary>
    public static bool TryParseValidatedIpcMessage(ReadOnlySpan<byte> utf8Payload, out TsfIpcEnvelope? envelope)
    {
        envelope = null;
        if (utf8Payload.IsEmpty || utf8Payload.Length > MaxIpcMessageBytes)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TsfIpcEnvelope>(utf8Payload, IpcJsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Command))
            {
                return false;
            }

            if (parsed.Command is not ("COMMIT" or "REPLACE" or "BACKSPACE" or "PING"))
            {
                return false;
            }

            if (parsed.BackspaceCount is < 0 or > 128 || (parsed.Text?.Length ?? 0) > 256)
            {
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a current-user-restricted Named Pipe server stream for TSF IPC communication
    /// (security/security-checklist.md Finding CHK-05).
    /// </summary>
    public NamedPipeServerStream CreateAuthenticatedServerStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pipeSecurity = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is not null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                identity.User,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        var anonymousSid = new SecurityIdentifier(WellKnownSidType.AnonymousSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            anonymousSid,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: MaxIpcMessageBytes,
            outBufferSize: MaxIpcMessageBytes,
            pipeSecurity);
    }

    /// <summary>
    /// Verifies that if <c>BengaliKeyboard.TSF.dll</c> is registered under <c>HKLM\SOFTWARE\Classes\CLSID</c>,
    /// its binary path resides inside an Administrator-protected Program Files directory
    /// (security/security-checklist.md Finding CHK-08).
    /// </summary>
    public static bool VerifyRegisteredNativeTsfTrust()
    {
        try
        {
            var clsidKeyPath = $@"SOFTWARE\Classes\CLSID\{{{TsfClsid:D}}}\InprocServer32";
            using var key = Registry.LocalMachine.OpenSubKey(clsidKeyPath, writable: false);
            var dllPath = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(dllPath))
            {
                return false;
            }

            return IsTrustedProgramFilesPath(dllPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies that a native DLL path resolves strictly inside <c>%ProgramFiles%</c> or <c>%ProgramFiles(x86)%</c>
    /// without path traversal segments.
    /// </summary>
    public static bool IsTrustedProgramFilesPath(string candidateDllPath)
    {
        if (string.IsNullOrWhiteSpace(candidateDllPath))
        {
            return false;
        }

        try
        {
            var trimmed = candidateDllPath.Trim().Trim('"');
            var fullPath = Path.GetFullPath(trimmed);

            if (!fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var inProgramFiles = !string.IsNullOrWhiteSpace(programFiles)
                && fullPath.StartsWith(
                    Path.GetFullPath(programFiles).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);

            var inProgramFilesX86 = !string.IsNullOrWhiteSpace(programFilesX86)
                && fullPath.StartsWith(
                    Path.GetFullPath(programFilesX86).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);

            return inProgramFiles || inProgramFilesX86;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentUserSidString()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? Environment.UserName;
        }
        catch
        {
            return Environment.UserName;
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
