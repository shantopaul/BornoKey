# Contributing to BornoKey (বর্ণকী)

Thank you for your interest in contributing to **BornoKey**!  
BornoKey is an open-source, privacy-first, 100% offline Bengali (`বাংলা`) and English (`ENG`) virtual and physical keyboard for Windows 10 and Windows 11.

---

## 🧭 Core Principles & Constraints

When contributing to BornoKey, please keep these non-negotiable principles in mind:

1. **100% Offline & Zero Keystroke Logging:**
   - BornoKey **never** initiates network connections, telemetric pings, cloud sync, or analytics.
   - Keystrokes and clipboard data are processed purely in-memory and local DPAPI-encrypted storage. Any PR introducing external network dependencies will be rejected.
2. **Zero Build Warnings:**
   - The solution enforces `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. All builds must compile cleanly with 0 warnings.
3. **Comprehensive Test Coverage:**
   - Any modifications to Unicode composition, layout maps, prediction, or storage must include unit tests. All tests (`49 / 49`) must pass before submitting.
4. **Performance Budgets:**
   - Keystroke latency must remain $\le 5$ ms.
   - Suggestion queries must complete in $\le 50$ ms.

---

## 🛠️ Development Setup

### Prerequisites

- **Operating System:** Windows 10 (21H2+) or Windows 11 (x64)
- **SDK:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via `global.json`)
- **IDE:** Visual Studio 2026 (with *.NET Desktop Development* and *Desktop development with C++*) or Visual Studio Code with the *C# Dev Kit* extension.
- **Installer Builder (Optional):** [Inno Setup 6](https://jrsoftware.org/isdl.php) (for compiling `BornoKey-Setup-v1.0.0-win-x64.exe`).

### Build & Run Locally

```powershell
# 1. Clone your fork
git clone https://github.com/<your-username>/BornoKey.git
cd BornoKey

# 2. Restore NuGet dependencies
dotnet restore BengaliKeyboard.sln

# 3. Build in Release mode
dotnet build BengaliKeyboard.sln -c Release

# 4. Run the xUnit test suite (must report 49 / 49 passing)
dotnet test BengaliKeyboard.sln -c Release --nologo --verbosity minimal

# 5. Launch the application
dotnet run --project src/BengaliKeyboard.App/BengaliKeyboard.App.csproj
```

---

## 📦 Solution Architecture

The solution (`BengaliKeyboard.sln`) is modularized into focused projects:

- **`BengaliKeyboard.Core`**: Common abstractions, contracts, and core domain primitives.
- **`BengaliKeyboard.Layouts`**: Bengali (6 layers: Primary, Shift, Number, Symbol) and English layout definitions.
- **`BengaliKeyboard.Input`**: Low-level Windows input hooks, `WS_EX_NOACTIVATE` window management, and Windows TSF integration.
- **`BengaliKeyboard.Prediction`**: Real-time prefix dictionary matching, contraction expansion, and n-gram next-word prediction.
- **`BengaliKeyboard.Emoji`**: Direct2D + DirectWrite `COLR/CPAL` vector color emoji keyboard.
- **`BengaliKeyboard.Clipboard`**: DPAPI-encrypted Windows clipboard history manager.
- **`BengaliKeyboard.Storage`**: Local SQLite database access (`dictionary.db`, `settings.json`).
- **`BengaliKeyboard.Windows`**: Native Windows API wrappers and interop.
- **`BengaliKeyboard.UI`**: Custom WPF controls, responsive uniform scaling, themes, and settings views.
- **`BengaliKeyboard.App`**: Executable entry point (`BornoKey.exe`), lifecycle management, and tray icon.
- **`BengaliKeyboard.Tests`**: Complete xUnit test suite.

---

## 🌿 Git & Pull Request Workflow

1. **Fork the Repository:** Create a personal fork on GitHub.
2. **Create a Feature Branch:**
   ```powershell
   git checkout -b feature/your-feature-name
   # or for bug fixes:
   git checkout -b fix/issue-description
   ```
3. **Commit Messages:** Follow the [Conventional Commits](https://www.conventionalcommits.org/) format:
   - `feat(layout): add new Bengali symbol mapping`
   - `fix(prediction): handle edge case in prefix lookup`
   - `docs(readme): clarify shortcut documentation`
   - `test(emoji): add Direct2D fallback test case`
4. **Run Verification Before Pushing:**
   ```powershell
   dotnet test BengaliKeyboard.sln -c Release --nologo
   ```
5. **Open a Pull Request:**
   - Fill out the PR template completely.
   - Reference any linked issues (e.g., `Fixes #12`).

---

## 👤 Recognition

Accepted contributors are credited in [`AUTHORS.md`](./AUTHORS.md) and in GitHub release notes. Thank you for making Bengali typing on Windows faster, easier, and privacy-first!
