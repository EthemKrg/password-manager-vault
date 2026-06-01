namespace PasswordManager.Core;

public enum VaultError
{
    None = 0,
    InvalidVaultPath,
    InvalidMasterPassword,
    InvalidEntry,
    FileNotFound,
    FileAlreadyExists,
    StaleVaultSnapshot,
    UnsupportedVaultFormat,
    UnsupportedVaultFeatures,
    OpenFailed,
    SaveFailed,
    Unknown
}
