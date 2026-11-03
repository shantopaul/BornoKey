# UI/UX Design System, Color Tokens & Typography Specification
## BornoKey — Ridmik-Style Bengali Virtual Keyboard for Windows

---

## 1. Visual Identity & Design Philosophy

**BornoKey** blends two distinct visual paradigms into a cohesive desktop utility:
1. **Android Ridmik Keyboard Familiarity:** Dark slate surfaces, high-contrast white Bengali glyphs, distinct charcoal functional keys, a dedicated number row, and an integrated 3–5 item suggestion bar directly above the keys.
2. **Windows 11 Fluent Desktop Craftsmanship:** Rounded window corners (`8px`), crisp `1px` surface borders, subtle keycap elevation shadows, zero unnecessary window chrome, and smooth proportional scaling from compact badges up to wide desktop monitors.

### Core UI Principles
- **Legibility First:** Bengali characters feature complex top (*reph*, *i-kar*, *oi-kar*) and bottom (*u-kar*, *rri-kar*, *hasanta*) ascenders/descenders. Keys must never clip vertical matras or conjuncts at any window scale.
- **Zero-Latency Tactile Feedback:** Visual key press feedback must occur within `< 16ms` (1 frame) on mouse-down or physical key-down so rapid typing feels mechanical and responsive.
- **Unobtrusive Floating Presence:** Minimal header bar (`28px`), non-activating click behavior (`WS_EX_NOACTIVATE`), and configurable opacity allow the keyboard to float over editors without visual clutter.

---

## 2. Colors & Theme System

All UI components bind strictly to semantic `DynamicResource` brush tokens—never raw hex literals in control XAML.

### 2.1 Dark Theme Palette (Default — Ridmik Signature)

| Semantic Token Key | Role / Element | Hex Value | WCAG Contrast |
|---|---|---|---|
| `Brush.Window.Background` | Main Keyboard Chassis Background | `#17191F` | Base Surface |
| `Brush.Header.Background` | Top Drag Bar & Suggestion Bar Surface | `#20242B` | Subtle Elevation |
| `Brush.Window.Border` | Outer Window Border (`1px`) | `#2E333D` | Crisp Frame |
| `Brush.Key.Regular.Background` | Standard Character Key Surface | `#30343D` | — |
| `Brush.Key.Regular.Hover` | Standard Character Key Hover | `#3B404C` | Interactive |
| `Brush.Key.Regular.Pressed` | Standard Character Key Active / Pressed | `#23262E` | Tactile Depress |
| `Brush.Key.Special.Background` | Functional Keys (`Shift`, `⌫`, `?123`, `🌐`) | `#414653` | Clear Hierarchy |
| `Brush.Key.Special.Hover` | Functional Key Hover | `#4E5464` | Interactive |
| `Brush.Key.Special.Pressed` | Functional Key Active / Pressed | `#2D313B` | Tactile Depress |
| `Brush.Key.Shadow` | 1.5px Bottom Keycap Edge Shadow | `#0E1014` | Depth Cue |
| `Brush.Accent.Primary` | `Enter` Key, Active Language Pill, Active Tab | `#3B82F6` | `4.6:1` vs White |
| `Brush.Accent.Hover` | Primary Accent Hover State | `#2563EB` | `5.8:1` vs White |
| `Brush.Accent.Subtle` | Selected Suggestion Pill Highlight | `#1E3A5F` | `8.2:1` vs White |
| `Brush.Text.Primary` | Primary Key Glyph & Main UI Text | `#FFFFFF` | `12.4:1` on `#30343D` (AAA) |
| `Brush.Text.Secondary` | Secondary Key Hint (Top-Right) & Subtitles | `#AEB5C2` | `6.1:1` on `#30343D` (AA) |
| `Brush.Text.Muted` | Placeholders, Dividers & Disabled Labels | `#6E7687` | — |
| `Brush.Status.Success` | Active / Pinned Indicator | `#10B981` | Semantic |
| `Brush.Status.Warning` | Elevation / Fallback Notice | `#F59E0B` | Semantic |
| `Brush.Status.Danger` | Delete Clip / Clear History Action | `#EF4444` | Semantic |

### 2.2 Light Theme Palette (Windows 11 Daylight)

| Semantic Token Key | Role / Element | Hex Value | WCAG Contrast |
|---|---|---|---|
| `Brush.Window.Background` | Main Keyboard Chassis Background | `#ECEEF2` | Base Surface |
| `Brush.Header.Background` | Top Drag Bar & Suggestion Bar Surface | `#F7F8FA` | Subtle Elevation |
| `Brush.Window.Border` | Outer Window Border (`1px`) | `#D0D5DD` | Crisp Frame |
| `Brush.Key.Regular.Background` | Standard Character Key Surface | `#FFFFFF` | — |
| `Brush.Key.Regular.Hover` | Standard Character Key Hover | `#F3F4F6` | Interactive |
| `Brush.Key.Regular.Pressed` | Standard Character Key Active / Pressed | `#E5E7EB` | Tactile Depress |
| `Brush.Key.Special.Background` | Functional Keys (`Shift`, `⌫`, `?123`, `🌐`) | `#D8DCE3` | Clear Hierarchy |
| `Brush.Key.Special.Hover` | Functional Key Hover | `#C8CDD6` | Interactive |
| `Brush.Key.Special.Pressed` | Functional Key Active / Pressed | `#B8BEC9` | Tactile Depress |
| `Brush.Key.Shadow` | 1.5px Bottom Keycap Edge Shadow | `#B0B7C3` | Depth Cue |
| `Brush.Accent.Primary` | `Enter` Key, Active Language Pill, Active Tab | `#2563EB` | `5.8:1` vs White |
| `Brush.Text.Primary` | Primary Key Glyph & Main UI Text | `#111827` | `17.5:1` on `#FFFFFF` (AAA) |
| `Brush.Text.Secondary` | Secondary Key Hint (Top-Right) & Subtitles | `#4B5563` | `7.5:1` on `#FFFFFF` (AAA) |

### 2.3 Theme Customization Tokens
Users can customize the following visual parameters in **Settings → Appearance**:
- **Theme Mode:** `Dark` (Default), `Light`, `System Default` (auto-syncs with Windows `AppsUseLightTheme` registry key).
- **Window Opacity:** Slider from `60%` (`0.60`) to `100%` (`1.00`), default `98%` (`0.98`).
- **Key Corner Radius:** Configurable from `4px` (Sharp) to `10px` (Pill-Rounded), default `6px`.
- **Key Spacing (Gap):** Configurable `3px`, `4px` (Default), or `6px`.

---

## 3. Fonts & Bengali Script Rendering

### 3.1 Font Stack Specification
| Usage Context | Primary Font Family | Fallback Stack | Packaging / Source |
|---|---|---|---|
| **Bengali Key Glyphs & Suggestions** | `Noto Sans Bengali` | `Nirmala UI`, `Vrinda`, `Segoe UI` | Bundled in `assets/Fonts/NotoSansBengali-Regular.ttf` & `Medium.ttf` |
| **English UI, QWERTY Keys & Settings** | `Segoe UI Variable Display` | `Segoe UI`, `Arial` | Native Windows 10/11 System Font |
| **Emoji Grid & Category Icons** | `Segoe UI Emoji` | `Segoe UI Symbol` | Native Windows Color/Monochrome Emoji Font |
| **Keyboard Shortcut Badges** | `Cascadia Code` | `Consolas`, `Courier New` | Native Windows Monospace Font |

### 3.2 Bengali Glyph & Matra Rendering Rules
Bengali Unicode characters require generous vertical ascender (above the *matra* headline, e.g., `ি`, `ী`, `ৈ`, `ঁ`, `র্`) and descender (below the baseline, e.g., `ু`, `ূ`, `ৃ`, `্র`, `্`) headroom:
1. **Never Clip Ascenders/Descenders:** All `TextBlock` elements rendering Bengali text must set `Padding="0,2,0,2"` and avoid restrictive fixed `LineHeight` values that slice off top/bottom vowel signs.
2. **Dotted Circle (`◌`) Display on Vowel Sign Keys:** Dependent vowel signs (*kars* such as `া`, `ি`, `ী`, `ু`, `ূ`, `ৃ`, `ে`, `ৈ`, `ো`, `ৌ`) must be rendered cleanly on keycaps while emitting only the raw combining Unicode code point when pressed.
3. **Clear Text Rendering:** Enable `TextOptions.TextFormattingMode="Ideal"` and `TextOptions.TextRenderingMode="ClearType"` (or `"Auto"` when scaled inside a `Viewbox`) so curves on Bengali glyphs remain razor-sharp at every window size.

---

## 4. Typography Scale & Hierarchy

All keycap and suggestion bar sizes are defined at the **Reference Base Canvas (`850 × 310 px`)** and scale proportionally via WPF vector layout scaling when the user resizes the floating window:

| Element | Font Family | Base Size (px) | Font Weight | Color Token | Alignment |
|---|---|---|---|---|---|
| **Primary Bengali Key Glyph** | `Noto Sans Bengali` | `20px` | `Medium (500)` | `Brush.Text.Primary` | Center / Center |
| **Primary English Key Glyph** | `Segoe UI Variable` | `18px` | `SemiBold (600)` | `Brush.Text.Primary` | Center / Center |
| **Secondary / Shift Hint Glyph** | `Noto Sans Bengali` | `11px` | `Regular (400)` | `Brush.Text.Secondary` | Top-Right (`Margin="0,3,5,0"`) |
| **Number Row Key Glyph** | `Noto Sans Bengali` / `Segoe UI` | `16px` | `Medium (500)` | `Brush.Text.Primary` | Center / Center |
| **Suggestion Bar Candidate** | `Noto Sans Bengali` | `16px` | `Medium (500)` | `Brush.Text.Primary` | Center / Vertical Center |
| **Primary Top Candidate (Rank #1)** | `Noto Sans Bengali` | `16.5px` | `SemiBold (600)` | `Brush.Accent.Primary` | Center / Vertical Center |
| **Spacebar Language Label** | `Noto Sans Bengali` / `Segoe UI` | `13px` | `Medium (500)` | `Brush.Text.Secondary` | Center / Center |
| **Language Toggle (`[ বাংলা ] [ ENG ]`)** | `Noto Sans Bengali` / `Segoe UI` | `12.5px` | `SemiBold (600)` | `Brush.Text.Primary` | Center |
| **Emoji Grid Item** | `Segoe UI Emoji` | `24px` | `Regular (400)` | Native Color | Center |
| **Clipboard Card Body Text** | `Noto Sans Bengali` / `Segoe UI` | `13.5px` | `Regular (400)` | `Brush.Text.Primary` | Left, Max 2 Lines (`TextTrimming="CharacterEllipsis"`) |
| **Settings Section Header** | `Segoe UI Variable` | `18px` | `SemiBold (600)` | `Brush.Text.Primary` | Left |
| **Settings Label / Description** | `Segoe UI` | `13px` / `11.5px` | `Regular (400)` | `Brush.Text.Primary` / `Secondary` | Left |

---

## 5. Window Modes, Layout Geometry & Keycap Anatomy

### 5.1 Main Floating Keyboard Anatomy (Mode A: Expanded — `850 × 310 px` Default)

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ [≡ Drag] BornoKey    [ বাংলা | ENG ]    [😀] [📋] [⚙]   [Compact] [▬] [✕] │ ← Header Toolbar (30px)
├──────────────────────────────────────────────────────────────────────────────┤
│   [ আমার ]        │        [ আমাকে ]        │       [ আমাদের ]      │ [ আমায় ] │ ← Suggestion Bar (38px)
├──────────────────────────────────────────────────────────────────────────────┤
│  [১]   [২]   [৩]   [৪]   [৫]   [৬]   [৭]   [৮]   [৯]   [০]                  │ ← Row 0: Number Row (38px)
│  [ঙ]   [য]   [ড]   [প]   [ট]   [চ]   [জ]   [হ]   [গ]   [ড়]                 │ ← Row 1: Bangla Row 1 (46px)
│  [্র]  [া]   [ি]   [ু]   [র]   [ক]   [ত]   [দ]   [স]   [ব]                  │ ← Row 2: Bangla Row 2 (46px)
│  [⇧ Shift]  [ো]   [ে]   [্]   [অ]   [ম]   [ন]   [ল]   [শ]   [⌫ Backspace]  │ ← Row 3: Bangla Row 3 (46px)
│  [?123] [😀] [🌐 বাংলা/ENG]  [       বাংলা (Space)       ]  [।]  [⏎ Enter]   │ ← Row 4: Action Row (48px)
└──────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Single Keycap Anatomy (`KeyboardKeyControl`)

```text
   ┌─────────────────┐
   │            খ    │  ← Secondary / Shift Hint Glyph (11px, #AEB5C2, Top-Right)
   │                 │
   │        ক        │  ← Primary Character Glyph (20px, #FFFFFF, Centered)
   │                 │
   └─────────────────┘
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀   ← 1.5px Bottom Edge Shadow (#0E1014) for tactile Ridmik depth
```

### 5.3 Dimension Bounds by Presentation Mode

| Window Mode | Default Size (`W × H`) | Minimum Size (`W × H`) | Maximum Size (`W × H`) | Scaling Behavior |
|---|---|---|---|---|
| **Mode A: Expanded Floating** | `850 × 310 px` | `520 × 210 px` | `1600 × 600 px` | Proportional `Viewbox` + crisp border thickness |
| **Mode B: Compact Floating** | `620 × 230 px` | `440 × 180 px` | `900 × 360 px` | Compact padding, full key usability |
| **Mode C: Minimal Floating Icon** | `72 × 40 px` | `60 × 36 px` | `96 × 48 px` | Draggable `[ বাংলা ]` pill badge with status dot |
| **Mode D: Hidden (System Tray)** | `0 × 0 px` (Hidden) | — | — | Accessible via Tray Icon & Global Hotkey |
| **Settings Window** | `760 × 540 px` | `680 × 480 px` | Resizable | Standard Windows 11 2-pane navigation layout |
