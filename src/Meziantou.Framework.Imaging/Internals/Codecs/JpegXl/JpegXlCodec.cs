namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG XL codec registration: a bare codestream (ISO/IEC 18181-1), or the container that holds one with metadata
/// (ISO/IEC 18181-2).
/// </summary>
internal sealed class JpegXlCodec : ImageCodec
{
    private JpegXlCodec()
    {
    }

    public static JpegXlCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.JpegXl;

    /// <summary>
    /// Gets a value indicating that a still image of one VarDCT frame is decoded at 1/8 of its size from its LF image
    /// (<see cref="JpegXlDecoder"/>); another image is decoded at full size for a target size and rejected for an exact scale.
    /// </summary>
    public override bool SupportsScaledDecoding => true;

    /// <summary>Determines whether the data starts with the two bytes of a codestream (<c>FF 0A</c>) or with the signature box of a container (12 bytes).</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
        => prefix.StartsWith(JpegXlFormat.CodestreamSignature) || prefix.StartsWith(JpegXlFormat.ContainerSignature);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new JpegXlStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = new JpegXlDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new JpegXlStructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }
}
