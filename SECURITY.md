# Security Policy — BornoKey (বর্ণকী)

Security and privacy are fundamental pillars of **BornoKey**. BornoKey is engineered from the ground up as a **100% offline-first, zero-telemetry** keyboard and clipboard manager.

---

## 🛡️ Supported Versions

We provide security updates and patches for the following versions:

| Version | Supported | Notes |
|---|---|---|
| `1.0.x` | ✅ Yes | Current Active Production Release |
| `< 1.0.0` | ❌ No | Pre-release preview builds |

---

## 🔒 Privacy & Security Invariants

BornoKey strictly adheres to the following privacy invariants:

1. **Zero Network Connectivity:**
   - BornoKey contains **no socket connections, HTTP clients, telemetric beacons, crash reporters, or analytics**. All computations, dictionary lookups, and emoji renderings execute 100% locally on your machine.
2. **DPAPI Data Encryption:**
   - Any sensitive persisted data (such as clipboard history) is encrypted at rest using the Windows **Data Protection API (`DPAPI`)** bound to the current Windows user account (`CurrentUser` scope).
3. **Password Manager Exclusion:**
   - BornoKey automatically detects and ignores clipboard items tagged with password-manager formats (`Clipboard Viewer Ignore`, `ExcludeClipboardContentFromMonitor`, 1Password, Bitwarden, KeePassXC, Windows CredUI).
4. **Zero Keystroke Persistence:**
   - Virtual and physical keyboard inputs are forwarded directly via Windows Text Services Framework (`TSF`) or `SendInput`. Keystrokes are **never logged to disk or transmitted**.

---

## 🚨 Reporting a Vulnerability

If you discover a security vulnerability or privacy violation in BornoKey:

1. **Do not disclose the issue publicly** in GitHub issues, pull requests, or discussions.
2. **Submit a Private Vulnerability Report** directly on GitHub:
   - Go to [BornoKey Security Advisories](https://github.com/shantopaul/BornoKey/security/advisories/new).
3. **Alternatively, contact the lead maintainer directly:**
   - Email: **`shanto@shantopaul.com`**
   - Subject: `[Security] BornoKey Vulnerability Report`

### What to Include in Your Report

- Detailed description of the vulnerability.
- Steps to reproduce or proof-of-concept (PoC) code.
- Impact assessment (e.g., potential data exposure or privilege escalation).
- Your Windows version and BornoKey build version.

### Response & Disclosure Timeline

- **Acknowledgment:** Within 48 hours of submission.
- **Triage & Patch:** We strive to release a fix for confirmed security vulnerabilities within 7 to 14 days.
- **Credit:** Security researchers who report valid vulnerabilities will be acknowledged in release notes and in [`AUTHORS.md`](./AUTHORS.md).
