namespace Meziantou.Framework.Pdf;

/// <summary>Thrown when a document uses a valid feature the library does not support.</summary>
public sealed class UnsupportedPdfFeatureException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="UnsupportedPdfFeatureException"/> class.</summary>
    public UnsupportedPdfFeatureException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedPdfFeatureException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public UnsupportedPdfFeatureException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedPdfFeatureException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public UnsupportedPdfFeatureException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
