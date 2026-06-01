using System.Globalization;
using System.Security.Cryptography;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure;

public sealed class FileSystemVaultBackupService : IVaultBackupService
{
    private const string BackupDirectoryName = "backups";
    private const string BackupMarker = "_backup_";
    private const string ConflictMarker = "_conflict_";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";
    private readonly IVaultService _vaultService;
    private readonly TimeProvider _timeProvider;

    public FileSystemVaultBackupService(IVaultService vaultService, TimeProvider? timeProvider = null)
    {
        _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>> ListArtifactsAsync(
        string vaultPath,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            return Task.FromResult(
                VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Failure(VaultError.InvalidVaultPath));
        }

        try
        {
            var fullVaultPath = Path.GetFullPath(vaultPath);
            var backupDirectory = GetBackupDirectory(fullVaultPath);
            if (!Directory.Exists(backupDirectory))
            {
                return Task.FromResult(
                    VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Success([]));
            }

            var safeVaultName = SafeVaultName(fullVaultPath);
            var artifacts = Directory
                .EnumerateFiles(backupDirectory, $"{safeVaultName}_*.kdbx", SearchOption.TopDirectoryOnly)
                .Select(path => TryParseArtifact(path, safeVaultName))
                .Where(artifact => artifact is not null)
                .Cast<VaultBackupArtifact>()
                .OrderByDescending(artifact => artifact.CreatedAtUtc)
                .ThenByDescending(artifact => artifact.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Task.FromResult(
                VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Success(artifacts));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                VaultOperationResult<IReadOnlyList<VaultBackupArtifact>>.Failure(
                    VaultError.BackupFailed,
                    ex.GetType().Name));
        }
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
                    VaultError.ConflictCopyFailed);
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

    public async Task<VaultOperationResult> RestoreBackupAsync(
        string vaultPath,
        string backupPath,
        string masterPassword,
        VaultBackupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) || string.IsNullOrWhiteSpace(backupPath))
        {
            return VaultOperationResult.Failure(VaultError.InvalidVaultPath);
        }

        if (string.IsNullOrWhiteSpace(masterPassword))
        {
            return VaultOperationResult.Failure(VaultError.InvalidMasterPassword);
        }

        string? restoreTemporaryPath = null;

        try
        {
            var fullVaultPath = Path.GetFullPath(vaultPath);
            var fullBackupPath = Path.GetFullPath(backupPath);

            if (!File.Exists(fullVaultPath) || !File.Exists(fullBackupPath))
            {
                return VaultOperationResult.Failure(VaultError.FileNotFound);
            }

            if (!IsBackupPathForVault(fullVaultPath, fullBackupPath))
            {
                return VaultOperationResult.Failure(
                    VaultError.InvalidVaultPath,
                    "Backup must be inside this vault's backups directory.");
            }

            var backupLoadResult = await _vaultService.LoadAsync(
                fullBackupPath,
                masterPassword,
                cancellationToken);
            if (!backupLoadResult.Succeeded)
            {
                return VaultOperationResult.Failure(backupLoadResult.Error);
            }

            restoreTemporaryPath = GetRestoreTemporaryPath(fullVaultPath);
            var backupFingerprint = await ComputeVaultFingerprintAsync(fullBackupPath, cancellationToken);
            await CopyFileAsync(fullBackupPath, restoreTemporaryPath, cancellationToken);
            var backupFingerprintAfterCopy = await ComputeVaultFingerprintAsync(fullBackupPath, cancellationToken);
            var temporaryFingerprint = await ComputeVaultFingerprintAsync(restoreTemporaryPath, cancellationToken);
            if (!string.Equals(backupFingerprint, backupFingerprintAfterCopy, StringComparison.Ordinal)
                || !string.Equals(backupFingerprint, temporaryFingerprint, StringComparison.Ordinal))
            {
                return VaultOperationResult.Failure(
                    VaultError.StaleVaultSnapshot,
                    "Backup changed while restore was being prepared.");
            }

            var temporaryLoadResult = await _vaultService.LoadAsync(
                restoreTemporaryPath,
                masterPassword,
                cancellationToken);
            if (!temporaryLoadResult.Succeeded)
            {
                return VaultOperationResult.Failure(temporaryLoadResult.Error);
            }

            var preRestoreBackupResult = await CreateBackupAsync(
                fullVaultPath,
                options,
                cancellationToken);
            if (!preRestoreBackupResult.Succeeded)
            {
                return VaultOperationResult.Failure(preRestoreBackupResult.Error);
            }

            ReplaceTargetWithTemporaryFile(restoreTemporaryPath, fullVaultPath);
            restoreTemporaryPath = null;

            return VaultOperationResult.Success();
        }
        catch (Exception ex)
        {
            return VaultOperationResult.Failure(VaultError.RestoreFailed, ex.GetType().Name);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(restoreTemporaryPath))
            {
                DeleteIfExists(restoreTemporaryPath);
            }
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

    private static VaultBackupArtifact? TryParseArtifact(string path, string safeVaultName)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var prefix = $"{safeVaultName}_";
        if (!fileNameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = fileNameWithoutExtension[prefix.Length..];
        return TryParseArtifactWithMarker(path, remainder, BackupMarker, VaultBackupArtifactKind.Backup)
            ?? TryParseArtifactWithMarker(path, remainder, ConflictMarker, VaultBackupArtifactKind.ConflictCopy);
    }

    private static VaultBackupArtifact? TryParseArtifactWithMarker(
        string path,
        string remainder,
        string marker,
        VaultBackupArtifactKind kind)
    {
        var markerIndex = remainder.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            return null;
        }

        var timestampText = remainder[..markerIndex];
        if (!DateTimeOffset.TryParseExact(
                timestampText,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var createdAtUtc))
        {
            return null;
        }

        var sourceFingerprint = kind == VaultBackupArtifactKind.Backup
            ? remainder[(markerIndex + marker.Length)..]
            : null;
        return new VaultBackupArtifact(
            path,
            kind,
            createdAtUtc.ToUniversalTime(),
            string.IsNullOrWhiteSpace(sourceFingerprint) ? null : sourceFingerprint);
    }

    private static string BuildBackupFileName(
        string fullVaultPath,
        DateTimeOffset createdAtUtc,
        string sourceFingerprint)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SafeVaultName(fullVaultPath)}_{FormatTimestamp(createdAtUtc)}{BackupMarker}{ShortHash(sourceFingerprint)}.kdbx");
    }

    private static string BuildConflictFileName(string fullVaultPath, DateTimeOffset createdAtUtc)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SafeVaultName(fullVaultPath)}_{FormatTimestamp(createdAtUtc)}{ConflictMarker}{Guid.NewGuid():N}.kdbx");
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
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

    private static string GetRestoreTemporaryPath(string fullVaultPath)
    {
        var parentDirectory = Path.GetDirectoryName(fullVaultPath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(parentDirectory, $".{Path.GetFileName(fullVaultPath)}.{Guid.NewGuid():N}.restore.tmp");
    }

    private static bool IsBackupPathForVault(string fullVaultPath, string fullBackupPath)
    {
        if (!string.Equals(Path.GetExtension(fullBackupPath), ".kdbx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var backupDirectory = Path.GetFullPath(GetBackupDirectory(fullVaultPath));
        var backupDirectoryPrefix = backupDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparer = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return fullBackupPath.StartsWith(backupDirectoryPrefix, comparer);
    }

    private static void ReplaceTargetWithTemporaryFile(string temporaryPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(temporaryPath, targetPath, null);
            return;
        }

        File.Move(temporaryPath, targetPath);
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
