# Engineering Rules, Standards & AI Boundaries
## BornoKey — Ridmik-Style Bengali Virtual Keyboard for Windows

This document establishes the mandatory technical rules, approved/prohibited libraries, error-handling protocols, privacy boundaries, and operational guidelines for AI coding agents working on **BornoKey**.

---

## 1. What to Use (Approved Stack, Libraries & Patterns)

### 1.1 Approved Core Technologies & NuGet Packages
| Category | Approved Choice | Mandatory Usage Pattern |
|---|---|---|
| **Framework & UI** | `.NET 10.0-windows` + **WPF** | Use hardware-accelerated WPF with custom `ControlTemplate`s, `ResourceDictionary` themes, and proportional `Viewbox`/grid layouts. |
| **MVVM Framework** | `CommunityToolkit.Mvvm` | Use `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, and `WeakReferenceMessenger` for decoupled communication. |
| **Dependency Injection** | `Microsoft.Extensions.DependencyInjection` | Register all services, repositories, and ViewModels in `Bootstrapper.cs`. Use constructor injection exclusively. |
| **Local Database** | `Microsoft.Data.Sqlite` | Use parameterized SQL queries (`SqliteCommand`), WAL journal mode (`PRAGMA journal_mode=WAL;`), and connection pooling. |
| **JSON Serialization** | `System.Text.Json` | Load keyboard layouts (`Definitions/**/*.json`), emoji catalogs, and persist `settings.json` using atomic file writes. |
| **System Input** | **Windows TSF (C++ COM)** + **Win32 `SendInput`** | Implement native TSF text service in `native/BengaliKeyboard.TSF/` and provide managed `SendInput` (`KEYEVENTF_UNICODE`) in `BengaliKeyboard.Input` so system-wide typing works in all scenarios. |
| **Window Focus Preservation** | Win32 `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE` | Prevent the floating virtual keyboard from stealing focus from the user's active document or browser tab. |
| **Clipboard Monitoring** | Win32 `AddClipboardFormatListener` | Listen for `WM_CLIPBOARDUPDATE` window messages purely on an event-driven basis. |
| **Logging** | `Serilog` + `Serilog.Sinks.File` | Write structured operational logs to `%LOCALAPPDATA%\BengaliKeyboard\Logs\` with rolling file size limits. |
| **Automated Testing** | `xUnit` | Write unit and integration tests for Unicode composition, JSON layout parsing, SQLite repositories, prediction ranking, and window bounds restoration. |

### 1.2 Mandatory Coding Practices
- **Unicode & Grapheme Accuracy:** Always use `System.Globalization.StringInfo` and `NormalizationForm.FormC` when inspecting, counting, or backspacing Bengali text so multi-codepoint clusters (Consonant + *Hasanta* `্` + Consonant + *Kar* vowel sign) are handled accurately.
- **Data-Driven Keyboard Layouts:** Every key row, width ratio, primary/shift character, and key action must be loaded from JSON layout files via `LayoutLoader.cs` and `LayoutManager.cs`.
- **Asynchronous Off-Thread Data Access:** Execute all dictionary prefix lookups, bigram/trigram queries, emoji searches, and clipboard database writes asynchronously (`Task` / `async-await`) off the WPF UI thread, marshaling only the final observable collection updates to the `Dispatcher`.
- **Single-Instance Application:** Enforce a named system-wide `Mutex` (`Global\BornoKey_SingleInstance_Mutex`) in `Program.cs` and signal the running instance via Named Pipe or window message to restore its window if launched a second time.

---

## 2. What to Avoid (Strictly Prohibited)

### 2.1 Prohibited Frameworks & Libraries
- **NO Web Wrappers:** Never use Electron, WebView2, CefSharp, Blazor Hybrid, React Native, or Tauri. This is a native, lightweight Windows WPF application.
- **NO Heavy ORMs:** Avoid `EntityFrameworkCore`, `NHibernate`, or reflection-heavy mappers that increase startup time and memory footprint. Use `Microsoft.Data.Sqlite` directly.
- **NO Bloated UI Suites:** Avoid heavy commercial or third-party theme frameworks (e.g., DevExpress, Telerik, Syncfusion, heavy Material Design suites) that bloat RAM beyond the 50–100 MB target. Use clean custom WPF `ResourceDictionary` styles.
- **NO Cloud / Paid AI APIs:** Never integrate OpenAI, Gemini, Claude, Azure Cognitive Services, or any external HTTP API for word completion, next-word prediction, or emoji search. All intelligence must run 100% offline.

### 2.2 Prohibited Architectural & Functional Anti-Patterns
- **NO Phonetic Transliteration as Primary Engine:** Do not build an English-to-Bengali phonetic converter (where typing `ami` outputs `আমি`) as the primary typing mode. Users must type direct Bengali characters mapped to the Ridmik layout.
- **NO Hardcoded XAML Keys:** Do not create 50+ individual hardcoded `<Button Content="ক"/>` elements in `KeyboardWindow.xaml`. Render rows and keys dynamically via `ItemsControl` bound to the active `KeyboardLayoutModel`.
- **NO Clipboard or Focus Polling Timers:** Never run a `DispatcherTimer` or `Thread.Sleep` loop every 100ms to check `Clipboard.GetText()` or active window handles. Use OS event notifications (`WM_CLIPBOARDUPDATE`, WinEvent hooks).
- **NO UI Thread Blocking:** Never run `.Result`, `.Wait()`, or synchronous SQLite table scans on the UI thread during typing.
- **NO Keystroke or Clipboard Logging:** Never log typed characters, words, or clipboard contents into Serilog files or console output under any log level (`Debug`, `Information`, `Error`).
- **NO Mandatory Administrator Elevation:** The app must run under standard user privileges (`requestedExecutionLevel level="asInvoker"`) and store all mutable files inside `%LOCALAPPDATA%\BengaliKeyboard\`.

---

## 3. Error Handling & Resilience Standards

Every module must handle operational edge cases gracefully without crashing, freezing the UI, or losing user configuration:

| Failure Scenario | Mandatory Recovery & Handling Strategy |
|---|---|
| **1. Unexpected Windows Restart / Crash** | Write `settings.json` atomically (`settings.json.tmp` → `File.Move` with overwrite) immediately after debounced (300ms) window moves, resizes, or mode switches—never wait only for `Application.Exit`. |
| **2. Disconnected Secondary Monitor** | On startup and `SystemEvents.DisplaySettingsChanged`, validate saved `(Left, Top, Width, Height)` against `Screen`/`EnumDisplayMonitors`. If off-screen, reposition smoothly onto the primary display's working area. |
| **3. Invalid / Corrupted `settings.json`** | Catch `JsonException` / `IOException`, back up the corrupted file to `settings.corrupt.bak`, log a warning, and initialize with safe default settings (`Expanded`, `850x310`, Dark theme, Bangla mode). |
| **4. SQLite Database Locked or Busy** | Configure `DefaultTimeout = 5` seconds on `SqliteConnectionStringBuilder`, enable `PRAGMA journal_mode=WAL;`, and retry transient `SqliteException` (`SQLite Error 5: 'database is locked'`) up to 3 times with exponential backoff. |
| **5. Missing Dictionary or Emoji Seed Files** | If `dictionary.db` or seed JSON files are missing/deleted, auto-regenerate from embedded fallback resources or return empty suggestion lists gracefully without crashing typing functionality. |
| **6. Clipboard Access Denied (`COMException`)** | Another process may temporarily lock the Windows clipboard (`CLIPBRD_E_CANT_OPEN`). Retry up to 5 times with a `15ms` delay; if still locked, skip the event silently without throwing to the UI. |
| **7. Target Window is Elevated (UIPI Boundary)** | When a standard-user process attempts `SendInput` into an Administrator window, Windows UIPI blocks injection. Catch/detect elevation mismatch and surface a subtle non-intrusive status tooltip rather than crashing. |
| **8. TSF COM Registration Unavailable** | Automatically fall back to `Win32SendInputInjector` (`KEYEVENTF_UNICODE`) + low-level keyboard hook so both on-screen and physical Bengali typing continue functioning seamlessly. |
| **9. Dynamic DPI or Resolution Change** | Subscribe to `DpiChanged` on WPF windows and recalculate physical-to-logical pixel coordinates so the keyboard never clips or distorts. |
| **10. Unhandled Exceptions** | Register global handlers on `Application.Current.DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` to flush state and log stack traces safely. |

---

## 4. Boundaries & Rules for AI Coding Agents

1. **Read All 6 Specification Files Before Coding:**
   - Always check `docs/PRD.md`, `docs/Architecture.md`, `docs/Rules.md`, `docs/Phases.md`, `docs/Design.md`, and `docs/Memory.md` before creating or modifying code.
2. **Work Strictly Phase by Phase (`docs/Phases.md`):**
   - Never attempt to dump the entire application in a single untested step. Implement one phase at a time, compile with `dotnet build`, run `dotnet test`, verify functionality, and record progress.
3. **Zero Placeholder / Stub Tolerance:**
   - Do **not** leave `// TODO: implement later`, `throw new NotImplementedException()`, empty command handlers, or buttons that do nothing. Every feature in the active phase must be complete and wired up end-to-end.
4. **No Fake / Simulated Input:**
   - Never simulate system-wide typing by displaying hardcoded example strings in an internal textbox. On-screen and physical keys must genuinely emit Unicode output via `ITextInjector`.
5. **Preserve Ridmik Layout Fidelity:**
   - Do not invent an arbitrary Bengali layout. Follow the Ridmik Keyboard layout structure (dedicated number row, primary/shift layers, vowel sign handling, `[ বাংলা ]`/`[ ENG ]` toggle, suggestion bar directly above the keys).
6. **Keep `docs/Memory.md` Continuously Updated:**
   - Before starting a file and immediately after completing/verifying a file or phase, update `docs/Memory.md` with:
     - Completed phases and files
     - Currently active file and task
     - Build/test status and any architectural notes
7. **Respect Security & Sandbox Rules:**
   - Never bypass OS security controls, never capture passwords from secure desktops, and never add network telemetry.
