namespace Meziantou.Framework.Email;

/// <summary>The base class of the exceptions the library throws for the content of a file it reads.</summary>
public class EmailException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="EmailException"/> class.</summary>
    public EmailException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmailException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public EmailException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmailException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public EmailException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
