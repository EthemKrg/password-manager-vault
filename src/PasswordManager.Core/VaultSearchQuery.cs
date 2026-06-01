using System.Collections.ObjectModel;

namespace PasswordManager.Core;

public sealed record VaultSearchQuery
{
    private string _text = string.Empty;
    private IReadOnlyList<string> _tags = Array.Empty<string>();

    public VaultSearchQuery(string? Text = "", IReadOnlyList<string>? Tags = null)
    {
        this.Text = Text ?? string.Empty;
        this.Tags = Tags ?? Array.Empty<string>();
    }

    public static VaultSearchQuery Empty { get; } = new();

    public string Text
    {
        get => _text;
        init => _text = value?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = NormalizeTags(value);
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
