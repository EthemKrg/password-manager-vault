namespace PasswordManager.Core;

public sealed class PasswordHealthAnalyzer : IPasswordHealthAnalyzer
{
    private readonly TimeProvider _timeProvider;

    public PasswordHealthAnalyzer(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PasswordHealthReport Analyze(VaultSnapshot snapshot, PasswordHealthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var resolvedOptions = options ?? new PasswordHealthOptions();
        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var issues = new List<PasswordHealthIssue>();

        foreach (var entry in snapshot.Entries)
        {
            if (IsWeak(entry.Password, resolvedOptions))
            {
                issues.Add(new PasswordHealthIssue(entry.Id, PasswordHealthIssueKind.WeakPassword));
            }

            if (nowUtc - entry.PasswordChangedAtUtc > resolvedOptions.MaximumPasswordAge)
            {
                issues.Add(new PasswordHealthIssue(entry.Id, PasswordHealthIssueKind.OldPassword));
            }
        }

        foreach (var reusedGroup in snapshot.Entries
                     .GroupBy(entry => entry.Password, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var relatedEntryCount = reusedGroup.Count();
            issues.AddRange(reusedGroup.Select(entry =>
                new PasswordHealthIssue(
                    entry.Id,
                    PasswordHealthIssueKind.ReusedPassword,
                    relatedEntryCount)));
        }

        return new PasswordHealthReport(issues);
    }

    private static bool IsWeak(string password, PasswordHealthOptions options)
    {
        return password.Length < options.MinimumLength
            || CountCharacterSets(password) < options.MinimumCharacterSetCount;
    }

    private static int CountCharacterSets(string password)
    {
        var count = 0;
        count += password.Any(char.IsUpper) ? 1 : 0;
        count += password.Any(char.IsLower) ? 1 : 0;
        count += password.Any(char.IsDigit) ? 1 : 0;
        count += password.Any(IsSymbol) ? 1 : 0;
        return count;
    }

    private static bool IsSymbol(char character)
    {
        return !char.IsLetterOrDigit(character);
    }
}
