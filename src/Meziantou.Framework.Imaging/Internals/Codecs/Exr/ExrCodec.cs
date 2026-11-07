namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The OpenEXR codec registration: files starting with the magic number 20000630 (<c>76 2F 31 01</c>).</summary>
internal sealed class ExrCodec : ImageCodec
{
    private ExrCodec()
    {
    }

    public static ExrCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.OpenExr;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => prefix.StartsWith(ExrFormat.Magic);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new ExrStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new ExrDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new ExrStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
