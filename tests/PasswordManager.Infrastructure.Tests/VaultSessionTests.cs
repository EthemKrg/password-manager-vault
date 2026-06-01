using PasswordManager.Core;
using PasswordManager.Infrastructure;

namespace PasswordManager.Infrastructure.Tests;

public sealed class VaultSessionTests
{
    private const string VaultPath = "session-test.kdbx";
    private const string MasterPassword = "session master password";
    private const string WrongPassword = "wrong session password";
    private static readonly DateTimeOffset SessionStart = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public void InitialState_IsNoVaultLoaded()
    {
        var session = new VaultSession(new FakeVaultService());

        Assert.Equal(VaultSessionState.NoVaultLoaded, session.State);
        Assert.Null(session.VaultPath);
        Assert.Null(session.CurrentSnapshot);
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task Operations_WhenNoVaultLoadedReturnNoVaultLoaded()
    {
        var session = new VaultSession(new FakeVaultService());

        var addResult = session.AddEntry(CreateDraft("GitHub"));
        var updateResult = session.UpdateEntry(CreateEntry("GitHub"));
        var deleteResult = session.DeleteEntry(Guid.NewGuid());
        var searchResult = session.Search(VaultSearchQuery.Empty);
        var saveResult = await session.SaveAsync();

        Assert.False(addResult.Succeeded);
        Assert.Equal(VaultError.NoVaultLoaded, addResult.Error);
        Assert.False(updateResult.Succeeded);
        Assert.Equal(VaultError.NoVaultLoaded, updateResult.Error);
        Assert.False(deleteResult.Succeeded);
        Assert.Equal(VaultError.NoVaultLoaded, deleteResult.Error);
        Assert.False(searchResult.Succeeded);
        Assert.Equal(VaultError.NoVaultLoaded, searchResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.NoVaultLoaded, saveResult.Error);
    }

    [Fact]
    public async Task Operations_WhenLockedReturnVaultLocked()
    {
        var service = new FakeVaultService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-1")));
        var session = new VaultSession(service);

        var unlockResult = await session.UnlockAsync(VaultPath, MasterPassword);
        var lockResult = session.Lock();
        var addResult = session.AddEntry(CreateDraft("GitHub"));
        var searchResult = session.Search(VaultSearchQuery.Empty);
        var saveResult = await session.SaveAsync();

        Assert.True(unlockResult.Succeeded);
        Assert.True(lockResult.Succeeded);
        Assert.Equal(VaultSessionState.Locked, session.State);
        Assert.False(addResult.Succeeded);
        Assert.Equal(VaultError.VaultLocked, addResult.Error);
        Assert.False(searchResult.Succeeded);
        Assert.Equal(VaultError.VaultLocked, searchResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.VaultLocked, saveResult.Error);
    }

    [Fact]
    public async Task CreateAsync_CreatesLoadsAndUnlocksSession()
    {
        var service = new FakeVaultService();
        var loadedSnapshot = new VaultSnapshot([], "created-fingerprint");
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(loadedSnapshot));
        var session = new VaultSession(service);

        var result = await session.CreateAsync(VaultPath, MasterPassword);

        Assert.True(result.Succeeded);
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        Assert.Equal(VaultPath, session.VaultPath);
        Assert.Same(loadedSnapshot, session.CurrentSnapshot);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal((VaultPath, MasterPassword), Assert.Single(service.CreateCalls));
        Assert.Equal((VaultPath, MasterPassword), Assert.Single(service.LoadCalls));
    }

    [Fact]
    public async Task UnlockAsync_FailureDoesNotReplaceExistingSession()
    {
        var service = new FakeVaultService();
        var originalSnapshot = new VaultSnapshot([CreateEntry("Original")], "original-fp");
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(originalSnapshot));
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Failure(VaultError.OpenFailed));
        var session = new VaultSession(service);

        var originalUnlockResult = await session.UnlockAsync(VaultPath, MasterPassword);
        var failedUnlockResult = await session.UnlockAsync("other.kdbx", WrongPassword);

        Assert.True(originalUnlockResult.Succeeded);
        Assert.False(failedUnlockResult.Succeeded);
        Assert.Equal(VaultError.OpenFailed, failedUnlockResult.Error);
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        Assert.Equal(VaultPath, session.VaultPath);
        Assert.Same(originalSnapshot, session.CurrentSnapshot);
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task Mutations_UpdateSnapshotAndDirtyState()
    {
        var session = await CreateUnlockedSession(new VaultSnapshot([], "fp-1"));

        var addResult = session.AddEntry(CreateDraft("GitHub"));
        var addedEntry = addResult.Value!;
        var updatedEntry = addedEntry with { Notes = "updated notes" };

        Assert.True(addResult.Succeeded);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(addedEntry.Id, Assert.Single(session.CurrentSnapshot!.Entries).Id);

        var missingUpdateResult = session.UpdateEntry(CreateEntry("Missing"));
        Assert.False(missingUpdateResult.Succeeded);
        Assert.Equal(VaultError.EntryNotFound, missingUpdateResult.Error);

        var updateResult = session.UpdateEntry(updatedEntry);
        Assert.True(updateResult.Succeeded);
        Assert.Equal("updated notes", Assert.Single(session.CurrentSnapshot.Entries).Notes);

        var missingDeleteResult = session.DeleteEntry(Guid.NewGuid());
        Assert.False(missingDeleteResult.Succeeded);
        Assert.Equal(VaultError.EntryNotFound, missingDeleteResult.Error);

        var deleteResult = session.DeleteEntry(addedEntry.Id);
        Assert.True(deleteResult.Succeeded);
        Assert.Empty(session.CurrentSnapshot.Entries);
    }

    [Fact]
    public async Task AddEntry_UsesSessionTimeProviderForEntryMetadata()
    {
        var timeProvider = new ManualTimeProvider(SessionStart);
        var session = await CreateUnlockedSession(new VaultSnapshot([], "fp-1"), timeProvider);

        var addResult = session.AddEntry(CreateDraft("GitHub"));

        Assert.True(addResult.Succeeded);
        Assert.Equal(SessionStart, addResult.Value!.CreatedAtUtc);
        Assert.Equal(SessionStart, addResult.Value.UpdatedAtUtc);
        Assert.Equal(SessionStart, addResult.Value.PasswordChangedAtUtc);
    }

    [Fact]
    public async Task UpdateEntry_RefreshesUpdatedTimestampAndOnlyChangesPasswordTimestampWhenPasswordChanges()
    {
        var timeProvider = new ManualTimeProvider(SessionStart);
        var session = await CreateUnlockedSession(new VaultSnapshot([], "fp-1"), timeProvider);
        var addResult = session.AddEntry(CreateDraft("GitHub"));
        var addedEntry = addResult.Value!;

        var metadataOnlyUpdateTime = SessionStart.AddMinutes(5);
        timeProvider.SetUtcNow(metadataOnlyUpdateTime);
        var metadataOnlyUpdateResult = session.UpdateEntry(addedEntry with
        {
            Notes = "updated notes",
            IsFavorite = true
        });
        var metadataOnlyUpdatedEntry = Assert.Single(session.CurrentSnapshot!.Entries);

        var passwordUpdateTime = SessionStart.AddMinutes(10);
        timeProvider.SetUtcNow(passwordUpdateTime);
        var passwordUpdateResult = session.UpdateEntry(metadataOnlyUpdatedEntry with
        {
            Password = "new-password-for-github"
        });
        var passwordUpdatedEntry = Assert.Single(session.CurrentSnapshot.Entries);

        Assert.True(addResult.Succeeded);
        Assert.True(metadataOnlyUpdateResult.Succeeded);
        Assert.Equal(SessionStart, metadataOnlyUpdatedEntry.CreatedAtUtc);
        Assert.Equal(metadataOnlyUpdateTime, metadataOnlyUpdatedEntry.UpdatedAtUtc);
        Assert.Equal(SessionStart, metadataOnlyUpdatedEntry.PasswordChangedAtUtc);
        Assert.True(metadataOnlyUpdatedEntry.IsFavorite);

        Assert.True(passwordUpdateResult.Succeeded);
        Assert.Equal(SessionStart, passwordUpdatedEntry.CreatedAtUtc);
        Assert.Equal(passwordUpdateTime, passwordUpdatedEntry.UpdatedAtUtc);
        Assert.Equal(passwordUpdateTime, passwordUpdatedEntry.PasswordChangedAtUtc);
    }

    [Fact]
    public async Task AddEntry_WhenEntryAlreadyExistsReturnsEntryAlreadyExists()
    {
        var existingEntry = CreateEntry("GitHub");
        var session = await CreateUnlockedSession(new VaultSnapshot([existingEntry], "fp-1"));

        var result = session.AddEntry(existingEntry);

        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.EntryAlreadyExists, result.Error);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal(existingEntry.Id, Assert.Single(session.CurrentSnapshot!.Entries).Id);
    }

    [Fact]
    public async Task SaveAsync_UsesStoredPasswordAndReloadsSnapshot()
    {
        var service = new FakeVaultService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([CreateEntry("Reloaded")], "fp-after")));
        var session = new VaultSession(service);
        var unlockResult = await session.UnlockAsync(VaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));

        var saveResult = await session.SaveAsync();

        Assert.True(unlockResult.Succeeded);
        Assert.True(addResult.Succeeded);
        Assert.True(saveResult.Succeeded);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal("fp-after", session.CurrentSnapshot!.SourceFingerprint);

        var saveCall = Assert.Single(service.SaveCalls);
        Assert.Equal(VaultPath, saveCall.Path);
        Assert.Equal(MasterPassword, saveCall.Password);
        Assert.Equal(addResult.Value!.Id, Assert.Single(saveCall.Snapshot.Entries).Id);
        Assert.Equal(2, service.LoadCalls.Count);
    }

    [Fact]
    public async Task SaveAsync_WhenServiceFailsKeepsDirtySnapshot()
    {
        var service = new FakeVaultService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        service.SaveResults.Enqueue(VaultOperationResult.Failure(VaultError.SaveFailed));
        var session = new VaultSession(service);
        await session.UnlockAsync(VaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));

        var saveResult = await session.SaveAsync();

        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.SaveFailed, saveResult.Error);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(addResult.Value!.Id, Assert.Single(session.CurrentSnapshot!.Entries).Id);
        Assert.Single(service.LoadCalls);
    }

    [Fact]
    public async Task SaveAsync_WithBackupServiceCreatesBackupBeforeSave()
    {
        var service = new FakeVaultService();
        var backupService = new FakeVaultBackupService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([CreateEntry("Reloaded")], "fp-after")));
        var session = new VaultSession(service, vaultBackupService: backupService);
        await session.UnlockAsync(VaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));

        var saveResult = await session.SaveAsync();

        Assert.True(addResult.Succeeded);
        Assert.True(saveResult.Succeeded);
        Assert.Equal([VaultPath], backupService.BackupCalls);
        Assert.Single(service.SaveCalls);
        Assert.Empty(backupService.ConflictCopyCalls);
    }

    [Fact]
    public async Task SaveAsync_WhenBackupFailsDoesNotSaveAndKeepsDirtySnapshot()
    {
        var service = new FakeVaultService();
        var backupService = new FakeVaultBackupService();
        backupService.BackupResults.Enqueue(
            VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.BackupFailed));
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        var session = new VaultSession(service, vaultBackupService: backupService);
        await session.UnlockAsync(VaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));

        var saveResult = await session.SaveAsync();

        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.BackupFailed, saveResult.Error);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(addResult.Value!.Id, Assert.Single(session.CurrentSnapshot!.Entries).Id);
        Assert.Empty(service.SaveCalls);
        Assert.Empty(backupService.ConflictCopyCalls);
    }

    [Fact]
    public async Task SaveAsync_WhenStaleSnapshotCreatesConflictCopyAndKeepsDirtySnapshot()
    {
        var service = new FakeVaultService();
        var backupService = new FakeVaultBackupService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        service.SaveResults.Enqueue(VaultOperationResult.Failure(VaultError.StaleVaultSnapshot));
        var session = new VaultSession(service, vaultBackupService: backupService);
        await session.UnlockAsync(VaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));

        var saveResult = await session.SaveAsync();

        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.StaleVaultSnapshot, saveResult.Error);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(addResult.Value!.Id, Assert.Single(session.CurrentSnapshot!.Entries).Id);
        Assert.Single(backupService.BackupCalls);
        var conflictCall = Assert.Single(backupService.ConflictCopyCalls);
        Assert.Equal(VaultPath, conflictCall.Path);
        Assert.Equal(MasterPassword, conflictCall.Password);
        Assert.Equal(addResult.Value.Id, Assert.Single(conflictCall.Snapshot.Entries).Id);
    }

    [Fact]
    public async Task SaveAsync_WhenConflictCopyFailsReturnsConflictCopyFailedAndKeepsDirtySnapshot()
    {
        var service = new FakeVaultService();
        var backupService = new FakeVaultBackupService();
        backupService.ConflictCopyResults.Enqueue(
            VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.ConflictCopyFailed));
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        service.SaveResults.Enqueue(VaultOperationResult.Failure(VaultError.StaleVaultSnapshot));
        var session = new VaultSession(service, vaultBackupService: backupService);
        await session.UnlockAsync(VaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));

        var saveResult = await session.SaveAsync();

        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.ConflictCopyFailed, saveResult.Error);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(addResult.Value!.Id, Assert.Single(session.CurrentSnapshot!.Entries).Id);
    }

    [Fact]
    public async Task LockAndClose_ProtectUnsavedChangesUnlessDiscarded()
    {
        var session = await CreateUnlockedSession(new VaultSnapshot([], "fp-1"));
        session.AddEntry(CreateDraft("GitHub"));

        var lockWithoutDiscardResult = session.Lock();

        Assert.False(lockWithoutDiscardResult.Succeeded);
        Assert.Equal(VaultError.UnsavedChanges, lockWithoutDiscardResult.Error);
        Assert.Equal(VaultSessionState.Unlocked, session.State);

        var lockWithDiscardResult = session.Lock(discardUnsavedChanges: true);

        Assert.True(lockWithDiscardResult.Succeeded);
        Assert.Equal(VaultSessionState.Locked, session.State);
        Assert.Null(session.CurrentSnapshot);
        Assert.False(session.HasUnsavedChanges);

        var closeResult = session.Close();

        Assert.True(closeResult.Succeeded);
        Assert.Equal(VaultSessionState.NoVaultLoaded, session.State);
        Assert.Null(session.VaultPath);
    }

    [Fact]
    public async Task UnlockCurrentAsync_ReopensLockedVaultPath()
    {
        var service = new FakeVaultService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-before")));
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([CreateEntry("Reloaded")], "fp-after")));
        var session = new VaultSession(service);
        await session.UnlockAsync(VaultPath, MasterPassword);
        session.Lock();

        var result = await session.UnlockCurrentAsync(WrongPassword);

        Assert.True(result.Succeeded);
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        Assert.Equal(VaultPath, session.VaultPath);
        Assert.Equal("fp-after", session.CurrentSnapshot!.SourceFingerprint);
        Assert.Equal((VaultPath, WrongPassword), service.LoadCalls[1]);
    }

    [Fact]
    public async Task UnlockOrCreate_WhenDirtyReturnsUnsavedChangesAndDoesNotCallService()
    {
        var service = new FakeVaultService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], "fp-1")));
        var session = new VaultSession(service);
        await session.UnlockAsync(VaultPath, MasterPassword);
        session.AddEntry(CreateDraft("GitHub"));

        var unlockResult = await session.UnlockAsync("other.kdbx", MasterPassword);
        var createResult = await session.CreateAsync("new.kdbx", MasterPassword);

        Assert.False(unlockResult.Succeeded);
        Assert.Equal(VaultError.UnsavedChanges, unlockResult.Error);
        Assert.False(createResult.Succeeded);
        Assert.Equal(VaultError.UnsavedChanges, createResult.Error);
        Assert.Single(service.LoadCalls);
        Assert.Empty(service.CreateCalls);
    }

    [Fact]
    public async Task VaultSession_WithDgNetVaultService_RoundtripsThroughLockAndUnlock()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var session = new VaultSession(new DgNetVaultService());

        var createResult = await session.CreateAsync(vaultPath, MasterPassword);
        var addResult = session.AddEntry(CreateDraft("GitHub"));
        var saveResult = await session.SaveAsync();
        var lockResult = session.Lock();
        var unlockResult = await session.UnlockCurrentAsync(MasterPassword);
        var searchResult = session.Search(new VaultSearchQuery("github"));

        Assert.True(createResult.Succeeded);
        Assert.True(addResult.Succeeded);
        Assert.True(saveResult.Succeeded);
        Assert.True(lockResult.Succeeded);
        Assert.True(unlockResult.Succeeded);
        Assert.True(searchResult.Succeeded);
        Assert.Equal(addResult.Value!.Id, Assert.Single(searchResult.Value!).Id);
        Assert.False(session.HasUnsavedChanges);
    }

    private static async Task<VaultSession> CreateUnlockedSession(
        VaultSnapshot snapshot,
        TimeProvider? timeProvider = null)
    {
        var service = new FakeVaultService();
        service.LoadResults.Enqueue(VaultOperationResult<VaultSnapshot>.Success(snapshot));
        var session = new VaultSession(service, timeProvider);
        var result = await session.UnlockAsync(VaultPath, MasterPassword);
        Assert.True(result.Succeeded);
        return session;
    }

    private static AccountEntryDraft CreateDraft(string serviceName)
    {
        return new AccountEntryDraft(
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            $"{serviceName.ToLowerInvariant()}@example.test",
            $"password-for-{serviceName}",
            $"notes for {serviceName}",
            ["session"]);
    }

    private static AccountEntry CreateEntry(string serviceName)
    {
        return AccountEntry.Create(CreateDraft(serviceName), SessionStart);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }

    private sealed class FakeVaultService : IVaultService
    {
        public Queue<VaultOperationResult> CreateResults { get; } = new();

        public Queue<VaultOperationResult<VaultSnapshot>> LoadResults { get; } = new();

        public Queue<VaultOperationResult> SaveResults { get; } = new();

        public List<(string Path, string Password)> CreateCalls { get; } = [];

        public List<(string Path, string Password)> LoadCalls { get; } = [];

        public List<(string Path, string Password, VaultSnapshot Snapshot)> SaveCalls { get; } = [];

        public Task<VaultOperationResult> CreateAsync(
            string vaultPath,
            string masterPassword,
            CancellationToken cancellationToken = default)
        {
            CreateCalls.Add((vaultPath, masterPassword));
            return Task.FromResult(CreateResults.Count > 0
                ? CreateResults.Dequeue()
                : VaultOperationResult.Success());
        }

        public Task<VaultOperationResult<VaultSnapshot>> LoadAsync(
            string vaultPath,
            string masterPassword,
            CancellationToken cancellationToken = default)
        {
            LoadCalls.Add((vaultPath, masterPassword));
            return Task.FromResult(LoadResults.Count > 0
                ? LoadResults.Dequeue()
                : VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([], $"fp-{LoadCalls.Count}")));
        }

        public Task<VaultOperationResult> SaveAsync(
            string vaultPath,
            string masterPassword,
            VaultSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            SaveCalls.Add((vaultPath, masterPassword, snapshot));
            return Task.FromResult(SaveResults.Count > 0
                ? SaveResults.Dequeue()
                : VaultOperationResult.Success());
        }
    }

    private sealed class FakeVaultBackupService : IVaultBackupService
    {
        public Queue<VaultOperationResult<VaultBackupArtifact>> BackupResults { get; } = new();

        public Queue<VaultOperationResult<VaultBackupArtifact>> ConflictCopyResults { get; } = new();

        public List<string> BackupCalls { get; } = [];

        public List<(string Path, string Password, VaultSnapshot Snapshot)> ConflictCopyCalls { get; } = [];

        public Task<VaultOperationResult<VaultBackupArtifact>> CreateBackupAsync(
            string vaultPath,
            VaultBackupOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            BackupCalls.Add(vaultPath);
            return Task.FromResult(BackupResults.Count > 0
                ? BackupResults.Dequeue()
                : VaultOperationResult<VaultBackupArtifact>.Success(
                    new VaultBackupArtifact(
                        $"{vaultPath}.backup",
                        VaultBackupArtifactKind.Backup,
                        SessionStart,
                        "backup-fp")));
        }

        public Task<VaultOperationResult<VaultBackupArtifact>> CreateConflictCopyAsync(
            string vaultPath,
            string masterPassword,
            VaultSnapshot snapshot,
            VaultBackupOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ConflictCopyCalls.Add((vaultPath, masterPassword, snapshot));
            return Task.FromResult(ConflictCopyResults.Count > 0
                ? ConflictCopyResults.Dequeue()
                : VaultOperationResult<VaultBackupArtifact>.Success(
                    new VaultBackupArtifact(
                        $"{vaultPath}.conflict",
                        VaultBackupArtifactKind.ConflictCopy,
                        SessionStart,
                        snapshot.SourceFingerprint)));
        }
    }
}
