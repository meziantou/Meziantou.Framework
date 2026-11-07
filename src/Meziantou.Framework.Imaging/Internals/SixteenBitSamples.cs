using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands encoded 16-bit samples, big-endian (PNG, Netpbm, big-endian TIFF) or little-endian (little-endian TIFF), to
/// native-endian <see cref="PixelFormat.Rgba64"/> pixels: RGB with an opaque alpha channel, and gray+alpha with the gray
/// replicated. With 128-bit hardware vectors on a little-endian machine, the samples are shuffled several pixels at a
/// time; the results equal the <c>...Scalar</c> references.
/// </summary>
internal static class SixteenBitSamples
{
    /// <summary>Expands 16-bit RGB samples to opaque <see cref="PixelFormat.Rgba64"/> pixels, two pixels at a time.</summary>
    /// <param name="source">At least <c>width * 6</c> bytes.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="destination">At least <c>width * 8</c> bytes; nothing past them is written.</param>
    /// <param name="bigEndian">Whether the samples are big-endian.</param>
    public static void ExpandRgbOpaque(ReadOnlySpan<byte> source, int width, Span<byte> destination, bool bigEndian)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            var alpha = Vector128.Create(0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0xFF, (byte)0xFF);

            // 16 bytes are loaded for the 12 of two pixels
            if (bigEndian)
            {
                var shuffle = Vector128.Create((byte)1, 0, 3, 2, 5, 4, 0xFF, 0xFF, 7, 6, 9, 8, 11, 10, 0xFF, 0xFF);
                for (; (x * 6) + Vector128<byte>.Count <= width * 6; x += 2)
                {
                    (Vector128.Shuffle(Vector128.Create(source[(x * 6)..]), shuffle) | alpha).CopyTo(destination[(x * 8)..]);
                }
            }
            else
            {
                var shuffle = Vector128.Create((byte)0, 1, 2, 3, 4, 5, 0xFF, 0xFF, 6, 7, 8, 9, 10, 11, 0xFF, 0xFF);
                for (; (x * 6) + Vector128<byte>.Count <= width * 6; x += 2)
                {
                    (Vector128.Shuffle(Vector128.Create(source[(x * 6)..]), shuffle) | alpha).CopyTo(destination[(x * 8)..]);
                }
            }
        }

        ExpandRgbOpaqueScalar(source[(x * 6)..(width * 6)], width - x, destination[(x * 8)..], bigEndian);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="ExpandRgbOpaque"/>.</summary>
    internal static void ExpandRgbOpaqueScalar(ReadOnlySpan<byte> source, int width, Span<byte> destination, bool bigEndian)
    {
        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 8)]));
        for (var x = 0; x < width; x++)
        {
            var samples = source.Slice(x * 6, 6);
            pixels[x * 4] = Read(samples, bigEndian);
            pixels[(x * 4) + 1] = Read(samples[2..], bigEndian);
            pixels[(x * 4) + 2] = Read(samples[4..], bigEndian);
            pixels[(x * 4) + 3] = ushort.MaxValue;
        }
    }

    /// <summary>Expands 16-bit gray+alpha samples to <see cref="PixelFormat.Rgba64"/> pixels (the gray replicated), four pixels at a time.</summary>
    /// <param name="source">At least <c>width * 4</c> bytes.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="destination">At least <c>width * 8</c> bytes; nothing past them is written.</param>
    /// <param name="bigEndian">Whether the samples are big-endian.</param>
    public static void ExpandGrayAlpha(ReadOnlySpan<byte> source, int width, Span<byte> destination, bool bigEndian)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            if (bigEndian)
            {
                var low = Vector128.Create((byte)1, 0, 1, 0, 1, 0, 3, 2, 5, 4, 5, 4, 5, 4, 7, 6);
                var high = Vector128.Create((byte)9, 8, 9, 8, 9, 8, 11, 10, 13, 12, 13, 12, 13, 12, 15, 14);
                for (; x + 4 <= width; x += 4)
                {
                    var samples = Vector128.Create(source[(x * 4)..]);
                    Vector128.Shuffle(samples, low).CopyTo(destination[(x * 8)..]);
                    Vector128.Shuffle(samples, high).CopyTo(destination[((x * 8) + 16)..]);
                }
            }
            else
            {
                var low = Vector128.Create((byte)0, 1, 0, 1, 0, 1, 2, 3, 4, 5, 4, 5, 4, 5, 6, 7);
                var high = Vector128.Create((byte)8, 9, 8, 9, 8, 9, 10, 11, 12, 13, 12, 13, 12, 13, 14, 15);
                for (; x + 4 <= width; x += 4)
                {
                    var samples = Vector128.Create(source[(x * 4)..]);
                    Vector128.Shuffle(samples, low).CopyTo(destination[(x * 8)..]);
                    Vector128.Shuffle(samples, high).CopyTo(destination[((x * 8) + 16)..]);
                }
            }
        }

        ExpandGrayAlphaScalar(source[(x * 4)..(width * 4)], width - x, destination[(x * 8)..], bigEndian);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="ExpandGrayAlpha"/>.</summary>
    internal static void ExpandGrayAlphaScalar(ReadOnlySpan<byte> source, int width, Span<byte> destination, bool bigEndian)
    {
        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 8)]));
        for (var x = 0; x < width; x++)
        {
            var gray = Read(source[(x * 4)..], bigEndian);
            pixels[x * 4] = gray;
            pixels[(x * 4) + 1] = gray;
            pixels[(x * 4) + 2] = gray;
            pixels[(x * 4) + 3] = Read(source[((x * 4) + 2)..], bigEndian);
        }
    }

    private static ushort Read(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(source) : BinaryPrimitives.ReadUInt16LittleEndian(source);
}
