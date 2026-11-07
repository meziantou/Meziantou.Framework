namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The GIF (87a/89a) codec registration.</summary>
internal class GifCodec : ImageCodec
{
    protected GifCodec()
    {
    }

    public static GifCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Gif;

    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith("GIF87a"u8) || prefix.StartsWith("GIF89a"u8);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new GifStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = CreateDecodeObserver(request, context);
        try
        {
            return new StructureDecodeParser(new GifStructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }

    /// <summary>Creates the pixel decoder plugged into the block walk: the GIF decoder.</summary>
    protected virtual GifDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new GifDecoder(request, context);
}
