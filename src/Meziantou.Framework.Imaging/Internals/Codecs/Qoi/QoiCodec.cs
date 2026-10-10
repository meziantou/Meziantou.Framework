namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The QOI codec registration: a 14-byte header starting with the magic <c>qoif</c>.</summary>
internal sealed class QoiCodec : ImageCodec
{
    private QoiCodec()
    {
    }

    public static QoiCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Qoi;

    /// <summary>Determines whether the data starts with the 4-byte magic <c>qoif</c>.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(QoiFormat.Magic);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new QoiStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new QoiDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new QoiStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
