using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the steps of an animated cursor into the frames of one animation, in playback order.
/// </summary>
/// <remarks>
/// <para>
/// A stored frame is decoded once, by the icon decoder, the first time a step shows it; a later step showing the same
/// frame copies the frame already decoded (<see cref="DecodedFrameSink.CopyFromFrame"/>), so a sequence that repeats a
/// frame costs neither a second read nor a second decode. Every step is still a full, independent frame of the image and
/// is charged to <see cref="ImageResourceLimits.MaxFrames"/> and <see cref="ImageResourceLimits.MaxTotalPixels"/> exactly
/// once: the embedded decode does not charge its own frame.
/// </para>
/// <para>
/// Embedded payloads are decoded to the pixel format of the animation, which is at least as wide as each of them, so that
/// step never narrows a sample or drops alpha; the caller's request (a typed load, a background color) is applied once,
/// by the sink of the animation. A color profile inside a PNG payload has no place in an animation whose frames may each
/// have their own, so it is not retained.
/// </para>
/// </remarks>
internal static class AniDecoder
{
    private static readonly PixelConversionOptions EmbeddedConversion = new() { DiscardIncompatibleColorProfile = true };

    /// <summary>Decodes the animation.</summary>
    /// <param name="structure">The validated structure.</param>
    /// <param name="request">The requested representation and selection.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>A new image owned by the caller.</returns>
    public static Image Decode(AniStructure structure, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var pixelFormat = structure.PixelFormat;
        var embeddedRequest = new ImageDecodeRequest(pixelFormat, FrameLimit: null, EmbeddedConversion) { IsEmbeddedPayload = true };
        using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Ani, structure.Canvas, pixelFormat, pixelFormat, iccProfile: null);
        for (var step = 0; step < structure.StepCount; step++)
        {
            var frame = structure.GetFrame(step);
            sink.BeginFrame(AnimationTiming.FromAniRate(structure.GetRate(step)), frame.Selected!.Hotspot);
            if (frame.FirstOutputIndex >= 0)
            {
                sink.CopyFromFrame(frame.FirstOutputIndex);
            }
            else
            {
                DecodeFrame(frame, sink, embeddedRequest, context);
                frame.FirstOutputIndex = step;
            }

            if (!sink.EndImage())
                break;
        }

        return sink.Build(structure.Metadata, new AnimationMetadata());
    }

    private static void DecodeFrame(AniFrame frame, DecodedFrameSink sink, ImageDecodeRequest request, ImageCodecContext context)
    {
        Image decoded;
        try
        {
            decoded = IcoEntryDecoder.Decode(frame.Source!, frame.ContainerFormat, frame.Selected!, request, context);
        }
        catch (ImageException exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
        {
            throw AniStructureReader.Rewrap(frame.Index, exception);
        }

        using (decoded)
        {
            var rowLength = sink.Canvas.Width * PixelFormats.GetBytesPerPixel(sink.SourcePixelFormat);
            using var source = Image.GetFrameForDecoder(decoded, 0).GetStorage().AcquireLease();
            using var destination = sink.LeaseCurrentFrame();
            for (var y = 0; y < sink.Canvas.Height; y++)
            {
                sink.WriteRow(destination, y, source.GetRowBytes(y)[..rowLength]);
            }
        }
    }
}
