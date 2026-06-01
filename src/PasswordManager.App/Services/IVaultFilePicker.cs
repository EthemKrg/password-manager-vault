namespace PasswordManager.App.Services;

public interface IVaultFilePicker
{
    Task<string?> PickOpenVaultPathAsync();

    Task<string?> PickCreateVaultPathAsync();
}
