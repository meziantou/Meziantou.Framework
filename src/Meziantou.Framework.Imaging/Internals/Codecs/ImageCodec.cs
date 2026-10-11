namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An internal codec registration: content signature, identifier and eager decoder of one container format
///. This is not a public plug-in interface.
/// </summary>
/// <remarks>
/// <para>
/// Codecs are selected by <see cref="ImageCodecRegistry.Detect"/> from the first
/// <see cref="Image.FormatDetectionPrefixLength"/> bytes of the input, never from file names. Follow-up formats
/// add a codec here and an <see cref="ImageFormat"/> member; the public entry points stay the same.
/// </para>
/// <para>
/// Parsers are synchronous push-model state machines (<see cref="ImageParser{TResult}"/>); the driver performs the
/// synchronous or asynchronous I/O, so a codec never implements I/O or async code.
/// </para>
/// </remarks>
internal abstract class ImageCodec
{
    /// <summary>Gets the format produced by this codec.</summary>
    public abstract ImageFormat Format { get; }

    /// <summary>Determines whether the data starts with the signature of this format.</summary>
    /// <param name="prefix">At least <see cref="Image.FormatDetectionPrefixLength"/> bytes.</param>
    /// <returns><see langword="true"/> if the signature matches.</returns>
    public abstract bool IsMatch(ReadOnlySpan<byte> prefix);

    /// <summary>
    /// Gets a value indicating whether the format is addressed by file offsets instead of being a byte stream (TIFF,
    /// BigTIFF, ICO, CUR and ANI). Such a codec implements <see cref="IdentifyRandomAccess"/> and
    /// <see cref="DecodeRandomAccess"/>; its <see cref="CreateIdentifyParser"/> and <see cref="CreateDecodeParser"/>
    /// buffer the whole input first (<see cref="WholeInputParser{TResult}"/>), which the driver only uses when it cannot
    /// seek. Sequential readers (<see cref="ImageReader{TPixel}"/>) reject these formats explicitly.
    /// </summary>
    public virtual bool RequiresRandomAccess => false;

    /// <summary>Describes the input without decoding pixels, reading it by offset.</summary>
    /// <param name="source">The random-access view of the input.</param>
    /// <param name="mode">The identify mode.</param>
    /// <param name="context">The per-input context.</param>
    /// <returns>The information.</returns>
    public virtual ImageInfo IdentifyRandomAccess(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context)
        => throw new NotSupportedException($"The {GetType().Name} codec does not read random-access input.");

    /// <summary>Decodes the input, reading it by offset.</summary>
    /// <param name="source">The random-access view of the input.</param>
    /// <param name="request">The requested representation and selection.</param>
    /// <param name="context">The per-input context.</param>
    /// <returns>The image.</returns>
    public virtual Image DecodeRandomAccess(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
        => throw new NotSupportedException($"The {GetType().Name} codec does not read random-access input.");

    /// <summary>Creates a parser producing an <see cref="ImageInfo"/> without allocating pixels.</summary>
    /// <param name="mode">The identify mode.</param>
    /// <param name="context">The per-input context.</param>
    /// <returns>A new parser positioned at the start of the data (signature included).</returns>
    public virtual ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RequiresRandomAccess
            ? new WholeInputParser<ImageInfo>(context, Format, source => IdentifyRandomAccess(source, mode, context))
            : throw new NotSupportedException($"The {GetType().Name} codec does not create an identify parser.");
    }

    /// <summary>Creates a parser decoding the whole image (up to <see cref="ImageDecodeRequest.FrameLimit"/>).</summary>
    /// <param name="request">The requested representation and selection.</param>
    /// <param name="context">The per-input context; the image is created in <see cref="ImageCodecContext.Scope"/>.</param>
    /// <returns>A new parser positioned at the start of the data (signature included).</returns>
    public virtual ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RequiresRandomAccess
            ? new WholeInputParser<Image>(context, Format, source => DecodeRandomAccess(source, request, context))
            : throw new NotSupportedException($"The {GetType().Name} codec does not create a decode parser.");
    }
}
