using DgNet.Keepass;
using PasswordManager.Core;
using System.Globalization;
using System.Security.Cryptography;

namespace PasswordManager.Infrastructure;

public sealed class DgNetVaultService : IVaultService
{
    private const char TagSeparator = ';';
    private const string AppVaultName = "Password Manager Vault";
    private const string AppVaultMarker = "PasswordManagerVault:v1";
    private const string AppFavoriteKey = "PMV.IsFavorite";
    private const string AppCreatedAtKey = "PMV.CreatedAtUtc";
    private const string AppUpdatedAtKey = "PMV.UpdatedAtUtc";
    private const string AppPasswordChangedAtKey = "PMV.PasswordChangedAtUtc";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
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
    private static readonly HashSet<string> AppEntryStringKeys = new(StringComparer.Ordinal)
    {
        AppFavoriteKey,
        AppCreatedAtKey,
        AppUpdatedAtKey,
        AppPasswordChangedAtKey
    };

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

            if (File.Exists(vaultPath))
            {
                return VaultOperationResult.Failure(VaultError.FileAlreadyExists);
            }

            using var database = Database.Create(masterPassword);
            MarkAsAppManaged(database);
            await SaveDatabaseReplacingTargetAsync(database, vaultPath, cancellationToken);

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
            var fingerprintBeforeOpen = await ComputeVaultFingerprintAsync(vaultPath, cancellationToken);

            using var database = new Database(vaultPath, masterPassword);
            await database.OpenAsync(cancellationToken);

            var fingerprintAfterOpen = await ComputeVaultFingerprintAsync(vaultPath, cancellationToken);
            if (!string.Equals(fingerprintBeforeOpen, fingerprintAfterOpen, StringComparison.Ordinal))
            {
                return VaultOperationResult<VaultSnapshot>.Failure(
                    VaultError.StaleVaultSnapshot,
                    "Vault changed while it was being loaded.");
            }

            var metadataFallbackTimestamp = DateTimeOffset.UtcNow;
            var supportResult = ValidateAppManagedDatabase(database, metadataFallbackTimestamp);
            if (supportResult is not null)
            {
                return VaultOperationResult<VaultSnapshot>.Failure(supportResult.Error, supportResult.Message);
            }

            var entries = database.RootGroup
                .FindAllEntries(_ => true)
                .Select(entry => MapEntry(entry, metadataFallbackTimestamp))
                .ToArray();

            return VaultOperationResult<VaultSnapshot>.Success(new VaultSnapshot(entries, fingerprintAfterOpen));
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

            if (File.Exists(vaultPath))
            {
                var existingVaultResult = await ValidateExistingVaultForRewriteAsync(
                    vaultPath,
                    masterPassword,
                    snapshot,
                    cancellationToken);

                if (!existingVaultResult.Succeeded)
                {
                    return existingVaultResult;
                }
            }

            await SaveSnapshotReplacingTargetAsync(vaultPath, masterPassword, snapshot, cancellationToken);
            return VaultOperationResult.Success();
        }
        catch (ArgumentException ex)
        {
            return VaultOperationResult.Failure(VaultError.InvalidEntry, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return VaultOperationResult.Failure(VaultError.SaveFailed, ex.GetType().Name);
        }
    }

    public async Task<VaultOperationResult> ChangeMasterPasswordAsync(
        string vaultPath,
        string currentMasterPassword,
        string newMasterPassword,
        VaultSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var currentValidation = ValidateVaultInput(vaultPath, currentMasterPassword);
        if (currentValidation is not null)
        {
            return currentValidation;
        }

        if (string.IsNullOrWhiteSpace(newMasterPassword))
        {
            return VaultOperationResult.Failure(VaultError.InvalidMasterPassword);
        }

        if (string.Equals(currentMasterPassword, newMasterPassword, StringComparison.Ordinal))
        {
            return VaultOperationResult.Failure(VaultError.InvalidMasterPassword);
        }

        if (!File.Exists(vaultPath))
        {
            return VaultOperationResult.Failure(VaultError.FileNotFound);
        }

        try
        {
            EnsureParentDirectory(vaultPath);

            var existingVaultResult = await ValidateExistingVaultForRewriteAsync(
                vaultPath,
                currentMasterPassword,
                snapshot,
                cancellationToken);
            if (!existingVaultResult.Succeeded)
            {
                return existingVaultResult;
            }

            await SaveSnapshotReplacingTargetAsync(vaultPath, newMasterPassword, snapshot, cancellationToken);
            return VaultOperationResult.Success();
        }
        catch (ArgumentException ex)
        {
            return VaultOperationResult.Failure(VaultError.InvalidEntry, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return VaultOperationResult.Failure(VaultError.SaveFailed, ex.GetType().Name);
        }
    }

    public static AccountEntry CreateEntry(AccountEntryDraft draft)
    {
        return AccountEntry.Create(draft);
    }

    private static AccountEntry MapEntry(Entry entry, DateTimeOffset metadataFallbackTimestamp)
    {
        if (!TryReadAppEntryMetadata(entry, metadataFallbackTimestamp, out var metadata))
        {
            throw new ArgumentException("Entry metadata is invalid.", nameof(entry));
        }

        return new AccountEntry(
            entry.Uuid,
            entry.Title,
            entry.Url,
            entry.UserName,
            entry.Password,
            entry.Notes,
            SplitTags(entry.Tags),
            metadata.IsFavorite,
            metadata.CreatedAtUtc,
            metadata.UpdatedAtUtc,
            metadata.PasswordChangedAtUtc);
    }

    private static Entry MapEntry(AccountEntry account)
    {
        var entry = new Entry
        {
            Uuid = account.Id,
            Title = account.ServiceName,
            Url = account.WebsiteUrl,
            UserName = account.UsernameOrEmail,
            Password = account.Password,
            Notes = account.Notes,
            Tags = string.Join(TagSeparator, NormalizeTags(account.Tags)),
            Times = new Times
            {
                CreationTime = account.CreatedAtUtc.UtcDateTime,
                LastModificationTime = account.UpdatedAtUtc.UtcDateTime
            }
        };

        SetAppEntryString(entry, AppFavoriteKey, account.IsFavorite ? TrueValue : FalseValue);
        SetAppEntryString(entry, AppCreatedAtKey, SerializeTimestamp(account.CreatedAtUtc));
        SetAppEntryString(entry, AppUpdatedAtKey, SerializeTimestamp(account.UpdatedAtUtc));
        SetAppEntryString(entry, AppPasswordChangedAtKey, SerializeTimestamp(account.PasswordChangedAtUtc));

        return entry;
    }

    private static void SetAppEntryString(Entry entry, string key, string value)
    {
        entry.Strings[key] = new EntryString
        {
            Value = value,
            Protected = false
        };
    }

    private static string SerializeTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToString("O", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select((tag, index) => NormalizeTag(tag, index))
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

    private static string NormalizeTag(string? tag, int index)
    {
        if (tag is null)
        {
            throw new ArgumentException($"Tag at index {index} cannot be null.", nameof(tag));
        }

        var normalized = tag.Trim();
        if (normalized.Contains(TagSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Tags cannot contain '{TagSeparator}'.", nameof(tag));
        }

        return normalized;
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

    private static async Task<VaultOperationResult> ValidateExistingVaultForRewriteAsync(
        string vaultPath,
        string masterPassword,
        VaultSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            var fingerprintBeforeOpen = await ComputeVaultFingerprintAsync(vaultPath, cancellationToken);

            using var existingDatabase = new Database(vaultPath, masterPassword);
            await existingDatabase.OpenAsync(cancellationToken);

            var fingerprintAfterOpen = await ComputeVaultFingerprintAsync(vaultPath, cancellationToken);
            if (!string.Equals(fingerprintBeforeOpen, fingerprintAfterOpen, StringComparison.Ordinal))
            {
                return VaultOperationResult.Failure(
                    VaultError.StaleVaultSnapshot,
                    "Vault changed while it was being validated.");
            }

            var supportResult = ValidateAppManagedDatabase(existingDatabase, DateTimeOffset.UtcNow);
            if (supportResult is not null)
            {
                return supportResult;
            }

            if (!string.Equals(snapshot.SourceFingerprint, fingerprintAfterOpen, StringComparison.Ordinal))
            {
                return VaultOperationResult.Failure(
                    VaultError.StaleVaultSnapshot,
                    "Snapshot is not based on the current vault file.");
            }

            return VaultOperationResult.Success();
        }
        catch (Exception ex)
        {
            return VaultOperationResult.Failure(VaultError.OpenFailed, ex.GetType().Name);
        }
    }

    private static VaultOperationResult? ValidateAppManagedDatabase(
        Database database,
        DateTimeOffset metadataFallbackTimestamp)
    {
        if (!IsAppManagedDatabase(database))
        {
            return VaultOperationResult.Failure(VaultError.UnsupportedVaultFormat);
        }

        if (HasUnsupportedFeatures(database, metadataFallbackTimestamp))
        {
            return VaultOperationResult.Failure(VaultError.UnsupportedVaultFeatures);
        }

        return null;
    }

    private static void MarkAsAppManaged(Database database)
    {
        database.Metadata.Name = AppVaultName;
        database.Metadata.Description = AppVaultMarker;
    }

    private static bool IsAppManagedDatabase(Database database)
    {
        return string.Equals(database.Metadata.Name, AppVaultName, StringComparison.Ordinal)
            && string.Equals(database.Metadata.Description, AppVaultMarker, StringComparison.Ordinal);
    }

    private static bool HasUnsupportedFeatures(Database database, DateTimeOffset metadataFallbackTimestamp)
    {
        if (HasUnsupportedMetadata(database.Metadata) || HasUnsupportedRootGroupFeatures(database.RootGroup))
        {
            return true;
        }

        return database.RootGroup
            .FindAllEntries(_ => true)
            .Any(entry => HasUnsupportedEntryFeatures(entry, metadataFallbackTimestamp));
    }

    private static bool HasUnsupportedMetadata(Metadata metadata)
    {
        return !string.IsNullOrEmpty(metadata.DefaultUserName)
            || metadata.CustomIcons.Count > 0
            || metadata.RecycleBinEnabled is not true
            || metadata.RecycleBinUuid != Guid.Empty
            || metadata.HistoryMaxItems != DefaultHistoryMaxItems
            || metadata.HistoryMaxSize != DefaultHistoryMaxSize
            || metadata.ProtectPassword is not true;
    }

    private static bool HasUnsupportedRootGroupFeatures(Group rootGroup)
    {
        return rootGroup.Groups.Count > 0
            || !string.Equals(rootGroup.Name, "Root", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(rootGroup.Notes)
            || rootGroup.IconId != 0
            || rootGroup.CustomIconUuid != Guid.Empty
            || rootGroup.EnableAutoType is not null
            || rootGroup.EnableSearching is not null
            || HasUnsupportedTimes(rootGroup.Times);
    }

    private static bool HasUnsupportedEntryFeatures(Entry entry, DateTimeOffset metadataFallbackTimestamp)
    {
        return entry.IconId != 0
            || entry.CustomIconUuid != Guid.Empty
            || !string.IsNullOrEmpty(entry.ForegroundColor)
            || !string.IsNullOrEmpty(entry.BackgroundColor)
            || !string.IsNullOrEmpty(entry.OverrideUrl)
            || HasUnsupportedTimes(entry.Times)
            || HasUnsupportedAutoType(entry.AutoType)
            || HasUnsupportedEntryStrings(entry)
            || HasInvalidAppEntryMetadata(entry, metadataFallbackTimestamp)
            || entry.History.Count > 0
            || entry.Binaries.Count > 0;
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

    private static bool HasUnsupportedEntryStrings(Entry entry)
    {
        if (entry.Strings.Keys.Any(key => !IsSupportedEntryStringKey(key)))
        {
            return true;
        }

        return HasUnexpectedProtection(entry, "Title", protectedValue: false)
            || HasUnexpectedProtection(entry, "UserName", protectedValue: false)
            || HasUnexpectedProtection(entry, "Password", protectedValue: true)
            || HasUnexpectedProtection(entry, "URL", protectedValue: false)
            || HasUnexpectedProtection(entry, "Notes", protectedValue: false)
            || AppEntryStringKeys.Any(key => HasUnexpectedProtection(entry, key, protectedValue: false));
    }

    private static bool HasUnexpectedProtection(Entry entry, string key, bool protectedValue)
    {
        return entry.Strings.TryGetValue(key, out var value)
            && value.Protected != protectedValue;
    }

    private static bool IsSupportedEntryStringKey(string key)
    {
        return StandardEntryStringKeys.Contains(key) || AppEntryStringKeys.Contains(key);
    }

    private static bool HasInvalidAppEntryMetadata(Entry entry, DateTimeOffset metadataFallbackTimestamp)
    {
        return !TryReadAppEntryMetadata(entry, metadataFallbackTimestamp, out _);
    }

    private static bool TryReadAppEntryMetadata(
        Entry entry,
        DateTimeOffset fallbackTimestamp,
        out AppEntryMetadata metadata)
    {
        metadata = default;

        if (!TryReadAppFavorite(entry, out var isFavorite)
            || !TryReadAppTimestamp(
                entry,
                AppCreatedAtKey,
                TimestampFromKeePass(entry.Times.CreationTime, fallbackTimestamp),
                out var createdAtUtc)
            || !TryReadAppTimestamp(
                entry,
                AppUpdatedAtKey,
                TimestampFromKeePass(entry.Times.LastModificationTime, createdAtUtc),
                out var updatedAtUtc)
            || !TryReadAppTimestamp(
                entry,
                AppPasswordChangedAtKey,
                updatedAtUtc,
                out var passwordChangedAtUtc)
            || createdAtUtc > passwordChangedAtUtc
            || passwordChangedAtUtc > updatedAtUtc)
        {
            return false;
        }

        metadata = new AppEntryMetadata(isFavorite, createdAtUtc, updatedAtUtc, passwordChangedAtUtc);
        return true;
    }

    private static bool TryReadAppFavorite(Entry entry, out bool isFavorite)
    {
        isFavorite = false;

        if (!entry.Strings.TryGetValue(AppFavoriteKey, out var entryString))
        {
            return true;
        }

        if (entryString.Value == TrueValue)
        {
            isFavorite = true;
            return true;
        }

        if (entryString.Value == FalseValue)
        {
            return true;
        }

        return false;
    }

    private static bool TryReadAppTimestamp(
        Entry entry,
        string key,
        DateTimeOffset fallbackTimestamp,
        out DateTimeOffset timestamp)
    {
        timestamp = fallbackTimestamp;

        return !entry.Strings.TryGetValue(key, out var entryString)
            || TryParseTimestamp(entryString.Value, out timestamp);
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp))
        {
            return false;
        }

        return timestamp != default && timestamp.Offset == TimeSpan.Zero;
    }

    private static DateTimeOffset TimestampFromKeePass(DateTime timestamp, DateTimeOffset fallbackTimestamp)
    {
        if (timestamp == default)
        {
            return fallbackTimestamp;
        }

        var utcTimestamp = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
            : timestamp.ToUniversalTime();
        return new DateTimeOffset(utcTimestamp);
    }

    private readonly record struct AppEntryMetadata(
        bool IsFavorite,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset PasswordChangedAtUtc);

    private static async Task<string> ComputeVaultFingerprintAsync(
        string vaultPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Open(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task SaveDatabaseReplacingTargetAsync(
        Database database,
        string vaultPath,
        CancellationToken cancellationToken)
    {
        var fullVaultPath = Path.GetFullPath(vaultPath);
        var parentDirectory = Path.GetDirectoryName(fullVaultPath) ?? Directory.GetCurrentDirectory();
        var temporaryPath = Path.Combine(parentDirectory, $".{Path.GetFileName(fullVaultPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await database.SaveAsAsync(temporaryPath, cancellationToken);

            if (File.Exists(fullVaultPath))
            {
                File.Replace(temporaryPath, fullVaultPath, null);
            }
            else
            {
                File.Move(temporaryPath, fullVaultPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task SaveSnapshotReplacingTargetAsync(
        string vaultPath,
        string masterPassword,
        VaultSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        using var database = Database.Create(masterPassword);
        MarkAsAppManaged(database);
        foreach (var account in snapshot.Entries)
        {
            database.RootGroup.AddEntry(MapEntry(account));
        }

        await SaveDatabaseReplacingTargetAsync(database, vaultPath, cancellationToken);
    }
}
