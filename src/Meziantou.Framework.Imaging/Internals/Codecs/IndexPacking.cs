using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Packs rows of one byte per pixel to 1 or 4 bits per pixel, the first pixel in the most significant bits, for the BMP,
/// TIFF and Netpbm encoders. With 128-bit hardware vectors, 16 bytes are packed at a time; each method writes the bytes of
/// its <c>...Scalar</c> reference.
/// </summary>
internal static class IndexPacking
{
    /// <summary>Packs one bit per pixel: set for a byte that is zero (<paramref name="setWhenZero"/>) or not zero.</summary>
    /// <param name="values">One byte per pixel.</param>
    /// <param name="setWhenZero">Whether a zero byte, or a byte that is not zero, gives a set bit.</param>
    /// <param name="destination">
    /// Receives exactly <c>(values.Length + 7) / 8</c> bytes, the bits past the last pixel clear.
    /// </param>
    public static void PackBits(ReadOnlySpan<byte> values, bool setWhenZero, Span<byte> destination)
    {
        _ = destination[..((values.Length + 7) / 8)];
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            // The bytes of each group of 8 are reversed, so that the most significant bit of each byte of the mask is the
            // first pixel of its group
            ref var source = ref unsafe(MemoryMarshal.GetReference(values));
            var reverse = Vector128.Create((byte)7, 6, 5, 4, 3, 2, 1, 0, 15, 14, 13, 12, 11, 10, 9, 8);
            var flip = setWhenZero ? Vector128<byte>.Zero : Vector128<byte>.AllBitsSet;
            for (; x <= values.Length - Vector128<byte>.Count; x += Vector128<byte>.Count)
            {
                var set = Vector128.Equals(unsafe(Vector128.LoadUnsafe(ref source, (nuint)x)), Vector128<byte>.Zero) ^ flip;
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(x / 8)..], (ushort)Vector128.Shuffle(set, reverse).ExtractMostSignificantBits());
            }
        }

        PackBitsScalar(values[x..], setWhenZero, destination[(x / 8)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="PackBits"/>.</summary>
    internal static void PackBitsScalar(ReadOnlySpan<byte> values, bool setWhenZero, Span<byte> destination)
    {
        var length = (values.Length + 7) / 8;
        destination[..length].Clear();
        for (var x = 0; x < values.Length; x++)
        {
            if ((values[x] == 0) == setWhenZero)
            {
                destination[x >> 3] |= (byte)(0x80 >> (x & 7));
            }
        }
    }

    /// <summary>
    /// Packs two pixels per byte, the first in the high-order half: the byte of pixels <c>a</c> and <c>b</c> is
    /// <c>((a &amp; 15) &lt;&lt; 4) | b</c>, and the byte of a last pixel <c>a</c> alone is <c>(a &amp; 15) &lt;&lt; 4</c>.
    /// </summary>
    /// <param name="indexes">One index per pixel, below 16.</param>
    /// <param name="destination">Receives exactly <c>(indexes.Length + 1) / 2</c> bytes.</param>
    public static void PackNibbles(ReadOnlySpan<byte> indexes, Span<byte> destination)
    {
        _ = destination[..((indexes.Length + 1) / 2)];
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            // A 16-bit lane holds a pair of pixels, the first one in its low byte
            ref var source = ref unsafe(MemoryMarshal.GetReference(indexes));
            ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
            var high = Vector128.Create((ushort)0xF0);
            for (; x <= indexes.Length - (2 * Vector128<byte>.Count); x += 2 * Vector128<byte>.Count)
            {
                var first = unsafe(Vector128.LoadUnsafe(ref source, (nuint)x)).AsUInt16();
                var second = unsafe(Vector128.LoadUnsafe(ref source, (nuint)x + 16)).AsUInt16();
                var packed = Vector128.Narrow(((first << 4) & high) | (first >> 8), ((second << 4) & high) | (second >> 8));
                unsafe { packed.StoreUnsafe(ref target, (nuint)(x / 2)); }
            }
        }

        PackNibblesScalar(indexes[x..], destination[(x / 2)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="PackNibbles"/>.</summary>
    internal static void PackNibblesScalar(ReadOnlySpan<byte> indexes, Span<byte> destination)
    {
        var length = (indexes.Length + 1) / 2;
        destination[..length].Clear();
        for (var x = 0; x < indexes.Length; x++)
        {
            destination[x >> 1] |= (byte)((x & 1) == 0 ? indexes[x] << 4 : indexes[x]);
        }
    }
}
