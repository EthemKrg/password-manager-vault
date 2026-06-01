using Microsoft.Maui.Platform;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace PasswordManager.App.Services;

public sealed class WindowsVaultFilePicker : IVaultFilePicker
{
    public async Task<string?> PickOpenVaultPathAsync()
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".kdbx");

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickCreateVaultPathAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = "vault",
            DefaultFileExtension = ".kdbx"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("KeePass vault", [".kdbx"]);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static void InitializePicker(object picker)
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not MauiWinUIWindow platformWindow)
        {
            throw new InvalidOperationException("A Windows app window is required before opening a file picker.");
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(platformWindow));
    }
}
