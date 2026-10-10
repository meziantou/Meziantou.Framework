namespace Meziantou.Framework.Imaging;

/// <summary>The base class of the exceptions describing image content, feature and resource-limit failures.</summary>
/// <remarks>
/// <para>
/// Catch a derived type to handle a specific failure category:
/// <see cref="UnknownImageFormatException"/> (unrecognized signature),
/// <see cref="InvalidImageContentException"/> (malformed or truncated recognized data),
/// <see cref="UnsupportedImageFeatureException"/> (valid but unsupported feature, or a loss requiring an explicit policy), and
/// <see cref="ImageResourceLimitException"/> (configured limit exceeded).
/// </para>
/// <para>
/// Other failures use the standard .NET exceptions: <see cref="ArgumentException"/> for invalid arguments,
/// <see cref="InvalidOperationException"/> for invalid states, <see cref="ObjectDisposedException"/> after disposal,
/// <see cref="OperationCanceledException"/> for cancellation, and <see cref="IOException"/> for I/O failures, which are
/// propagated unchanged.
/// </para>
/// </remarks>
public class ImageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ImageException"/> class.</summary>
    public ImageException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public ImageException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ImageException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
