using System.Security.Cryptography;
using DgNet.Keepass;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class DgNetVaultHardeningTests
{
    private const string MasterPassword = "correct horse battery staple - hardening tests only";
    private const string WrongPassword = "wrong password - hardening tests only";
    private static readonly DateTimeOffset EntryCreatedAtUtc = new(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);
    private static readonly DateTimeOffset EntryPasswordChangedAtUtc = EntryCreatedAtUtc.AddMinutes(3);
    private static readonly DateTimeOffset EntryUpdatedAtUtc = EntryPasswordChangedAtUtc.AddMinutes(5);

    [Fact]
    public async Task LoadAnalyzeAndSaveAsync_CorruptedVaultFailWithoutReplacingSource()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var analyzer = new DgNetExternalVaultAnalyzer();
        var vaultPath = temp.GetVaultPath();
        var initialSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry("Original")));
        TruncateVaultFile(vaultPath);
        var corruptedHash = await ComputeHashAsync(vaultPath);

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var analysisResult = await analyzer.AnalyzeAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry("Replacement")));

        Assert.True(initialSaveResult.Succeeded);
        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.OpenFailed, loadResult.Error);
        Assert.True(analysisResult.Succeeded);
        Assert.Equal(ExternalVaultAnalysisKind.Unreadable, analysisResult.Value!.Kind);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.OpenFailed, saveResult.Error);
        Assert.Equal(corruptedHash, await ComputeHashAsync(vaultPath));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public async Task Operations_WithInvalidPathsReturnSafeFailures()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var snapshot = VaultSnapshot.Empty.Add(CreateEntry("GitHub"));
        var missingPath = temp.GetVaultPath("missing.kdbx");
        var directoryPath = temp.GetVaultPath("directory-target.kdbx");
        Directory.CreateDirectory(directoryPath);

        var createWithBlankPathResult = await service.CreateAsync(" ", MasterPassword);
        var loadWithBlankPathResult = await service.LoadAsync(" ", MasterPassword);
        var saveWithBlankPathResult = await service.SaveAsync(" ", MasterPassword, snapshot);
        var loadMissingResult = await service.LoadAsync(missingPath, MasterPassword);
        var loadDirectoryResult = await service.LoadAsync(directoryPath, MasterPassword);
        var saveDirectoryResult = await service.SaveAsync(directoryPath, MasterPassword, snapshot);
        var createDirectoryResult = await service.CreateAsync(directoryPath, MasterPassword);

        Assert.False(createWithBlankPathResult.Succeeded);
        Assert.Equal(VaultError.InvalidVaultPath, createWithBlankPathResult.Error);
        Assert.False(loadWithBlankPathResult.Succeeded);
        Assert.Equal(VaultError.InvalidVaultPath, loadWithBlankPathResult.Error);
        Assert.False(saveWithBlankPathResult.Succeeded);
        Assert.Equal(VaultError.InvalidVaultPath, saveWithBlankPathResult.Error);
        Assert.False(loadMissingResult.Succeeded);
        Assert.Equal(VaultError.FileNotFound, loadMissingResult.Error);
        Assert.False(loadDirectoryResult.Succeeded);
        Assert.Equal(VaultError.FileNotFound, loadDirectoryResult.Error);
        Assert.False(saveDirectoryResult.Succeeded);
        Assert.Equal(VaultError.SaveFailed, saveDirectoryResult.Error);
        Assert.False(createDirectoryResult.Succeeded);
        Assert.Equal(VaultError.SaveFailed, createDirectoryResult.Error);
        Assert.True(Directory.Exists(directoryPath));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public async Task SaveAsync_FailurePathsPreserveExistingVaultAndDoNotLeaveTemporaryFiles()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var initialEntry = CreateEntry("Original");
        var replacementSnapshot = VaultSnapshot.Empty.Add(CreateEntry("Replacement"));
        var initialSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(initialEntry));
        var originalHash = await ComputeHashAsync(vaultPath);

        var wrongPasswordResult = await service.SaveAsync(vaultPath, WrongPassword, replacementSnapshot);
        var hashAfterWrongPassword = await ComputeHashAsync(vaultPath);
        ConvertToExternalVault(vaultPath);
        var externalHash = await ComputeHashAsync(vaultPath);
        var unsupportedExternalResult = await service.SaveAsync(vaultPath, MasterPassword, replacementSnapshot);

        Assert.True(initialSaveResult.Succeeded);
        Assert.False(wrongPasswordResult.Succeeded);
        Assert.Equal(VaultError.OpenFailed, wrongPasswordResult.Error);
        Assert.Equal(originalHash, hashAfterWrongPassword);
        Assert.False(unsupportedExternalResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFormat, unsupportedExternalResult.Error);
        Assert.Equal(externalHash, await ComputeHashAsync(vaultPath));
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public async Task SaveAsync_StaleSnapshotPreservesNewerVaultAndLeavesNoTemporaryFiles()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var initialSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry("Original")));
        var firstLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var staleLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var firstSavedSnapshot = firstLoadResult.Value!.Update(
            firstLoadResult.Value.Entries.Single() with
            {
                Notes = "first writer wins"
            });
        var firstSaveResult = await service.SaveAsync(vaultPath, MasterPassword, firstSavedSnapshot);
        var hashAfterFirstSave = await ComputeHashAsync(vaultPath);
        var staleSnapshot = staleLoadResult.Value!.Update(
            staleLoadResult.Value.Entries.Single() with
            {
                Notes = "stale writer loses"
            });

        var staleSaveResult = await service.SaveAsync(vaultPath, MasterPassword, staleSnapshot);
        var finalLoadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(initialSaveResult.Succeeded);
        Assert.True(firstLoadResult.Succeeded);
        Assert.True(staleLoadResult.Succeeded);
        Assert.True(firstSaveResult.Succeeded);
        Assert.False(staleSaveResult.Succeeded);
        Assert.Equal(VaultError.StaleVaultSnapshot, staleSaveResult.Error);
        Assert.Equal(hashAfterFirstSave, await ComputeHashAsync(vaultPath));
        Assert.True(finalLoadResult.Succeeded);
        Assert.Equal("first writer wins", Assert.Single(finalLoadResult.Value!.Entries).Notes);
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public async Task VaultSession_SaveAsync_WhenVaultChangedOnDiskKeepsDirtySnapshotUnlocked()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var service = new DgNetVaultService();
        var session = new VaultSession(service);
        var createResult = await session.CreateAsync(vaultPath, MasterPassword);
        var initialAddResult = session.AddEntry(CreateDraft("Original"));
        var initialSaveResult = await session.SaveAsync();
        var externalLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var externalSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            externalLoadResult.Value!.Update(
                externalLoadResult.Value.Entries.Single() with
                {
                    Notes = "external update"
                }));
        var dirtyAddResult = session.AddEntry(CreateDraft("LocalDirty"));

        var staleSessionSaveResult = await session.SaveAsync();
        var finalLoadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(createResult.Succeeded);
        Assert.True(initialAddResult.Succeeded);
        Assert.True(initialSaveResult.Succeeded);
        Assert.True(externalLoadResult.Succeeded);
        Assert.True(externalSaveResult.Succeeded);
        Assert.True(dirtyAddResult.Succeeded);
        Assert.False(staleSessionSaveResult.Succeeded);
        Assert.Equal(VaultError.StaleVaultSnapshot, staleSessionSaveResult.Error);
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        Assert.True(session.HasUnsavedChanges);
        Assert.Contains(session.CurrentSnapshot!.Entries, entry => entry.Id == dirtyAddResult.Value!.Id);
        Assert.True(finalLoadResult.Succeeded);
        Assert.DoesNotContain(finalLoadResult.Value!.Entries, entry => entry.Id == dirtyAddResult.Value!.Id);
        Assert.Equal("external update", Assert.Single(finalLoadResult.Value.Entries).Notes);
        Assert.Empty(TemporaryFiles(temp));
    }

    [Fact]
    public async Task VaultSession_SaveAsync_WithBackupServiceCreatesConflictCopyWhenVaultChangedOnDisk()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var service = new DgNetVaultService();
        var backupService = new FileSystemVaultBackupService(service);
        var session = new VaultSession(service, vaultBackupService: backupService);
        var createResult = await session.CreateAsync(vaultPath, MasterPassword);
        var initialAddResult = session.AddEntry(CreateDraft("Original"));
        var initialSaveResult = await session.SaveAsync();
        var externalLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var externalSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            externalLoadResult.Value!.Update(
                externalLoadResult.Value.Entries.Single() with
                {
                    Notes = "external update"
                }));
        var hashAfterExternalSave = await ComputeHashAsync(vaultPath);
        var dirtyAddResult = session.AddEntry(CreateDraft("LocalDirty"));

        var staleSessionSaveResult = await session.SaveAsync();
        var finalLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var conflictFiles = Directory.GetFiles(Path.Combine(temp.Path, "backups"), "*_conflict_*.kdbx");
        var conflictLoadResult = await service.LoadAsync(Assert.Single(conflictFiles), MasterPassword);

        Assert.True(createResult.Succeeded);
        Assert.True(initialAddResult.Succeeded);
        Assert.True(initialSaveResult.Succeeded);
        Assert.True(externalLoadResult.Succeeded);
        Assert.True(externalSaveResult.Succeeded);
        Assert.True(dirtyAddResult.Succeeded);
        Assert.False(staleSessionSaveResult.Succeeded);
        Assert.Equal(VaultError.StaleVaultSnapshot, staleSessionSaveResult.Error);
        Assert.Equal(hashAfterExternalSave, await ComputeHashAsync(vaultPath));
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        Assert.True(session.HasUnsavedChanges);
        Assert.True(finalLoadResult.Succeeded);
        Assert.Equal("external update", Assert.Single(finalLoadResult.Value!.Entries).Notes);
        Assert.True(conflictLoadResult.Succeeded);
        Assert.Contains(conflictLoadResult.Value!.Entries, entry => entry.Id == initialAddResult.Value!.Id);
        Assert.Contains(conflictLoadResult.Value.Entries, entry => entry.Id == dirtyAddResult.Value!.Id);
        Assert.Empty(TemporaryFiles(temp));
    }

    private static AccountEntryDraft CreateDraft(string serviceName)
    {
        return new AccountEntryDraft(
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            $"{serviceName.ToLowerInvariant()}@example.test",
            $"password-for-{serviceName}",
            $"notes for {serviceName}",
            ["hardening"]);
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
            ["hardening"],
            isFavorite: false,
            EntryCreatedAtUtc,
            EntryUpdatedAtUtc,
            EntryPasswordChangedAtUtc);
    }

    private static async Task<string> ComputeHashAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static IReadOnlyList<string> TemporaryFiles(TemporaryVaultDirectory temp)
    {
        return Directory.GetFiles(temp.Path, "*.tmp", SearchOption.AllDirectories);
    }

    private static void TruncateVaultFile(string vaultPath)
    {
        var bytes = File.ReadAllBytes(vaultPath);
        File.WriteAllBytes(vaultPath, bytes.Take(Math.Min(bytes.Length, 32)).ToArray());
    }

    private static void ConvertToExternalVault(string vaultPath)
    {
        using var database = Database.Open(vaultPath, MasterPassword);
        database.Metadata.Name = "External Vault";
        database.Metadata.Description = "Not managed by Password Manager Vault";
        database.Save();
    }
}
