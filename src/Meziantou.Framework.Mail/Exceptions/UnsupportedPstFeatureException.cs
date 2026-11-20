namespace Meziantou.Framework.Mail;

/// <summary>Thrown when an Outlook data file uses a feature the library does not support, such as an unknown format version or encryption method.</summary>
public sealed class UnsupportedPstFeatureException : PstException
{
    /// <summary>Initializes a new instance of the <see cref="UnsupportedPstFeatureException"/> class.</summary>
    public UnsupportedPstFeatureException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedPstFeatureException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public UnsupportedPstFeatureException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedPstFeatureException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public UnsupportedPstFeatureException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
