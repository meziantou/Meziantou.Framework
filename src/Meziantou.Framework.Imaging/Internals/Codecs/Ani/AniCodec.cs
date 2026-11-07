using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The animated cursor (<c>ANI</c>) codec registration. The container is read by offset: its chunks may come in any
/// order and a step may show any stored frame, so, like the icon and cursor files it embeds, it is never streamed.
/// </summary>
/// <remarks>
/// <c>Image.Load</c> returns the animation as it is played: one frame per step, in step order, each with the duration of
/// its step and the hotspot of its cursor image. The animation always loops (the format has no play count).
/// <c>Image.Identify</c> validates the same structure without decoding a pixel; a header identification reports the same
/// information as a full scan, because the pixel format of the animation depends on every displayed frame.
/// </remarks>
internal sealed class AniCodec : ImageCodec
{
    private AniCodec()
    {
    }

    public static AniCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Ani;

    public override bool RequiresRandomAccess => true;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => AniFormat.MatchesSignature(prefix);

    public override ImageInfo IdentifyRandomAccess(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        using var structure = AniStructureReader.Read(source, context);
        if (mode == ImageIdentifyMode.FullScan)
        {
            for (var step = 0; step < structure.StepCount; step++)
            {
                context.Tracker.ChargeScannedFrame();
            }
        }

        return new ImageInfo(
            ImageFormat.Ani,
            structure.Canvas,
            structure.PixelFormat,
            structure.ColorModel,
            structure.BitsPerComponent,
            frameCount: structure.StepCount,
            isAnimated: true,
            hasPosterFrame: false,
            mayHaveTransparency: structure.MayHaveTransparency,
            animation: new AnimationMetadata(),
            structure.Metadata,
            mode);
    }

    public override Image DecodeRandomAccess(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        using var structure = AniStructureReader.Read(source, context);
        return AniDecoder.Decode(structure, request, context);
    }
}
