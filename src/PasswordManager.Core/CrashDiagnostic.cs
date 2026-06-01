namespace PasswordManager.Core;

public sealed record CrashDiagnostic(
    string Category,
    string ExceptionType,
    bool IsFatal);
