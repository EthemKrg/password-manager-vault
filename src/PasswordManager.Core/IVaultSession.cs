namespace PasswordManager.Core;

public interface IVaultSession
{
    VaultSessionState State { get; }

    string? VaultPath { get; }

    VaultSnapshot? CurrentSnapshot { get; }

    bool HasUnsavedChanges { get; }

    Task<VaultOperationResult> CreateAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult> UnlockAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default);

    Task<VaultOperationResult> UnlockCurrentAsync(
        string masterPassword,
        CancellationToken cancellationToken = default);

    VaultOperationResult<AccountEntry> AddEntry(AccountEntryDraft draft);

    VaultOperationResult AddEntry(AccountEntry entry);

    VaultOperationResult UpdateEntry(AccountEntry entry);

    VaultOperationResult DeleteEntry(Guid entryId);

    VaultOperationResult<IReadOnlyList<AccountEntry>> Search(VaultSearchQuery query);

    Task<VaultOperationResult> SaveAsync(CancellationToken cancellationToken = default);

    VaultOperationResult Lock(bool discardUnsavedChanges = false);

    VaultOperationResult Close(bool discardUnsavedChanges = false);
}
