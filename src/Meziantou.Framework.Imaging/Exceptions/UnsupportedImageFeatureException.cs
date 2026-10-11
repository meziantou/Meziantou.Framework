namespace Meziantou.Framework.Imaging;

/// <summary>
/// The exception thrown when valid content uses a feature this version does not support (for example an arithmetic-coded
/// JPEG or a GIF plain-text extension), or when an operation would lose information (animation, alpha, precision,
/// metadata, color profile) and no explicit policy allows the loss.
/// </summary>
public class UnsupportedImageFeatureException : ImageException
{
    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageFeatureException"/> class.</summary>
    public UnsupportedImageFeatureException()
        : base("The image uses a feature that is not supported.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageFeatureException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public UnsupportedImageFeatureException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageFeatureException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public UnsupportedImageFeatureException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageFeatureException"/> class for a specific format and feature.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="format">The format involved, or <see cref="ImageFormat.Unknown"/> for format-independent conversions.</param>
    /// <param name="feature">A short, stable, human-readable feature name (for example <c>"JPEG arithmetic coding"</c>).</param>
    public UnsupportedImageFeatureException(string? message, ImageFormat format, string? feature)
        : base(message)
    {
        Format = format;
        Feature = feature;
    }

    /// <summary>Gets the format involved, or <see cref="ImageFormat.Unknown"/> if not specified.</summary>
    public ImageFormat Format { get; }

    /// <summary>Gets a short human-readable name of the unsupported feature, if specified.</summary>
    public string? Feature { get; }
}
