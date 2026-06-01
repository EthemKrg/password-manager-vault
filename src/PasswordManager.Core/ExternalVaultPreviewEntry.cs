using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed record ExternalVaultPreviewEntry
{
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private string _serviceName = string.Empty;
    private string _usernameOrEmail = string.Empty;
    private string _websiteUrl = string.Empty;

    public ExternalVaultPreviewEntry(
        string serviceName,
        string usernameOrEmail,
        string websiteUrl,
        IReadOnlyList<string> tags,
        bool hasNotes,
        bool hasPassword)
    {
        ServiceName = NormalizeOptional(serviceName, trim: true);
        UsernameOrEmail = NormalizeOptional(usernameOrEmail, trim: true);
        WebsiteUrl = NormalizeOptional(websiteUrl, trim: true);
        Tags = tags;
        HasNotes = hasNotes;
        HasPassword = hasPassword;
    }

    public string ServiceName
    {
        get => _serviceName;
        init => _serviceName = NormalizeOptional(value, trim: true);
    }

    public string UsernameOrEmail
    {
        get => _usernameOrEmail;
        init => _usernameOrEmail = NormalizeOptional(value, trim: true);
    }

    public string WebsiteUrl
    {
        get => _websiteUrl;
        init => _websiteUrl = NormalizeOptional(value, trim: true);
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = NormalizeTags(value);
    }

    public bool HasNotes { get; init; }

    public bool HasPassword { get; init; }

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
            .Select(tag => tag?.Trim() ?? string.Empty)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Array.AsReadOnly(normalizedTags);
    }
}
