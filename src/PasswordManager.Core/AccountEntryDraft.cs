using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed record AccountEntryDraft
{
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private string _serviceName = string.Empty;
    private string _websiteUrl = string.Empty;
    private string _usernameOrEmail = string.Empty;
    private string _password = string.Empty;
    private string _notes = string.Empty;

    public AccountEntryDraft(
        string serviceName,
        string websiteUrl,
        string usernameOrEmail,
        string password,
        string notes,
        IReadOnlyList<string> tags)
    {
        ServiceName = serviceName;
        WebsiteUrl = websiteUrl;
        UsernameOrEmail = usernameOrEmail;
        Password = password;
        Notes = notes;
        Tags = tags;
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
}
