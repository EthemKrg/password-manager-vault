# Password Manager — PROGRESS

## 1. Current Status

| Area | Status |
|---|---|
| Product idea | Locked |
| Security philosophy | Locked |
| Sync direction | Locked |
| Backup direction | Locked |
| UI taste direction | Locked |
| Motion direction | Locked |
| First platform | Locked: Windows |
| Second platform | Locked: Android |
| MVP boundaries | Mostly locked |
| Tracking style | Roadmap + live progress + decision docs |
| KDBX library | Provisional: DgNet.Keepass 0.2.0 |
| Threat model | Draft created |
| UI wireframes | Not started |
| App implementation | Started: Core + Infrastructure skeleton |

---

## 2. Tracking Rule

This project uses:

```text
ROADMAP.md = big direction and phases
PROGRESS.md = live state, decisions, risks, active work
Decision docs = security, vault, workflow, and review rules
```

No separate milestone or mandatory task document is needed.

Reason:

- PROGRESS is already the living tracker.
- ROADMAP already contains the big phase order.
- Security-sensitive decisions are already controlled by decision docs.
- Extra task-document layers create noise.

---

## 3. Locked Decisions

- [x] This is a password vault, not a notes/list app.
- [x] Local-first is mandatory.
- [x] Cloud sync stores only encrypted vault files.
- [x] Versioned encrypted backups are required.
- [x] Master password is never stored.
- [x] Master password loss means vault cannot be recovered unless recovery-key design is implemented.
- [x] Windows desktop comes first.
- [x] Android comes second.
- [x] iOS is later.
- [x] UI should feel premium, secure, and motion-rich.
- [x] Motion must be fast and not block daily use.
- [x] Browser extension and autofill are out of MVP.
- [x] Codex owns implementation in this workspace.
- [x] Decision authority stays in project docs and user-approved major changes.
- [x] Milestone terminology is removed from tracking.
- [x] Use Phase for roadmap and PROGRESS for executable work.
- [x] Small fixes are handled directly.
- [x] Manual tests are performed locally with concise steps.
- [x] Efficiency is part of the workflow standard.
- [x] DgNet.Keepass 0.2.0 is the provisional KDBX implementation candidate.
- [x] KDBX library-specific code must stay inside PasswordManager.Infrastructure.

---

## Development Workflow

Locked workflow:

```text
Planning and decisions: Codex + User through project Markdown
Implementation: Codex directly in this workspace
Second-eye review: Optional for large/risky work
Small fixes: Codex handles directly
Manual testing: User performs it when required
Efficiency: Required
```

Task routing:

```text
Small fix -> Direct edit.
Medium task -> Implement directly and verify.
Large/risky task -> Record decision, implement in small steps, optionally use second-eye review.
```

Manual testing stays short and local.

---

## 4. Open Questions

These must be resolved before or during Phase 0.

## 4.1 Technical

- [x] Which KDBX library will be used? Provisional: DgNet.Keepass 0.2.0.
- [x] Is the library license compatible with the intended project use? DgNet.Keepass is MIT.
- [ ] Does the library work well with .NET MAUI on Windows and Android?
- [ ] Do we need a fallback custom encrypted vault format if KDBX library options are unsuitable?
- [ ] How will vault metadata be stored without weakening KDBX compatibility?
- [ ] What is the minimum supported Windows version?
- [ ] What is the minimum supported Android version?

## 4.2 Product

- [ ] Will the app support recovery key in MVP or only show unrecoverable warning?
- [ ] How many backups should be kept by default?
- [ ] Should lock-on-minimize be enabled by default?
- [ ] Clipboard clear duration: 20s, 30s, or configurable?
- [ ] Should password reveal auto-hide after a duration?
- [ ] Should backup restore require re-entering master password?

## 4.3 UI / Motion

- [ ] Final color palette.
- [ ] Typography choice.
- [ ] Icon style.
- [ ] Entry retrieval motion prototype.
- [ ] Unlock transition prototype.
- [ ] Reduced-motion behavior.

---

## 5. Active Work

## Phase 1 Local Vault Core

Status:

```text
Started
```

Goal:

```text
Build the first replaceable local vault core behind Core interfaces and Infrastructure implementation.
```

Execution rule:

```text
Keep Core free of DgNet/KDBX-specific types.
Keep save/load behavior narrow until backup/conflict rules are implemented.
```

Input docs:

Current implementation:

- PasswordManager.slnx
- src/PasswordManager.Core
- src/PasswordManager.Infrastructure
- tests/PasswordManager.Infrastructure.Validation

Validation passed:

- `dotnet build PasswordManager.slnx`
- `dotnet run --project tests\PasswordManager.Infrastructure.Validation\PasswordManager.Infrastructure.Validation.csproj`
- `dotnet list PasswordManager.slnx package --include-transitive --vulnerable`

Known constraints:

- Local environment has .NET SDK 10.0.300 but no MAUI workload installed.
- DgNet.Keepass is provisional; official KeePass/KeePassXC interoperability still needs validation before release.
- Favorite metadata is not in the first Core model yet; it needs a clean KDBX metadata strategy before implementation.

---

## 6. Next Work

Immediate next work:

1. Add focused automated tests around DgNetVaultService.
2. Add update/delete/search behavior to the Core vault service contract.
3. Add safe save behavior placeholders for backup/conflict handling.
4. Install/verify MAUI workload before creating the Windows desktop app shell.
5. Validate DgNet vault files with official KeePass/KeePassXC before release.

Potential next implementation steps:

```text
Update/delete/search
Backup-safe save boundary
Desktop MAUI shell
Unlock/create vault flow
```

Do not build broad UI before local vault behavior is stable.

---

## 7. Quality Bar

The project should not move forward if it violates any of these:

- Credentials are saved as plaintext anywhere.
- UI becomes slow due to animations.
- Sync can silently overwrite newer data.
- Logs can leak secrets.
- Security behavior is unclear to the user.
- App feels like a generic CRUD admin panel.
- App feels like a toy/game instead of a trusted premium vault.
- Implementation changes architecture or security direction without updating project docs.

---

## 8. Current Project Stage

```text
Stage: Phase 1 local vault core started
Confidence: Good for app-created vaults
Main risk: DgNet compatibility with official KeePass/KeePassXC files
Active work: Local vault core
```

---

## 9. Working Rule

Use ROADMAP.md and this PROGRESS.md as the source of truth.

Decision authority stays in the project docs and with explicit user approval for major direction changes.

## Implementation Rule

Codex may:

- Implement documented direction.
- Report build/runtime results.
- Surface risks.
- Recommend changes.

Codex must not:

- Change product direction.
- Change security strategy.
- Change architecture boundaries.
- Change vault format.
- Change sync strategy.
- Add out-of-scope features.
- Implement custom crypto unless explicitly approved.
- Store secrets in plaintext for convenience.

When decisions change:

- Update the relevant ROADMAP section.
- Update PROGRESS status.
- Move resolved open questions into locked decisions.
- Do not start implementation work that contradicts the security checklist.

---

## 10. Progress Log

### 2026-05-31

- Project definition created.
- Local-first password vault direction locked.
- Encrypted cloud-folder sync selected.
- Versioned backups selected.
- Windows-first, Android-second platform order selected.
- Premium vault-like UI/motion direction selected.
- External executor/reviewer workflow originally documented.
- Milestone terminology removed from tracking.
- Phase + Task system selected.
- Active task originally set to TASK_001_KDBX_SPIKE.
- Workflow rule originally locked around external Builder/Reviewer roles.

### 2026-06-01

- Workflow changed: Codex owns implementation directly in this workspace.
- Separate TASK_001_KDBX_SPIKE.md is no longer required.
- Active work changed to direct KDBX validation.
- Project documents updated to remove DeepSeek as the default implementation path.
- KDBX candidates checked through NuGet and local spikes.
- DgNet.Keepass 0.2.0 selected as provisional implementation candidate because it is MIT licensed and passed basic vault behavior checks.
- KeePassLib.Standard 2.57.1 passed basic behavior checks but remains a fallback/comparison candidate because of GPL-2.0-or-later license risk.
- Interop spike found DgNet KDBX3/AES-KDF output can be read by KeePassLib.Standard, but DgNet failed to read KeePassLib.Standard default output.
- Created PasswordManager.slnx with Core, Infrastructure, and Infrastructure.Validation projects.
- Implemented initial DgNetVaultService behind IVaultService.
- Verified solution build, infrastructure validation run, and vulnerable package scan.
