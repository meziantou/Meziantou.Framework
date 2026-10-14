using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Products of coverages (clip masks, opacities) and the coverages of the drawings without antialiasing, four at a time
/// with the scalar loops as their references.
/// </summary>
/// <remarks>
/// A coverage is at most <see cref="CoverageRasterizer.FullCoverage"/> (2^16): the product of a coverage by a factor
/// below 2^16, plus 2^15, is below 2^32 and fits the lanes of a <see cref="Vector128{T}"/> of <see cref="uint"/>. A
/// factor of 2^16 leaves the coverage as it is.
/// </remarks>
internal static class CoverageKernels
{
    private const uint Full = CoverageRasterizer.FullCoverage;

    /// <summary>Multiplies coverages by a mask: <c>round(a b / 65536)</c>, to nearest with ties upward.</summary>
    /// <returns><see langword="false"/> when every product is zero.</returns>
    public static bool Multiply(ReadOnlySpan<uint> coverage, ReadOnlySpan<uint> mask, Span<uint> product)
    {
        var i = 0;
        var any = Vector128<uint>.Zero;
        if (Vector128.IsHardwareAccelerated)
        {
            var full = Vector128.Create(Full);
            var half = Vector128.Create(Full / 2);
            for (; i + 4 <= coverage.Length; i += 4)
            {
                var value = Vector128.Create(coverage.Slice(i, 4));
                var factor = Vector128.Create(mask.Slice(i, 4));
                var result = Vector128.ConditionalSelect(Vector128.Equals(factor, full), value, Vector128.ShiftRightLogical((value * factor) + half, 16));
                result.CopyTo(product.Slice(i, 4));
                any |= result;
            }
        }

        var tail = MultiplyScalar(coverage[i..], mask[i..], product[i..]);
        return tail || any != Vector128<uint>.Zero;
    }

    /// <summary>The scalar reference of <see cref="Multiply"/>.</summary>
    public static bool MultiplyScalar(ReadOnlySpan<uint> coverage, ReadOnlySpan<uint> mask, Span<uint> product)
    {
        var any = false;
        for (var i = 0; i < coverage.Length; i++)
        {
            var value = mask[i] == Full ? coverage[i] : (uint)((((ulong)coverage[i] * mask[i]) + (Full / 2)) >> 16);
            product[i] = value;
            any |= value != 0;
        }

        return any;
    }

    /// <summary>Multiplies coverages by an opacity below <see cref="CoverageRasterizer.FullCoverage"/>: <c>round(a b / 65536)</c>. The product may be the coverages themselves.</summary>
    public static void Scale(ReadOnlySpan<uint> coverage, uint opacity, Span<uint> product)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var factor = Vector128.Create(opacity);
            var half = Vector128.Create(Full / 2);
            for (; i + 4 <= coverage.Length; i += 4)
            {
                Vector128.ShiftRightLogical((Vector128.Create(coverage.Slice(i, 4)) * factor) + half, 16).CopyTo(product.Slice(i, 4));
            }
        }

        ScaleScalar(coverage[i..], opacity, product[i..]);
    }

    /// <summary>The scalar reference of <see cref="Scale"/>.</summary>
    public static void ScaleScalar(ReadOnlySpan<uint> coverage, uint opacity, Span<uint> product)
    {
        for (var i = 0; i < coverage.Length; i++)
        {
            product[i] = (uint)((((ulong)coverage[i] * opacity) + (Full / 2)) >> 16);
        }
    }

    /// <summary>Makes coverages all or nothing: a coverage of at least half is full, the others are zero.</summary>
    /// <returns><see langword="false"/> when no coverage is full.</returns>
    public static bool Threshold(ReadOnlySpan<uint> coverage, Span<uint> result)
    {
        var i = 0;
        var any = Vector128<uint>.Zero;
        if (Vector128.IsHardwareAccelerated)
        {
            var full = Vector128.Create(Full);
            var half = Vector128.Create(Full / 2);
            for (; i + 4 <= coverage.Length; i += 4)
            {
                var value = Vector128.GreaterThanOrEqual(Vector128.Create(coverage.Slice(i, 4)), half) & full;
                value.CopyTo(result.Slice(i, 4));
                any |= value;
            }
        }

        var tail = ThresholdScalar(coverage[i..], result[i..]);
        return tail || any != Vector128<uint>.Zero;
    }

    /// <summary>The scalar reference of <see cref="Threshold"/>.</summary>
    public static bool ThresholdScalar(ReadOnlySpan<uint> coverage, Span<uint> result)
    {
        var any = false;
        for (var i = 0; i < coverage.Length; i++)
        {
            var isCovered = coverage[i] >= Full / 2;
            result[i] = isCovered ? Full : 0;
            any |= isCovered;
        }

        return any;
    }
}
