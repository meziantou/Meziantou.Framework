using System.Numerics;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.AutoCrop;

/// <summary>
/// An independent reference of the auto-crop contract of the library, written directly from its documented rules and
/// sharing no code with the library: every pixel of every buffer is visited by plain nested loops (no pruning, no row
/// special case), the border colors are kept in lists searched linearly, and the weights are exact
/// <see cref="BigInteger"/> fractions.
/// </summary>
/// <remarks>
/// <para>The rules, on buffers of the same size and layout given in frame order, the poster last:</para>
/// <list type="number">
/// <item>A pixel is read as red, green, blue and alpha at the precision of the buffer (gray is replicated, a layout
/// without alpha is opaque); a fully transparent pixel is transparent black.</item>
/// <item>The pixels on the one-pixel frame of the analysis rectangle are tallied by their 8-bit reduction
/// (<c>(v * 255 + 32767) / 65535</c> for 16-bit samples), row by row. At most <c>threshold</c> colors are tracked. The
/// background is the first pixel seen of the most frequent tracked color, the first tracked among equals.</item>
/// <item>Each of those pixels also falls in the luma bucket <c>min(10, Y * 11 / 255)</c>, where
/// <c>Y = (2126 R + 7152 G + 722 B + 5000) / 10000</c> on the 8-bit color flattened onto white,
/// <c>(c * a + 255 * (255 - a) + 127) / 255</c>.</item>
/// <item>The border is found when fewer than <c>threshold</c> colors are tracked, or when a bucket threshold is given and
/// the bucket of the background holds at least that share of the tallied pixels.</item>
/// <item>Otherwise, steps 2 to 4 are redone once with <c>ceil(threshold / 2)</c>, no bucket threshold, and the rectangle
/// without <c>floor(width / 20)</c> columns and <c>floor(height / 20)</c> rows on each side.</item>
/// <item>A pixel is content unless <c>2126 |dR| + 7152 |dG| + 722 |dB| &lt;= threshold * 10000 * scale</c> and
/// <c>|dA| &lt; threshold * scale</c>, with the differences taken from the background and <c>scale</c> 1 or 257. The
/// content box is the bounding box of the content pixels of the rectangle in all buffers; it must be at least 3x3.</item>
/// <item>The weights are <c>sum(m * (2x + 1 - W)) / (W * 10000 * max * max * N)</c> and the same with <c>y</c> and
/// <c>H</c>, over the <c>N</c> pixels of all buffers, with <c>m = (2126 |dR| + 7152 |dG| + 722 |dB|) * A</c>.</item>
/// <item>The kept rectangle is the content box grown by the paddings and moved by <c>padding * weight</c> truncated
/// toward zero; it is clamped to the canvas on request, and filled with the background outside the canvas otherwise.</item>
/// </list>
/// </remarks>
public static class ReferenceAutoCrop
{
    /// <summary>Analyzes the frames of an image.</summary>
    /// <param name="frames">The displayed frames followed by the poster, if any; same size and layout.</param>
    /// <param name="options">The options.</param>
    /// <returns>The analysis.</returns>
    public static ReferenceAutoCropAnalysis Analyze(IReadOnlyList<RawPixelBuffer> frames, ReferenceAutoCropOptions options)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfZero(frames.Count);
        var width = frames[0].Width;
        var height = frames[0].Height;
        var layout = frames[0].Layout;
        foreach (var frame in frames)
        {
            if (frame.Width != width || frame.Height != height || frame.Layout != layout)
                throw new ArgumentException("The frames must have the same size and layout.", nameof(frames));
        }

        var threshold = options.ColorThreshold;
        var left = 0;
        var top = 0;
        var right = width;
        var bottom = height;
        var usedRetry = false;
        var border = Tally(frames, left, top, right, bottom, threshold);
        var background = border.Background;
        var found = border.Colors < threshold || (options.BucketThreshold is { } share && border.BackgroundBucket >= share * border.Total);
        if (!found)
        {
            usedRetry = true;
            threshold = (int)Math.Ceiling(threshold / 2m);
            left = width / 20;
            top = height / 20;
            right = width - left;
            bottom = height - top;
            border = Tally(frames, left, top, right, bottom, threshold);
            if (border.Colors >= threshold)
                return Failed(usedRetry, width, height, background);

            background = border.Background;
        }

        var scale = layout.ScaleFrom8Bit;
        int? minX = null;
        int? minY = null;
        int? maxX = null;
        int? maxY = null;
        foreach (var frame in frames)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var pixel = Read(frame, x, y);
                    var isBackground = ColorDifference(pixel, background) <= (long)threshold * 10000 * scale && Math.Abs(pixel.Alpha - background.Alpha) < threshold * scale;
                    if (isBackground)
                        continue;

                    minX = minX is null ? x : Math.Min(minX.Value, x);
                    minY = minY is null ? y : Math.Min(minY.Value, y);
                    maxX = maxX is null ? x : Math.Max(maxX.Value, x);
                    maxY = maxY is null ? y : Math.Max(maxY.Value, y);
                }
            }
        }

        if (minX is null || minY is null || maxX is null || maxY is null)
            return Failed(usedRetry, width, height, background);

        var boxWidth = maxX.Value - minX.Value + 1;
        var boxHeight = maxY.Value - minY.Value + 1;
        if (boxWidth < 3 || boxHeight < 3)
            return Failed(usedRetry, width, height, background);

        var momentX = BigInteger.Zero;
        var momentY = BigInteger.Zero;
        if (options.AnalyzeWeights)
        {
            foreach (var frame in frames)
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = Read(frame, x, y);
                        var mass = new BigInteger(ColorDifference(pixel, background)) * pixel.Alpha;
                        momentX += mass * ((2 * x) + 1 - width);
                        momentY += mass * ((2 * y) + 1 - height);
                    }
                }
            }
        }

        var max = new BigInteger(layout.MaxSampleValue);
        var pixels = new BigInteger(width) * height * frames.Count;
        var denominator = 10000 * max * max * pixels;
        return new ReferenceAutoCropAnalysis(true, usedRetry, minX.Value, minY.Value, boxWidth, boxHeight, background, momentX, momentY, denominator * width, denominator * height);
    }

    /// <summary>Gets the rectangle kept by an auto-crop, in the coordinates of the analyzed canvas.</summary>
    /// <param name="canvasWidth">The width of the canvas.</param>
    /// <param name="canvasHeight">The height of the canvas.</param>
    /// <param name="analysis">A successful analysis.</param>
    /// <param name="options">The options.</param>
    /// <returns>The rectangle; it may reach outside the canvas unless <see cref="ReferenceAutoCropOptions.Contain"/> is set.</returns>
    public static (long X, long Y, long Width, long Height) GetKeptRectangle(int canvasWidth, int canvasHeight, ReferenceAutoCropAnalysis analysis, ReferenceAutoCropOptions options)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(options);

        // BigInteger division truncates toward zero
        var shiftX = (long)BigInteger.Divide(options.PaddingX * analysis.WeightXNumerator, analysis.WeightXDenominator);
        var shiftY = (long)BigInteger.Divide(options.PaddingY * analysis.WeightYNumerator, analysis.WeightYDenominator);
        var x = analysis.X - (long)options.PaddingX + shiftX;
        var y = analysis.Y - (long)options.PaddingY + shiftY;
        var right = x + analysis.Width + (2L * options.PaddingX);
        var bottom = y + analysis.Height + (2L * options.PaddingY);
        if (options.Contain)
        {
            x = Math.Max(x, 0);
            y = Math.Max(y, 0);
            right = Math.Min(right, canvasWidth);
            bottom = Math.Min(bottom, canvasHeight);
        }

        return (x, y, right - x, bottom - y);
    }

    /// <summary>
    /// Gets a value indicating whether the shifts of the kept rectangle are safe from the rounding of a weight to a
    /// double: each exact <c>padding * weight</c> is zero or at least a millionth away from an integer.
    /// </summary>
    /// <param name="analysis">The analysis.</param>
    /// <param name="options">The options.</param>
    /// <returns><see langword="true"/> if any correctly rounded evaluation truncates to the same shifts.</returns>
    public static bool HasRobustShifts(ReferenceAutoCropAnalysis analysis, ReferenceAutoCropOptions options)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(options);
        return IsRobust(options.PaddingX * analysis.WeightXNumerator, analysis.WeightXDenominator)
            && IsRobust(options.PaddingY * analysis.WeightYNumerator, analysis.WeightYDenominator);

        static bool IsRobust(BigInteger numerator, BigInteger denominator)
        {
            if (numerator.IsZero)
                return true;

            var remainder = BigInteger.Abs(numerator) % denominator;
            return remainder * 1_000_000 >= denominator && (denominator - remainder) * 1_000_000 >= denominator;
        }
    }

    /// <summary>Applies an analysis to one buffer of the analyzed image.</summary>
    /// <param name="frame">The buffer.</param>
    /// <param name="analysis">The analysis of all the buffers of the image.</param>
    /// <param name="options">The options.</param>
    /// <returns>The cropped or enlarged buffer; <paramref name="frame"/> itself when the analysis failed or the kept rectangle is the canvas.</returns>
    public static RawPixelBuffer Apply(RawPixelBuffer frame, ReferenceAutoCropAnalysis analysis, ReferenceAutoCropOptions options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(analysis);
        if (!analysis.Success)
            return frame;

        var (x, y, width, height) = GetKeptRectangle(frame.Width, frame.Height, analysis, options);
        if (x == 0 && y == 0 && width == frame.Width && height == frame.Height)
            return frame;

        var layout = frame.Layout;
        var fill = new int[layout.ChannelCount];
        for (var c = 0; c < fill.Length; c++)
        {
            fill[c] = layout.GetChannelName(c) switch
            {
                'R' or 'Y' => analysis.Background.Red,
                'G' => analysis.Background.Green,
                'B' => analysis.Background.Blue,
                _ => analysis.Background.Alpha,
            };
        }

        return frame.Extend(checked((int)x), checked((int)y), checked((int)width), checked((int)height), fill);
    }

    private static ReferenceAutoCropAnalysis Failed(bool usedRetry, int width, int height, (int Red, int Green, int Blue, int Alpha) background)
        => new(false, usedRetry, 0, 0, width, height, background, BigInteger.Zero, BigInteger.Zero, BigInteger.One, BigInteger.One);

    private static (int Red, int Green, int Blue, int Alpha) Read(RawPixelBuffer buffer, int x, int y)
    {
        var layout = buffer.Layout;
        var alpha = layout.HasAlpha ? buffer.GetSample(x, y, layout.AlphaChannel) : layout.MaxSampleValue;
        if (alpha == 0)
            return (0, 0, 0, 0);

        var red = buffer.GetSample(x, y, 0);
        return layout.IsColor ? (red, buffer.GetSample(x, y, 1), buffer.GetSample(x, y, 2), alpha) : (red, red, red, alpha);
    }

    private static long ColorDifference((int Red, int Green, int Blue, int Alpha) pixel, (int Red, int Green, int Blue, int Alpha) background)
        => (2126L * Math.Abs(pixel.Red - background.Red)) + (7152L * Math.Abs(pixel.Green - background.Green)) + (722L * Math.Abs(pixel.Blue - background.Blue));

    private static (int Colors, (int Red, int Green, int Blue, int Alpha) Background, long BackgroundBucket, long Total) Tally(IReadOnlyList<RawPixelBuffer> frames, int left, int top, int right, int bottom, int threshold)
    {
        var keys = new List<(int Red, int Green, int Blue, int Alpha)>();
        var counts = new List<long>();
        var firsts = new List<(int Red, int Green, int Blue, int Alpha)>();
        var buckets = new long[11];
        long total = 0;
        foreach (var frame in frames)
        {
            var sixteenBit = frame.Layout.BytesPerSample == 2;
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    if (x != left && x != right - 1 && y != top && y != bottom - 1)
                        continue;

                    var pixel = Read(frame, x, y);
                    var key = sixteenBit ? (To8Bit(pixel.Red), To8Bit(pixel.Green), To8Bit(pixel.Blue), To8Bit(pixel.Alpha)) : pixel;
                    total++;
                    buckets[Bucket(key)]++;
                    var index = keys.IndexOf(key);
                    if (index >= 0)
                    {
                        counts[index]++;
                    }
                    else if (keys.Count < threshold)
                    {
                        keys.Add(key);
                        counts.Add(1);
                        firsts.Add(pixel);
                    }
                }
            }
        }

        var best = 0;
        for (var i = 1; i < counts.Count; i++)
        {
            if (counts[i] > counts[best])
            {
                best = i;
            }
        }

        return (keys.Count, firsts[best], buckets[Bucket(keys[best])], total);
    }

    private static int To8Bit(int value) => ((value * 255) + 32767) / 65535;

    private static int Bucket((int Red, int Green, int Blue, int Alpha) color)
    {
        var red = OnWhite(color.Red, color.Alpha);
        var green = OnWhite(color.Green, color.Alpha);
        var blue = OnWhite(color.Blue, color.Alpha);
        var luma = ((2126 * red) + (7152 * green) + (722 * blue) + 5000) / 10000;
        return Math.Min(10, luma * 11 / 255);

        static int OnWhite(int sample, int alpha) => ((sample * alpha) + (255 * (255 - alpha)) + 127) / 255;
    }
}
