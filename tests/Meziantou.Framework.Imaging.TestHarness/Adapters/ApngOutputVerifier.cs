using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// Verifies APNG encoder output against the image that was saved, with the independent harness reader
/// (<see cref="ReferencePng"/>): the container and sequence rules, the encoded control semantics of full-canvas output
/// (every frame a canvas-sized <c>SOURCE</c> region at offset 0 with <c>dispose_op</c> NONE, so no stale delta or disposal
/// data can survive an edit), the frame count, the play count, the poster layout, exact delays, and the pixels of every
/// displayed frame and of the separate poster, compared exactly with the intended full-canvas frames. The library decoder is
/// never used.
/// </summary>
public static class ApngOutputVerifier
{
    /// <summary>Verifies an encoded APNG.</summary>
    /// <param name="png">The encoded file.</param>
    /// <param name="expected">The saved image: its displayed frames, separate poster and animation settings are the intent.</param>
    /// <param name="exactDurations">
    /// <see langword="true"/> to require every <c>delay_num / delay_den</c> to equal the frame duration exactly; otherwise the
    /// caller checks the (explicitly rounded) delays.
    /// </param>
    /// <param name="context">A description used in failure messages.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="GoldenAssertionException">The output does not match.</exception>
    public static ReferencePng Verify(ReadOnlySpan<byte> png, Image expected, bool exactDurations = true, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        context ??= "APNG output";
        ReferencePng reference;
        try
        {
            reference = ReferencePng.Parse(png);
        }
        catch (InvalidDataException exception)
        {
            throw new GoldenAssertionException($"{context}: invalid PNG/APNG structure: {exception.Message}", exception);
        }

        Check(reference.Width == expected.Width && reference.Height == expected.Height, context, $"the canvas is {reference.Width}x{reference.Height}, {expected.Width}x{expected.Height} expected");
        var animation = reference.Animation ?? throw new GoldenAssertionException($"{context}: no acTL chunk (a static PNG was written).");
        Check(animation.NumFrames == expected.Frames.Count, context, $"acTL num_frames is {animation.NumFrames}, {expected.Frames.Count} displayed frames expected (a poster is not counted)");
        Check(animation.NumPlays == (expected.Animation?.TotalPlays ?? 0), context, $"acTL num_plays is {animation.NumPlays}, {expected.Animation?.TotalPlays ?? 0} expected");
        Check(animation.HasSeparatePoster == (expected.PosterFrame is not null), context, $"separate poster {animation.HasSeparatePoster}, expected {expected.PosterFrame is not null}");
        var types = reference.ChunkTypes;
        Check(types.Count > 1 && types[1] == "acTL", context, "acTL does not follow IHDR");

        uint sequence = 0;
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            var frame = animation.Frames[i];
            var name = $"{context}, frame {i}";
            Check(frame.SequenceNumber == sequence, name, $"fcTL sequence number {frame.SequenceNumber}, {sequence} expected");
            sequence = frame.SequenceNumber + 1 + (uint)frame.DataSequenceNumbers.Count;
            Check(frame.IsFullCanvasSource(reference.Width, reference.Height), name, $"region {frame.Width}x{frame.Height}+{frame.XOffset}+{frame.YOffset} blend {frame.BlendOp}: full-canvas SOURCE expected");
            Check(frame.DisposeOp == 0, name, $"dispose_op {frame.DisposeOp}, NONE expected");
            Check(frame.DelayDenominator != 0, name, "delay_den is 0 (the encoder always writes an explicit denominator)");
            Check(frame.UsesIdat == (i == 0 && expected.PosterFrame is null), name, frame.UsesIdat ? "uses IDAT" : "uses fdAT");
            if (exactDurations)
            {
                var duration = expected.Frames[i].Metadata.Duration;
                Check((Int128)frame.DelayNumerator * duration.Denominator == (Int128)duration.Numerator * frame.DelayDenominator, name, $"delay {frame.DelayNumerator}/{frame.DelayDenominator}, {duration} expected exactly");
            }

            Compare(ImageSnapshots.CaptureFrame(expected.Frames[i]), reference.DecodeDisplayedFrame(i), name);
        }

        if (expected.PosterFrame is { } poster)
        {
            Compare(ImageSnapshots.CaptureFrame(poster), reference.DecodePoster(), context + ", poster");
        }

        return reference;
    }

    private static void Compare(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context + " decoded by the reference reader");
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static void Check(bool condition, string context, string message)
    {
        if (!condition)
            throw new GoldenAssertionException($"{context}: {message}.");
    }
}
