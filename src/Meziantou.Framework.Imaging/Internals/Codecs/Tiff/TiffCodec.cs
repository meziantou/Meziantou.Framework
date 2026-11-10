namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The TIFF and BigTIFF codec registration. TIFF is a random-access container: <c>Image.Identify</c> and
/// <c>Image.Load</c> describe and decode its <em>first page</em>, and the whole document is reached through
/// <see cref="ImageCollection"/>, which never materializes a page it was not asked for.
/// </summary>
internal sealed class TiffCodec : ImageCodec
{
    private TiffCodec()
    {
    }

    public static TiffCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Tiff;

    public override bool RequiresRandomAccess => true;

    /// <summary>Gets a value indicating that the decoder only reads the strips or the tiles that an area meets (<see cref="ImageDecodeRequest.Region"/>).</summary>
    public override bool SupportsRegionDecoding => true;

    /// <summary>
    /// Gets a value indicating that the decoder reads a reduced-resolution image of the first page when the file stores one
    /// (<see cref="TiffResolutionLevels"/>); without one, a target size gives the full resolution and an exact scale is rejected.
    /// </summary>
    public override bool SupportsScaledDecoding => true;

    /// <summary>Determines whether the data starts with a classic TIFF or a BigTIFF header.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => TiffHeader.MatchesSignature(prefix);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageInfo IdentifyRandomAccess(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var header = TiffHeader.Read(source);
        TiffPage? first = null;
        var count = 0;
        foreach (var directory in TiffDirectoryChain.Enumerate(source, header, context.Limits))
        {
            var page = TiffPage.Resolve(directory, count, context);
            first ??= page;
            count++;
            if (mode != ImageIdentifyMode.FullScan)
                break;

            context.Tracker.ChargeScannedFrame();
        }

        var page0 = first ?? throw TiffFormat.Invalid("The TIFF document has no page.");
        return new ImageInfo(
            ImageFormat.Tiff,
            page0.Size,
            page0.DefaultPixelFormat,
            page0.ColorModel,
            page0.BitsPerSample,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: page0.HasAlpha,
            animation: null,
            page0.Metadata,
            mode,
            collectionEntryCount: mode == ImageIdentifyMode.FullScan ? count : null);
    }

    public override Image DecodeRandomAccess(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var header = TiffHeader.Read(source);
        var directory = TiffDirectory.Read(source, header, header.FirstDirectoryOffset);
        var page = TiffPage.Resolve(directory, index: 0, context);
        return request.IsScaled ? TiffResolutionLevels.Decode(source, page, request, context) : TiffPageDecoder.Decode(source, page, request, context);
    }
}
