using System.Text;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class DgNetVaultServiceTests
{
    private const string MasterPassword = "correct horse battery staple - automated tests only";
    private const string WrongPassword = "wrong password - automated tests only";

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
        var updatedFirstEntry = firstEntry with
        {
            UsernameOrEmail = "updated@example.test",
            Notes = "Updated after initial save"
        };
        var updatedSnapshot = initialSnapshot
            .Update(updatedFirstEntry)
            .Delete(secondEntry.Id);
        var updateSaveResult = await service.SaveAsync(vaultPath, MasterPassword, updatedSnapshot);
        var loadResult = await service.LoadAsync(vaultPath, MasterPassword);

        Assert.True(initialSaveResult.Succeeded);
        Assert.True(updateSaveResult.Succeeded);
        Assert.True(loadResult.Succeeded);

        var loadedEntry = Assert.Single(loadResult.Value!.Entries);
        AssertEntryEqual(updatedFirstEntry, loadedEntry);
    }

    private static AccountEntry CreateEntry(
        string serviceName = "GitHub",
        string websiteUrl = "https://github.com",
        string usernameOrEmail = "dev@example.test",
        string password = "SPK-AUTOMATED-TEST-1f97",
        string notes = "Automated test entry",
        IReadOnlyList<string>? tags = null)
    {
        return DgNetVaultService.CreateEntry(new AccountEntryDraft(
            serviceName,
            websiteUrl,
            usernameOrEmail,
            password,
            notes,
            tags ?? ["dev", "source"]));
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
    }
}
