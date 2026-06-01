namespace PasswordManager.Core;

public sealed record VaultSnapshot(IReadOnlyList<AccountEntry> Entries)
{
    public static VaultSnapshot Empty { get; } = new([]);
}
