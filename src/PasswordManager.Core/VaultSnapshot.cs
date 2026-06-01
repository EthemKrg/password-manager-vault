using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed record VaultSnapshot
{
    private IReadOnlyList<AccountEntry> _entries = Array.Empty<AccountEntry>();

    public VaultSnapshot(IReadOnlyList<AccountEntry> entries)
    {
        Entries = entries;
    }

    public static VaultSnapshot Empty { get; } = new([]);

    public IReadOnlyList<AccountEntry> Entries
    {
        get => _entries;
        init => _entries = NormalizeEntries(value);
    }

    public VaultSnapshot Add(AccountEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Entries.Any(existing => existing.Id == entry.Id))
        {
            throw new InvalidOperationException("Entry already exists.");
        }

        return new VaultSnapshot(Entries.Append(entry).ToArray());
    }

    public VaultSnapshot Update(AccountEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var found = false;
        var entries = Entries
            .Select(existing =>
            {
                if (existing.Id != entry.Id)
                {
                    return existing;
                }

                found = true;
                return entry;
            })
            .ToArray();

        if (!found)
        {
            throw new KeyNotFoundException("Entry does not exist.");
        }

        return new VaultSnapshot(entries);
    }

    public VaultSnapshot Delete(Guid entryId)
    {
        var entries = Entries
            .Where(entry => entry.Id != entryId)
            .ToArray();

        if (entries.Length == Entries.Count)
        {
            throw new KeyNotFoundException("Entry does not exist.");
        }

        return new VaultSnapshot(entries);
    }

    public IReadOnlyList<AccountEntry> Search(VaultSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Entries
            .Where(entry => MatchesText(entry, query.Text))
            .Where(entry => MatchesTags(entry, query.Tags))
            .OrderBy(entry => entry.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.UsernameOrEmail, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesText(AccountEntry entry, string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        return Contains(entry.ServiceName, text)
            || Contains(entry.WebsiteUrl, text)
            || Contains(entry.UsernameOrEmail, text)
            || Contains(entry.Notes, text)
            || entry.Tags.Any(tag => Contains(tag, text));
    }

    private static bool MatchesTags(AccountEntry entry, IReadOnlyCollection<string> tags)
    {
        if (tags.Count == 0)
        {
            return true;
        }

        return tags.All(requiredTag =>
            entry.Tags.Any(entryTag => string.Equals(entryTag, requiredTag, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool Contains(string value, string text)
    {
        return value.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlyCollection<AccountEntry> NormalizeEntries(IEnumerable<AccountEntry>? entries)
    {
        if (entries is null)
        {
            throw new ArgumentException("Entries cannot be null.", nameof(Entries));
        }

        var entryArray = entries.ToArray();
        if (entryArray.Any(entry => entry is null))
        {
            throw new ArgumentException("Entries cannot contain null values.", nameof(Entries));
        }

        if (entryArray.GroupBy(entry => entry.Id).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Entries cannot contain duplicate ids.", nameof(Entries));
        }

        return Array.AsReadOnly(entryArray);
    }
}
