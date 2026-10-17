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
        ArgumentOutOfRangeException.ThrowIfLessThan(packed.Length, ((width * bitsPerIndex) + 7) / 8, nameof(packed));
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, width * bytesPerPixel, nameof(destination));

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
            var x = 0;
            for (var i = 0; x < stored; i++)
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
