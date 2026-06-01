namespace PasswordManager.Core;

public sealed record VaultBackupOptions
{
    public const int DefaultMaxBackups = 10;

    public VaultBackupOptions(int maxBackups = DefaultMaxBackups)
    {
        if (maxBackups < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBackups), "At least one backup must be retained.");
        }

        MaxBackups = maxBackups;
    }

    public int MaxBackups { get; }
}
