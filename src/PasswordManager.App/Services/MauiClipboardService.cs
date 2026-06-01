using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace PasswordManager.App.Services;

public sealed class MauiClipboardService : IClipboardService
{
    private readonly IClipboard _clipboard;

    public MauiClipboardService()
        : this(Clipboard.Default)
    {
    }

    internal MauiClipboardService(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public Task SetTextAsync(string text)
    {
        return _clipboard.SetTextAsync(text);
    }

    public Task<string?> GetTextAsync()
    {
        return _clipboard.GetTextAsync();
    }

    public async Task<bool> ClearIfCurrentAsync(string expectedText)
    {
        var currentText = await _clipboard.GetTextAsync();
        if (!StringComparer.Ordinal.Equals(currentText, expectedText))
        {
            return false;
        }

        await _clipboard.SetTextAsync(string.Empty);
        return true;
    }
}
