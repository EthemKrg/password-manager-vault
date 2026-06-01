using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class SecurePasswordGeneratorTests
{
    private static readonly char[] SymbolCharacters = "!@#$%^&*()-_=+[]{};:,.?".ToCharArray();

    [Fact]
    public void Generate_DefaultOptions_ReturnsTwentyCharacters()
    {
        var generator = new SecurePasswordGenerator();

        var password = generator.Generate(new PasswordGeneratorOptions());

        Assert.Equal(PasswordGeneratorOptions.DefaultLength, password.Length);
        Assert.DoesNotContain(password, char.IsWhiteSpace);
    }

    [Fact]
    public void Generate_AllCharacterSets_GuaranteesAtLeastOneFromEachSet()
    {
        var generator = new SecurePasswordGenerator();

        var password = generator.Generate(new PasswordGeneratorOptions(length: 32));

        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, character => SymbolCharacters.Contains(character));
    }

    [Fact]
    public void Generate_SelectedCharacterSets_UsesOnlySelectedSets()
    {
        var generator = new SecurePasswordGenerator();

        var password = generator.Generate(new PasswordGeneratorOptions(
            length: 24,
            includeUppercase: false,
            includeLowercase: true,
            includeDigits: true,
            includeSymbols: false));

        Assert.Equal(24, password.Length);
        Assert.DoesNotContain(password, char.IsUpper);
        Assert.DoesNotContain(password, character => SymbolCharacters.Contains(character));
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
    }

    [Fact]
    public void PasswordGeneratorOptions_RejectsInvalidLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordGeneratorOptions(length: 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordGeneratorOptions(length: 129));
    }

    [Fact]
    public void PasswordGeneratorOptions_RejectsNoSelectedCharacterSets()
    {
        Assert.Throws<ArgumentException>(() => new PasswordGeneratorOptions(
            includeUppercase: false,
            includeLowercase: false,
            includeDigits: false,
            includeSymbols: false));
    }

    [Fact]
    public void Generate_NullOptions_Throws()
    {
        var generator = new SecurePasswordGenerator();

        Assert.Throws<ArgumentNullException>(() => generator.Generate(null!));
    }
}
