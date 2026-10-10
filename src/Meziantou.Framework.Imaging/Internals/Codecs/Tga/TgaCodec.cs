namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The TGA codec registration. TGA stores no signature, so the codec matches a strictly plausible 18-byte header
/// (<see cref="TgaHeader.IsPlausible"/>) and is the last codec the registry consults.
/// </summary>
internal sealed class TgaCodec : ImageCodec
{
    private TgaCodec()
    {
    }

    public static TgaCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Tga;

    /// <summary>Determines whether the first 18 bytes are a legal, self-consistent TGA header.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => TgaHeader.IsPlausible(prefix);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new TgaStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new TgaDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new TgaStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
