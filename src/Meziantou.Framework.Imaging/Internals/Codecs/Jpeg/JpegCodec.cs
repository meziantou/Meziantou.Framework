namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The JPEG (JFIF/EXIF) codec registration. The signature is SOI followed by a marker (<c>FF D8 FF</c>).</summary>
internal class JpegCodec : ImageCodec
{
    protected JpegCodec()
    {
    }

    public static JpegCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Jpeg;

    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new JpegStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = CreateDecodeObserver(request, context);
        try
        {
            return new StructureDecodeParser(new JpegStructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }

    /// <summary>Creates the pixel decoder plugged into the marker walk: the sequential and progressive decoder.</summary>
    protected virtual JpegDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new JpegDecoder(request, context);
}
