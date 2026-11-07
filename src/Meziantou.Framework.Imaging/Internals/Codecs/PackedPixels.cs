using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Byte-order conversions of stored pixels shared by the BMP and TGA decoders.</summary>
internal static class PackedPixels
{
    /// <summary>
    /// Converts 32-bit <c>BGRX</c> pixels (blue, green, red and an unused byte) to <see cref="PixelFormat.Rgb24"/>. With
    /// 128-bit hardware vectors, four pixels are shuffled at a time; the result equals <see cref="Bgrx32ToRgb24Scalar"/>.
    /// </summary>
    /// <param name="source">The pixels, 4 bytes each.</param>
    /// <param name="destination">At least <c>source.Length / 4 * 3</c> bytes.</param>
    public static void Bgrx32ToRgb24(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var count = source.Length / 4;
        destination = destination[..(count * 3)];
        var x = 0;
        if (Vector128.IsHardwareAccelerated && count >= 6)
        {
            // Each step stores 16 bytes for 12: the last 4 are the next pixels, written again by the next step
            var shuffle = Vector128.Create((byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0, 0, 0, 0);
            for (; (x * 3) + Vector128<byte>.Count <= destination.Length && (x * 4) + Vector128<byte>.Count <= source.Length; x += 4)
            {
                Vector128.Shuffle(Vector128.Create(source[(x * 4)..]), shuffle).CopyTo(destination[(x * 3)..]);
            }
        }

        Bgrx32ToRgb24Scalar(source[(x * 4)..(count * 4)], destination[(x * 3)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="Bgrx32ToRgb24"/>.</summary>
    internal static void Bgrx32ToRgb24Scalar(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var count = source.Length / 4;
        for (var x = 0; x < count; x++)
        {
            destination[x * 3] = source[(x * 4) + 2];
            destination[(x * 3) + 1] = source[(x * 4) + 1];
            destination[(x * 3) + 2] = source[x * 4];
        }
    }
}
