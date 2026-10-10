using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>A snapshot of the information that can be determined about an encoded image without decoding its pixels.</summary>
/// <remarks>
/// <para>
/// Instances are produced by <see cref="Image.Identify(Stream, ImageIdentifyOptions?)"/> and by
/// <see cref="ImageReader{TPixel}.Info"/>. Values that cannot be determined from the examined part of the file are
/// <see langword="null"/>; they are never guessed. In <see cref="ImageIdentifyMode.Header"/> mode, the frame count of a
/// GIF file, for instance, is unknown.
/// </para>
/// <para>
/// <see cref="Metadata"/> and <see cref="Animation"/> are detached copies owned by this instance: modifying them does not
/// affect any image, reader, or other <see cref="ImageInfo"/>.
/// </para>
/// </remarks>
public sealed class ImageInfo
{
    internal ImageInfo(
        ImageFormat format,
        Size size,
        PixelFormat pixelFormat,
        ImageColorModel colorModel,
        int bitsPerComponent,
        int? frameCount,
        bool? isAnimated,
        bool? hasPosterFrame,
        bool? mayHaveTransparency,
        AnimationMetadata? animation,
        ImageMetadata metadata,
        ImageIdentifyMode identifyMode,
        int? collectionEntryCount = null)
    {
        // Internal factory used by codecs: inconsistent snapshots are programming errors, reported eagerly so that unknown
        // values can never be silently replaced by guesses
        if (format == ImageFormat.Unknown || !Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "The format must be a known image format.");

        if (size.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The canvas size must be positive.");

        if (!Enum.IsDefined(pixelFormat))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "The pixel format is not valid.");

        if (!Enum.IsDefined(colorModel))
            throw new ArgumentOutOfRangeException(nameof(colorModel), colorModel, "The color model is not valid.");

        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
            throw new ArgumentOutOfRangeException(nameof(bitsPerComponent), bitsPerComponent, "The bits per component must be 1, 2, 4, 8 or 16.");

        if (frameCount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "The frame count must be positive or unknown (null).");

        if (collectionEntryCount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(collectionEntryCount), collectionEntryCount, "The collection entry count must be positive or unknown (null).");

        if (!Enum.IsDefined(identifyMode))
            throw new ArgumentOutOfRangeException(nameof(identifyMode), identifyMode, "The identify mode is not valid.");

        ArgumentNullException.ThrowIfNull(metadata);

        // A known animation signal contradicts "not animated"
        if (isAnimated == false && (frameCount > 1 || hasPosterFrame == true || animation is not null))
            throw new ArgumentException("An image with several frames, a poster frame or animation settings is animated.", nameof(isAnimated));

        Format = format;
        Size = size;
        PixelFormat = pixelFormat;
        ColorModel = colorModel;
        BitsPerComponent = bitsPerComponent;
        FrameCount = frameCount;
        IsAnimated = isAnimated;
        HasPosterFrame = hasPosterFrame;
        MayHaveTransparency = mayHaveTransparency;
        Animation = animation?.Clone();
        Metadata = metadata.Clone();
        IdentifyMode = identifyMode;
        CollectionEntryCount = collectionEntryCount;
    }

    /// <summary>Gets the detected encoded format.</summary>
    public ImageFormat Format { get; }

    /// <summary>
    /// Gets the number of entries of a multi-entry container (the pages of a TIFF document, the representations of an
    /// icon), or <see langword="null"/> for a single-image format or when the examined part of the file does not say.
    /// </summary>
    /// <remarks>
    /// Entries are never animation frames: <see cref="FrameCount"/> describes the image this snapshot is about (the first
    /// page, or the default representation) and stays 1. Use <see cref="ImageCollection"/> to reach the other entries.
    /// </remarks>
    public int? CollectionEntryCount { get; }

    /// <summary>Gets the canvas width, in pixels.</summary>
    public int Width => Size.Width;

    /// <summary>Gets the canvas height, in pixels.</summary>
    public int Height => Size.Height;

    /// <summary>Gets the canvas size, in pixels.</summary>
    public Size Size { get; }

    /// <summary>Gets the pixel format that the untyped <c>Image.Load</c> overloads produce for this file (the default working representation).</summary>
    public PixelFormat PixelFormat { get; }

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel { get; }

    /// <summary>Gets the precision of the encoded samples, in bits per component (for example 1, 2, 4, 8 or 16 for PNG).</summary>
    public int BitsPerComponent { get; }

    /// <summary>Gets the number of displayed animation frames (excluding a separate poster frame), or <see langword="null"/> when unknown.</summary>
    public int? FrameCount { get; }

    /// <summary>Gets a value indicating whether the file is an animation, or <see langword="null"/> when unknown.</summary>
    public bool? IsAnimated { get; }

    /// <summary>Gets a value indicating whether the file contains a separate poster frame (APNG default image that is not part of the animation), or <see langword="null"/> when unknown.</summary>
    public bool? HasPosterFrame { get; }

    /// <summary>
    /// Gets a value indicating whether the encoding is capable of producing non-opaque pixels (alpha channel, transparency
    /// chunk, or transparent palette index), or <see langword="null"/> when unknown. This is not the result of a pixel scan.
    /// </summary>
    public bool? MayHaveTransparency { get; }

    /// <summary>Gets the animation-wide information (such as the number of plays), or <see langword="null"/> if the file is not known to be animated.</summary>
    public AnimationMetadata? Animation { get; }

    /// <summary>Gets the metadata found in the examined part of the file.</summary>
    public ImageMetadata Metadata { get; }

    /// <summary>Gets the mode used to produce this snapshot.</summary>
    public ImageIdentifyMode IdentifyMode { get; }
}
