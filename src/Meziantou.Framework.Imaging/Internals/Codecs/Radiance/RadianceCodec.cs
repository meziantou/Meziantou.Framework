namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Radiance HDR codec registration: files starting with the line <c>#?RADIANCE</c> or <c>#?RGBE</c>.</summary>
internal sealed class RadianceCodec : ImageCodec
{
    private RadianceCodec()
    {
    }

    public static RadianceCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Radiance;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => RadianceFormat.MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new RadianceStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new RadianceDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new RadianceStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
