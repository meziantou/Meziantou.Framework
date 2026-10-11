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
            page0.SourcePixelFormat,
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
        using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Tiff, page.Size, page.SourcePixelFormat, page.SourcePixelFormat, page.Metadata.IccProfile);
        TiffPageDecoder.Decode(source, page, sink);
        return sink.Build(page.Metadata, animation: null);
    }
}
