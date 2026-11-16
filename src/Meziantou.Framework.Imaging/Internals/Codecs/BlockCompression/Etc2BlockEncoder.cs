using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the ETC2 and EAC blocks of 4×4 texels (Khronos Data Format Specification 1.4, sections 21 and 22), the inverse of
/// <see cref="Etc2BlockDecoder"/>. The texels are given row by row (texel <c>x + 4y</c>); the blocks store their pixel
/// indices column by column (a..p), which the encoder transposes.
/// </summary>
/// <remarks>
/// <para>
/// A color block is in one of five modes (section 22.1): individual and differential (ETC1: two sub-blocks of 2×4 or 4×2
/// texels, each a base color and a table of four intensity modifiers), and the modes that ETC2 signals by a differential
/// block whose sum of a base and its delta leaves [0, 31]: T and H (two colors and a distance) and planar (three colors
/// of a gradient). The encoder never writes a differential block whose sums overflow, and sets the free bits of the T, H
/// and planar modes so that exactly the intended sum overflows.
/// </para>
/// <para>
/// The fast level tries both flips of the individual and differential modes (base colors from the averages of the
/// sub-blocks, the best table and modifiers for each) and the planar mode (a least-squares plane, then the nearest
/// codes and their neighbors). The thorough level also searches the neighbors of the base colors, and the T and H modes
/// from a 2-means clustering of the texels. The error is the sum of the squared differences of the three channels.
/// </para>
/// <para>
/// With punchthrough alpha (section 22.9), a texel whose alpha is below 128 is transparent, the others opaque. The bit of
/// the individual mode is the opaque bit: a block with a transparent texel clears it, so its differential sub-blocks have
/// no small modifiers and its index 2 (the third paint color in the T and H modes) is transparent black; such a block
/// cannot be planar. A block of opaque texels sets it, and has every mode but the individual one.
/// </para>
/// </remarks>
internal static class Etc2BlockEncoder
{
    /// <summary>Encodes an ETC2 RGB block (8 bytes) from 16 RGBA texels; alpha is ignored.</summary>
    public static void EncodeRgb(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
        => BinaryPrimitives.WriteUInt64BigEndian(block, EncodeColor(rgba, transparent: 0, punchthrough: false, thorough));

    /// <summary>
    /// Encodes an ETC2 RGB block with punchthrough alpha (8 bytes) from 16 RGBA texels: a texel whose alpha is below 128
    /// is transparent (decoded as transparent black), the others opaque.
    /// </summary>
    public static void EncodePunchthrough(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
    {
        var transparent = 0;
        for (var i = 0; i < 16; i++)
        {
            if (rgba[(i * 4) + 3] < 128)
            {
                transparent |= 1 << i;
            }
        }

        // A transparent block: a differential block without the opaque bit, whose indices are all 2
        var bits = transparent == 0xFFFF ? 0xFFFF_0000UL : EncodeColor(rgba, transparent, punchthrough: true, thorough);
        BinaryPrimitives.WriteUInt64BigEndian(block, bits);
    }

    /// <summary>Encodes an ETC2 RGBA block (16 bytes): the 8-bit EAC alpha block (section 22.3), then the RGB block.</summary>
    public static void EncodeRgba(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
    {
        EacBlockEncoder.EncodeAlpha(rgba, block, thorough);
        EncodeRgb(rgba, block[8..], thorough);
    }

    /// <summary>
    /// Encodes an unsigned EAC R11 block (8 bytes), or one channel of RG11: sample <c>i</c> (row-major) is
    /// <c>samples[i × stride]</c>, a 16-bit value (the decoder extends 11 bits to 16 by replication).
    /// </summary>
    public static void EncodeEacUnsigned(ReadOnlySpan<ushort> samples, int stride, Span<byte> block, bool thorough)
        => EacBlockEncoder.EncodeUnsigned(samples, stride, block, thorough);

    /// <summary>
    /// Encodes a signed EAC R11 block (8 bytes), or one channel of RG11, from values in [-1, 1] (others are clamped, NaN
    /// is 0): sample <c>i</c> (row-major) is <c>samples[i × stride]</c>.
    /// </summary>
    public static void EncodeEacSigned(ReadOnlySpan<float> samples, int stride, Span<byte> block, bool thorough)
        => EacBlockEncoder.EncodeSigned(samples, stride, block, thorough);

    private static ulong EncodeColor(ReadOnlySpan<byte> rgba, int transparent, bool punchthrough, bool thorough)
    {
        Span<int> texels = stackalloc int[48];
        for (var i = 0; i < 16; i++)
        {
            texels[i * 3] = rgba[i * 4];
            texels[(i * 3) + 1] = rgba[(i * 4) + 1];
            texels[(i * 3) + 2] = rgba[(i * 4) + 2];
        }

        // The bit of the differential mode (and of T, H and planar); with punchthrough alpha, the opaque bit
        var differentialBit = !punchthrough || transparent == 0 ? 1u : 0u;
        var best = new Candidate(long.MaxValue, 0);
        EncodeSubblocks(texels, transparent, allowIndividual: !punchthrough, differentialBit, thorough, ref best);
        if (transparent == 0 && best.Error > 0)
        {
            EncodePlanar(texels, ref best);
        }

        if (thorough && best.Error > 0)
        {
            EncodeTAndH(texels, transparent, differentialBit, ref best);
        }

        return best.Bits;
    }

    /// <summary>The individual and differential modes, with both flips.</summary>
    private static void EncodeSubblocks(ReadOnlySpan<int> texels, int transparent, bool allowIndividual, uint differentialBit, bool thorough, ref Candidate best)
    {
        var restricted = transparent != 0;
        var radius = thorough ? 1 : 0;
        Span<double> average = stackalloc double[6];
        Span<int> codes = stackalloc int[2 * 28];
        Span<long> errors = stackalloc long[2 * 28];
        Span<int> counts = stackalloc int[2];
        Span<byte> indices = stackalloc byte[16];
        Span<int> color = stackalloc int[2];
        Span<int> tables = stackalloc int[2];
        for (var flip = 0; flip < 2; flip++)
        {
            average.Clear();
            counts.Clear();
            for (var i = 0; i < 16; i++)
            {
                if ((transparent & (1 << i)) != 0)
                    continue;

                var half = GetHalf(flip, i);
                counts[half]++;
                for (var channel = 0; channel < 3; channel++)
                {
                    average[(half * 3) + channel] += texels[(i * 3) + channel];
                }
            }

            for (var half = 0; half < 2; half++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    average[(half * 3) + channel] = counts[half] == 0 ? 0 : average[(half * 3) + channel] / counts[half];
                }
            }

            if (allowIndividual)
            {
                // Two independent 4-bit base colors
                long total = 0;
                for (var half = 0; half < 2; half++)
                {
                    var count = Neighbors(average.Slice(half * 3, 3), 4, radius, codes);
                    var bestError = long.MaxValue;
                    for (var k = 0; k < count; k++)
                    {
                        var (r, g, b) = Unpack(codes[k], 4);
                        var error = SubblockError(texels, flip, half, Extend4(r), Extend4(g), Extend4(b), transparent, restricted, bestError, out var table);
                        if (error < bestError)
                        {
                            (bestError, color[half], tables[half]) = (error, codes[k], table);
                        }
                    }

                    total += bestError;
                }

                if (total < best.Error)
                {
                    for (var half = 0; half < 2; half++)
                    {
                        var (r, g, b) = Unpack(color[half], 4);
                        WriteSubblockIndices(texels, flip, half, Extend4(r), Extend4(g), Extend4(b), tables[half], transparent, restricted, indices);
                    }

                    var (r1, g1, b1) = Unpack(color[0], 4);
                    var (r2, g2, b2) = Unpack(color[1], 4);
                    var high = ((uint)r1 << 28) | ((uint)r2 << 24) | ((uint)g1 << 20) | ((uint)g2 << 16) | ((uint)b1 << 12) | ((uint)b2 << 8)
                        | ((uint)tables[0] << 5) | ((uint)tables[1] << 2) | (uint)flip;
                    best = new Candidate(total, ((ulong)high << 32) | PackIndices(indices));
                }
            }

            // Differential: a 5-bit base color and a second one within [-4, 3] of it. The candidates of each sub-block are
            // its nearest codes, and the projection of the nearest code of the other sub-block within the delta range
            var counts0 = Neighbors(average[..3], 5, radius, codes);
            var counts1 = Neighbors(average.Slice(3, 3), 5, radius, codes[28..]);
            var nearest0 = NearestCode(average[..3], 5);
            var nearest1 = NearestCode(average.Slice(3, 3), 5);
            codes[counts0++] = Project(nearest0, nearest1, referenceIsFirst: false);
            codes[28 + counts1++] = Project(nearest1, nearest0, referenceIsFirst: true);
            for (var half = 0; half < 2; half++)
            {
                var count = half == 0 ? counts0 : counts1;
                for (var k = 0; k < count; k++)
                {
                    var (r, g, b) = Unpack(codes[(half * 28) + k], 5);
                    errors[(half * 28) + k] = SubblockError(texels, flip, half, Extend5(r), Extend5(g), Extend5(b), transparent, restricted, long.MaxValue, out _);
                }
            }

            var bestPair = long.MaxValue;
            int first = 0, second = 0;
            for (var i = 0; i < counts0; i++)
            {
                for (var j = 0; j < counts1; j++)
                {
                    var error = errors[i] + errors[28 + j];
                    if (error < bestPair && FitsDelta(codes[i], codes[28 + j]))
                    {
                        (bestPair, first, second) = (error, codes[i], codes[28 + j]);
                    }
                }
            }

            if (bestPair < best.Error)
            {
                var (r1, g1, b1) = Unpack(first, 5);
                var (r2, g2, b2) = Unpack(second, 5);
                SubblockError(texels, flip, 0, Extend5(r1), Extend5(g1), Extend5(b1), transparent, restricted, long.MaxValue, out var table1);
                SubblockError(texels, flip, 1, Extend5(r2), Extend5(g2), Extend5(b2), transparent, restricted, long.MaxValue, out var table2);
                WriteSubblockIndices(texels, flip, 0, Extend5(r1), Extend5(g1), Extend5(b1), table1, transparent, restricted, indices);
                WriteSubblockIndices(texels, flip, 1, Extend5(r2), Extend5(g2), Extend5(b2), table2, transparent, restricted, indices);
                var high = ((uint)r1 << 27) | ((uint)((r2 - r1) & 7) << 24) | ((uint)g1 << 19) | ((uint)((g2 - g1) & 7) << 16)
                    | ((uint)b1 << 11) | ((uint)((b2 - b1) & 7) << 8) | ((uint)table1 << 5) | ((uint)table2 << 2) | (differentialBit << 1) | (uint)flip;
                best = new Candidate(bestPair, ((ulong)high << 32) | PackIndices(indices));
            }
        }
    }

    /// <summary>Gets the sub-block of a texel: the left and right halves without flip, the top and bottom ones with it.</summary>
    private static int GetHalf(int flip, int texel) => flip == 0 ? (texel & 3) >> 1 : texel >> 3;

    /// <summary>The texels of the sub-blocks: left, right, top, bottom (index <c>(flip × 2 + half) × 8</c>).</summary>
    private static ReadOnlySpan<byte> SubblockTexels =>
    [
        0, 1, 4, 5, 8, 9, 12, 13,
        2, 3, 6, 7, 10, 11, 14, 15,
        0, 1, 2, 3, 4, 5, 6, 7,
        8, 9, 10, 11, 12, 13, 14, 15,
    ];

    /// <summary>
    /// The error of a sub-block for a base color, with its best table (Table 142): each texel takes its nearest modifier.
    /// Without the opaque bit, index 0 is the base color and index 2 is reserved for the transparent texels.
    /// </summary>
    private static long SubblockError(ReadOnlySpan<int> texels, int flip, int half, int r, int g, int b, int transparent, bool restricted, long limit, out int table)
    {
        var bestError = limit;
        table = 0;
        for (var candidate = 0; candidate < 8; candidate++)
        {
            long error = 0;
            foreach (var i in SubblockTexels.Slice(((flip * 2) + half) * 8, 8))
            {
                if ((transparent & (1 << i)) != 0)
                    continue;

                ChooseModifier(texels.Slice(i * 3, 3), r, g, b, candidate, restricted, out var texelError);
                error += texelError;
                if (error >= bestError)
                    break;
            }

            if (error < bestError)
            {
                bestError = error;
                table = candidate;
            }
        }

        return bestError;
    }

    private static void WriteSubblockIndices(ReadOnlySpan<int> texels, int flip, int half, int r, int g, int b, int table, int transparent, bool restricted, Span<byte> indices)
    {
        foreach (var i in SubblockTexels.Slice(((flip * 2) + half) * 8, 8))
        {
            indices[i] = (transparent & (1 << i)) != 0 ? (byte)2 : (byte)ChooseModifier(texels.Slice(i * 3, 3), r, g, b, table, restricted, out _);
        }
    }

    /// <summary>Chooses the pixel index of a texel (Table 143: 0 is +a, 1 +b, 2 -a, 3 -b).</summary>
    private static int ChooseModifier(ReadOnlySpan<int> texel, int r, int g, int b, int table, bool restricted, out long error)
    {
        var modifiers = Etc2BlockDecoder.IntensityModifiers.Slice(table * 4, 4);
        var best = 0;
        error = long.MaxValue;
        for (var index = 0; index < 4; index++)
        {
            if (restricted && index == 2)
                continue;

            int modifier = index switch
            {
                0 => restricted ? 0 : modifiers[2],
                1 => modifiers[3],
                2 => modifiers[1],
                _ => modifiers[0],
            };
            var candidate = ColorError(texel, r + modifier, g + modifier, b + modifier);
            if (candidate < error)
            {
                error = candidate;
                best = index;
            }
        }

        return best;
    }

    private static long ColorError(ReadOnlySpan<int> texel, int r, int g, int b)
    {
        long dr = Math.Clamp(r, 0, 255) - texel[0];
        long dg = Math.Clamp(g, 0, 255) - texel[1];
        long db = Math.Clamp(b, 0, 255) - texel[2];
        return (dr * dr) + (dg * dg) + (db * db);
    }

    /// <summary>The planar mode (section 22.1, part g): a least-squares plane through the texels, each channel searched alone.</summary>
    private static void EncodePlanar(ReadOnlySpan<int> texels, ref Candidate best)
    {
        Span<int> origin = stackalloc int[3];
        Span<int> horizontal = stackalloc int[3];
        Span<int> vertical = stackalloc int[3];
        long total = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            // p(x, y) = c + a x + b y: on the 4×4 grid, the sums of (x - 1.5)² and (y - 1.5)² are 20
            double sum = 0, sumX = 0, sumY = 0;
            for (var i = 0; i < 16; i++)
            {
                var value = texels[(i * 3) + channel];
                sum += value;
                sumX += ((i & 3) - 1.5) * value;
                sumY += ((i >> 2) - 1.5) * value;
            }

            var a = sumX / 20;
            var b = sumY / 20;
            var c = (sum / 16) - (1.5 * a) - (1.5 * b);
            var bits = channel == 1 ? 7 : 6;
            var o0 = NearestCode(Math.Clamp(c, 0, 255), bits);
            var h0 = NearestCode(Math.Clamp(c + (4 * a), 0, 255), bits);
            var v0 = NearestCode(Math.Clamp(c + (4 * b), 0, 255), bits);
            var max = (1 << bits) - 1;
            var bestError = long.MaxValue;
            for (var o = Math.Max(o0 - 1, 0); o <= Math.Min(o0 + 1, max); o++)
            {
                for (var h = Math.Max(h0 - 1, 0); h <= Math.Min(h0 + 1, max); h++)
                {
                    for (var v = Math.Max(v0 - 1, 0); v <= Math.Min(v0 + 1, max); v++)
                    {
                        var error = PlanarError(texels, channel, Extend(o, bits), Extend(h, bits), Extend(v, bits), bestError);
                        if (error < bestError)
                        {
                            (bestError, origin[channel], horizontal[channel], vertical[channel]) = (error, o, h, v);
                        }
                    }
                }
            }

            total += bestError;
            if (total >= best.Error)
                return;
        }

        // Table 141 part g: RO, GO and BO split around the bits that make the blue sum overflow
        var block = ((ulong)(uint)origin[0] << 57) | ((ulong)(uint)(origin[1] >> 6) << 56) | ((ulong)(uint)(origin[1] & 0x3F) << 49)
            | ((ulong)(uint)(origin[2] >> 5) << 48) | ((ulong)(uint)((origin[2] >> 3) & 3) << 43) | ((ulong)(uint)(origin[2] & 7) << 39)
            | ((ulong)(uint)(horizontal[0] >> 1) << 34) | (1UL << 33) | ((ulong)(uint)(horizontal[0] & 1) << 32)
            | ((ulong)(uint)horizontal[1] << 25) | ((ulong)(uint)horizontal[2] << 19)
            | ((ulong)(uint)vertical[0] << 13) | ((ulong)(uint)vertical[1] << 6) | (ulong)(uint)vertical[2];

        // The red and green sums must not overflow (that would be T or H): the free high bit of each base has the sign
        // of its delta. The blue sum must overflow.
        block |= ((block >> 58) & 1) << 63;
        block |= ((block >> 50) & 1) << 55;
        var (k, sign) = ForceOverflow((uint)(block >> 43) & 3, (uint)(block >> 40) & 3);
        block |= ((ulong)k << 45) | ((ulong)sign << 42);
        best = new Candidate(total, block);
    }

    private static long PlanarError(ReadOnlySpan<int> texels, int channel, int o, int h, int v, long limit)
    {
        long error = 0;
        for (var i = 0; i < 16 && error < limit; i++)
        {
            var (x, y) = (i & 3, i >> 2);
            long difference = Math.Clamp(((x * (h - o)) + (y * (v - o)) + (4 * o) + 2) >> 2, 0, 255) - texels[(i * 3) + channel];
            error += difference * difference;
        }

        return error;
    }

    /// <summary>
    /// The free bits of a base whose sum with its delta must overflow: the base is <c>low + 4k</c> and the delta
    /// <c>delta - 4 × sign</c>, where <c>low</c> and <c>delta</c> are two bits fixed by the other fields.
    /// </summary>
    private static (uint K, uint Sign) ForceOverflow(uint low, uint delta) => low + delta < 4 ? (0u, 1u) : (7u, 0u);

    /// <summary>The T and H modes (section 22.1, parts e and f), from a 2-means clustering of the opaque texels.</summary>
    private static void EncodeTAndH(ReadOnlySpan<int> texels, int transparent, uint differentialBit, ref Candidate best)
    {
        var restricted = transparent != 0;
        Span<double> centers = stackalloc double[6];
        Span<byte> cluster = stackalloc byte[16];
        Cluster(texels, transparent, centers, cluster);

        Span<byte> indices = stackalloc byte[16];
        Span<double> means = stackalloc double[6];
        for (var order = 0; order < 2; order++)
        {
            // T: one color alone (paint 0) and one with three paints; either cluster may be the single color
            var color1 = NearestCode(centers.Slice(order * 3, 3), 4);
            var color2 = NearestCode(centers.Slice((1 - order) * 3, 3), 4);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var error = EvaluateT(texels, color1, color2, transparent, restricted, out var distance, indices);
                if (error < best.Error)
                {
                    best = new Candidate(error, PackT(color1, color2, distance, differentialBit, indices));
                }

                // Refit each color to the texels of its paints, the distance taken out
                int d = Etc2BlockDecoder.Distances[distance];
                means.Clear();
                int count1 = 0, count2 = 0;
                for (var i = 0; i < 16; i++)
                {
                    if ((transparent & (1 << i)) != 0)
                        continue;

                    var offset = indices[i] switch { 1 => d, 3 => -d, _ => 0 };
                    var slot = indices[i] == 0 ? 0 : 3;
                    if (slot == 0)
                    {
                        count1++;
                    }
                    else
                    {
                        count2++;
                    }

                    for (var channel = 0; channel < 3; channel++)
                    {
                        means[slot + channel] += texels[(i * 3) + channel] - offset;
                    }
                }

                if (count1 > 0)
                {
                    Scale(means[..3], count1);
                    color1 = NearestCode(means[..3], 4);
                }

                if (count2 > 0)
                {
                    Scale(means[3..], count2);
                    color2 = NearestCode(means[3..], 4);
                }
            }
        }

        {
            // H: two colors, each with two paints
            var color1 = NearestCode(centers[..3], 4);
            var color2 = NearestCode(centers[3..], 4);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var error = EvaluateH(texels, ref color1, ref color2, transparent, restricted, out var distance, indices);
                if (error < best.Error)
                {
                    best = new Candidate(error, PackH(color1, color2, distance, differentialBit, indices));
                }

                if (error == long.MaxValue)
                    break;

                int d = Etc2BlockDecoder.Distances[distance];
                means.Clear();
                int count1 = 0, count2 = 0;
                for (var i = 0; i < 16; i++)
                {
                    if ((transparent & (1 << i)) != 0)
                        continue;

                    var offset = (indices[i] & 1) == 0 ? d : -d;
                    var slot = indices[i] < 2 ? 0 : 3;
                    if (slot == 0)
                    {
                        count1++;
                    }
                    else
                    {
                        count2++;
                    }

                    for (var channel = 0; channel < 3; channel++)
                    {
                        means[slot + channel] += texels[(i * 3) + channel] - offset;
                    }
                }

                if (count1 > 0)
                {
                    Scale(means[..3], count1);
                    color1 = NearestCode(means[..3], 4);
                }

                if (count2 > 0)
                {
                    Scale(means[3..], count2);
                    color2 = NearestCode(means[3..], 4);
                }
            }
        }
    }

    private static void Scale(Span<double> values, int count)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] /= count;
        }
    }

    /// <summary>
    /// Splits the opaque texels in two clusters: the two farthest texels as seeds, then four iterations of 2-means.
    /// </summary>
    private static void Cluster(ReadOnlySpan<int> texels, int transparent, Span<double> centers, Span<byte> cluster)
    {
        int seed1 = -1, seed2 = -1;
        long farthest = -1;
        for (var i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
                continue;

            for (var j = i; j < 16; j++)
            {
                if ((transparent & (1 << j)) != 0)
                    continue;

                var distance = ColorError(texels.Slice(i * 3, 3), texels[j * 3], texels[(j * 3) + 1], texels[(j * 3) + 2]);
                if (distance > farthest)
                {
                    (farthest, seed1, seed2) = (distance, i, j);
                }
            }
        }

        for (var channel = 0; channel < 3; channel++)
        {
            centers[channel] = texels[(seed1 * 3) + channel];
            centers[3 + channel] = texels[(seed2 * 3) + channel];
        }

        Span<double> sums = stackalloc double[6];
        Span<int> counts = stackalloc int[2];
        for (var iteration = 0; iteration < 4; iteration++)
        {
            sums.Clear();
            counts.Clear();
            for (var i = 0; i < 16; i++)
            {
                if ((transparent & (1 << i)) != 0)
                    continue;

                double distance1 = 0, distance2 = 0;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = texels[(i * 3) + channel];
                    distance1 += (value - centers[channel]) * (value - centers[channel]);
                    distance2 += (value - centers[3 + channel]) * (value - centers[3 + channel]);
                }

                var c = distance2 < distance1 ? 1 : 0;
                cluster[i] = (byte)c;
                counts[c]++;
                for (var channel = 0; channel < 3; channel++)
                {
                    sums[(c * 3) + channel] += texels[(i * 3) + channel];
                }
            }

            for (var c = 0; c < 2; c++)
            {
                if (counts[c] == 0)
                    continue;

                for (var channel = 0; channel < 3; channel++)
                {
                    centers[(c * 3) + channel] = sums[(c * 3) + channel] / counts[c];
                }
            }
        }
    }

    /// <summary>The T mode with its best distance (Table 144): paint 0 is color 1, paints 1 to 3 color 2 plus d, 0, -d.</summary>
    private static long EvaluateT(ReadOnlySpan<int> texels, int color1, int color2, int transparent, bool restricted, out int distance, Span<byte> indices)
    {
        Span<int> paints = stackalloc int[12];
        var (r1, g1, b1) = Unpack(color1, 4);
        var (r2, g2, b2) = Unpack(color2, 4);
        var bestError = long.MaxValue;
        distance = 0;
        for (var candidate = 0; candidate < 8; candidate++)
        {
            int d = Etc2BlockDecoder.Distances[candidate];
            SetPaint(paints, 0, Extend4(r1), Extend4(g1), Extend4(b1), 0);
            SetPaint(paints, 1, Extend4(r2), Extend4(g2), Extend4(b2), d);
            SetPaint(paints, 2, Extend4(r2), Extend4(g2), Extend4(b2), 0);
            SetPaint(paints, 3, Extend4(r2), Extend4(g2), Extend4(b2), -d);
            var error = PaintError(texels, paints, transparent, restricted, bestError, default);
            if (error < bestError)
            {
                bestError = error;
                distance = candidate;
            }
        }

        int bestDistance = Etc2BlockDecoder.Distances[distance];
        SetPaint(paints, 1, Extend4(r2), Extend4(g2), Extend4(b2), bestDistance);
        SetPaint(paints, 3, Extend4(r2), Extend4(g2), Extend4(b2), -bestDistance);
        PaintError(texels, paints, transparent, restricted, long.MaxValue, indices);
        return bestError;
    }

    /// <summary>
    /// The H mode with its best distance: paints 0 and 1 are color 1 plus and minus d, paints 2 and 3 color 2. The low
    /// bit of the distance index is whether color 1 is at least color 2, so the colors are swapped to get the other one.
    /// </summary>
    private static long EvaluateH(ReadOnlySpan<int> texels, ref int color1, ref int color2, int transparent, bool restricted, out int distance, Span<byte> indices)
    {
        Span<int> paints = stackalloc int[12];
        var bestError = long.MaxValue;
        distance = 0;
        int best1 = color1, best2 = color2;
        for (var order = 0; order < 2; order++)
        {
            var (first, second) = order == 0 ? (color1, color2) : (color2, color1);
            var (r1, g1, b1) = Unpack(first, 4);
            var (r2, g2, b2) = Unpack(second, 4);

            // The colors compare as 24-bit values: the 4-bit codes compare the same way
            var lowBit = first >= second ? 1 : 0;
            for (var candidate = lowBit; candidate < 8; candidate += 2)
            {
                int d = Etc2BlockDecoder.Distances[candidate];
                SetPaint(paints, 0, Extend4(r1), Extend4(g1), Extend4(b1), d);
                SetPaint(paints, 1, Extend4(r1), Extend4(g1), Extend4(b1), -d);
                SetPaint(paints, 2, Extend4(r2), Extend4(g2), Extend4(b2), d);
                SetPaint(paints, 3, Extend4(r2), Extend4(g2), Extend4(b2), -d);
                var error = PaintError(texels, paints, transparent, restricted, bestError, default);
                if (error < bestError)
                {
                    (bestError, distance, best1, best2) = (error, candidate, first, second);
                }
            }
        }

        if (bestError == long.MaxValue)
            return bestError;

        (color1, color2) = (best1, best2);
        var (br1, bg1, bb1) = Unpack(best1, 4);
        var (br2, bg2, bb2) = Unpack(best2, 4);
        int bestDistance = Etc2BlockDecoder.Distances[distance];
        SetPaint(paints, 0, Extend4(br1), Extend4(bg1), Extend4(bb1), bestDistance);
        SetPaint(paints, 1, Extend4(br1), Extend4(bg1), Extend4(bb1), -bestDistance);
        SetPaint(paints, 2, Extend4(br2), Extend4(bg2), Extend4(bb2), bestDistance);
        SetPaint(paints, 3, Extend4(br2), Extend4(bg2), Extend4(bb2), -bestDistance);
        PaintError(texels, paints, transparent, restricted, long.MaxValue, indices);
        return bestError;
    }

    private static void SetPaint(Span<int> paints, int index, int r, int g, int b, int distance)
    {
        paints[index * 3] = Math.Clamp(r + distance, 0, 255);
        paints[(index * 3) + 1] = Math.Clamp(g + distance, 0, 255);
        paints[(index * 3) + 2] = Math.Clamp(b + distance, 0, 255);
    }

    /// <summary>The error of the nearest paint colors; without the opaque bit, paint 2 is for the transparent texels only.</summary>
    private static long PaintError(ReadOnlySpan<int> texels, ReadOnlySpan<int> paints, int transparent, bool restricted, long limit, Span<byte> indices)
    {
        long error = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
            {
                if (!indices.IsEmpty)
                {
                    indices[i] = 2;
                }

                continue;
            }

            var best = long.MaxValue;
            var bestIndex = 0;
            for (var index = 0; index < 4; index++)
            {
                if (restricted && index == 2)
                    continue;

                var candidate = ColorError(texels.Slice(i * 3, 3), paints[index * 3], paints[(index * 3) + 1], paints[(index * 3) + 2]);
                if (candidate < best)
                {
                    best = candidate;
                    bestIndex = index;
                }
            }

            error += best;
            if (!indices.IsEmpty)
            {
                indices[i] = (byte)bestIndex;
            }
            else if (error >= limit)
            {
                return error;
            }
        }

        return error;
    }

    /// <summary>Table 141 part e: the T mode, with the free bits set so that the red sum overflows.</summary>
    private static ulong PackT(int color1, int color2, int distance, uint differentialBit, ReadOnlySpan<byte> indices)
    {
        var (r1, g1, b1) = Unpack(color1, 4);
        var (r2, g2, b2) = Unpack(color2, 4);
        var high = ((uint)(r1 >> 2) << 27) | ((uint)(r1 & 3) << 24) | ((uint)g1 << 20) | ((uint)b1 << 16)
            | ((uint)r2 << 12) | ((uint)g2 << 8) | ((uint)b2 << 4) | ((uint)(distance >> 1) << 2) | (differentialBit << 1) | (uint)(distance & 1);
        var (k, sign) = ForceOverflow((high >> 27) & 3, (high >> 24) & 3);
        high |= (k << 29) | (sign << 26);
        return ((ulong)high << 32) | PackIndices(indices);
    }

    /// <summary>
    /// Table 141 part f: the H mode, with the free bits set so that the red sum stays in range and the green sum overflows.
    /// The low bit of the distance index is not stored: the order of the colors gives it.
    /// </summary>
    private static ulong PackH(int color1, int color2, int distance, uint differentialBit, ReadOnlySpan<byte> indices)
    {
        var (r1, g1, b1) = Unpack(color1, 4);
        var (r2, g2, b2) = Unpack(color2, 4);
        var high = ((uint)r1 << 27) | ((uint)(g1 >> 1) << 24) | ((uint)(g1 & 1) << 20) | ((uint)(b1 >> 3) << 19) | ((uint)(b1 & 7) << 15)
            | ((uint)r2 << 11) | ((uint)g2 << 7) | ((uint)b2 << 3) | ((uint)(distance >> 2) << 2) | (differentialBit << 1) | (uint)((distance >> 1) & 1);
        high |= ((high >> 26) & 1) << 31;
        var (k, sign) = ForceOverflow((high >> 19) & 3, (high >> 16) & 3);
        high |= (k << 21) | (sign << 18);
        return ((ulong)high << 32) | PackIndices(indices);
    }

    /// <summary>Packs the pixel indices of the texels (row-major) column by column: the low bits, then the high bits.</summary>
    private static uint PackIndices(ReadOnlySpan<byte> indices)
    {
        uint bits = 0;
        for (var i = 0; i < 16; i++)
        {
            var position = ((i & 3) * 4) + (i >> 2);
            bits |= ((uint)indices[i] & 1) << position;
            bits |= ((uint)indices[i] >> 1) << (16 + position);
        }

        return bits;
    }

    private static bool FitsDelta(int first, int second)
    {
        var (r1, g1, b1) = Unpack(first, 5);
        var (r2, g2, b2) = Unpack(second, 5);
        return r2 - r1 is >= -4 and <= 3 && g2 - g1 is >= -4 and <= 3 && b2 - b1 is >= -4 and <= 3;
    }

    /// <summary>Moves a 5-bit color within the delta range of another one (Table 141 part c).</summary>
    private static int Project(int color, int reference, bool referenceIsFirst)
    {
        var (r, g, b) = Unpack(color, 5);
        var (rr, rg, rb) = Unpack(reference, 5);
        return referenceIsFirst
            ? Pack(rr + Math.Clamp(r - rr, -4, 3), rg + Math.Clamp(g - rg, -4, 3), rb + Math.Clamp(b - rb, -4, 3), 5)
            : Pack(rr - Math.Clamp(rr - r, -4, 3), rg - Math.Clamp(rg - g, -4, 3), rb - Math.Clamp(rb - b, -4, 3), 5);
    }

    /// <summary>Lists the codes of a color and, with a radius, its neighbors in each channel; returns their number.</summary>
    private static int Neighbors(ReadOnlySpan<double> color, int bits, int radius, Span<int> codes)
    {
        var (r, g, b) = Unpack(NearestCode(color, bits), bits);
        var max = (1 << bits) - 1;
        var count = 0;
        for (var dr = -radius; dr <= radius; dr++)
        {
            for (var dg = -radius; dg <= radius; dg++)
            {
                for (var db = -radius; db <= radius; db++)
                {
                    if (r + dr >= 0 && r + dr <= max && g + dg >= 0 && g + dg <= max && b + db >= 0 && b + db <= max)
                    {
                        codes[count++] = Pack(r + dr, g + dg, b + db, bits);
                    }
                }
            }
        }

        return count;
    }

    private static int NearestCode(ReadOnlySpan<double> color, int bits) => Pack(NearestCode(color[0], bits), NearestCode(color[1], bits), NearestCode(color[2], bits), bits);

    /// <summary>The code of a number of bits whose extension to 8 bits is the nearest to a value.</summary>
    private static int NearestCode(double value, int bits)
    {
        var max = (1 << bits) - 1;
        var estimate = Math.Clamp((int)Math.Round(value * max / 255, MidpointRounding.ToEven), 0, max);
        var best = estimate;
        for (var candidate = Math.Max(estimate - 1, 0); candidate <= Math.Min(estimate + 1, max); candidate++)
        {
            if (Math.Abs(Extend(candidate, bits) - value) < Math.Abs(Extend(best, bits) - value))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static int Pack(int r, int g, int b, int bits) => (r << (2 * bits)) | (g << bits) | b;

    private static (int R, int G, int B) Unpack(int color, int bits)
    {
        var mask = (1 << bits) - 1;
        return (color >> (2 * bits), (color >> bits) & mask, color & mask);
    }

    private static int Extend(int value, int bits) => bits switch
    {
        4 => Extend4(value),
        5 => Extend5(value),
        6 => (value << 2) | (value >> 4),
        _ => (value << 1) | (value >> 6),
    };

    private static int Extend4(int value) => (value << 4) | value;

    private static int Extend5(int value) => (value << 3) | (value >> 2);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Candidate(long Error, ulong Bits);
}
