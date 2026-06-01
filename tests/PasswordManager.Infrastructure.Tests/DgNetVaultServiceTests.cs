using System.Text;
using DgNet.Keepass;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class DgNetVaultServiceTests
{
    private const string MasterPassword = "correct horse battery staple - automated tests only";
    private const string WrongPassword = "wrong password - automated tests only";
    private const string AppVaultName = "Password Manager Vault";
    private const string AppVaultMarker = "PasswordManagerVault:v1";
    private const string AppFavoriteKey = "PMV.IsFavorite";
    private const string AppCreatedAtKey = "PMV.CreatedAtUtc";
    private const string AppUpdatedAtKey = "PMV.UpdatedAtUtc";
    private const string AppPasswordChangedAtKey = "PMV.PasswordChangedAtUtc";
    private static readonly DateTimeOffset EntryCreatedAtUtc = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset EntryPasswordChangedAtUtc = EntryCreatedAtUtc.AddMinutes(7);
    private static readonly DateTimeOffset EntryUpdatedAtUtc = EntryPasswordChangedAtUtc.AddMinutes(11);

    [Fact]
    public async Task CreateAsync_CreatesEmptyVaultAndDoesNotOverwriteExistingVault()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();

        var createResult = await service.CreateAsync(vaultPath, MasterPassword);
        var secondCreateResult = await service.CreateAsync(vaultPath, MasterPassword);
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(createResult.Succeeded);
        Assert.False(secondCreateResult.Succeeded);
        Assert.Equal(VaultError.FileAlreadyExists, secondCreateResult.Error);
        Assert.True(loadResult.Succeeded);
        Assert.Empty(loadResult.Value!.Entries);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundtripsEntriesAndRejectsWrongPassword()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var entry = CreateEntry();
        var snapshot = VaultSnapshot.Empty.Add(entry);

        var saveResult = await service.SaveAsync(vaultPath, MasterPassword, snapshot);
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var wrongPasswordResult = await service.LoadAsync(vaultPath, WrongPassword);

        Assert.True(saveResult.Succeeded);
        Assert.True(loadResult.Succeeded);
        Assert.False(wrongPasswordResult.Succeeded);
        Assert.Equal(VaultError.OpenFailed, wrongPasswordResult.Error);

        var loadedEntry = Assert.Single(loadResult.Value!.Entries);
        AssertEntryEqual(entry, loadedEntry);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundtripsFavoriteAndTimestampMetadata()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var entry = CreateEntry(isFavorite: true);
        var snapshot = VaultSnapshot.Empty.Add(entry);

        var saveResult = await service.SaveAsync(vaultPath, MasterPassword, snapshot);
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(saveResult.Succeeded);
        Assert.True(loadResult.Succeeded);

        var loadedEntry = Assert.Single(loadResult.Value!.Entries);
        AssertEntryEqual(entry, loadedEntry);

        using var database = Database.Open(vaultPath, MasterPassword);
        var rawEntry = Assert.Single(database.RootGroup.Entries);
        AssertMetadataString(rawEntry, AppFavoriteKey, "true");
        AssertMetadataString(rawEntry, AppCreatedAtKey, EntryCreatedAtUtc.ToString("O"));
        AssertMetadataString(rawEntry, AppUpdatedAtKey, EntryUpdatedAtUtc.ToString("O"));
        AssertMetadataString(rawEntry, AppPasswordChangedAtKey, EntryPasswordChangedAtUtc.ToString("O"));
        Assert.Equal(EntryCreatedAtUtc.UtcDateTime, rawEntry.Times.CreationTime);
        Assert.Equal(EntryUpdatedAtUtc.UtcDateTime, rawEntry.Times.LastModificationTime);
    }

    [Fact]
    public async Task LoadAndSaveAsync_MetadataLessAppVaultUsesKeePassTimesAndWritesAppMetadata()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        CreateMarkerSpoofedVault(
            vaultPath,
            database =>
            {
                var entry = database.RootGroup.Entries.Single();
                entry.Times = new Times
                {
                    CreationTime = EntryCreatedAtUtc.UtcDateTime,
                    LastModificationTime = EntryUpdatedAtUtc.UtcDateTime
                };
            });

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(loadResult.Succeeded);
        var loadedEntry = Assert.Single(loadResult.Value!.Entries);
        var saveResult = await service.SaveAsync(vaultPath, MasterPassword, loadResult.Value!);

        Assert.False(loadedEntry.IsFavorite);
        Assert.Equal(EntryCreatedAtUtc, loadedEntry.CreatedAtUtc);
        Assert.Equal(EntryUpdatedAtUtc, loadedEntry.UpdatedAtUtc);
        Assert.Equal(EntryUpdatedAtUtc, loadedEntry.PasswordChangedAtUtc);
        Assert.True(saveResult.Succeeded);

        using var database = Database.Open(vaultPath, MasterPassword);
        var rawEntry = Assert.Single(database.RootGroup.Entries);
        AssertMetadataString(rawEntry, AppFavoriteKey, "false");
        AssertMetadataString(rawEntry, AppPasswordChangedAtKey, EntryUpdatedAtUtc.ToString("O"));
    }

    [Fact]
    public async Task SaveAsync_DoesNotExposePlaintextOrLeaveTemporaryFiles()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var entry = CreateEntry(
            serviceName: "Sensitive Service",
            usernameOrEmail: "sensitive.user@example.test",
            password: "SENSITIVE-PASSWORD-8fd20d5d",
            notes: "private note marker 52d1");

        var saveResult = await service.SaveAsync(vaultPath, MasterPassword, VaultSnapshot.Empty.Add(entry));

        Assert.True(saveResult.Succeeded);
        Assert.True(File.Exists(vaultPath));

        var fileText = Encoding.UTF8.GetString(File.ReadAllBytes(vaultPath));
        Assert.DoesNotContain(entry.ServiceName, fileText, StringComparison.Ordinal);
        Assert.DoesNotContain(entry.UsernameOrEmail, fileText, StringComparison.Ordinal);
        Assert.DoesNotContain(entry.Password, fileText, StringComparison.Ordinal);
        Assert.DoesNotContain(entry.Notes, fileText, StringComparison.Ordinal);

        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        Assert.Equal([vaultPath], Directory.GetFiles(temp.Path, "*.kdbx"));
    }

    [Fact]
    public async Task SaveAsync_PersistsUpdatedAndDeletedSnapshots()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var firstEntry = CreateEntry(serviceName: "GitHub", usernameOrEmail: "first@example.test");
        var secondEntry = CreateEntry(serviceName: "Mail", usernameOrEmail: "second@example.test");
        var initialSnapshot = VaultSnapshot.Empty
            .Add(firstEntry)
            .Add(secondEntry);

        var initialSaveResult = await service.SaveAsync(vaultPath, MasterPassword, initialSnapshot);
        var initialLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var loadedSnapshot = initialLoadResult.Value!;
        var loadedFirstEntry = loadedSnapshot.Entries.Single(entry => entry.Id == firstEntry.Id);
        var updatedFirstEntry = loadedFirstEntry with
        {
            UsernameOrEmail = "updated@example.test",
            Notes = "Updated after initial save"
        };
        var updatedSnapshot = loadedSnapshot
            .Update(updatedFirstEntry)
            .Delete(secondEntry.Id);
        var updateSaveResult = await service.SaveAsync(vaultPath, MasterPassword, updatedSnapshot);
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(initialSaveResult.Succeeded);
        Assert.True(initialLoadResult.Succeeded);
        Assert.True(updateSaveResult.Succeeded);
        Assert.True(loadResult.Succeeded);

        var loadedEntry = Assert.Single(loadResult.Value!.Entries);
        AssertEntryEqual(updatedFirstEntry, loadedEntry);
    }

    [Fact]
    public async Task SaveAsync_RejectsStaleSnapshotWhenVaultChangedOnDisk()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var originalEntry = CreateEntry(serviceName: "Original", usernameOrEmail: "original@example.test");

        var initialSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(originalEntry));
        var staleLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var currentLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var externalUpdate = currentLoadResult.Value!.Update(
            currentLoadResult.Value.Entries.Single() with
            {
                Notes = "Saved by another process"
            });
        var externalSaveResult = await service.SaveAsync(vaultPath, MasterPassword, externalUpdate);
        var staleUpdate = staleLoadResult.Value!.Update(
            staleLoadResult.Value.Entries.Single() with
            {
                Notes = "Stale local edit"
            });

        var staleSaveResult = await service.SaveAsync(vaultPath, MasterPassword, staleUpdate);
        var finalLoadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(initialSaveResult.Succeeded);
        Assert.True(staleLoadResult.Succeeded);
        Assert.True(currentLoadResult.Succeeded);
        Assert.True(externalSaveResult.Succeeded);
        Assert.False(staleSaveResult.Succeeded);
        Assert.Equal(VaultError.StaleVaultSnapshot, staleSaveResult.Error);
        Assert.True(finalLoadResult.Succeeded);
        Assert.Equal("Saved by another process", Assert.Single(finalLoadResult.Value!.Entries).Notes);
    }

    [Fact]
    public async Task SaveAsync_RequiresLoadedSnapshotWhenOverwritingExistingVault()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var originalEntry = CreateEntry(serviceName: "Original", usernameOrEmail: "original@example.test");
        var replacementEntry = CreateEntry(serviceName: "Replacement", usernameOrEmail: "replacement@example.test");

        var initialSaveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(originalEntry));
        var overwriteResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(replacementEntry));
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(initialSaveResult.Succeeded);
        Assert.False(overwriteResult.Succeeded);
        Assert.Equal(VaultError.StaleVaultSnapshot, overwriteResult.Error);
        Assert.True(loadResult.Succeeded);
        AssertEntryEqual(originalEntry, Assert.Single(loadResult.Value!.Entries));
    }

    [Fact]
    public async Task SaveAsync_WithWrongPasswordFailsAndPreservesExistingVault()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var originalEntry = CreateEntry(serviceName: "Original", usernameOrEmail: "original@example.test");
        var replacementEntry = CreateEntry(serviceName: "Replacement", usernameOrEmail: "replacement@example.test");

        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(originalEntry));
        var wrongPasswordSaveResult = await service.SaveAsync(
            vaultPath,
            WrongPassword,
            VaultSnapshot.Empty.Add(replacementEntry));
        var loadWithOriginalPasswordResult = await service.LoadAsync(vaultPath, MasterPassword);
        var loadWithWrongPasswordResult = await service.LoadAsync(vaultPath, WrongPassword);

        Assert.True(saveResult.Succeeded);
        Assert.False(wrongPasswordSaveResult.Succeeded);
        Assert.Equal(VaultError.OpenFailed, wrongPasswordSaveResult.Error);
        Assert.True(loadWithOriginalPasswordResult.Succeeded);
        Assert.False(loadWithWrongPasswordResult.Succeeded);

        var loadedEntry = Assert.Single(loadWithOriginalPasswordResult.Value!.Entries);
        AssertEntryEqual(originalEntry, loadedEntry);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectVaultsNotCreatedByThisApp()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        var unmanagedEntry = CreateEntry(serviceName: "External", usernameOrEmail: "external@example.test");
        CreateUnmanagedVault(vaultPath, unmanagedEntry);

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry(serviceName: "Replacement")));

        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFormat, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFormat, saveResult.Error);

        using var database = Database.Open(vaultPath, MasterPassword);
        var preservedEntry = Assert.Single(database.RootGroup.Entries);
        Assert.Equal(unmanagedEntry.ServiceName, preservedEntry.Title);
        Assert.Equal(unmanagedEntry.UsernameOrEmail, preservedEntry.UserName);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectAppManagedVaultsWithUnsupportedStructure()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();

        var createResult = await service.CreateAsync(vaultPath, MasterPassword);
        AddUnsupportedGroup(vaultPath);

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry()));

        Assert.True(createResult.Succeeded);
        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, saveResult.Error);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectMarkerSpoofedVaultsWithUnsupportedMetadata()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        CreateMarkerSpoofedVault(
            vaultPath,
            database => database.Metadata.CustomIcons.Add(new CustomIcon
            {
                Name = "external custom icon",
                Data = [1, 2, 3]
            }));

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry(serviceName: "Replacement")));

        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, saveResult.Error);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectMarkerSpoofedVaultsWithUnsupportedEntryFeatures()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        CreateMarkerSpoofedVault(
            vaultPath,
            database =>
            {
                var entry = database.RootGroup.Entries.Single();
                entry.IconId = 7;
                entry.Times.Expires = true;
                entry.Times.ExpiryTime = DateTime.UtcNow.AddDays(1);
                entry.AutoType.Associations.Add(new AutoTypeAssociation
                {
                    Window = "*",
                    Sequence = "{PASSWORD}"
                });
            });

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry(serviceName: "Replacement")));

        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, saveResult.Error);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectUnknownEntryCustomStrings()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        CreateMarkerSpoofedVault(
            vaultPath,
            database =>
            {
                var entry = database.RootGroup.Entries.Single();
                entry.Strings["External.CustomField"] = CreateEntryString("external value");
            });

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry(serviceName: "Replacement")));

        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, saveResult.Error);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectInvalidAppEntryMetadata()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        CreateMarkerSpoofedVault(
            vaultPath,
            database =>
            {
                var entry = database.RootGroup.Entries.Single();
                entry.Strings[AppFavoriteKey] = CreateEntryString("True");
                entry.Strings[AppCreatedAtKey] = CreateEntryString("not-a-date");
            });

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry(serviceName: "Replacement")));

        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, saveResult.Error);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RejectProtectedAppEntryMetadata()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var service = new DgNetVaultService();
        var vaultPath = temp.GetVaultPath();
        CreateMarkerSpoofedVault(
            vaultPath,
            database =>
            {
                var entry = database.RootGroup.Entries.Single();
                entry.Strings[AppFavoriteKey] = CreateEntryString("true", protectedValue: true);
            });

        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
        var saveResult = await service.SaveAsync(
            vaultPath,
            MasterPassword,
            VaultSnapshot.Empty.Add(CreateEntry(serviceName: "Replacement")));

        Assert.False(loadResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, loadResult.Error);
        Assert.False(saveResult.Succeeded);
        Assert.Equal(VaultError.UnsupportedVaultFeatures, saveResult.Error);
    }

    private static AccountEntry CreateEntry(
        string serviceName = "GitHub",
        string websiteUrl = "https://github.com",
        string usernameOrEmail = "dev@example.test",
        string password = "SPK-AUTOMATED-TEST-1f97",
        string notes = "Automated test entry",
        IReadOnlyList<string>? tags = null,
        bool isFavorite = false)
    {
        return new AccountEntry(
            Guid.NewGuid(),
            serviceName,
            websiteUrl,
            usernameOrEmail,
            password,
            notes,
            tags ?? ["dev", "source"],
            isFavorite,
            EntryCreatedAtUtc,
            EntryUpdatedAtUtc,
            EntryPasswordChangedAtUtc);
    }

    private static void AssertEntryEqual(AccountEntry expected, AccountEntry actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ServiceName, actual.ServiceName);
        Assert.Equal(expected.WebsiteUrl, actual.WebsiteUrl);
        Assert.Equal(expected.UsernameOrEmail, actual.UsernameOrEmail);
        Assert.Equal(expected.Password, actual.Password);
        Assert.Equal(expected.Notes, actual.Notes);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.IsFavorite, actual.IsFavorite);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Assert.Equal(expected.PasswordChangedAtUtc, actual.PasswordChangedAtUtc);
    }

    private static void AssertMetadataString(Entry entry, string key, string expectedValue)
    {
        Assert.True(entry.Strings.TryGetValue(key, out var entryString));
        Assert.Equal(expectedValue, entryString.Value);
        Assert.False(entryString.Protected);
    }

    private static EntryString CreateEntryString(string value, bool protectedValue = false)
    {
        return new EntryString
        {
            Value = value,
            Protected = protectedValue
        };
    }

    private static void CreateUnmanagedVault(string vaultPath, AccountEntry entry)
    {
        using var database = Database.Create(MasterPassword);
        database.RootGroup.AddEntry(new Entry
        {
            Uuid = entry.Id,
            Title = entry.ServiceName,
            UserName = entry.UsernameOrEmail,
            Password = entry.Password,
            Url = entry.WebsiteUrl,
            Notes = entry.Notes,
            Tags = string.Join(';', entry.Tags)
        });

        database.SaveAs(vaultPath);
    }

    private static void CreateMarkerSpoofedVault(string vaultPath, Action<Database> mutate)
    {
        using var database = Database.Create(MasterPassword);
        database.Metadata.Name = AppVaultName;
        database.Metadata.Description = AppVaultMarker;
        database.RootGroup.AddEntry(new Entry
        {
            Title = "External",
            UserName = "external@example.test",
            Password = "external password",
            Url = "https://external.example.test",
            Notes = "External marker spoof",
            Tags = "external"
        });
        mutate(database);
        database.SaveAs(vaultPath);
    }

    private static void AddUnsupportedGroup(string vaultPath)
    {
        using var database = Database.Open(vaultPath, MasterPassword);
        database.RootGroup.AddGroup(new Group { Name = "Unsupported nested group" });
        database.Save();
    }
}
