namespace Meziantou.Framework.Imaging;

/// <summary>The exception thrown when the data of a recognized format is malformed, inconsistent, or truncated.</summary>
/// <remarks>Malformed data is never reported as a clean end of input or as a successfully truncated image.</remarks>
public class InvalidImageContentException : ImageException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidImageContentException"/> class.</summary>
    public InvalidImageContentException()
        : base("The image content is invalid.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidImageContentException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public InvalidImageContentException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidImageContentException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidImageContentException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidImageContentException"/> class for a specific format.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="format">The format whose data is invalid.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidImageContentException(string? message, ImageFormat format, Exception? innerException = null)
        : base(message, innerException)
    {
        Format = format;
    }

    /// <summary>Gets the format whose data is invalid, or <see cref="ImageFormat.Unknown"/> if not specified.</summary>
    public ImageFormat Format { get; }
}
