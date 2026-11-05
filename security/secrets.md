# Secrets & Sensitive Material Assessment — BornoKey

## 0. Current Project State & Secret Scan Summary

> **Verification of Existing Workspace:**  
> A full inspection of all files currently in `BornoKey/` (`Ridmik-Style Bengali Virtual Keyboard for Windows.md` and `docs/*.md`) confirms:
> - **Hardcoded Secrets Found:** **0** (No API keys, passwords, tokens, private keys, certificates, or `.env` files exist in the workspace).
> - **Git Repository Status:** No `.git` directory or commit history exists yet.
> - **Executable Code Status:** Pre-coding specification phase (`docs/Memory.md:9-15`).

---

## 1. Where Secrets Are Expected

Because **BornoKey** is a 100% offline-first native Windows desktop application (`Ridmik-Style Bengali Virtual Keyboard for Windows.md:464`, `1222`; `docs/Rules.md:36`), it requires **zero external cloud API keys, database server passwords, or OAuth client secrets**.

However, three distinct categories of sensitive/secret material apply to this project's lifecycle and runtime:

| Secret / Sensitive Material Category | Expected Location | Purpose | Current Specification Status |
|---|---|---|---|
| **1. User-Owned Secrets Captured via Clipboard (`ClipboardHistory`)** | `%LOCALAPPDATA%\BengaliKeyboard\Database\keyboard.db` (`ClipboardHistory.Content`) | Users inevitably copy passwords, API tokens, 2FA codes, and private keys to the Windows clipboard while BornoKey is running. | **Gap Identified:** Currently specified as plaintext `Content TEXT NOT NULL` (`docs/Architecture.md:304`). |
| **2. Per-User Local Encryption & HMAC Key Material** | Windows DPAPI (`DataProtectionScope.CurrentUser`) or `%LOCALAPPDATA%\BengaliKeyboard\Config\` | Protects local SQLite clipboard contents at rest and keys the deduplication `ContentHash` (`HMACSHA256`). | **Gap Identified:** Not yet specified in `docs/Architecture.md` or `docs/Rules.md`. |
| **3. Authenticode Code-Signing Certificates (`.pfx` / Hardware Token)** | External CI/CD Secret Store or Developer Windows Certificate Store (`Cert:\CurrentUser\My`) | Signs `BornoKey.exe`, `BengaliKeyboard.TSF.dll` (COM in-process server), and the Inno Setup installer (`BornoKeySetup.iss`). | **Gap Identified:** No `.gitignore` or signing key handling policy is documented yet. |

---

## 2. Environment & Configuration Handling

- **Runtime Configuration (`%LOCALAPPDATA%\BengaliKeyboard\Config\settings.json`):**
  - Specified in `docs/Architecture.md:214`, `252` and `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1195`.
  - Stores non-secret UI and behavioral preferences (`WindowMode`, `Width`, `Height`, `Left`, `Top`, `MonitorDeviceId`, `ActiveLanguage`, `Theme`, `AlwaysOnTop`, global shortcut bindings).
  - Must **never** store encryption keys, raw clipboard items, or sensitive user text.
- **SQLite Application Settings (`ApplicationSettings` Table in `keyboard.db`):**
  - Specified in `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1169-1172` and `docs/Architecture.md:310-313` (`Key TEXT PRIMARY KEY, Value TEXT NOT NULL`).
  - If an installation-specific HMAC key is stored here to compute `ClipboardHistory.ContentHash`, its `Value` must be encrypted via Windows DPAPI (`ProtectedData.Protect`) rather than stored in plaintext.
- **Environment Variables:**
  - The application uses only standard Windows folder resolution (`%LOCALAPPDATA%` / `Environment.SpecialFolder.LocalApplicationData`, `docs/Architecture.md:209`). No secret environment variables are required or consumed.

---

## 3. Hardcoded-Secret Risks

- **Current Status:** No hardcoded secrets exist in the repository.
- **Future Implementation Risk (C# & C++ Binary Decompilation):**
  - `.NET 10` assemblies (`BengaliKeyboard.*.dll`) can be trivially decompiled using tools such as ILSpy or dnSpy.
  - **Strict Rule:** If encryption is added for `keyboard.db` or `ClipboardHistory.Content`, developers must **never** hardcode a static AES key, passphrase, or HMAC salt inside C# source files (`ClipboardRepository.cs` or `SqliteConnectionFactory.cs`). Static keys embedded in client binaries offer zero protection because every installation shares the same extractable key.
  - All local encryption keys must be derived dynamically per Windows user account using **Windows Data Protection API (DPAPI)** (`ProtectedData.Protect` / `CryptProtectData`).

---

## 4. Client-Side Exposure Risks

Because BornoKey is a desktop client application running on the user's machine:
1. **Plaintext SQLite Files (`keyboard.db`):**
   - Located at `%LOCALAPPDATA%\BengaliKeyboard\Database\keyboard.db` (`docs/Architecture.md:254`).
   - Without DPAPI encryption on `ClipboardHistory.Content` and `ContentHash`, any local process running under the user's account—or any cloud backup tool syncing AppData—exposes historical clipboard secrets.
2. **SQLite Write-Ahead Log (`keyboard.db-wal` & `keyboard.db-shm`):**
   - `docs/Rules.md:15` mandates `PRAGMA journal_mode=WAL;`.
   - Even after a user clicks `[Clear History]` (`DELETE FROM ClipboardHistory`), deleted plaintext rows can remain residually in `keyboard.db` free pages or the `.db-wal` file until a `VACUUM` and `PRAGMA wal_checkpoint(TRUNCATE);` are executed or unless the column payloads were encrypted prior to insertion.
3. **In-Memory Process Dumps:**
   - Sensitive strings stored as immutable `System.String` objects in `.NET` remain in managed heap memory until garbage collected. Keeping clipboard history payloads encrypted in memory until rendered or bounding maximum retention reduces exposure window.

---

## 5. Git & History Exposure Risks

- **Current Status:** The workspace is not yet initialized as a Git repository (`git status` returns `fatal: not a git repository`), and no `.gitignore` file exists yet.
- **Risk During Phase 1–10 Development:**
  - Once Git is initialized (`Ridmik-Style Bengali Virtual Keyboard for Windows.md:82`), developers testing the application locally or packaging the installer (`installer/BornoKeySetup.iss`) could accidentally stage and commit:
    - Code-signing certificates (`*.pfx`, `*.p12`, `*.snk`, `*.cer`, `*.key`)
    - Local test SQLite databases (`*.db`, `*.db-wal`, `*.db-shm`) containing real developer clipboard history
    - Local diagnostic log files (`*.log`)
    - User-specific Visual Studio files (`*.suo`, `*.user`, `.vs/`)

---

## 6. Logging & Error Exposure Risks

- **Architectural Specification:**
  - `Ridmik-Style Bengali Virtual Keyboard for Windows.md:645`, `1286` and `docs/Rules.md:44` explicitly state: *"Never log typed characters, words, or clipboard contents into Serilog files or console output under any log level (`Debug`, `Information`, `Error`)."*
- **Subtle Exposure Vectors to Guard Against in Code:**
  1. **Exception Message Leakage:** If a database write fails in `ClipboardRepository` or `UserDictionaryRepository`, passing the model object or raw SQL query into `Log.Error(ex, "Failed to save {@Entry}", entry)` via Serilog's `@` destructuring operator will serialize `ClipboardEntry.Content` directly to `%LOCALAPPDATA%\BengaliKeyboard\Logs\bornokey-YYYYMMDD.log`.
  2. **Global Unhandled Exception Dumps:** `docs/Rules.md:64` specifies global handlers on `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`. These handlers must log only exception types, error codes, and call stacks—never dumping model state or local variables containing input buffers.

---

## 7. Secret Rotation & Sanitization Requirements

| Material Type | Rotation / Sanitization Mechanism |
|---|---|
| **User Clipboard History (`keyboard.db`)** | 1. Automatic expiration task enforcing user-configured retention period (`24h`, `7d`, `30d`, `docs/PRD.md:127`).<br>2. When deleting items or executing `Clear Clipboard History`, execute `PRAGMA secure_delete = ON;` and `PRAGMA wal_checkpoint(TRUNCATE);` so deleted records are zeroed on disk. |
| **Per-User DPAPI / HMAC Key** | Tied automatically to the user's Windows login credentials by the OS (`DataProtectionScope.CurrentUser`). When the user clicks `Clear All Application Data` in **Settings → Privacy** (`docs/PRD.md:129`), regenerate the HMAC key so old hashes are invalidated. |
| **Authenticode Code-Signing Certificate** | Stored outside the repository in hardware HSM / Azure Key Vault / Windows Certificate Store; rotated prior to certificate expiration with RFC 3161 timestamping (`signtool sign /tr ... /td sha256 /fd sha256`) so existing signed releases remain valid after certificate expiry. |

---

## 8. Actual Findings with Evidence

### Confirmed Vulnerabilities
*None in executable code (no source code or hardcoded secrets exist in the repository yet).*

---

### Security Weaknesses

#### Finding SEC-01: Unsalted Deterministic `ContentHash` in `ClipboardHistory` Schema (`Security weakness`)
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Architecture.md:305` defines `ContentHash TEXT NOT NULL UNIQUE` in the `ClipboardHistory` SQLite table for deduplication of copied text.
- **Risk:** If `ContentHash` is implemented as an unsalted SHA-256 hash, an attacker who obtains `keyboard.db` can perform fast offline dictionary or brute-force lookups against `ContentHash` to identify short secrets (e.g., 6-digit OTPs, PINs, or known passwords) even if `Content` is encrypted.
- **Affected location:** `docs/Architecture.md:302-308` (planned `src/BengaliKeyboard.Clipboard/ClipboardHistoryService.cs` & `ClipboardRepository.cs`).
- **Why it matters:** Deduplication hashes over sensitive user clips leak information about low-entropy secrets unless keyed with a secret per-installation key.
- **Recommended fix:** Compute `ContentHash` using `HMACSHA256` with a random 256-bit key generated per installation, encrypted via Windows DPAPI (`ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`), and stored in `ApplicationSettings`.

---

### Missing Controls

#### Finding SEC-02: Missing At-Rest Encryption Specification for `ClipboardHistory.Content` (`Missing control`)
- **Severity:** **HIGH**
- **Evidence:** `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1162-1167` and `docs/Architecture.md:302-308` specify `Content TEXT NOT NULL` in `keyboard.db` without DPAPI encryption, despite `Ridmik-Style Bengali Virtual Keyboard for Windows.md:634` noting that clipboard data can contain passwords and confidential information, and line `1254` requiring *"Local database protection where appropriate"*.
- **Risk:** Any user-copied secret that bypasses clipboard format exclusions (such as an API token, SSH key, or password copied from a browser/terminal) is written in cleartext to `%LOCALAPPDATA%\BengaliKeyboard\Database\keyboard.db`.
- **Affected location:** `docs/Architecture.md:302-308`, `Ridmik-Style Bengali Virtual Keyboard for Windows.md:1162-1167`.
- **Why it matters:** Plaintext SQLite tables in `%LOCALAPPDATA%` are trivially readable by any non-elevated script, backup tool, or local attacker.
- **Recommended fix:** Encrypt `ClipboardHistory.Content` using `ProtectedData.Protect(Encoding.UTF8.GetBytes(content), optionalEntropy, DataProtectionScope.CurrentUser)` before storing it in SQLite (as base64 or `BLOB`), and enable `PRAGMA secure_delete = ON;` on `keyboard.db`.

#### Finding SEC-03: Missing `.gitignore` to Prevent Accidental Commit of Certificates, `.db` Files & Logs (`Missing control`)
- **Severity:** **MEDIUM**
- **Evidence:** Workspace file listing confirms no `.gitignore` file exists in `BornoKey/`, while `Ridmik-Style Bengali Virtual Keyboard for Windows.md:82` specifies Git/GitHub version control and `docs/Architecture.md:247`, `254` specify local SQLite databases (`*.db`) and an installer (`installer/BornoKeySetup.iss`).
- **Risk:** When Git is initialized during Phase 1, developer test databases (`keyboard.db`), rolling log files (`*.log`), or code-signing files (`*.pfx`, `*.snk`) could be accidentally committed to version history.
- **Affected location:** Repository root (`BornoKey/`).
- **Why it matters:** Secrets or personal test clipboard databases committed to Git history persist across commits even after file deletion.
- **Recommended fix:** Create a comprehensive `.gitignore` at the repository root prior to the first `git commit` that explicitly excludes `*.pfx`, `*.p12`, `*.snk`, `*.key`, `*.pem`, `*.db`, `*.db-wal`, `*.db-shm`, `*.log`, `bin/`, `obj/`, and `.vs/`.

#### Finding SEC-04: Missing Serilog Destructuring Redaction Policy for Domain Models (`Missing control`)
- **Severity:** **MEDIUM**
- **Evidence:** `docs/Rules.md:19`, `44` mandate Serilog file logging (`%LOCALAPPDATA%\BengaliKeyboard\Logs\`) and forbid logging typed characters or clipboard contents, but no automated redaction attribute (e.g., `[NotLogged]` or custom `IDestructuringPolicy`) is specified for `ClipboardEntry`, `KeyDefinition`, or `SuggestionCandidate` models (`docs/Architecture.md:155-162`).
- **Risk:** A developer logging an exception with structured model parameters (`_logger.LogError(ex, "Error processing {@Item}", item)`) could inadvertently write sensitive user text to disk logs.
- **Affected location:** `docs/Architecture.md:155-162` (planned `src/BengaliKeyboard.Core/Models/ClipboardEntry.cs` and `src/BengaliKeyboard.App/Bootstrapper.cs`).
- **Why it matters:** Relying solely on developer discipline without safe `ToString()` overrides or Serilog destructuring policies frequently leads to accidental PII/secret leakage in error logs.
- **Recommended fix:** Override `ToString()` on `ClipboardEntry` to return only non-sensitive metadata (`$"ClipboardEntry[Id={Id}, IsPinned={IsPinned}, Length={Content?.Length ?? 0}]"`) and configure a Serilog `IDestructuringPolicy` in `Bootstrapper.cs` that strips `Content` and raw input buffers from all log events.
