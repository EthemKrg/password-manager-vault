namespace PasswordManager.App.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);

    Task<string?> GetTextAsync();

    Task<bool> ClearIfCurrentAsync(string expectedText);
}
