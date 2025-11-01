namespace Meziantou.Framework.HumanReadable;

/// <summary>
/// Represents errors that occur during human-readable serialization.
/// </summary>
public class HumanReadableSerializerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HumanReadableSerializerException"/> class.
    /// </summary>
    public HumanReadableSerializerException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HumanReadableSerializerException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public HumanReadableSerializerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HumanReadableSerializerException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or <see langword="null"/> if no inner exception is specified.</param>
    public HumanReadableSerializerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
