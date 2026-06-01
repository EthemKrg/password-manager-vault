namespace PasswordManager.Core;

public sealed record VaultBackupArtifact(
    string FilePath,
    VaultBackupArtifactKind Kind,
    DateTimeOffset CreatedAtUtc,
    string? SourceFingerprint);
