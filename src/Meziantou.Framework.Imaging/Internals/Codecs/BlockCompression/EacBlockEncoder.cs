using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the EAC blocks of 4×4 values (Khronos Data Format Specification 1.4, sections 22.3, 22.5 and 22.7): the 8-bit
/// alpha of RGBA ETC2, and the unsigned and signed 11-bit channels of R11 and RG11. The inverse of the EAC part of
/// <see cref="Etc2BlockDecoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// A block is a base codeword, a multiplier, one of 16 modifier tables (Table 146) and a 3-bit index per value: a value is
/// the base plus the modifier of its index times the multiplier, clamped. The 11-bit formats scale the base and the
/// multiplier by 8, add 4 to the unsigned base, and read a multiplier of 0 as 1/8 (equations 6 and 9), which gives a
/// flat block a precision of one 11-bit step. The 8-bit alpha block does not use the multiplier 0 (it would make every
/// value the base; the encoder reaches any flat value with the table whose modifier 0 exists instead).
/// </para>
/// <para>
/// The search, in units of the base codeword: for each table and each multiplier near the one that makes the range of the
/// table cover the range of the block, the bases around the one that centers the table on the block, then the nearest
/// value of each index. The thorough level tries more multipliers and bases. Every candidate is scored by the exact error of
/// what the decoder computes.
/// </para>
/// </remarks>
internal static class EacBlockEncoder
{
    private enum Kind
    {
        Alpha,
        Unsigned,
        Signed,
    }

    /// <summary>Encodes the 8-bit alpha block of RGBA ETC2 (section 22.3) from the alpha of 16 RGBA texels, row by row.</summary>
    public static void EncodeAlpha(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
    {
        Span<int> target = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            target[i] = rgba[(i * 4) + 3];
        }

        BinaryPrimitives.WriteUInt64BigEndian(block, Encode(target, Kind.Alpha, thorough));
    }

    /// <summary>
    /// Encodes an unsigned R11 block (section 22.5) from 16-bit samples, row by row: the decoder extends its 11-bit values
    /// to 16 bits by bit replication, so the error is measured on those.
    /// </summary>
    public static void EncodeUnsigned(ReadOnlySpan<ushort> samples, int stride, Span<byte> block, bool thorough)
    {
        Span<int> target = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            target[i] = samples[i * stride];
        }

        BinaryPrimitives.WriteUInt64BigEndian(block, Encode(target, Kind.Unsigned, thorough));
    }

    /// <summary>
    /// Encodes a signed R11 block (section 22.7) from values in [-1, 1], row by row (others are clamped, NaN is 0): the
    /// decoder gives an 11-bit value divided by 1023.
    /// </summary>
    public static void EncodeSigned(ReadOnlySpan<float> samples, int stride, Span<byte> block, bool thorough)
    {
        Span<int> target = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            var value = samples[i * stride];
            value = float.IsNaN(value) ? 0 : Math.Clamp(value, -1f, 1f);

            // In 1/256 of the 11-bit unit, so that the error of a value between two steps still counts
            target[i] = (int)Math.Round(value * 1023.0 * 256, MidpointRounding.ToEven);
        }

        BinaryPrimitives.WriteUInt64BigEndian(block, Encode(target, Kind.Signed, thorough));
    }

    private static ulong Encode(ReadOnlySpan<int> target, Kind kind, bool thorough)
    {
        // The targets in units of the base codeword: the 11-bit formats multiply the base by 8
        double low = double.MaxValue, high = double.MinValue;
        foreach (var value in target)
        {
            var scaled = ToBaseUnits(value, kind);
            low = Math.Min(low, scaled);
            high = Math.Max(high, scaled);
        }

        var (minBase, maxBase) = kind == Kind.Signed ? (-127, 127) : (0, 255);
        var offset = kind == Kind.Unsigned ? 0.5 : 0;
        var baseRadius = thorough ? 2 : 1;
        var multiplierRadius = thorough ? 2 : 0;
        Span<int> palette = stackalloc int[8];
        var bestError = long.MaxValue;
        int bestBase = 0, bestMultiplier = 1, bestTable = 0;
        var modifiers = Etc2BlockDecoder.AlphaModifiers;
        for (var table = 0; table < 16 && bestError > 0; table++)
        {
            var row = modifiers.Slice(table * 8, 8);
            int modifierMin = row[3], modifierMax = row[7];
            var ideal = (high - low) / (modifierMax - modifierMin);
            var firstMultiplier = Math.Clamp((int)Math.Floor(ideal) - multiplierRadius, 1, 15);
            var lastMultiplier = Math.Min((int)Math.Ceiling(ideal) + multiplierRadius, 15);

            // The multiplier 0 of the 11-bit formats is 1/8: only for blocks whose range it can cover
            if (kind != Kind.Alpha && ideal < 1)
            {
                firstMultiplier = 0;
            }

            lastMultiplier = Math.Max(lastMultiplier, firstMultiplier == 0 ? 1 : firstMultiplier);
            for (var multiplier = firstMultiplier; multiplier <= lastMultiplier; multiplier++)
            {
                var step = multiplier == 0 ? 0.125 : multiplier;
                var center = (int)Math.Round(((low + high) / 2) - offset - ((modifierMin + modifierMax) / 2.0 * step), MidpointRounding.ToEven);
                for (var baseCodeword = Math.Max(center - baseRadius, minBase); baseCodeword <= Math.Min(center + baseRadius, maxBase); baseCodeword++)
                {
                    for (var index = 0; index < 8; index++)
                    {
                        palette[index] = Decode(baseCodeword, multiplier, row[index], kind);
                    }

                    var error = Error(target, palette, bestError, indices: default);
                    if (error < bestError)
                    {
                        (bestError, bestBase, bestMultiplier, bestTable) = (error, baseCodeword, multiplier, table);
                    }
                }
            }
        }

        var bestRow = modifiers.Slice(bestTable * 8, 8);
        for (var index = 0; index < 8; index++)
        {
            palette[index] = Decode(bestBase, bestMultiplier, bestRow[index], kind);
        }

        Span<byte> indices = stackalloc byte[16];
        Error(target, palette, long.MaxValue, indices);
        var bits = ((ulong)(byte)bestBase << 56) | ((ulong)bestMultiplier << 52) | ((ulong)bestTable << 48);
        for (var i = 0; i < 16; i++)
        {
            // The indices are in the order a..p: column by column
            var (x, y) = (i & 3, i >> 2);
            bits |= (ulong)indices[i] << (45 - (3 * ((x * 4) + y)));
        }

        return bits;
    }

    private static double ToBaseUnits(int value, Kind kind) => kind switch
    {
        Kind.Alpha => value,
        Kind.Unsigned => value * 2047.0 / 65535 / 8,
        _ => value / 2048.0,
    };

    /// <summary>The value of a modifier, as the decoder computes it, in the unit of the targets.</summary>
    private static int Decode(int baseCodeword, int multiplier, int modifier, Kind kind)
    {
        if (kind == Kind.Alpha)
            return Math.Clamp(baseCodeword + (modifier * multiplier), 0, 255);

        var term = multiplier == 0 ? modifier : modifier * multiplier * 8;
        if (kind == Kind.Signed)
            return Math.Clamp((baseCodeword * 8) + term, -1023, 1023) * 256;

        // The 11-bit value extended to 16 bits by bit replication, as the decoder writes it
        var value = Math.Clamp((baseCodeword * 8) + 4 + term, 0, 2047);
        return (value << 5) | (value >> 6);
    }

    /// <summary>The squared error of the nearest values of a palette; stops at <paramref name="limit"/>.</summary>
    private static long Error(ReadOnlySpan<int> target, ReadOnlySpan<int> palette, long limit, Span<byte> indices)
    {
        long error = 0;
        for (var i = 0; i < 16; i++)
        {
            var best = long.MaxValue;
            var bestIndex = 0;
            for (var index = 0; index < 8; index++)
            {
                long difference = palette[index] - target[i];
                if (difference * difference < best)
                {
                    best = difference * difference;
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
}
