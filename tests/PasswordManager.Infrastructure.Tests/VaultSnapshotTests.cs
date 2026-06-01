using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class VaultSnapshotTests
{
    [Fact]
    public void AddUpdateDelete_ManageEntriesWithoutMutatingOriginalSnapshot()
    {
        var original = VaultSnapshot.Empty;
        var entry = CreateEntry("GitHub", "dev@example.test", ["source"]);
        var added = original.Add(entry);
        var updatedEntry = entry with { UsernameOrEmail = "updated@example.test", Notes = "Updated notes" };
        var updated = added.Update(updatedEntry);
        var deleted = updated.Delete(entry.Id);

        Assert.Empty(original.Entries);
        Assert.Equal(entry, Assert.Single(added.Entries));
        Assert.Equal(updatedEntry, Assert.Single(updated.Entries));
        Assert.Empty(deleted.Entries);
    }

    [Fact]
    public void AddUpdateDelete_RejectInvalidEntryOperations()
    {
        var entry = CreateEntry("GitHub", "dev@example.test", ["source"]);
        var snapshot = VaultSnapshot.Empty.Add(entry);
        var missingEntry = entry with { Id = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() => snapshot.Add(entry));
        Assert.Throws<KeyNotFoundException>(() => snapshot.Update(missingEntry));
        Assert.Throws<KeyNotFoundException>(() => snapshot.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void Search_FiltersByTextAndRequiredTags()
    {
        var github = CreateEntry("GitHub", "dev@example.test", ["source", "dev"]);
        var bank = CreateEntry("Bank", "money@example.test", ["finance"]);
        var mail = CreateEntry("Mail", "mail@example.test", ["personal"]);
        var snapshot = VaultSnapshot.Empty
            .Add(bank)
            .Add(mail)
            .Add(github);

        var textResults = snapshot.Search(new VaultSearchQuery("git"));
        var tagResults = snapshot.Search(new VaultSearchQuery(Tags: ["finance"]));
        var combinedResults = snapshot.Search(new VaultSearchQuery("example", ["dev", "source"]));
        var emptyResults = snapshot.Search(VaultSearchQuery.Empty);

        Assert.Equal(github.Id, Assert.Single(textResults).Id);
        Assert.Equal(bank.Id, Assert.Single(tagResults).Id);
        Assert.Equal(github.Id, Assert.Single(combinedResults).Id);
        Assert.Equal(["Bank", "GitHub", "Mail"], emptyResults.Select(entry => entry.ServiceName).ToArray());
    }

    private static AccountEntry CreateEntry(string serviceName, string usernameOrEmail, IReadOnlyList<string> tags)
    {
        return DgNetVaultService.CreateEntry(new AccountEntryDraft(
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            usernameOrEmail,
            $"password-for-{serviceName}",
            $"notes for {serviceName}",
            tags));
    }
}
