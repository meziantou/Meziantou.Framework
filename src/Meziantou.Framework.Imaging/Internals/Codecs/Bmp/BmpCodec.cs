namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The BMP codec registration: the <c>BM</c> signature of a standalone Windows bitmap file.</summary>
internal sealed class BmpCodec : ImageCodec
{
    private BmpCodec()
    {
    }

    public static BmpCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Bmp;

    /// <summary>Determines whether the data starts with the 2-byte signature <c>BM</c>.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(BmpFormat.Magic);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new BmpStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new BmpDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new BmpStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
