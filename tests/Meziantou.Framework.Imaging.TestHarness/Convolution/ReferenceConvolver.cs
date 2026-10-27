using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Convolution;

/// <summary>
/// An independent, high-precision reference of the convolution contract of the library, written directly from
/// its mathematical definition and sharing no code with the library: every output pixel is a direct two-dimensional
/// <see cref="decimal"/> sum over a copy of the source (nothing is computed in place, no row is buffered), the edge rules
/// are spelled out index by index, and the sRGB transfer function is the one of <see cref="ReferenceResampler"/>.
/// </summary>
/// <remarks>
/// The reference produces the exact (unrounded) value of every output sample; <see cref="ReferenceFilterResult.Compare"/>
/// then accepts only the correctly rounded value, or either neighbor when the exact value lies within
/// <see cref="ReferenceFilterResult.TieWindow"/> of a rounding boundary.
/// </remarks>
public static class ReferenceConvolver
{
    /// <summary>Convolves a buffer.</summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="options">The options.</param>
    /// <returns>The exact output samples.</returns>
    /// <exception cref="ArgumentException">The matrix does not have odd dimensions or the right number of weights.</exception>
    public static ReferenceFilterResult Convolve(RawPixelBuffer source, ReferenceConvolutionOptions options)
        => Filter(source, options, sharpenAmount: 0, sharpenThreshold: 0);

    /// <summary>
    /// Sharpens a buffer with an unsharp mask: every filtered sample <c>o</c> (premultiplied by alpha, or in linear light,
    /// as the convolution filters it) whose convolution by the blur of <paramref name="options"/> is <c>b</c> becomes
    /// <c>o + amount (o - b)</c>, or stays <c>o</c> when <c>|o - b|</c> is less than <paramref name="threshold"/> times the
    /// range of the sample (alpha: the maximum sample; a color: the maximum sample, or 1 in linear light, times the maximum
    /// sample when premultiplied).
    /// </summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="options">The blur.</param>
    /// <param name="amount">The multiple of the difference that is added.</param>
    /// <param name="threshold">The smallest difference that is sharpened, a fraction of the range.</param>
    /// <returns>The exact output samples.</returns>
    /// <exception cref="ArgumentException">The matrix does not have odd dimensions or the right number of weights.</exception>
    public static ReferenceFilterResult Sharpen(RawPixelBuffer source, ReferenceConvolutionOptions options, decimal amount, decimal threshold)
        => Filter(source, options, amount, threshold);

    /// <summary>
    /// Replaces every sample by the median of the same sample of the square of side <c>2 radius + 1</c> around its pixel
    /// (pixels read beyond the edges as <see cref="Map"/> says, zeros for <see cref="ReferenceEdgeMode.Zero"/>): every
    /// value of the square is gathered and sorted, and the middle one is kept. Alpha is filtered like the colors.
    /// </summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="radius">The number of pixels on each side.</param>
    /// <param name="edgeMode">The pixels read beyond the edges.</param>
    /// <returns>The output samples.</returns>
    public static ReferenceFilterResult Median(RawPixelBuffer source, int radius, ReferenceEdgeMode edgeMode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var layout = source.Layout;
        var channels = layout.ChannelCount;
        var values = new decimal[source.Width * source.Height * channels];
        var window = new List<int>();
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    window.Clear();
                    for (var j = -radius; j <= radius; j++)
                    {
                        for (var i = -radius; i <= radius; i++)
                        {
                            var sx = Map(x + i, source.Width, edgeMode);
                            var sy = Map(y + j, source.Height, edgeMode);
                            window.Add(sx is null || sy is null ? 0 : source.GetSample(sx.Value, sy.Value, c));
                        }
                    }

                    window.Sort();
                    values[(((y * source.Width) + x) * channels) + c] = window[window.Count / 2];
                }
            }
        }

        return new ReferenceFilterResult(string.Create(CultureInfo.InvariantCulture, $"median {radius}, {edgeMode} edges"), source.Width, source.Height, layout, values, zeroAlphaIsTransparentBlack: false, artifactDirectory: "convolution");
    }

    private static ReferenceFilterResult Filter(RawPixelBuffer source, ReferenceConvolutionOptions options, decimal sharpenAmount, decimal sharpenThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.KernelWidth % 2 != 1 || options.KernelHeight % 2 != 1 || options.Weights.Count != options.KernelWidth * options.KernelHeight)
            throw new ArgumentException("The matrix must have odd dimensions and one weight per cell.", nameof(options));

        var layout = source.Layout;
        var channels = layout.ChannelCount;
        var width = source.Width;
        var height = source.Height;
        var max = (decimal)layout.MaxSampleValue;
        var premultiplied = layout.HasAlpha && !options.PreserveAlpha;
        var values = new decimal[width * height * channels];
        var sums = new decimal[channels];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // The weight of matrix cell (i, j) applies to the pixel i - width / 2 columns right and j - height / 2 rows below
                Array.Clear(sums);
                for (var j = 0; j < options.KernelHeight; j++)
                {
                    var sy = Map(y + j - (options.KernelHeight / 2), height, options.EdgeMode);
                    for (var i = 0; i < options.KernelWidth; i++)
                    {
                        var sx = Map(x + i - (options.KernelWidth / 2), width, options.EdgeMode);
                        if (sx is null || sy is null)
                            continue;

                        var weight = options.Weights[(j * options.KernelWidth) + i];
                        decimal alpha = premultiplied ? source.GetSample(sx.Value, sy.Value, layout.AlphaChannel) : 1;
                        for (var c = 0; c < channels; c++)
                        {
                            var sample = (decimal)source.GetSample(sx.Value, sy.Value, c);
                            sums[c] += c == layout.AlphaChannel
                                ? weight * sample
                                : weight * alpha * (options.Linear ? ReferenceResampler.Decode(options.Transfer, sample / max) : sample);
                        }
                    }
                }

                if (sharpenAmount != 0)
                {
                    // The original samples, as the sums filter them
                    decimal alpha = premultiplied ? source.GetSample(x, y, layout.AlphaChannel) : 1;
                    for (var c = 0; c < channels; c++)
                    {
                        var sample = (decimal)source.GetSample(x, y, c);
                        var original = c == layout.AlphaChannel ? sample : alpha * (options.Linear ? ReferenceResampler.Decode(options.Transfer, sample / max) : sample);
                        var range = c == layout.AlphaChannel ? max : (options.Linear ? 1 : max) * (premultiplied ? max : 1);
                        var difference = original - sums[c];
                        sums[c] = Math.Abs(difference) < sharpenThreshold * range ? original : original + (sharpenAmount * difference);
                    }
                }

                for (var c = 0; c < channels; c++)
                {
                    decimal value;
                    if (c == layout.AlphaChannel)
                    {
                        value = premultiplied ? Clamp(sums[c], max) : source.GetSample(x, y, c);
                    }
                    else
                    {
                        // Premultiplied colors are divided by the exact filtered alpha (irrelevant when the alpha rounds to 0)
                        var color = sums[c];
                        if (premultiplied)
                        {
                            var alpha = sums[layout.AlphaChannel];
                            color = alpha > 0 ? color / alpha : 0;
                        }

                        value = options.Linear ? max * ReferenceResampler.Encode(options.Transfer, Clamp(color, 1)) : Clamp(color, max);
                    }

                    values[(((y * width) + x) * channels) + c] = value;
                }
            }
        }

        return new ReferenceFilterResult(options.ToString(), width, height, layout, values, zeroAlphaIsTransparentBlack: premultiplied, artifactDirectory: "convolution");
    }

    /// <summary>Gets the index read for an index that may be outside an axis of <paramref name="length"/> pixels, or <see langword="null"/> when nothing is read.</summary>
    /// <param name="index">The index.</param>
    /// <param name="length">The number of pixels of the axis.</param>
    /// <param name="mode">The edge mode.</param>
    /// <returns>The index inside the image.</returns>
    public static int? Map(int index, int length, ReferenceEdgeMode mode)
    {
        switch (mode)
        {
            case ReferenceEdgeMode.Clamp:
                return Math.Clamp(index, 0, length - 1);

            case ReferenceEdgeMode.Mirror:
                // Fold the index back about the border it is beyond (between pixels -1 and 0, or length - 1 and length)
                // until it is inside
                while (index < 0 || index >= length)
                {
                    index = index < 0 ? -1 - index : (2 * length) - 1 - index;
                }

                return index;

            case ReferenceEdgeMode.Wrap:
                while (index < 0)
                {
                    index += length;
                }

                while (index >= length)
                {
                    index -= length;
                }

                return index;

            case ReferenceEdgeMode.Zero:
                return index >= 0 && index < length ? index : null;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static decimal Clamp(decimal value, decimal max) => value < 0 ? 0 : value > max ? max : value;
}
