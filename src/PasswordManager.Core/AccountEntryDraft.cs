namespace PasswordManager.Core;

public sealed record AccountEntryDraft(
    string ServiceName,
    string WebsiteUrl,
    string UsernameOrEmail,
    string Password,
    string Notes,
    IReadOnlyList<string> Tags);
