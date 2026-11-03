# BornoKey (বর্ণকী) — Ridmik-Style Bengali Virtual Keyboard for Windows

**BornoKey** is a modern, lightweight, **100% offline** desktop Bengali (`বাংলা`) and English (`ENG`) virtual keyboard and system-wide input utility for **Windows 10 and Windows 11**, inspired by the intuitive layout and smart predictive typing experience of the Android Ridmik Keyboard.

Unlike phonetic transliteration tools where users must guess English spellings, **BornoKey** provides **Direct Bengali Unicode Typing** (`অ আ ই ঈ ক খ গ ঘ ◌া ◌ি ◌্`) through a floating, non-focus-stealing on-screen keyboard and optional physical keyboard mapping.

---

## ✨ Key Features

1. **Direct Ridmik-Style Bengali Unicode Input (`BengaliKeyboard.Input`)**
   - Direct Bengali consonant (`ব্যঞ্জনবর্ণ`), independent vowel (`স্বরবর্ণ`), dependent vowel sign (`কার`: `া ি ী ু ূ ৃ ে ৈ ো ৌ`), modifier (`ঁ ং ঃ ৎ`), and numeral (`০–৯`) output.
   - Automatic vowel-kar attachment (`ক` + `া` → `কা`), standalone vowel fallback after spaces, and **Indic Conjunct (`যুক্তাক্ষর` / Juktakhor) formation** via Hasanta (`্`), including `ক্ষ`, `জ্ঞ`, `ত্র`, `স্ত`, `ন্দ্র`, रेফ (`র্`), র-ফলা (`্র`), and য-ফলা (`্য`) with `ZWJ`/`ZWNJ` support and Unicode NFC normalization.
   - Smart Grapheme-Aware Backspace that cleanly peels back the last vowel sign or conjunct member without corrupting multi-codepoint clusters.

2. **6 Configurable JSON-Driven Keyboard Layouts (`BengaliKeyboard.Layouts`)**
   - **Bangla Primary (`Bangla.Primary`)** — High-frequency Bengali consonants, vowels, vowel signs (`কার`), and Hasanta (`্`).
   - **Bangla Shift (`Bangla.Shift`)** — Aspirated consonants, rare vowels (`ঋ ঐ ঔ`), modifiers, and common pre-composed conjuncts (`ক্ষ`, `জ্ঞ`, `ত্র`, `শ্র`).
   - **Bangla Numbers (`Bangla.Numbers`)** — Bengali numerals (`০ ১ ২ ৩ ৪ ৫ ৬ ৭ ৮ ৯`) and arithmetic operators.
   - **Bangla Symbols (`Bangla.Symbols`)** — Currency (`৳ $ € £ ¥`), Dari (`।`), Double Dari (`॥`), and extended punctuation.
   - **English QWERTY (`English.Qwerty`)** & **English Symbols (`English.Symbols`)**.
   - Path-traversal-hardened loader with embedded resource fallback if external JSON files are missing or modified.

3. **Smart Offline Bengali Predictive Text Engine (`BengaliKeyboard.Prediction`)**
   - **Real-Time Word Completion (`≤ 50 ms`):** Prefix matching (`আমা` → `আমার`, `আমাকে`, `আমাদের`, `আমায়`) powered by an indexed SQLite `BengaliWords` dictionary (`dictionary.db`, opened in `ReadOnly` mode at runtime) plus personalized `UserDictionary` frequency learning.
   - **Context-Aware Next-Word Prediction:** Offline `Trigram` → `Bigram` → `Unigram` backoff model (`আমি` → `বাংলা`, `ভালোবাসি`, `চাই`) with session-learned user bigrams.
   - **Interactive Suggestion Bar:** Displays 3–5 ranked candidates directly above the top key row for one-click replacement.

4. **Focus-Preserving System-Wide Text Injection (`WS_EX_NOACTIVATE` + `TSF` / `SendInput`)**
   - All floating overlays use Win32 `WS_EX_NOACTIVATE` and `WM_MOUSEACTIVATE` (`MA_NOACTIVATE`) so clicking virtual keys **never steals focus** from Notepad, Microsoft Word, Chrome, Edge, Firefox, VS Code, or Windows Search.
   - Dual-mode injection: Native C++ **Windows Text Services Framework (`BengaliKeyboard.TSF.dll`)** paired with a managed `Win32SendInputInjector` (`KEYEVENTF_UNICODE`) fallback that works immediately without requiring administrator COM registration.
   - Respects Windows UIPI boundaries when an elevated Administrator window is focused and automatically unhooks `WH_KEYBOARD_LL` in English mode or during Windows session lock.

5. **Adaptive 4-Mode Floating Window & Multi-Monitor DPI Engine (`BengaliKeyboard.Windows`)**
   - **Expanded Mode** (`850 × 310 px` default) • **Compact Mode** (`620 × 230 px` default) • **Minimal Floating `[বাংলা]` Badge** (`72 × 40 px`) • **Hidden to System Tray**.
   - `PerMonitorV2` DPI awareness, 8-edge/corner borderless resizing, 60% visible area clamping, and automatic primary-monitor snap-back when an external display is unplugged.

6. **Built-In Offline Emoji Keyboard (`BengaliKeyboard.Emoji`)**
   - 10 categories (*Frequently Used, Smileys & Emotion, People & Body, Animals & Nature, Food & Drink, Travel & Places, Activities, Objects, Symbols, Flags*).
   - Instant offline search by both English and Bengali keywords (`smile`, `heart`, `fire`, `হাসি`, `ভালোবাসা`, `আগুন`) and persistent SQLite `EmojiUsage` ranking.

7. **Privacy-First Event-Driven Clipboard History Manager (`BengaliKeyboard.Clipboard`)**
   - Zero-polling event-driven capture via Win32 `AddClipboardFormatListener` (`WM_CLIPBOARDUPDATE`).
   - Honors all 4 password-manager exclusion formats (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`, `ClipboardViewerIgnore`) so passwords from 1Password, Bitwarden, KeePassXC, and Windows CredUI are never captured.
   - Encrypts clipboard entries at rest using **Windows DPAPI (`DataProtectionScope.CurrentUser`)** and deduplicates via a per-user DPAPI-protected 256-bit `HMACSHA256` key.

8. **8-Section Customization & Themes Suite (`SettingsWindow`)**
   - Windows 11-inspired 2-pane Settings dialog covering **General, Keyboard, Appearance, Prediction, Emoji, Clipboard, Shortcuts, and Privacy**.
   - Instant runtime switching between **Dark Theme (Ridmik Signature)**, **Light Theme**, and **Windows System Default** (`AppsUseLightTheme`), plus custom `#RRGGBB` accent/key colors and adjustable floating opacity (`60%–100%`).

---

## ⌨️ Default Global Keyboard Shortcuts

| Action | Default Shortcut | Conflict Fallback |
|---|---|---|
| Toggle Language (`বাংলা` ↔ `ENG`) | `Ctrl + Space` | `Alt + Shift + B` |
| Show / Hide Floating Keyboard | `Ctrl + Shift + K` | Configurable in Settings |
| Open Emoji Panel | `Ctrl + Shift + E` | Configurable in Settings |
| Open Clipboard History Panel | `Ctrl + Shift + V` | Configurable in Settings |

---

## 🏗️ Solution Architecture

```text
BornoKey/
├── BengaliKeyboard.sln
├── Directory.Build.props
├── global.json
├── README.md
├── docs/
│   ├── PRD.md                     # Product Requirements Document
│   ├── Architecture.md            # System Architecture & Interop Design
│   ├── Design.md                  # UI/UX Design Tokens & Typography
│   ├── Rules.md                   # Engineering Rules & 10-Scenario Resilience Matrix
│   ├── Phases.md                  # 10-Phase Implementation Roadmap
│   ├── Memory.md                  # Project Progress Ledger
│   └── INSTALLATION.md            # Build, Deployment & Installer Guide
├── security/
│   ├── threat-model.md            # STRIDE Threat Model (TM-01 .. TM-08)
│   ├── secrets.md                 # Zero-Secrets & DPAPI Key Lifecycle Policy
│   ├── attack-surface.md          # Local IPC, Hook & SQLite Attack Surface
│   └── security-checklist.md      # 20-Domain Security Verification Ledger
├── data/
│   ├── Dictionary/bn_dictionary_seed.json
│   └── Emoji/emoji_catalog.json
├── assets/
│   └── Icons/ (app-icon.ico, tray-icon.ico)
├── installer/
│   └── BornoKeySetup.iss          # Inno Setup 6 Windows Installer Script
├── native/
│   └── BengaliKeyboard.TSF/       # C++17 Windows Text Services Framework COM DLL
└── src/
    ├── BengaliKeyboard.Core/      # Domain Models, Enums, Events & Service Interfaces
    ├── BengaliKeyboard.Storage/   # AppPaths (NTFS ACLs), DPAPI+HMAC, SQLite WAL & JSON Settings
    ├── BengaliKeyboard.Layouts/   # 6 Embedded/External JSON Layouts & LayoutManager
    ├── BengaliKeyboard.Input/     # UnicodeProcessor, BengaliInputEngine, SendInput & Hook
    ├── BengaliKeyboard.Prediction/# DictionaryService, WordCompletion & N-Gram Prediction
    ├── BengaliKeyboard.Emoji/     # Offline Emoji Repository, Search & Usage Service
    ├── BengaliKeyboard.Clipboard/ # WM_CLIPBOARDUPDATE Monitor & DPAPI ClipboardRepository
    ├── BengaliKeyboard.Windows/   # Multi-Monitor DPI, 4-Mode State Machine, Tray & Hotkeys
    ├── BengaliKeyboard.UI/        # WPF Windows, Controls, Dark/Light Themes & ViewModels
    ├── BengaliKeyboard.App/       # Single-Instance Host, DI Bootstrapper & Serilog Redaction
    └── BengaliKeyboard.Tests/     # xUnit Automated Test Suite
```

---

## 🚀 Quick Start (Build, Test & Run)

### Prerequisites
- **OS:** Windows 10 (21H2+) or Windows 11 (x64)
- **SDK:** .NET 10.0 Windows Desktop SDK (`net10.0-windows`)

### Build & Run Automated Tests
```powershell
# Build the entire solution (TreatWarningsAsErrors enabled)
dotnet build BengaliKeyboard.sln -c Release

# Run the complete xUnit test suite
dotnet test BengaliKeyboard.sln -c Release

# Launch BornoKey
dotnet run --project src/BengaliKeyboard.App/BengaliKeyboard.App.csproj
```

### Publish Release Package
```powershell
dotnet publish src/BengaliKeyboard.App/BengaliKeyboard.App.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
```

See [docs/INSTALLATION.md](./docs/INSTALLATION.md) for full installer compilation (`installer/BornoKeySetup.iss`), self-contained deployment, and optional C++ TSF registration instructions.

---

## 🔒 Privacy & Security By Design

- **100% Offline Execution:** Zero `HttpClient` references, zero telemetry endpoints, and zero network sockets.
- **No Keystroke or Clipboard Logging:** Rolling diagnostic logs under `%LOCALAPPDATA%\BengaliKeyboard\Logs\` are protected by `PrivacyDestructuringPolicy` and redacted model `ToString()` implementations.
- **Encrypted Local Storage:** `%LOCALAPPDATA%\BengaliKeyboard\` is hardened with current-user NTFS ACLs, `ClipboardHistory.Content` is encrypted via Windows DPAPI, and SQLite runs with `PRAGMA secure_delete = ON;`.
