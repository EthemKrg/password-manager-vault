using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class PasswordHealthAnalyzerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly PasswordHealthOptions TestOptions = new(
        minimumLength: 12,
        minimumCharacterSetCount: 3,
        maximumPasswordAge: TimeSpan.FromDays(180));

    [Fact]
    public void Analyze_StrongUniqueRecentPasswords_ReturnsNoIssues()
    {
        var analyzer = new PasswordHealthAnalyzer(new FixedTimeProvider(NowUtc));
        var snapshot = new VaultSnapshot(
            [
                CreateEntry("GitHub", "StrongUnique1!"),
                CreateEntry("Mail", "DifferentStrong2?")
            ],
            "fp");

        var report = analyzer.Analyze(snapshot, TestOptions);

        Assert.False(report.HasIssues);
        Assert.Equal(0, report.AffectedEntryCount);
    }

    [Fact]
    public void Analyze_WeakPassword_ReturnsWeakIssueWithoutPasswordValue()
    {
        var analyzer = new PasswordHealthAnalyzer(new FixedTimeProvider(NowUtc));
        var entry = CreateEntry("GitHub", "short1");
        var snapshot = new VaultSnapshot([entry], "fp");

        var report = analyzer.Analyze(snapshot, TestOptions);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(entry.Id, issue.EntryId);
        Assert.Equal(PasswordHealthIssueKind.WeakPassword, issue.Kind);
        Assert.DoesNotContain(
            report.Issues,
            issue => issue.ToString()!.Contains(entry.Password, StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReusedPassword_FlagsEveryEntryInReuseGroup()
    {
        var analyzer = new PasswordHealthAnalyzer(new FixedTimeProvider(NowUtc));
        var first = CreateEntry("GitHub", "SharedStrong1!");
        var second = CreateEntry("Mail", "SharedStrong1!");
        var third = CreateEntry("Bank", "UniqueStrong2?");
        var snapshot = new VaultSnapshot([first, second, third], "fp");

        var report = analyzer.Analyze(snapshot, TestOptions);

        var reuseIssues = report.Issues
            .Where(issue => issue.Kind == PasswordHealthIssueKind.ReusedPassword)
            .ToArray();
        Assert.Equal(2, reuseIssues.Length);
        Assert.All(reuseIssues, issue => Assert.Equal(2, issue.RelatedEntryCount));
        Assert.Contains(reuseIssues, issue => issue.EntryId == first.Id);
        Assert.Contains(reuseIssues, issue => issue.EntryId == second.Id);
        Assert.DoesNotContain(reuseIssues, issue => issue.EntryId == third.Id);
    }

    [Fact]
    public void Analyze_OldPassword_UsesPasswordChangedTimestamp()
    {
        var analyzer = new PasswordHealthAnalyzer(new FixedTimeProvider(NowUtc));
        var oldEntry = CreateEntry(
            "GitHub",
            "StrongUnique1!",
            passwordChangedAtUtc: NowUtc.AddDays(-181),
            updatedAtUtc: NowUtc);
        var recentEntry = CreateEntry("Mail", "DifferentStrong2?");
        var snapshot = new VaultSnapshot([oldEntry, recentEntry], "fp");

        var report = analyzer.Analyze(snapshot, TestOptions);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(oldEntry.Id, issue.EntryId);
        Assert.Equal(PasswordHealthIssueKind.OldPassword, issue.Kind);
    }

    [Fact]
    public void Analyze_NullSnapshot_Throws()
    {
        var analyzer = new PasswordHealthAnalyzer(new FixedTimeProvider(NowUtc));

        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(null!));
    }

    [Fact]
    public void PasswordHealthOptions_RejectsInvalidPolicy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHealthOptions(minimumLength: 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHealthOptions(minimumCharacterSetCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHealthOptions(maximumPasswordAge: TimeSpan.Zero));
    }

    private static AccountEntry CreateEntry(
        string serviceName,
        string password,
        DateTimeOffset? passwordChangedAtUtc = null,
        DateTimeOffset? updatedAtUtc = null)
    {
        var resolvedPasswordChangedAtUtc = passwordChangedAtUtc ?? NowUtc.AddDays(-1);
        var createdAtUtc = resolvedPasswordChangedAtUtc.AddDays(-1);
        var resolvedUpdatedAtUtc = updatedAtUtc ?? resolvedPasswordChangedAtUtc;

        return new AccountEntry(
            Guid.NewGuid(),
            serviceName,
            $"https://{serviceName.ToLowerInvariant()}.example.test",
            $"{serviceName.ToLowerInvariant()}@example.test",
            password,
            string.Empty,
            [],
            isFavorite: false,
            createdAtUtc,
            resolvedUpdatedAtUtc,
            resolvedPasswordChangedAtUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return nowUtc;
        }
    }
}
