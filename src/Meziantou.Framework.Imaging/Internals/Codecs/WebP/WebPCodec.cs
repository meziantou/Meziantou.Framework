namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The WebP codec registration: a RIFF container whose form type is <c>WEBP</c>.</summary>
internal class WebPCodec : ImageCodec
{
    protected WebPCodec()
    {
    }

    public static WebPCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.WebP;

    /// <summary>Determines whether the data starts with <c>RIFF</c>, a 4-byte size and <c>WEBP</c> (12 bytes).</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.Length >= 12 && prefix.StartsWith("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new WebPStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = CreateDecodeObserver(request, context);
        try
        {
            return new StructureDecodeParser(new WebPStructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }

    /// <summary>Creates the pixel decoder plugged into the chunk walk: the WebP decoder (still images and animations).</summary>
    protected virtual WebPDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new WebPDecoder(request, context);
}
