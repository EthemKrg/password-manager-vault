namespace PasswordManager.Core;

public interface IVaultBackupService
{
    Task<VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>> ListArtifactsAsync(
        string vaultPath,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult<VaultBackupArtifact>> CreateBackupAsync(
        string vaultPath,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult<VaultBackupArtifact>> CreateConflictCopyAsync(
        string vaultPath,
        string masterPassword,
        VaultSnapshot snapshot,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult> RestoreBackupAsync(
        string vaultPath,
        string backupPath,
        string masterPassword,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default);
}
