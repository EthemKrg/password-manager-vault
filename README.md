# Password Manager Vault

Password Manager Vault is an early-stage, local-first personal password manager for Windows first and Android later.

The project goal is to store account credentials in an encrypted vault file, keep cloud providers outside the trust boundary, and build a careful desktop/mobile experience around fast daily use.

## Security Status

This project is **not security-audited** and is **not ready for real credentials**.

Do not use this application to store production passwords, financial accounts, recovery codes, API keys, private keys, or other real secrets yet.

Current security posture:

- Vault storage uses KDBX/KeePass-compatible encrypted files as the target direction.
- `DgNet.Keepass 0.2.0` is the provisional implementation candidate.
- The current implementation has passed local create/save/load/wrong-password validation.
- Official KeePass and KeePassXC interoperability still needs more validation before release.
- No custom cryptographic file format is allowed without explicit security review.

Security goals:

- Master password is never stored.
- Plaintext credentials are never written to disk.
- Logs and crash output must not contain secrets.
- Clipboard use must be explicit and auto-cleared.
- Passwords are hidden by default.
- Sync and backup must never silently overwrite newer/conflicting vault data.

## Current State

Implemented:

- Core domain models and vault service contract.
- Infrastructure vault service using `DgNet.Keepass`.
- Validation console for create/save/load/wrong-password behavior.
- Public-repo security docs and CI configuration.

Not implemented yet:

- MAUI Windows app shell.
- Android app.
- Clipboard handling.
- Backup/versioning.
- Conflict detection.
- Auto-lock.
- Full UI.
- Independent security audit.

## Repository Layout

```text
PasswordManager.slnx
src/
  PasswordManager.Core/
  PasswordManager.Infrastructure/
tests/
  PasswordManager.Infrastructure.Validation/
spikes/
  DgNetKdbxValidation/
  KdbxInteropValidation/
  KdbxValidation/
```

`src/PasswordManager.Core` contains domain models and interfaces only.

`src/PasswordManager.Infrastructure` contains library-specific implementation code. KDBX/DgNet types must not leak into Core or future UI layers.

`tests/PasswordManager.Infrastructure.Validation` is a focused executable validation project.

`spikes/` contains technical experiments. Spike code is not product code.

## Build

Prerequisite:

- .NET SDK 10.x

Build:

```powershell
dotnet build PasswordManager.slnx
```

Run the current validation:

```powershell
dotnet run --project tests\PasswordManager.Infrastructure.Validation\PasswordManager.Infrastructure.Validation.csproj
```

Check for vulnerable packages:

```powershell
dotnet list PasswordManager.slnx package --include-transitive --vulnerable
```

## Project Documents

- [ROADMAP.md](ROADMAP.md) - product direction, MVP scope, phases.
- [PROGRESS.md](PROGRESS.md) - current status and next work.
- [VAULT_DECISION.md](VAULT_DECISION.md) - KDBX/library decision record.
- [THREAT_MODEL_DRAFT.md](THREAT_MODEL_DRAFT.md) - initial threat model.
- [REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md) - security and architecture review checklist.
- [WORKFLOW_RULES.md](WORKFLOW_RULES.md) - implementation workflow rules.

## Contributing

Security-sensitive changes should be small, reviewable, and backed by tests or validation steps.

Do not add:

- Custom cryptography.
- Plaintext credential storage.
- Secret logging.
- Unreviewed recovery mechanisms.
- Silent sync overwrite behavior.
- Broad UI work that bypasses vault/security decisions.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE).
