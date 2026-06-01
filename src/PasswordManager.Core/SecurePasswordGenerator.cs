using System.Security.Cryptography;

namespace PasswordManager.Core;

public sealed class SecurePasswordGenerator : IPasswordGenerator
{
    private const string UppercaseCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseCharacters = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitCharacters = "0123456789";
    private const string SymbolCharacters = "!@#$%^&*()-_=+[]{};:,.?";

    public string Generate(PasswordGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var selectedSets = GetSelectedCharacterSets(options).ToArray();
        var allCharacters = string.Concat(selectedSets);
        var generated = new List<char>(options.Length);

        foreach (var characterSet in selectedSets)
        {
            generated.Add(RandomCharacter(characterSet));
        }

        while (generated.Count < options.Length)
        {
            generated.Add(RandomCharacter(allCharacters));
        }

        Shuffle(generated);
        return new string(generated.ToArray());
    }

    private static IEnumerable<string> GetSelectedCharacterSets(PasswordGeneratorOptions options)
    {
        if (options.IncludeUppercase)
        {
            yield return UppercaseCharacters;
        }

        if (options.IncludeLowercase)
        {
            yield return LowercaseCharacters;
        }

        if (options.IncludeDigits)
        {
            yield return DigitCharacters;
        }

        if (options.IncludeSymbols)
        {
            yield return SymbolCharacters;
        }
    }

    private static char RandomCharacter(string characters)
    {
        return characters[RandomNumberGenerator.GetInt32(characters.Length)];
    }

    private static void Shuffle(IList<char> characters)
    {
        for (var index = characters.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swapIndex]) = (characters[swapIndex], characters[index]);
        }
    }
}
