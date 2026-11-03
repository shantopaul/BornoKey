# Product Requirements Document (PRD)
## BornoKey — Ridmik-Style Bengali Virtual Keyboard for Windows

| Metadata | Details |
|---|---|
| **Product Name** | BornoKey (Ridmik-Style Bengali Virtual Keyboard for Windows) |
| **Project Type** | Native Windows Desktop Application & System-Wide Input Method |
| **Target Platform** | Windows 10 (1903+) & Windows 11 (x64 / ARM64) |
| **Primary Stack** | C#, .NET 10, WPF (MVVM), SQLite, Native C++ TSF / Win32 Interop |
| **Architecture Mode** | Modular, Lightweight, 100% Offline-First, Privacy-First |
| **Document Version** | 1.0.0 |
| **Status** | Approved Pre-Coding Specification |

---

## 1. Executive Summary & Product Vision

### 1.1 What We Are Building
**BornoKey** is a native, lightweight, professional-grade Bengali virtual and physical keyboard application for Windows PCs that brings the familiar **Android Ridmik Keyboard** typing experience to the Windows desktop.

Unlike web-based wrappers, simple UI mockups, or pure Roman-to-Bengali phonetic transliteration tools, BornoKey is a **true system-wide Windows input utility**. It enables users to type Bengali Unicode characters directly via an on-screen floating touch/mouse keyboard or their physical keyboard into any active Windows application—complete with real-time offline Bengali word completion, next-word prediction, an integrated emoji keyboard with search and frequency tracking, and a privacy-first clipboard history manager.

### 1.2 Core Product Philosophy
1. **Lightweight:** Minimal CPU (< 1% idle) and RAM (50–100 MB target) footprint.
2. **Fast:** Immediate key response and < 50 ms offline candidate lookup latency.
3. **Offline-First:** 100% of typing, word completion, next-word prediction, emoji search, and clipboard features operate without internet access.
4. **Persistent:** Automatically remembers and restores exact window mode, coordinates, dimensions, monitor placement, language, and theme across Windows restarts.
5. **Professional:** Modern Windows 11-inspired UI with Ridmik's dark visual identity, crisp Bengali typography, and rounded key geometry.
6. **Flexible:** Four presentation modes (Expanded, Compact, Minimal Floating Icon, System Tray Hidden) with proportional scaling.
7. **Accurate:** Standards-compliant Bengali Unicode output, including vowel signs (*kars*), *hasanta* (`্`), conjuncts (*juktakkhor*), and diacritics.
8. **System-Wide:** Direct text injection into any focused Windows application without losing target window focus or requiring manual copy-paste.
9. **Privacy-First:** Zero telemetry, zero cloud uploads, local-only SQLite storage, and no keystroke logging.
10. **Modular:** Clean separation between UI, Input Engine, Layout Definitions, Prediction, Emoji, Clipboard, Windows Interop, and Storage.

---

## 2. Targeted Users & Personas

### 2.1 Primary User Segments
| User Persona | Background & Pain Point | How BornoKey Solves It |
|---|---|---|
| **Mobile-First Bengali Desktop Users** | Millions of Bangladeshi and Bengali-speaking users type effortlessly on Android using Ridmik Keyboard, but struggle on Windows PCs with steep desktop layouts (e.g., traditional Bijoy) or phonetic transliteration quirks. | Provides the exact visual layout, key positioning, suggestion bar, and direct character behavior they already know from Ridmik on Android. |
| **Writers, Journalists, & Students** | Draft long-form Bengali documents, assignments, emails, and articles in MS Word, Google Docs, Notepad, and browsers; suffer from repetitive typing and slow punctuation/emoji access. | Real-time 3–5 word completions, context-aware bigram/trigram next-word predictions, and instant `[ বাংলা ]` / `[ ENG ]` switching dramatically speed up composition. |
| **Social Media Managers & Support Agents** | Frequently switch between Bengali text, English terms, emojis, and repeated canned responses across Chrome, Edge, WhatsApp Desktop, and Messenger. | Built-in Emoji Search, Frequently Used Emoji History, and Pinned Clipboard History Manager inside the same floating keyboard window. |
| **2-in-1 Laptop, Tablet & Mouse/Hybrid Users** | Use touchscreens or prefer clicking on-screen keys for specific Bengali characters and conjuncts while keeping the keyboard unobtrusive on screen. | Draggable, proportionally resizable floating window that shrinks to a tiny `[ বাংলা ]` floating badge or hides to the system tray on demand. |

---

## 3. Comprehensive Feature Requirements

### 3.1 Ridmik-Style Keyboard Layout System
- **Visual Source of Truth:** Reproduces the exact key arrangement, row structure, and character mapping from the reference Ridmik Keyboard screenshots.
- **Data-Driven Layout Definitions:** All layouts are loaded dynamically from JSON configuration files (`PrimaryLayout.json`, `ShiftLayout.json`, `NumberLayout.json`, `SymbolLayout.json`, `QwertyLayout.json`) rather than hardcoded in XAML.
- **Supported Layers & Rows:**
  - Dedicated Top Number Row (`১ ২ ৩ ৪ ৫ ৬ ৭ ৮ ৯ ০` in Bangla mode / `1 2 3 4 5 6 7 8 9 0` in English mode).
  - Primary Bengali Character Rows (vowels, consonants, common vowel signs).
  - Shift / Alternate Character Layer (secondary consonants, rare vowels, conjunct helpers, diacritics).
  - Dedicated Symbol Layouts (Bengali & English punctuation, currency `৳`, mathematical & special symbols).
  - Full English QWERTY Layout.
  - Functional & Action Keys: `Shift`, `Backspace` (with press-and-hold repeat), `Enter`, `Spacebar` (displaying current language), `[ বাংলা / ENG ]` toggle, `?123` / Symbol toggle, and `Emoji` panel trigger.

### 3.2 Bengali Unicode Input Engine & System-Wide Integration
- **Direct Bengali Input (Non-Transliteration):** Users type Bengali characters directly according to the active layout—never requiring Romanized phonetic input (`ami banglay likhi`).
- **Complete Unicode Script Handling:**
  - Independent vowels (`অ`–`ঔ`), consonants (`ক`–`ঁ`), dependent vowel signs / *kars* (`া`, `ি`, `ী`, `ু`, `ূ`, `ৃ`, `ে`, `ৈ`, `ো`, `ৌ`).
  - Virama / *Hasanta* (`্`) for forming consonant clusters / *juktakkhor* (e.g., `ক্ষ`, `জ্ঞ`, `ত্র`, `প্র`, `ঙ্গ`, `ম্প`).
  - Bengali numerals (`০`–`৯`), *Dari* (`।`), *Chandrabindu* (`ঁ`), *Anusvara* (`ং`), *Visarga* (`ঃ`), and *Nukta* (`়`).
  - Automatic Unicode normalization (NFC) and smartgrapheme-aware backspace handling.
- **Dual Input Channels Sharing One Engine:**
  - **On-Screen Virtual Keyboard:** Clicking or tapping virtual keys never steals focus from the target application (`WS_EX_NOACTIVATE`) and immediately inserts characters at the active caret.
  - **Physical Keyboard Integration:** When Bengali mode is active, physical keystrokes map through the same `BengaliInputEngine` and update the live suggestion bar.
- **Windows Integration Architecture:**
  - Primary production integration via **Windows Text Services Framework (TSF)** for native composition, caret tracking, and universal app compatibility.
  - High-reliability **Win32 `SendInput` (`KEYEVENTF_UNICODE`)** fallback engine so typing works out-of-the-box during development and in environments where COM registration is restricted.

### 3.3 Instant Language Switching (`বাংলা` ↔ `ENG`)
- **One-Click On-Screen Toggle:** Dedicated `[ বাংলা ] [ ENG ]` indicator/button switches both physical and virtual keyboard output instantaneously.
- **Global Keyboard Shortcut:** Configurable system-wide hotkey (e.g., `Ctrl+Space` or `Alt+Shift+B`, customizable to avoid conflicts) toggles language mode from anywhere in Windows.
- **State Persistence:** Remembers the active language across sessions.

### 3.4 Offline Smart Bengali Predictive Text Engine
- **Suggestion Bar UI:** Located directly above the top key row; displays **3 to 5 ranked candidates** with one-click insertion and optional physical keyboard selection.
- **Capability 1 — Real-Time Word Completion:**
  - Detects the current partial Bengali word prefix as the user types.
  - Queries the indexed local dictionary (`bn_words.db` / prefix trie + SQLite frequency table) in **≤ 50 ms**.
  - Example: Typing `আমা` surfaces `[আমার] [আমাকে] [আমাদের] [আমায়]`.
  - Clicking a candidate replaces the partial prefix cleanly while preserving surrounding text and spacing.
- **Capability 2 — Context-Aware Next-Word Prediction:**
  - Triggered immediately after a word is completed (spacebar or suggestion click).
  - Uses an offline **Unigram + Bigram + Trigram language model** (`bigrams.db`, `trigrams.db`) with context ranking and punctuation awareness.
  - Example: Typing `আমি` suggests `[বাংলা] [একটি] [আজ] [তোমাকে] [ভালো]`; typing `আমি বাংলা` suggests `[লিখতে] [ভাষা] [শিখছি] [পড়তে]`.

### 3.5 Emoji Keyboard, Search & Frequently Used History
- **Dedicated Emoji Panel:** Accessible via the keyboard's Emoji key without leaving the floating window footprint.
- **Keyword Search:** Instant offline search across emoji names, keywords, and aliases (e.g., `smile` → `😀 😃 😄 😁 😊`, `heart` → `❤️ 💙 💚 💛 💜`, `food` → `🍎 🍕 🍔 🍟`).
- **10 Standard Categories:**
  1. Frequently Used
  2. Smileys & Emotions
  3. People & Body
  4. Animals & Nature
  5. Food & Drink
  6. Travel & Places
  7. Activities
  8. Objects
  9. Symbols
  10. Flags
- **Mandatory Frequently Used History:**
  - Every inserted emoji increments `UsageCount` and updates `LastUsedUtc` in SQLite (`EmojiUsage` table).
  - Ranked by composite frequency + recency score, deduplicated, persisted across restarts, with user-configurable history limit and one-click clear.

### 3.6 Built-In Clipboard History Manager
- **Event-Driven Monitoring:** Uses Windows `AddClipboardFormatListener` (`WM_CLIPBOARDUPDATE`)—zero background polling.
- **Core Capabilities:**
  - Captures copied plain/Unicode text (Bengali, English, emojis) into local SQLite (`ClipboardHistory` table).
  - Displays scrollable list withPinned items at the top (`📌`).
  - Instant real-time search through saved clips.
  - Click to insert/paste directly into the currently focused application or re-copy to clipboard.
  - Pin/Unpin individual clips, delete individual clips, or Clear All unpinned history.
- **Strict Privacy Controls:**
  - 100% local storage; never uploaded or logged to disk logs.
  - Toggle to pause/disable clipboard monitoring at any time.
  - Configurable maximum item capacity and retention period.
  - Ignores clipboard updates originating from excluded/password-manager formats (`CF_EXCLUDE_CLIPBOARD_CONTENT_FROM_MONITOR_PROCESSING`) or secure desktop contexts.

### 3.7 Floating Keyboard Window & 4 Presentation Modes
- **Mode A — Expanded Floating Keyboard:** Full virtual keyboard with suggestion bar, number row, character grid, and header toolbar.
- **Mode B — Compact Floating Keyboard:** Reduced footprint optimized for smaller screens while preserving readable Bengali glyphs and comfortable hit targets.
- **Mode C — Minimal Floating Icon Mode:** Shrinks into a draggable `[ বাংলা ]` floating badge anywhere on screen; clicking restores the previous Expanded or Compact state.
- **Mode D — Hidden / System Tray Mode:** Zero desktop footprint; runs silently in the background with physical keyboard hooks/TSF active and accessible via the Windows Notification Area icon.
- **Window Management Capabilities:**
  - Smooth drag via header bar, free proportional resizing within safe min/max bounds, Always-on-Top toggle, multi-monitor bounds clamping, and automatic per-monitor DPI scaling.

### 3.8 Windows System Tray, Auto-Startup & State Restoration
- **System Tray Icon & Context Menu:**
  - Left-click / Double-click restores the last visible keyboard mode.
  - Right-click opens context menu: `Show Keyboard`, `Compact Mode`, `Minimal Icon`, `Hide Keyboard`, `Bangla / English`, `Emoji`, `Clipboard History`, `Settings`, `Start with Windows`, `Exit`.
- **Auto-Startup & Single Instance:**
  - Named global Mutex prevents duplicate processes and brings the existing instance forward.
  - Optional `Start with Windows` registration (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` or Startup shortcut) with zero splash screens.
- **Mandatory State Restoration:**
  - Saves and restores `WindowMode`, `Width`, `Height`, `Left (X)`, `Top (Y)`, `MonitorDeviceId`, `ActiveLanguage`, `ActiveLayout`, `Theme`, `Opacity`, and `AlwaysOnTop`.
  - Gracefully recalculates valid coordinates if a secondary monitor is disconnected or resolution changes.

### 3.9 Dedicated Settings Window
Organized into 8 clear navigation tabs:
1. **General:** Start with Windows, launch mode, default language (` বাংলা ` / `ENG`), default layout.
2. **Keyboard:** Default/minimum dimensions, window mode, Always on Top, scaling factor, key spacing, key repeat delay/rate.
3. **Appearance:** Dark / Light / System Default theme, custom key/accent colors, window opacity, key corner radius.
4. **Prediction:** Toggle word completion, toggle next-word prediction, suggestion count (3–5), user dictionary management.
5. **Emoji:** Frequently used limit, clear emoji history.
6. **Clipboard:** Enable/disable monitoring, history item limit, retention period (24h / 7d / 30d / Forever), clear clipboard history.
7. **Shortcuts:** Global hotkeys for Toggle Language, Show/Hide Keyboard, Open Emoji Panel, Open Clipboard History.
8. **Privacy:** Local storage path inspection, Clear All Application Data, Reset Settings to Defaults.

---

## 4. Non-Functional Requirements & Engineering Targets

| Category | Metric | Target Specification |
|---|---|---|
| **CPU Usage** | Idle background / tray state | `< 1%` CPU usage (zero polling timers) |
| **Memory Footprint** | Idle / active working set | `50 MB – 100 MB` RAM (lazy dictionary & emoji loading) |
| **Input Latency** | Key click / press to character insertion | `< 10 ms` (imperceptible to user) |
| **Prediction Speed** | Prefix completion & n-gram lookup | `≤ 50 ms` asynchronous query off the UI thread |
| **Startup Time** | Cold launch to ready state | `< 2.0 seconds` on standard Windows 10/11 hardware |
| **Network Usage** | Core typing, prediction, emoji, clipboard | `0 bytes` (strictly offline, no external API calls) |
| **Privilege Level** | Standard runtime execution | Runs as Standard User (`asInvoker`), stores data in `%LOCALAPPDATA%\BengaliKeyboard\` |

### Application Compatibility Targets
Must be verified against:
- Windows Notepad & WordPad
- Microsoft Word / Office Suite
- Google Chrome, Microsoft Edge, Mozilla Firefox
- Visual Studio Code & Windows Terminal
- Windows 10/11 Search Bar, File Explorer rename fields, and Messaging apps (WhatsApp, Telegram)

---

## 5. Definition of Done (Acceptance Criteria)

The product is complete only when all 10 criteria are verified:
1. **System-Wide Typing:** Both physical keyboard and on-screen mouse/touch clicks insert accurate Bengali Unicode characters into external Windows applications without losing target window focus.
2. **Authentic Layout:** The on-screen keyboard matches the Ridmik reference screenshots across Bangla Primary, Bangla Shift, Numbers, Symbols, and English QWERTY layers.
3. **Smart Offline Prediction:** Typing partial Bengali words shows 3–5 completions in ≤ 50 ms, and completing a word shows context-aware next-word suggestions using the offline n-gram model.
4. **Emoji System:** Keyword search, 10 categories, and SQLite-backed Frequently Used emoji ranking persist across restarts.
5. **Clipboard Manager:** Captures copied text via OS notifications, supports search/pin/delete/paste, and enforces privacy exclusions.
6. **4-Mode Floating Window:** Smoothly transitions between Expanded, Compact, Minimal Floating Icon, and System Tray Hidden modes with proportional UI scaling.
7. **State Persistence:** Restores exact size, screen coordinates, monitor placement, mode, and language after app or Windows restart.
8. **System Tray & Auto-Start:** Runs reliably from the Windows system tray and supports automatic launch at Windows login.
9. **Performance & Privacy:** Meets CPU/RAM targets, never blocks the UI dispatcher thread, and logs zero sensitive text.
10. **Automated Tests & Installer:** Unit/integration tests pass (`xUnit`) and the application packages cleanly into a distributable Windows installer.
