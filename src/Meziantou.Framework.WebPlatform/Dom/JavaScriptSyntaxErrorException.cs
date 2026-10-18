namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Represents a JavaScript <c>SyntaxError</c> thrown by an algorithm of a specification (e.g. an import map that is not valid JSON).</summary>
public sealed class JavaScriptSyntaxErrorException : FormatException
{
    /// <summary>Creates a syntax error exception.</summary>
    public JavaScriptSyntaxErrorException()
    {
    }

    /// <summary>Creates a syntax error exception with the given message.</summary>
    public JavaScriptSyntaxErrorException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates a syntax error exception with the given message and inner exception.</summary>
    public JavaScriptSyntaxErrorException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
