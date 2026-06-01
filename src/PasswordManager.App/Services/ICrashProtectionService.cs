using PasswordManager.Core;

namespace PasswordManager.App.Services;

public interface ICrashProtectionService
{
    void Register(MainPage mainPage);

    Task ProtectAsync(Exception? exception, bool isFatal);

    Task ProtectAsync(CrashDiagnostic diagnostic);
}
