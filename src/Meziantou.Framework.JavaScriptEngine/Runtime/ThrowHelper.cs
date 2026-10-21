using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Throws the native errors, created in the current realm of the engine of the thread.</summary>
internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowTypeError(string message) => throw CreateException(ErrorType.TypeError, message);

    [DoesNotReturn]
    public static T ThrowTypeError<T>(string message) => throw CreateException(ErrorType.TypeError, message);

    [DoesNotReturn]
    public static void ThrowRangeError(string message) => throw CreateException(ErrorType.RangeError, message);

    [DoesNotReturn]
    public static T ThrowRangeError<T>(string message) => throw CreateException(ErrorType.RangeError, message);

    [DoesNotReturn]
    public static void ThrowReferenceError(string message) => throw CreateException(ErrorType.ReferenceError, message);

    [DoesNotReturn]
    public static T ThrowReferenceError<T>(string message) => throw CreateException(ErrorType.ReferenceError, message);

    [DoesNotReturn]
    public static T ThrowSyntaxError<T>(string message) => throw CreateException(ErrorType.SyntaxError, message);

    [DoesNotReturn]
    public static void ThrowSyntaxError(string message) => throw CreateException(ErrorType.SyntaxError, message);

    [DoesNotReturn]
    public static void ThrowError(ErrorType type, string message) => throw CreateException(type, message);

    public static JsException CreateException(ErrorType type, string message)
    {
        var realm = JsEngine.Current?.CurrentRealm;
        if (realm is null)
            throw new InvalidOperationException($"{type}: {message} (no JavaScript engine is running on this thread)");

        return new JsException(new JsValue(realm.CreateError(type, message)));
    }

    /// <summary>Creates the exception of the SyntaxError of a source text that cannot be parsed, located at the error.</summary>
    public static JsException CreateSyntaxErrorException(JsSyntaxErrorException exception)
    {
        var realm = JsEngine.Current?.CurrentRealm;
        if (realm is null)
            throw new InvalidOperationException($"SyntaxError: {exception.Message} (no JavaScript engine is running on this thread)");

        var location = exception.Line > 0 ? new JsSourceLocation(exception.Url, exception.Line, exception.Column) : (JsSourceLocation?)null;
        return new JsException(new JsValue(realm.CreateError(ErrorType.SyntaxError, exception.Message)), location);
    }
}
