namespace PasswordManager.Core;

public interface IPasswordGenerator
{
    string Generate(PasswordGeneratorOptions options);
}
