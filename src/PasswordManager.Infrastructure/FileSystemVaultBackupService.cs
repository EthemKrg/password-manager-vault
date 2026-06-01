using System.Globalization;
using System.Security.Cryptography;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure;

public sealed class FileSystemVaultBackupService : IVaultBackupService
{
    private const string BackupDirectoryName = "backups";
    private readonly IVaultService _vaultService;
    private readonly TimeProvider _timeProvider;

    public FileSystemVaultBackupService(IVaultService vaultService, TimeProvider? timeProvider = null)
    {
        _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<VaultOperationResult<VaultBackupArtifact>> CreateBackupAsync(
        string vaultPath,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            return VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.InvalidVaultPath);
        }

        var backupOptions = options ?? new VaultBackupOptions();

        try
        {
            var fullVaultPath = Path.GetFullPath(vaultPath);
            if (!File.Exists(fullVaultPath))
            {
                return VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.FileNotFound);
            }

            var createdAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var sourceFingerprint = await ComputeVaultFingerprintAsync(fullVaultPath, cancellationToken);
            var backupDirectory = GetBackupDirectory(fullVaultPath);
            Directory.CreateDirectory(backupDirectory);

            var backupPath = GetUniquePath(
                backupDirectory,
                BuildBackupFileName(fullVaultPath, createdAtUtc, sourceFingerprint));

            await CopyFileAsync(fullVaultPath, backupPath, cancellationToken);

            var sourceFingerprintAfterCopy = await ComputeVaultFingerprintAsync(fullVaultPath, cancellationToken);
            var backupFingerprint = await ComputeVaultFingerprintAsync(backupPath, cancellationToken);
            if (!string.Equals(sourceFingerprint, sourceFingerprintAfterCopy, StringComparison.Ordinal)
                || !string.Equals(sourceFingerprint, backupFingerprint, StringComparison.Ordinal))
            {
                DeleteIfExists(backupPath);
                return VaultOperationResult<VaultBackupArtifact>.Failure(
                    VaultError.StaleVaultSnapshot,
                    "Vault changed while backup was being created.");
            }

            PruneOldBackups(backupDirectory, SafeVaultName(fullVaultPath), backupOptions.MaxBackups);

            return VaultOperationResult<VaultBackupArtifact>.Success(
                new VaultBackupArtifact(
                    backupPath,
                    VaultBackupArtifactKind.Backup,
                    createdAtUtc,
                    sourceFingerprint));
        }
        catch (Exception ex)
        {
            return VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.BackupFailed, ex.GetType().Name);
        }
    }

    public async Task<VaultOperationResult<VaultBackupArtifact>> CreateConflictCopyAsync(
        string vaultPath,
        string masterPassword,
        VaultSnapshot snapshot,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            return VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.InvalidVaultPath);
        }

        if (string.IsNullOrWhiteSpace(masterPassword))
        {
            return VaultOperationResult<VaultBackupArtifact>.Failure(VaultError.InvalidMasterPassword);
        }

        try
        {
            var fullVaultPath = Path.GetFullPath(vaultPath);
            var backupDirectory = GetBackupDirectory(fullVaultPath);
            Directory.CreateDirectory(backupDirectory);

            var createdAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var conflictPath = GetUniquePath(
                backupDirectory,
                BuildConflictFileName(fullVaultPath, createdAtUtc));

            var saveResult = await _vaultService.SaveAsync(
                conflictPath,
                masterPassword,
                snapshot,
                cancellationToken);
            if (!saveResult.Succeeded)
            {
                DeleteIfExists(conflictPath);
                return VaultOperationResult<VaultBackupArtifact>.Failure(
                    VaultError.ConflictCopyFailed,
                    saveResult.Message ?? saveResult.Error.ToString());
            }

            return VaultOperationResult<VaultBackupArtifact>.Success(
                new VaultBackupArtifact(
                    conflictPath,
                    VaultBackupArtifactKind.ConflictCopy,
                    createdAtUtc,
                    snapshot.SourceFingerprint));
        }
        catch (Exception ex)
        {
            return VaultOperationResult<VaultBackupArtifact>.Failure(
                VaultError.ConflictCopyFailed,
                ex.GetType().Name);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = File.Open(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<string> ComputeVaultFingerprintAsync(
        string vaultPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Open(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string GetBackupDirectory(string fullVaultPath)
    {
        var parentDirectory = Path.GetDirectoryName(fullVaultPath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(parentDirectory, BackupDirectoryName);
    }

    private static string BuildBackupFileName(
        string fullVaultPath,
        DateTimeOffset createdAtUtc,
        string sourceFingerprint)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SafeVaultName(fullVaultPath)}_{FormatTimestamp(createdAtUtc)}_backup_{ShortHash(sourceFingerprint)}.kdbx");
    }

    private static string BuildConflictFileName(string fullVaultPath, DateTimeOffset createdAtUtc)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SafeVaultName(fullVaultPath)}_{FormatTimestamp(createdAtUtc)}_conflict_{Guid.NewGuid():N}.kdbx");
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static string ShortHash(string fingerprint)
    {
        return fingerprint.Length <= 8 ? fingerprint : fingerprint[..8];
    }

    private static string SafeVaultName(string fullVaultPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(fullVaultPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "vault";
        }

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var safeChars = fileName
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray();
        var safeName = new string(safeChars).Trim();
        return safeName.Length == 0 ? "vault" : safeName;
    }

    private static string GetUniquePath(string directoryPath, string fileName)
    {
        var candidatePath = Path.Combine(directoryPath, fileName);
        if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
        {
            return candidatePath;
        }

        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var index = 1; index <= 999; index++)
        {
            candidatePath = Path.Combine(directoryPath, $"{stem}_{index}{extension}");
            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new IOException("Could not allocate a unique backup file path.");
    }

    private static void PruneOldBackups(string backupDirectory, string safeVaultName, int maxBackups)
    {
        var backupFiles = Directory
            .EnumerateFiles(backupDirectory, $"{safeVaultName}_*_backup_*.kdbx", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(maxBackups)
            .ToArray();

        foreach (var file in backupFiles)
        {
            file.Delete();
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
