namespace PasswordManager.Core;

public interface IExternalVaultAnalyzer
{
    Task<VaultOperationResult<ExternalVaultAnalysis>> AnalyzeAsync(
        string vaultPath,
        string masterPassword,
        CancellationToken cancellationToken = default);
}
