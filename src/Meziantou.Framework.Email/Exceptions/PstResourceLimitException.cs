namespace Meziantou.Framework.Email;

/// <summary>Thrown when reading a file would exceed a limit of <see cref="PstReadOptions"/>: the depth of a B-tree or of embedded messages, or the size of a property.</summary>
public sealed class PstResourceLimitException : PstException
{
    /// <summary>Initializes a new instance of the <see cref="PstResourceLimitException"/> class.</summary>
    public PstResourceLimitException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PstResourceLimitException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PstResourceLimitException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PstResourceLimitException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public PstResourceLimitException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
