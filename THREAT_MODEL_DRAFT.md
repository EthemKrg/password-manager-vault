# Password Manager — THREAT_MODEL_DRAFT

## 1. Purpose

This is the initial threat model draft for the Password Manager project.

This document defines what the app should protect against, what it does not protect against, and which security behaviors are mandatory.

---

## 2. Assets to Protect

The app must protect:

- Master password
- Account passwords
- Usernames/emails
- Website/service URLs
- Notes inside entries
- Tags/categories if sensitive
- Vault metadata if it can reveal user behavior
- Backup files
- Clipboard content
- Recovery key if implemented
- Decrypted vault contents in memory as much as practical

---

## 3. Trust Boundaries

## 3.1 Trusted

- The user
- The app code after integrity is assumed
- The local device only while not compromised
- The decrypted vault only while unlocked in app memory

## 3.2 Not Trusted

- Cloud storage provider
- Cloud-synced folder
- File system outside the app's controlled behavior
- Logs
- Crash reports
- Clipboard
- Screenshots/screen recording
- Other apps on the device
- Malware/keyloggers
- Someone with physical access to an unlocked device

---

## 4. Primary Threats

## 4.1 Cloud Account Compromise

Scenario:

An attacker gets access to the user's OneDrive/Google Drive/iCloud/Dropbox folder.

Expected protection:

- Attacker only sees encrypted vault files.
- Attacker cannot read credentials without master password/recovery key.
- Versioned backups are also encrypted.

Required controls:

- No plaintext vault copies in cloud folder.
- No plaintext backups.
- No metadata sidecar containing secrets.

---

## 4.2 Device Loss or Theft

Scenario:

The user's PC or phone is lost/stolen.

Expected protection:

- Vault file remains encrypted at rest.
- App should require master password or approved unlock method.
- Auto-lock reduces exposure if the app was left open.

Required controls:

- Master password is never stored.
- Vault is encrypted at rest.
- Lock-on-idle.
- Optional lock/mask on minimize.
- Quick unlock, if implemented, must be reviewed separately.

---

## 4.3 Local File Inspection

Scenario:

Someone inspects app data folders, cloud sync folders, backups, or config files.

Expected protection:

- No plaintext credentials are found.
- Config files do not contain secrets.
- Logs do not contain secrets.

Required controls:

- No plaintext credential files.
- No secret logs.
- No appsettings secrets.
- Backups encrypted.

---

## 4.4 Clipboard Exposure

Scenario:

The user copies a password and another app reads the clipboard.

Expected protection:

- Clipboard exposure window is short.
- User understands the countdown.

Required controls:

- Clipboard auto-clear.
- Clipboard countdown UI.
- Configurable or sensible default duration.
- Avoid copying without explicit action.

---

## 4.5 Sync Conflict or Corruption

Scenario:

Two devices edit the vault, or cloud sync corrupts/overwrites a file.

Expected protection:

- App does not silently overwrite newer vault.
- App creates backup before destructive actions.
- App can detect conflict and create conflict copy.

Required controls:

- Last modified metadata check.
- Backup before save/restore.
- Conflict detection.
- No silent overwrite.

---

## 4.6 Wrong Password / Brute Force

Scenario:

Attacker repeatedly tries passwords against the vault file.

Expected protection:

- Vault encryption/KDF makes guessing expensive.
- Wrong password fails safely.

Required controls:

- Use established vault format/KDF.
- Do not weaken KDF settings.
- Do not store password hints that reveal too much.
- Do not implement backdoor recovery.

---

## 4.7 Malware / Keylogger

Scenario:

The device is compromised by malware or keylogger.

Expected protection:

The app cannot fully protect against a compromised endpoint.

Required communication:

- Be honest in documentation.
- Do not claim protection against malware/keyloggers.
- Minimize exposure but do not oversell.

Possible mitigations:

- Auto-lock.
- Temporary reveal.
- Clipboard clear.
- Avoid unnecessary plaintext lifetime.
- Biometric quick unlock only after review.

---

## 4.8 Shoulder Surfing / Screen Exposure

Scenario:

Someone sees the screen while the vault is open.

Expected protection:

- Passwords are hidden by default.
- Reveal is temporary and deliberate.

Required controls:

- Mask passwords by default.
- Reveal requires explicit action.
- Optional auto-hide after duration.
- Lock/mask on minimize.

---

## 5. Explicit Non-Goals

The app does not fully protect against:

- Fully compromised OS
- Active keylogger
- Screen recording malware
- Malicious clipboard manager
- User revealing master password
- Weak master password chosen by user
- Unlocked device in attacker's hands
- Cloud provider deleting files if backups/versioning are not available
- User losing both master password and recovery key

---

## 6. Mandatory Security Behaviors

- Master password is never stored.
- Plaintext credentials are never written to disk.
- Backups are encrypted.
- Logs never contain secrets.
- Clipboard auto-clears.
- Passwords are hidden by default.
- Wrong password fails safely.
- App locks after inactivity.
- App warns that forgotten master password cannot be recovered.
- Sync never silently overwrites newer/conflicting vault.
- Dangerous actions require confirmation.

---

## 7. Open Threat Model Questions

- Should recovery key be implemented in MVP?
- Should lock-on-minimize be enabled by default?
- What is the default clipboard clear duration?
- Should password reveal auto-hide?
- Should the app support screenshot protection on mobile?
- Should failed unlock attempts introduce delay?
- Should vault file metadata be minimized?
- How should conflict resolution work in first version?

---

## 8. Review Rule

Any new feature must answer:

1. Does this increase secret exposure?
2. Does this create plaintext anywhere?
3. Does this weaken the vault model?
4. Does this make sync overwrite more dangerous?
5. Does this create misleading security claims?
6. Does this make the app slower without real value?

If yes, redesign before implementation.
