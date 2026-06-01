using Microsoft.Maui.ApplicationModel;
using PasswordManager.Core;

namespace PasswordManager.App.Services;

public sealed class CrashProtectionService : ICrashProtectionService
{
    private MainPage? _mainPage;
    private int _isHandlingCrash;
    private bool _registered;

    public void Register(MainPage mainPage)
    {
        ArgumentNullException.ThrowIfNull(mainPage);

        if (_registered)
        {
            return;
        }

        _mainPage = mainPage;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        var windowsApplication = Microsoft.UI.Xaml.Application.Current;
        if (windowsApplication is not null)
        {
            windowsApplication.UnhandledException += OnWindowsUnhandledException;
        }

        _registered = true;
    }

    public Task ProtectAsync(Exception? exception, bool isFatal)
    {
        return ProtectAsync(CrashDiagnosticSanitizer.Create(exception, isFatal));
    }

    public async Task ProtectAsync(CrashDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (Interlocked.Exchange(ref _isHandlingCrash, 1) == 1)
        {
            return;
        }

        try
        {
            var mainPage = _mainPage;
            if (mainPage is null)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() => mainPage.HandleCrashProtectionAsync(diagnostic));
        }
        catch
        {
            // Crash protection must not create a second crash surface or log secret-bearing exception text.
        }
        finally
        {
            Interlocked.Exchange(ref _isHandlingCrash, 0);
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        _ = ProtectAsync(args.ExceptionObject as Exception, args.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _ = ProtectAsync(args.Exception, isFatal: false);
        args.SetObserved();
    }

    private void OnWindowsUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        _ = ProtectAsync(args.Exception, isFatal: true);
        args.Handled = false;
    }
}
