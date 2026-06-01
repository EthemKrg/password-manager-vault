namespace PasswordManager.Core;

public sealed record PasswordHealthIssue(
    Guid EntryId,
    PasswordHealthIssueKind Kind,
    int RelatedEntryCount = 1);
