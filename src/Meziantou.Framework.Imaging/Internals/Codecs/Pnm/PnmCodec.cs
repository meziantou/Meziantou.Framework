namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Netpbm codec registration: the magic number <c>P1</c> to <c>P7</c> followed by white space.</summary>
internal sealed class PnmCodec : ImageCodec
{
    private PnmCodec()
    {
    }

    public static PnmCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Pnm;

    /// <summary>Determines whether the data starts with <c>P1</c> to <c>P7</c> followed by a white-space byte.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
        => prefix.Length >= 3 && prefix[0] == (byte)'P' && prefix[1] is >= (byte)'1' and <= (byte)'7' && PnmFormat.IsWhiteSpace(prefix[2]);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new PnmStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var decoder = new PnmDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new PnmStructureParser(context, StructureWalk.Decode, decoder), decoder);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }
}
