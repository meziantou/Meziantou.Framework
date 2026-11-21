namespace Meziantou.Framework.Email;

/// <summary>Thrown when an Outlook data file is malformed, inconsistent or truncated: a bad signature or checksum, a reference outside the file, a structure that does not match the specification.</summary>
public sealed class InvalidPstException : PstException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidPstException"/> class.</summary>
    public InvalidPstException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidPstException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidPstException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidPstException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public InvalidPstException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
