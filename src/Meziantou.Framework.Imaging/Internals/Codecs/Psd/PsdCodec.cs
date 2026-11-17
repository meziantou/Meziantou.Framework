namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Adobe Photoshop codec registration: PSD and PSB files, starting with the signature <c>8BPS</c>. Only read.</summary>
internal sealed class PsdCodec : ImageCodec
{
    private PsdCodec()
    {
    }

    public static PsdCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Psd;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => PsdFormat.MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new PsdStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new PsdDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new PsdStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
