namespace PasswordManager.Core;

public enum VaultError
{
    None = 0,
    InvalidVaultPath,
    InvalidMasterPassword,
    InvalidEntry,
    FileNotFound,
    FileAlreadyExists,
    UnsupportedVaultFormat,
    UnsupportedVaultFeatures,
    OpenFailed,
    SaveFailed,
    Unknown
}
