namespace Meziantou.Framework.Email;

/// <summary>Thrown when a file is malformed or truncated beyond what the reader can recover.</summary>
public sealed class InvalidEmailDataException : EmailException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidEmailDataException"/> class.</summary>
    public InvalidEmailDataException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidEmailDataException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidEmailDataException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidEmailDataException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public InvalidEmailDataException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
