# Project-Specific Security Checklist & Findings Ledger — BornoKey

## 0. Current Implementation Reality Check

> **Verification Statement:**  
> As of this assessment, **BornoKey** is in the **Pre-Coding Specification Stage** (`docs/Memory.md:9-15`).  
> - **Implemented Runtime Controls:** **0** (No `.cs`, `.cpp`, `.csproj`, or `.sln` files exist on disk yet).  
> - **Specified Architectural Controls:** Documented across `Ridmik-Style Bengali Virtual Keyboard for Windows.md` and `docs/*.md`.  
> - Every checklist item below reflects the **actual current state** (`[ ] Not yet implemented in code`) and distinguishes between controls that are **already specified in `docs/`** versus **controls missing from the specification**.

---

## 1. 20-Domain Project-Specific Security Checklist

### 1.1 Authentication
*(Desktop context: BornoKey has no user login screen or remote authentication; identity is bound to the interactive Windows user session and local IPC peer identity).*
- [ ] **[Missing in Spec & Code]** Verify local Named Pipe / IPC peer identity in `Program.cs` and `TsfBridgeService.cs` using `PipeOptions.CurrentUserOnly` and current Windows user SID (`WindowsIdentity.GetCurrent().User`).
- [ ] **[Missing in Spec & Code]** Verify Authenticode digital signatures when `BengaliKeyboard.App.exe` loads or communicates with `BengaliKeyboard.TSF.dll`.

### 1.2 Authorization / RBAC
*(Desktop context: OS privilege levels, Mandatory Integrity Control (MIC), UIPI, and NTFS/Registry ACLs).*
- [ ] **[Specified in `docs/Rules.md:45` — Not Yet Coded]** Configure application manifest with `<requestedExecutionLevel level="asInvoker" uiAccess="false" />` so BornoKey runs under standard user privileges.
- [ ] **[Specified in `docs/Rules.md:61` — Not Yet Coded]** Respect Windows UIPI boundaries when an elevated Administrator window is focused; surface a graceful status indicator rather than crashing when `SendInput` is blocked.
- [ ] **[Missing in Spec & Code]** Restrict NTFS ACLs on `%LOCALAPPDATA%\BengaliKeyboard\` exclusively to `SYSTEM`, `Administrators`, and the owning `CurrentUser` SID (`InheritanceFlags` disabled for other local users).
- [ ] **[Missing in Spec & Code]** Ensure `BengaliKeyboard.TSF.dll` is installed in an Administrator-protected directory (`%ProgramFiles%\BornoKey\`) before registering COM `InprocServer32`.

### 1.3 Session / Token Security
*(Desktop context: Windows login session isolation, Terminal Services session boundaries, and Secure Desktop transitions).*
- [ ] **[Missing in Spec & Code — Currently Mis-Specified as `Global\`]** Scope single-instance Mutex to the current user session (`Local\BornoKey_SingleInstance_<UserSID>`) instead of `Global\BornoKey_SingleInstance_Mutex` (`docs/Rules.md:27`).
- [ ] **[Specified in `Ridmik-Style Bengali Virtual Keyboard for Windows.md:643`, `1263` — Not Yet Coded]** Detect Windows Secure Desktop / session lock transitions (`SystemEvents.SessionSwitch` / `OpenInputDesktop`) and pause input hooks and clipboard capture during lock screen or UAC prompts.

### 1.4 Input Validation
- [ ] **[Specified in `docs/Architecture.md:346-350` — Not Yet Coded]** Clamp restored window coordinates (`Left`, `Top`, `Width`, `Height`) to valid monitor `WorkingArea` bounds and mode min/max dimensions (`WindowPositionService.cs`).
- [ ] **[Missing in Spec & Code]** Enforce strict value clamping on `settings.json` deserialization (`Opacity` in `[0.60, 1.00]`, `HistoryLimit` in `[10, 500]`, `SuggestionCount` in `[3, 5]`).
- [ ] **[Missing in Spec & Code]** Validate `Definitions/**/*.json` keyboard layouts in `LayoutLoader.cs` (`KeyDefinition.UnicodeOutput` length `<= 8` characters, positive finite `WidthRatio` in `[0.5, 6.0]`, `JsonSerializerOptions.MaxDepth = 16`).
- [ ] **[Missing in Spec & Code]** Enforce a maximum payload size limit (`<= 64 KB` / `65,536` chars) on incoming `WM_CLIPBOARDUPDATE` text in `ClipboardMonitor.cs` before reading into managed memory.

### 1.5 Injection (SQL Injection, Command Injection, Keystroke Injection)
- [ ] **[Specified in `docs/Rules.md:15` — Not Yet Coded]** Use parameterized `SqliteCommand` queries (`@param`) exclusively across `ClipboardRepository`, `EmojiRepository`, `DictionaryService`, and `UserDictionaryRepository`.
- [ ] **[Missing in Spec & Code]** Escape SQLite `LIKE` wildcard characters (`%`, `_`, `\`) using an `ESCAPE '\'` clause in `ClipboardHistoryService.Search` and `EmojiSearchService.Search` so literal `%` searches do not trigger full-table wildcard scans.
- [ ] **[Missing in Spec & Code]** Quote executable paths (`"\"...\\BengaliKeyboard.App.exe\""`) in `StartupService.cs` when registering `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` to prevent unquoted path execution hijacking.
- [ ] **[Missing in Spec & Code]** Reject unauthenticated IPC messages on `TsfBridgeService` to prevent local processes from injecting unauthorized synthetic keystrokes via `Win32SendInputInjector`.

### 1.6 XSS / CSRF (Desktop UI & Local IPC Equivalents)
*(Desktop context: No HTML/DOM browser rendering exists (`docs/Rules.md:31`). Desktop equivalents are XAML injection and cross-process message spoofing).*
- [ ] **[Specified in `docs/Rules.md:31` — Not Yet Coded]** Use pure native WPF text rendering (`TextBlock.Text` data binding)—never parse untrusted clipboard or dictionary strings via `XamlReader.Load` or `WebBrowser`/`WebView2`.
- [ ] **[Missing in Spec & Code]** Filter incoming Win32 messages in `KeyboardWindow.WndProc` (`UIPI` / `ChangeWindowMessageFilterEx`) so lower-integrity processes cannot spoof custom window messages.

### 1.7 API Security (Local IPC & COM Interfaces)
- [ ] **[Missing in Spec & Code]** Define a strict, fixed-length binary/JSON message schema for `TsfBridgeService.cs` and the single-instance Named Pipe with maximum message size `<= 4096` bytes.
- [ ] **[Missing in Spec & Code]** Validate all COM interface pointers and buffer lengths in `native/BengaliKeyboard.TSF/` (`TextService.cpp`, `CompositionManager.cpp`) using safe C++ bounds checks and `HRESULT` error propagation.

### 1.8 Rate Limiting / Abuse & Resource Exhaustion
- [ ] **[Specified in `docs/Rules.md:42`, `54` — Not Yet Coded]** Debounce rapid window move/resize configuration saves by `300ms` (`JsonSettingsService.cs`) and avoid background polling timers.
- [ ] **[Missing in Spec & Code]** Debounce or rate-limit rapid `WM_CLIPBOARDUPDATE` floods (e.g., another app copying in a tight loop) in `ClipboardMonitor.cs` and cap on-screen key repeat timers (`OnScreenInputService.cs`) when the window loses visibility.
- [ ] **[Missing in Spec & Code]** Cancel in-flight asynchronous prediction queries (`CancellationTokenSource` in `BengaliInputEngine.cs`) when a newer keystroke arrives so fast typing never queues overlapping SQLite queries.

### 1.9 File Uploads & Local File Deserialization
*(Desktop context: Local JSON & SQLite file loading).*
- [ ] **[Specified in `docs/Rules.md:56` — Not Yet Coded]** Catch `JsonException`/`IOException` on `settings.json`, back up corrupted files to `settings.corrupt.bak`, and reset to safe defaults.
- [ ] **[Missing in Spec & Code]** Prevent path traversal when resolving layout file names in `LayoutManager.cs` by verifying that resolved paths stay strictly within `Path.Combine(AppContext.BaseDirectory, "Definitions")`.

### 1.10 Database Security (`keyboard.db` & `dictionary.db`)
- [ ] **[Specified in `docs/Rules.md:15`, `57` — Not Yet Coded]** Configure `PRAGMA journal_mode=WAL;`, `DefaultTimeout = 5`, and bounded retry on transient `SQLITE_BUSY` errors.
- [ ] **[Missing in Spec & Code]** Enable `PRAGMA secure_delete = ON;` on `keyboard.db` and execute `PRAGMA wal_checkpoint(TRUNCATE);` when clearing clipboard or emoji history so deleted sensitive text does not linger in `.db` free pages or `.db-wal` files.
- [ ] **[Missing in Spec & Code]** Open `dictionary.db` in read-only mode (`SqliteOpenMode.ReadOnly`) during normal runtime completions/predictions to prevent accidental or unauthorized mutation of the language model.

### 1.11 Secrets Management
- [ ] **[Verified in Workspace]** Maintain zero hardcoded secrets, API keys, or static encryption passwords in source code (`security/secrets.md`).
- [ ] **[Missing in Spec & Code]** Create root `.gitignore` before `git init` to block `*.pfx`, `*.p12`, `*.snk`, `*.key`, `*.db`, `*.db-wal`, `*.db-shm`, and `*.log` from ever entering Git history.

### 1.12 Encryption
- [ ] **[Missing in Spec & Code]** Encrypt `ClipboardHistory.Content` at rest in `keyboard.db` using Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`).
- [ ] **[Missing in Spec & Code]** Compute `ClipboardHistory.ContentHash` using a DPAPI-protected per-user `HMACSHA256` key instead of an unsalted SHA-256 hash.

### 1.13 CORS & Network Isolation
*(Desktop context: Native WPF desktop utility; browser CORS is not applicable).*
- [ ] **[Specified in `docs/PRD.md:148`, `docs/Rules.md:36` — Not Yet Coded]** Maintain 100% network isolation: zero HTTP clients, zero telemetry endpoints, and zero listening sockets.

### 1.14 Dependency Security
- [ ] **[Specified in `docs/Architecture.md:266-274` — Not Yet Coded]** Restrict NuGet dependencies strictly to the approved minimal list (`CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Data.Sqlite`, `Serilog`, `Serilog.Sinks.File`, `H.NotifyIcon.Wpf`, `xUnit`).
- [ ] **[Missing in Spec & Code]** Enable NuGet Central Package Management (`Directory.Packages.props`), lock package versions (`packages.lock.json`), and run `dotnet list package --vulnerable` during build verification.

### 1.15 Logging & Monitoring
- [ ] **[Specified in `docs/Rules.md:19`, `44` — Not Yet Coded]** Configure rolling file logs (`%LOCALAPPDATA%\BengaliKeyboard\Logs\`) and never log keystrokes, words, or clipboard contents.
- [ ] **[Missing in Spec & Code]** Implement a Serilog `IDestructuringPolicy` and safe `ToString()` overrides on `ClipboardEntry`, `KeyDefinition`, and `SuggestionCandidate` so structured exception logs cannot accidentally serialize user text.

### 1.16 Admin & Configuration Security
- [ ] **[Specified in `docs/PRD.md:120-129` — Not Yet Coded]** Provide user-accessible controls in `SettingsWindow.xaml` to disable clipboard monitoring, clear emoji/clipboard history, clear all application data, and reset settings.
- [ ] **[Missing in Spec & Code]** Require explicit user confirmation dialog before executing destructive actions (`Clear All Application Data`, `Reset Settings`) and ensure background services (`ClipboardMonitor`) immediately stop capturing when toggled off.

### 1.17 Deployment & Infrastructure (`installer/BornoKeySetup.iss`)
- [ ] **[Missing in Spec & Code]** Configure C++ compiler security flags (`/GS` buffer security check, `/DYNAMICBASE` ASLR, `/NXCOMPAT` DEP, `/CETCOMPAT` Shadow Stack) in `native/BengaliKeyboard.TSF/BengaliKeyboard.TSF.vcxproj`.
- [ ] **[Missing in Spec & Code]** Configure Inno Setup (`BornoKeySetup.iss`) to install binaries to `%autopf%\BornoKey`, verify clean uninstallation of `BengaliKeyboard.TSF.dll` COM registry keys, and support Authenticode signing.

### 1.18 Data Privacy
- [ ] **[Specified in `docs/PRD.md:113` — Incomplete in Spec & Not Yet Coded]** Check `CF_EXCLUDE_CLIPBOARD_CONTENT_FROM_MONITOR_PROCESSING` before capturing clipboard text.
- [ ] **[Missing in Spec & Code]** Also check `CanIncludeInClipboardHistory` (`DWORD == 0`), `CanUploadToCloudClipboard` (`DWORD == 0`), and `ClipboardViewerIgnore` formats so passwords copied from 1Password, Bitwarden, KeePassXC, and Windows CredUI are never captured.
- [ ] **[Missing in Spec & Code]** Dynamically unhook `WH_KEYBOARD_LL` (`UnhookWindowsHookEx`) in `PhysicalKeyboardService.cs` whenever BornoKey is in `English` mode (`LanguageMode.English`) so physical keystrokes are not intercepted unnecessarily.

### 1.19 Error Handling & Resilience
- [ ] **[Specified in `docs/Rules.md:51-65` — Not Yet Coded]** Implement the 10-scenario resilience matrix (atomic `settings.json.tmp` → `File.Move`, `CLIPBRD_E_CANT_OPEN` bounded retry, missing seed file recovery, global unhandled exception handlers).
- [ ] **[Missing in Spec & Code]** Ensure a `try/finally` block in `PhysicalKeyboardService.Dispose()` and `AppDomain.ProcessExit` always calls `UnhookWindowsHookEx` and `RemoveClipboardFormatListener` so crashes never leave orphaned OS hooks delaying Windows input.

### 1.20 Backup & Recovery
- [ ] **[Specified in `docs/Rules.md:54`, `56` — Not Yet Coded]** Use atomic temp-file swap (`settings.json.tmp` → `settings.json`) and automatic `.corrupt.bak` recovery for user settings.
- [ ] **[Missing in Spec & Code]** Handle SQLite database corruption (`SQLITE_CORRUPT` / Error 11) in `DatabaseInitializer.cs` by backing up the damaged `keyboard.db` file and re-initializing a clean schema so the keyboard remains usable.

---

## 2. Consolidated Findings Ledger (Categorized)

### 2.1 Confirmed Vulnerabilities
*None in executable code (verified that the repository currently contains only Markdown specification documents and zero source code or hardcoded credentials).*

---

### 2.2 Security Weaknesses (In Current Architectural Specification)

#### Finding CHK-01: Plaintext `ClipboardHistory.Content` & Unsalted `ContentHash` in SQLite Schema
- **Severity:** **HIGH**
- **Evidence:** `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1162-1167` and `docs/Architecture.md:302-308` specify `CREATE TABLE IF NOT EXISTS ClipboardHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, Content TEXT NOT NULL, ContentHash TEXT NOT NULL UNIQUE, ...)` in `%LOCALAPPDATA%\BengaliKeyboard\Database\keyboard.db`.
- **Risk:** Sensitive text copied by the user (tokens, personal messages, credentials) is stored in unencrypted plaintext on disk, and deterministic unsalted hashes allow offline guessing of short secrets.
- **Affected location:** `docs/Architecture.md:302-308`, `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1162-1167` (planned `src/BengaliKeyboard.Clipboard/ClipboardRepository.cs`).
- **Why it matters:** Directly exposes user clipboard history to any local process or unauthorized reader of `%LOCALAPPDATA%`, conflicting with `Ridmik-Style Bengali Virtual Keyboard for Windows.md:634` and `1254`.
- **Recommended fix:** Encrypt `ClipboardHistory.Content` using Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`), compute `ContentHash` via a per-user DPAPI-protected `HMACSHA256` key, and enable `PRAGMA secure_delete = ON;`.

#### Finding CHK-02: Cross-Session `Global\` Mutex Specification Enables Multi-User DoS
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Rules.md:27` specifies a global mutex named `Global\BornoKey_SingleInstance_Mutex` in `Program.cs`.
- **Risk:** In Windows, the `Global\` kernel object namespace spans all Terminal Services / Fast User Switching login sessions. If User A runs BornoKey (or a malicious process under User A squats on `Global\BornoKey_SingleInstance_Mutex`), User B logged into the same PC cannot start BornoKey.
- **Affected location:** `docs/Rules.md:27`, `docs/Architecture.md:139` (planned `src/BengaliKeyboard.App/Program.cs`).
- **Why it matters:** A per-user desktop input utility must isolate its single-instance lock per user session rather than machine-wide.
- **Recommended fix:** Replace `Global\BornoKey_SingleInstance_Mutex` with a session- and SID-scoped mutex name: `$"Local\\BornoKey_SingleInstance_{WindowsIdentity.GetCurrent().User?.Value}"`.

#### Finding CHK-03: Always-On `WH_KEYBOARD_LL` Hook Risk During English Mode
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Architecture.md:185` defines `PhysicalKeyboardService.cs` as *"Low-level WH_KEYBOARD_LL / TSF keystroke routing"*, while `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1259-1261` forbids acting as a general-purpose global keylogger.
- **Risk:** Keeping a Win32 `WH_KEYBOARD_LL` hook installed when the user switches to `English` mode (`LanguageMode.English`) routes every English keystroke and password entry through BornoKey's managed hook callback unnecessarily.
- **Affected location:** `docs/Architecture.md:185` (planned `src/BengaliKeyboard.Input/PhysicalKeyboardService.cs`).
- **Why it matters:** Minimizing hook lifetime protects user privacy, avoids Windows input queue lag if the managed process pauses for GC, and reduces antivirus heuristics flags.
- **Recommended fix:** Install `WH_KEYBOARD_LL` only when `LanguageMode == LanguageMode.Bangla` (and when TSF is not handling input directly), and call `UnhookWindowsHookEx` immediately upon switching to `LanguageMode.English` or when the Windows session locks.

---

### 2.3 Missing Controls (In Current Specification)

#### Finding CHK-04: Incomplete Password Manager Clipboard Exclusion Formats
- **Severity:** **HIGH**
- **Evidence:** `docs/PRD.md:113` only specifies checking `CF_EXCLUDE_CLIPBOARD_CONTENT_FROM_MONITOR_PROCESSING`.
- **Risk:** Password managers and Windows components that mark sensitive clipboard items using `CanIncludeInClipboardHistory = 0`, `CanUploadToCloudClipboard = 0`, or `ClipboardViewerIgnore` will still have their passwords captured into `keyboard.db`.
- **Affected location:** `docs/PRD.md:113` (planned `src/BengaliKeyboard.Clipboard/ClipboardMonitor.cs`).
- **Why it matters:** Essential to fulfill `Ridmik-Style Bengali Virtual Keyboard for Windows.md:643` and `1253` (*"No storage of passwords"*).
- **Recommended fix:** Check all four registered clipboard formats (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`, and `ClipboardViewerIgnore`) before reading `CF_UNICODETEXT`.

#### Finding CHK-05: Missing Local IPC Authentication & ACL Specification for `TsfBridgeService` and Single-Instance Pipe
- **Severity:** **MEDIUM–HIGH**
- **Evidence:** `docs/Architecture.md:187` specifies `TsfBridgeService.cs` (*"IPC / COM bridge to native TSF text service"*) and `docs/Rules.md:27` specifies a Named Pipe for single-instance signaling, without specifying `PipeSecurity` or `PipeOptions.CurrentUserOnly`.
- **Risk:** An unauthenticated local Named Pipe allows sandboxed or untrusted processes on the machine to send forged IPC commands to BornoKey (such as triggering synthetic text injection via `ITextInjector`).
- **Affected location:** `docs/Architecture.md:187`, `docs/Rules.md:27` (planned `TsfBridgeService.cs` and `Program.cs`).
- **Why it matters:** `TsfBridgeService` bridges directly into the system-wide text injection engine.
- **Recommended fix:** Create all `NamedPipeServerStream` instances with `PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous` and configure `PipeSecurity` to grant read/write access exclusively to `WindowsIdentity.GetCurrent().User` while denying `WellKnownSidType.AnonymousSid` and Low-Integrity AppContainers.

#### Finding CHK-06: Missing Maximum Size Bounds on Clipboard Capture & JSON Deserialization
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Architecture.md:201`, `304` (`ClipboardHistory.Content TEXT NOT NULL`) and `docs/Architecture.md:170`, `214` (`LayoutLoader.cs`, `JsonSettingsService.cs`) define no upper bounds on clipboard string length or JSON depth/value ranges.
- **Risk:** Copying a multi-hundred-megabyte text buffer causes Large Object Heap memory exhaustion and UI freezes; out-of-range values in `settings.json` (`Opacity: 0.0`) can render the keyboard invisible.
- **Affected location:** Planned `ClipboardMonitor.cs`, `LayoutLoader.cs`, and `JsonSettingsService.cs`.
- **Why it matters:** Directly affects the `< 100 MB` RAM and non-blocking UI requirements (`docs/PRD.md:144-146`).
- **Recommended fix:** Skip clipboard texts larger than `64 KB` (`65,536` characters), set `JsonSerializerOptions.MaxDepth = 16`, and clamp all numeric configuration properties to safe UI bounds.

#### Finding CHK-07: Missing `.gitignore` & Serilog Model Redaction Guardrails
- **Severity:** **MEDIUM**
- **Evidence:** No `.gitignore` exists in `BornoKey/`, and `docs/Architecture.md:155-162` defines `ClipboardEntry` without a redacted `ToString()` or Serilog `IDestructuringPolicy`.
- **Risk:** Accidental commit of test `.db`/`.log`/`.pfx` files once Git is initialized, or accidental logging of `ClipboardEntry.Content` during exception handling.
- **Affected location:** Repository root (`BornoKey/`) and planned `src/BengaliKeyboard.Core/Models/ClipboardEntry.cs`.
- **Why it matters:** Prevents developer test data leaks in Git and protects user privacy in `%LOCALAPPDATA%\BengaliKeyboard\Logs\`.
- **Recommended fix:** Add a `.gitignore` before Phase 1 coding starts and implement a Serilog `IDestructuringPolicy` + redacted `ClipboardEntry.ToString()`.

---

### 2.4 Potential Risks Requiring Verification (During Phase 3, 5 & 10 Implementation)

#### Finding CHK-08: Native TSF DLL (`BengaliKeyboard.TSF.dll`) Installation Path & COM Registration Permissions
- **Severity:** **HIGH**
- **Evidence:** `docs/Architecture.md:226-233`, `247` specify `native/BengaliKeyboard.TSF` and `installer/BornoKeySetup.iss`, while `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1205` notes the app should not require admin privileges for normal usage.
- **Risk:** If `Register.cpp` or `BornoKeySetup.iss` registers `BengaliKeyboard.TSF.dll` from a user-writable directory (`%LOCALAPPDATA%`), any local malware can overwrite the in-process COM DLL and execute arbitrary code inside every application that loads TSF.
- **Affected location:** Planned `native/BengaliKeyboard.TSF/Register.cpp` and `installer/BornoKeySetup.iss`.
- **Why it matters:** In-process COM text services are loaded into external processes across the desktop.
- **Recommended fix:** Verify during Phase 3 and Phase 10 that `BengaliKeyboard.TSF.dll` is compiled with `/GS /DYNAMICBASE /NXCOMPAT /CETCOMPAT`, signed, and registered only when installed inside `%ProgramFiles%\BornoKey\` with Administrator-only write permissions.

#### Finding CHK-09: Unquoted Executable Path in `HKCU\...\Run` Auto-Startup Registry Value
- **Severity:** **MEDIUM**
- **Evidence:** `docs/PRD.md:116` and `docs/Architecture.md:204` specify `StartupService.cs` registering BornoKey in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **Risk:** If the executable path contains spaces (e.g., `C:\Program Files\BornoKey\BengaliKeyboard.App.exe` or the current workspace path) and is written without surrounding double quotes, Windows attempts to execute `C:\Program.exe` on login.
- **Affected location:** Planned `src/BengaliKeyboard.Windows/StartupService.cs`.
- **Why it matters:** Standard Windows persistence hygiene.
- **Recommended fix:** Verify during Phase 5 that `StartupService.cs` wraps the executable path in literal double quotes (`"\"C:\\...\\BengaliKeyboard.App.exe\" --startup"`).

---

### 2.5 Recommendations Summary (Prioritized for Phase 1–10 Implementation)

1. **Before Phase 1 Coding Starts:** Create `.gitignore` excluding `*.db`, `*.db-wal`, `*.db-shm`, `*.log`, `*.pfx`, `*.snk`, `bin/`, `obj/`, and `.vs/`.
2. **In Phase 1 (`Program.cs`, `DatabaseInitializer.cs`, `JsonSettingsService.cs`, `ClipboardEntry.cs`):**
   - Use session-local mutex `Local\BornoKey_SingleInstance_<UserSID>` and `PipeOptions.CurrentUserOnly`.
   - Enable `PRAGMA journal_mode=WAL;` and `PRAGMA secure_delete=ON;` on `keyboard.db`, and open `dictionary.db` in `ReadOnly` mode.
   - Clamp all `settings.json` numeric values and add a Serilog `IDestructuringPolicy` that redacts `ClipboardEntry.Content`.
3. **In Phase 3 (`PhysicalKeyboardService.cs`, `TsfBridgeService.cs`, `BengaliKeyboard.TSF`):**
   - Dynamically unhook `WH_KEYBOARD_LL` when `LanguageMode.English` is active or when the desktop locks.
   - Enforce current-user SID ACLs on `TsfBridgeService` IPC and enable `/GS /DYNAMICBASE /NXCOMPAT /CETCOMPAT` on `BengaliKeyboard.TSF.vcxproj`.
4. **In Phase 5 (`StartupService.cs`):** Always quote the executable path written to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
5. **In Phase 7 (`ClipboardMonitor.cs`, `ClipboardRepository.cs`):**
   - Honor all 4 password-manager exclusion formats (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`, `ClipboardViewerIgnore`).
   - Enforce a `64 KB` max size limit per clip, encrypt `ClipboardHistory.Content` with Windows DPAPI (`ProtectedData.Protect`), and compute `ContentHash` with a DPAPI-protected per-user `HMACSHA256` key.
