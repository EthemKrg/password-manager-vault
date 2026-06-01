namespace PasswordManager.Core;

public sealed class PasswordHealthOptions
{
    public const int DefaultMinimumLength = 14;
    public const int DefaultMinimumCharacterSetCount = 3;
    public static readonly TimeSpan DefaultMaximumPasswordAge = TimeSpan.FromDays(365);

    public PasswordHealthOptions(
        int minimumLength = DefaultMinimumLength,
        int minimumCharacterSetCount = DefaultMinimumCharacterSetCount,
        TimeSpan? maximumPasswordAge = null)
    {
        if (minimumLength < PasswordGeneratorOptions.MinimumLength
            || minimumLength > PasswordGeneratorOptions.MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLength),
                $"Minimum length must be between {PasswordGeneratorOptions.MinimumLength} and {PasswordGeneratorOptions.MaximumLength} characters.");
        }

        if (minimumCharacterSetCount is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCharacterSetCount),
                "Minimum character set count must be between 1 and 4.");
        }

        var resolvedMaximumPasswordAge = maximumPasswordAge ?? DefaultMaximumPasswordAge;
        if (resolvedMaximumPasswordAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPasswordAge),
                "Maximum password age must be positive.");
        }

        MinimumLength = minimumLength;
        MinimumCharacterSetCount = minimumCharacterSetCount;
        MaximumPasswordAge = resolvedMaximumPasswordAge;
    }

    public int MinimumLength { get; }

    public int MinimumCharacterSetCount { get; }

    public TimeSpan MaximumPasswordAge { get; }
}
