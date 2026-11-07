namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Represents a JavaScript <c>RangeError</c> thrown by an algorithm of a specification.</summary>
public sealed class RangeErrorException : ArgumentOutOfRangeException
{
    /// <summary>Creates a range error exception.</summary>
    public RangeErrorException()
    {
    }

    /// <summary>Creates a range error exception with the given message.</summary>
    public RangeErrorException(string? message)
        : base(paramName: null, message)
    {
    }

    /// <summary>Creates a range error exception with the given message and inner exception.</summary>
    public RangeErrorException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
