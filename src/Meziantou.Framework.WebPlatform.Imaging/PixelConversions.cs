using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>The conversions between the 16-bit pixels of the surfaces and the 8-bit pixels of the bitmaps (RGBA, straight alpha).</summary>
internal static class PixelConversions
{
    /// <summary>
    /// Converts a 16-bit component to 8 bits: rounded to the nearest value, with a bias of 1/256 of a level, so that a coverage of
    /// a half (a tie) gives the same value whether it was rounded to 16 bits once (on the canvas) or twice (on a layer, then on
    /// the canvas).
    /// </summary>
    public static byte ToByte(ushort value) => (byte)(((value * 65280L) + (65535L * 129)) / (65535L * 256));

    /// <summary>
    /// Converts an 8-bit pixel (straight alpha) to the 16-bit pixel of its premultiplied 8-bit components, as a browser holds
    /// the pixels of a canvas: each color <c>c</c> of alpha <c>a</c> becomes <c>round(c a / 255)</c> (to nearest, ties upward),
    /// divided by the alpha. A straight 8-bit color rounded from such a component gives that component back.
    /// </summary>
    public static Rgba64 FromPremultipliedByte(Rgba32 pixel)
    {
        return pixel.A switch
        {
            0 => default,
            255 => new Rgba64((ushort)(pixel.R * 257), (ushort)(pixel.G * 257), (ushort)(pixel.B * 257), ushort.MaxValue),
            _ => new Rgba64(Convert(pixel.R, pixel.A), Convert(pixel.G, pixel.A), Convert(pixel.B, pixel.A), (ushort)(pixel.A * 257)),
        };

        static ushort Convert(byte color, byte alpha)
        {
            var premultiplied = ((color * alpha) + 127) / 255;
            return (ushort)(((premultiplied * 65535L) + (alpha / 2)) / alpha);
        }
    }

    /// <summary>Converts 16-bit pixels to 8-bit RGBA bytes.</summary>
    public static void ToBytes(ReadOnlySpan<Rgba64> pixels, Span<byte> destination)
    {
        // The components are in the order of the bytes (R, G, B, A): each one becomes a byte
        var components = unsafe(MemoryMarshal.Cast<Rgba64, ushort>(pixels));
        destination = destination[..components.Length];
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // ToByte(v) is (v + 129) / 257 (the fractions that the integer division of ToByte drops do not reach the next
            // integer), and that is ((v + 129) * 65281) >> 24 for every 16-bit value, without overflowing 32 bits
            for (; i <= components.Length - (Vector128<ushort>.Count * 2); i += Vector128<ushort>.Count * 2)
            {
                var low = Vector128.Create(components.Slice(i, Vector128<ushort>.Count));
                var high = Vector128.Create(components.Slice(i + Vector128<ushort>.Count, Vector128<ushort>.Count));
                Vector128.Narrow(Convert(low), Convert(high)).CopyTo(destination[i..]);
            }
        }

        for (; i < components.Length; i++)
        {
            destination[i] = ToByte(components[i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<ushort> Convert(Vector128<ushort> values)
        {
            var (lower, upper) = Vector128.Widen(values);
            return Vector128.Narrow(
                Vector128.ShiftRightLogical((lower + Vector128.Create(129u)) * Vector128.Create(65281u), 24),
                Vector128.ShiftRightLogical((upper + Vector128.Create(129u)) * Vector128.Create(65281u), 24));
        }
    }

    /// <summary>Converts 8-bit RGBA bytes to 16-bit pixels.</summary>
    public static void FromBytes(ReadOnlySpan<byte> bytes, Span<Rgba64> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = new Rgba64((ushort)(bytes[(i * 4) + 0] * 257), (ushort)(bytes[(i * 4) + 1] * 257), (ushort)(bytes[(i * 4) + 2] * 257), (ushort)(bytes[(i * 4) + 3] * 257));
        }
    }
}
