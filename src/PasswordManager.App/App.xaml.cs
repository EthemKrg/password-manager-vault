using PasswordManager.App.Services;

namespace PasswordManager.App;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly ICrashProtectionService _crashProtectionService;

    public App(MainPage mainPage, ICrashProtectionService crashProtectionService)
    {
        InitializeComponent();
        _mainPage = mainPage;
        _crashProtectionService = crashProtectionService;
        _crashProtectionService.Register(_mainPage);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_mainPage)
        {
            Title = "Password Manager Vault"
        };

        window.Activated += (_, _) => _mainPage.HandleWindowActivated();
        window.Resumed += (_, _) => _mainPage.HandleWindowActivated();
        window.Deactivated += async (_, _) => await _mainPage.HandleWindowDeactivatedAsync();
        window.Stopped += async (_, _) => await _mainPage.HandleWindowStoppedAsync();
        window.Destroying += async (_, _) => await _mainPage.HandleWindowDestroyingAsync();

        return window;
    }
}
