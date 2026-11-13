using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands rows of palette indices of 1, 2, 4 or 8 bits (packed, the first pixel in the most significant bits) to their
/// colors, for the PNG, BMP and TIFF decoders: the indices are validated first (<see cref="FindIndexOutside"/>), then
/// written from a table of 4-byte colors (<see cref="Expand"/>).
/// </summary>
internal static class IndexedPixels
{
    /// <summary>Reads the index of pixel <paramref name="x"/> of a packed row.</summary>
    public static int ReadIndex(ReadOnlySpan<byte> packed, int x, int bitsPerIndex)
    {
        if (bitsPerIndex == 8)
            return packed[x];

        var bit = x * bitsPerIndex;
        return (packed[bit >> 3] >> (8 - bitsPerIndex - (bit & 7))) & ((1 << bitsPerIndex) - 1);
    }

    /// <summary>Finds the first pixel whose index is <paramref name="entries"/> or more.</summary>
    /// <returns>The column of that pixel, or -1 when every index has a color.</returns>
    public static int FindIndexOutside(ReadOnlySpan<byte> packed, int bitsPerIndex, int width, int entries)
    {
        if (entries >= 1 << bitsPerIndex)
            return -1;

        if (bitsPerIndex == 8)
            return FindAtLeast(packed[..width], entries);

        for (var x = 0; x < width; x++)
        {
            if (ReadIndex(packed, x, bitsPerIndex) >= entries)
                return x;
        }

        return -1;
    }

    /// <summary>
    /// Finds the first byte that is <paramref name="limit"/> or more (0 to 255). With 128-bit hardware vectors, 16 bytes are
    /// compared at a time; the result equals <see cref="FindAtLeastScalar"/>.
    /// </summary>
    /// <returns>Its position, or -1.</returns>
    internal static int FindAtLeast(ReadOnlySpan<byte> values, int limit)
    {
        var start = 0;
        if (Vector128.IsHardwareAccelerated && values.Length >= Vector128<byte>.Count && limit is > 0 and <= byte.MaxValue)
        {
            var maximum = Vector128.Create((byte)(limit - 1));
            for (; start <= values.Length - Vector128<byte>.Count; start += Vector128<byte>.Count)
            {
                var outside = Vector128.GreaterThan(Vector128.Create(values[start..]), maximum);
                if (outside != Vector128<byte>.Zero)
                    return start + BitOperations.TrailingZeroCount(outside.ExtractMostSignificantBits());
            }
        }

        var rest = FindAtLeastScalar(values[start..], limit);
        return rest < 0 ? -1 : start + rest;
    }

    /// <summary>The byte-at-a-time reference of <see cref="FindAtLeast"/>.</summary>
    internal static int FindAtLeastScalar(ReadOnlySpan<byte> values, int limit)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] >= limit)
                return i;
        }

        return -1;
    }

    /// <summary>Writes the colors of the pixels of a packed row of indices.</summary>
    /// <param name="packed">The indices; every one must have a color (see <see cref="FindIndexOutside"/>).</param>
    /// <param name="bitsPerIndex">1, 2, 4 or 8.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="palette">The colors, packed <see cref="PixelFormat.Rgba32"/> pixels in native byte order, at least <c>2^bitsPerIndex</c> of them.</param>
    /// <param name="destination"><paramref name="width"/> pixels of <see cref="PixelFormat.Rgba32"/>, or of <see cref="PixelFormat.Rgb24"/> (the fourth byte of the colors dropped).</param>
    /// <param name="rgba">Whether the destination is <see cref="PixelFormat.Rgba32"/>.</param>
    public static void Expand(ReadOnlySpan<byte> packed, int bitsPerIndex, int width, ReadOnlySpan<uint> palette, Span<byte> destination, bool rgba)
    {
        if (bitsPerIndex is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(bitsPerIndex), bitsPerIndex, "Indices have 1, 2, 4 or 8 bits.");

        if (width <= 0)
            return;

        var bytesPerPixel = rgba ? 4 : 3;
        ArgumentOutOfRangeException.ThrowIfLessThan(palette.Length, 1 << bitsPerIndex, nameof(palette));

        // In 64 bits: the products of a large width would wrap around in 32 bits and pass the checks
        ArgumentOutOfRangeException.ThrowIfLessThan(packed.Length, (((long)width * bitsPerIndex) + 7) / 8, nameof(packed));
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (long)width * bytesPerPixel, nameof(destination));

        // Every index is below 2^bitsPerIndex and every color is written with one 4-byte store: the colors of a 3-byte
        // destination overlap by one byte, which the next color overwrites, and the last one is written byte by byte
        ref var colors = ref unsafe(MemoryMarshal.GetReference(palette));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var stored = rgba ? width : width - 1;
        if (bitsPerIndex == 8)
        {
            ref var indices = ref unsafe(MemoryMarshal.GetReference(packed));
            for (var x = 0; x < stored; x++)
            {
                unsafe
                {
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, x * bytesPerPixel), Unsafe.Add(ref colors, Unsafe.Add(ref indices, x)));
                }
            }
        }
        else
        {
            var mask = (1 << bitsPerIndex) - 1;
            var x = bitsPerIndex == 4 ? ExpandNibbles(packed, stored, palette, destination[..(width * bytesPerPixel)], rgba) : 0;
            for (var i = x * bitsPerIndex / 8; x < stored; i++)
            {
                int value = packed[i];
                for (var shift = 8 - bitsPerIndex; shift >= 0 && x < stored; shift -= bitsPerIndex, x++)
                {
                    unsafe
                    {
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, x * bytesPerPixel), Unsafe.Add(ref colors, (value >> shift) & mask));
                    }
                }
            }
        }

        if (!rgba)
        {
            var last = palette[ReadIndex(packed, width - 1, bitsPerIndex)];
            Span<byte> color = stackalloc byte[sizeof(uint)];
            unsafe
            {
                MemoryMarshal.Write(color, in last);
            }

            color[..3].CopyTo(destination[((width - 1) * 3)..]);
        }
    }

    /// <summary>
    /// Writes the colors of the leading groups of 32 pixels of 4-bit indices with 128-bit hardware vectors: the 16 colors
    /// are 4 tables of 16 bytes (one per channel) that each index selects from, and the channels are interleaved by
    /// widening. A 3-byte destination is written 16 bytes at a time for 12, so a group is written only when the 4 bytes after
    /// it are in the row.
    /// </summary>
    /// <returns>The number of pixels written, a multiple of 32.</returns>
    /// <param name="packed">The indices.</param>
    /// <param name="count">The number of pixels to write at most.</param>
    /// <param name="palette">The colors.</param>
    /// <param name="destination">The pixels of the row, and nothing after them.</param>
    /// <param name="rgba">Whether the pixels have 4 bytes (otherwise 3).</param>
    private static int ExpandNibbles(ReadOnlySpan<byte> packed, int count, ReadOnlySpan<uint> palette, Span<byte> destination, bool rgba)
    {
        if (!Vector128.IsHardwareAccelerated || !BitConverter.IsLittleEndian || count < 32)
            return 0;

        Span<byte> channels = stackalloc byte[64];
        for (var i = 0; i < 16; i++)
        {
            var color = palette[i];
            channels[i] = (byte)color;
            channels[16 + i] = (byte)(color >> 8);
            channels[32 + i] = (byte)(color >> 16);
            channels[48 + i] = (byte)(color >> 24);
        }

        var red = Vector128.Create(channels[..16]);
        var green = Vector128.Create(channels[16..32]);
        var blue = Vector128.Create(channels[32..48]);
        var alpha = Vector128.Create(channels[48..]);
        var bytesPerPixel = rgba ? 4 : 3;
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var x = 0;
        for (; x + 32 <= count && ((x + 32) * bytesPerPixel) + 4 <= destination.Length; x += 32)
        {
            // Each byte holds two pixels, the first one in its high-order half
            var (first, second) = Vector128.Widen(Vector128.Create(packed[(x / 2)..]));
            StoreColors(((first >>> 4) | ((first & Vector128.Create((ushort)15)) << 8)).AsByte(), red, green, blue, alpha, ref output, (nuint)(x * bytesPerPixel), rgba);
            StoreColors(((second >>> 4) | ((second & Vector128.Create((ushort)15)) << 8)).AsByte(), red, green, blue, alpha, ref output, (nuint)((x + 16) * bytesPerPixel), rgba);
        }

        return x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StoreColors(Vector128<byte> indices, Vector128<byte> red, Vector128<byte> green, Vector128<byte> blue, Vector128<byte> alpha, ref byte output, nuint offset, bool rgba)
        {
            var (redLow, redHigh) = Vector128.Widen(Vector128.Shuffle(red, indices));
            var (greenLow, greenHigh) = Vector128.Widen(Vector128.Shuffle(green, indices));
            var (blueLow, blueHigh) = Vector128.Widen(Vector128.Shuffle(blue, indices));
            var (alphaLow, alphaHigh) = Vector128.Widen(Vector128.Shuffle(alpha, indices));
            var (pixels0, pixels1) = Pack(redLow | (greenLow << 8), blueLow | (alphaLow << 8));
            var (pixels2, pixels3) = Pack(redHigh | (greenHigh << 8), blueHigh | (alphaHigh << 8));
            if (rgba)
            {
                unsafe
                {
                    pixels0.StoreUnsafe(ref output, offset);
                    pixels1.StoreUnsafe(ref output, offset + 16);
                    pixels2.StoreUnsafe(ref output, offset + 32);
                    pixels3.StoreUnsafe(ref output, offset + 48);
                }

                return;
            }

            // The 3 bytes of each 4-byte pixel; each store writes 4 bytes that the next one overwrites
            var compact = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 15, 15, 15);
            unsafe
            {
                Vector128.Shuffle(pixels0, compact).StoreUnsafe(ref output, offset);
                Vector128.Shuffle(pixels1, compact).StoreUnsafe(ref output, offset + 12);
                Vector128.Shuffle(pixels2, compact).StoreUnsafe(ref output, offset + 24);
                Vector128.Shuffle(pixels3, compact).StoreUnsafe(ref output, offset + 36);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (Vector128<byte> Low, Vector128<byte> High) Pack(Vector128<ushort> redGreen, Vector128<ushort> blueAlpha)
        {
            var (redGreenLow, redGreenHigh) = Vector128.Widen(redGreen);
            var (blueAlphaLow, blueAlphaHigh) = Vector128.Widen(blueAlpha);
            return ((redGreenLow | (blueAlphaLow << 16)).AsByte(), (redGreenHigh | (blueAlphaHigh << 16)).AsByte());
        }
    }

    /// <summary>The pixel-at-a-time reference of <see cref="Expand"/>.</summary>
    internal static void ExpandScalar(ReadOnlySpan<byte> packed, int bitsPerIndex, int width, ReadOnlySpan<uint> palette, Span<byte> destination, bool rgba)
    {
        var bytesPerPixel = rgba ? 4 : 3;
        Span<byte> color = stackalloc byte[sizeof(uint)];
        for (var x = 0; x < width; x++)
        {
            var value = palette[ReadIndex(packed, x, bitsPerIndex)];
            unsafe
            {
                MemoryMarshal.Write(color, in value);
            }

            color[..bytesPerPixel].CopyTo(destination[(x * bytesPerPixel)..]);
        }
    }
}
