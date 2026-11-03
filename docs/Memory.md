# Project Memory & Live Progress Tracker
## BornoKey — Ridmik-Style Bengali Virtual Keyboard for Windows

> **Maintenance Rule:** Update this file (`Memory.md`) regularly—before starting a new file/phase and immediately after completing, building, and testing any file or phase.

---

## 1. Current Project Snapshot

| Field | Current Status |
|---|---|
| **Current Stage** | **Phase 5: Windows System Tray, Auto-Startup & State Restoration** (100% Complete — Ready for Phase 6) |
| **Currently Active File** | None (Phase 5 Completed & Verified; Next up: Phase 6 `BengaliKeyboard.Emoji`) |
| **Last Completed File** | `src/BengaliKeyboard.Windows/SystemTrayService.cs` |
| **Solution Build Status** | **PASSING** (`dotnet build BengaliKeyboard.sln` — 0 Warnings, 0 Errors) |
| **Automated Test Status** | **PASSING** (`27 / 27` xUnit tests passing across Phase 1, Phase 2, Phase 3, Phase 4 & Phase 5) |
| **Last Updated** | 2026-11-03 |

---

## 2. What Has Been Completed

### Pre-Coding Specification & Architecture Documents (100% Complete)
- [x] [`docs/PRD.md`](./PRD.md) — Complete Product Requirements Document (Product Vision, Target Users & Personas, 9 Core Feature Modules, Non-Functional Targets, Compatibility Matrix, Definition of Done).
- [x] [`docs/Architecture.md`](./Architecture.md) — Complete System Architecture (Component & Sequence Diagrams, State Machine, Full Solution & `%LOCALAPPDATA%` File Tree, Tech Stack, `WS_EX_NOACTIVATE` Interop, SQLite Schema, Multi-Monitor DPI Algorithm).
- [x] [`docs/Rules.md`](./Rules.md) — Engineering Rules & AI Boundaries (Approved Libraries, Prohibited Anti-Patterns, 10-Scenario Error Handling Matrix, Security & Privacy Guarantees, AI Agent Operating Rules).
- [x] [`docs/Phases.md`](./Phases.md) — 10-Phase Implementation Roadmap with exact file lists, deliverables, and verification criteria per phase.
- [x] [`docs/Design.md`](./Design.md) — Complete UI/UX Design System (Dark & Light Semantic Color Tokens, `Noto Sans Bengali` + `Segoe UI Variable` Font Stack, Typography Scale, Keycap & Floating Window Anatomy).
- [x] [`docs/Memory.md`](./Memory.md) — Live session memory and file-by-file progress tracker initialized (moved to `docs/`).

---

## 3. Currently Active Work Item

- **Current Phase:** **Phase 5 Complete** → Ready for **Phase 6 — Emoji Keyboard, Keyword Search & Frequently Used History**
- **File Currently Being Worked On:** None (Phase 5 completed, tested twice, and verified)
- **Next Immediate Action:** Start Phase 6 (`data/Emoji/emoji_catalog.json`, `src/BengaliKeyboard.Emoji/`, `EmojiWindow.xaml`, `EmojiViewModel.cs`, and `EmojiUsageTests.cs`).

---

## 4. Phase-by-Phase File Completion Checklist

### Phase 1 — Project Foundation, DI & Storage Infrastructure (100% Complete)
- [x] `BengaliKeyboard.sln`, `global.json`, `Directory.Build.props`
- [x] `src/BengaliKeyboard.Core/BengaliKeyboard.Core.csproj`
- [x] `src/BengaliKeyboard.Core/Enums/` (`KeyboardMode.cs`, `LanguageMode.cs`, `LayoutLayer.cs`, `KeyType.cs`, `AppTheme.cs`)
- [x] `src/BengaliKeyboard.Core/Models/` (`KeyDefinition.cs`, `KeyboardLayoutModel.cs`, `SuggestionCandidate.cs`, `EmojiItem.cs`, `ClipboardEntry.cs`, `WindowStateConfig.cs`, `AppSettings.cs`)
- [x] `src/BengaliKeyboard.Core/Interfaces/` (`IBengaliInputEngine.cs`, `ITextInjector.cs`, `ILayoutManager.cs`, `IPredictionService.cs`, `IEmojiService.cs`, `IClipboardService.cs`, `IWindowManagerService.cs`, `ISystemTrayService.cs`, `IStartupService.cs`, `ISettingsService.cs`)
- [x] `src/BengaliKeyboard.Core/Events/` (`LanguageChangedMessage.cs`, `SuggestionsUpdatedMessage.cs`, `WindowModeChangedMessage.cs`, `ThemeChangedMessage.cs`)
- [x] `src/BengaliKeyboard.Storage/BengaliKeyboard.Storage.csproj`
- [x] `src/BengaliKeyboard.Storage/AppPaths.cs` (with current-user NTFS ACL hardening)
- [x] `src/BengaliKeyboard.Storage/Security/LocalDataProtector.cs` (Windows DPAPI + keyed `HMACSHA256`)
- [x] `src/BengaliKeyboard.Storage/Database/SqliteConnectionFactory.cs` (WAL + `secure_delete` + `ReadOnly` dictionary mode + lock retry)
- [x] `src/BengaliKeyboard.Storage/Database/DatabaseInitializer.cs` (Full schemas + automatic `.corrupt.bak` recovery)
- [x] `src/BengaliKeyboard.Storage/Configuration/JsonSettingsService.cs` (Atomic `.tmp` write + clamping + `.corrupt.bak` recovery)
- [x] `src/BengaliKeyboard.UI/BengaliKeyboard.UI.csproj`, `Themes/Colors.Dark.xaml`, `Windows/KeyboardWindow.xaml` (`.cs` with `WS_EX_NOACTIVATE`)
- [x] `src/BengaliKeyboard.App/BengaliKeyboard.App.csproj` & `app.manifest` (`asInvoker`, `PerMonitorV2` DPI)
- [x] `src/BengaliKeyboard.App/Program.cs` & `SingleInstanceManager.cs` (`Local\` SID-scoped Mutex + `CurrentUserOnly` Named Pipe)
- [x] `src/BengaliKeyboard.App/Bootstrapper.cs` & `Logging/PrivacyDestructuringPolicy.cs`
- [x] `src/BengaliKeyboard.App/App.xaml` & `App.xaml.cs`
- [x] `src/BengaliKeyboard.Tests/BengaliKeyboard.Tests.csproj` & `Phase1FoundationTests.cs` (`8/8` passing)

### Phase 2 — Ridmik-Style Keyboard UI & Configurable Layout Engine (100% Complete)
- [x] `src/BengaliKeyboard.Layouts/BengaliKeyboard.Layouts.csproj`
- [x] `src/BengaliKeyboard.Layouts/Definitions/Bangla/PrimaryLayout.json`
- [x] `src/BengaliKeyboard.Layouts/Definitions/Bangla/ShiftLayout.json`
- [x] `src/BengaliKeyboard.Layouts/Definitions/Bangla/NumberLayout.json`
- [x] `src/BengaliKeyboard.Layouts/Definitions/Bangla/SymbolLayout.json`
- [x] `src/BengaliKeyboard.Layouts/Definitions/English/QwertyLayout.json`
- [x] `src/BengaliKeyboard.Layouts/Definitions/English/SymbolLayout.json`
- [x] `src/BengaliKeyboard.Layouts/LayoutLoader.cs` (with path-traversal protection, duplicate ID check, and embedded resource fallback)
- [x] `src/BengaliKeyboard.Layouts/LayoutManager.cs`
- [x] `src/BengaliKeyboard.UI/BengaliKeyboard.UI.csproj`
- [x] `src/BengaliKeyboard.UI/Themes/Colors.Dark.xaml`, `Typography.xaml`, `KeyStyles.xaml`
- [x] `src/BengaliKeyboard.UI/Controls/KeyboardKeyControl.xaml` (`.cs` with dotted-circle combining glyph formatting & right-click alternate output)
- [x] `src/BengaliKeyboard.UI/Controls/SuggestionBarControl.xaml` (`.cs`)
- [x] `src/BengaliKeyboard.UI/Controls/LanguageToggleControl.xaml` (`.cs`)
- [x] `src/BengaliKeyboard.UI/Controls/HeaderToolbarControl.xaml` (`.cs`)
- [x] `src/BengaliKeyboard.UI/Windows/KeyboardWindow.xaml` (`.cs` with dynamic proportional star-sized grid)
- [x] `src/BengaliKeyboard.UI/ViewModels/KeyboardViewModel.cs`
- [x] `src/BengaliKeyboard.Tests/LayoutLoaderTests.cs` (`6/6` passing; `14/14` total passing)

### Phase 3 — Bengali Unicode Input Engine & System-Wide Text Injection (100% Complete)
- [x] `src/BengaliKeyboard.Input/BengaliKeyboard.Input.csproj`
- [x] `src/BengaliKeyboard.Input/UnicodeProcessor.cs` (with Indic Conjunct Linking for multi-consonant Juktakhor & NFC diffing)
- [x] `src/BengaliKeyboard.Input/BengaliInputEngine.cs`
- [x] `src/BengaliKeyboard.Input/OnScreenInputService.cs`
- [x] `src/BengaliKeyboard.Input/PhysicalKeyboardService.cs` (dynamically unhooked in `English` mode and during session lock)
- [x] `src/BengaliKeyboard.Input/TSF/Win32SendInputInjector.cs` (with `dwExtraInfo` self-injection marker & UIPI elevation check)
- [x] `src/BengaliKeyboard.Input/TSF/TsfBridgeService.cs` (with `CurrentUserOnly` PipeSecurity & Program Files path trust check)
- [x] `native/BengaliKeyboard.TSF/` (`BengaliKeyboard.TSF.vcxproj`, `DllMain.cpp`, `Register.cpp`, `TextService.h`, `TextService.cpp`, `CompositionManager.cpp`)
- [x] `src/BengaliKeyboard.Tests/UnicodeProcessorTests.cs` (`6/6` passing; `20/20` total passing)

### Phase 4 — Adaptive Floating Keyboard & 4-Mode Window Management (100% Complete)
- [x] `src/BengaliKeyboard.UI/Windows/FloatingIconWindow.xaml` (`.cs` with click-vs-drag threshold & right-click language toggle)
- [x] `src/BengaliKeyboard.Windows/BengaliKeyboard.Windows.csproj`
- [x] `src/BengaliKeyboard.Windows/NativeMethods.cs`
- [x] `src/BengaliKeyboard.Windows/MonitorService.cs` (with 60% intersection rule & per-monitor DPI scaling)
- [x] `src/BengaliKeyboard.Windows/WindowPositionService.cs` (with 4-mode state machine, disconnected monitor snap-back, and 300ms debounced persistence)
- [x] `src/BengaliKeyboard.Tests/WindowPositionRestoreTests.cs` (`4/4` passing; `24/24` total passing)

### Phase 5 — Windows System Tray, Auto-Startup & State Restoration (100% Complete)
- [x] `src/BengaliKeyboard.Windows/SystemTrayService.cs` (Win32 `Shell_NotifyIconW` + thread-safe 10-item context menu)
- [x] `src/BengaliKeyboard.Windows/StartupService.cs` (quoted executable path hardening against unquoted path hijacking)
- [x] `src/BengaliKeyboard.Windows/GlobalHotkeyService.cs` (Win32 `RegisterHotKey` with conflict-safe `Alt+Shift+B` fallback)
- [x] `assets/Icons/app-icon.ico` & `tray-icon.ico` (`27/27` total tests passing)

### Phase 6 — Emoji Keyboard, Keyword Search & Frequently Used History
- [ ] `data/Emoji/emoji_catalog.json`
- [ ] `src/BengaliKeyboard.Emoji/BengaliKeyboard.Emoji.csproj`
- [ ] `src/BengaliKeyboard.Emoji/EmojiRepository.cs`
- [ ] `src/BengaliKeyboard.Emoji/EmojiSearchService.cs`
- [ ] `src/BengaliKeyboard.Emoji/EmojiUsageService.cs`
- [ ] `src/BengaliKeyboard.UI/Windows/EmojiWindow.xaml` (`.cs`)
- [ ] `src/BengaliKeyboard.UI/ViewModels/EmojiViewModel.cs`
- [ ] `src/BengaliKeyboard.Tests/EmojiUsageTests.cs`

### Phase 7 — Privacy-First Clipboard History Manager
- [ ] `src/BengaliKeyboard.Clipboard/BengaliKeyboard.Clipboard.csproj`
- [ ] `src/BengaliKeyboard.Clipboard/ClipboardMonitor.cs`
- [ ] `src/BengaliKeyboard.Clipboard/ClipboardRepository.cs`
- [ ] `src/BengaliKeyboard.Clipboard/ClipboardHistoryService.cs`
- [ ] `src/BengaliKeyboard.UI/Windows/ClipboardWindow.xaml` (`.cs`)
- [ ] `src/BengaliKeyboard.UI/ViewModels/ClipboardViewModel.cs`
- [ ] `src/BengaliKeyboard.Tests/ClipboardServiceTests.cs`

### Phase 8 — Smart Offline Bengali Predictive Text Engine
- [ ] `data/Dictionary/bn_dictionary_seed.json`
- [ ] `src/BengaliKeyboard.Prediction/BengaliKeyboard.Prediction.csproj`
- [ ] `src/BengaliKeyboard.Prediction/DictionaryService.cs`
- [ ] `src/BengaliKeyboard.Prediction/WordCompletionService.cs`
- [ ] `src/BengaliKeyboard.Prediction/NGramModel.cs`
- [ ] `src/BengaliKeyboard.Prediction/NextWordPredictionService.cs`
- [ ] `src/BengaliKeyboard.Prediction/SuggestionRankingService.cs`
- [ ] `src/BengaliKeyboard.UI/ViewModels/SuggestionBarViewModel.cs`
- [ ] `src/BengaliKeyboard.Tests/PredictionEngineTests.cs`

### Phase 9 — Customization, Themes & Settings Panel
- [ ] `src/BengaliKeyboard.UI/Themes/Colors.Light.xaml`
- [ ] `src/BengaliKeyboard.UI/Windows/SettingsWindow.xaml` (`.cs`)
- [ ] `src/BengaliKeyboard.UI/ViewModels/SettingsViewModel.cs`

### Phase 10 — Comprehensive Testing, Documentation & Release Installer
- [ ] `README.md`
- [ ] `docs/INSTALLATION.md`
- [ ] `installer/BornoKeySetup.iss`

---

## 5. Key Architectural Decisions Log

1. **Direct Bengali Typing Only:** No Romanized English-to-Bengali phonetic transliteration is used as the primary input method; keys output direct Bengali Unicode characters mapped to the Ridmik layout.
2. **Focus Preservation (`WS_EX_NOACTIVATE`):** All floating keyboard overlays use Win32 `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE` (`MA_NOACTIVATE`) so clicking virtual keys never steals focus from external Windows applications.
3. **Dual Input Injection (`TSF` + `SendInput`):** Native C++ TSF component (`native/BengaliKeyboard.TSF`) is paired with a managed `Win32SendInputInjector` (`KEYEVENTF_UNICODE`) so typing works immediately both with and without COM registration.
4. **100% Offline & Privacy-First:** Word completion, bigram/trigram next-word prediction, emoji search, and clipboard history run strictly locally against `%LOCALAPPDATA%\BengaliKeyboard\` SQLite databases with zero telemetry or keystroke logging.
