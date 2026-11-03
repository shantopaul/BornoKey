using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BengaliKeyboard.Storage;

/// <summary>
/// Resolves and initializes local application storage directories under %LOCALAPPDATA%\BengaliKeyboard\.
/// Supports custom root injection for isolated unit and integration testing.
/// </summary>
public sealed class AppPaths
{
    public string BaseDirectory { get; }

    public string ConfigDirectory { get; }

    public string DatabaseDirectory { get; }

    public string LogsDirectory { get; }

    public string CacheDirectory { get; }

    public string SettingsFilePath { get; }

    public string UserDatabasePath { get; }

    public string DictionaryDatabasePath { get; }

    public AppPaths(string? customBaseDirectory = null)
    {
        BaseDirectory = string.IsNullOrWhiteSpace(customBaseDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BengaliKeyboard")
            : Path.GetFullPath(customBaseDirectory);

        ConfigDirectory = Path.Combine(BaseDirectory, "Config");
        DatabaseDirectory = Path.Combine(BaseDirectory, "Database");
        LogsDirectory = Path.Combine(BaseDirectory, "Logs");
        CacheDirectory = Path.Combine(BaseDirectory, "Cache");

        SettingsFilePath = Path.Combine(ConfigDirectory, "settings.json");
        UserDatabasePath = Path.Combine(DatabaseDirectory, "keyboard.db");
        DictionaryDatabasePath = Path.Combine(DatabaseDirectory, "dictionary.db");
    }

    /// <summary>
    /// Creates all required subdirectories and hardens NTFS directory ACLs for the current user (CHK-1.2).
    /// </summary>
    public void EnsureDirectoriesCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
        ApplyCurrentUserDirectorySecurity(BaseDirectory);

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }

    private static void ApplyCurrentUserDirectorySecurity(string directoryPath)
    {
        try
        {
            var currentUserSid = WindowsIdentity.GetCurrent().User;
            if (currentUserSid is null)
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            // Disable inheritance while preserving existing explicit rules, then ensure CurrentUser, SYSTEM, and Administrators have full control
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(
                currentUserSid,
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));

            directoryInfo.SetAccessControl(security);
        }
        catch
        {
            // Non-fatal if filesystem does not support NTFS ACL modification
        }
    }
}
