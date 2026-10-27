using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>
/// An independent, high-precision reference of the transformations of the library (rotation, skew, affine and
/// projective transformations), written directly from their definition and sharing no code with the library:
/// <see cref="decimal"/> geometry (<see cref="ReferenceMatrix"/>), kernels, weights and sums, and a direct
/// two-dimensional weighted sum per output pixel over every pixel within the reach of the kernels, where the pixels
/// beyond the edges of the source are the background.
/// </summary>
/// <remarks>
/// <para>
/// The source covers <c>[0, width] x [0, height]</c>; output pixel <c>(x, y)</c> takes the source at the point
/// <c>p = Inverse(x + 1/2, y + 1/2)</c>, or the background when <c>W</c> is not positive there. With
/// <c>c = p - 1/2</c> on each axis, the weight of source pixel <c>i</c> is <c>K((i - c) / f)</c> for every integer with
/// <c>|i - c| &lt; radius f</c>, where <c>f = max(1, |det J| / |row of the other axis of J|)</c> and <c>J</c> is the
/// derivative of the inverse at the point (at most 16 when the transformation is not affine). The value is the sum of the
/// weighted working samples (premultiplied by alpha) divided by the sum of the weights.
/// </para>
/// <para>
/// An expanded canvas holds the bounds of the transformed image, its size being the bound rounded up (or to the nearest
/// integer when it is within 1e-6 of it), with the bounds centered in it.
/// </para>
/// </remarks>
public static class ReferenceTransformer
{
    private const decimal MaxProjectiveScale = 16;

    /// <summary>Computes the size of the canvas.</summary>
    /// <param name="sourceWidth">The source width.</param>
    /// <param name="sourceHeight">The source height.</param>
    /// <param name="options">The options.</param>
    /// <returns>The size.</returns>
    public static (int Width, int Height) ComputeSize(int sourceWidth, int sourceHeight, ReferenceTransformOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (width, height, _) = ComputeCanvas(sourceWidth, sourceHeight, options);
        return (width, height);
    }

    /// <summary>Transforms a buffer.</summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="options">The options.</param>
    /// <returns>The exact output samples.</returns>
    public static ReferenceTransformResult Transform(RawPixelBuffer source, ReferenceTransformOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var layout = source.Layout;
        var channels = layout.ChannelCount;
        if (options.Background.Count != channels)
            throw new ArgumentException("The background must have a sample per channel of the source.", nameof(options));

        var (width, height, forward) = ComputeCanvas(source.Width, source.Height, options);
        var inverse = forward.Invert().Normalize();
        var max = (decimal)layout.MaxSampleValue;
        var values = new decimal[width * height * channels];
        if (TryPermute(source, inverse, width, height, values))
            return new ReferenceTransformResult(options, width, height, layout, values, zeroAlphaIsTransparentBlack: false);

        var nearest = options.Kernel == ReferenceKernel.Nearest;
        var working = nearest ? [] : Working(source, options);
        var background = Working(options.Background, layout, options);

        // The background pixel the library stores: transparent black when its alpha is zero
        var transparentBackground = layout.HasAlpha && options.Background[layout.AlphaChannel] == 0;
        var sums = new decimal[channels];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var output = (((y * width) + x) * channels);
                var px = x + 0.5m;
                var py = y + 0.5m;
                var w = inverse.GetW(px, py);
                if (w <= 0)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        values[output + c] = transparentBackground ? 0 : options.Background[c];
                    }

                    continue;
                }

                var u = ((px * inverse.M11) + (py * inverse.M21) + inverse.M31) / w;
                var v = ((px * inverse.M12) + (py * inverse.M22) + inverse.M32) / w;
                if (nearest)
                {
                    var sx = decimal.Floor(u);
                    var sy = decimal.Floor(v);
                    var inside = sx >= 0 && sx < source.Width && sy >= 0 && sy < source.Height;
                    for (var c = 0; c < channels; c++)
                    {
                        values[output + c] = inside ? source.GetSample((int)sx, (int)sy, c) : transparentBackground ? 0 : options.Background[c];
                    }

                    continue;
                }

                // The derivatives of u = X / W and v = Y / W
                var dudx = (inverse.M11 - (u * inverse.M13)) / w;
                var dudy = (inverse.M21 - (u * inverse.M23)) / w;
                var dvdx = (inverse.M12 - (v * inverse.M13)) / w;
                var dvdy = (inverse.M22 - (v * inverse.M23)) / w;
                var determinant = Math.Abs((dudx * dvdy) - (dudy * dvdx));
                var scaleX = Math.Max(1, determinant / Sqrt((dvdx * dvdx) + (dvdy * dvdy)));
                var scaleY = Math.Max(1, determinant / Sqrt((dudx * dudx) + (dudy * dudy)));
                if (!inverse.IsAffine)
                {
                    scaleX = Math.Min(scaleX, MaxProjectiveScale);
                    scaleY = Math.Min(scaleY, MaxProjectiveScale);
                }

                var xWeights = Weights(u - 0.5m, scaleX, options.Kernel);
                var yWeights = Weights(v - 0.5m, scaleY, options.Kernel);
                Array.Clear(sums);
                var total = 0m;
                foreach (var (sy, wy) in yWeights)
                {
                    foreach (var (sx, wx) in xWeights)
                    {
                        var weight = wx * wy;
                        total += weight;
                        var inside = sx >= 0 && sx < source.Width && sy >= 0 && sy < source.Height;
                        var offset = ((sy * source.Width) + sx) * channels;
                        for (var c = 0; c < channels; c++)
                        {
                            sums[c] += weight * (inside ? working[offset + c] : background[c]);
                        }
                    }
                }

                var alpha = layout.HasAlpha ? sums[layout.AlphaChannel] / total : 1;
                for (var c = 0; c < channels; c++)
                {
                    decimal value;
                    if (c == layout.AlphaChannel)
                    {
                        value = Clamp(alpha, max);
                    }
                    else
                    {
                        var color = sums[c] / total;
                        color = layout.HasAlpha ? (alpha > 0 ? color / alpha : 0) : color;
                        value = options.Linear ? max * ReferenceResampler.Encode(options.Transfer, Clamp(color, 1)) : Clamp(color, max);
                    }

                    values[output + c] = value;
                }
            }
        }

        return new ReferenceTransformResult(options, width, height, layout, values, zeroAlphaIsTransparentBlack: !nearest);
    }

    /// <summary>
    /// Copies the source when the transformation is a quarter turn or a mirror (a matrix of 0, 1 and -1 with one
    /// coefficient per row) that moves the center of every source pixel to the center of a pixel of the canvas and fills
    /// it: a permutation of the pixels, which keeps every sample (the hidden colors of transparent pixels included),
    /// whatever the filter.
    /// </summary>
    private static bool TryPermute(RawPixelBuffer source, ReferenceMatrix inverse, int width, int height, decimal[] values)
    {
        if (!inverse.IsAffine || (long)width * height != (long)source.Width * source.Height)
            return false;

        if (!IsUnit(inverse.M11) || !IsUnit(inverse.M12) || !IsUnit(inverse.M21) || !IsUnit(inverse.M22) || inverse.M11 * inverse.M12 != 0 || inverse.M21 * inverse.M22 != 0)
            return false;

        var channels = source.Layout.ChannelCount;
        var used = new bool[source.Width * source.Height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = inverse.Transform(x + 0.5m, y + 0.5m)!.Value;
                var sx = point.X - 0.5m;
                var sy = point.Y - 0.5m;
                if (sx != decimal.Floor(sx) || sy != decimal.Floor(sy) || sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height || used[(int)((sy * source.Width) + sx)])
                    return false;

                used[(int)((sy * source.Width) + sx)] = true;
                for (var c = 0; c < channels; c++)
                {
                    values[(((y * width) + x) * channels) + c] = source.GetSample((int)sx, (int)sy, c);
                }
            }
        }

        return true;
    }

    private static bool IsUnit(decimal value) => value is 0 or 1 or -1;

    private static (int Width, int Height, ReferenceMatrix Forward) ComputeCanvas(int sourceWidth, int sourceHeight, ReferenceTransformOptions options)
    {
        var transform = options.Forward.Normalize();
        switch (options.Canvas)
        {
            case ReferenceCanvasMode.Expand:
            {
                (decimal X, decimal Y)[] corners = [(0, 0), (sourceWidth, 0), (0, sourceHeight), (sourceWidth, sourceHeight)];
                var points = corners.Select(corner => transform.Transform(corner.X, corner.Y) ?? throw new ArgumentException("A corner is behind the horizon.", nameof(options))).ToArray();
                var left = points.Min(point => point.X);
                var right = points.Max(point => point.X);
                var top = points.Min(point => point.Y);
                var bottom = points.Max(point => point.Y);
                var width = ExpandedSize(right - left);
                var height = ExpandedSize(bottom - top);
                var dx = ((width - (right - left)) / 2) - left;
                var dy = ((height - (bottom - top)) / 2) - top;
                return (width, height, transform.Then(ReferenceMatrix.CreateTranslation(dx, dy)).Normalize());
            }

            case ReferenceCanvasMode.Keep:
            case ReferenceCanvasMode.Explicit:
            {
                var width = options.Canvas == ReferenceCanvasMode.Keep ? sourceWidth : options.CanvasWidth;
                var height = options.Canvas == ReferenceCanvasMode.Keep ? sourceHeight : options.CanvasHeight;
                var forward = options.Centered ? transform.Then(ReferenceMatrix.CreateTranslation(width / 2m, height / 2m)) : transform;
                return (width, height, forward.Normalize());
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static int ExpandedSize(decimal extent)
    {
        var rounded = decimal.Round(extent, MidpointRounding.AwayFromZero);
        return (int)Math.Max(1, Math.Abs(extent - rounded) <= 0.000001m ? rounded : decimal.Ceiling(extent));
    }

    private static decimal Radius(ReferenceKernel kernel) => kernel switch
    {
        ReferenceKernel.Box => 0.5m,
        ReferenceKernel.Triangle or ReferenceKernel.Hermite => 1,
        ReferenceKernel.CatmullRom or ReferenceKernel.MitchellNetravali or ReferenceKernel.Robidoux or ReferenceKernel.RobidouxSharp or ReferenceKernel.Spline or ReferenceKernel.Lanczos2 => 2,
        ReferenceKernel.Lanczos3 => 3,
        ReferenceKernel.Lanczos5 => 5,
        ReferenceKernel.Lanczos8 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(kernel)),
    };

    /// <summary>
    /// The weights <c>K((i - c) / f)</c> of every integer <c>i</c> with <c>|i - c| &lt; radius f</c> (not normalized); for a box,
    /// a weight of 1 for every integer in <c>(c - f/2, c + f/2]</c>, the footprint of the box resize.
    /// </summary>
    private static List<(int Index, decimal Weight)> Weights(decimal center, decimal scale, ReferenceKernel kernel)
    {
        var reach = Radius(kernel) * scale;
        var result = new List<(int Index, decimal Weight)>();
        for (var i = (int)decimal.Floor(center - reach); i <= (int)decimal.Ceiling(center + reach); i++)
        {
            if (kernel == ReferenceKernel.Box)
            {
                if (i > center - reach && i <= center + reach)
                {
                    result.Add((i, 1));
                }
            }
            else if (Math.Abs(i - center) < reach)
            {
                // A distance of 1e-28 is a center the rounding of the geometry moved off a pixel: its square underflows,
                // and the kernel, flat at 0, is 1 there
                var distance = (i - center) / scale;
                result.Add((i, ReferenceResampler.Kernel(kernel, Math.Abs(distance) < 1e-20m ? 0 : distance)));
            }
        }

        return result;
    }

    /// <summary>Working samples: encoded values or linear light, premultiplied by alpha (0 to max) when present.</summary>
    private static decimal[] Working(RawPixelBuffer source, ReferenceTransformOptions options)
    {
        var channels = source.Layout.ChannelCount;
        var result = new decimal[source.Width * source.Height * channels];
        var pixel = new int[channels];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    pixel[c] = source.GetSample(x, y, c);
                }

                Working(pixel, source.Layout, options).CopyTo(result, ((y * source.Width) + x) * channels);
            }
        }

        return result;
    }

    private static decimal[] Working(IReadOnlyList<int> pixel, RawPixelLayout layout, ReferenceTransformOptions options)
    {
        var channels = layout.ChannelCount;
        var max = (decimal)layout.MaxSampleValue;
        decimal alpha = layout.HasAlpha ? pixel[layout.AlphaChannel] : 1;
        var result = new decimal[channels];
        for (var c = 0; c < channels; c++)
        {
            result[c] = c == layout.AlphaChannel ? alpha : (options.Linear ? ReferenceResampler.Decode(options.Transfer, pixel[c] / max) : pixel[c]) * alpha;
        }

        return result;
    }

    private static decimal Sqrt(decimal value)
    {
        if (value <= 0)
            return 0;

        // Newton's iteration from a double estimate: quadratic convergence to the precision of decimal
        var estimate = (decimal)Math.Sqrt((double)value);
        for (var i = 0; i < 8; i++)
        {
            estimate = (estimate + (value / estimate)) / 2;
        }

        return estimate;
    }

    private static decimal Clamp(decimal value, decimal max) => value < 0 ? 0 : value > max ? max : value;
}
