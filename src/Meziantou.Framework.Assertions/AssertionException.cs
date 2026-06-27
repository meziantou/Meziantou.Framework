namespace Meziantou.Framework.Assertions;

/// <summary>Represents an assertion failure.</summary>
public sealed class AssertionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssertionException"/> class.
    /// </summary>
    public AssertionException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssertionException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AssertionException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssertionException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public AssertionException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
