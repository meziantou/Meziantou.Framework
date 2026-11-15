using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoders of the blocks of 4×4 texels of the S3TC (BC1, BC2, BC3; Khronos Data Format Specification 1.4, section 18)
/// and RGTC (BC4, BC5; section 19) formats: the 16 texels of a block in, the bytes of the block out.
/// </summary>
/// <remarks>
/// <para>
/// Texels are in row-major order (texel <c>x + 4y</c>), as <see cref="BlockDecoder"/> writes them. The encoders evaluate
/// every candidate with the palettes of <see cref="BlockDecoder"/> (the same rounding), so the error they minimize, the sum
/// of the squared differences of the channels, is the error of the decoded block.
/// </para>
/// <para>
/// The colors are fitted along the principal axis of the texels, then refined by least squares on the indices they get
/// (fast). The thorough level adds a cluster fit (every split of the texels, ordered along the axis, into the clusters of
/// the palette, solved by least squares) and a local search of the 5:6:5 endpoints. A block of one color is encoded with
/// the endpoints whose interpolated entry is nearest that color, from tables computed once.
/// </para>
/// <para>
/// The encoders are deterministic (integer decisions, scalar double arithmetic in a fixed order), allocation-free and
/// accept any input.
/// </para>
/// </remarks>
internal static class S3tcBlockEncoder
{
    /// <summary>The maximum number of passes of the local search of the endpoints (thorough level).</summary>
    private const int MaxLocalSearchPasses = 16;

    /// <summary>The half width of the window of endpoints searched around the extremes of an RGTC channel (thorough level).</summary>
    private const int ChannelSearchRadius = 4;

    /// <summary>
    /// The endpoints of a block of one color, per channel: for each 8-bit value, the endpoints <c>(a, b)</c> of 5 or 6 bits
    /// whose palette entry is the nearest to the value. Four tables of 256 pairs: 5 bits with the entry
    /// <c>(2a + b) / 3</c> of the four-color mode, 5 bits with the entry <c>(a + b) / 2</c> of the three-color mode, then the
    /// same for 6 bits. <c>a = b</c> is among the pairs, so an exact endpoint is found when it is the best.
    /// </summary>
    private static readonly byte[] SolidColorTables = BuildSolidColorTables();

    /// <summary>
    /// Encodes a BC1 block (section 18.2).
    /// </summary>
    /// <param name="rgba">The 64 bytes of the 16 RGBA texels.</param>
    /// <param name="block">The 8 bytes of the block.</param>
    /// <param name="allowTransparency">
    /// <see langword="false"/> to ignore alpha (every texel decodes opaque: the transparent entry of the three-color mode is
    /// never used, because DXGI_FORMAT_BC1_UNORM decodes it transparent); <see langword="true"/> for the 1-bit alpha of
    /// "BC1 with alpha": texels whose alpha is below 128 decode transparent black, the others opaque.
    /// </param>
    /// <param name="thorough">Whether to search more endpoints (slower, better).</param>
    public static void EncodeBc1(ReadOnlySpan<byte> rgba, Span<byte> block, bool allowTransparency, bool thorough)
    {
        var transparentMask = 0;
        if (allowTransparency)
        {
            for (var i = 0; i < 16; i++)
            {
                if (rgba[(i * 4) + 3] < 128)
                {
                    transparentMask |= 1 << i;
                }
            }
        }

        EncodeColors(rgba, block[..8], transparentMask, allowThreeColorMode: true, thorough);
    }

    /// <summary>
    /// Encodes a BC2 block (section 18.3): the alpha of each texel rounded to the nearest of the 16 levels of 4 bits, then
    /// the colors in the four-color mode (the only one BC2 decodes).
    /// </summary>
    /// <param name="rgba">The 64 bytes of the 16 RGBA texels.</param>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="thorough">Whether to search more endpoints (slower, better).</param>
    public static void EncodeBc2(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
    {
        ulong alpha = 0;
        for (var i = 0; i < 16; i++)
        {
            // The nearest multiple of 17 (alpha / 15 on the 8-bit scale); 255 / 15 is odd, so there is never a tie
            var level = ((rgba[(i * 4) + 3] * 15) + 127) / 255;
            alpha |= (ulong)level << (4 * i);
        }

        BinaryPrimitives.WriteUInt64LittleEndian(block, alpha);
        EncodeColors(rgba, block.Slice(8, 8), transparentMask: 0, allowThreeColorMode: false, thorough);
    }

    /// <summary>
    /// Encodes a BC3 block (section 18.4): the alpha as an unsigned RGTC channel, then the colors in the four-color mode (the
    /// only one BC3 decodes).
    /// </summary>
    /// <param name="rgba">The 64 bytes of the 16 RGBA texels.</param>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="thorough">Whether to search more endpoints (slower, better).</param>
    public static void EncodeBc3(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
    {
        EncodeUnsignedChannel(rgba[3..], stride: 4, block[..8], thorough);
        EncodeColors(rgba, block.Slice(8, 8), transparentMask: 0, allowThreeColorMode: false, thorough);
    }

    /// <summary>
    /// Encodes an unsigned RGTC channel (section 19.1: BC4 unsigned, a channel of BC5 unsigned, the alpha of BC3).
    /// </summary>
    /// <param name="samples">The samples: texel <c>i</c> is at <c>i × stride</c>.</param>
    /// <param name="stride">The distance between two samples, in bytes.</param>
    /// <param name="block">The 8 bytes of the channel.</param>
    /// <param name="thorough">Whether to search more endpoints (slower, better).</param>
    public static void EncodeUnsignedChannel(ReadOnlySpan<byte> samples, int stride, Span<byte> block, bool thorough)
    {
        Span<int> values = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            values[i] = samples[i * stride];
        }

        var (red0, red1, indices) = EncodeChannel(values, 255, thorough);
        WriteChannel(block, (byte)red0, (byte)red1, indices);
    }

    /// <summary>
    /// Encodes a signed RGTC channel (section 19.2: BC4 signed, a channel of BC5 signed).
    /// </summary>
    /// <param name="samples">The samples, in [-1, 1]: texel <c>i</c> is at <c>i × stride</c>. Values out of the range are clamped, NaN is read as 0.</param>
    /// <param name="stride">The distance between two samples, in floats.</param>
    /// <param name="block">The 8 bytes of the channel.</param>
    /// <param name="thorough">Whether to search more endpoints (slower, better).</param>
    /// <remarks>
    /// The format stores multiples of 1/127 from -127 to 127. The stored value -128 also decodes to -1 but changes the
    /// comparison of the endpoints that selects the palette, so it is never written: the channel is encoded as an unsigned
    /// one shifted by 127, whose palettes are the same (the interpolations divide by 5 or 7, which never gives a half, so
    /// the rounding toward the nearest integer does not depend on the sign) with -1 and 1 as the extra entries.
    /// </remarks>
    public static void EncodeSignedChannel(ReadOnlySpan<float> samples, int stride, Span<byte> block, bool thorough)
    {
        Span<int> values = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            var sample = samples[i * stride];
            var value = float.IsNaN(sample) ? 0.0 : Math.Clamp((double)sample, -1.0, 1.0);
            values[i] = (int)Math.Round(value * 127, MidpointRounding.AwayFromZero) + 127;
        }

        var (red0, red1, indices) = EncodeChannel(values, 254, thorough);
        WriteChannel(block, (byte)(sbyte)(red0 - 127), (byte)(sbyte)(red1 - 127), indices);
    }

    /// <summary>The colors of BC1, BC2 and BC3 (section 18.1).</summary>
    /// <param name="rgba">The 16 RGBA texels.</param>
    /// <param name="block">The 8 bytes of the colors.</param>
    /// <param name="transparentMask">The texels that must decode transparent (code 3 of the three-color mode).</param>
    /// <param name="allowThreeColorMode">Whether the three-color mode is decoded (BC1 only).</param>
    /// <param name="thorough">Whether to search more endpoints.</param>
    private static void EncodeColors(ReadOnlySpan<byte> rgba, Span<byte> block, int transparentMask, bool allowThreeColorMode, bool thorough)
    {
        Span<int> colors = stackalloc int[48];
        for (var i = 0; i < 16; i++)
        {
            colors[i * 3] = rgba[i * 4];
            colors[(i * 3) + 1] = rgba[(i * 4) + 1];
            colors[(i * 3) + 2] = rgba[(i * 4) + 2];
        }

        var opaqueCount = 16 - BitOperations.PopCount((uint)transparentMask);
        if (opaqueCount == 0)
        {
            // Every texel is transparent: equal endpoints select the three-color mode, code 3 everywhere
            WriteColors(block, new ColorCandidate(0, 0, ThreeColorMode: true, uint.MaxValue, 0));
            return;
        }

        // A transparent texel needs code 3 of the three-color mode, so the whole block is in that mode
        var allowFourColorMode = transparentMask == 0;
        if (IsSolid(colors, transparentMask, out var solidColor))
        {
            WriteColors(block, EncodeSolidColor(solidColor, transparentMask, allowFourColorMode, allowThreeColorMode));
            return;
        }

        // The mean and the principal axis of the opaque texels
        Span<double> mean = stackalloc double[3];
        Span<double> scatter = stackalloc double[9];
        Span<double> axis = stackalloc double[3];
        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
                continue;

            for (var c = 0; c < 3; c++)
            {
                mean[c] += colors[(i * 3) + c];
            }
        }

        for (var c = 0; c < 3; c++)
        {
            mean[c] /= opaqueCount;
        }

        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
                continue;

            for (var c = 0; c < 3; c++)
            {
                for (var d = 0; d < 3; d++)
                {
                    scatter[(c * 3) + d] += (colors[(i * 3) + c] - mean[c]) * (colors[(i * 3) + d] - mean[d]);
                }
            }
        }

        BcnEncoderMath.PrincipalAxis(scatter, 3, axis);

        var best = new ColorCandidate(0, 0, ThreeColorMode: false, 0, long.MaxValue);
        if (allowFourColorMode)
        {
            FitColors(colors, transparentMask, mean, axis, threeColorMode: false, thorough, ref best);
        }

        if (allowThreeColorMode)
        {
            FitColors(colors, transparentMask, mean, axis, threeColorMode: true, thorough, ref best);
        }

        WriteColors(block, best);
    }

    /// <summary>Gets a value indicating whether every opaque texel has the same color.</summary>
    private static bool IsSolid(ReadOnlySpan<int> colors, int transparentMask, out int color)
    {
        color = -1;
        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
                continue;

            var value = (colors[i * 3] << 16) | (colors[(i * 3) + 1] << 8) | colors[(i * 3) + 2];
            if (color < 0)
            {
                color = value;
            }
            else if (color != value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Encodes a block whose opaque texels have one color: each channel independently takes the endpoints whose entry is
    /// the nearest (every texel uses the same code, so the channels do not interact), in the mode with the smaller error.
    /// </summary>
    private static ColorCandidate EncodeSolidColor(int color, int transparentMask, bool allowFourColorMode, bool allowThreeColorMode)
    {
        Span<int> target = [color >> 16, (color >> 8) & 0xFF, color & 0xFF];
        var best = new ColorCandidate(0, 0, ThreeColorMode: false, 0, long.MaxValue);
        for (var mode = 0; mode < 2; mode++)
        {
            var threeColorMode = mode == 1;
            if (threeColorMode ? !allowThreeColorMode : !allowFourColorMode)
                continue;

            int color0 = 0, color1 = 0;
            long error = 0;
            for (var c = 0; c < 3; c++)
            {
                var sixBits = c == 1;
                var table = ((sixBits ? 2 : 0) + (threeColorMode ? 1 : 0)) * 512;
                int a = SolidColorTables[table + (target[c] * 2)];
                int b = SolidColorTables[table + (target[c] * 2) + 1];
                var shift = c switch { 0 => 11, 1 => 5, _ => 0 };
                color0 |= a << shift;
                color1 |= b << shift;
                var max = sixBits ? 63 : 31;
                var decoded = threeColorMode ? RoundDivide((a + b) * 255, 2 * max) : RoundDivide(((2 * a) + b) * 255, 3 * max);
                error += (decoded - target[c]) * (decoded - target[c]);
            }

            // Code 2 everywhere: (2 × c0 + c1) / 3 in the four-color mode, (c0 + c1) / 2 in the three-color mode
            var indices = 0xAAAAAAAAu;
            if (threeColorMode)
            {
                indices |= (uint)Spread(transparentMask);
            }

            if (error * (16 - BitOperations.PopCount((uint)transparentMask)) < best.Error)
            {
                best = new ColorCandidate((ushort)color0, (ushort)color1, threeColorMode, indices, error * (16 - BitOperations.PopCount((uint)transparentMask)));
            }
        }

        return best;
    }

    /// <summary>Spreads a 16-bit mask to the codes of 2 bits: code 3 for every texel of the mask.</summary>
    private static ulong Spread(int mask)
    {
        ulong result = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                result |= 3UL << (2 * i);
            }
        }

        return result;
    }

    /// <summary>Fits the endpoints of one mode of the colors and keeps the result when it is better than <paramref name="best"/>.</summary>
    private static void FitColors(ReadOnlySpan<int> colors, int transparentMask, ReadOnlySpan<double> mean, ReadOnlySpan<double> axis, bool threeColorMode, bool thorough, ref ColorCandidate best)
    {
        // The endpoints at the extremes of the projections of the texels on the axis
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
                continue;

            var t = 0.0;
            for (var c = 0; c < 3; c++)
            {
                t += (colors[(i * 3) + c] - mean[c]) * axis[c];
            }

            minimum = Math.Min(minimum, t);
            maximum = Math.Max(maximum, t);
        }

        Span<double> endpoint0 = stackalloc double[3];
        Span<double> endpoint1 = stackalloc double[3];
        for (var c = 0; c < 3; c++)
        {
            endpoint0[c] = mean[c] + (minimum * axis[c]);
            endpoint1[c] = mean[c] + (maximum * axis[c]);
        }

        var candidate = Evaluate(colors, transparentMask, Quantize565(endpoint0), Quantize565(endpoint1), threeColorMode);
        Refine(colors, transparentMask, thorough ? 4 : 2, ref candidate);
        if (thorough)
        {
            if (ClusterFit(colors, transparentMask, mean, axis, threeColorMode, endpoint0, endpoint1))
            {
                var clustered = Evaluate(colors, transparentMask, Quantize565(endpoint0), Quantize565(endpoint1), threeColorMode);
                Refine(colors, transparentMask, 4, ref clustered);
                if (clustered.Error < candidate.Error)
                {
                    candidate = clustered;
                }
            }

            LocalSearch(colors, transparentMask, ref candidate);
        }

        if (candidate.Error < best.Error)
        {
            best = candidate;
        }
    }

    /// <summary>
    /// Refines endpoints by least squares: the endpoints that minimize the error of the indices of the candidate, rounded to
    /// 5:6:5, which can change the indices again. Stops when the error no longer decreases.
    /// </summary>
    private static void Refine(ReadOnlySpan<int> colors, int transparentMask, int iterations, ref ColorCandidate candidate)
    {
        Span<double> endpoint0 = stackalloc double[3];
        Span<double> endpoint1 = stackalloc double[3];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (!LeastSquares(colors, transparentMask, candidate, endpoint0, endpoint1))
                return;

            var next = Evaluate(colors, transparentMask, Quantize565(endpoint0), Quantize565(endpoint1), candidate.ThreeColorMode);
            if (next.Error >= candidate.Error)
                return;

            candidate = next;
        }
    }

    /// <summary>Solves the endpoints that minimize the squared error for the codes of a candidate.</summary>
    private static bool LeastSquares(ReadOnlySpan<int> colors, int transparentMask, ColorCandidate candidate, Span<double> endpoint0, Span<double> endpoint1)
    {
        double a = 0, b = 0, c = 0;
        Span<double> x0 = stackalloc double[3];
        Span<double> x1 = stackalloc double[3];
        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
                continue;

            var code = (int)(candidate.Indices >> (2 * i)) & 3;
            var weight = GetWeight(code, candidate.ThreeColorMode);
            a += (1 - weight) * (1 - weight);
            b += (1 - weight) * weight;
            c += weight * weight;
            for (var channel = 0; channel < 3; channel++)
            {
                x0[channel] += (1 - weight) * colors[(i * 3) + channel];
                x1[channel] += weight * colors[(i * 3) + channel];
            }
        }

        var determinant = (a * c) - (b * b);
        if (determinant < 1e-6)
            return false;

        for (var channel = 0; channel < 3; channel++)
        {
            endpoint0[channel] = ((c * x0[channel]) - (b * x1[channel])) / determinant;
            endpoint1[channel] = ((a * x1[channel]) - (b * x0[channel])) / determinant;
        }

        return true;
    }

    /// <summary>The position of the entry of a code between endpoint 0 and endpoint 1.</summary>
    private static double GetWeight(int code, bool threeColorMode) => code switch
    {
        0 => 0,
        1 => 1,
        2 => threeColorMode ? 0.5 : 1.0 / 3,
        _ => 2.0 / 3,
    };

    /// <summary>
    /// The cluster fit: orders the texels along the axis and tries every split of that order into consecutive clusters, one
    /// per entry of the palette (from endpoint 0 to endpoint 1), solving the endpoints of each split by least squares. The
    /// split with the smallest error (before rounding to 5:6:5) gives the endpoints.
    /// </summary>
    /// <returns><see langword="true"/> when a split has a solution.</returns>
    private static bool ClusterFit(ReadOnlySpan<int> colors, int transparentMask, ReadOnlySpan<double> mean, ReadOnlySpan<double> axis, bool threeColorMode, Span<double> endpoint0, Span<double> endpoint1)
    {
        // Order the opaque texels by projection (insertion sort, ties by texel: deterministic)
        Span<int> order = stackalloc int[16];
        Span<double> projection = stackalloc double[16];
        var count = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
                continue;

            var t = 0.0;
            for (var c = 0; c < 3; c++)
            {
                t += (colors[(i * 3) + c] - mean[c]) * axis[c];
            }

            var position = count;
            while (position > 0 && projection[position - 1] > t)
            {
                projection[position] = projection[position - 1];
                order[position] = order[position - 1];
                position--;
            }

            projection[position] = t;
            order[position] = i;
            count++;
        }

        // Prefix sums of the colors in that order
        Span<int> prefix = stackalloc int[17 * 3];
        for (var k = 0; k < count; k++)
        {
            for (var c = 0; c < 3; c++)
            {
                prefix[((k + 1) * 3) + c] = prefix[(k * 3) + c] + colors[(order[k] * 3) + c];
            }
        }

        // The clusters take the entries in palette order: 0, 1/3, 2/3, 1 (four colors) or 0, 1/2, 1 (three colors). The
        // three-color mode is the four-color one whose third cluster is empty, with 1/2 for the second cluster.
        var bestScore = double.MinValue;
        int bestFirst = -1, bestSecond = -1, bestThird = -1;
        var middle = threeColorMode ? 0.5 : 1.0 / 3;
        for (var first = 0; first <= count; first++)
        {
            for (var second = first; second <= count; second++)
            {
                var lastThird = threeColorMode ? second : count;
                for (var third = second; third <= lastThird; third++)
                {
                    var score = SplitScore(prefix, count, first, second, third, middle);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        (bestFirst, bestSecond, bestThird) = (first, second, third);
                    }
                }
            }
        }

        if (bestFirst < 0)
            return false;

        SolveSplit(prefix, count, bestFirst, bestSecond, bestThird, middle, endpoint0, endpoint1);
        return true;
    }

    /// <summary>
    /// The score of a split of the cluster fit: the texels of order [0, first) take endpoint 0, [first, second) the entry
    /// at <paramref name="middle"/>, [second, third) the entry at 2/3, [third, count) endpoint 1. The residual of the
    /// least-squares endpoints <c>a</c> and <c>b</c> is <c>Σ x² − (a · X0 + b · X1)</c>, and
    /// <c>a · X0 + b · X1 = (C · X0² − 2B · X0 · X1 + A · X1²) / (AC − B²)</c>, so the best split has the largest score.
    /// </summary>
    private static double SplitScore(ReadOnlySpan<int> prefix, int count, int first, int second, int third, double middle)
    {
        var (a, b, c) = SplitWeights(count, first, second, third, middle);
        var determinant = (a * c) - (b * b);
        if (determinant < 1e-6)
            return double.MinValue;

        var score = 0.0;
        for (var channel = 0; channel < 3; channel++)
        {
            var (x0, x1) = SplitSums(prefix, count, first, second, third, middle, channel);
            score += (c * x0 * x0) - (2 * b * x0 * x1) + (a * x1 * x1);
        }

        return score / determinant;
    }

    /// <summary>The least-squares endpoints of a split of the cluster fit.</summary>
    private static void SolveSplit(ReadOnlySpan<int> prefix, int count, int first, int second, int third, double middle, Span<double> endpoint0, Span<double> endpoint1)
    {
        var (a, b, c) = SplitWeights(count, first, second, third, middle);
        var determinant = (a * c) - (b * b);
        for (var channel = 0; channel < 3; channel++)
        {
            var (x0, x1) = SplitSums(prefix, count, first, second, third, middle, channel);
            endpoint0[channel] = ((c * x0) - (b * x1)) / determinant;
            endpoint1[channel] = ((a * x1) - (b * x0)) / determinant;
        }
    }

    /// <summary>The sums <c>Σ (1 − w)²</c>, <c>Σ (1 − w) w</c> and <c>Σ w²</c> of the weights of a split.</summary>
    private static (double A, double B, double C) SplitWeights(int count, int first, int second, int third, double middle)
    {
        var size1 = second - first;
        var size2 = third - second;
        var size3 = count - third;
        const double Two = 2.0 / 3;
        var a = first + (size1 * (1 - middle) * (1 - middle)) + (size2 * (1 - Two) * (1 - Two));
        var b = (size1 * (1 - middle) * middle) + (size2 * (1 - Two) * Two);
        var c = (size1 * middle * middle) + (size2 * Two * Two) + size3;
        return (a, b, c);
    }

    /// <summary>The sums <c>Σ (1 − w) x</c> and <c>Σ w x</c> of a channel of a split.</summary>
    private static (double X0, double X1) SplitSums(ReadOnlySpan<int> prefix, int count, int first, int second, int third, double middle, int channel)
    {
        double sum0 = prefix[(first * 3) + channel];
        double sum1 = prefix[(second * 3) + channel] - prefix[(first * 3) + channel];
        double sum2 = prefix[(third * 3) + channel] - prefix[(second * 3) + channel];
        double sum3 = prefix[(count * 3) + channel] - prefix[(third * 3) + channel];
        const double Two = 2.0 / 3;
        var x0 = sum0 + ((1 - middle) * sum1) + ((1 - Two) * sum2);
        var x1 = (middle * sum1) + (Two * sum2) + sum3;
        return (x0, x1);
    }

    /// <summary>
    /// Moves each 5:6:5 component of the endpoints by one step while the error decreases: the rounding of the least-squares
    /// solution is not the best rounding, and the decoder's rounding of the interpolated entries matters.
    /// </summary>
    private static void LocalSearch(ReadOnlySpan<int> colors, int transparentMask, ref ColorCandidate candidate)
    {
        for (var pass = 0; pass < MaxLocalSearchPasses; pass++)
        {
            var improved = false;
            for (var component = 0; component < 6; component++)
            {
                var shift = (component % 3) switch { 0 => 11, 1 => 5, _ => 0 };
                var max = component % 3 == 1 ? 63 : 31;
                for (var delta = -1; delta <= 1; delta += 2)
                {
                    int color0 = candidate.Color0;
                    int color1 = candidate.Color1;
                    ref var color = ref component < 3 ? ref color0 : ref color1;
                    var value = ((color >> shift) & max) + delta;
                    if (value < 0 || value > max)
                        continue;

                    color = (color & ~(max << shift)) | (value << shift);
                    var next = Evaluate(colors, transparentMask, (ushort)color0, (ushort)color1, candidate.ThreeColorMode);
                    if (next.Error < candidate.Error)
                    {
                        candidate = next;
                        improved = true;
                    }
                }
            }

            if (!improved)
                return;
        }
    }

    /// <summary>Rounds an endpoint of 8-bit channels to 5:6:5.</summary>
    private static ushort Quantize565(ReadOnlySpan<double> endpoint)
    {
        var r = QuantizeChannel(endpoint[0], 31);
        var g = QuantizeChannel(endpoint[1], 63);
        var b = QuantizeChannel(endpoint[2], 31);
        return (ushort)((r << 11) | (g << 5) | b);
    }

    private static int QuantizeChannel(double value, int max)
    {
        // NaN cannot reach here (the inputs are bytes), but Math.Clamp keeps the cast defined anyway
        var clamped = Math.Clamp(value, 0, 255);
        return Math.Clamp((int)((clamped * max / 255) + 0.5), 0, max);
    }

    /// <summary>
    /// Computes the codes and the error of a pair of endpoints in a mode: each opaque texel takes the nearest entry (codes 0
    /// to 2 in the three-color mode, whose code 3 is transparent), each transparent texel code 3.
    /// </summary>
    private static ColorCandidate Evaluate(ReadOnlySpan<int> colors, int transparentMask, ushort color0, ushort color1, bool threeColorMode)
    {
        Span<int> palette = stackalloc int[12];
        ComputePalette(color0, color1, threeColorMode, palette);
        var codes = threeColorMode ? 3 : 4;
        uint indices = 0;
        long error = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((transparentMask & (1 << i)) != 0)
            {
                indices |= 3u << (2 * i);
                continue;
            }

            var bestCode = 0;
            var bestError = int.MaxValue;
            for (var code = 0; code < codes; code++)
            {
                var dr = palette[code * 3] - colors[i * 3];
                var dg = palette[(code * 3) + 1] - colors[(i * 3) + 1];
                var db = palette[(code * 3) + 2] - colors[(i * 3) + 2];
                var distance = (dr * dr) + (dg * dg) + (db * db);
                if (distance < bestError)
                {
                    bestError = distance;
                    bestCode = code;
                }
            }

            indices |= (uint)bestCode << (2 * i);
            error += bestError;
        }

        return new ColorCandidate(color0, color1, threeColorMode, indices, error);
    }

    /// <summary>The RGB entries of the palette, as <see cref="BlockDecoder"/> decodes them (entry 3 of the three-color mode is unused).</summary>
    private static void ComputePalette(ushort color0, ushort color1, bool threeColorMode, Span<int> palette)
    {
        int r0 = color0 >> 11, g0 = (color0 >> 5) & 0x3F, b0 = color0 & 0x1F;
        int r1 = color1 >> 11, g1 = (color1 >> 5) & 0x3F, b1 = color1 & 0x1F;
        palette[0] = RoundDivide(r0 * 255, 31);
        palette[1] = RoundDivide(g0 * 255, 63);
        palette[2] = RoundDivide(b0 * 255, 31);
        palette[3] = RoundDivide(r1 * 255, 31);
        palette[4] = RoundDivide(g1 * 255, 63);
        palette[5] = RoundDivide(b1 * 255, 31);
        if (threeColorMode)
        {
            palette[6] = RoundDivide((r0 + r1) * 255, 2 * 31);
            palette[7] = RoundDivide((g0 + g1) * 255, 2 * 63);
            palette[8] = RoundDivide((b0 + b1) * 255, 2 * 31);
        }
        else
        {
            palette[6] = RoundDivide(((2 * r0) + r1) * 255, 3 * 31);
            palette[7] = RoundDivide(((2 * g0) + g1) * 255, 3 * 63);
            palette[8] = RoundDivide(((2 * b0) + b1) * 255, 3 * 31);
            palette[9] = RoundDivide((r0 + (2 * r1)) * 255, 3 * 31);
            palette[10] = RoundDivide((g0 + (2 * g1)) * 255, 3 * 63);
            palette[11] = RoundDivide((b0 + (2 * b1)) * 255, 3 * 31);
        }
    }

    /// <summary>
    /// Writes the colors of a candidate, ordering the endpoints as its mode requires: <c>color0 &gt; color1</c> selects the
    /// four-color mode in BC1, <c>color0 ≤ color1</c> the three-color mode. Swapping the endpoints swaps codes 0 and 1 (and
    /// 2 and 3 in the four-color mode), which keeps every decoded entry.
    /// </summary>
    private static void WriteColors(Span<byte> block, ColorCandidate candidate)
    {
        var color0 = candidate.Color0;
        var color1 = candidate.Color1;
        var indices = candidate.Indices;
        if (!candidate.ThreeColorMode)
        {
            if (color0 == color1)
            {
                // Equal endpoints decode in the three-color mode in BC1, whose code 3 is transparent; every entry is the
                // same color anyway (in BC2 and BC3 too), so code 0 keeps the result
                indices = 0;
            }
            else if (color0 < color1)
            {
                (color0, color1) = (color1, color0);
                indices ^= 0x55555555u;
            }
        }
        else if (color0 > color1)
        {
            (color0, color1) = (color1, color0);
            uint swapped = 0;
            for (var i = 0; i < 16; i++)
            {
                var code = (indices >> (2 * i)) & 3;
                swapped |= (code < 2 ? code ^ 1 : code) << (2 * i);
            }

            indices = swapped;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(block, color0);
        BinaryPrimitives.WriteUInt16LittleEndian(block[2..], color1);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], indices);
    }

    /// <summary>
    /// Encodes the 16 values of an RGTC channel (0 to <paramref name="maximum"/>) to its endpoints and 3-bit indices. The
    /// palette depends on the order of the endpoints: <c>red0 &gt; red1</c> interpolates 6 entries between them (sevenths),
    /// <c>red0 ≤ red1</c> interpolates 4 (fifths) and adds 0 and <paramref name="maximum"/>.
    /// </summary>
    private static (int Red0, int Red1, ulong Indices) EncodeChannel(ReadOnlySpan<int> values, int maximum, bool thorough)
    {
        int minimum = int.MaxValue, maximumValue = int.MinValue;
        int innerMinimum = int.MaxValue, innerMaximum = int.MinValue;
        foreach (var value in values)
        {
            minimum = Math.Min(minimum, value);
            maximumValue = Math.Max(maximumValue, value);
            if (value != 0 && value != maximum)
            {
                innerMinimum = Math.Min(innerMinimum, value);
                innerMaximum = Math.Max(innerMaximum, value);
            }
        }

        if (minimum == maximumValue)
            return (minimum, minimum, 0);

        // The eight-entry palette spans the values; the six-entry one spans the values other than 0 and the maximum, which
        // its two extra entries represent exactly
        var bestRed0 = maximumValue;
        var bestRed1 = minimum;
        var bestError = EvaluateChannel(values, bestRed0, bestRed1, maximum, long.MaxValue, out var bestIndices);
        int sixRed0 = 0, sixRed1 = 0;
        if (innerMinimum <= innerMaximum)
        {
            sixRed0 = innerMinimum;
            sixRed1 = innerMaximum;
        }

        var error = EvaluateChannel(values, sixRed0, sixRed1, maximum, bestError, out var indices);
        if (error < bestError)
        {
            (bestRed0, bestRed1, bestError, bestIndices) = (sixRed0, sixRed1, error, indices);
        }

        if (thorough && bestError > 0)
        {
            // Every pair of endpoints near the extremes of both palettes; the pair order selects the palette, so the window
            // of one palette also tries the other where the extremes are close
            SearchChannelWindow(values, maximum, maximumValue, minimum, ref bestRed0, ref bestRed1, ref bestError, ref bestIndices);
            if (innerMinimum <= innerMaximum)
            {
                SearchChannelWindow(values, maximum, innerMinimum, innerMaximum, ref bestRed0, ref bestRed1, ref bestError, ref bestIndices);
            }

            // Then single steps from the best pair while the error decreases
            for (var pass = 0; pass < MaxLocalSearchPasses && bestError > 0; pass++)
            {
                var improved = false;
                for (var neighbor = 0; neighbor < 4; neighbor++)
                {
                    var red0 = bestRed0 + neighbor switch { 0 => -1, 1 => 1, _ => 0 };
                    var red1 = bestRed1 + neighbor switch { 2 => -1, 3 => 1, _ => 0 };
                    if (red0 < 0 || red0 > maximum || red1 < 0 || red1 > maximum)
                        continue;

                    error = EvaluateChannel(values, red0, red1, maximum, bestError, out indices);
                    if (error < bestError)
                    {
                        (bestRed0, bestRed1, bestError, bestIndices) = (red0, red1, error, indices);
                        improved = true;
                    }
                }

                if (!improved)
                    break;
            }
        }

        return (bestRed0, bestRed1, bestIndices);
    }

    private static void SearchChannelWindow(ReadOnlySpan<int> values, int maximum, int center0, int center1, ref int bestRed0, ref int bestRed1, ref long bestError, ref ulong bestIndices)
    {
        for (var red0 = Math.Max(center0 - ChannelSearchRadius, 0); red0 <= Math.Min(center0 + ChannelSearchRadius, maximum); red0++)
        {
            for (var red1 = Math.Max(center1 - ChannelSearchRadius, 0); red1 <= Math.Min(center1 + ChannelSearchRadius, maximum); red1++)
            {
                var error = EvaluateChannel(values, red0, red1, maximum, bestError, out var indices);
                if (error < bestError)
                {
                    (bestRed0, bestRed1, bestError, bestIndices) = (red0, red1, error, indices);
                }
            }
        }
    }

    /// <summary>
    /// Computes the indices and the error of a pair of RGTC endpoints, with the palette of <see cref="BlockDecoder"/>. Stops
    /// early (returning a value of at least <paramref name="limit"/>) once the error reaches the limit.
    /// </summary>
    private static long EvaluateChannel(ReadOnlySpan<int> values, int red0, int red1, int maximum, long limit, out ulong indices)
    {
        Span<int> palette = stackalloc int[8];
        palette[0] = red0;
        palette[1] = red1;
        if (red0 > red1)
        {
            for (var i = 1; i < 7; i++)
            {
                palette[i + 1] = RoundDivide(((7 - i) * red0) + (i * red1), 7);
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                palette[i + 1] = RoundDivide(((5 - i) * red0) + (i * red1), 5);
            }

            palette[6] = 0;
            palette[7] = maximum;
        }

        indices = 0;
        long error = 0;
        for (var i = 0; i < 16; i++)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var index = 0; index < 8; index++)
            {
                var difference = palette[index] - values[i];
                var distance = difference * difference;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            indices |= (ulong)bestIndex << (3 * i);
            error += bestDistance;
            if (error >= limit)
                return error;
        }

        return error;
    }

    private static void WriteChannel(Span<byte> block, byte red0, byte red1, ulong indices)
    {
        block[0] = red0;
        block[1] = red1;
        for (var i = 0; i < 6; i++)
        {
            block[2 + i] = (byte)(indices >> (8 * i));
        }
    }

    /// <summary>Rounds a non-negative fraction to the nearest integer, halves up (as <see cref="BlockDecoder"/>).</summary>
    private static int RoundDivide(int numerator, int denominator) => ((2 * numerator) + denominator) / (2 * denominator);

    private static byte[] BuildSolidColorTables()
    {
        var tables = new byte[4 * 512];
        Span<int> pairs = stackalloc int[256];
        for (var table = 0; table < 4; table++)
        {
            var max = table >= 2 ? 63 : 31;
            var threeColorMode = (table & 1) == 1;

            // The first pair (in the order of a, then b) that decodes to each value
            pairs.Fill(-1);
            for (var a = 0; a <= max; a++)
            {
                for (var b = 0; b <= max; b++)
                {
                    var decoded = threeColorMode ? RoundDivide((a + b) * 255, 2 * max) : RoundDivide(((2 * a) + b) * 255, 3 * max);
                    if (pairs[decoded] < 0)
                    {
                        pairs[decoded] = (a << 8) | b;
                    }
                }
            }

            for (var value = 0; value < 256; value++)
            {
                // The nearest reachable value, below first on a tie
                var pair = -1;
                for (var distance = 0; pair < 0; distance++)
                {
                    if (value - distance >= 0 && pairs[value - distance] >= 0)
                    {
                        pair = pairs[value - distance];
                    }
                    else if (value + distance < 256 && pairs[value + distance] >= 0)
                    {
                        pair = pairs[value + distance];
                    }
                }

                tables[(table * 512) + (value * 2)] = (byte)(pair >> 8);
                tables[(table * 512) + (value * 2) + 1] = (byte)pair;
            }
        }

        return tables;
    }

    /// <summary>A pair of 5:6:5 endpoints in a mode, the codes of the texels and the squared error.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ColorCandidate(ushort Color0, ushort Color1, bool ThreeColorMode, uint Indices, long Error);
}
