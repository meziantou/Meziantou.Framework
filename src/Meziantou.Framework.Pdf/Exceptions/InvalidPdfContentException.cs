namespace Meziantou.Framework.Pdf;

/// <summary>Thrown when a document is malformed, inconsistent or truncated beyond what the reader can recover.</summary>
public sealed class InvalidPdfContentException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidPdfContentException"/> class.</summary>
    public InvalidPdfContentException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidPdfContentException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidPdfContentException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidPdfContentException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public InvalidPdfContentException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
