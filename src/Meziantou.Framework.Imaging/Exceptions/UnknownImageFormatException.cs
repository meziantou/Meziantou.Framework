namespace Meziantou.Framework.Imaging;

/// <summary>The exception thrown when the input does not start with the signature of a supported image format.</summary>
public class UnknownImageFormatException : ImageException
{
    /// <summary>Initializes a new instance of the <see cref="UnknownImageFormatException"/> class.</summary>
    public UnknownImageFormatException()
        : base("The image format is not recognized.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnknownImageFormatException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public UnknownImageFormatException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnknownImageFormatException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public UnknownImageFormatException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
