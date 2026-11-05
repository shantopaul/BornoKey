# Attack Surface Analysis — BornoKey

## 0. Current Project State Verification

> **Repository Status:**  
> Inspection of `BornoKey/` confirms the project is currently in the **Pre-Coding Specification Stage** (`docs/Memory.md:9-15`). Only specification and architecture Markdown files exist (`Ridmik-Style Bengali Virtual Keyboard for Windows.md` and `docs/*.md`); no source code (`.cs`, `.cpp`, `.csproj`) has been created yet. All surfaces mapped below represent the exact architectural components, OS hooks, IPC channels, files, and databases defined in the project specifications.

---

## 1. Public Endpoints

- **HTTP / HTTPS / WebSocket / REST / GraphQL Endpoints:** **None (`0`)**.
- **Listening TCP / UDP Network Ports:** **None (`0`)**.
- **Evidence:** `Ridmik-Style Bengali Virtual Keyboard for Windows.md:464`, `1222`, `1249-1251` and `docs/PRD.md:148` mandate 100% offline operation with zero network listeners and zero external network calls.

---

## 2. Internal Endpoints (Local IPC, COM & OS Message Surfaces)

Although BornoKey has no network endpoints, it exposes **four local inter-process and OS message surfaces** on the Windows machine:

| Internal Surface | Planned Component | Mechanism | Reachability & Trust Context |
|---|---|---|---|
| **1. Single-Instance Kernel Mutex** | `src/BengaliKeyboard.App/Program.cs` (`docs/Rules.md:27`) | Named Mutex `Global\BornoKey_SingleInstance_Mutex` | **Multi-Session Local Reachability:** Because `Global\` is specified instead of `Local\`, processes in other Windows login sessions on the same PC can open or lock this mutex. |
| **2. Single-Instance Wakeup IPC** | `src/BengaliKeyboard.App/Program.cs` (`docs/Rules.md:27`) | Named Pipe or Win32 Registered Window Message | **Same-Session / Local Reachability:** Used by a second launched instance to signal the primary instance to restore its window. |
| **3. TSF Managed-to-Native Bridge** | `src/BengaliKeyboard.Input/TSF/TsfBridgeService.cs` (`docs/Architecture.md:187`) | Local IPC / COM bridge between `BornoKey.exe` and `BengaliKeyboard.TSF.dll` | **Cross-Process Local Reachability:** Every target Windows application that loads `BengaliKeyboard.TSF.dll` communicates across process boundaries with BornoKey. |
| **4. Win32 Window Procedure (`WndProc`)** | `src/BengaliKeyboard.UI/Windows/KeyboardWindow.xaml.cs` (`docs/Architecture.md:146`, `287`) | HwndSource hook receiving `WM_MOUSEACTIVATE`, `WM_CLIPBOARDUPDATE`, `WM_HOTKEY`, `WM_DPICHANGED` | **Same-Session Desktop Reachability:** Any medium-integrity process on the interactive desktop can send Win32 messages via `SendMessage`/`PostMessage` unless restricted. |

---

## 3. Authentication & Authorization Surfaces

- **Application-Level Authentication:** None. As a local desktop input utility, BornoKey relies on the interactive Windows user session.
- **Operating System Authorization & Privilege Boundaries:**
  1. **Standard User Execution (`asInvoker`):** Specified in `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1205`, `1257` and `docs/Rules.md:45`. Normal operation runs with Medium Integrity under the logged-in user's token.
  2. **Windows UIPI (User Interface Privilege Isolation):** Blocks standard-user `SendInput` (`Win32SendInputInjector.cs`) and window messages from injecting input into High-Integrity (Elevated Administrator) applications (`Ridmik-Style Bengali Virtual Keyboard for Windows.md:1319`, `docs/Rules.md:61`).
  3. **Windows Secure Desktop Boundary:** Isolates UAC elevation prompts, Ctrl+Alt+Delete, and the Windows lock screen (`Winlogon`) from standard user-space hooks and clipboard listeners (`Ridmik-Style Bengali Virtual Keyboard for Windows.md:643`, `1263`).
  4. **Installer Elevation Boundary:** `installer/BornoKeySetup.iss` (`docs/Architecture.md:247`) requires Administrator privileges if registering `BengaliKeyboard.TSF.dll` machine-wide in `HKLM` and `%ProgramFiles%`.

---

## 4. User Inputs & Untrusted Data Entry Points

| Input Surface | Component Location | Data Format & Potential Abuse Vector |
|---|---|---|
| **1. System-Wide Clipboard Content (`WM_CLIPBOARDUPDATE`)** | `src/BengaliKeyboard.Clipboard/ClipboardMonitor.cs` (`docs/Architecture.md:201`) | **Untrusted External Text:** Any website (via browser copy button) or application can place multi-megabyte strings, malformed Unicode, null bytes, or sensitive credentials onto the Windows clipboard. |
| **2. Physical Keyboard Events** | `src/BengaliKeyboard.Input/PhysicalKeyboardService.cs` (`docs/Architecture.md:185`) | **OS Key Events:** Rapid key repeat floods, complex modifier combinations, or non-character virtual keys processed by `BengaliInputEngine` and `UnicodeProcessor`. |
| **3. On-Screen Virtual Key Clicks** | `src/BengaliKeyboard.Input/OnScreenInputService.cs` (`docs/Architecture.md:184`) | **UI Mouse/Touch Events:** Rapid clicks, press-and-hold repeat timers on `Backspace`/`Space`, and mode switching triggers. |
| **4. Emoji & Clipboard Search Boxes** | `EmojiWindow.xaml` & `ClipboardWindow.xaml` (`docs/Architecture.md:147-148`, `288`) | **Interactive Text Input:** User-typed search queries passed to `EmojiSearchService.cs` and `ClipboardHistoryService.cs` SQLite `LIKE` / prefix queries. Also temporarily toggles off `WS_EX_NOACTIVATE` (`docs/Architecture.md:288`). |
| **5. Settings Panel Controls** | `SettingsWindow.xaml` / `SettingsViewModel.cs` (`docs/Architecture.md:149`, `167`) | **Configuration Inputs:** Custom window dimensions, opacity values, history capacity limits, retention periods, and global shortcut combinations. |

---

## 5. File Uploads & Local File Ingestion

- **Remote / Web File Uploads:** **None (`0`)**.
- **Local File Deserialization Surfaces (`System.Text.Json`):**
  1. **`%LOCALAPPDATA%\BengaliKeyboard\Config\settings.json`** (`JsonSettingsService.cs`, `docs/Architecture.md:214`, `252`): Loaded at every startup. If modified by another local process or corrupted, out-of-range values (`Width = -500`, `Opacity = 0.0`) or deeply nested JSON objects could break window rendering or hide the UI.
  2. **Keyboard Layout Definitions (`Definitions/**/*.json`)** (`LayoutLoader.cs`, `docs/Architecture.md:170-179`): Defines every key's `UnicodeOutput`, `AlternateOutput`, `WidthRatio`, and `KeyType` (`Ridmik-Style Bengali Virtual Keyboard for Windows.md:185-195`). If located in a writable directory without schema validation, tampering could cause a key click to emit unexpected string sequences.
  3. **Dictionary & Emoji Seed Files (`bn_dictionary_seed.json`, `emoji_catalog.json`)** (`docs/Architecture.md:241-244`): Ingested to populate `dictionary.db` and emoji metadata.

---

## 6. Database Access

All database operations use `Microsoft.Data.Sqlite` (`docs/Architecture.md:269`) against two local SQLite files:

| Database File | Tables Defined (`docs/Architecture.md:294-341`) | Access Pattern & Risk Surface |
|---|---|---|
| **`%LOCALAPPDATA%\BengaliKeyboard\Database\keyboard.db`** | `EmojiUsage`, `ClipboardHistory`, `ApplicationSettings` (+ `UserDictionaryRepository`) | **Read/Write at Runtime:** Receives continuous inserts from `ClipboardMonitor` and `EmojiUsageService`, and executes search queries from `ClipboardHistoryService`. Vulnerable to SQL injection if string concatenation is ever used instead of `SqliteParameter`, or wildcard DoS in `LIKE` queries if `%` and `_` are not escaped. |
| **`%LOCALAPPDATA%\BengaliKeyboard\Database\dictionary.db`** | `BengaliWords`, `Bigrams`, `Trigrams` | **Read-Heavy at Runtime (≤ 50 ms target):** Queried asynchronously on every keystroke (`WordCompletionService`, `NextWordPredictionService`). |

---

## 7. Storage Surfaces (Filesystem & Windows Registry)

1. **Local AppData Directory (`%LOCALAPPDATA%\BengaliKeyboard\`):**
   - Contains `Config/settings.json`, `Database/keyboard.db`, `Database/dictionary.db`, `Logs/bornokey-YYYYMMDD.log`, and `Cache/` (`docs/Architecture.md:250-258`).
   - Inherits default NTFS permissions for the current user (`%USERNAME%:(F)`, `SYSTEM:(F)`, `Administrators:(F)`).
2. **Application Installation Directory (`%ProgramFiles%\BornoKey\` or User Install Path):**
   - Houses `BornoKey.exe`, managed `.dll` assemblies, `native/BengaliKeyboard.TSF.dll`, `assets/`, and `data/`.
3. **Windows Registry Keys:**
   - **Auto-Startup:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (or `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`) managed by `StartupService.cs` (`docs/PRD.md:116`, `docs/Architecture.md:204`).
   - **COM / TSF Registration:** `HKLM\SOFTWARE\Classes\CLSID\{...}\InprocServer32` and TSF profile registration via `ITfInputProcessorProfiles` in `native/BengaliKeyboard.TSF/Register.cpp` (`docs/Architecture.md:229`).

---

## 8. Webhooks & Third-Party Integrations

- **Webhooks:** **None (`0`)**.
- **Third-Party Cloud / Analytics / AI Integrations:** **None (`0`)**. Explicitly prohibited by `Ridmik-Style Bengali Virtual Keyboard for Windows.md:464`, `1249-1251` and `docs/Rules.md:36`.

---

## 9. Admin & Privileged Surfaces

1. **`SettingsWindow.xaml` & System Tray Menu (`SystemTrayService.cs`):**
   - Local control plane allowing the user to toggle `Start with Windows`, enable/disable clipboard monitoring, configure global hotkeys, and wipe local databases (`Clear Emoji History`, `Clear Clipboard History`, `Clear All Application Data`, `docs/PRD.md:120-129`).
2. **Installer & TSF Registration (`installer/BornoKeySetup.iss` & `Register.cpp`):**
   - The only surface that executes with Administrator privileges during installation/uninstallation to register the native COM TSF DLL (`BengaliKeyboard.TSF.dll`) and create `Program Files` directories.

---

## 10. Network & Deployment Exposure

- **Runtime Network Exposure:** Zero. Neither `BornoKey.exe` nor `BengaliKeyboard.TSF.dll` links against network client libraries (`HttpClient` is unused).
- **Deployment / Update Surface:**
  - `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1256` lists *"Safe application update mechanism"* under Security & Privacy requirements, and line `1503`/`1507` specifies an Inno Setup / MSIX installer distributed via GitHub Release.
  - Currently, no automated updater service is included in the module tree (`docs/Architecture.md:130-248`), meaning updates are performed via signed installer packages.

---

## 11. Dependencies & Externally Reachable Surfaces

| Dependency / External Surface | Specified Package / API (`docs/Architecture.md:266-274`) | Attack Surface Consideration |
|---|---|---|
| **`Microsoft.Data.Sqlite` (`10.0.0`)** | Managed wrapper + native `e_sqlite3.dll` | Native SQLite engine parsing `.db` files on disk. |
| **`CommunityToolkit.Mvvm` (`8.4.0+`)** | Managed NuGet package | Source-generated MVVM bindings and in-process `WeakReferenceMessenger`. |
| **`Serilog` & `Serilog.Sinks.File` (`4.x`)** | Managed NuGet packages | Writes diagnostic logs to `%LOCALAPPDATA%\BengaliKeyboard\Logs\`. |
| **`H.NotifyIcon.Wpf` / WinForms `NotifyIcon`** | System Tray integration | Interacts with Windows Explorer (`Shell_NotifyIcon`). |
| **Win32 P/Invoke (`user32.dll`, `shcore.dll`, `dwmapi.dll`)** | `NativeMethods.cs` (`docs/Architecture.md:202`) | Unmanaged pointers, struct marshaling, window style manipulation (`SetWindowLongPtr`), `SendInput`, `SetWindowsHookEx`, `AddClipboardFormatListener`, `RegisterHotKey`. |
| **Target External Applications** | Notepad, Word, Chrome, Edge, Firefox, VS Code (`docs/PRD.md:152-156`) | Receives synthetic keystrokes (`SendInput`) and loads `BengaliKeyboard.TSF.dll` into its process memory. |

---

## 12. Detailed Attack Surface Findings

### Confirmed Vulnerabilities
*None in executable code (project is currently in the pre-coding specification stage).*

---

### Security Weaknesses

#### Finding AS-01: Unrestricted Payload Size & Format Ingestion on `ClipboardMonitor` (`Security weakness` / `Missing control`)
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Architecture.md:201` specifies `ClipboardMonitor.cs` (`AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`) writing into `ClipboardHistory` (`Content TEXT NOT NULL`, `docs/Architecture.md:304`), without specifying a maximum string length or byte size cap prior to reading `Clipboard.GetText()` and computing `ContentHash`.
- **Risk:** If a user copies a multi-hundred-megabyte text stream (e.g., a large log file or database dump) in another application, `ClipboardMonitor` will allocate the entire string on the `.NET` Large Object Heap (LOH), hash it, and write it into `keyboard.db`, causing memory spikes well above the `50–100 MB` RAM target (`docs/PRD.md:144`) and freezing the UI.
- **Affected location:** `docs/Architecture.md:201-203`, `302-308` (planned `src/BengaliKeyboard.Clipboard/ClipboardMonitor.cs` and `ClipboardHistoryService.cs`).
- **Why it matters:** `WM_CLIPBOARDUPDATE` fires automatically for every system-wide copy operation, making `ClipboardMonitor` an involuntary ingestion point for arbitrarily large external data.
- **Recommended fix:** Inspect the native global memory block size (`GlobalSize` on `GetClipboardData(CF_UNICODETEXT)`) or enforce a strict maximum character length (e.g., `65,536` characters / `128 KB`) before reading, hashing, or persisting clipboard entries.

#### Finding AS-02: Temporary Removal of `WS_EX_NOACTIVATE` for Search Boxes Creates Focus-Race Window (`Security weakness`)
- **Severity:** **LOW–MEDIUM**
- **Evidence:** `docs/Architecture.md:288` specifies: *"For interactive text inputs inside BornoKey itself (such as the Emoji Search box or Clipboard Search box), clicking those specific search controls temporarily allows activation so the user can type search queries, then restores `WS_EX_NOACTIVATE` when returning to the keyboard grid."*
- **Risk:** When `WS_EX_NOACTIVATE` is removed so BornoKey itself takes foreground focus, the previous target application loses foreground focus. If the user subsequently clicks an emoji, clipboard item, or virtual key to insert text into the external application, `Win32SendInputInjector` will inject the text into BornoKey's own search box (or nowhere) unless BornoKey explicitly tracks and restores focus (`SetForegroundWindow`) to the last valid external `HWND` before calling `SendInput`.
- **Affected location:** `docs/Architecture.md:280-289` (planned `KeyboardWindow.xaml.cs`, `EmojiWindow.xaml.cs`, `ClipboardWindow.xaml.cs`, and `Win32SendInputInjector.cs`).
- **Why it matters:** Reliable system-wide insertion without losing the target application's caret is a core product requirement (`Ridmik-Style Bengali Virtual Keyboard for Windows.md:224-227`).
- **Recommended fix:** Track the last active external window handle (`GetForegroundWindow()`) prior to activating BornoKey's search box, re-apply `WS_EX_NOACTIVATE`, restore foreground focus to the saved external `HWND` when an emoji or clip is selected for insertion, and verify focus restoration before dispatching `SendInput`.

---

### Missing Controls

#### Finding AS-03: Missing Validation & Sanitization Bounds on `LayoutLoader` and `JsonSettingsService` (`Missing control`)
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Architecture.md:170-179` and `214` specify loading keyboard layouts (`Definitions/**/*.json`) and user configuration (`%LOCALAPPDATA%\BengaliKeyboard\Config\settings.json`) via `System.Text.Json`, while `docs/Rules.md:56` only addresses catching `JsonException`/`IOException` for corrupted files.
- **Risk:** Valid JSON containing malicious or extreme logical values—such as `Opacity: 0.0` (making the keyboard completely invisible while still intercepting physical keys), negative/huge `WidthRatio` values, `JsonSerializerOptions` without `MaxDepth`, or a modified `KeyDefinition.UnicodeOutput` containing long arbitrary text strings—will deserialize without throwing a `JsonException`.
- **Affected location:** `docs/Architecture.md:170`, `214` (planned `src/BengaliKeyboard.Layouts/LayoutLoader.cs` and `src/BengaliKeyboard.Storage/Configuration/JsonSettingsService.cs`).
- **Why it matters:** Local configuration and layout JSON files cross Trust Boundary `TB-02` on startup.
- **Recommended fix:** Enforce `JsonSerializerOptions { MaxDepth = 16, AllowTrailingCommas = false }`, clamp `Opacity` strictly to `[0.60, 1.00]` (`docs/Design.md:72`), clamp `HistoryLimit` to `[10, 500]`, and validate that every `KeyDefinition.UnicodeOutput` in layout files has a maximum length of `<= 8` Unicode code points matching valid Bengali/English characters.

#### Finding AS-04: Missing Quoting of Executable Path in `HKCU\...\Run` Auto-Startup Registration (`Potential risk requiring verification` / `Missing control`)
- **Severity:** **MEDIUM**
- **Evidence:** `docs/PRD.md:116` and `docs/Architecture.md:204` specify `StartupService.cs` registering BornoKey under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (or the Startup folder), and the project path itself contains spaces (`06. Ridmik-Style Bengali Virtual Keyboard for Windows\BornoKey` or `C:\Program Files\BornoKey\`).
- **Risk:** Writing an unquoted executable path containing spaces into `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` creates a classic Windows **Unquoted Startup Path** weakness (`C:\Program.exe` execution hijacking).
- **Affected location:** `docs/PRD.md:116`, `docs/Architecture.md:204` (planned `src/BengaliKeyboard.Windows/StartupService.cs`).
- **Why it matters:** Unquoted registry run paths allow privilege escalation or code hijacking if a writable parent path prefix exists.
- **Recommended fix:** Always wrap the executable path in explicit double quotes (`"\"C:\\Program Files\\BornoKey\\BornoKey.exe\" --startup"`) when writing the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry value, and validate that the executable path is rooted inside the application's actual base directory (`AppContext.BaseDirectory`).
