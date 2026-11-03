# Ridmik-Style Bengali Virtual Keyboard for Windows
## Complete Software Requirements Specification (SRS), Product Design & Technical Implementation Documentation

**Project Type:** Native Windows Desktop Application  
**Project Name:** Bengali Virtual Keyboard (Working Name)  
**Platform:** Windows 10 / Windows 11  
**Primary Language:** C#  
**UI Framework:** WPF  
**Target Framework:** .NET 10  
**Architecture:** Modular, Lightweight, Offline-First  
**Primary Input System:** Bengali Unicode Input Engine  
**Primary Layout Reference:** User-provided Ridmik Keyboard screenshots  
**Document Version:** 1.0  
**Status:** Development Specification

---

# 1. Project Overview

## 1.1 Product Vision

Build a lightweight, professional-looking Bengali virtual keyboard application for Windows PCs that delivers an Android-style Bengali typing experience similar to the Ridmik Keyboard application available on the Google Play Store.

The primary objective is to bring the familiar mobile Bengali keyboard experience to Windows desktop users.

The application must allow users to:

- Type Bengali directly using a virtual on-screen keyboard.
- Type Bengali using their physical Windows keyboard.
- Switch between Bengali and English instantly.
- Use a familiar Ridmik-style Bengali keyboard layout.
- Access emojis, emoji search, and frequently used emojis.
- Access clipboard history.
- Use a floating, draggable, resizable keyboard.
- Minimize the keyboard to the Windows system tray.
- Restore the keyboard from the system tray.
- Customize keyboard size, appearance, and position.
- Automatically restore the previous keyboard configuration after restarting Windows.
- Receive intelligent Bengali word completion and next-word suggestions.
- Use all essential typing features without an internet connection.

This must be a real Windows input application, not simply a keyboard UI demonstration.

## 1.2 Core Product Philosophy

The application must follow these principles:

1. Lightweight: Minimal CPU and RAM consumption.
2. Fast: Immediate keyboard response and low input latency.
3. Offline-first: Core typing and prediction must work without internet.
4. Persistent: Remember the user's last configuration.
5. Professional: Modern Windows 11-inspired interface.
6. Flexible: Users can customize keyboard visibility and dimensions.
7. Accurate: Correct Bengali Unicode output.
8. System-wide: Work inside other Windows applications.
9. Privacy-first: Keep clipboard history and typing data locally.
10. Modular: Features must be independently maintainable.

---

# 2. Technology Stack

## 2.1 Recommended Technologies

| Component | Technology |
|---|---|
| Programming Language | C# |
| Runtime | .NET 10 |
| Desktop UI | WPF |
| UI Styling | Custom WPF Styles / Control Templates |
| MVVM Framework | CommunityToolkit.Mvvm |
| Input Integration | Windows Text Services Framework (TSF) |
| Windows Integration | Windows API / PInvoke |
| Local Database | SQLite |
| ORM / Data Access | Microsoft.Data.Sqlite |
| Configuration | JSON |
| Logging | Serilog |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| System Tray | WPF-compatible NotifyIcon implementation |
| Testing | xUnit |
| Installer | Inno Setup or MSIX |
| Version Control | Git / GitHub |

### Important architecture decision

Use WPF for the visual keyboard, settings, emoji panel, clipboard panel, and floating window.

For reliable system-wide Bengali input, implement a Windows Text Services Framework (TSF) text service.

A native C++ COM component may be necessary for the TSF integration. Keep the main application and all business logic in C#.

Do not assume that a WPF keyboard window alone can function as a complete Windows-wide input method.

For an early prototype, Unicode SendInput can be used as an experimental fallback. However, production implementation should prioritize TSF for proper composition, candidate selection, application compatibility, and text insertion.

---

# 3. Main Application Modules

The application consists of these independent modules:

1. Bengali Input Engine
2. Ridmik-Style Keyboard Layout
3. Physical Keyboard Integration
4. Smart Bengali Predictive Text
5. Next Word Prediction
6. Emoji Keyboard & Emoji Search
7. Frequently Used Emoji History
8. Clipboard History Manager
9. Floating Keyboard Window
10. Adaptive Keyboard Size System
11. System Tray Integration
12. Startup & State Restoration
13. Appearance & Customization
14. Settings & Configuration
15. Local Storage & Privacy Management

All modules should communicate through well-defined interfaces.

---

# 4. Bengali Keyboard Layout System

## 4.1 Primary Requirement

The primary keyboard layout must follow the Bengali keyboard design shown in the user's supplied Ridmik Keyboard screenshots.

The objective is to reproduce the familiar mobile typing experience on Windows.

**Important:** The supplied screenshots are the visual source of truth. Do not invent a different keyboard layout or automatically assume a different layout standard.

Before implementing the layout, inspect the screenshots and document:

- Every key position.
- Bengali character assigned to each key.
- Number row.
- Punctuation keys.
- Shift and alternate characters.
- Backspace.
- Enter.
- Spacebar.
- Language switch.
- Symbol switch.
- Emoji access.
- Additional character panels.

Preserve the character arrangement and key positions as closely as possible.

## 4.2 Keyboard Layout Requirements

The keyboard must contain:

- Bengali character rows.
- Dedicated number row.
- Common Bengali punctuation.
- English character mode.
- Shift / alternate character mode.
- Symbol layout.
- Emoji access.
- Backspace.
- Enter.
- Spacebar.
- Language switch.
- Layout switch.

Create a configurable layout definition rather than hardcoding every character into the UI.

Suggested structure:

```text
KeyboardLayouts/
├── Bangla/
│   ├── PrimaryLayout.json
│   ├── ShiftLayout.json
│   ├── NumberLayout.json
│   └── SymbolLayout.json
│
├── English/
│   ├── QwertyLayout.json
│   └── SymbolLayout.json
│
└── LayoutManager.cs
```

Each key definition should support:

- Display label.
- Unicode output.
- Alternate output.
- Key type.
- Width ratio.
- Row position.
- Tooltip.
- Optional icon.
- Shift behavior.

## 4.3 Visual Design

Use the supplied Ridmik screenshots as the primary visual reference.

Design requirements:

- Dark keyboard theme.
- Rounded rectangular keys.
- Consistent spacing.
- Clear Bengali glyph rendering.
- Professional typography.
- Subtle shadows.
- Responsive key dimensions.
- Smooth but lightweight interactions.
- Clear distinction between special and regular keys.

Use a Bengali-compatible font such as Noto Sans Bengali or another appropriately licensed Bengali font.

Ensure that the actual glyphs render correctly at all keyboard sizes.

## 4.4 Keyboard Interaction

When the user clicks an on-screen key:

1. Identify the selected key.
2. Retrieve its Unicode output.
3. Send the character through the active input integration.
4. Insert it into the currently focused application's text field.
5. Update word completion and prediction.

The application must not require users to manually copy and paste Bengali text.

---

# 5. Bengali Input Engine

## 5.1 Core Requirement

Users must be able to type Bengali directly.

This is NOT an English-to-Bengali transliteration application.

The user should not have to type Romanized Bengali such as:

```text
ami banglay likhi
```

Instead, the user should be able to select Bengali mode and type using the Bengali characters assigned to the keyboard layout.

## 5.2 Unicode Support

The engine must support:

- Bengali vowels.
- Bengali consonants.
- Bengali vowel signs.
- Bengali conjunct characters.
- Bengali diacritics.
- Bengali numerals.
- Bengali punctuation.
- Unicode combining marks.
- Proper text ordering.

Examples of expected output:

```text
আমি বাংলা লিখতে পারি।
আমার একটি কম্পিউটার আছে।
বাংলা ভাষা সুন্দর।
```

The implementation must correctly handle Bengali Unicode sequences, including consonant clusters and the virama character (্).

Use appropriate Unicode normalization and test real Bengali text rather than assuming every visible character is a separate Unicode code point.

## 5.3 Input Integration

Preferred production implementation:

- Windows Text Services Framework.
- Correct text composition.
- Unicode insertion.
- Focus-aware input.
- Candidate selection support.
- Proper backspace behavior.
- Correct interaction with text editors.

The physical keyboard and on-screen keyboard must share the same input engine.

---

# 6. English and Bengali Mode Switching

Provide a dedicated language toggle.

Example:

```text
[ বাংলা ]  [ ENG ]
```

Expected behavior:

- One click switches between Bengali and English.
- Physical keyboard input follows the selected language.
- On-screen keyboard keys update immediately.
- Current mode is visually indicated.
- The selected language persists according to user settings.

Provide an optional global keyboard shortcut for switching languages.

The shortcut must be configurable to avoid conflicts with existing Windows shortcuts.

---

# 7. Smart Bengali Predictive Text Engine

## 7.1 Product Requirement

The application must provide a mobile-keyboard-style suggestion bar similar to Ridmik Keyboard.

Users should not need to type every character of a word manually.

The application must provide intelligent suggestions while the user types.

There are two separate capabilities:

1. Word Completion.
2. Next Word Prediction.

Both must work offline.

## 7.2 Real-Time Word Completion

Example:

The user begins typing:

```text
আমা
```

The suggestion bar could display:

```text
[আমার] [আমাকে] [আমাদের] [আমায়]
```

The user clicks or selects a suggestion.

The engine inserts the complete word into the focused text field.

Requirements:

- Detect the currently typed word.
- Search the local Bengali dictionary.
- Rank possible completions.
- Display approximately 3–5 suggestions.
- Update suggestions while typing.
- Replace the incomplete word correctly.
- Preserve surrounding text.
- Handle punctuation and spaces.
- Support backspace and cursor editing.

## 7.3 Next Word Prediction

The application must also predict what the user might type next based on previously completed words.

Example:

User types:

```text
আমি
```

Possible suggestions:

```text
[বাংলা] [একটি] [আজ] [তোমাকে] [ভালো]
```

Another example:

```text
আমি বাংলা
```

Possible suggestions:

```text
[লিখতে] [ভাষা] [শিখছি] [পড়তে]
```

These are illustrative examples, not fixed outputs.

The prediction engine should use:

- Bengali language dictionary.
- Word frequency data.
- Bigram language model.
- Trigram language model.
- Context-based ranking.
- Sentence context.
- Punctuation awareness.

A lightweight n-gram model is recommended for the initial implementation.

## 7.4 Suggestion Bar UI

Place the suggestion bar directly above the keyboard keys.

Example:

```text
┌────────────────────────────────────────────┐
│  আমার      আমাকে      আমাদের      আরও      │
├────────────────────────────────────────────┤
│  Bengali Keyboard Layout                   │
│                                            │
│  [ Key ] [ Key ] [ Key ] [ Key ]           │
│  [ Key ] [ Key ] [ Key ] [ Key ]           │
└────────────────────────────────────────────┘
```

Requirements:

- Display 3–5 suggestions.
- Support mouse clicks.
- Support physical keyboard navigation where practical.
- Update without noticeable delay.
- Visually highlight the selected suggestion.
- Correctly replace the current partial word.
- Support word completion and next-word insertion.

## 7.5 Dictionary Architecture

Store dictionary and language-model data locally.

Suggested structure:

```text
Data/
├── Dictionary/
│   ├── bn_words.db
│   ├── word_frequency.db
│   └── common_phrases.db
│
└── LanguageModel/
    ├── bigrams.db
    └── trigrams.db
```

Use SQLite or a compact indexed dictionary structure.

Avoid loading a massive dictionary into memory unnecessarily.

## 7.6 Performance Requirements

- Suggestions should update with minimal latency.
- Target approximately 50 ms for local candidate lookup under normal conditions.
- Do not block the keyboard UI thread.
- Use asynchronous processing where appropriate.
- Cache frequently accessed dictionary results.
- Keep prediction offline.

Do not integrate a cloud AI API for ordinary word completion.

---

# 8. Emoji Keyboard & Emoji Search

## 8.1 Emoji Panel

Implement a dedicated emoji panel inspired by the supplied screenshots.

The panel should be accessible through an emoji button.

Provide:

- Emoji categories.
- Emoji search.
- Frequently used emojis.
- Recently used emojis.
- Emoji insertion.
- Scrollable emoji grid.
- Search clearing.
- Keyboard navigation.

## 8.2 Emoji Search

Users must be able to search for emojis by keywords.

Examples:

```text
Search: smile
Results: 😀 😃 😄 😁 😊

Search: heart
Results: ❤️ 💙 💚 💛 💜

Search: food
Results: 🍎 🍕 🍔 🍟
```

Use a local emoji metadata database.

Each emoji should contain:

- Unicode sequence.
- Name.
- Search keywords.
- Category.
- Optional aliases.

Support multi-code-point emoji sequences, including skin-tone variations and joined emoji.

## 8.3 Emoji Categories

Recommended categories:

- Frequently Used.
- Smileys & Emotions.
- People & Body.
- Animals & Nature.
- Food & Drink.
- Travel & Places.
- Activities.
- Objects.
- Symbols.
- Flags.

## 8.4 Frequently Used Emoji History

This is a mandatory feature.

When a user selects an emoji:

1. Record its usage.
2. Update its usage count.
3. Update its last-used timestamp.
4. Add it to the frequently used list.
5. Sort by frequency and recency.

Example:

```text
Frequently Used

❤️  😂  😊  🥰  😍
👍  🙏  🔥  💯  😎
```

Requirements:

- Store history locally.
- Preserve history after application restart.
- Prioritize frequently selected emojis.
- Avoid duplicate entries.
- Allow users to clear emoji history.
- Provide a configurable history limit.

Suggested database table:

```sql
CREATE TABLE EmojiUsage (
    Emoji TEXT PRIMARY KEY,
    UsageCount INTEGER NOT NULL,
    LastUsedUtc TEXT NOT NULL
);
```

---

# 9. Clipboard History Manager

## 9.1 Overview

Implement a built-in clipboard history manager.

Users should be able to access previously copied text without repeatedly copying the same content.

The feature should work like a modern desktop clipboard manager.

## 9.2 Core Features

- Capture copied text.
- Maintain clipboard history.
- Display previous clipboard items.
- Click an item to copy it again.
- Paste a selected item into the active application.
- Search clipboard history.
- Delete individual items.
- Clear all history.
- Pin important items.
- Persist history locally.
- Configurable history capacity.

## 9.3 Clipboard UI

Example:

```text
┌──────────────────────────────────┐
│ Clipboard History                │
│                                  │
│ Search clipboard...              │
│                                  │
│ 📌 Important text                │
│                                  │
│ Previously copied text           │
│                                  │
│ Bengali text                     │
│                                  │
│ [Clear History]                  │
└──────────────────────────────────┘
```

## 9.4 Technical Implementation

Use appropriate Windows clipboard APIs and clipboard change notifications.

Avoid continuous clipboard polling.

Recommended architecture:

- Clipboard listener service.
- Background processing.
- Local SQLite storage.
- Clipboard item manager.
- Search service.
- Pin and deletion functionality.

## 9.5 Privacy Requirements

Clipboard data can contain passwords, personal information, and confidential content.

Therefore:

- Keep clipboard history local.
- Never upload clipboard contents.
- Provide an option to disable clipboard monitoring.
- Provide a history retention setting.
- Provide a clear-all function.
- Avoid collecting clipboard content from secure desktop or protected password-entry contexts where detectable.
- Do not capture sensitive information intentionally.
- Do not log clipboard contents.

---

# 10. Floating Keyboard System

## 10.1 Core Requirement

The keyboard must support a floating window.

Users should be able to place the keyboard anywhere on their Windows desktop.

The floating window must remain independent of the active application's main window.

## 10.2 Floating Window Features

- Drag anywhere using the title/header area.
- Move freely around the desktop.
- Resize using mouse dragging.
- Always-on-top option.
- Minimize to system tray.
- Restore from tray.
- Remember the previous position.
- Remember the previous size.
- Support multiple monitor environments.
- Avoid accidentally moving outside the usable desktop area.

## 10.3 Window Modes

The application must support four primary presentation modes.

### Mode A: Expanded Floating Keyboard

Normal full keyboard with all keys, suggestion bar, and keyboard controls.

### Mode B: Compact Floating Keyboard

A smaller keyboard with reduced dimensions.

Maintain usable key spacing and readable characters.

### Mode C: Minimal Icon Mode

The keyboard can shrink into a single compact floating icon.

Example:

```text
       ┌─────┐
       │ বাংলা │
       └─────┘
```

This icon can be dragged anywhere.

Clicking it restores the keyboard to its previous expanded or compact dimensions.

### Mode D: Hidden / System Tray Mode

The keyboard disappears entirely from the desktop.

The application continues running in the background and remains accessible through its system tray icon.

## 10.4 Mode Switching

Users must be able to switch between modes whenever they want.

Suggested controls:

```text
[ Expand ]
[ Compact ]
[ Minimize to Icon ]
[ Hide to Tray ]
[ Always on Top ]
```

All visibility operations must be reversible.

---

# 11. Adaptive Keyboard Size & Layout Persistence

## 11.1 Flexible Resizing

Users must have complete control over keyboard dimensions.

The keyboard should support:

- Very small dimensions.
- Compact dimensions.
- Normal dimensions.
- Large dimensions.
- Custom dimensions.

Use minimum and maximum dimensions to prevent the keyboard from becoming unusable.

## 11.2 Independent Layout Scaling

Resizing must scale the complete keyboard UI proportionally.

The following elements must adapt:

- Keys.
- Bengali characters.
- English characters.
- Icons.
- Suggestion bar.
- Number row.
- Special keys.
- Emoji controls.
- Spacing.
- Rounded corners.

Avoid stretching text or distorting key proportions.

Use responsive WPF layouts and appropriate scaling techniques.

## 11.3 Persistent State Restoration

This is a mandatory core requirement.

The application must remember the user's last configuration.

For example:

Initial configuration:

```text
Mode: Floating
Width: 850 px
Height: 310 px
X: 400 px
Y: 650 px
Theme: Dark
Language: Bengali
Layout: User-selected layout
Always On Top: Enabled
```

After restarting Windows, restore the saved configuration.

If the user last used minimal icon mode, restore the minimal icon mode.

If the user last used compact mode, restore compact mode.

If the user hid the keyboard to the tray, do not unexpectedly display the full keyboard at startup.

The application must remember:

- Window mode.
- Width.
- Height.
- X position.
- Y position.
- Monitor information.
- Keyboard layout.
- Language mode.
- Theme.
- Scaling preference.
- Always-on-top setting.
- Last active panel.
- Relevant visibility preferences.

### Important restoration behavior

If the previous monitor is disconnected, restore the keyboard on an available monitor.

If the saved coordinates are outside the current display area, calculate a valid position.

Handle Windows DPI scaling correctly.

Persist configuration after meaningful changes, not just when Windows shuts down.

---

# 12. Windows System Tray Integration

## 12.1 Requirement

The application must have a persistent notification-area icon.

The application should continue running when its keyboard window is hidden.

It must not terminate when the user hides or minimizes the keyboard.

## 12.2 Tray Context Menu

Right-clicking the tray icon should display:

```text
Bengali Keyboard

Show Keyboard
Compact Mode
Minimal Icon
Hide Keyboard
Bangla / English
Emoji
Clipboard History
Settings
Start with Windows
Exit
```

## 12.3 Tray Interactions

- Left-click: Show or restore the keyboard.
- Right-click: Open context menu.
- Double-click: Restore the last visible keyboard state.
- Exit: Fully close the application.

When hidden, the application should remain lightweight.

---

# 13. Windows Startup Integration

## 13.1 Auto Startup

The user requires the keyboard to launch automatically whenever Windows starts.

Implement a configurable Start with Windows option.

Use an appropriate Windows startup registration mechanism, such as the current user's Startup folder or a supported startup registration mechanism.

## 13.2 Startup Behavior

When Windows starts:

1. Launch the background application.
2. Load saved configuration.
3. Initialize the input integration.
4. Initialize the system tray icon.
5. Restore the saved presentation mode.
6. Restore the last keyboard size and position when visible.
7. Keep the application ready for Bengali typing.

Startup must not display unnecessary splash screens or multiple windows.

## 13.3 Startup Performance

- Avoid unnecessary background services.
- Do not initialize emoji search until required.
- Load large dictionary data lazily.
- Avoid blocking the Windows startup process.
- Ensure only one application instance runs.

---

# 14. Professional UI/UX Design System

## 14.1 Design Direction

Create a professional Windows 11-inspired interface with the familiar visual identity of the supplied Ridmik keyboard screenshots.

The visual design should not look like an outdated desktop utility.

## 14.2 Design Characteristics

- Dark theme by default.
- Rounded rectangular keys.
- Subtle shadows.
- Balanced spacing.
- Smooth transitions.
- Clear iconography.
- Bengali-compatible typography.
- Consistent visual hierarchy.
- Minimal window chrome.
- Lightweight rendering.

## 14.3 Keyboard Theme

Suggested starting palette:

| Element | Suggested Color |
|---|---|
| Main Background | #17191F |
| Key Background | #30343D |
| Special Key | #414653 |
| Primary Text | #FFFFFF |
| Secondary Text | #AEB5C2 |
| Accent | #3B82F6 |
| Suggestion Background | #20242B |

These are initial design tokens and can be adjusted after comparing the screenshots.

## 14.4 Theme Options

Initial release:

- Dark.
- Light.
- System Default.

Additional customization may include:

- Key background color.
- Text color.
- Accent color.
- Keyboard transparency.
- Key corner radius.
- Keyboard opacity.

---

# 15. Settings Panel

Create a dedicated settings window.

Recommended sections:

### General
- Start with Windows.
- Launch minimized.
- Single-instance behavior.
- Default language.
- Default layout.

### Keyboard
- Default keyboard size.
- Minimum size.
- Default window mode.
- Always on top.
- Keyboard scaling.
- Key spacing.

### Appearance
- Dark theme.
- Light theme.
- System theme.
- Custom colors.
- Opacity.

### Prediction
- Enable word completion.
- Enable next-word prediction.
- Number of suggestions.
- Dictionary selection.
- Offline prediction settings.

### Emoji
- Emoji history limit.
- Frequently used emojis.
- Clear emoji history.

### Clipboard
- Enable clipboard history.
- History limit.
- Retention period.
- Clear clipboard history.

### Shortcuts
- Toggle language.
- Show/hide keyboard.
- Open emoji panel.
- Open clipboard history.

### Privacy
- Local-only storage.
- Clear application data.
- Disable clipboard monitoring.
- Reset settings.

---

# 16. Application Architecture

## 16.1 Recommended Architecture

Use a modular architecture based on separation of concerns.

```text
BengaliKeyboard/
│
├── src/
│   │
│   ├── BengaliKeyboard.App/
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml
│   │   ├── Bootstrapper.cs
│   │   └── Program.cs
│   │
│   ├── BengaliKeyboard.UI/
│   │   ├── Windows/
│   │   │   ├── KeyboardWindow.xaml
│   │   │   ├── FloatingIconWindow.xaml
│   │   │   ├── EmojiWindow.xaml
│   │   │   ├── ClipboardWindow.xaml
│   │   │   └── SettingsWindow.xaml
│   │   │
│   │   ├── Controls/
│   │   │   ├── KeyboardKey.xaml
│   │   │   ├── SuggestionBar.xaml
│   │   │   └── LanguageToggle.xaml
│   │   │
│   │   ├── Themes/
│   │   └── ViewModels/
│   │
│   ├── BengaliKeyboard.Core/
│   │   ├── Interfaces/
│   │   ├── Models/
│   │   ├── Services/
│   │   └── Events/
│   │
│   ├── BengaliKeyboard.Input/
│   │   ├── BengaliInputEngine.cs
│   │   ├── UnicodeProcessor.cs
│   │   ├── PhysicalKeyboardService.cs
│   │   ├── OnScreenInputService.cs
│   │   └── TSF/
│   │
│   ├── BengaliKeyboard.Layouts/
│   │   ├── LayoutManager.cs
│   │   ├── LayoutLoader.cs
│   │   └── Definitions/
│   │
│   ├── BengaliKeyboard.Prediction/
│   │   ├── WordCompletionService.cs
│   │   ├── NextWordPredictionService.cs
│   │   ├── DictionaryService.cs
│   │   ├── NGramModel.cs
│   │   └── SuggestionRankingService.cs
│   │
│   ├── BengaliKeyboard.Emoji/
│   │   ├── EmojiSearchService.cs
│   │   ├── EmojiUsageService.cs
│   │   └── EmojiRepository.cs
│   │
│   ├── BengaliKeyboard.Clipboard/
│   │   ├── ClipboardMonitor.cs
│   │   ├── ClipboardHistoryService.cs
│   │   └── ClipboardRepository.cs
│   │
│   ├── BengaliKeyboard.Windows/
│   │   ├── SystemTrayService.cs
│   │   ├── StartupService.cs
│   │   ├── WindowPositionService.cs
│   │   ├── GlobalHotkeyService.cs
│   │   └── MonitorService.cs
│   │
│   ├── BengaliKeyboard.Storage/
│   │   ├── Database/
│   │   ├── Configuration/
│   │   └── Repositories/
│   │
│   └── BengaliKeyboard.Tests/
│
├── native/
│   └── BengaliKeyboard.TSF/
│       └── Native Windows TSF integration
│
├── assets/
│   ├── Icons/
│   ├── Fonts/
│   └── Emoji/
│
├── data/
│   ├── Dictionary/
│   ├── LanguageModels/
│   └── Emoji/
│
├── installer/
│
├── docs/
│
└── README.md
```

The native TSF component should be packaged and registered through an appropriate Windows installation process.

Do not create a separate Windows service unless a demonstrated technical requirement makes one necessary.

## 16.2 MVVM Pattern

Use MVVM consistently.

Responsibilities:

**Models**
- Keyboard key.
- Keyboard layout.
- Emoji.
- Clipboard item.
- Suggestion.
- Application configuration.

**ViewModels**
- KeyboardViewModel.
- EmojiViewModel.
- ClipboardViewModel.
- SettingsViewModel.

**Services**
- Input service.
- Prediction service.
- Storage service.
- Window management service.
- Tray service.

Keep business logic out of XAML code-behind wherever possible.

---

# 17. Local Data Storage

Use SQLite for structured local data.

Suggested database tables:

```sql
CREATE TABLE EmojiUsage (
    Emoji TEXT PRIMARY KEY,
    UsageCount INTEGER NOT NULL,
    LastUsedUtc TEXT NOT NULL
);

CREATE TABLE ClipboardHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Content TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    IsPinned INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE ApplicationSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
```

Additional tables may include:

- BengaliDictionary.
- WordFrequency.
- BigramFrequency.
- TrigramFrequency.

Keep user configuration separate from dictionary resources.

Suggested application data directory:

```text
%LOCALAPPDATA%\BengaliKeyboard\
```

Suggested structure:

```text
BengaliKeyboard/
├── Config/
│   └── settings.json
│
├── Database/
│   └── keyboard.db
│
├── Logs/
│
└── Cache/
```

The application must not require administrator privileges for normal usage.

---

# 18. Performance & Lightweight Requirements

The application must be designed as a lightweight desktop utility.

Suggested initial engineering targets:

| Metric | Target |
|---|---|
| Idle CPU | Ideally below 1% |
| Typing response | No perceptible delay |
| Local prediction | Target around 50 ms |
| Startup | Target under 2 seconds on a typical modern PC |
| Idle RAM | Target 50–100 MB where practical |
| Network dependency | None for core features |
| UI responsiveness | No blocking during typing |
| Background operation | Minimal resource consumption |

These are engineering targets, not guaranteed figures. Measure actual usage through profiling.

Optimization requirements:

- Use lazy loading.
- Avoid unnecessary background timers.
- Avoid continuous clipboard polling.
- Use event-driven Windows notifications.
- Keep UI operations on the Dispatcher thread.
- Run dictionary searches asynchronously.
- Cache frequently used data.
- Dispose native resources correctly.
- Avoid excessive animations.
- Prevent unnecessary database queries.

---

# 19. Security & Privacy

The application should operate locally by default.

Requirements:

- No cloud upload of typed text.
- No cloud upload of clipboard history.
- No unnecessary telemetry.
- No keystroke logging.
- No storage of passwords.
- Local database protection where appropriate.
- Secure file permissions.
- Safe application update mechanism.
- No unnecessary administrator permissions.

Never implement a general-purpose global keylogger.

Physical keyboard integration must only process the input required for the active Bengali input method.

The input method should respect Windows secure desktop and protected application boundaries.

---

# 20. Error Handling & Reliability

Handle the following situations:

- Windows restarts unexpectedly.
- Previous monitor is disconnected.
- Saved position is invalid.
- SQLite database is temporarily unavailable.
- Dictionary files are missing.
- Clipboard access is denied.
- Another application has input focus.
- Keyboard window closes unexpectedly.
- TSF registration fails.
- User changes display resolution.
- DPI scaling changes.
- Multiple application instances launch.

The application must recover gracefully without losing configuration.

Use structured logs, but never log sensitive typed text or clipboard contents.

---

# 21. Testing Strategy

## 21.1 Bengali Typing Tests

Test:

- Basic Bengali characters.
- Vowel signs.
- Conjunct letters.
- Unicode normalization.
- Backspace.
- Space.
- Enter.
- Punctuation.
- Long Bengali sentences.

## 21.2 Application Compatibility Tests

Test input in:

- Windows Notepad.
- Microsoft Word.
- Google Chrome.
- Microsoft Edge.
- Firefox.
- Visual Studio Code.
- Windows search fields.
- Common desktop applications.

Also test elevated and non-elevated application boundaries and document any Windows security restrictions.

## 21.3 Floating Keyboard Tests

Test:

- Dragging.
- Resizing.
- Compact mode.
- Minimal icon mode.
- Hide to tray.
- Restore from tray.
- Always on top.
- Multi-monitor positioning.
- Display resolution changes.
- DPI scaling.

## 21.4 Persistence Tests

1. Set a custom keyboard size.
2. Move the keyboard to another position.
3. Select minimal icon mode.
4. Restart the application.
5. Verify the state is restored.
6. Restart Windows.
7. Verify the state is still restored.

## 21.5 Emoji Tests

- Emoji search.
- Category selection.
- Emoji insertion.
- Frequently used history.
- Restart persistence.
- Unicode variation sequences.

## 21.6 Clipboard Tests

- Copy multiple text items.
- Verify history.
- Search.
- Pin.
- Delete.
- Paste.
- Restart persistence.
- Disable clipboard monitoring.

## 21.7 Prediction Tests

- Partial Bengali word.
- Multiple word completions.
- Next-word suggestions.
- Context-sensitive ranking.
- Punctuation handling.
- Backspace handling.
- Suggestion selection.
- Offline operation.

---

# 22. Development Roadmap

Implement the project in the following phases.

## Phase 1 — Project Foundation

- Create .NET 10 WPF solution.
- Configure MVVM.
- Establish modular project structure.
- Build application bootstrapper.
- Configure dependency injection.
- Implement single-instance behavior.
- Create the initial keyboard window.

**Deliverable:** Working desktop application foundation.

## Phase 2 — Keyboard UI & Layout

- Analyze supplied Ridmik screenshots.
- Extract exact character positions.
- Build reusable keyboard key controls.
- Implement Bengali layout.
- Implement number row.
- Implement symbol layout.
- Implement English layout.
- Add language switch.
- Implement dark theme.

**Deliverable:** Functional visual keyboard.

## Phase 3 — Bengali Input Integration

- Implement the Bengali input engine.
- Integrate physical keyboard.
- Implement on-screen key input.
- Develop TSF integration.
- Implement Unicode insertion.
- Handle cursor and backspace behavior.
- Test across Windows applications.

**Deliverable:** System-wide Bengali typing.

## Phase 4 — Floating Keyboard & Window Management

- Floating window.
- Dragging.
- Resizing.
- Compact mode.
- Minimal icon mode.
- Hide to tray.
- Always on top.
- Position persistence.
- Size persistence.
- Multi-monitor restoration.

**Deliverable:** Complete adaptive floating keyboard.

## Phase 5 — System Tray & Startup

- System tray icon.
- Context menu.
- Show/hide commands.
- Startup registration.
- Saved-state restoration.
- Single-instance startup.

**Deliverable:** Persistent Windows background application.

## Phase 6 — Emoji System

- Emoji database.
- Emoji grid.
- Search.
- Categories.
- Frequently used emojis.
- Local usage history.
- Emoji insertion.

**Deliverable:** Complete emoji keyboard.

## Phase 7 — Clipboard History

- Clipboard listener.
- History manager.
- Search.
- Pin.
- Delete.
- Paste.
- Local persistence.
- Privacy controls.

**Deliverable:** Integrated clipboard manager.

## Phase 8 — Smart Bengali Prediction

- Bengali dictionary.
- Word completion.
- Word frequency ranking.
- Bigram model.
- Trigram model.
- Next-word prediction.
- Suggestion bar.
- Suggestion insertion.
- Performance optimization.

**Deliverable:** Offline smart Bengali predictive keyboard.

## Phase 9 — Customization & Polish

- Settings panel.
- Theme options.
- Keyboard customization.
- Shortcut customization.
- Error handling.
- Performance profiling.
- UI polish.

**Deliverable:** Production-ready application candidate.

## Phase 10 — Testing & Release

- Full functional testing.
- Compatibility testing.
- Security review.
- Installer development.
- Startup verification.
- Release packaging.
- Documentation.
- GitHub release.

**Deliverable:** Installable Windows application.

---

# 23. Functional Requirements Checklist

The following checklist represents the mandatory product requirements.

## Bengali Input

- [ ] Direct Bengali typing.
- [ ] Physical keyboard support.
- [ ] On-screen keyboard support.
- [ ] Unicode Bengali output.
- [ ] Correct conjunct handling.
- [ ] English/Bengali switching.
- [ ] System-wide text insertion.

## Keyboard Layout

- [ ] Ridmik-style layout.
- [ ] Screenshot-based character positioning.
- [ ] Number row.
- [ ] Symbol layout.
- [ ] Alternate characters.
- [ ] Dark theme.
- [ ] Rounded keys.
- [ ] Bengali-compatible font.

## Prediction

- [ ] Real-time word completion.
- [ ] 3–5 suggestions.
- [ ] Next-word prediction.
- [ ] Context-based suggestions.
- [ ] Offline dictionary.
- [ ] Bigram/trigram prediction.
- [ ] Click-to-insert suggestions.

## Emoji

- [ ] Emoji keyboard.
- [ ] Emoji search.
- [ ] Emoji categories.
- [ ] Frequently used emojis.
- [ ] Persistent emoji history.
- [ ] Emoji insertion.

## Clipboard

- [ ] Clipboard monitoring.
- [ ] Clipboard history.
- [ ] Search.
- [ ] Pin.
- [ ] Delete.
- [ ] Paste.
- [ ] Local persistence.
- [ ] Privacy settings.

## Floating Keyboard

- [ ] Floating window.
- [ ] Drag anywhere.
- [ ] Resize freely.
- [ ] Compact mode.
- [ ] Minimal single-icon mode.
- [ ] Hide to system tray.
- [ ] Always on top.
- [ ] Restore previous dimensions.
- [ ] Restore previous position.
- [ ] Multi-monitor support.

## Windows Integration

- [ ] Startup with Windows.
- [ ] System tray icon.
- [ ] Tray context menu.
- [ ] Show/hide functionality.
- [ ] Background operation.
- [ ] Single-instance application.
- [ ] Startup state restoration.

## Customization

- [ ] Dark theme.
- [ ] Light theme.
- [ ] System theme.
- [ ] Custom keyboard size.
- [ ] Custom position.
- [ ] Persistent settings.
- [ ] Custom shortcuts.

---

# 24. Important Technical Decisions & Restrictions

The following decisions are mandatory during implementation.

1. Do not create a web application or Electron application. This is a native Windows desktop application.
2. Use C# and WPF for the primary application.
3. Do not implement only a visual keyboard. System-wide input is mandatory.
4. Do not use English-to-Bengali transliteration as the primary typing method.
5. Follow the user-provided Ridmik keyboard screenshots for the visual layout and key positions.
6. Do not hardcode keyboard layouts into individual UI controls.
7. Keep prediction offline.
8. Do not use a paid AI API for normal word completion.
9. Store emoji history and clipboard history locally.
10. Save keyboard position and dimensions automatically.
11. Preserve the last selected keyboard presentation mode.
12. Avoid unnecessary background processes.
13. Do not block the UI thread.
14. Use appropriate native Windows integration for reliable system-wide typing.
15. Do not compromise privacy for clipboard functionality.
16. Do not claim a feature is complete until it has been implemented and tested.

---

# 25. Definition of Done

The project can be considered complete only when all the following conditions are satisfied.

**Typing:** A user can type Bengali using both the physical keyboard and the on-screen keyboard in supported Windows applications.

**Layout:** The keyboard reproduces the key arrangement from the supplied Ridmik screenshots.

**Prediction:** Partial words receive 3–5 useful Bengali suggestions, and completed words receive context-aware next-word suggestions.

**Emoji:** Emoji search, categories, and frequently used history work and survive restarts.

**Clipboard:** Clipboard history works locally with search, pinning, deletion, and privacy controls.

**Floating UI:** The keyboard can be freely moved, resized, minimized to a single icon, hidden to the tray, and restored.

**Persistence:** The application restores the user's saved dimensions, position, mode, and layout after Windows restarts.

**Startup:** The application can launch automatically with Windows and remain available in the system tray.

**Performance:** Normal typing and keyboard interaction remain responsive without unnecessary CPU or memory consumption.

**Reliability:** The application handles monitor changes, display scaling, application focus, and unexpected shutdowns gracefully.

---

# 26. Instructions for AI Coding Agents

You are acting as a senior Windows desktop software architect and C#/.NET engineer.

Your task is to design and implement the complete Bengali Virtual Keyboard application described in this specification.

## Implementation Rules

1. Read and understand this entire specification before writing code.
2. Inspect the user-provided Ridmik Keyboard screenshots if available.
3. Identify the exact keyboard layout and character positions.
4. Create the complete project architecture.
5. Use C#, WPF, MVVM, and .NET 10.
6. Implement the system-wide input functionality rather than stopping at the UI.
7. Build reusable components and maintainable services.
8. Implement SQLite-based local persistence.
9. Implement offline Bengali word completion and next-word prediction.
10. Integrate the Windows Text Services Framework appropriately.
11. Implement floating, compact, minimal-icon, and tray-hidden modes.
12. Implement reliable position and size restoration.
13. Implement the complete emoji and clipboard functionality.
14. Follow privacy and performance requirements.
15. Write automated tests for critical functionality.
16. Keep the solution buildable after each development phase.

## Required Working Method

Do not attempt to implement every feature in one enormous code-generation step.

Work phase by phase.

For each phase:

- Explain the architecture briefly.
- Create the necessary project files.
- Write complete, executable code.
- Explain dependencies and installation.
- Build the project.
- Fix compilation errors.
- Run relevant tests.
- Verify the implemented functionality.
- Update project documentation.
- Identify remaining limitations.

Do not use placeholder methods for critical functionality.

Do not create buttons that do nothing.

Do not simulate working system-wide Bengali input using hardcoded example text.

If native Windows integration requires C++ or COM, implement the necessary native component and explain the build and registration process.

When an implementation detail is uncertain, investigate the relevant Windows/.NET API documentation before choosing an approach.

Do not silently remove requirements because they are technically challenging.

If a requirement cannot be implemented as specified, explain the limitation and provide the closest reliable alternative.

## Final Expected Deliverables

1. Complete Visual Studio solution.
2. Full C# source code.
3. Native TSF integration source code.
4. Bengali keyboard layout definitions.
5. Offline Bengali dictionary and prediction resources, with documented licensing and attribution.
6. Emoji database.
7. Clipboard history implementation.
8. Floating keyboard implementation.
9. Windows system tray integration.
10. Startup integration.
11. SQLite database implementation.
12. Automated tests.
13. README.md.
14. Installation documentation.
15. Windows installer.
16. Release build.

---

# 27. Final Product Vision

The final application should feel like using Ridmik Keyboard on an Android smartphone, but directly on a Windows PC.

The user should be able to:

- Open any Windows application.
- Activate Bengali mode.
- Type Bengali using the familiar keyboard layout.
- See suggestions immediately.
- Complete words with one click.
- Predict and insert the next word.
- Search and insert emojis.
- Access frequently used emojis.
- Retrieve previously copied text.
- Move the keyboard anywhere on the screen.
- Resize it to any comfortable size.
- Reduce it to a single floating icon.
- Hide it completely to the system tray.
- Restart Windows and find the application in its previous configuration.

The entire experience should be smooth, responsive, professional, and lightweight.

**The primary goal is not to build another ordinary on-screen keyboard. The goal is to create a complete, intelligent, customizable, offline-first Bengali typing environment for Windows.**