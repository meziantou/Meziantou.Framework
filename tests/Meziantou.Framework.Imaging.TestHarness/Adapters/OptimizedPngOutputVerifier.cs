using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// Verifies the output of <c>PngOptimization.MinimumSize</c> against the image that was saved, with the independent harness
/// reader (<see cref="ReferencePng"/>): any color type and bit depth, a <c>tRNS</c> color key, and APNG frames that are
/// regions of the canvas blended with SOURCE or OVER and disposed with any operation. The pixels are compared exactly in
/// 16-bit RGBA, after the frames are composited by the reader (<see cref="ReferencePng.ComposeDisplayedFramesRgba16"/>,
/// which rejects any OVER pixel whose result depends on the rounding of a decoder). The library decoder is never used.
/// </summary>
public static class OptimizedPngOutputVerifier
{
    /// <summary>Verifies an encoded static PNG.</summary>
    /// <param name="png">The encoded file.</param>
    /// <param name="expected">The saved image.</param>
    /// <param name="context">A description used in failure messages.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="GoldenAssertionException">The output does not match.</exception>
    public static ReferencePng VerifyStill(ReadOnlySpan<byte> png, Image expected, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        context ??= "optimized PNG output";
        var reference = Parse(png, context);
        Check(reference.Width == expected.Width && reference.Height == expected.Height, context, $"the image is {reference.Width}x{reference.Height}, {expected.Width}x{expected.Height} expected");
        Check(reference.Animation is null, context, "an acTL chunk was written for a static image");
        Compare(expected.Frames[0], reference.DecodeImageRgba16(), context);
        return reference;
    }

    /// <summary>Verifies an encoded APNG.</summary>
    /// <param name="png">The encoded file.</param>
    /// <param name="expected">The saved image: its displayed frames, separate poster and animation settings are the intent.</param>
    /// <param name="context">A description used in failure messages.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="GoldenAssertionException">The output does not match.</exception>
    public static ReferencePng VerifyAnimation(ReadOnlySpan<byte> png, Image expected, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        context ??= "optimized APNG output";
        var reference = Parse(png, context);
        Check(reference.Width == expected.Width && reference.Height == expected.Height, context, $"the canvas is {reference.Width}x{reference.Height}, {expected.Width}x{expected.Height} expected");
        var animation = reference.Animation ?? throw new GoldenAssertionException($"{context}: no acTL chunk (a static PNG was written).");
        Check(animation.NumFrames == expected.Frames.Count, context, $"acTL num_frames is {animation.NumFrames}, {expected.Frames.Count} displayed frames expected (a poster is not counted)");
        Check(animation.NumPlays == (expected.Animation?.TotalPlays ?? 0), context, $"acTL num_plays is {animation.NumPlays}, {expected.Animation?.TotalPlays ?? 0} expected");
        Check(animation.HasSeparatePoster == (expected.PosterFrame is not null), context, $"separate poster {animation.HasSeparatePoster}, expected {expected.PosterFrame is not null}");

        uint sequence = 0;
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            var frame = animation.Frames[i];
            var name = $"{context}, frame {i}";
            Check(frame.SequenceNumber == sequence, name, $"fcTL sequence number {frame.SequenceNumber}, {sequence} expected");
            sequence = frame.SequenceNumber + 1 + (uint)frame.DataSequenceNumbers.Count;
            Check(frame.UsesIdat == (i == 0 && expected.PosterFrame is null), name, frame.UsesIdat ? "uses IDAT" : "uses fdAT");
            Check(frame.DelayDenominator != 0, name, "delay_den is 0 (the encoder always writes an explicit denominator)");
            var duration = expected.Frames[i].Metadata.Duration;
            Check((Int128)frame.DelayNumerator * duration.Denominator == (Int128)duration.Numerator * frame.DelayDenominator, name, $"delay {frame.DelayNumerator}/{frame.DelayDenominator}, {duration} expected exactly");
        }

        IReadOnlyList<RawPixelBuffer> displayed;
        try
        {
            displayed = reference.ComposeDisplayedFramesRgba16();
        }
        catch (InvalidDataException exception)
        {
            throw new GoldenAssertionException($"{context}: {exception.Message}", exception);
        }

        for (var i = 0; i < displayed.Count; i++)
        {
            Compare(expected.Frames[i], displayed[i], $"{context}, frame {i}");
        }

        if (expected.PosterFrame is { } poster)
        {
            Compare(poster, reference.DecodeImageRgba16(), context + ", poster");
        }

        return reference;
    }

    private static ReferencePng Parse(ReadOnlySpan<byte> png, string context)
    {
        try
        {
            return ReferencePng.Parse(png);
        }
        catch (InvalidDataException exception)
        {
            throw new GoldenAssertionException($"{context}: invalid PNG/APNG structure: {exception.Message}", exception);
        }
    }

    private static void Compare(ImageFrame expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(ReferencePng.ToRgba16(ImageSnapshots.CaptureFrame(expected)), actual, ComparisonPolicy.Exact, context + " decoded by the reference reader");
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static void Check(bool condition, string context, string message)
    {
        if (!condition)
            throw new GoldenAssertionException($"{context}: {message}.");
    }
}
