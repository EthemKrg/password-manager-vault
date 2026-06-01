using Microsoft.Extensions.Logging;
using PasswordManager.App.Services;
using PasswordManager.Core;
using PasswordManager.Infrastructure;

namespace PasswordManager.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IVaultService, DgNetVaultService>();
        builder.Services.AddSingleton<IExternalVaultAnalyzer, DgNetExternalVaultAnalyzer>();
        builder.Services.AddSingleton<IVaultBackupService, FileSystemVaultBackupService>();
        builder.Services.AddSingleton<IVaultSession>(services =>
            new VaultSession(
                services.GetRequiredService<IVaultService>(),
                vaultBackupService: services.GetRequiredService<IVaultBackupService>()));
        builder.Services.AddSingleton<IVaultFilePicker, WindowsVaultFilePicker>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
