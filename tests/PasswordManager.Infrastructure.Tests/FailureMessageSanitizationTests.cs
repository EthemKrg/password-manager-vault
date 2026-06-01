using PasswordManager.Core;
using PasswordManager.Infrastructure;

namespace PasswordManager.Infrastructure.Tests;

public sealed class FailureMessageSanitizationTests
{
    private const string VaultPath = "vault.kdbx";
    private const string MasterPassword = "master password for sanitization tests";
    private const string SecretBearingMessage = "MASTER-SECRET leak marker with ENTRY-PASSWORD leak marker";
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task VaultSession_DoesNotExposeCreateFailureMessage()
    {
        var service = new SecretBearingVaultService();
        service.CreateResults.Enqueue(VaultOperationResult.Failure(VaultError.SaveFailed, SecretBearingMessage));
        var session = new VaultSession(service);

        var result = await session.CreateAsync(VaultPath, MasterPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.SaveFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    [Fact]
    public async Task VaultSession_DoesNotExposeLoadFailureMessage()
    {
        var service = new SecretBearingVaultService();
        service.LoadResults.Enqueue(
            VaultOperationResult<VaultSnapshot>.Failure(VaultError.OpenFailed, SecretBearingMessage));
        var session = new VaultSession(service);

        var result = await session.UnlockAsync(VaultPath, MasterPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.OpenFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    [Fact]
    public async Task VaultSession_DoesNotExposeSaveFailureMessage()
    {
        var service = new SecretBearingVaultService();
        service.SaveResults.Enqueue(VaultOperationResult.Failure(VaultError.SaveFailed, SecretBearingMessage));
        var session = new VaultSession(service);
        var unlockResult = await session.UnlockAsync(VaultPath, MasterPassword);

        var result = await session.SaveAsync();

        Assert.True(unlockResult.Succeeded);
        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.SaveFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    [Fact]
    public async Task VaultSession_DoesNotExposeBackupFailureMessage()
    {
        var service = new SecretBearingVaultService();
        var backupService = new SecretBearingBackupService();
        backupService.BackupResults.Enqueue(
            VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.BackupFailed, SecretBearingMessage));
        var session = new VaultSession(service, vaultBackupService: backupService);
        var unlockResult = await session.UnlockAsync(VaultPath, MasterPassword);

        var result = await session.SaveAsync();

        Assert.True(unlockResult.Succeeded);
        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.BackupFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    [Fact]
    public async Task VaultSession_DoesNotExposeMasterPasswordChangeFailureMessage()
    {
        var service = new SecretBearingVaultService();
        service.ChangeMasterPasswordResults.Enqueue(
            VaultOperationResult.Failure(VaultError.SaveFailed, SecretBearingMessage));
        var session = new VaultSession(service);
        var unlockResult = await session.UnlockAsync(VaultPath, MasterPassword);

        var result = await session.ChangeMasterPasswordAsync(MasterPassword, "new master password");

        Assert.True(unlockResult.Succeeded);
        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.SaveFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    [Fact]
    public async Task VaultSession_DoesNotExposeListOrRestoreFailureMessages()
    {
        var service = new SecretBearingVaultService();
        var backupService = new SecretBearingBackupService();
        backupService.ListResults.Enqueue(
            VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Failure(VaultError.BackupFailed, SecretBearingMessage));
        backupService.RestoreResults.Enqueue(
            VaultOperationResult.Failure(VaultError.RestoreFailed, SecretBearingMessage));
        var session = new VaultSession(service, vaultBackupService: backupService);
        var unlockResult = await session.UnlockAsync(VaultPath, MasterPassword);

        var listResult = await session.ListBackupArtifactsAsync();
        var restoreResult = await session.RestoreBackupAsync("backup.kdbx", MasterPassword);

        Assert.True(unlockResult.Succeeded);
        Assert.False(listResult.Succeeded);
        Assert.Equal(VaultError.BackupFailed, listResult.Error);
        AssertDoesNotExposeSecret(listResult.Message);
        Assert.False(restoreResult.Succeeded);
        Assert.Equal(VaultError.RestoreFailed, restoreResult.Error);
        AssertDoesNotExposeSecret(restoreResult.Message);
    }

    [Fact]
    public async Task FileSystemBackupService_DoesNotExposeConflictCopyDependencyMessage()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new SecretBearingVaultService();
        service.SaveResults.Enqueue(VaultOperationResult.Failure(VaultError.SaveFailed, SecretBearingMessage));
        var backupService = new FileSystemVaultBackupService(service);

        var result = await backupService.CreateConflictCopyAsync(
            temp.GetVaultPath(),
            MasterPassword,
            new VaultSnapshot([CreateEntry("Dirty")], "source-fingerprint"));

        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.ConflictCopyFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    [Fact]
    public async Task FileSystemBackupService_DoesNotExposeRestoreDependencyMessage()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var backupDirectory = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, "vault_20260601T1000000000000Z_backup_12345678.kdbx");
        await File.WriteAllBytesAsync(vaultPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(backupPath, [4, 5, 6]);
        var service = new SecretBearingVaultService();
        service.LoadResults.Enqueue(
            VaultOperationResult<VaultSnapshot>.Failure(VaultError.OpenFailed, SecretBearingMessage));
        var backupService = new FileSystemVaultBackupService(service);

        var result = await backupService.RestoreBackupAsync(vaultPath, backupPath, MasterPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(VaultError.OpenFailed, result.Error);
        AssertDoesNotExposeSecret(result.Message);
    }

    private static void AssertDoesNotExposeSecret(string? message)
    {
        if (message is null)
        {
            return;
        }

        Assert.DoesNotContain("MASTER-SECRET", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ENTRY-PASSWORD", message, StringComparison.Ordinal);
    }

    private static AccountEntry CreateEntry(string serviceName)
    {
        return new AccountEntry(
            Guid.NewGuid(),
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            $"{serviceName.ToLowerInvariant()}@example.test",
            $"password-for-{serviceName}",
            $"notes for {serviceName}",
            ["sanitization"],
            isFavorite: false,
            Timestamp,
            Timestamp,
            Timestamp);
    }

    private sealed class SecretBearingVaultService : IVaultService
    {
        public Queue<VaultOperationResult> CreateResults { get; } = new();

        public Queue<VaultOperationResult<VaultSnapshot>> LoadResults { get; } = new();

        public Queue<VaultOperationResult> SaveResults { get; } = new();

        public Queue<VaultOperationResult> ChangeMasterPasswordResults { get; } = new();

        public Task<VaultOperationResult> CreateAsync(
            string vaultPath,
            string masterPassword,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateResults.Count > 0
                ? CreateResults.Dequeue()
                : VaultOperationResult.Success());
        }

        public Task<VaultOperationResult<VaultSnapshot>> LoadAsync(
            string vaultPath,
            string masterPassword,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LoadResults.Count > 0
                ? LoadResults.Dequeue()
                : VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot([CreateEntry("Loaded")], "loaded-fingerprint")));
        }

        public Task<VaultOperationResult> SaveAsync(
            string vaultPath,
            string masterPassword,
            VaultSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SaveResults.Count > 0
                ? SaveResults.Dequeue()
                : VaultOperationResult.Success());
        }

        public Task<VaultOperationResult> ChangeMasterPasswordAsync(
            string vaultPath,
            string currentMasterPassword,
            string newMasterPassword,
            VaultSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ChangeMasterPasswordResults.Count > 0
                ? ChangeMasterPasswordResults.Dequeue()
                : VaultOperationResult.Success());
        }
    }

    private sealed class SecretBearingBackupService : IVaultBackupService
    {
        public Queue<VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>> ListResults { get; } = new();

        public Queue<VaultOperationResult<VaultBackupArtifact>> BackupResults { get; } = new();

        public Queue<VaultOperationResult> RestoreResults { get; } = new();

        public Task<VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>> ListArtifactsAsync(
            string vaultPath,
            VaultBackupOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ListResults.Count > 0
                ? ListResults.Dequeue()
                : VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Success([]));
        }

        public Task<VaultOperationResult<VaultBackupArtifact>> CreateBackupAsync(
            string vaultPath,
            VaultBackupOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackupResults.Count > 0
                ? BackupResults.Dequeue()
                : VaultOperationResult<VaultBackupArtifact>.Success(
                    new VaultBackupArtifact(
                        $"{vaultPath}.backup",
                        VaultBackupArtifactKind.Backup,
                        Timestamp,
                        "backup-fingerprint")));
        }

        public Task<VaultOperationResult<VaultBackupArtifact>> CreateConflictCopyAsync(
            string vaultPath,
            string masterPassword,
            VaultSnapshot snapshot,
            VaultBackupOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                VaultOperationResult<VaultBackupArtifact>.Success(
                    new VaultBackupArtifact(
                        $"{vaultPath}.conflict",
                        VaultBackupArtifactKind.ConflictCopy,
                        Timestamp,
                        snapshot.SourceFingerprint)));
        }

        public Task<VaultOperationResult> RestoreBackupAsync(
            string vaultPath,
            string backupPath,
            string masterPassword,
            VaultBackupOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RestoreResults.Count > 0
                ? RestoreResults.Dequeue()
                : VaultOperationResult.Success());
        }
    }
}
