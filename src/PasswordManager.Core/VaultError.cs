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
    VaultLocked,
    NoVaultLoaded,
    UnsavedChanges,
    EntryNotFound,
    EntryAlreadyExists,
    UnsupportedVaultFormat,
    UnsupportedVaultFeatures,
    OpenFailed,
    SaveFailed,
    Unknown
}
