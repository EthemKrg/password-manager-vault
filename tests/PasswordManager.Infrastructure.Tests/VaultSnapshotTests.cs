using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class VaultSnapshotTests
{
    [Fact]
    public void AddUpdateDelete_ManageEntriesWithoutMutatingOriginalSnapshot()
    {
        var original = VaultSnapshot.Empty;
        var entry = CreateEntry("GitHub", "dev@example.test", ["source"]);
        var snapshotFromFile = new VaultSnapshot([], "ABC123");
        var added = snapshotFromFile.Add(entry);
        var updatedEntry = entry with { UsernameOrEmail = "updated@example.test", Notes = "Updated notes" };
        var updated = added.Update(updatedEntry);
        var deleted = updated.Delete(entry.Id);

        Assert.Empty(original.Entries);
        Assert.Equal(entry, Assert.Single(added.Entries));
        Assert.Equal(updatedEntry, Assert.Single(updated.Entries));
        Assert.Empty(deleted.Entries);
        Assert.Equal("ABC123", added.SourceFingerprint);
        Assert.Equal("ABC123", updated.SourceFingerprint);
        Assert.Equal("ABC123", deleted.SourceFingerprint);
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

    [Fact]
    public void Constructors_DefensivelyCopyMutableLists()
    {
        var tags = new List<string> { "source" };
        var entry = CreateEntry("GitHub", "dev@example.test", tags);
        tags.Add("mutated");

        var entries = new List<AccountEntry> { entry };
        var snapshot = new VaultSnapshot(entries);
        entries.Clear();

        Assert.Equal(["source"], entry.Tags);
        Assert.Equal(entry.Id, Assert.Single(snapshot.Entries).Id);
    }

    [Fact]
    public void Constructors_RejectInvalidRequiredFieldsAndTags()
    {
        var entry = CreateEntry("Example", "user@example.test", []);

        Assert.Throws<ArgumentException>(() => new AccountEntry(
            Guid.Empty,
            "Example",
            "",
            "",
            "password",
            "",
            []));
        Assert.Throws<ArgumentException>(() => entry with { Id = Guid.Empty });
        Assert.Throws<ArgumentException>(() => new AccountEntryDraft(
            " ",
            "https://example.test",
            "user@example.test",
            "password",
            "",
            []));
        Assert.Throws<ArgumentException>(() => new AccountEntryDraft(
            "Example",
            "https://example.test",
            "user@example.test",
            " ",
            "",
            []));
        Assert.Throws<ArgumentException>(() => new AccountEntryDraft(
            "Example",
            "",
            "",
            "password",
            "",
            ["valid", "bad;tag"]));
        Assert.Throws<ArgumentException>(() => new AccountEntryDraft(
            "Example",
            "",
            "",
            "password",
            "",
            new List<string?> { "valid", null! }!));
    }

    [Fact]
    public void SearchQuery_NormalizesNullsAndRejectsDelimitedTags()
    {
        var query = new VaultSearchQuery(null, null);

        Assert.Equal(string.Empty, query.Text);
        Assert.Empty(query.Tags);
        Assert.Throws<ArgumentException>(() => new VaultSearchQuery("test", ["bad;tag"]));
    }

    private static AccountEntry CreateEntry(string serviceName, string usernameOrEmail, IReadOnlyList<string> tags)
    {
        return AccountEntry.Create(new AccountEntryDraft(
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            usernameOrEmail,
            $"password-for-{serviceName}",
            $"notes for {serviceName}",
            tags));
    }
}
