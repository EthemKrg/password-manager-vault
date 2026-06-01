namespace PasswordManager.Core;

public sealed class VaultSession : IVaultSession
{
    private readonly IVaultService _vaultService;
    private readonly TimeProvider _timeProvider;
    private readonly IVaultBackupService? _vaultBackupService;
    private readonly VaultBackupOptions _vaultBackupOptions;
    private string? _masterPassword;

    public VaultSession(
        IVaultService vaultService,
        TimeProvider? timeProvider = null,
        IVaultBackupService? vaultBackupService = null,
        VaultBackupOptions? vaultBackupOptions = null)
    {
        _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _vaultBackupService = vaultBackupService;
        _vaultBackupOptions = vaultBackupOptions ?? new VaultBackupOptions();
    }

    public VaultSessionState State { get; private set; } = VaultSessionState.NoVaultLoaded;

    public string? VaultPath { get; private set; }

    public VaultSnapshot? CurrentSnapshot { get; private set; }

    public bool HasUnsavedChanges { get; private set; }

    public async Task<VaultOperationResult> CreateAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        var replacementCheck = EnsureCanReplaceCurrentSession();
        if (replacementCheck is not null)
        {
            return replacementCheck;
        }

        var createResult = await _vaultService.CreateAsync(vaultPath, masterPassword, cancellationToken);
        if (!createResult.Succeeded)
        {
            return createResult;
        }

        var loadResult = await _vaultService.LoadAsync(vaultPath, masterPassword, cancellationToken);
        if (!loadResult.Succeeded)
        {
            return VaultOperationResult.Failure(loadResult.Error, loadResult.Message);
        }

        SetUnlocked(vaultPath, masterPassword, loadResult.Value!);
        return VaultOperationResult.Success();
    }

    public async Task<VaultOperationResult> UnlockAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        var replacementCheck = EnsureCanReplaceCurrentSession();
        if (replacementCheck is not null)
        {
            return replacementCheck;
        }

        return await LoadIntoSessionAsync(vaultPath, masterPassword, cancellationToken);
    }

    public async Task<VaultOperationResult> UnlockCurrentAsync(
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        if (State == VaultSessionState.Unlocked)
        {
            return VaultOperationResult.Success();
        }

        if (string.IsNullOrWhiteSpace(VaultPath))
        {
            return VaultOperationResult.Failure(VaultError.NoVaultLoaded);
        }

        return await LoadIntoSessionAsync(VaultPath, masterPassword, cancellationToken);
    }

    public VaultOperationResult<AccountEntry> AddEntry(AccountEntryDraft draft)
    {
        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return VaultOperationResult<AccountEntry>.Failure(stateCheck.Error, stateCheck.Message);
        }

        try
        {
            var entry = AccountEntry.Create(draft, _timeProvider.GetUtcNow());
            var addResult = AddEntryToCurrentSnapshot(entry);
            return addResult.Succeeded
                ? VaultOperationResult<AccountEntry>.Success(entry)
                : VaultOperationResult<AccountEntry>.Failure(addResult.Error, addResult.Message);
        }
        catch (ArgumentException ex)
        {
            return VaultOperationResult<AccountEntry>.Failure(VaultError.InvalidEntry, ex.GetType().Name);
        }
    }

    public VaultOperationResult AddEntry(AccountEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return stateCheck;
        }

        return AddEntryToCurrentSnapshot(entry);
    }

    public VaultOperationResult UpdateEntry(AccountEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return stateCheck;
        }

        try
        {
            var existingEntry = CurrentSnapshot!.Entries.SingleOrDefault(existing => existing.Id == entry.Id);
            if (existingEntry is null)
            {
                return VaultOperationResult.Failure(VaultError.EntryNotFound, nameof(KeyNotFoundException));
            }

            var updatedAtUtc = LaterOf(_timeProvider.GetUtcNow(), existingEntry.UpdatedAtUtc);
            var passwordChangedAtUtc = string.Equals(
                existingEntry.Password,
                entry.Password,
                StringComparison.Ordinal)
                ? existingEntry.PasswordChangedAtUtc
                : updatedAtUtc;
            var updatedEntry = new AccountEntry(
                entry.Id,
                entry.ServiceName,
                entry.WebsiteUrl,
                entry.UsernameOrEmail,
                entry.Password,
                entry.Notes,
                entry.Tags,
                entry.IsFavorite,
                existingEntry.CreatedAtUtc,
                updatedAtUtc,
                passwordChangedAtUtc);

            CurrentSnapshot = CurrentSnapshot.Update(updatedEntry);
            HasUnsavedChanges = true;
            return VaultOperationResult.Success();
        }
        catch (KeyNotFoundException ex)
        {
            return VaultOperationResult.Failure(VaultError.EntryNotFound, ex.GetType().Name);
        }
        catch (ArgumentException ex)
        {
            return VaultOperationResult.Failure(VaultError.InvalidEntry, ex.GetType().Name);
        }
    }

    public VaultOperationResult DeleteEntry(Guid entryId)
    {
        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return stateCheck;
        }

        try
        {
            CurrentSnapshot = CurrentSnapshot!.Delete(entryId);
            HasUnsavedChanges = true;
            return VaultOperationResult.Success();
        }
        catch (KeyNotFoundException ex)
        {
            return VaultOperationResult.Failure(VaultError.EntryNotFound, ex.GetType().Name);
        }
    }

    public VaultOperationResult<IReadOnlyList<AccountEntry>> Search(VaultSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return VaultOperationResult<IReadOnlyList<AccountEntry>>.Failure(
                stateCheck.Error,
                stateCheck.Message);
        }

        return VaultOperationResult<IReadOnlyList<AccountEntry>>.Success(CurrentSnapshot!.Search(query));
    }

    public async Task<VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>> ListBackupArtifactsAsync(
        CancellationToken cancellationToken = default)
    {
        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Failure(
                stateCheck.Error,
                stateCheck.Message);
        }

        if (_vaultBackupService is null)
        {
            return VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Failure(VaultError.BackupFailed);
        }

        return await _vaultBackupService.ListArtifactsAsync(
            VaultPath!,
            _vaultBackupOptions,
            cancellationToken);
    }

    public async Task<VaultOperationResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return stateCheck;
        }

        if (_vaultBackupService is not null)
        {
            var backupResult = await _vaultBackupService.CreateBackupAsync(
                VaultPath!,
                _vaultBackupOptions,
                cancellationToken);
            if (!backupResult.Succeeded)
            {
                return VaultOperationResult.Failure(backupResult.Error, backupResult.Message);
            }
        }

        var saveResult = await _vaultService.SaveAsync(
            VaultPath!,
            _masterPassword!,
            CurrentSnapshot!,
            cancellationToken);
        if (!saveResult.Succeeded)
        {
            if (saveResult.Error == VaultError.StaleVaultSnapshot && _vaultBackupService is not null)
            {
                var conflictCopyResult = await _vaultBackupService.CreateConflictCopyAsync(
                    VaultPath!,
                    _masterPassword!,
                    CurrentSnapshot!,
                    _vaultBackupOptions,
                    cancellationToken);
                if (!conflictCopyResult.Succeeded)
                {
                    return VaultOperationResult.Failure(conflictCopyResult.Error, conflictCopyResult.Message);
                }
            }

            return saveResult;
        }

        var reloadResult = await _vaultService.LoadAsync(VaultPath!, _masterPassword!, cancellationToken);
        if (!reloadResult.Succeeded)
        {
            return VaultOperationResult.Failure(reloadResult.Error, reloadResult.Message);
        }

        CurrentSnapshot = reloadResult.Value!;
        HasUnsavedChanges = false;
        return VaultOperationResult.Success();
    }

    public async Task<VaultOperationResult> RestoreBackupAsync(
        string backupPath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return stateCheck;
        }

        if (HasUnsavedChanges)
        {
            return VaultOperationResult.Failure(VaultError.UnsavedChanges);
        }

        if (_vaultBackupService is null)
        {
            return VaultOperationResult.Failure(VaultError.BackupFailed);
        }

        var restoreResult = await _vaultBackupService.RestoreBackupAsync(
            VaultPath!,
            backupPath,
            masterPassword,
            _vaultBackupOptions,
            cancellationToken);
        if (!restoreResult.Succeeded)
        {
            return restoreResult;
        }

        var reloadResult = await _vaultService.LoadAsync(VaultPath!, masterPassword, cancellationToken);
        if (!reloadResult.Succeeded)
        {
            return VaultOperationResult.Failure(reloadResult.Error, reloadResult.Message);
        }

        SetUnlocked(VaultPath!, masterPassword, reloadResult.Value!);
        return VaultOperationResult.Success();
    }

    public async Task<VaultOperationResult> ChangeMasterPasswordAsync(
        string currentMasterPassword,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        var stateCheck = EnsureUnlocked();
        if (stateCheck is not null)
        {
            return stateCheck;
        }

        if (HasUnsavedChanges)
        {
            return VaultOperationResult.Failure(VaultError.UnsavedChanges);
        }

        if (string.IsNullOrWhiteSpace(currentMasterPassword)
            || string.IsNullOrWhiteSpace(newMasterPassword)
            || string.Equals(currentMasterPassword, newMasterPassword, StringComparison.Ordinal))
        {
            return VaultOperationResult.Failure(VaultError.InvalidMasterPassword);
        }

        var currentLoadResult = await _vaultService.LoadAsync(
            VaultPath!,
            currentMasterPassword,
            cancellationToken);
        if (!currentLoadResult.Succeeded)
        {
            return VaultOperationResult.Failure(currentLoadResult.Error, currentLoadResult.Message);
        }

        if (!string.Equals(
                currentLoadResult.Value!.SourceFingerprint,
                CurrentSnapshot!.SourceFingerprint,
                StringComparison.Ordinal))
        {
            return VaultOperationResult.Failure(
                VaultError.StaleVaultSnapshot,
                "Vault changed on disk before master password change.");
        }

        if (_vaultBackupService is not null)
        {
            var backupResult = await _vaultBackupService.CreateBackupAsync(
                VaultPath!,
                _vaultBackupOptions,
                cancellationToken);
            if (!backupResult.Succeeded)
            {
                return VaultOperationResult.Failure(backupResult.Error, backupResult.Message);
            }
        }

        var changeResult = await _vaultService.ChangeMasterPasswordAsync(
            VaultPath!,
            currentMasterPassword,
            newMasterPassword,
            CurrentSnapshot!,
            cancellationToken);
        if (!changeResult.Succeeded)
        {
            return changeResult;
        }

        var reloadResult = await _vaultService.LoadAsync(VaultPath!, newMasterPassword, cancellationToken);
        if (!reloadResult.Succeeded)
        {
            return VaultOperationResult.Failure(reloadResult.Error, reloadResult.Message);
        }

        SetUnlocked(VaultPath!, newMasterPassword, reloadResult.Value!);
        return VaultOperationResult.Success();
    }

    private VaultOperationResult AddEntryToCurrentSnapshot(AccountEntry entry)
    {
        try
        {
            CurrentSnapshot = CurrentSnapshot!.Add(entry);
            HasUnsavedChanges = true;
            return VaultOperationResult.Success();
        }
        catch (InvalidOperationException ex)
        {
            return VaultOperationResult.Failure(VaultError.EntryAlreadyExists, ex.GetType().Name);
        }
        catch (ArgumentException ex)
        {
            return VaultOperationResult.Failure(VaultError.InvalidEntry, ex.GetType().Name);
        }
    }

    public VaultOperationResult Lock(bool discardUnsavedChanges = false)
    {
        var dirtyCheck = EnsureCanDiscardUnsavedChanges(discardUnsavedChanges);
        if (dirtyCheck is not null)
        {
            return dirtyCheck;
        }

        CurrentSnapshot = null;
        _masterPassword = null;
        HasUnsavedChanges = false;
        State = string.IsNullOrWhiteSpace(VaultPath)
            ? VaultSessionState.NoVaultLoaded
            : VaultSessionState.Locked;

        return VaultOperationResult.Success();
    }

    public VaultOperationResult Close(bool discardUnsavedChanges = false)
    {
        var dirtyCheck = EnsureCanDiscardUnsavedChanges(discardUnsavedChanges);
        if (dirtyCheck is not null)
        {
            return dirtyCheck;
        }

        CurrentSnapshot = null;
        VaultPath = null;
        _masterPassword = null;
        HasUnsavedChanges = false;
        State = VaultSessionState.NoVaultLoaded;

        return VaultOperationResult.Success();
    }

    private async Task<VaultOperationResult> LoadIntoSessionAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken)
    {
        var loadResult = await _vaultService.LoadAsync(vaultPath, masterPassword, cancellationToken);
        if (!loadResult.Succeeded)
        {
            return VaultOperationResult.Failure(loadResult.Error, loadResult.Message);
        }

        SetUnlocked(vaultPath, masterPassword, loadResult.Value!);
        return VaultOperationResult.Success();
    }

    private void SetUnlocked(string vaultPath, string masterPassword, VaultSnapshot snapshot)
    {
        VaultPath = vaultPath;
        _masterPassword = masterPassword;
        CurrentSnapshot = snapshot;
        HasUnsavedChanges = false;
        State = VaultSessionState.Unlocked;
    }

    private VaultOperationResult? EnsureUnlocked()
    {
        return State switch
        {
            VaultSessionState.Unlocked when CurrentSnapshot is not null => null,
            VaultSessionState.Locked => VaultOperationResult.Failure(VaultError.VaultLocked),
            _ => VaultOperationResult.Failure(VaultError.NoVaultLoaded)
        };
    }

    private VaultOperationResult? EnsureCanReplaceCurrentSession()
    {
        return HasUnsavedChanges
            ? VaultOperationResult.Failure(VaultError.UnsavedChanges)
            : null;
    }

    private VaultOperationResult? EnsureCanDiscardUnsavedChanges(bool discardUnsavedChanges)
    {
        return HasUnsavedChanges && !discardUnsavedChanges
            ? VaultOperationResult.Failure(VaultError.UnsavedChanges)
            : null;
    }

    private static DateTimeOffset LaterOf(DateTimeOffset first, DateTimeOffset second)
    {
        return first >= second ? first : second;
    }
}
