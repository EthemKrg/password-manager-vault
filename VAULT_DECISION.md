# Password Manager — VAULT_DECISION

## 1. Purpose

This document controls the vault technology decision for the Password Manager project.

This project is security-sensitive. The vault layer is the heart of the product. UI, sync, and motion must not be implemented before the vault approach is technically validated.

---

## 2. Decision Authority

Decision authority stays in the project documents and with explicit user approval for major security/product changes.

Codex may implement and verify the selected direction, but must not silently change the vault format, architecture boundary, security strategy, or sync strategy.

Codex may:

- Implement the documented direction.
- Run technical validation.
- Report factual results.
- Surface risks.
- Recommend changes with supporting evidence.

Codex must not:

- Choose a different vault format.
- Invent custom encryption.
- Move secrets into JSON, XML, SQLite, logs, or app settings.
- Add out-of-scope features that weaken Phase 0 focus.
- Hide license or compatibility risks.

---

## 3. Current Vault Direction

Preferred vault direction:

```text
KDBX / KeePass-compatible encrypted vault file
```

Reason:

- Password vault format is a high-risk area.
- We do not want to invent our own crypto file format.
- KDBX is designed for storing credential entries.
- KDBX gives us a known vault-style storage model.
- Encrypted vault files fit our local-first + cloud-folder sync strategy.

This is not yet a final implementation decision. It is the selected direction for Phase 0 task validation.

---

## 4. Library Acceptance Criteria

A vault library can only be accepted if it passes these criteria.

## 4.1 Required

- [ ] Supports KDBX read.
- [ ] Supports KDBX write.
- [ ] Can create a new encrypted vault.
- [ ] Can open an existing encrypted vault with master password.
- [ ] Can add/update/delete credential entries.
- [ ] Can save and reopen the vault without data loss.
- [ ] Does not require plaintext credentials to be written to disk.
- [ ] Does not require storing the master password.
- [ ] Works in a Windows .NET MAUI app.
- [ ] Has a plausible path to Android .NET MAUI support.
- [ ] License is compatible with intended usage.
- [ ] Does not force weak/placeholder encryption.
- [ ] Allows integrity failure/corruption handling to be detected.

## 4.2 Strongly Preferred

- [ ] Maintained or at least stable enough for production evaluation.
- [ ] Has clear examples or readable source.
- [ ] Supports modern KDBX versions.
- [ ] Has testable APIs.
- [ ] Does not require UI-thread operations.
- [ ] Does not rely on Windows-only APIs if Android support is needed.
- [ ] Allows clean infrastructure-layer isolation.

## 4.3 Rejection Triggers

Reject the library if:

- It cannot write KDBX files.
- It only reads KDBX.
- It stores secrets in plaintext.
- It logs secrets.
- It requires master password persistence.
- It is Windows-only with no Android path.
- License is incompatible with our intended usage.
- API is too unstable or opaque to safely build around.
- It forces us into messy UI-layer coupling.
- It requires large unsafe hacks to work inside .NET MAUI.

---

## 5. Candidate Matrix

This is the initial candidate matrix. Live package/license details must be verified during direct KDBX validation.

| Candidate | Direction | Status | Notes |
|---|---|---|---|
| KDBX format | Preferred format | Selected for task direction | Format-level decision, not library-level final decision |
| DgNet.Keepass | Provisional implementation candidate | Investigate further / usable for app-created vaults | MIT, net6-net10, read/write passed, low adoption, interop needs more validation |
| KeePassLib.Standard | Fallback / comparison candidate | Technically works, license risk | GPL-2.0-or-later, read/write passed, official KeePassLib-style API, copyleft risk |
| Official KeePass library/package | Secondary check | Risky until verified | May be desktop/.NET Framework oriented |
| Other maintained .NET KDBX libraries | Research if needed | Open | Only consider if they meet criteria |
| Custom encrypted JSON | Last resort | Avoid | High responsibility, custom crypto risk |
| SQLite + custom encryption | Last resort | Avoid | Key management and crypto burden move to us |

---

## 6. Selected Candidate for First Validation

Current provisional implementation candidate:

```text
DgNet.Keepass
```

Reason:

- MIT license avoids the GPL copyleft risk of KeePassLib.Standard.
- Targets modern .NET versions including net10.0.
- Has a small, direct API for create/open/save/search operations.
- Basic create/save/reopen/wrong-password/corruption/plaintext scan validation passed.

Important:

```text
This is not final approval.
Direct validation must still verify Android path, official KeePass/KeePassXC interoperability, and maintenance/security risk.
```

KeePassLib.Standard remains useful as a comparison/fallback candidate, but its GPL-2.0-or-later license is a major product constraint unless this app is intentionally distributed under a compatible license.

---

## 6.1 Validation Results — 2026-06-01

Local environment:

```text
.NET SDK: 10.0.300
MAUI workload: not installed
OS: Windows
```

Validated packages:

| Package | Version | License | Result | Notes |
|---|---:|---|---|---|
| DgNet.Keepass | 0.2.0 | MIT | Basic behavior passed | Create, save, reopen, wrong password, corruption failure, plaintext scan all passed |
| KeePassLib.Standard | 2.57.1 | GPL-2.0-or-later | Basic behavior passed | Needed direct `System.Security.Cryptography.Xml` 10.0.8 override to remove vulnerability warnings |

Security/vulnerability checks:

- `dotnet list package --include-transitive --vulnerable` reported no vulnerable packages for the DgNet.Keepass spike.
- KeePassLib.Standard initially pulled vulnerable `System.Security.Cryptography.Xml 8.0.1`; adding direct `System.Security.Cryptography.Xml 10.0.8` removed the vulnerability report in the spike.

Behavior checks passed for both package spikes:

- New encrypted vault file can be created.
- Test entry can be added.
- Vault can be saved.
- Vault can be reopened with the correct master password.
- Wrong master password fails.
- Corrupted vault fails to open.
- Test credential strings were not visible in a UTF-8 scan of the `.kdbx` output file.
- No plaintext sidecar files were created by the spike.

Interop check:

| Direction | Result | Notes |
|---|---|---|
| DgNet.Keepass configured as KDBX3/AES-KDF -> KeePassLib.Standard read | Passed | Useful compatibility mode for app-created vaults |
| KeePassLib.Standard default write -> DgNet.Keepass read | Failed | DgNet reported an unknown KDF UUID |

Decision:

```text
Proceed with DgNet.Keepass only as a provisional Infrastructure-layer implementation for app-created vaults.
Do not expose DgNet types to Core or UI.
Before release, test with official KeePass and KeePassXC files.
If existing KeePass vault import/open support becomes MVP-critical, revisit the library decision.
```

---

## 7. Fallback Policy

If the first candidate fails, do not jump directly into custom crypto.

Fallback order:

1. Try another maintained KDBX-compatible .NET library.
2. Consider wrapping platform-specific/native KeePass-compatible tooling only if safe and practical.
3. Consider alternative established encrypted vault formats only if they are well documented and safely implementable.
4. Consider custom encrypted vault format only as a last resort, after explicit approval and separate security design review.

Custom crypto/file format is not allowed by default.

---

## 8. Required Validation Result

The validation must prove:

- A new vault can be created.
- A test entry can be added.
- The vault can be saved.
- The app can close/reopen the vault.
- The test entry still exists after reopen.
- Wrong master password fails.
- Plaintext credentials are not visible in output files.
- No secrets are written to logs.
- The code can be isolated in Infrastructure layer.
- App/Core layer does not depend directly on library-specific types.

---

## 9. Proposed Architecture Boundary

Use library-specific code only inside:

```text
PasswordManager.Infrastructure
```

Core should expose interfaces and domain models only.

Example boundary:

```text
PasswordManager.Core
- AccountEntry
- AccountEntryDraft
- IVaultService
- VaultUnlockResult
- VaultError
- VaultState

PasswordManager.Infrastructure
- KdbxVaultService
- KdbxVaultMapper
- FileVaultStorage
```

Do not leak KDBX/library-specific classes into UI or Core.

---

## 10. Review Questions

After KDBX validation, review these:

- Was the validation narrow and factual?
- Was any decision changed without updating this document?
- Was custom crypto avoided?
- Were plaintext credentials avoided?
- Was the master password never stored or logged?
- Can KDBX code be isolated in Infrastructure?
- Does the code build on Windows?
- Is Android support plausible?
- Is the license acceptable?
- Does wrong password fail safely?
- Does file corruption/integrity failure produce a manageable error?
- Does the implementation look maintainable?
- Was the solution appropriately scoped?

---

## 11. Decision Outcomes

After validation review, choose one:

## Proceed

Criteria:

- Library works.
- License is acceptable.
- Architecture is clean.
- Windows build works.
- Android path is plausible.
- No security rule was violated.

## Investigate Further

Criteria:

- Library works partially.
- License is unclear.
- Android support is uncertain.
- API is awkward but maybe manageable.

## Reject

Criteria:

- Security issue.
- License issue.
- No Android path.
- Cannot write KDBX.
- Secrets leak.
- Architecture becomes messy.
- Too many hacks needed.
