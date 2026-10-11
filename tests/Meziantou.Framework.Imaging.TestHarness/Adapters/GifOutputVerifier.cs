using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Gif;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// Verifies GIF encoder output against the image that was saved, with the independent harness reader
/// (<see cref="ReferenceGif"/>), never the library decoder.
/// </summary>
/// <remarks>
/// <para>
/// The intent of each frame is its pixels after the documented alpha policy (<see cref="ToIntent(RawPixelBuffer, GifEncoder)"/>: 16-bit samples reduced
/// to 8 bits with nearest rounding, gray replicated, alpha below the threshold transparent and the rest opaque, or every
/// pixel flattened onto the background). Structure is checked exactly: GIF89a without global color table, one full-canvas
/// image per displayed frame with a local color table, the interlace flag, a Graphic Control Extension on every animation
/// frame with disposal 2 (restore to background, cleared to transparent) and the transparent flag exactly when the frame
/// has transparent pixels, the delays (exact or nearest hundredths), the NETSCAPE2.0 loop count (repetitions; none for a single
/// play or a still image), the comments, and at most <see cref="GifEncoder.MaxColors"/> palette entries used, the
/// transparent one included.
/// </para>
/// <para>
/// Pixels: transparent pixels must be exactly the intended transparent pixels in every frame (no stale content). A frame
/// whose intent has at most the available number of distinct colors must be reproduced exactly; other frames are quantized
/// and their opaque pixels are reported as a <see cref="ReconstructionError"/> for the caller's quality criteria.
/// </para>
/// </remarks>
public static class GifOutputVerifier
{
    /// <summary>Gets the intended GIF pixels of a frame (8-bit RGBA, transparent pixels stored as 0, 0, 0, 0).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="encoder">The encoder settings.</param>
    /// <returns>The intended pixels.</returns>
    public static RawPixelBuffer ToIntent(ImageFrame frame, GifEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return ToIntent(ImageSnapshots.CaptureFrame(frame), encoder);
    }

    /// <summary>Gets the intended GIF pixels of a raw frame (8-bit RGBA, transparent pixels stored as 0, 0, 0, 0).</summary>
    /// <param name="source">The pixels, in any layout.</param>
    /// <param name="encoder">The encoder settings.</param>
    /// <returns>The intended pixels.</returns>
    public static RawPixelBuffer ToIntent(RawPixelBuffer source, GifEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(encoder);
        var layout = source.Layout;
        var max = layout.MaxSampleValue;
        var result = new byte[source.Width * source.Height * 4];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                int r, g, b;
                if (layout.IsColor)
                {
                    (r, g, b) = (source.GetSample(x, y, 0), source.GetSample(x, y, 1), source.GetSample(x, y, 2));
                }
                else
                {
                    r = g = b = source.GetSample(x, y, 0);
                }

                var a = layout.HasAlpha ? source.GetSample(x, y, layout.AlphaChannel) : max;
                var offset = ((y * source.Width) + x) * 4;
                if (encoder.AlphaMode == GifAlphaMode.Flatten && a != max)
                {
                    // Flattened at source precision (background reduced first for 8-bit sources)
                    var background = encoder.BackgroundColor!.Value;
                    int Background(ushort sample) => max == 255 ? To8(sample) : sample;
                    r = Flatten(r, Background(background.R), a, max);
                    g = Flatten(g, Background(background.G), a, max);
                    b = Flatten(b, Background(background.B), a, max);
                    a = max;
                }

                var opaque = encoder.AlphaMode == GifAlphaMode.Flatten || (max == 255 ? a : To8(a)) >= encoder.AlphaThreshold;
                if (!opaque)
                    continue;

                result[offset] = (byte)(max == 255 ? r : To8(r));
                result[offset + 1] = (byte)(max == 255 ? g : To8(g));
                result[offset + 2] = (byte)(max == 255 ? b : To8(b));
                result[offset + 3] = 255;
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, result);

        static int To8(int value) => ((value * 255) + 32767) / 65535;
        static int Flatten(int sample, int background, int alpha, int max) => (int)((((long)sample * alpha) + ((long)background * (max - alpha)) + (max / 2)) / max);
    }

    /// <summary>Counts the distinct opaque colors and whether transparent pixels exist in an intended frame.</summary>
    /// <param name="intent">The intended pixels (8-bit RGBA).</param>
    /// <returns>The number of distinct opaque colors, and whether any pixel is transparent.</returns>
    public static (int Colors, bool HasTransparency) CountColors(RawPixelBuffer intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var colors = new HashSet<int>();
        var transparent = false;
        var span = intent.Span;
        for (var i = 0; i < span.Length; i += 4)
        {
            if (span[i + 3] == 0)
            {
                transparent = true;
            }
            else
            {
                colors.Add((span[i] << 16) | (span[i + 1] << 8) | span[i + 2]);
            }
        }

        return (colors.Count, transparent);
    }

    /// <summary>Gets the GIF delay of a duration with the documented rounding: nearest hundredth, ties up.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The delay in hundredths of a second.</returns>
    public static long GetNearestDelay(FrameDuration duration) => (long)(((Int128)duration.Numerator * 200 + duration.Denominator) / (2 * (Int128)duration.Denominator));

    /// <summary>Verifies an encoded GIF.</summary>
    /// <param name="gif">The encoded file.</param>
    /// <param name="expected">The saved image (its frames, durations, animation settings and comments are the intent).</param>
    /// <param name="encoder">The encoder settings used.</param>
    /// <param name="animated">Whether the output is an animation (several frames or animation settings); <see langword="null"/> to infer it from the image.</param>
    /// <param name="context">A description used in failure messages.</param>
    /// <returns>The verification result.</returns>
    /// <exception cref="GoldenAssertionException">The output does not match.</exception>
    public static GifVerification Verify(ReadOnlySpan<byte> gif, Image expected, GifEncoder encoder, bool? animated = null, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(encoder);
        context ??= "GIF output";
        var isAnimated = animated ?? expected.IsAnimated;
        ReferenceGif reference;
        try
        {
            reference = ReferenceGif.Parse(gif);
        }
        catch (InvalidDataException exception)
        {
            throw new GoldenAssertionException($"{context}: invalid GIF structure: {exception.Message}", exception);
        }

        Check(reference.Version == "89a", context, $"version {reference.Version}, 89a expected");
        Check(reference.Width == expected.Width && reference.Height == expected.Height, context, $"the logical screen is {reference.Width}x{reference.Height}, {expected.Width}x{expected.Height} expected");
        Check(reference.GlobalColorTable is null, context, "a global color table was written (every frame uses a local table)");
        Check(reference.Images.Count == expected.Frames.Count, context, $"{reference.Images.Count} images, {expected.Frames.Count} displayed frames expected");

        // Animation-wide data before the first image: the loop extension (repetitions), then the comments
        var expectedLoop = isAnimated ? expected.Animation?.TotalPlays switch { null => 0, 1 => (int?)null, var plays => plays - 1 } : null;
        Check(reference.LoopCounts.Count == (expectedLoop is null ? 0 : 1), context, $"{reference.LoopCounts.Count} loop extensions, {(expectedLoop is null ? 0 : 1)} expected");
        if (expectedLoop is not null)
        {
            Check(reference.LoopCounts[0] == expectedLoop, context, $"NETSCAPE2.0 loop count {reference.LoopCounts[0]}, {expectedLoop} expected");
        }

        var firstImage = reference.Blocks.ToList().IndexOf("Image");
        Check(reference.Blocks.Take(firstImage).Count(block => block is "Loop" or "Comment") == reference.LoopCounts.Count + reference.Comments.Count, context, "the loop extension and the comments must precede the first image");
        var comments = expected.Metadata.TextEntries.Where(entry => encoder.MetadataHandling != MetadataHandling.Strip && entry.Keyword == Metadata.ImageTextEntry.CommentKeyword).Select(entry => entry.Value).ToList();
        Check(reference.Comments.SequenceEqual(comments, StringComparer.Ordinal), context, $"comments [{string.Join(", ", reference.Comments)}], [{string.Join(", ", comments)}] expected");

        var displayed = reference.DecodeDisplayedFrames();
        var intents = new List<RawPixelBuffer>(displayed.Count);
        var errors = new List<ReconstructionError?>(displayed.Count);
        for (var i = 0; i < reference.Images.Count; i++)
        {
            var image = reference.Images[i];
            var name = $"{context}, frame {i}";
            var intent = ToIntent(expected.Frames[i], encoder);
            intents.Add(intent);
            var (colors, hasTransparency) = CountColors(intent);
            Check(image.CoversCanvas(reference.Width, reference.Height), name, $"image {image.Width}x{image.Height}+{image.Left}+{image.Top}: a full-canvas image expected");
            Check(image.LocalColorTable is not null, name, "no local color table");
            Check(image.Interlaced == encoder.Interlaced, name, $"interlaced {image.Interlaced}, {encoder.Interlaced} expected");
            var duration = expected.Frames[i].Metadata.Duration;
            var delay = encoder.DurationRounding == FrameDurationRounding.RoundToNearest ? GetNearestDelay(duration) : duration.Numerator * 100 / duration.Denominator;
            if (isAnimated)
            {
                Check(image.Control is not null, name, "an animation frame has no Graphic Control Extension");
                Check(image.Control!.DisposalMethod == 2, name, $"disposal {image.Control.DisposalMethod}, 2 (restore to background) expected");
            }
            else if (image.Control is not null)
            {
                Check(image.Control.DisposalMethod == 0, name, $"disposal {image.Control.DisposalMethod}, 0 expected for a still image");
            }

            Check((image.Control?.DelayHundredths ?? 0) == delay, name, $"delay {image.Control?.DelayHundredths ?? 0}, {delay} hundredths expected for {duration}");
            Check((image.Control?.HasTransparency ?? false) == hasTransparency, name, $"transparent flag {image.Control?.HasTransparency ?? false}, {hasTransparency} expected");

            // Palette entries actually used, the transparent one included
            var used = image.Indices.ToArray().Distinct().Count();
            Check(used <= encoder.MaxColors, name, $"{used} palette entries used, at most {encoder.MaxColors} allowed");

            // Transparency: exactly the intended transparent pixels (no stale content under pixels that became transparent)
            var actual = displayed[i];
            for (var y = 0; y < actual.Height; y++)
            {
                for (var x = 0; x < actual.Width; x++)
                {
                    var expectedAlpha = intent.GetSample(x, y, 3);
                    var actualAlpha = actual.GetSample(x, y, 3);
                    Check(expectedAlpha == actualAlpha, name, $"pixel ({x}, {y}) has alpha {actualAlpha}, {expectedAlpha} expected");
                }
            }

            var available = encoder.MaxColors - (hasTransparency ? 1 : 0);
            if (colors <= available)
            {
                var result = PixelBufferComparer.Compare(intent, actual, ComparisonPolicy.Exact, name + " decoded by the reference reader (exact palette)");
                if (!result.IsMatch)
                    throw new GoldenAssertionException(result.Describe());

                errors.Add(null);
            }
            else
            {
                errors.Add(ReconstructionError.Measure(DropAlpha(intent), DropAlpha(actual)));
            }
        }

        return new GifVerification(reference, displayed, intents, errors);
    }

    private static RawPixelBuffer DropAlpha(RawPixelBuffer rgba)
    {
        var source = rgba.Span;
        var result = new byte[rgba.Width * rgba.Height * 3];
        for (var i = 0; i < result.Length / 3; i++)
        {
            source.Slice(i * 4, 3).CopyTo(result.AsSpan(i * 3));
        }

        return RawPixelBuffer.Create(rgba.Width, rgba.Height, RawPixelLayout.Rgb8, result);
    }

    private static void Check(bool condition, string context, string message)
    {
        if (!condition)
            throw new GoldenAssertionException($"{context}: {message}.");
    }
}
