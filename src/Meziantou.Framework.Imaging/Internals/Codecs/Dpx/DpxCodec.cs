namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The DPX codec registration: files starting with the magic number <c>SDPX</c> (big-endian) or <c>XPDS</c> (little-endian).</summary>
internal sealed class DpxCodec : ImageCodec
{
    private DpxCodec()
    {
    }

    public static DpxCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Dpx;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => DpxFormat.MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new DpxStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new RawRowDecoder(request, context, ImageFormat.Dpx);
        try
        {
            return new StructureDecodeParser(new DpxStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
