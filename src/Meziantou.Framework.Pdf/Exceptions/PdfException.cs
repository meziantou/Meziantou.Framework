namespace Meziantou.Framework.Pdf;

/// <summary>The base class of the exceptions the library throws for the content of a document.</summary>
public class PdfException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class.</summary>
    public PdfException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PdfException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public PdfException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
