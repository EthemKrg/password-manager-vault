using System.Security.Cryptography;
using DgNet.Keepass;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class DgNetExternalVaultAnalyzerTests
{
    private const string MasterPassword = "correct horse battery staple - analyzer tests only";
    private const string WrongPassword = "wrong password - analyzer tests only";
    private const string SensitivePassword = "SENSITIVE-ANALYZER-PASSWORD-61fd";

    [Fact]
    public async Task AnalyzeAsync_AppManagedVaultReturnsAppManagedAndDoesNotModifySource()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var service = new DgNetVaultService();
        var analyzer = new DgNetExternalVaultAnalyzer();

        var createResult = await service.CreateAsync(vaultPath, MasterPassword);
        var hashBefore = await ComputeHashAsync(vaultPath);
        var result = await analyzer.AnalyzeAsync(vaultPath, MasterPassword);
        var hashAfter = await ComputeHashAsync(vaultPath);

        Assert.True(createResult.Succeeded);
        Assert.True(result.Succeeded);
        Assert.Equal(ExternalVaultAnalysisKind.AppManaged, result.Value!.Kind);
        Assert.False(result.Value.CanStrictImport);
        Assert.Empty(result.Value.Entries);
        Assert.Empty(result.Value.Issues);
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(hashBefore, result.Value.SourceFingerprint);
    }

    [Fact]
    public async Task AnalyzeAsync_FlatExternalVaultReturnsReadOnlyPreviewWithoutPasswordValue()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var analyzer = new DgNetExternalVaultAnalyzer();
        CreateFlatExternalVault(vaultPath);

        var hashBefore = await ComputeHashAsync(vaultPath);
        var result = await analyzer.AnalyzeAsync(vaultPath, MasterPassword);
        var hashAfter = await ComputeHashAsync(vaultPath);

        Assert.True(result.Succeeded);
        Assert.Equal(ExternalVaultAnalysisKind.ExternalReadable, result.Value!.Kind);
        Assert.True(result.Value.CanStrictImport);
        Assert.Empty(result.Value.Issues);
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(hashBefore, result.Value.SourceFingerprint);

        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal("External GitHub", entry.ServiceName);
        Assert.Equal("external@example.test", entry.UsernameOrEmail);
        Assert.Equal("https://github.example.test", entry.WebsiteUrl);
        Assert.Equal(["external", "source"], entry.Tags);
        Assert.True(entry.HasNotes);
        Assert.True(entry.HasPassword);

        var previewText = string.Join(
            "|",
            entry.ServiceName,
            entry.UsernameOrEmail,
            entry.WebsiteUrl,
            string.Join(",", entry.Tags));
        Assert.DoesNotContain(SensitivePassword, previewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsUnsupportedExternalFeaturesWithoutModifyingSource()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var analyzer = new DgNetExternalVaultAnalyzer();
        CreateUnsupportedExternalVault(vaultPath);

        var hashBefore = await ComputeHashAsync(vaultPath);
        var result = await analyzer.AnalyzeAsync(vaultPath, MasterPassword);
        var hashAfter = await ComputeHashAsync(vaultPath);

        Assert.True(result.Succeeded);
        Assert.Equal(ExternalVaultAnalysisKind.ExternalReadable, result.Value!.Kind);
        Assert.False(result.Value.CanStrictImport);
        Assert.Equal(hashBefore, hashAfter);

        var issueKinds = result.Value.Issues
            .Select(issue => issue.Kind)
            .Order()
            .ToArray();
        Assert.Equal(
            [
                ExternalVaultIssueKind.Groups,
                ExternalVaultIssueKind.CustomFields,
                ExternalVaultIssueKind.Attachments,
                ExternalVaultIssueKind.History,
                ExternalVaultIssueKind.Icons,
                ExternalVaultIssueKind.AutoType,
                ExternalVaultIssueKind.UnsupportedMetadata,
                ExternalVaultIssueKind.UnsupportedTimestamps
            ],
            issueKinds);
    }

    [Fact]
    public async Task AnalyzeAsync_WrongPasswordReturnsUnreadableAndDoesNotModifySource()
    {
        using var temp = TemporaryVaultDirectory.Create();
        var vaultPath = temp.GetVaultPath();
        var analyzer = new DgNetExternalVaultAnalyzer();
        CreateFlatExternalVault(vaultPath);

        var hashBefore = await ComputeHashAsync(vaultPath);
        var result = await analyzer.AnalyzeAsync(vaultPath, WrongPassword);
        var hashAfter = await ComputeHashAsync(vaultPath);

        Assert.True(result.Succeeded);
        Assert.Equal(ExternalVaultAnalysisKind.Unreadable, result.Value!.Kind);
        Assert.False(result.Value.CanStrictImport);
        Assert.Empty(result.Value.Entries);
        Assert.Contains(
            result.Value.Issues,
            issue => issue.Kind == ExternalVaultIssueKind.UnsupportedFormatOrKdf);
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(hashBefore, result.Value.SourceFingerprint);
    }

    private static void CreateFlatExternalVault(string vaultPath)
    {
        using var database = Database.Create(MasterPassword);
        database.RootGroup.AddEntry(CreateExternalEntry());
        database.SaveAs(vaultPath);
    }

    private static void CreateUnsupportedExternalVault(string vaultPath)
    {
        using var database = Database.Create(MasterPassword);
        database.Metadata.DefaultUserName = "external-default-user";
        database.Metadata.CustomIcons.Add(new CustomIcon
        {
            Name = "external-icon",
            Data = [1, 2, 3]
        });

        var rootEntry = CreateExternalEntry();
        rootEntry.Strings["External.CustomField"] = new EntryString
        {
            Value = "external custom value",
            Protected = false
        };
        rootEntry.Binaries.Add(new EntryBinary
        {
            Name = "attachment.txt",
            Data = [4, 5, 6]
        });
        rootEntry.History.Add(rootEntry.Clone());
        rootEntry.IconId = 7;
        rootEntry.AutoType.Associations.Add(new AutoTypeAssociation
        {
            Window = "*",
            Sequence = "{USERNAME}{TAB}{PASSWORD}"
        });
        rootEntry.Times.Expires = true;
        rootEntry.Times.ExpiryTime = DateTime.UtcNow.AddDays(1);
        database.RootGroup.AddEntry(rootEntry);

        var nestedGroup = new Group
        {
            Name = "Nested",
            IconId = 4,
            EnableAutoType = false,
            Times = new Times
            {
                Expires = true,
                ExpiryTime = DateTime.UtcNow.AddDays(2)
            }
        };
        nestedGroup.AddEntry(CreateExternalEntry("Nested External"));
        database.RootGroup.AddGroup(nestedGroup);

        database.SaveAs(vaultPath);
    }

    private static Entry CreateExternalEntry(string title = "External GitHub")
    {
        return new Entry
        {
            Title = title,
            UserName = "external@example.test",
            Password = SensitivePassword,
            Url = "https://github.example.test",
            Notes = "external notes",
            Tags = "external;source"
        };
    }

    private static async Task<string> ComputeHashAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}
