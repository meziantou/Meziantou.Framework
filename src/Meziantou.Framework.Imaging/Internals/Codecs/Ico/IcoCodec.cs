namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The ICO and CUR codec registration. An icon file is a random-access directory of alternative
/// representations: <c>Image.Identify</c> and <c>Image.Load</c> describe and decode the <em>default</em> representation
/// (the largest, then the deepest), and every representation is reached through <see cref="ImageCollection"/>.
/// </summary>
internal sealed class IcoCodec : ImageCodec
{
    private IcoCodec(int type)
    {
        Type = type;
        Format = IcoFormat.GetFormat(type);
    }

    /// <summary>Gets the codec of Windows icon files.</summary>
    public static IcoCodec Icon { get; } = new(IcoFormat.IconType);

    /// <summary>Gets the codec of Windows cursor files.</summary>
    public static IcoCodec Cursor { get; } = new(IcoFormat.CursorType);

    /// <summary>Gets the directory type this codec recognizes: 1 for an icon, 2 for a cursor.</summary>
    public int Type { get; }

    public override ImageFormat Format { get; }

    public override bool RequiresRandomAccess => true;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => IcoFormat.MatchesSignature(prefix, Type);

    public override ImageInfo IdentifyRandomAccess(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var (format, representations) = IcoDirectory.Read(source, context);
        var best = representations[0];
        foreach (var representation in representations)
        {
            if (IsBetter(representation, best))
            {
                best = representation;
            }
        }

        if (mode == ImageIdentifyMode.FullScan)
        {
            foreach (var _ in representations)
            {
                context.Tracker.ChargeScannedFrame();
            }
        }

        return new ImageInfo(
            format,
            best.Size,
            best.PixelFormat,
            best.ColorModel,
            best.BitsPerComponent,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: best.MayHaveTransparency,
            animation: null,
            best.Metadata,
            mode,
            collectionEntryCount: representations.Length);
    }

    public override Image DecodeRandomAccess(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var (format, representations) = IcoDirectory.Read(source, context);
        var best = representations[0];
        foreach (var representation in representations)
        {
            if (IsBetter(representation, best))
            {
                best = representation;
            }
        }

        return IcoEntryDecoder.Decode(source, format, best, request, context);
    }

    /// <summary>
    /// The default representation rule, used by <c>Image.Load</c> and <c>Image.Identify</c>: the largest representation,
    /// then the one with the most bits per stored pixel, then the first in directory order.
    /// </summary>
    private static bool IsBetter(IcoRepresentation candidate, IcoRepresentation best)
    {
        var candidateArea = (long)candidate.Size.Width * candidate.Size.Height;
        var bestArea = (long)best.Size.Width * best.Size.Height;
        if (candidateArea != bestArea)
            return candidateArea > bestArea;

        return candidate.BitsPerPixel > best.BitsPerPixel;
    }
}
