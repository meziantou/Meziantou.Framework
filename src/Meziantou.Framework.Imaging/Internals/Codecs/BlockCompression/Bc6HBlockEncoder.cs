using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the blocks of 4×4 texels of BC6H (Khronos Data Format Specification 1.4, section 20.2, and the Direct3D 11.3
/// functional specification), the inverse of <see cref="BlockDecoder.DecodeBc6H"/>.
/// </summary>
/// <remarks>
/// <para>
/// The encoder works in the domain of the interpolation of the decoder: the unquantized endpoints are 16-bit integers
/// (0 to 65535 for BC6H_UF16, -32767 to 32767 for BC6H_SF16) and a texel is the half-float bit pattern of its value scaled
/// by 31/64 (31/32 for the magnitude of the signed format). Each half value is first mapped to the integer of this domain
/// that decodes to it, and every error is measured there. As the bit pattern of a half float is roughly the logarithm of
/// its value (an exponent, then a linear mantissa), an error in this domain is roughly a relative error, as the eye
/// perceives HDR content, and it is the domain where the format interpolates: a fit there is a fit of what the decoder
/// computes.
/// </para>
/// <para>
/// The unsigned format stores 0 to 65504 (the largest finite half, 0x7BFF), the signed one -65504 to 65504: neither stores
/// infinities nor NaN. The unsigned encoder stores negative values and -0 as 0; both store NaN as 0 and infinities (and,
/// for the unsigned format, any larger pattern) as the largest finite value of their sign.
/// </para>
/// <para>
/// The fast level fits one line by its principal axis (power iteration on the covariance), refines its endpoints by least
/// squares on the chosen indices, and tries the four modes of one subset (Table 121: mode 0x03 of untransformed 10-bit
/// endpoints, and modes 0x07, 0x0B and 0x0F whose second endpoint is a delta of the first, with 11, 12 and 16 bits of
/// precision when the delta fits). The thorough level also ranks the 32 partitions of two subsets by the residual of a
/// line fit of each subset and tries the ten modes of two subsets on the best of them.
/// </para>
/// </remarks>
internal static class Bc6HBlockEncoder
{
    /// <summary>The number of partitions that the thorough level encodes with every mode of two subsets.</summary>
    private const int PartitionCandidates = 3;

    /// <summary>Encodes a block.</summary>
    /// <param name="rgba">The 64 half-float bit patterns of the 16 RGBA texels, row by row (texel <c>x + 4y</c>); alpha is ignored.</param>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="isSigned">Whether the format is BC6H_SF16 rather than BC6H_UF16.</param>
    /// <param name="thorough">Whether to also search the modes of two subsets.</param>
    public static void Encode(ReadOnlySpan<ushort> rgba, Span<byte> block, bool isSigned, bool thorough)
    {
        Span<int> target = stackalloc int[48];
        for (var i = 0; i < 16; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                target[(i * 3) + channel] = ToInterpolated(rgba[(i * 4) + channel], isSigned);
            }
        }

        Span<byte> candidate = stackalloc byte[16];
        Span<double> endpoints = stackalloc double[12];
        var bestError = long.MaxValue;
        FitSubset(target, subsets: 1, partition: 0, subset: 0, endpoints);
        foreach (var mode in BptcTables.Bc6HModes)
        {
            if (mode.HasTwoSubsets)
                continue;

            var error = EncodeMode(mode, partition: 0, endpoints, target, isSigned, candidate, iterations: thorough ? 4 : 3);
            if (error < bestError)
            {
                bestError = error;
                candidate.CopyTo(block);
            }
        }

        if (!thorough || bestError == 0)
            return;

        // Rank the partitions by the residual of a line fit of each subset: the modes of two subsets have 3-bit indices
        // and fewer endpoint bits, so they only win where one line does not fit the block
        Span<double> residuals = stackalloc double[32];
        for (var partition = 0; partition < 32; partition++)
        {
            residuals[partition] = FitSubset(target, 2, partition, 0, endpoints) + FitSubset(target, 2, partition, 1, endpoints[6..]);
        }

        Span<int> selected = stackalloc int[PartitionCandidates];
        for (var k = 0; k < PartitionCandidates; k++)
        {
            var best = -1;
            for (var partition = 0; partition < 32; partition++)
            {
                if (selected[..k].Contains(partition))
                    continue;

                if (best < 0 || residuals[partition] < residuals[best])
                {
                    best = partition;
                }
            }

            selected[k] = best;
        }

        foreach (var partition in selected)
        {
            FitSubset(target, 2, partition, 0, endpoints);
            FitSubset(target, 2, partition, 1, endpoints[6..]);
            foreach (var mode in BptcTables.Bc6HModes)
            {
                if (!mode.HasTwoSubsets)
                    continue;

                var error = EncodeMode(mode, partition, endpoints, target, isSigned, candidate, iterations: 2);
                if (error < bestError)
                {
                    bestError = error;
                    candidate.CopyTo(block);
                }
            }
        }
    }

    /// <summary>
    /// Maps a half-float bit pattern to the value of the interpolation domain that decodes to it: the unsigned decoder
    /// returns <c>(v × 31) &gt;&gt; 6</c>, the signed one <c>(|v| × 31) &gt;&gt; 5</c> and the sign.
    /// </summary>
    internal static int ToInterpolated(ushort half, bool isSigned)
    {
        var magnitude = half & 0x7FFF;
        if (magnitude > 0x7C00)
            return 0; // NaN

        if (magnitude == 0x7C00)
        {
            magnitude = 0x7BFF; // An infinity is the largest finite value
        }

        if (isSigned)
        {
            // The smallest v of 32h <= 31v < 32h + 32 (an interval of 31 values of 31v holds exactly one multiple of 31)
            var value = Math.Min(((magnitude * 32) + 30) / 31, 32767);
            return (half & 0x8000) != 0 ? -value : value;
        }

        if ((half & 0x8000) != 0)
            return 0;

        // The middle of 64h <= 31v < 64h + 64
        return Math.Min(((magnitude * 64) + 46) / 31, 65535);
    }

    /// <summary>
    /// Fits a line to the texels of a subset: its endpoints are the extreme projections of the texels on the principal
    /// axis. Returns the residual: the sum of the squared distances of the texels to the line.
    /// </summary>
    private static double FitSubset(ReadOnlySpan<int> target, int subsets, int partition, int subset, Span<double> endpoints)
    {
        double meanR = 0, meanG = 0, meanB = 0;
        var count = 0;
        for (var i = 0; i < 16; i++)
        {
            if (BptcTables.GetSubset(subsets, partition, i) != subset)
                continue;

            meanR += target[i * 3];
            meanG += target[(i * 3) + 1];
            meanB += target[(i * 3) + 2];
            count++;
        }

        meanR /= count;
        meanG /= count;
        meanB /= count;

        double rr = 0, rg = 0, rb = 0, gg = 0, gb = 0, bb = 0;
        for (var i = 0; i < 16; i++)
        {
            if (BptcTables.GetSubset(subsets, partition, i) != subset)
                continue;

            var r = target[i * 3] - meanR;
            var g = target[(i * 3) + 1] - meanG;
            var b = target[(i * 3) + 2] - meanB;
            rr += r * r;
            rg += r * g;
            rb += r * b;
            gg += g * g;
            gb += g * b;
            bb += b * b;
        }

        // Power iteration, from the column of the channel of the largest variance (never orthogonal to the axis)
        double axisR, axisG, axisB;
        if (rr >= gg && rr >= bb)
        {
            (axisR, axisG, axisB) = (rr, rg, rb);
        }
        else if (gg >= bb)
        {
            (axisR, axisG, axisB) = (rg, gg, gb);
        }
        else
        {
            (axisR, axisG, axisB) = (rb, gb, bb);
        }

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var scale = Math.Max(Math.Abs(axisR), Math.Max(Math.Abs(axisG), Math.Abs(axisB)));
            if (scale == 0)
                break;

            (axisR, axisG, axisB) = (axisR / scale, axisG / scale, axisB / scale);
            (axisR, axisG, axisB) = ((rr * axisR) + (rg * axisG) + (rb * axisB), (rg * axisR) + (gg * axisG) + (gb * axisB), (rb * axisR) + (gb * axisG) + (bb * axisB));
        }

        var length2 = (axisR * axisR) + (axisG * axisG) + (axisB * axisB);
        var total = rr + gg + bb;
        if (length2 <= 0 || total <= 0)
        {
            endpoints[0] = endpoints[3] = meanR;
            endpoints[1] = endpoints[4] = meanG;
            endpoints[2] = endpoints[5] = meanB;
            return 0;
        }

        double low = double.MaxValue, high = double.MinValue, projected = 0;
        for (var i = 0; i < 16; i++)
        {
            if (BptcTables.GetSubset(subsets, partition, i) != subset)
                continue;

            var t = (((target[i * 3] - meanR) * axisR) + ((target[(i * 3) + 1] - meanG) * axisG) + ((target[(i * 3) + 2] - meanB) * axisB)) / length2;
            low = Math.Min(low, t);
            high = Math.Max(high, t);
            projected += t * t * length2;
        }

        endpoints[0] = meanR + (low * axisR);
        endpoints[1] = meanG + (low * axisG);
        endpoints[2] = meanB + (low * axisB);
        endpoints[3] = meanR + (high * axisR);
        endpoints[4] = meanG + (high * axisG);
        endpoints[5] = meanB + (high * axisB);
        ClampToBounds(target, subsets, partition, subset, endpoints);
        return Math.Max(total - projected, 0);
    }

    /// <summary>
    /// Clamps the endpoints of a subset to the bounding box of its texels. Past it, the interpolation of the half-float
    /// bit patterns would make values far larger than any texel (most visibly across zero in the signed format), at a small
    /// gain of error in this domain.
    /// </summary>
    private static void ClampToBounds(ReadOnlySpan<int> target, int subsets, int partition, int subset, Span<double> endpoints)
    {
        for (var channel = 0; channel < 3; channel++)
        {
            int min = int.MaxValue, max = int.MinValue;
            for (var i = 0; i < 16; i++)
            {
                if (BptcTables.GetSubset(subsets, partition, i) != subset)
                    continue;

                min = Math.Min(min, target[(i * 3) + channel]);
                max = Math.Max(max, target[(i * 3) + channel]);
            }

            endpoints[channel] = Math.Clamp(endpoints[channel], min, max);
            endpoints[3 + channel] = Math.Clamp(endpoints[3 + channel], min, max);
        }
    }

    /// <summary>
    /// Encodes the block in a mode and a partition from endpoints in the interpolation domain, refining them by least
    /// squares; returns the squared error of the best attempt, written to <paramref name="output"/>.
    /// </summary>
    private static long EncodeMode(BptcTables.Bc6HMode mode, int partition, ReadOnlySpan<double> initialEndpoints, ReadOnlySpan<int> target, bool isSigned, Span<byte> output, int iterations)
    {
        var subsets = mode.HasTwoSubsets ? 2 : 1;
        var indexBits = subsets == 1 ? 4 : 3;
        var weights = BptcTables.GetWeights(indexBits);
        Span<double> endpoints = stackalloc double[12];
        initialEndpoints[..(subsets * 6)].CopyTo(endpoints);
        Span<int> quantized = stackalloc int[12];
        Span<int> unquantized = stackalloc int[12];
        Span<byte> indices = stackalloc byte[16];
        var bestError = long.MaxValue;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            QuantizeEndpoints(mode, partition, endpoints, target, isSigned, quantized, unquantized);
            var error = ComputeIndices(subsets, partition, weights, unquantized, target, indices);
            if (error < bestError)
            {
                bestError = error;
                Pack(mode, partition, quantized, indices, output);
            }

            if (error == 0 || iteration == iterations - 1)
                break;

            Refine(subsets, partition, weights, indices, target, endpoints);
        }

        return bestError;
    }

    /// <summary>
    /// Quantizes the endpoints to the precision of a mode. The first endpoint of each subset is the one whose anchor index
    /// has its high bit clear (the anchor index has one bit less), and the endpoints of a transformed mode are deltas from
    /// the first one: a delta that does not fit is clamped.
    /// </summary>
    private static void QuantizeEndpoints(BptcTables.Bc6HMode mode, int partition, Span<double> endpoints, ReadOnlySpan<int> target, bool isSigned, Span<int> quantized, Span<int> unquantized)
    {
        var subsets = mode.HasTwoSubsets ? 2 : 1;
        var indexBits = subsets == 1 ? 4 : 3;
        var weights = BptcTables.GetWeights(indexBits);
        int bits = mode.EndpointBits;
        for (var i = 0; i < subsets * 6; i++)
        {
            quantized[i] = Quantize(endpoints[i], bits, isSigned);
            unquantized[i] = Unquantize(quantized[i], bits, isSigned);
        }

        for (var subset = 0; subset < subsets; subset++)
        {
            var anchor = subset == 0 ? 0 : BptcTables.TwoSubsetAnchors[partition];
            if (BestIndex(weights, unquantized[(subset * 6)..], target[(anchor * 3)..], weights.Length) < weights.Length / 2)
                continue;

            // Swapping the endpoints mirrors the indices exactly: the weights are symmetric (w[n - 1 - k] = 64 - w[k])
            for (var i = subset * 6; i < (subset * 6) + 3; i++)
            {
                (quantized[i], quantized[i + 3]) = (quantized[i + 3], quantized[i]);
                (endpoints[i], endpoints[i + 3]) = (endpoints[i + 3], endpoints[i]);
            }
        }

        if (mode.Transformed)
        {
            var mask = (1 << bits) - 1;
            for (var e = 1; e < subsets * 2; e++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var deltaBits = GetDeltaBits(mode, channel);
                    var low = -(1 << (deltaBits - 1));
                    var high = (1 << (deltaBits - 1)) - 1;

                    // The decoder adds the delta modulo 2^bits: a wrapped difference may fit where the plain one does not
                    var difference = quantized[(e * 3) + channel] - quantized[channel];
                    var wrapped = SignExtend(difference & mask, bits);
                    if (wrapped < low || wrapped > high)
                    {
                        quantized[(e * 3) + channel] = quantized[channel] + Math.Clamp(difference, low, high);
                    }
                }
            }
        }

        for (var i = 0; i < subsets * 6; i++)
        {
            unquantized[i] = Unquantize(quantized[i], bits, isSigned);
        }
    }

    /// <summary>Chooses the indices of the texels (the anchors restricted to the low half); returns the squared error.</summary>
    private static long ComputeIndices(int subsets, int partition, ReadOnlySpan<byte> weights, ReadOnlySpan<int> unquantized, ReadOnlySpan<int> target, Span<byte> indices)
    {
        long error = 0;
        for (var i = 0; i < 16; i++)
        {
            var subset = BptcTables.GetSubset(subsets, partition, i);
            var limit = BptcTables.IsAnchor(subsets, partition, i) ? weights.Length / 2 : weights.Length;
            var index = BestIndex(weights, unquantized[(subset * 6)..], target[(i * 3)..], limit);
            indices[i] = (byte)index;
            error += TexelError(weights[index], unquantized[(subset * 6)..], target[(i * 3)..]);
        }

        return error;
    }

    private static int BestIndex(ReadOnlySpan<byte> weights, ReadOnlySpan<int> endpoints, ReadOnlySpan<int> texel, int limit)
    {
        // The interpolated colors are on the segment of the endpoints: the projection of the texel on it gives the nearest
        // weight, and only its neighbors can be nearer once the decoder's rounding is applied
        long dot = 0, length2 = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            long direction = endpoints[3 + channel] - endpoints[channel];
            dot += direction * (texel[channel] - endpoints[channel]);
            length2 += direction * direction;
        }

        var estimate = 0;
        if (length2 > 0)
        {
            var weight = 64.0 * dot / length2;
            while (estimate < limit - 1 && weights[estimate + 1] <= weight)
            {
                estimate++;
            }
        }

        var best = 0;
        var bestError = long.MaxValue;
        for (var k = Math.Max(estimate - 1, 0); k <= Math.Min(estimate + 2, limit - 1); k++)
        {
            var error = TexelError(weights[k], endpoints, texel);
            if (error < bestError)
            {
                bestError = error;
                best = k;
            }
        }

        return best;
    }

    private static long TexelError(int weight, ReadOnlySpan<int> endpoints, ReadOnlySpan<int> texel)
    {
        long error = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            // Equation 2, as the decoder interpolates
            long difference = ((((64 - weight) * endpoints[channel]) + (weight * endpoints[3 + channel]) + 32) >> 6) - texel[channel];
            error += difference * difference;
        }

        return error;
    }

    /// <summary>Solves the least-squares endpoints of each subset for the weights of the chosen indices.</summary>
    private static void Refine(int subsets, int partition, ReadOnlySpan<byte> weights, ReadOnlySpan<byte> indices, ReadOnlySpan<int> target, Span<double> endpoints)
    {
        Span<double> sumA = stackalloc double[3];
        Span<double> sumB = stackalloc double[3];
        for (var subset = 0; subset < subsets; subset++)
        {
            double aa = 0, ab = 0, bb = 0;
            sumA.Clear();
            sumB.Clear();
            for (var i = 0; i < 16; i++)
            {
                if (BptcTables.GetSubset(subsets, partition, i) != subset)
                    continue;

                var t = weights[indices[i]] / 64.0;
                var s = 1 - t;
                aa += s * s;
                ab += s * t;
                bb += t * t;
                for (var channel = 0; channel < 3; channel++)
                {
                    sumA[channel] += s * target[(i * 3) + channel];
                    sumB[channel] += t * target[(i * 3) + channel];
                }
            }

            var determinant = (aa * bb) - (ab * ab);
            if (determinant < 1e-6)
                continue; // Every texel has the same weight: keep the endpoints

            for (var channel = 0; channel < 3; channel++)
            {
                var a = ((bb * sumA[channel]) - (ab * sumB[channel])) / determinant;
                var b = ((aa * sumB[channel]) - (ab * sumA[channel])) / determinant;
                endpoints[(subset * 6) + channel] = a;
                endpoints[(subset * 6) + 3 + channel] = b;
            }

            ClampToBounds(target, subsets, partition, subset, endpoints.Slice(subset * 6, 6));
        }
    }

    /// <summary>Finds the endpoint of a number of bits whose unquantized value is the nearest to a value.</summary>
    private static int Quantize(double value, int bits, bool isSigned)
    {
        if (!isSigned)
        {
            value = Math.Clamp(value, 0, 65535);
            if (bits >= 16)
                return (int)Math.Round(value, MidpointRounding.ToEven);

            var max = (1 << bits) - 1;
            return Nearest((int)(value * (1 << bits) / 65536), 0, max, value, bits, isSigned: false);
        }

        value = Math.Clamp(value, -32767, 32767);
        if (bits >= 16)
            return (int)Math.Round(value, MidpointRounding.ToEven); // Never -32768, which the decoder would make -infinity

        var magnitude = Nearest((int)(Math.Abs(value) * (1 << (bits - 1)) / 32768), 0, (1 << (bits - 1)) - 1, Math.Abs(value), bits, isSigned: true);
        return value < 0 ? -magnitude : magnitude;
    }

    private static int Nearest(int estimate, int min, int max, double value, int bits, bool isSigned)
    {
        var best = Math.Clamp(estimate, min, max);
        var bestDistance = Math.Abs(Unquantize(best, bits, isSigned) - value);
        for (var candidate = Math.Max(estimate - 1, min); candidate <= Math.Min(estimate + 1, max); candidate++)
        {
            var distance = Math.Abs(Unquantize(candidate, bits, isSigned) - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static int Unquantize(int value, int bits, bool isSigned) => isSigned ? BlockDecoder.UnquantizeSigned(value, bits) : BlockDecoder.UnquantizeUnsigned(value, bits);

    private static int GetDeltaBits(BptcTables.Bc6HMode mode, int channel) => channel switch
    {
        0 => mode.RedBits,
        1 => mode.GreenBits,
        _ => mode.BlueBits,
    };

    private static int SignExtend(int value, int bits) => (value << (32 - bits)) >> (32 - bits);

    /// <summary>Writes the fields of a mode in the order of its layout (Table 122), then the indices.</summary>
    private static void Pack(BptcTables.Bc6HMode mode, int partition, ReadOnlySpan<int> quantized, ReadOnlySpan<byte> indices, Span<byte> output)
    {
        var subsets = mode.HasTwoSubsets ? 2 : 1;
        int bits = mode.EndpointBits;
        var mask = (1 << bits) - 1;
        Span<int> fields = stackalloc int[14];
        fields.Clear();
        fields[0] = mode.Number;
        fields[1] = partition;
        for (var e = 0; e < subsets * 2; e++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var value = quantized[(e * 3) + channel];
                if (e > 0 && mode.Transformed)
                {
                    // The delta, as the decoder reads it: sign-extended from its bits, added modulo 2^bits
                    value = SignExtend((value - quantized[channel]) & mask, bits) & ((1 << GetDeltaBits(mode, channel)) - 1);
                }

                fields[2 + (e * 3) + channel] = value & mask;
            }
        }

        var writer = new BitWriter();
        foreach (var destination in BptcTables.Bc6HLayouts.Slice(mode.LayoutOffset, mode.LayoutLength))
        {
            writer.Write((uint)(fields[destination >> 4] >> (destination & 0xF)) & 1, 1);
        }

        var indexBits = subsets == 1 ? 4 : 3;
        for (var i = 0; i < 16; i++)
        {
            writer.Write(indices[i], BptcTables.IsAnchor(subsets, partition, i) ? indexBits - 1 : indexBits);
        }

        writer.CopyTo(output);
    }

    /// <summary>Writes the bits of a 128-bit block from the least significant one.</summary>
    [StructLayout(LayoutKind.Auto)]
    private ref struct BitWriter
    {
        private ulong _low;
        private ulong _high;
        private int _position;

        public void Write(uint value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var bit = (ulong)((value >> i) & 1);
                if (_position < 64)
                {
                    _low |= bit << _position;
                }
                else
                {
                    _high |= bit << (_position - 64);
                }

                _position++;
            }
        }

        public readonly void CopyTo(Span<byte> output)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(output, _low);
            BinaryPrimitives.WriteUInt64LittleEndian(output[8..], _high);
        }
    }
}
