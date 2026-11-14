using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the rows of a run-length TGA image: run-length packets for 2 to 128 equal pixels, raw packets for the 1 to 128
/// pixels before the next pair of equal pixels; packets never cross the scan line.
/// </summary>
internal static class TgaRunLengthEncoder
{
    /// <summary>Encodes one row of stored pixels.</summary>
    /// <param name="stored">The stored samples of the row.</param>
    /// <param name="bytesPerPixel">1, 3 or 4.</param>
    /// <param name="destination">At least the length of the samples plus 2 bytes per 128 pixels.</param>
    /// <returns>The number of bytes written.</returns>
    public static int EncodeRow(ReadOnlySpan<byte> stored, int bytesPerPixel, Span<byte> destination) => bytesPerPixel switch
    {
        1 => EncodeRow<byte>(stored, destination),
        3 => EncodeRow(unsafe(MemoryMarshal.Cast<byte, Pixel24>(stored)), destination),
        _ => EncodeRow(unsafe(MemoryMarshal.Cast<byte, uint>(stored)), destination),
    };

    /// <summary>The pixel-at-a-time reference of <see cref="EncodeRow(ReadOnlySpan{byte}, int, Span{byte})"/>.</summary>
    internal static int EncodeRowScalar(ReadOnlySpan<byte> stored, int bytesPerPixel, Span<byte> destination) => bytesPerPixel switch
    {
        1 => EncodeRowScalar<byte>(stored, destination),
        3 => EncodeRowScalar(unsafe(MemoryMarshal.Cast<byte, Pixel24>(stored)), destination),
        _ => EncodeRowScalar(unsafe(MemoryMarshal.Cast<byte, uint>(stored)), destination),
    };

    /// <summary>Encodes one row of pixels (compared as values): the runs are found by <see cref="RunLengthSearch"/>.</summary>
    private static int EncodeRow<TPixel>(ReadOnlySpan<TPixel> pixels, Span<byte> destination)
        where TPixel : unmanaged, IEquatable<TPixel>
    {
        var bytes = Unsafe.SizeOf<TPixel>();
        var width = pixels.Length;
        var written = 0;
        var x = 0;
        while (x < width)
        {
            var run = RunLengthSearch.CountRun(pixels[x..], TgaFormat.MaxPacketLength);
            if (run >= 2)
            {
                destination[written++] = (byte)(TgaFormat.RunLengthPacketFlag | (run - 1));
                unsafe { MemoryMarshal.AsBytes(pixels.Slice(x, 1)).CopyTo(destination[written..]); }
                written += bytes;
                x += run;
                continue;
            }

            // Raw packet: stop before the next run of two or more identical pixels, which starts after this pixel
            var pair = IndexOfPair(pixels[(x + 1)..Math.Min(width, x + TgaFormat.MaxPacketLength + 1)]);
            var count = pair < 0 ? Math.Min(TgaFormat.MaxPacketLength, width - x) : pair + 1;
            destination[written++] = (byte)(count - 1);
            unsafe { MemoryMarshal.AsBytes(pixels.Slice(x, count)).CopyTo(destination[written..]); }
            written += count * bytes;
            x += count;
        }

        return written;
    }

    private static int IndexOfPair<TPixel>(ReadOnlySpan<TPixel> pixels)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(byte))
            return RunLengthSearch.IndexOfPair(unsafe(MemoryMarshal.Cast<TPixel, byte>(pixels)));

        if (typeof(TPixel) == typeof(uint))
            return RunLengthSearch.IndexOfPair(unsafe(MemoryMarshal.Cast<TPixel, uint>(pixels)));

        return RunLengthSearch.IndexOfPair24(unsafe(MemoryMarshal.AsBytes(pixels)));
    }

    private static int EncodeRowScalar<TPixel>(ReadOnlySpan<TPixel> pixels, Span<byte> destination)
        where TPixel : unmanaged, IEquatable<TPixel>
    {
        var bytes = Unsafe.SizeOf<TPixel>();
        var width = pixels.Length;
        var written = 0;
        var x = 0;
        while (x < width)
        {
            var pixel = pixels[x];
            var run = 1;
            while (x + run < width && run < TgaFormat.MaxPacketLength && pixels[x + run].Equals(pixel))
            {
                run++;
            }

            if (run >= 2)
            {
                destination[written++] = (byte)(TgaFormat.RunLengthPacketFlag | (run - 1));
                unsafe { MemoryMarshal.AsBytes(pixels.Slice(x, 1)).CopyTo(destination[written..]); }
                written += bytes;
                x += run;
                continue;
            }

            // Raw packet: stop before the next run of two or more identical pixels
            var count = 1;
            while (x + count < width && count < TgaFormat.MaxPacketLength)
            {
                if (x + count + 1 < width && pixels[x + count].Equals(pixels[x + count + 1]))
                    break;

                count++;
            }

            destination[written++] = (byte)(count - 1);
            unsafe { MemoryMarshal.AsBytes(pixels.Slice(x, count)).CopyTo(destination[written..]); }
            written += count * bytes;
            x += count;
        }

        return written;
    }

    /// <summary>A stored 24-bit pixel, compared as a value.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct Pixel24 : IEquatable<Pixel24>
    {
        private readonly byte _first;
        private readonly byte _second;
        private readonly byte _third;

        public bool Equals(Pixel24 other) => _first == other._first && _second == other._second && _third == other._third;

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Pixel24 other && Equals(other);

        public override int GetHashCode() => _first | (_second << 8) | (_third << 16);
    }
}
