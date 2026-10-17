namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG 2000 codec registration: a codestream (Rec. ITU-T T.800 | ISO/IEC 15444-1, Annex A), or a file of the JP2
/// family that holds one with its color specification and metadata (Annex I), with the block coder of Rec. ITU-T T.800 or
/// the high-throughput block coder of Rec. ITU-T T.814 | ISO/IEC 15444-15.
/// </summary>
internal sealed class Jpeg2000Codec : ImageCodec
{
    private Jpeg2000Codec()
    {
    }

    public static Jpeg2000Codec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Jpeg2000;

    /// <summary>Determines whether the data starts with the SOC and SIZ markers of a codestream (<c>FF 4F FF 51</c>) or with the signature box of a file (12 bytes).</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
        => prefix.StartsWith(Jpeg2000Format.CodestreamSignature) || prefix.StartsWith(Jpeg2000Format.FileSignature);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new Jpeg2000StructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = new Jpeg2000Decoder(request, context);
        try
        {
            return new StructureDecodeParser(new Jpeg2000StructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }
}
