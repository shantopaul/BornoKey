# Installation, Build & Deployment Guide — BornoKey

This guide covers building **BornoKey** from source, publishing framework-dependent or self-contained Windows binaries, compiling the **Inno Setup** installer (`BornoKeySetup.exe`), and managing local user data.

---

## 1. System Requirements

| Requirement | Minimum Specification |
|---|---|
| **Operating System** | Windows 10 (Version 21H2 / Build 19044+) or Windows 11 (64-bit / `x64`) |
| **Runtime (Framework-Dependent)** | .NET 10.0 Windows Desktop Runtime (`Microsoft.WindowsDesktop.App 10.0`) |
| **Runtime (Self-Contained)** | None required (bundled inside installer/output folder) |
| **Disk Space** | `~35 MB` (Framework-Dependent) / `~140 MB` (Self-Contained) |
| **RAM** | `< 100 MB` active working set (`< 60 MB` minimized to System Tray) |
| **Privileges** | Standard User (`asInvoker`) for portable/normal execution; Administrator only when installing to `%ProgramFiles%\BornoKey\` or registering `BengaliKeyboard.TSF.dll` |

---

## 2. Building & Testing from Source

Open PowerShell in the repository root (`BornoKey/`):

```powershell
# 1. Restore NuGet packages and verify zero vulnerable dependencies
dotnet restore BengaliKeyboard.sln
dotnet list BengaliKeyboard.sln package --vulnerable

# 2. Build in Release configuration (0 Warnings, 0 Errors)
dotnet build BengaliKeyboard.sln -c Release

# 3. Execute all unit and integration tests
dotnet test BengaliKeyboard.sln -c Release
```

---

## 3. Publishing Release Binaries

### Option A: Framework-Dependent Release (`win-x64`)
Produces a compact deployment directory in `publish/win-x64` for machines with the .NET 10 Desktop Runtime installed:

```powershell
dotnet publish src/BengaliKeyboard.App/BengaliKeyboard.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o publish/win-x64
```

### Option B: Self-Contained Release (`win-x64`)
Bundles the .NET 10 Desktop Runtime so **BornoKey** runs on any clean Windows 10/11 PC with zero external prerequisites:

```powershell
dotnet publish src/BengaliKeyboard.App/BengaliKeyboard.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o publish/win-x64
```

### Verified Published Layout (`publish/win-x64/`)
The publish target automatically includes all required offline assets:
- `BornoKey.exe` (Primary executable with `app.manifest` `PerMonitorV2` DPI & `asInvoker`)
- `Definitions/Bangla/*.json` & `Definitions/English/*.json` (6 customizable keyboard layout definitions)
- `data/Dictionary/bn_dictionary_seed.json` (Offline Bengali words, bigrams, and trigrams seed)
- `data/Emoji/emoji_catalog.json` (Offline 10-category emoji catalog with Bengali & English keywords)
- `assets/Icons/app-icon.ico` & `assets/Icons/tray-icon.ico`

---

## 4. Building the Windows Installer (`BornoKeySetup.iss`)

1. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe`).
2. Publish the Release binaries into `publish/win-x64` using Option A or Option B above.
3. *(Optional)* To include the native C++ Windows Text Services Framework COM DLL (`BengaliKeyboard.TSF.dll`), build `native/BengaliKeyboard.TSF/BengaliKeyboard.TSF.vcxproj` in `Release|x64` with Visual Studio C++ Desktop workload and copy `BengaliKeyboard.TSF.dll` into `publish/win-x64/`.
4. Compile the installer script:
   ```powershell
   iscc installer/BornoKeySetup.iss
   ```
5. The resulting installer is generated at:
   ```text
   installer/Output/BornoKeySetup-1.0.0-x64.exe
   ```

### Security & Deployment Guarantees in `BornoKeySetup.iss`
- Installs binaries into the Administrator-protected `%ProgramFiles%\BornoKey` (`{autopf}\BornoKey`) directory (`security/security-checklist.md` CHK-08).
- Registers `BengaliKeyboard.TSF.dll` only if present inside `{app}` (`%ProgramFiles%\BornoKey`).
- Quotes the auto-startup registry path in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`"\"C:\Program Files\BornoKey\BornoKey.exe\" --startup"`) to prevent unquoted path hijacking (CHK-09).

---

## 5. Local Data Storage & Uninstallation Cleanup

At runtime, **BornoKey** stores all user configuration and offline databases strictly inside the current user's profile:

```text
%LOCALAPPDATA%\BengaliKeyboard\
├── Config\
│   └── settings.json          # Atomic user preferences, window geometry & theme
├── Database\
│   ├── keyboard.db            # EmojiUsage, DPAPI-encrypted ClipboardHistory & UserDictionary
│   └── dictionary.db          # Read-only seeded BengaliWords, Bigrams & Trigrams
└── Logs\
    └── bornokey-YYYYMMDD.log  # Privacy-redacted diagnostic logs (7-day retention)
```

### Resetting or Removing User Data
- **Inside BornoKey:** Open **System Tray → Settings → Privacy** and click **Clear All Application Data** or **Reset Settings to Defaults**.
- **During Uninstallation:** The Inno Setup uninstaller cleanly stops any running `BornoKey.exe` instance, removes the `HKCU\...\Run` startup entry, unregisters `BengaliKeyboard.TSF.dll` (if installed), and offers an optional prompt to delete `%LOCALAPPDATA%\BengaliKeyboard\`.
