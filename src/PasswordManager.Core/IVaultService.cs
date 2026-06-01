namespace PasswordManager.Core;

public interface IVaultService
{
    Task<VaultOperationResult> CreateAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult<VaultSnapshot>> LoadAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult> SaveAsync(
        string vaultPath,
        string masterPassword,
        VaultSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult> ChangeMasterPasswordAsync(
        string vaultPath,
        string currentMasterPassword,
        string newMasterPassword,
        VaultSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
