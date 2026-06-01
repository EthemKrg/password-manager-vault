namespace PasswordManager.Core;

public sealed record AccountEntry(
    Guid Id,
    string ServiceName,
    string WebsiteUrl,
    string UsernameOrEmail,
    string Password,
    string Notes,
    IReadOnlyList<string> Tags);
