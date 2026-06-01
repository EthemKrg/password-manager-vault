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
        var pickerOpenedAtUtc = DateTimeOffset.UtcNow;
        var picker = new FileSavePicker
        {
            SuggestedFileName = "vault",
            DefaultFileExtension = ".kdbx"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("KeePass vault", [".kdbx"]);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        RemoveNewEmptyPickerPlaceholder(file.Path, pickerOpenedAtUtc);
        return file.Path;
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

    private static void RemoveNewEmptyPickerPlaceholder(string path, DateTimeOffset pickerOpenedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var fileInfo = new FileInfo(path);
        fileInfo.Refresh();
        if (fileInfo.Length != 0)
        {
            return;
        }

        var thresholdUtc = pickerOpenedAtUtc.UtcDateTime.AddSeconds(-5);
        if (fileInfo.CreationTimeUtc < thresholdUtc && fileInfo.LastWriteTimeUtc < thresholdUtc)
        {
            return;
        }

        File.Delete(path);
    }
}
