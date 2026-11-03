using System.Diagnostics;
using System.IO;
using BengaliKeyboard.Core.Interfaces;
using Microsoft.Win32;

namespace BengaliKeyboard.Windows;

/// <summary>
/// Manages Windows auto-startup registration under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// Enforces quoted executable paths (<c>"\"C:\\...\\BengaliKeyboard.App.exe\" --startup"</c>) to prevent
/// unquoted path execution hijacking on paths containing spaces (security/security-checklist.md Finding CHK-09).
/// </summary>
public sealed class StartupService : IStartupService
{
    public const string DefaultRunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RegistryValueName = "BornoKey";
    public const string StartupArgument = "--startup";

    private readonly string _runRegistrySubKey;
    private readonly string _executablePath;

    public string RunRegistrySubKey => _runRegistrySubKey;

    public string ExecutablePath => _executablePath;

    public StartupService(string? customRegistrySubKey = null, string? customExecutablePath = null)
    {
        _runRegistrySubKey = string.IsNullOrWhiteSpace(customRegistrySubKey)
            ? DefaultRunRegistryPath
            : customRegistrySubKey;

        _executablePath = string.IsNullOrWhiteSpace(customExecutablePath)
            ? ResolveCurrentExecutablePath()
            : customExecutablePath;
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runRegistrySubKey, writable: false);
            var existingValue = key?.GetValue(RegistryValueName) as string;
            if (string.IsNullOrWhiteSpace(existingValue))
            {
                return false;
            }

            // Must begin with a literal double-quote and contain the expected executable path
            return existingValue.StartsWith('"') &&
                   existingValue.Contains(_executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runRegistrySubKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enable)
            {
                var quotedCommand = FormatQuotedStartupCommand(_executablePath);
                key.SetValue(RegistryValueName, quotedCommand, RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(RegistryValueName) is not null)
                {
                    key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Formats a safe, double-quoted Windows startup command line for the specified executable path,
    /// rejecting relative paths, non-<c>.exe</c> targets, or shell metacharacters (Finding CHK-09).
    /// </summary>
    public static string FormatQuotedStartupCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path cannot be empty.", nameof(executablePath));
        }

        var trimmed = executablePath.Trim().Trim('"');
        if (trimmed.IndexOfAny(['"', '&', '|', ';', '\r', '\n', '<', '>']) >= 0)
        {
            throw new ArgumentException("Executable path contains disallowed command characters.", nameof(executablePath));
        }

        var fullPath = Path.GetFullPath(trimmed);
        if (!Path.IsPathRooted(fullPath) || !fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Startup target must be a rooted .exe file path.", nameof(executablePath));
        }

        return $"\"{fullPath}\" {StartupArgument}";
    }

    private static string ResolveCurrentExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(processPath);
        }

        using var process = Process.GetCurrentProcess();
        var mainModulePath = process.MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(mainModulePath))
        {
            return Path.GetFullPath(mainModulePath);
        }

        return Path.Combine(AppContext.BaseDirectory, "BengaliKeyboard.App.exe");
    }
}
