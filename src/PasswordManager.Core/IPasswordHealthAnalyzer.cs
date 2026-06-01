namespace PasswordManager.Core;

public interface IPasswordHealthAnalyzer
{
    PasswordHealthReport Analyze(VaultSnapshot snapshot, PasswordHealthOptions? options = null);
}
