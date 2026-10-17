namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The PNG/APNG codec registration. APNG is a PNG variant (same signature), recognized by its <c>acTL</c> chunk.</summary>
internal class PngCodec : ImageCodec
{
    protected PngCodec()
    {
    }

    public static PngCodec Instance { get; } = new();

    /// <summary>Gets the 8-byte PNG signature.</summary>
    public static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public override ImageFormat Format => ImageFormat.Png;

    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(Signature);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new PngStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = CreateDecodeObserver(request, context);
        try
        {
            return new StructureDecodeParser(new PngStructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }

    /// <summary>Creates the pixel decoder plugged into the chunk walk: the PNG decoder, which routes APNG files to <see cref="ApngDecoder"/>.</summary>
    protected virtual PngDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new PngDecoder(request, context);
}
