namespace PasswordManager.Core;

public static class CrashDiagnosticSanitizer
{
    private const string DefaultCategory = "UnhandledException";
    private const string UnknownExceptionType = "UnknownException";

    public static CrashDiagnostic Create(Exception? exception, bool isFatal)
    {
        return new CrashDiagnostic(
            DefaultCategory,
            SanitizeTypeName(exception?.GetType().Name),
            isFatal);
    }

    private static string SanitizeTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return UnknownExceptionType;
        }

        var sanitized = new string(typeName
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? UnknownExceptionType
            : sanitized;
    }
}
