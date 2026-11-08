namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// The base class of the encoders of the formats the library does not write itself. Pass an encoder to
/// <see cref="Image.Save(Stream, ImageEncoder)"/> and its overloads, or let <see cref="Image.Save(string, ImageEncoder?)"/> create
/// it from the file extension (<see cref="CustomImageCodec.CreateEncoder"/>).
/// </summary>
/// <remarks>
/// <para>
/// The library validates the arguments, opens the destination and, for a path, publishes the file only once
/// <see cref="Encode"/> returns (an existing file is left unchanged on failure); the encoder writes the whole image.
/// <see cref="ImageFrame.Save(Stream, ImageEncoder)"/> gives it a still copy of the frame. Sequential writers
/// (<see cref="ImageWriter{TPixel}"/>) and <see cref="ImageCollection"/> do not use custom encoders.
/// </para>
/// <para>
/// <see cref="ImageEncoder.MetadataHandling"/> and <see cref="ImageEncoder.FloatingPointConversion"/> are settings the
/// encoder reads itself: the library does not apply them to a custom format. Like the built-in encoders, an encoder must be
/// immutable and thread-safe, and must reject what it cannot write without a loss instead of dropping it
/// (<see cref="UnsupportedImageFeatureException"/>).
/// </para>
/// </remarks>
public abstract class CustomImageEncoder : ImageEncoder
{
    /// <summary>Initializes an encoder of a format.</summary>
    /// <param name="formatInfo">The description of the format, created with the public constructor of <see cref="ImageFormatInfo"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="formatInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="formatInfo"/> describes a built-in format.</exception>
    protected CustomImageEncoder(ImageFormatInfo formatInfo)
    {
        ArgumentNullException.ThrowIfNull(formatInfo);
        if (formatInfo.Format != ImageFormat.Custom)
            throw new ArgumentException("A custom encoder describes its own format: create its description with the public constructor of ImageFormatInfo.", nameof(formatInfo));

        FormatInfo = formatInfo;
    }

    /// <summary>Gets <see cref="ImageFormat.Custom"/>.</summary>
    public sealed override ImageFormat Format => ImageFormat.Custom;

    /// <summary>Gets the description of the format the encoder writes.</summary>
    public ImageFormatInfo FormatInfo { get; }

    /// <summary>Writes an image.</summary>
    /// <param name="image">The image: its frames, poster frame, animation settings and metadata. It must not be modified.</param>
    /// <param name="destination">The writable destination, at the position where the file starts. Do not close it.</param>
    public abstract void Encode(Image image, Stream destination);

    /// <summary>Asynchronously writes an image.</summary>
    /// <param name="image">The image: its frames, poster frame, animation settings and metadata. It must not be modified.</param>
    /// <param name="destination">The writable destination, at the position where the file starts. Do not close it.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the image is written.</returns>
    /// <remarks>
    /// The default implementation calls <see cref="Encode"/>, which writes to <paramref name="destination"/> synchronously.
    /// Override it when the destination may not support synchronous writes (an HTTP response body).
    /// </remarks>
    public virtual ValueTask EncodeAsync(Image image, Stream destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Encode(image, destination);
        return ValueTask.CompletedTask;
    }
}
