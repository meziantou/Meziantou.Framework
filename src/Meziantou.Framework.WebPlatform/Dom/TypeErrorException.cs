namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Represents a JavaScript <c>TypeError</c> thrown by an algorithm of a specification.</summary>
public sealed class TypeErrorException : ArgumentException
{
    /// <summary>Creates a type error exception.</summary>
    public TypeErrorException()
    {
    }

    /// <summary>Creates a type error exception with the given message.</summary>
    public TypeErrorException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates a type error exception with the given message and inner exception.</summary>
    public TypeErrorException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
