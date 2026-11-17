namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The KTX 2.0 codec registration: files starting with the identifier «KTX 20».</summary>
/// <remarks>
/// A KTX 2.0 file is read at random: its level index locates the levels, the base level last. <c>Image.Identify</c> and
/// <c>Image.Load</c> describe and decode the base level of the first face of the first layer (or the animation of an array
/// with <c>KTXanimData</c>), and every subresource is reached through <see cref="ImageCollection"/>.
/// </remarks>
internal sealed class Ktx2Codec : ImageCodec
{
    private Ktx2Codec()
    {
    }

    public static Ktx2Codec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Ktx2;

    public override bool RequiresRandomAccess => true;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => Ktx2Format.MatchesSignature(prefix);

    public override ImageInfo IdentifyRandomAccess(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context) => Ktx2Decoder.Identify(source, mode, context);

    public override Image DecodeRandomAccess(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context) => Ktx2Decoder.Decode(source, request, context);
}
