namespace PasswordManager.Core;

public sealed class PasswordGeneratorOptions
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 128;
    public const int DefaultLength = 20;

    public PasswordGeneratorOptions(
        int length = DefaultLength,
        bool includeUppercase = true,
        bool includeLowercase = true,
        bool includeDigits = true,
        bool includeSymbols = true)
    {
        if (length < MinimumLength || length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                $"Password length must be between {MinimumLength} and {MaximumLength} characters.");
        }

        var selectedSetCount = CountSelectedSets(includeUppercase, includeLowercase, includeDigits, includeSymbols);
        if (selectedSetCount == 0)
        {
            throw new ArgumentException("At least one password character set must be selected.", nameof(includeLowercase));
        }

        if (length < selectedSetCount)
        {
            throw new ArgumentException("Password length must fit every selected character set.", nameof(length));
        }

        Length = length;
        IncludeUppercase = includeUppercase;
        IncludeLowercase = includeLowercase;
        IncludeDigits = includeDigits;
        IncludeSymbols = includeSymbols;
        SelectedCharacterSetCount = selectedSetCount;
    }

    public int Length { get; }

    public bool IncludeUppercase { get; }

    public bool IncludeLowercase { get; }

    public bool IncludeDigits { get; }

    public bool IncludeSymbols { get; }

    internal int SelectedCharacterSetCount { get; }

    private static int CountSelectedSets(
        bool includeUppercase,
        bool includeLowercase,
        bool includeDigits,
        bool includeSymbols)
    {
        var count = 0;
        count += includeUppercase ? 1 : 0;
        count += includeLowercase ? 1 : 0;
        count += includeDigits ? 1 : 0;
        count += includeSymbols ? 1 : 0;
        return count;
    }
}
