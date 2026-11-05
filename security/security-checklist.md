# Project-Specific Security Checklist & Findings Ledger — BornoKey

## 0. Implementation Verification Status (Phase 10 Complete)

> **Verification Statement:**  
> All **20 security domains** and all **10 architectural security findings (`CHK-01` through `CHK-10`)** are **100% implemented in source code and verified by automated xUnit tests** (`src/BengaliKeyboard.Tests/`).

---

## 1. 20-Domain Project-Specific Security Checklist

### 1.1 Authentication
- [x] **[Implemented in `SingleInstanceManager.cs` & `TsfBridgeService.cs`]** Verify local Named Pipe / IPC peer identity using `PipeOptions.CurrentUserOnly` and current Windows user SID (`WindowsIdentity.GetCurrent().User`).
- [x] **[Implemented in `TsfBridgeService.cs`]** Verify trusted `%ProgramFiles%\BornoKey\` installation path and Authenticode/file integrity before loading or communicating with `BengaliKeyboard.TSF.dll`.

### 1.2 Authorization / RBAC
- [x] **[Implemented in `src/BengaliKeyboard.App/app.manifest`]** Configure application manifest with `<requestedExecutionLevel level="asInvoker" uiAccess="false" />` so BornoKey runs under standard user privileges.
- [x] **[Implemented in `Win32SendInputInjector.cs`]** Respect Windows UIPI boundaries when an elevated Administrator window is focused; surface a graceful status indicator rather than crashing when `SendInput` is blocked.
- [x] **[Implemented in `AppPaths.cs`]** Restrict NTFS ACLs on `%LOCALAPPDATA%\BengaliKeyboard\` exclusively to `SYSTEM`, `Administrators`, and the owning `CurrentUser` SID (`InheritanceFlags` disabled for other local users).
- [x] **[Implemented in `installer/BornoKeySetup.iss` & `Register.cpp`]** Ensure `BengaliKeyboard.TSF.dll` is installed in an Administrator-protected directory (`%ProgramFiles%\BornoKey\`) before registering COM `InprocServer32`.

### 1.3 Session / Token Security
- [x] **[Implemented in `SingleInstanceManager.cs`]** Scope single-instance Mutex to the current user session (`Local\BornoKey_SingleInstance_<UserSID>`).
- [x] **[Implemented in `PhysicalKeyboardService.cs` & `ClipboardMonitor.cs`]** Detect Windows Secure Desktop / session lock transitions (`SystemEvents.SessionSwitch`) and pause input hooks and clipboard capture during lock screen or UAC prompts.

### 1.4 Input Validation
- [x] **[Implemented in `WindowPositionService.cs` & `WindowStateConfig.cs`]** Clamp restored window coordinates (`Left`, `Top`, `Width`, `Height`) to valid monitor `WorkingArea` bounds and mode min/max dimensions.
- [x] **[Implemented in `AppSettings.cs` & `JsonSettingsService.cs`]** Enforce strict value clamping on `settings.json` deserialization (`Opacity` in `[0.60, 1.00]`, `ClipboardHistoryLimit` in `[10, 500]`, `SuggestionCount` in `[3, 5]`).
- [x] **[Implemented in `LayoutLoader.cs` & `KeyDefinition.cs`]** Validate `Definitions/**/*.json` keyboard layouts (`KeyDefinition.UnicodeOutput` length `<= 8` characters, positive finite `WidthRatio` in `[0.5, 6.0]`, `JsonSerializerOptions.MaxDepth = 16`).
- [x] **[Implemented in `ClipboardMonitor.cs` & `ClipboardEntry.cs`]** Enforce a maximum payload size limit (`<= 64 KB` / `65,536` chars) on incoming `WM_CLIPBOARDUPDATE` text before capturing.

### 1.5 Injection (SQL Injection, Command Injection, Keystroke Injection)
- [x] **[Implemented in `ClipboardRepository.cs`, `EmojiUsageService.cs`, `DictionaryService.cs`]** Use parameterized `SqliteCommand` queries (`@param`) exclusively across all SQLite repositories.
- [x] **[Implemented in `DictionaryService.cs`]** Escape SQLite `LIKE` wildcard characters (`%`, `_`, `\`) using an `ESCAPE '\'` clause so literal `%` searches do not trigger full-table wildcard scans.
- [x] **[Implemented in `StartupService.cs` & `BornoKeySetup.iss`]** Quote executable paths (`"\"...\\BornoKey.exe\" --startup"`) when registering `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` to prevent unquoted path hijacking.
- [x] **[Implemented in `TsfBridgeService.cs`]** Reject unauthenticated or oversized IPC messages (`> 4096` bytes) on `TsfBridgeService`.

### 1.6 XSS / CSRF (Desktop UI & Local IPC Equivalents)
- [x] **[Implemented across `src/BengaliKeyboard.UI/`]** Use pure native WPF text rendering (`TextBlock.Text` data binding)—never parse untrusted clipboard or dictionary strings via `XamlReader.Load` or `WebBrowser`/`WebView2`.
- [x] **[Implemented in `Win32SendInputInjector.cs` & `PhysicalKeyboardService.cs`]** Tag synthetic keystrokes with `BornoKeyInjectedExtraInfo` (`0x424B4559`) and respect UIPI boundaries.

### 1.7 API Security (Local IPC & COM Interfaces)
- [x] **[Implemented in `TsfBridgeService.cs` & `SingleInstanceManager.cs`]** Enforce fixed maximum message size (`<= 4096` bytes) and `CurrentUserOnly` pipe ACLs.
- [x] **[Implemented in `native/BengaliKeyboard.TSF/`]** Validate all COM interface pointers and buffer lengths in `TextService.cpp`, `CompositionManager.cpp`, and `Register.cpp` using safe C++ bounds checks and `HRESULT` error propagation.

### 1.8 Rate Limiting / Abuse & Resource Exhaustion
- [x] **[Implemented in `WindowPositionService.cs`]** Debounce rapid window move/resize configuration saves by `300ms` and avoid background polling timers.
- [x] **[Implemented in `ClipboardMonitor.cs` & `KeyboardKeyControl.xaml.cs`]** Debounce rapid `WM_CLIPBOARDUPDATE` floods (`150ms` minimum interval) and stop key repeat timers immediately when a key loses mouse capture or window visibility.
- [x] **[Implemented in `BengaliInputEngine.cs` & `EmojiViewModel.cs`]** Cancel in-flight asynchronous prediction and search queries (`CancellationTokenSource`) when a newer keystroke arrives.

### 1.9 File Uploads & Local File Deserialization
- [x] **[Implemented in `JsonSettingsService.cs`]** Catch `JsonException`/`IOException` on `settings.json`, back up corrupted files to `settings.corrupt.bak`, and reset to safe defaults.
- [x] **[Implemented in `LayoutLoader.cs`]** Prevent path traversal when resolving layout file paths by verifying that canonical paths stay strictly within the configured `Definitions` root directory.

### 1.10 Database Security (`keyboard.db` & `dictionary.db`)
- [x] **[Implemented in `SqliteConnectionFactory.cs`]** Configure `PRAGMA journal_mode=WAL;`, `DefaultTimeout = 5`, and bounded retry on transient `SQLITE_BUSY` errors.
- [x] **[Implemented in `SqliteConnectionFactory.cs`, `ClipboardRepository.cs`, `EmojiUsageService.cs`, `DatabaseInitializer.cs`]** Enable `PRAGMA secure_delete = ON;` on `keyboard.db` and execute `PRAGMA wal_checkpoint(TRUNCATE);` when clearing history.
- [x] **[Implemented in `SqliteConnectionFactory.cs` & `DictionaryService.cs`]** Open `dictionary.db` in read-only mode (`SqliteOpenMode.ReadOnly`) during runtime completions/predictions.

### 1.11 Secrets Management
- [x] **[Verified across Repository]** Maintain zero hardcoded secrets, API keys, or static encryption passwords in source code (`security/secrets.md`).
- [x] **[Implemented in `.gitignore`]** Block `*.pfx`, `*.p12`, `*.snk`, `*.key`, `*.db`, `*.db-wal`, `*.db-shm`, and `*.log` from entering Git history.

### 1.12 Encryption
- [x] **[Implemented in `LocalDataProtector.cs` & `ClipboardRepository.cs`]** Encrypt `ClipboardHistory.Content` at rest in `keyboard.db` using Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`).
- [x] **[Implemented in `LocalDataProtector.cs`, `DatabaseInitializer.cs`, `ClipboardRepository.cs`]** Compute `ClipboardHistory.ContentHash` using a DPAPI-protected per-user 256-bit `HMACSHA256` key, with full key rotation on `Clear All Application Data`.

### 1.13 CORS & Network Isolation
- [x] **[Verified across Solution]** Maintain 100% network isolation: zero HTTP clients, zero telemetry endpoints, and zero listening network sockets.

### 1.14 Dependency Security
- [x] **[Verified across `src/*/*.csproj`]** Restrict NuGet dependencies strictly to the approved minimal list (`CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Data.Sqlite`, `Serilog`, `Serilog.Sinks.File`, `xUnit`) with 0 vulnerable packages.

### 1.15 Logging & Monitoring
- [x] **[Implemented in `Bootstrapper.cs`]** Configure rolling file logs (`%LOCALAPPDATA%\BengaliKeyboard\Logs\`) with 7-day retention and 5 MB size cap.
- [x] **[Implemented in `PrivacyDestructuringPolicy.cs`, `ClipboardEntry.cs`, `KeyDefinition.cs`, `SuggestionCandidate.cs`]** Redact user text in Serilog `IDestructuringPolicy` and model `ToString()` overrides.

### 1.16 Admin & Configuration Security
- [x] **[Implemented in `SettingsWindow.xaml` & `SettingsViewModel.cs`]** Provide user-accessible controls to disable clipboard monitoring, clear emoji/clipboard/dictionary history, clear all application data, and reset settings.
- [x] **[Implemented in `SettingsViewModel.cs`]** Require explicit confirmation (`RequestResetConfirmationCommand` → `ResetSettingsToDefaultsCommand`) before resetting configuration to factory defaults.

### 1.17 Deployment & Infrastructure (`installer/BornoKeySetup.iss`)
- [x] **[Implemented in `native/BengaliKeyboard.TSF/BengaliKeyboard.TSF.vcxproj`]** Configure C++ compiler security flags (`/GS` buffer security check, `/DYNAMICBASE` ASLR, `/NXCOMPAT` DEP, `/CETCOMPAT` Shadow Stack).
- [x] **[Implemented in `installer/BornoKeySetup.iss`]** Configure Inno Setup to install binaries to `{autopf}\BornoKey`, quote `HKCU\...\Run` startup paths, and support Authenticode signing.

### 1.18 Data Privacy
- [x] **[Implemented in `ClipboardMonitor.cs`]** Honor all 4 password-manager exclusion formats (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`, `ClipboardViewerIgnore`).
- [x] **[Implemented in `PhysicalKeyboardService.cs`]** Dynamically unhook `WH_KEYBOARD_LL` (`UnhookWindowsHookEx`) whenever BornoKey is in `English` mode (`LanguageMode.English`) or during session lock.

### 1.19 Error Handling & Resilience
- [x] **[Implemented across `Storage`, `Input`, `Clipboard`, `App`]** Implement the 10-scenario resilience matrix (atomic `settings.json.tmp` → `File.Move`, `CLIPBRD_E_CANT_OPEN` bounded retry, embedded resource fallback, global unhandled exception handlers).
- [x] **[Implemented in `PhysicalKeyboardService.cs` & `ClipboardMonitor.cs`]** Ensure `Dispose()` always unhooks `WH_KEYBOARD_LL` and removes the clipboard listener cleanly.

### 1.20 Backup & Recovery
- [x] **[Implemented in `JsonSettingsService.cs`]** Use atomic temp-file swap (`settings.json.tmp` → `settings.json`) and automatic `.corrupt.bak` recovery for user settings.
- [x] **[Implemented in `DatabaseInitializer.cs`]** Handle SQLite database corruption (`SQLITE_CORRUPT` / Error 11 & Error 26) by backing up the damaged `.db` file and re-initializing a clean schema.
