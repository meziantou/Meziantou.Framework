namespace Meziantou.Framework.Pdf;

/// <summary>Thrown when reading or writing a document exceeds a configured limit (<see cref="PdfReadOptions"/>).</summary>
public sealed class PdfResourceLimitException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfResourceLimitException"/> class.</summary>
    public PdfResourceLimitException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfResourceLimitException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PdfResourceLimitException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfResourceLimitException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public PdfResourceLimitException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
