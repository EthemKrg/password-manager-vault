namespace PasswordManager.Core;

public interface IVaultBackupService
{
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
}
