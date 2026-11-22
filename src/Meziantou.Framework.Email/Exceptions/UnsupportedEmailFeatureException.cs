namespace Meziantou.Framework.Email;

/// <summary>Thrown when a file uses a feature the library does not support, such as an unknown format version or encryption method of an Outlook data file.</summary>
public sealed class UnsupportedEmailFeatureException : EmailException
{
    /// <summary>Initializes a new instance of the <see cref="UnsupportedEmailFeatureException"/> class.</summary>
    public UnsupportedEmailFeatureException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedEmailFeatureException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public UnsupportedEmailFeatureException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedEmailFeatureException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public UnsupportedEmailFeatureException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
