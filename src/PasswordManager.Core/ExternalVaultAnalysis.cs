using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed record ExternalVaultAnalysis
{
    private IReadOnlyList<ExternalVaultPreviewEntry> _entries = Array.Empty<ExternalVaultPreviewEntry>();
    private IReadOnlyList<ExternalVaultIssue> _issues = Array.Empty<ExternalVaultIssue>();
    private string? _sourceFingerprint;

    public ExternalVaultAnalysis(
        ExternalVaultAnalysisKind kind,
        IReadOnlyList<ExternalVaultPreviewEntry> entries,
        IReadOnlyList<ExternalVaultIssue> issues,
        string? sourceFingerprint)
    {
        Kind = kind;
        Entries = entries;
        Issues = issues;
        SourceFingerprint = sourceFingerprint;
    }

    public ExternalVaultAnalysisKind Kind { get; init; }

    public IReadOnlyList<ExternalVaultPreviewEntry> Entries
    {
        get => _entries;
        init => _entries = NormalizeList(value, nameof(Entries));
    }

    public IReadOnlyList<ExternalVaultIssue> Issues
    {
        get => _issues;
        init => _issues = NormalizeList(value, nameof(Issues));
    }

    public string? SourceFingerprint
    {
        get => _sourceFingerprint;
        init => _sourceFingerprint = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public bool CanStrictImport => Kind == ExternalVaultAnalysisKind.ExternalReadable && Issues.Count == 0;

    private static ReadOnlyCollection<T> NormalizeList<T>(IEnumerable<T>? values, string fieldName)
    {
        if (values is null)
        {
            throw new ArgumentException($"{fieldName} cannot be null.", fieldName);
        }

        var valueArray = values.ToArray();
        if (valueArray.Any(value => value is null))
        {
            throw new ArgumentException($"{fieldName} cannot contain null values.", fieldName);
        }

        return Array.AsReadOnly(valueArray);
    }
}
