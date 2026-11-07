namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Represents a JavaScript <c>EvalError</c> thrown by an algorithm of a specification (e.g. the compilation of a string
/// blocked by a content security policy, https://w3c.github.io/webappsec-csp/#can-compile-strings).
/// </summary>
#pragma warning disable CA1064 // The exception never leaves the library: the bindings convert it to a JavaScript EvalError
internal sealed class EvalErrorException : Exception
#pragma warning restore CA1064
{
    /// <summary>Creates an eval error exception.</summary>
    public EvalErrorException()
    {
    }

    /// <summary>Creates an eval error exception with the given message.</summary>
    public EvalErrorException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates an eval error exception with the given message and inner exception.</summary>
    public EvalErrorException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
