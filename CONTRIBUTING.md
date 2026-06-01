# Contributing

This project is security-sensitive. Small, reviewable changes are preferred.

## Before Opening a Pull Request

- Keep changes scoped.
- Run `dotnet build PasswordManager.slnx`.
- Run `dotnet run --project tests\PasswordManager.Infrastructure.Validation\PasswordManager.Infrastructure.Validation.csproj`.
- Run `dotnet list PasswordManager.slnx package --include-transitive --vulnerable`.
- Do not commit `.kdbx`, `.key`, `.keyx`, real credentials, API keys, logs, crash dumps, or local app data.

## Security-Sensitive Changes

Changes touching these areas need extra care:

- Vault read/write.
- Master password handling.
- Clipboard.
- Backup/versioning.
- Sync/conflict handling.
- Logging.
- Recovery mechanisms.
- Dependency updates in cryptography or storage libraries.

Do not add custom cryptography or plaintext export behavior without a documented security decision.
