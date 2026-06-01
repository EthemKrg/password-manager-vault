# Password Manager — ROADMAP

## 0. Project Definition

**Project Name:** Password Manager  
**Product Type:** Local-first personal password vault  
**Primary Goal:** Store and manage account credentials securely, locally, practically, and beautifully across Windows PC and mobile.

The app stores different account credentials such as service/site name, email/username, password, URL, notes, tags, and metadata inside an encrypted vault.

This is not a simple password list. It is a secure personal vault.

---

## 1. Product Statement

Password Manager is a local-first, master-password-protected, encrypted personal vault for storing account credentials. It supports encrypted cloud-folder sync and versioned encrypted backups so the user can access the same protected vault from Windows and mobile devices while keeping the actual secret data unreadable outside the app.

## Core Promise

Fast access. Strong local security. Premium vault-like experience. No plaintext credential storage.

---

## 2. Locked Product Decisions

| Area | Decision |
|---|---|
| App type | Local-first password manager |
| Storage model | Encrypted vault file |
| Main unlock method | Master password |
| Cloud approach | Encrypted cloud-folder sync |
| Backup approach | Versioned encrypted backups |
| First platform | Windows desktop |
| Second platform | Android mobile |
| iOS | Later phase |
| UI style | Premium, minimal, motion-rich vault experience |
| UX priority | Fast daily use, no long cinematic delays |
| Implementation owner | Codex in this workspace |

---

## 3. Working System

This project uses three document types only:

```text
ROADMAP = long-term direction and phase order
PROGRESS = live tracking, decisions, risks, active task
Decision docs = vault, threat model, workflow, and review rules
```

No separate milestone tracking is needed.

Reason:

- PROGRESS already tracks actual work.
- ROADMAP already shows big phases.
- Decision docs are enough to control security-sensitive work.
- Extra ceremony slows the project.

## 3.1 Execution Workflow

```text
Codex = planning, implementation, verification, and document updates
User = product preference, local manual testing, and major decision approval
External reviewer = optional second eye for large/risky changes
```

Manual tests should stay short and focused.

---

## 4. Security Philosophy

The app must behave like a secure vault, not a notes app.

Non-negotiable principles:

- Master password is never stored.
- Plaintext passwords are never written to disk.
- Vault backups are encrypted.
- Clipboard is cleared automatically after a short duration.
- Sensitive fields are hidden by default.
- Crash logs and debug logs must never contain secrets.
- App auto-locks after inactivity or when required.
- Cloud providers only see encrypted vault files.

---

## 5. Sync Direction

Chosen direction: **encrypted cloud-folder sync**.

The app writes the encrypted vault file into a user-selected folder. That folder can be inside OneDrive, Google Drive, iCloud Drive, Dropbox, or another sync provider.

Example structure:

```text
/PasswordVault/
  vault.kdbx
  /backups/
    vault_2026-05-31_14-20.kdbx
    vault_2026-05-30_18-10.kdbx
```

Important behavior:

- The cloud folder is not trusted.
- The vault remains encrypted before reaching the cloud.
- If local data is lost, cloud backup can restore it.
- If master password/recovery key is lost, the vault cannot be recovered.
- Versioned backups protect against accidental overwrite, corruption, and sync mistakes.

---

## 6. UI / Motion Direction

## 6.1 UI Identity

The UI should feel like entering and operating a high-security private vault.

Target feeling:

- Secure
- Premium
- Fast
- Calm
- Precise
- Controlled
- Beautiful without becoming decorative noise

The style should be inspired by high-taste design engineering: minimal surfaces, excellent spacing, refined transitions, polished interaction states, and strong visual hierarchy.

## 6.2 Design Metaphor

The product should not feel like a generic SaaS dashboard. It should feel like a secure personal chamber.

Core metaphor:

```text
Unlock screen = secure access terminal
Main dashboard = inside the vault chamber
Credential list = protected archive shelves
Selected account = locked case retrieved from storage
Password reveal = controlled unsealing action
Clipboard countdown = temporary secure transfer
Auto-lock = chamber closing down
```

The metaphor must stay **abstract and premium**, not literal or cartoonish.

Avoid:

- Long 3D vault door animations
- Excessive glow
- Heavy glassmorphism
- Game-like effects
- Slow cinematic transitions during daily use
- Every click triggering dramatic motion

## 6.3 Motion Rules

Motion should make the app feel alive, secure, and premium, but never slow.

| Motion Type | Duration | Use Case |
|---|---:|---|
| Fast | 120–180 ms | Hover, focus, press |
| Standard | 180–280 ms | Select, reveal, panel update |
| Signature | 280–450 ms | Entry retrieval, secure drawer transition |
| Rare dramatic | 500–900 ms | First unlock, vault creation, restore success |

Hard limit:

```text
No regular interaction should exceed 900 ms.
```

---

## 7. MVP Scope

## 7.1 MVP Goal

Build a secure, premium-feeling Windows desktop password vault with local encrypted storage, basic account management, fast search, safe clipboard handling, and versioned backup support.

## 7.2 MVP Features

### Vault

- Create new vault
- Open existing vault
- Lock vault
- Auto-lock after inactivity
- Change master password
- Vault integrity check

### Account Entries

- Add account
- Edit account
- Delete account
- View account details
- Copy username/email
- Copy password
- Reveal/hide password
- Search accounts
- Filter by tag/category
- Favorite accounts

### Backup / Sync

- Select vault folder
- Save encrypted vault file
- Create timestamped encrypted backups
- Restore from backup
- Basic conflict detection
- Warning before overwriting newer vault

### Security UX

- Clipboard auto-clear
- Password hidden by default
- Dangerous action confirmation
- Recovery warning during vault creation
- No plaintext export in MVP unless explicitly added later with strong warning

### UI / Motion

- Premium unlock screen
- Main dashboard with sidebar/list/detail layout
- Entry retrieval motion
- Password reveal motion
- Copy confirmation and clipboard countdown
- Locked state animation

---

## 8. Out of Scope for MVP

These are intentionally excluded from the first version:

- Browser extension
- Browser autofill
- Web app
- Cloud account system
- Team/family vault
- Credential sharing
- Email-based recovery
- Automatic WhatsApp parsing/import
- Native OneDrive/Google Drive API integration
- iOS release
- Complex multi-device merge UI

Reason: these features increase security risk, scope, and maintenance cost. They should only be considered after the local vault and backup system are reliable.

---

## 9. Proposed Tech Direction

## 9.1 Platform

Recommended stack:

```text
.NET MAUI
Windows first
Android second
iOS later
```

Reason:

- The developer is already strong in C#.
- One codebase can target desktop and mobile.
- Windows-first development keeps scope controlled.

## 9.2 Vault Format

Target direction:

```text
KDBX / KeePass-compatible vault format
```

Reason:

- Password vault format is a security-sensitive area.
- Avoid inventing a custom crypto file format.
- Compatibility with an existing ecosystem is valuable.

Open technical validation:

```text
Spike a suitable .NET KDBX library.
Check license, maintenance status, MAUI compatibility, Android support, and security assumptions.
```

## 9.3 Cloud Sync

First implementation:

```text
Folder-based sync
```

The app does not directly integrate with cloud APIs in MVP. The user selects a folder. If that folder is synced by OneDrive, Google Drive, iCloud, or Dropbox, the encrypted vault is synced externally.

---

## 10. Data Model Draft

## 10.1 Account Entry

```text
AccountEntry
- Id
- ServiceName
- WebsiteUrl
- UsernameOrEmail
- Password
- Notes
- Tags
- Favorite
- CreatedAt
- UpdatedAt
- PasswordChangedAt
```

## 10.2 Vault Metadata

```text
VaultMetadata
- VaultVersion
- CreatedAt
- LastModifiedAt
- LastModifiedDevice
- BackupCount
- KdfSettings
- LastBackupAt
```

## 10.3 Backup Metadata

```text
BackupInfo
- FileName
- CreatedAt
- SourceVaultVersion
- SourceDevice
- IsAutoBackup
```

---

## 11. Screen List

## 11.1 Required MVP Screens

1. Welcome / Create Vault
2. Open Existing Vault
3. Unlock Vault
4. Recovery Key / Recovery Warning
5. Main Dashboard
6. Account Detail Panel
7. Add Account
8. Edit Account
9. Delete Confirmation
10. Search / Filter
11. Password Generator
12. Sync & Backup Settings
13. Restore Backup
14. Conflict Warning
15. Security Settings
16. Locked State

## 11.2 Main Dashboard Layout

```text
Left Sidebar:
- All Items
- Favorites
- Categories
- Tags
- Security Warnings
- Recently Updated

Center Panel:
- Search bar
- Account list
- Service icon/name
- Username preview
- Favorite indicator

Right Panel:
- Selected account details
- Copy username
- Copy password
- Reveal password
- Edit
- Last updated
- Tags
```

---

## 12. Phase Roadmap

## Phase 0 — Technical Validation

Goal: remove risky unknowns before building too much UI.

Tasks:

- [x] Define KDBX library acceptance criteria.
- [x] Build candidate matrix for vault implementation options.
- [x] Choose first candidate to test based on our criteria.
- [x] Run a direct KDBX feasibility spike.
- [x] Write threat model draft.
- [x] Define folder-based sync behavior.
- [x] Confirm project architecture boundary.

Exit criteria:

- Tech stack partially confirmed: .NET 10 SDK is available, MAUI workload still needs installation/verification.
- Vault library provisionally selected for app-created vaults.
- MVP scope frozen.
- Core screens listed.
- Implementation and review boundaries locked.

---

## Phase 1 — Local Vault Core

Goal: create and open a secure local vault.

Tasks:

- [ ] Create vault file.
- [ ] Unlock vault with master password.
- [ ] Lock vault.
- [ ] Save vault.
- [ ] Load vault.
- [ ] Add basic account entry.
- [ ] Edit account entry.
- [ ] Delete account entry.
- [ ] Search local entries.
- [ ] Ensure no plaintext file output.

Exit criteria:

- User can create a vault.
- User can add/edit/delete accounts.
- App can close and reopen the encrypted vault.

---

## Phase 2 — Premium Desktop UI

Goal: turn the core vault into a polished Windows desktop experience.

Tasks:

- [ ] Build unlock screen.
- [ ] Build main dashboard layout.
- [ ] Build sidebar.
- [ ] Build account list.
- [ ] Build account detail panel.
- [ ] Build add/edit account modal.
- [ ] Add reveal/hide password behavior.
- [ ] Add copy username/password actions.
- [ ] Add clipboard countdown UI.
- [ ] Add empty states.
- [ ] Add polished hover/press/focus states.

Exit criteria:

- App feels usable and visually coherent.
- Core CRUD is accessible through UI.
- Password copy/reveal flows feel safe and fast.

---

## Phase 3 — Motion & Vault Experience

Goal: add the signature premium vault feel without slowing usage.

Tasks:

- [ ] Add unlock transition.
- [ ] Add entry retrieval motion.
- [ ] Add password reveal motion.
- [ ] Add copy confirmation motion.
- [ ] Add auto-lock closing motion.
- [ ] Add subtle ambient locked-state motion.
- [ ] Add reduced-motion setting.
- [ ] Tune all timings under motion rules.

Exit criteria:

- Motion feels premium, controlled, and fast.
- Repeated daily use does not feel slow.
- Reduced-motion mode is available.

---

## Phase 4 — Backup & Cloud Folder Sync

Goal: protect the vault from device loss, corruption, and accidental overwrite.

Tasks:

- [ ] Let user select vault folder.
- [ ] Save vault to selected folder.
- [ ] Create timestamped backup before save/restore.
- [ ] Keep configurable backup count.
- [ ] Restore from backup.
- [ ] Detect newer vault file.
- [ ] Detect basic conflicts.
- [ ] Show conflict warning.
- [ ] Create conflict copy instead of overwriting.

Exit criteria:

- Local vault can be restored from encrypted backup.
- Vault can live inside cloud-synced folder.
- App does not silently overwrite newer/conflicting data.

---

## Phase 5 — Security Polish

Goal: harden risky user flows.

Tasks:

- [ ] Auto-lock after inactivity.
- [ ] Optional lock/mask on minimize.
- [ ] Clipboard auto-clear.
- [ ] Dangerous action confirmations.
- [ ] Recovery warning flow.
- [ ] Master password change flow.
- [ ] Password generator.
- [ ] Weak/reused password warnings.
- [ ] Validate logs for secret leakage.
- [ ] Validate crash behavior.

Exit criteria:

- App behaves safely during normal and edge-case usage.
- Sensitive data exposure is minimized.

---

## Phase 6 — Android Access

Goal: access the same encrypted vault from mobile.

Tasks:

- [ ] Adapt layout for mobile.
- [ ] Unlock existing vault.
- [ ] Search accounts.
- [ ] View account details.
- [ ] Copy username/password.
- [ ] Reveal/hide password.
- [ ] Connect to selected synced folder if feasible.
- [ ] Add mobile auto-lock behavior.
- [ ] Add biometric quick unlock only after secure design review.

Exit criteria:

- User can access and use vault from Android.
- Mobile experience remains fast and secure.

---

## Phase 7 — Import & Quality-of-Life

Goal: make migration and daily use smoother.

Tasks:

- [ ] CSV import.
- [ ] Manual structured import.
- [ ] Optional WhatsApp text helper flow.
- [ ] Tags management.
- [ ] Favorites polish.
- [ ] Recent copied/updated list.
- [ ] Better service icons.
- [ ] Duplicate account detection.

Exit criteria:

- User can migrate old credentials more easily.
- Daily usage is smoother and faster.
