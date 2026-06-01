namespace PasswordManager.Core;

public enum VaultError
{
    None = 0,
    InvalidVaultPath,
    InvalidMasterPassword,
    FileNotFound,
    OpenFailed,
    SaveFailed,
    Unknown
}
