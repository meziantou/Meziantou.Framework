namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The DDS (DirectDraw Surface) codec registration: files starting with the magic number <c>DDS </c>.</summary>
internal sealed class DdsCodec : ImageCodec
{
    private DdsCodec()
    {
    }

    public static DdsCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Dds;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => DdsFormat.MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new DdsStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context) => CreateDecodeParser(request, context, mip: 0);

    /// <summary>Creates the parser of the surface of a mipmap level that follows the headers.</summary>
    public static ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context, int mip)
    {
        var decoder = new DdsDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new DdsStructureParser(context, StructureWalk.Decode, decoder, mip), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
