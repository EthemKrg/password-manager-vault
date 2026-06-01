namespace PasswordManager.Core;

public sealed record VaultOperationResult(
    bool Succeeded,
    VaultError Error = VaultError.None,
    string? Message = null)
{
    public static VaultOperationResult Success()
    {
        return new VaultOperationResult(true);
    }

    public static VaultOperationResult Failure(VaultError error, string? message = null)
    {
        return new VaultOperationResult(false, error, message);
    }
}

public sealed record VaultOperationResult<T>(
    bool Succeeded,
    T? Value = default,
    VaultError Error = VaultError.None,
    string? Message = null)
{
    public static VaultOperationResult<T> Success(T value)
    {
        return new VaultOperationResult<T>(true, value);
    }

    public static VaultOperationResult<T> Failure(VaultError error, string? message = null)
    {
        return new VaultOperationResult<T>(false, default, error, message);
    }
}
