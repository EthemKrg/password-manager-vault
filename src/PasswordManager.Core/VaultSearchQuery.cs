namespace PasswordManager.Core;

public sealed record VaultSearchQuery(string Text = "", IReadOnlyList<string>? Tags = null)
{
    public static VaultSearchQuery Empty { get; } = new();
}
