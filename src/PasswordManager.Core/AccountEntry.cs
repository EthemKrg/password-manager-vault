using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed record AccountEntry
{
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private Guid _id;
    private string _serviceName = string.Empty;
    private string _websiteUrl = string.Empty;
    private string _usernameOrEmail = string.Empty;
    private string _password = string.Empty;
    private string _notes = string.Empty;
    private DateTimeOffset _createdAtUtc;
    private DateTimeOffset _updatedAtUtc;
    private DateTimeOffset _passwordChangedAtUtc;

    public AccountEntry(
        Guid id,
        string serviceName,
        string websiteUrl,
        string usernameOrEmail,
        string password,
        string notes,
        IReadOnlyList<string> tags,
        bool isFavorite,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset passwordChangedAtUtc)
    {
        Id = id;
        ServiceName = serviceName;
        WebsiteUrl = websiteUrl;
        UsernameOrEmail = usernameOrEmail;
        Password = password;
        Notes = notes;
        Tags = tags;
        IsFavorite = isFavorite;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        PasswordChangedAtUtc = passwordChangedAtUtc;
        ValidateTimestampOrder();
    }

    public static AccountEntry Create(AccountEntryDraft draft)
    {
        return Create(draft, TimeProvider.System.GetUtcNow());
    }

    public static AccountEntry Create(AccountEntryDraft draft, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var normalizedNowUtc = NormalizeUtcTimestamp(nowUtc, nameof(nowUtc));

        return new AccountEntry(
            Guid.NewGuid(),
            draft.ServiceName,
            draft.WebsiteUrl,
            draft.UsernameOrEmail,
            draft.Password,
            draft.Notes,
            draft.Tags,
            draft.IsFavorite,
            normalizedNowUtc,
            normalizedNowUtc,
            normalizedNowUtc);
    }

    public Guid Id
    {
        get => _id;
        init => _id = value == Guid.Empty
            ? throw new ArgumentException("Entry id is required.", nameof(Id))
            : value;
    }

    public string ServiceName
    {
        get => _serviceName;
        init => _serviceName = NormalizeRequired(value, nameof(ServiceName), trim: true);
    }

    public string WebsiteUrl
    {
        get => _websiteUrl;
        init => _websiteUrl = NormalizeOptional(value, trim: true);
    }

    public string UsernameOrEmail
    {
        get => _usernameOrEmail;
        init => _usernameOrEmail = NormalizeOptional(value, trim: true);
    }

    public string Password
    {
        get => _password;
        init => _password = NormalizeRequired(value, nameof(Password), trim: false);
    }

    public string Notes
    {
        get => _notes;
        init => _notes = NormalizeOptional(value, trim: false);
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = NormalizeTags(value);
    }

    public bool IsFavorite { get; init; }

    public DateTimeOffset CreatedAtUtc
    {
        get => _createdAtUtc;
        init
        {
            _createdAtUtc = NormalizeUtcTimestamp(value, nameof(CreatedAtUtc));
            ValidateTimestampOrderIfComplete();
        }
    }

    public DateTimeOffset UpdatedAtUtc
    {
        get => _updatedAtUtc;
        init
        {
            _updatedAtUtc = NormalizeUtcTimestamp(value, nameof(UpdatedAtUtc));
            ValidateTimestampOrderIfComplete();
        }
    }

    public DateTimeOffset PasswordChangedAtUtc
    {
        get => _passwordChangedAtUtc;
        init
        {
            _passwordChangedAtUtc = NormalizeUtcTimestamp(value, nameof(PasswordChangedAtUtc));
            ValidateTimestampOrderIfComplete();
        }
    }

    private static string NormalizeRequired(string? value, string fieldName, bool trim)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.", fieldName);
        }

        return trim ? value.Trim() : value;
    }

    private static string NormalizeOptional(string? value, bool trim)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return trim ? value.Trim() : value;
    }

    private static ReadOnlyCollection<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }

        var normalizedTags = tags
            .Select((tag, index) => NormalizeTag(tag, index))
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Array.AsReadOnly(normalizedTags);
    }

    private static string NormalizeTag(string? tag, int index)
    {
        if (tag is null)
        {
            throw new ArgumentException($"Tag at index {index} cannot be null.", nameof(Tags));
        }

        var normalized = tag.Trim();
        if (normalized.Contains(';', StringComparison.Ordinal))
        {
            throw new ArgumentException("Tags cannot contain ';'.", nameof(Tags));
        }

        return normalized;
    }

    private static DateTimeOffset NormalizeUtcTimestamp(DateTimeOffset value, string fieldName)
    {
        if (value == default)
        {
            throw new ArgumentException($"{fieldName} is required.", fieldName);
        }

        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"{fieldName} must be UTC.", fieldName);
        }

        return value;
    }

    private void ValidateTimestampOrderIfComplete()
    {
        if (_createdAtUtc == default || _updatedAtUtc == default || _passwordChangedAtUtc == default)
        {
            return;
        }

        ValidateTimestampOrder();
    }

    private void ValidateTimestampOrder()
    {
        if (CreatedAtUtc > PasswordChangedAtUtc)
        {
            throw new ArgumentException(
                "Created timestamp cannot be later than password changed timestamp.",
                nameof(CreatedAtUtc));
        }

        if (PasswordChangedAtUtc > UpdatedAtUtc)
        {
            throw new ArgumentException(
                "Password changed timestamp cannot be later than updated timestamp.",
                nameof(PasswordChangedAtUtc));
        }
    }
}
