# Security Policy

## Security Status

Password Manager Vault is not security-audited and is not ready for real credentials.

Do not use current builds to store production passwords, financial accounts, recovery codes, API keys, private keys, or other real secrets.

## Supported Versions

There are no supported production versions yet.

| Version | Supported |
|---|---|
| main / early development | Security reports accepted, no production support |

## Reporting a Vulnerability

If you find a security issue, please do not publish exploit details publicly first.

Use GitHub private vulnerability reporting if it is enabled on the repository. If it is not enabled yet, open a minimal public issue that says you have a potential security report without including secrets, exploit payloads, or step-by-step abuse details.

Useful report contents:

- Affected commit or version.
- Impact.
- Reproduction steps using test data only.
- Whether plaintext secrets can be written to disk, logs, clipboard, crash reports, or sync folders.
- Whether the issue affects vault encryption, unlock, save, backup, sync, or master password handling.

## Security Boundaries

In scope:

- Vault storage behavior.
- Master password handling.
- Plaintext credential exposure.
- Logs and crash output.
- Clipboard behavior.
- Backup and sync overwrite behavior.
- Dependency vulnerabilities.

Out of scope for current early development:

- Production security guarantees.
- Protection against a fully compromised operating system.
- Protection against active keyloggers or screen recording malware.
- Claims of independent audit coverage.

## Project Rules

- Do not implement custom cryptography without explicit security review.
- Do not store the master password.
- Do not write plaintext credentials to disk.
- Do not log secrets.
- Do not add unreviewed recovery/backdoor behavior.
- Do not silently overwrite newer or conflicting vault files.
- Keep KDBX/library-specific implementation details out of Core and UI layers.
