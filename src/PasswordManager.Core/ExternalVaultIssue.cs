namespace PasswordManager.Core;

public sealed record ExternalVaultIssue
{
    private string _description = string.Empty;
    private string? _subject;

    public ExternalVaultIssue(
        ExternalVaultIssueKind kind,
        string description,
        string? subject = null)
    {
        Kind = kind;
        Description = description;
        Subject = subject;
    }

    public ExternalVaultIssueKind Kind { get; init; }

    public string Description
    {
        get => _description;
        init => _description = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Description is required.", nameof(Description))
            : value.Trim();
    }

    public string? Subject
    {
        get => _subject;
        init => _subject = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
