using DgNet.Keepass;
using PasswordManager.Core;
using System.Security.Cryptography;

namespace PasswordManager.Infrastructure;

public sealed class DgNetExternalVaultAnalyzer : IExternalVaultAnalyzer
{
    private const char TagSeparator = ';';
    private const string AppVaultName = "Password Manager Vault";
    private const string AppVaultMarker = "PasswordManagerVault:v1";
    private const int DefaultHistoryMaxItems = 10;
    private const long DefaultHistoryMaxSize = 6_291_456;
    private static readonly HashSet<string> StandardEntryStringKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title",
        "UserName",
        "Password",
        "URL",
        "Notes"
    };

    public async Task<VaultOperationResult<ExternalVaultAnalysis>> AnalyzeAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateVaultInput(vaultPath, masterPassword);
        if (validation is not null)
        {
            return VaultOperationResult<ExternalVaultAnalysis>.Failure(validation.Error, validation.Message);
        }

        if (!File.Exists(vaultPath))
        {
            return VaultOperationResult<ExternalVaultAnalysis>.Failure(VaultError.FileNotFound);
        }

        string sourceFingerprint;
        try
        {
            sourceFingerprint = await ComputeVaultFingerprintAsync(vaultPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return VaultOperationResult<ExternalVaultAnalysis>.Failure(VaultError.OpenFailed, ex.GetType().Name);
        }

        try
        {
            using var database = new Database(vaultPath, masterPassword);
            await database.OpenAsync(cancellationToken);

            var fingerprintAfterOpen = await ComputeVaultFingerprintAsync(vaultPath, cancellationToken);
            if (!string.Equals(sourceFingerprint, fingerprintAfterOpen, StringComparison.Ordinal))
            {
                return VaultOperationResult<ExternalVaultAnalysis>.Failure(
                    VaultError.StaleVaultSnapshot,
                    "Vault changed while it was being analyzed.");
            }

            if (IsAppManagedDatabase(database))
            {
                return VaultOperationResult<ExternalVaultAnalysis>.Success(
                    new ExternalVaultAnalysis(
                        ExternalVaultAnalysisKind.AppManaged,
                        [],
                        [],
                        fingerprintAfterOpen));
            }

            return VaultOperationResult<ExternalVaultAnalysis>.Success(
                AnalyzeExternalDatabase(database, fingerprintAfterOpen));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return VaultOperationResult<ExternalVaultAnalysis>.Success(
                new ExternalVaultAnalysis(
                    ExternalVaultAnalysisKind.Unreadable,
                    [],
                    [
                        new ExternalVaultIssue(
                            ExternalVaultIssueKind.UnsupportedFormatOrKdf,
                            "The vault could not be opened by the current KDBX provider.",
                            ex.GetType().Name)
                    ],
                    sourceFingerprint));
        }
    }

    private static ExternalVaultAnalysis AnalyzeExternalDatabase(Database database, string sourceFingerprint)
    {
        var issues = new List<ExternalVaultIssue>();
        AddMetadataIssues(database.Metadata, issues);
        AddGroupIssues(database.RootGroup, issues);

        var entries = database.RootGroup
            .FindAllEntries(_ => true)
            .Select(entry =>
            {
                AddEntryIssues(entry, issues);
                return MapPreviewEntry(entry);
            })
            .ToArray();

        return new ExternalVaultAnalysis(
            ExternalVaultAnalysisKind.ExternalReadable,
            entries,
            issues,
            sourceFingerprint);
    }

    private static ExternalVaultPreviewEntry MapPreviewEntry(Entry entry)
    {
        return new ExternalVaultPreviewEntry(
            entry.Title,
            entry.UserName,
            entry.Url,
            SplitTags(entry.Tags),
            !string.IsNullOrEmpty(entry.Notes),
            !string.IsNullOrEmpty(entry.Password));
    }

    private static void AddMetadataIssues(Metadata metadata, List<ExternalVaultIssue> issues)
    {
        if (metadata.CustomIcons.Count > 0)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.Icons,
                "Vault metadata contains custom icons.");
        }

        if (!string.IsNullOrEmpty(metadata.DefaultUserName)
            || metadata.RecycleBinEnabled is not true
            || metadata.RecycleBinUuid != Guid.Empty
            || metadata.HistoryMaxItems != DefaultHistoryMaxItems
            || metadata.HistoryMaxSize != DefaultHistoryMaxSize
            || metadata.ProtectPassword is not true)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.UnsupportedMetadata,
                "Vault metadata contains fields that are not preserved by the app-managed format.");
        }
    }

    private static void AddGroupIssues(Group group, List<ExternalVaultIssue> issues)
    {
        if (group.Groups.Count > 0)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.Groups,
                "Vault contains groups that would be flattened by strict import.");
        }

        if (group.IconId != 0 || group.CustomIconUuid != Guid.Empty)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.Icons,
                "Vault groups contain icons that are not preserved by the app-managed format.");
        }

        if (group.EnableAutoType is not null)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.AutoType,
                "Vault groups contain auto-type settings.");
        }

        if (HasUnsupportedTimes(group.Times))
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.UnsupportedTimestamps,
                "Vault groups contain timestamp state that is not preserved by strict import.");
        }

        foreach (var childGroup in group.Groups)
        {
            AddGroupIssues(childGroup, issues);
        }
    }

    private static void AddEntryIssues(Entry entry, List<ExternalVaultIssue> issues)
    {
        var subject = string.IsNullOrWhiteSpace(entry.Title) ? null : entry.Title;

        if (entry.Strings.Keys.Any(key => !StandardEntryStringKeys.Contains(key)))
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.CustomFields,
                "Vault entries contain custom fields.",
                subject);
        }

        if (entry.Binaries.Count > 0)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.Attachments,
                "Vault entries contain attachments.",
                subject);
        }

        if (entry.History.Count > 0)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.History,
                "Vault entries contain history.",
                subject);
        }

        if (entry.IconId != 0 || entry.CustomIconUuid != Guid.Empty)
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.Icons,
                "Vault entries contain icons.",
                subject);
        }

        if (HasUnsupportedAutoType(entry.AutoType))
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.AutoType,
                "Vault entries contain auto-type settings.",
                subject);
        }

        if (HasUnsupportedTimes(entry.Times))
        {
            AddIssue(
                issues,
                ExternalVaultIssueKind.UnsupportedTimestamps,
                "Vault entries contain timestamp state that is not preserved by strict import.",
                subject);
        }
    }

    private static void AddIssue(
        List<ExternalVaultIssue> issues,
        ExternalVaultIssueKind kind,
        string description,
        string? subject = null)
    {
        if (issues.Any(issue => issue.Kind == kind))
        {
            return;
        }

        issues.Add(new ExternalVaultIssue(kind, description, subject));
    }

    private static bool HasUnsupportedTimes(Times times)
    {
        return times.Expires
            || times.UsageCount != 0
            || times.LastAccessTime != default
            || times.ExpiryTime != default
            || times.LocationChanged != default;
    }

    private static bool HasUnsupportedAutoType(AutoType autoType)
    {
        return autoType.Enabled is not true
            || autoType.DataTransferObfuscation != 0
            || !string.IsNullOrEmpty(autoType.DefaultSequence)
            || autoType.Associations.Count > 0;
    }

    private static IReadOnlyList<string> SplitTags(string tags)
    {
        return tags
            .Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsAppManagedDatabase(Database database)
    {
        return string.Equals(database.Metadata.Name, AppVaultName, StringComparison.Ordinal)
            && string.Equals(database.Metadata.Description, AppVaultMarker, StringComparison.Ordinal);
    }

    private static VaultOperationResult? ValidateVaultInput(string vaultPath, string masterPassword)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            return VaultOperationResult.Failure(VaultError.InvalidVaultPath);
        }

        if (string.IsNullOrWhiteSpace(masterPassword))
        {
            return VaultOperationResult.Failure(VaultError.InvalidMasterPassword);
        }

        return null;
    }

    private static async Task<string> ComputeVaultFingerprintAsync(
        string vaultPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Open(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
