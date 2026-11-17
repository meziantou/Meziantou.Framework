namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Kodak Cineon codec registration: files starting with the magic number <c>80 2A 5F D7</c>, in either byte order. Decoding only.</summary>
internal sealed class CineonCodec : ImageCodec
{
    private CineonCodec()
    {
    }

    public static CineonCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Cineon;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => CineonFormat.MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new CineonStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new RawRowDecoder(request, context, ImageFormat.Cineon);
        try
        {
            return new StructureDecodeParser(new CineonStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
