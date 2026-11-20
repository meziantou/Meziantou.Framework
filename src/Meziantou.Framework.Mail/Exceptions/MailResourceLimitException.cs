namespace Meziantou.Framework.Mail;

/// <summary>Thrown when reading a file would exceed a limit of the options of the reader, such as the size of a message or the number of parts.</summary>
public sealed class MailResourceLimitException : MailException
{
    /// <summary>Initializes a new instance of the <see cref="MailResourceLimitException"/> class.</summary>
    public MailResourceLimitException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MailResourceLimitException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public MailResourceLimitException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MailResourceLimitException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public MailResourceLimitException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
