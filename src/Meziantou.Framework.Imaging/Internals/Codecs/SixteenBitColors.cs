using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands 16-bit little-endian pixels of 5-bit (or 6-bit green) channels to 8-bit RGB or RGBA pixels, for the TGA and BMP
/// decoders: X1R5G5B5 or A1R5G5B5 (red in bits 10 to 14, the alpha bit 15), and R5G6B5 (red in bits 11 to 15). A channel
/// of <c>n</c> bits is expanded to <c>round(value * 255 / (2^n - 1))</c>, which is <c>(value * 527 + 23) &gt;&gt; 6</c> for 5
/// bits and <c>(value * 259 + 33) &gt;&gt; 6</c> for 6 bits. With 128-bit hardware vectors, 8 pixels are expanded at a time;
/// the result equals <see cref="ExpandScalar"/>.
/// </summary>
internal static class SixteenBitColors
{
    /// <summary>Expands a row of pixels.</summary>
    /// <param name="source">At least 2 bytes per pixel.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="sixBitGreen">Whether the pixels are R5G6B5 (otherwise X1R5G5B5 or A1R5G5B5).</param>
    /// <param name="alpha">Whether bit 15 is an alpha bit, written as 0 or 255 to RGBA pixels (otherwise the pixels are RGB).</param>
    /// <param name="destination">Exactly <paramref name="width"/> pixels of 3 or 4 bytes; nothing after them is written.</param>
    public static void Expand(ReadOnlySpan<byte> source, int width, bool sixBitGreen, bool alpha, Span<byte> destination)
    {
        var bytesPerPixel = alpha ? 4 : 3;
        _ = source[..(width * 2)];
        _ = destination[..(width * bytesPerPixel)];
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            ref var input = ref unsafe(MemoryMarshal.GetReference(source));
            ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
            var five = Vector128.Create((ushort)31);
            var greenMask = Vector128.Create(sixBitGreen ? (ushort)63 : (ushort)31);
            var greenFactor = Vector128.Create(sixBitGreen ? (ushort)259 : (ushort)527);
            var greenBias = Vector128.Create(sixBitGreen ? (ushort)33 : (ushort)23);
            var factor = Vector128.Create((ushort)527);
            var bias = Vector128.Create((ushort)23);
            var opaque = Vector128.Create((ushort)0xFF00);
            var redShift = sixBitGreen ? 11 : 10;

            // The 3 bytes of each 4-byte pixel
            var compact = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 15, 15, 15);
            for (; x <= width - Vector128<ushort>.Count && (x * bytesPerPixel) + 32 <= destination.Length; x += Vector128<ushort>.Count)
            {
                var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(x * 2))).AsUInt16();
                var red = ((((pixels >>> redShift) & five) * factor) + bias) >>> 6;
                var green = ((((pixels >>> 5) & greenMask) * greenFactor) + greenBias) >>> 6;
                var blue = (((pixels & five) * factor) + bias) >>> 6;
                var alphas = alpha ? (pixels >>> 15) * opaque : opaque;
                var (redGreenLow, redGreenHigh) = Vector128.Widen(red | (green << 8));
                var (blueAlphaLow, blueAlphaHigh) = Vector128.Widen(blue | alphas);
                var low = (redGreenLow | (blueAlphaLow << 16)).AsByte();
                var high = (redGreenHigh | (blueAlphaHigh << 16)).AsByte();
                var offset = (nuint)(x * bytesPerPixel);
                if (alpha)
                {
                    unsafe { low.StoreUnsafe(ref output, offset); }
                    unsafe { high.StoreUnsafe(ref output, offset + 16); }
                }
                else
                {
                    // 16 bytes stored for 12: the next store overwrites the last 4
                    unsafe { Vector128.Shuffle(low, compact).StoreUnsafe(ref output, offset); }
                    unsafe { Vector128.Shuffle(high, compact).StoreUnsafe(ref output, offset + 12); }
                }
            }
        }

        ExpandScalar(source[(x * 2)..], width - x, sixBitGreen, alpha, destination[(x * bytesPerPixel)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="Expand"/>.</summary>
    internal static void ExpandScalar(ReadOnlySpan<byte> source, int width, bool sixBitGreen, bool alpha, Span<byte> destination)
    {
        var bytesPerPixel = alpha ? 4 : 3;
        for (var x = 0; x < width; x++)
        {
            int value = BinaryPrimitives.ReadUInt16LittleEndian(source[(x * 2)..]);
            var target = destination.Slice(x * bytesPerPixel, bytesPerPixel);
            if (sixBitGreen)
            {
                target[0] = (byte)(((((value >> 11) & 31) * 255) + 15) / 31);
                target[1] = (byte)(((((value >> 5) & 63) * 255) + 31) / 63);
            }
            else
            {
                target[0] = (byte)(((((value >> 10) & 31) * 255) + 15) / 31);
                target[1] = (byte)(((((value >> 5) & 31) * 255) + 15) / 31);
            }

            target[2] = (byte)((((value & 31) * 255) + 15) / 31);
            if (alpha)
            {
                target[3] = (value & 0x8000) != 0 ? byte.MaxValue : (byte)0;
            }
        }
    }
}
