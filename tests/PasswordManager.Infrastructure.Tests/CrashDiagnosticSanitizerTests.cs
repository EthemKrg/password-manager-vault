using PasswordManager.Core;

namespace PasswordManager.Infrastructure.Tests;

public sealed class CrashDiagnosticSanitizerTests
{
    private const string SecretMarker = "MASTER-SECRET generated-password clipboard-value entry-password";

    [Fact]
    public void Create_DoesNotExposeExceptionMessage()
    {
        var exception = new InvalidOperationException(SecretMarker);

        var diagnostic = CrashDiagnosticSanitizer.Create(exception, isFatal: true);

        Assert.Equal("UnhandledException", diagnostic.Category);
        Assert.Equal(nameof(InvalidOperationException), diagnostic.ExceptionType);
        Assert.True(diagnostic.IsFatal);
        AssertDoesNotExposeSecret(diagnostic);
    }

    [Fact]
    public void Create_DoesNotExposeInnerExceptionMessage()
    {
        var exception = new ApplicationException(
            "outer " + SecretMarker,
            new ArgumentException("inner " + SecretMarker));

        var diagnostic = CrashDiagnosticSanitizer.Create(exception, isFatal: false);

        Assert.Equal(nameof(ApplicationException), diagnostic.ExceptionType);
        Assert.False(diagnostic.IsFatal);
        AssertDoesNotExposeSecret(diagnostic);
    }

    [Fact]
    public void Create_DoesNotUseRawExceptionStringOrStackTrace()
    {
        var exception = CreateThrownException();

        var diagnostic = CrashDiagnosticSanitizer.Create(exception, isFatal: false);

        var rawException = exception.ToString();
        Assert.Contains(nameof(CreateThrownException), rawException, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(CreateThrownException), diagnostic.ToString(), StringComparison.Ordinal);
        AssertDoesNotExposeSecret(diagnostic);
    }

    [Fact]
    public void Create_NullException_ReturnsDeterministicFallback()
    {
        var diagnostic = CrashDiagnosticSanitizer.Create(null, isFatal: false);

        Assert.Equal("UnhandledException", diagnostic.Category);
        Assert.Equal("UnknownException", diagnostic.ExceptionType);
        Assert.False(diagnostic.IsFatal);
    }

    private static Exception CreateThrownException()
    {
        try
        {
            throw new InvalidOperationException(SecretMarker);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void AssertDoesNotExposeSecret(CrashDiagnostic diagnostic)
    {
        var text = diagnostic.ToString();
        Assert.DoesNotContain("MASTER-SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("generated-password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("clipboard-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-password", text, StringComparison.Ordinal);
    }
}
