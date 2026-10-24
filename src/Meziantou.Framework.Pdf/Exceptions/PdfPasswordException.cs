namespace Meziantou.Framework.Pdf;

/// <summary>Thrown when an encrypted document is opened without its user or owner password (<see cref="PdfReadOptions.Password"/>).</summary>
public sealed class PdfPasswordException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class.</summary>
    public PdfPasswordException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PdfPasswordException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public PdfPasswordException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
