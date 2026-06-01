using System.Security.Cryptography;
using System.Text;
using PasswordManager.Core;
using PasswordManager.Infrastructure;

namespace PasswordManager.Infrastructure.Tests;

public sealed class FileSystemVaultBackupServiceTests
{
    private const string MasterPassword = "correct horse battery staple - backup tests only";

    [Fact]
    public async Task CreateBackupAsync_CopiesEncryptedVaultAndPrunesOldBackups()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 5, 31, 14, 20, 0, TimeSpan.Zero));
        var service = new DgNetVaultService();
        var backupService = new FileSystemVaultBackupService(service, timeProvider);
        var secretPassword = "secret-password-that-must-not-appear-in-backup-bytes";
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry("Primary", secretPassword)));
        var sourceHash = await ComputeHashAsync(vaultPath);

        var backupResult = await backupService.CreateBackupAsync(vaultPath);

        Assert.True(saveResult.Succeeded);
        Assert.True(backupResult.Succeeded);
        Assert.Equal(VaultBackupArtifactKind.Backup, backupResult.Value!.Kind);
        Assert.Equal(sourceHash, backupResult.Value.SourceFingerprint);
        Assert.Equal(sourceHash, await ComputeHashAsync(backupResult.Value.FilePath));
        Assert.False(ContainsBytes(
            await File.ReadAllBytesAsync(backupResult.Value.FilePath),
            Encoding.UTF8.GetBytes(secretPassword)));

        var backupLoadResult = await service.LoadAsync(backupResult.Value.FilePath, MasterPassword);
        Assert.True(backupLoadResult.Succeeded);
        Assert.Equal("Primary", Assert.Single(backupLoadResult.Value!.Entries).ServiceName);

        for (var index = 0; index < 12; index++)
        {
            timeProvider.Advance(TimeSpan.FromMinutes(1));
            var result = await backupService.CreateBackupAsync(vaultPath, new VaultBackupOptions(maxBackups: 10));
            Assert.True(result.Succeeded);
        }

        var backupFiles = Directory.GetFiles(Path.Combine(temp.Path, "backups"), "*_backup_*.kdbx");
        Assert.Equal(10, backupFiles.Length);
        Assert.All(backupFiles, path => Assert.StartsWith(Path.Combine(temp.Path, "backups"), path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateConflictCopyAsync_WritesDirtySnapshotWithoutReplacingMainVault()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var service = new DgNetVaultService();
        var backupService = new FileSystemVaultBackupService(service);
        var initialSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry("Main", "main-password")));
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var mainHashBeforeConflict = await ComputeHashAsync(vaultPath);
        var dirtySnapshot = loadResult.Value!.Add(CreateEntry("LocalDirty", "local-dirty-password"));

        var conflictResult = await backupService.CreateConflictCopyAsync(
            vaultPath,
            MasterPassword,
            dirtySnapshot);

        Assert.True(initialSaveResult.Succeeded);
        Assert.True(loadResult.Succeeded);
        Assert.True(conflictResult.Succeeded);
        Assert.Equal(VaultBackupArtifactKind.ConflictCopy, conflictResult.Value!.Kind);
        Assert.Equal(mainHashBeforeConflict, await ComputeHashAsync(vaultPath));

        var conflictLoadResult = await service.LoadAsync(conflictResult.Value.FilePath, MasterPassword);
        Assert.True(conflictLoadResult.Succeeded);
        Assert.Contains(conflictLoadResult.Value!.Entries, entry => entry.ServiceName == "Main");
        Assert.Contains(conflictLoadResult.Value.Entries, entry => entry.ServiceName == "LocalDirty");
    }

    [Fact]
    public async Task CreateBackupAsync_WithInvalidPathsReturnsSafeFailures()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var backupService = new FileSystemVaultBackupService(service);
        var missingVaultPath = temp.GetVaultPath("missing.kdbx");
        var directoryPath = temp.GetVaultPath("directory.kdbx");
        Directory.CreateDirectory(directoryPath);

        var blankPathResult = await backupService.CreateBackupAsync(" ");
        var missingPathResult = await backupService.CreateBackupAsync(missingVaultPath);
        var directoryPathResult = await backupService.CreateBackupAsync(directoryPath);

        Assert.False(blankPathResult.Succeeded);
        Assert.Equal(VaultError.InvalidVaultPath, blankPathResult.Error);
        Assert.False(missingPathResult.Succeeded);
        Assert.Equal(VaultError.FileNotFound, missingPathResult.Error);
        Assert.False(directoryPathResult.Succeeded);
        Assert.Equal(VaultError.FileNotFound, directoryPathResult.Error);
    }

    private static AccountEntry CreateEntry(string serviceName, string password)
    {
        var timestamp = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);
        return new AccountEntry(
            Guid.NewGuid(),
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            $"{serviceName.ToLowerInvariant()}@example.test",
            password,
            $"notes for {serviceName}",
            ["backup"],
            isFavorite: false,
            timestamp,
            timestamp,
            timestamp);
    }

    private static async Task<string> ComputeHashAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static bool ContainsBytes(byte[] source, byte[] value)
    {
        if (value.Length == 0 || source.Length < value.Length)
        {
            return false;
        }

        for (var sourceIndex = 0; sourceIndex <= source.Length - value.Length; sourceIndex++)
        {
            if (source.AsSpan(sourceIndex, value.Length).SequenceEqual(value))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
