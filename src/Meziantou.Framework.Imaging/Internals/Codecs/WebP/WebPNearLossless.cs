namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The near-lossless preprocessing of lossless WebP output: the color of the pixels that differ from a 4-connected
/// neighbor is rounded to a multiple of a power of two, which makes the residuals of the lossless coder repeat; smooth
/// pixels (all neighbors within the step) keep their value, so gradients do not band.
/// </summary>
/// <remarks>
/// The passes go from the coarsest step (<c>2^bits</c>, see <see cref="GetBits"/>) to 2, each deciding smoothness on the
/// image left by the previous one. A value rounded once is a multiple of every smaller step (or 255), so later passes never
/// move it again: every channel stays within <c>2^(bits - 1)</c> of the source. Alpha, and the pixels of the outer border,
/// are never changed.
/// </remarks>
internal static class WebPNearLossless
{
    /// <summary>Gets the rounding bits of a near-lossless level: 0 (exact) for 100, then 1 to 5 from 99 down to 0.</summary>
    public static int GetBits(int level) => level >= 100 ? 0 : 5 - (level / 20);

    /// <summary>Gets the largest difference between a source channel and the encoded one at a near-lossless level.</summary>
    public static int GetMaxError(int level)
    {
        var bits = GetBits(level);
        return bits == 0 ? 0 : 1 << (bits - 1);
    }

    /// <summary>Applies the preprocessing to RGBA pixels in place.</summary>
    public static void Apply(AllocationScope scope, Span<byte> rgba, int width, int height, int level, CancellationToken cancellationToken)
    {
        var bits = GetBits(level);
        if (bits == 0 || width < 3 || height < 3)
            return;

        var rowLength = width * 4;
        using var rowBuffer = scope.Rent(2 * rowLength, AllocationKind.Temporary, clear: false);
        var previous = rowBuffer.RawBuffer.AsSpan(0, rowLength);
        var current = rowBuffer.RawBuffer.AsSpan(rowLength, rowLength);
        for (var passBits = bits; passBits > 0; passBits--)
        {
            var limit = 1 << passBits;
            rgba[..rowLength].CopyTo(previous);
            for (var y = 1; y < height - 1; y++)
            {
                if ((y & 63) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // The rows above and at y as they were before this pass; the row below is not changed yet
                var row = rgba.Slice(y * rowLength, rowLength);
                row.CopyTo(current);
                var below = rgba.Slice((y + 1) * rowLength, rowLength);
                for (var x = 1; x < width - 1; x++)
                {
                    var i = x * 4;
                    if (IsNear(current, i, current, i - 4, limit) && IsNear(current, i, current, i + 4, limit) && IsNear(current, i, previous, i, limit) && IsNear(current, i, below, i, limit))
                        continue;

                    row[i] = Round(current[i], passBits);
                    row[i + 1] = Round(current[i + 1], passBits);
                    row[i + 2] = Round(current[i + 2], passBits);
                }

                var swap = previous;
                previous = current;
                current = swap;
            }
        }
    }

    /// <summary>Whether every color channel of two pixels differs by less than <paramref name="limit"/>.</summary>
    private static bool IsNear(ReadOnlySpan<byte> a, int i, ReadOnlySpan<byte> b, int j, int limit)
        => Math.Abs(a[i] - b[j]) < limit && Math.Abs(a[i + 1] - b[j + 1]) < limit && Math.Abs(a[i + 2] - b[j + 2]) < limit;

    /// <summary>Rounds to the nearest multiple of <c>2^bits</c> (ties to the even multiple), 255 for values above.</summary>
    internal static byte Round(byte value, int bits)
    {
        var step = 1 << bits;
        var low = value & ~(step - 1);
        var remainder = value - low;
        var rounded = remainder > step / 2 || (remainder == step / 2 && ((low >> bits) & 1) != 0) ? low + step : low;
        return (byte)Math.Min(rounded, 255);
    }
}
