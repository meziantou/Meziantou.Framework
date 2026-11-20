namespace Meziantou.Framework.Mail;

/// <summary>Thrown when a file is malformed or truncated beyond what the reader can recover.</summary>
public sealed class InvalidMailDataException : MailException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidMailDataException"/> class.</summary>
    public InvalidMailDataException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidMailDataException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidMailDataException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidMailDataException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public InvalidMailDataException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
