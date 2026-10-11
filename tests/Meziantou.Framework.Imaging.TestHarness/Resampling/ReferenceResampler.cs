using System.Numerics;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>
/// An independent, high-precision reference of the resize contract of the library, written directly from its
/// mathematical definition and sharing no code with the library: exact rational geometry (<see cref="Fraction"/>),
/// <see cref="decimal"/> kernels, weights and sums (<see cref="DecimalMath"/> for sine and the sRGB transfer function),
/// and a direct two-dimensional weighted sum per output pixel instead of the library's separable streaming passes.
/// </summary>
/// <remarks>
/// The reference produces the exact (unrounded) value of every output sample; <see cref="ReferenceFilterResult.Compare"/>
/// then accepts only the correctly rounded value, or either neighbor when the exact value lies within
/// <see cref="ReferenceFilterResult.TieWindow"/> of a rounding boundary.
/// </remarks>
public static class ReferenceResampler
{
    /// <summary>Computes the output size, or <see langword="null"/> when the request requires a forbidden enlargement.</summary>
    /// <param name="sourceWidth">The source width.</param>
    /// <param name="sourceHeight">The source height.</param>
    /// <param name="options">The options.</param>
    /// <returns>The output size.</returns>
    public static (int Width, int Height)? ComputeSize(int sourceWidth, int sourceHeight, ReferenceResizeOptions options)
        => ComputeGeometry(sourceWidth, sourceHeight, options) is { } geometry ? (geometry.X.Output, geometry.Y.Output) : null;

    /// <summary>Resizes a buffer.</summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="options">The options.</param>
    /// <returns>The exact output samples.</returns>
    /// <exception cref="ArgumentException">The request requires a forbidden enlargement.</exception>
    public static ReferenceResizeResult Resize(RawPixelBuffer source, ReferenceResizeOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var geometry = ComputeGeometry(source.Width, source.Height, options) ?? throw new ArgumentException("The request requires enlargement.", nameof(options));
        var layout = source.Layout;
        var channels = layout.ChannelCount;
        var width = geometry.X.Output;
        var height = geometry.Y.Output;
        var values = new decimal[width * height * channels];
        if (width == source.Width && height == source.Height)
        {
            // Keeping the canvas size keeps the image unchanged
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = source.GetSample(i / channels % width, i / channels / width, i % channels);
            }

            return new ReferenceResizeResult(options, width, height, layout, values, isUnchanged: true);
        }

        if (options.Kernel == ReferenceKernel.Nearest)
        {
            for (var y = 0; y < height; y++)
            {
                var sy = NearestIndex(geometry.Y, y);
                for (var x = 0; x < width; x++)
                {
                    var sx = NearestIndex(geometry.X, x);
                    var transparent = layout.HasAlpha && source.GetSample(sx, sy, layout.AlphaChannel) == 0;
                    for (var c = 0; c < channels; c++)
                    {
                        values[(((y * width) + x) * channels) + c] = transparent ? 0 : source.GetSample(sx, sy, c);
                    }
                }
            }

            return new ReferenceResizeResult(options, width, height, layout, values);
        }

        var xWeights = Enumerable.Range(0, width).Select(x => Weights(geometry.X, x, options.Kernel)).ToArray();
        var yWeights = Enumerable.Range(0, height).Select(y => Weights(geometry.Y, y, options.Kernel)).ToArray();
        var max = (decimal)layout.MaxSampleValue;
        var working = Working(source, options.Linear);
        var sums = new decimal[channels];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Array.Clear(sums);
                foreach (var (sy, wy) in yWeights[y])
                {
                    foreach (var (sx, wx) in xWeights[x])
                    {
                        var weight = wx * wy;
                        var offset = ((sy * source.Width) + sx) * channels;
                        for (var c = 0; c < channels; c++)
                        {
                            sums[c] += weight * working[offset + c];
                        }
                    }
                }

                var alpha = layout.HasAlpha ? sums[layout.AlphaChannel] : 1;
                for (var c = 0; c < channels; c++)
                {
                    decimal value;
                    if (c == layout.AlphaChannel)
                    {
                        value = Clamp(alpha, max);
                    }
                    else
                    {
                        // Colors are unpremultiplied by the exact filtered alpha (irrelevant when the alpha rounds to 0)
                        var color = layout.HasAlpha ? (alpha > 0 ? sums[c] / alpha : 0) : sums[c];
                        value = options.Linear ? max * EncodeSrgb(Clamp(color, 1)) : Clamp(color, max);
                    }

                    values[(((y * width) + x) * channels) + c] = value;
                }
            }
        }

        return new ReferenceResizeResult(options, width, height, layout, values);
    }

    /// <summary>The sRGB decoding function of IEC 61966-2-1 on a normalized value.</summary>
    /// <param name="value">The encoded value in [0, 1].</param>
    /// <returns>The linear value.</returns>
    public static decimal DecodeSrgb(decimal value) => value <= 0.04045m ? value / 12.92m : DecimalMath.Pow((value + 0.055m) / 1.055m, 2.4m);

    /// <summary>The sRGB encoding function of IEC 61966-2-1 on a normalized value.</summary>
    /// <param name="value">The linear value in [0, 1].</param>
    /// <returns>The encoded value.</returns>
    public static decimal EncodeSrgb(decimal value) => value <= 0.0031308m ? value * 12.92m : (1.055m * DecimalMath.Pow(value, 1 / 2.4m)) - 0.055m;

    /// <summary>Evaluates a kernel.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="x">The distance in (scaled) source pixels.</param>
    /// <returns>The kernel value.</returns>
    public static decimal Kernel(ReferenceKernel kernel, decimal x)
    {
        x = Math.Abs(x);
        switch (kernel)
        {
            case ReferenceKernel.Triangle:
                return x < 1 ? 1 - x : 0;

            case ReferenceKernel.CatmullRom:
                // Keys cubic convolution with a = -0.5
                if (x < 1)
                    return (1.5m * x * x * x) - (2.5m * x * x) + 1;

                return x < 2 ? (-0.5m * x * x * x) + (2.5m * x * x) - (4 * x) + 2 : 0;

            case ReferenceKernel.Lanczos3:
                if (x == 0)
                    return 1;

                return x < 3 ? 3 * DecimalMath.SinPi(x) * DecimalMath.SinPi(x / 3) / (DecimalMath.Pi * DecimalMath.Pi * x * x) : 0;

            default:
                throw new ArgumentOutOfRangeException(nameof(kernel));
        }
    }

    private static decimal Radius(ReferenceKernel kernel) => kernel switch
    {
        ReferenceKernel.Triangle => 1,
        ReferenceKernel.CatmullRom => 2,
        ReferenceKernel.Lanczos3 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kernel)),
    };

    /// <summary>
    /// The geometry: output pixel <c>d</c> of an axis is centered at the continuous source coordinate
    /// <c>offset + (d + 1/2) / scale</c> (source pixel <c>i</c> covers <c>[i, i + 1)</c>).
    /// </summary>
    private static (Axis X, Axis Y)? ComputeGeometry(int sourceWidth, int sourceHeight, ReferenceResizeOptions options)
    {
        var tw = options.TargetWidth;
        var th = options.TargetHeight;
        var sx = new Fraction(tw, sourceWidth);
        var sy = new Fraction(th, sourceHeight);
        switch (options.Mode)
        {
            case ReferenceResizeMode.Stretch:
                if (!options.AllowUpscaling && (sx > 1 || sy > 1))
                    return null;

                return (new Axis(sourceWidth, tw, sx, 0), new Axis(sourceHeight, th, sy, 0));

            case ReferenceResizeMode.Contain:
            {
                var scale = sx < sy ? sx : sy;
                if (!options.AllowUpscaling && scale > 1)
                {
                    scale = 1;
                }

                var width = RoundHalfUpAtLeastOne(sourceWidth * scale);
                var height = RoundHalfUpAtLeastOne(sourceHeight * scale);
                return (new Axis(sourceWidth, width, new Fraction(width, sourceWidth), 0), new Axis(sourceHeight, height, new Fraction(height, sourceHeight), 0));
            }

            case ReferenceResizeMode.Cover:
            {
                var scale = sx > sy ? sx : sy;
                if (!options.AllowUpscaling && scale > 1)
                    return null;

                var anchorX = ToFraction(options.AnchorX);
                var anchorY = ToFraction(options.AnchorY);
                return (
                    new Axis(sourceWidth, tw, scale, anchorX * (sourceWidth - (tw / scale))),
                    new Axis(sourceHeight, th, scale, anchorY * (sourceHeight - (th / scale))));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static int RoundHalfUpAtLeastOne(Fraction value) => (int)BigInteger.Max(1, (value + new Fraction(1, 2)).Floor());

    private static Fraction ToFraction(decimal value) => value switch
    {
        0m => 0,
        0.5m => new Fraction(1, 2),
        1m => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Anchors are 0, 1/2 or 1."),
    };

    private static Fraction Center(Axis axis, int index) => axis.Offset + (new Fraction((2 * index) + 1, 2) / axis.Scale);

    private static int NearestIndex(Axis axis, int index) => (int)BigInteger.Clamp(Center(axis, index).Floor(), 0, axis.Source - 1);

    /// <summary>
    /// The weights of output pixel <paramref name="index"/>: <c>K((i - c) / f)</c> for every integer <c>i</c> (zero outside
    /// the support), with <c>c = center - 1/2</c> and <c>f = max(1, 1 / scale)</c>, normalized by their sum; contributors
    /// outside the source take the value of the nearest edge pixel.
    /// </summary>
    private static List<(int Index, decimal Weight)> Weights(Axis axis, int index, ReferenceKernel kernel)
    {
        var center = (Center(axis, index) - new Fraction(1, 2)).ToDecimal();
        var scale = axis.Scale >= 1 ? 1m : (1 / axis.Scale).ToDecimal();
        var reach = Radius(kernel) * scale;
        var raw = new List<(int Index, decimal Weight)>();
        for (var i = (int)decimal.Floor(center - reach) - 2; i <= (int)decimal.Ceiling(center + reach) + 2; i++)
        {
            raw.Add((i, Kernel(kernel, (i - center) / scale)));
        }

        var sum = raw.Sum(item => item.Weight);
        var merged = new SortedDictionary<int, decimal>();
        foreach (var (i, weight) in raw)
        {
            var clamped = Math.Clamp(i, 0, axis.Source - 1);
            merged[clamped] = merged.GetValueOrDefault(clamped) + (weight / sum);
        }

        return [.. merged.Select(item => (item.Key, item.Value))];
    }

    /// <summary>Working samples: encoded values or linear light, premultiplied by alpha (0 to max) when present.</summary>
    private static decimal[] Working(RawPixelBuffer source, bool linear)
    {
        var layout = source.Layout;
        var channels = layout.ChannelCount;
        var max = (decimal)layout.MaxSampleValue;
        var result = new decimal[source.Width * source.Height * channels];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                decimal alpha = layout.HasAlpha ? source.GetSample(x, y, layout.AlphaChannel) : 1;
                for (var c = 0; c < channels; c++)
                {
                    var sample = (decimal)source.GetSample(x, y, c);
                    var offset = (((y * source.Width) + x) * channels) + c;
                    if (c == layout.AlphaChannel)
                    {
                        result[offset] = sample;
                    }
                    else
                    {
                        result[offset] = (linear ? DecodeSrgb(sample / max) : sample) * alpha;
                    }
                }
            }
        }

        return result;
    }

    private static decimal Clamp(decimal value, decimal max) => value < 0 ? 0 : value > max ? max : value;

    private readonly record struct Axis(int Source, int Output, Fraction Scale, Fraction Offset);
}
