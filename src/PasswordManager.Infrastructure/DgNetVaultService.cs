using DgNet.Keepass;
using PasswordManager.Core;

namespace PasswordManager.Infrastructure;

public sealed class DgNetVaultService : IVaultService
{
    private const char TagSeparator = ';';

    public async Task<VaultOperationResult> CreateAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateVaultInput(vaultPath, masterPassword);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            EnsureParentDirectory(vaultPath);

            using var database = Database.Create(masterPassword);
            await database.SaveAsAsync(vaultPath, cancellationToken);

            return VaultOperationResult.Success();
        }
        catch (Exception ex)
        {
            return VaultOperationResult.Failure(VaultError.SaveFailed, ex.GetType().Name);
        }
    }

    public async Task<VaultOperationResult<VaultSnapshot>> LoadAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateVaultInput(vaultPath, masterPassword);
        if (validation is not null)
        {
            return VaultOperationResult<VaultSnapshot>.Failure(validation.Error, validation.Message);
        }

        if (!File.Exists(vaultPath))
        {
            return VaultOperationResult<VaultSnapshot>.Failure(VaultError.FileNotFound);
        }

        try
        {
            using var database = new Database(vaultPath, masterPassword);
            await database.OpenAsync(cancellationToken);

            var entries = database.RootGroup
                .FindAllEntries(_ => true)
                .Select(MapEntry)
                .ToArray();

            return VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot(entries));
        }
        catch (UnauthorizedAccessException ex)
        {
            return VaultOperationResult<VaultSnapshot>.Failure(VaultError.OpenFailed, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return VaultOperationResult<VaultSnapshot>.Failure(VaultError.OpenFailed, ex.GetType().Name);
        }
    }

    public async Task<VaultOperationResult> SaveAsync(
        string vaultPath,
        string masterPassword,
        VaultSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var validation = ValidateVaultInput(vaultPath, masterPassword);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            EnsureParentDirectory(vaultPath);

            using var database = Database.Create(masterPassword);
            foreach (var account in snapshot.Entries)
            {
                database.RootGroup.AddEntry(MapEntry(account));
            }

            await database.SaveAsAsync(vaultPath, cancellationToken);
            return VaultOperationResult.Success();
        }
        catch (Exception ex)
        {
            return VaultOperationResult.Failure(VaultError.SaveFailed, ex.GetType().Name);
        }
    }

    public static AccountEntry CreateEntry(AccountEntryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new AccountEntry(
            Guid.NewGuid(),
            draft.ServiceName.Trim(),
            draft.WebsiteUrl.Trim(),
            draft.UsernameOrEmail.Trim(),
            draft.Password,
            draft.Notes,
            NormalizeTags(draft.Tags));
    }

    private static AccountEntry MapEntry(Entry entry)
    {
        return new AccountEntry(
            entry.Uuid,
            entry.Title,
            entry.Url,
            entry.UserName,
            entry.Password,
            entry.Notes,
            SplitTags(entry.Tags));
    }

    private static Entry MapEntry(AccountEntry account)
    {
        return new Entry
        {
            Uuid = account.Id,
            Title = account.ServiceName,
            Url = account.WebsiteUrl,
            UserName = account.UsernameOrEmail,
            Password = account.Password,
            Notes = account.Notes,
            Tags = string.Join(TagSeparator, NormalizeTags(account.Tags))
        };
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitTags(string tags)
    {
        return tags
            .Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static void EnsureParentDirectory(string vaultPath)
    {
        var parentDirectory = Path.GetDirectoryName(Path.GetFullPath(vaultPath));
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }
    }
}
