using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed class PasswordHealthReport
{
    public static PasswordHealthReport Empty { get; } = new([]);

    public PasswordHealthReport(IReadOnlyList<PasswordHealthIssue> issues)
    {
        Issues = new ReadOnlyCollection<PasswordHealthIssue>(
            (issues ?? throw new ArgumentNullException(nameof(issues))).ToArray());
    }

    public IReadOnlyList<PasswordHealthIssue> Issues { get; }

    public bool HasIssues => Issues.Count > 0;

    public int AffectedEntryCount => Issues
        .Select(issue => issue.EntryId)
        .Distinct()
        .Count();

    public IReadOnlyList<PasswordHealthIssue> GetIssuesForEntry(Guid entryId)
    {
        return Issues
            .Where(issue => issue.EntryId == entryId)
            .ToArray();
    }
}
