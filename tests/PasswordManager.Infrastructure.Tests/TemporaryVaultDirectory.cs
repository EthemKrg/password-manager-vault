namespace PasswordManager.Infrastructure.Tests;

internal sealed class TemporaryVaultDirectory : IDisposable
{
    private TemporaryVaultDirectory(string path)
    {
        Path = path;
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public static TemporaryVaultDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "PasswordManagerVaultTests",
            Guid.NewGuid().ToString("N"));

        return new TemporaryVaultDirectory(path);
    }

    public string GetVaultPath(string fileName = "test-vault.kdbx")
    {
        return System.IO.Path.Combine(Path, fileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}
