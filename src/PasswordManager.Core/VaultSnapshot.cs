namespace PasswordManager.Core;

public sealed record VaultSnapshot(IReadOnlyList<AccountEntry> Entries)
{
    public static VaultSnapshot Empty { get; } = new([]);

    public VaultSnapshot Add(AccountEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Entries.Any(existing => existing.Id == entry.Id))
        {
            throw new InvalidOperationException("Entry already exists.");
        }

        return this with { Entries = Entries.Append(entry).ToArray() };
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

        return this with { Entries = entries };
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

        return this with { Entries = entries };
    }

    public IReadOnlyList<AccountEntry> Search(VaultSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var text = query.Text.Trim();
        var tags = query.Tags?
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .ToArray() ?? [];

        return Entries
            .Where(entry => MatchesText(entry, text))
            .Where(entry => MatchesTags(entry, tags))
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
}
