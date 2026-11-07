using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands rows of packed grayscale samples of 1, 2 or 4 bits (the first pixel in the most significant bits) to 8-bit
/// samples, scaled exactly (<c>v * 255 / (2^bits - 1)</c>: x255, x85, x17) and optionally inverted, for the PNG, Netpbm
/// and TIFF decoders. Every whole byte of the row is one store of the 8, 4 or 2 samples it holds, read from a table of
/// the 256 byte values (<see cref="Expand"/>).
/// </summary>
internal static class PackedGraySamples
{
    // For each sample width (1, 2, 4 bits) and inversion, the 8 / bits samples of every byte value
    private static readonly byte[][] Tables = CreateTables();

    /// <summary>Expands a packed row.</summary>
    /// <param name="packed">The samples, at least <c>ceil(width * bits / 8)</c> bytes.</param>
    /// <param name="bits">1, 2 or 4.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="invert">Whether the scaled samples are inverted (<c>255 - v</c>), for data where zero is white.</param>
    /// <param name="destination">At least <paramref name="width"/> bytes; nothing past them is written.</param>
    public static void Expand(ReadOnlySpan<byte> packed, int bits, int width, bool invert, Span<byte> destination)
    {
        if (bits is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "Packed gray samples have 1, 2 or 4 bits.");

        if (width <= 0)
            return;

        ArgumentOutOfRangeException.ThrowIfLessThan(packed.Length, ((width * bits) + 7) / 8, nameof(packed));
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, width, nameof(destination));

        var samplesPerByte = 8 / bits;
        var whole = width / samplesPerByte;
        ref var source = ref unsafe(MemoryMarshal.GetReference(packed));
        ref var entries = ref unsafe(MemoryMarshal.GetArrayDataReference(Tables[GetTableIndex(bits, invert)]));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        switch (bits)
        {
            case 1:
                for (var i = 0; i < whole; i++)
                {
                    unsafe
                    {
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, i * 8), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref entries, Unsafe.Add(ref source, i) * 8)));
                    }
                }

                break;

            case 2:
                for (var i = 0; i < whole; i++)
                {
                    unsafe
                    {
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, i * 4), Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref entries, Unsafe.Add(ref source, i) * 4)));
                    }
                }

                break;

            default:
                for (var i = 0; i < whole; i++)
                {
                    unsafe
                    {
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, i * 2), Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref entries, Unsafe.Add(ref source, i) * 2)));
                    }
                }

                break;
        }

        var expanded = whole * samplesPerByte;
        ExpandScalar(packed[whole..], bits, width - expanded, invert, destination[expanded..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="Expand"/>.</summary>
    internal static void ExpandScalar(ReadOnlySpan<byte> packed, int bits, int width, bool invert, Span<byte> destination)
    {
        var max = (1 << bits) - 1;
        for (var x = 0; x < width; x++)
        {
            var position = x * bits;
            var value = ((packed[position >> 3] >> (8 - bits - (position & 7))) & max) * 255 / max;
            destination[x] = (byte)(invert ? 255 - value : value);
        }
    }

    private static int GetTableIndex(int bits, bool invert) => (BitOperations.Log2((uint)bits) * 2) + (invert ? 1 : 0);

    private static byte[][] CreateTables()
    {
        var tables = new byte[6][];
        foreach (var bits in (ReadOnlySpan<int>)[1, 2, 4])
        {
            var samplesPerByte = 8 / bits;
            foreach (var invert in (ReadOnlySpan<bool>)[false, true])
            {
                var table = new byte[256 * samplesPerByte];
                for (var value = 0; value < 256; value++)
                {
                    ExpandScalar([(byte)value], bits, samplesPerByte, invert, table.AsSpan(value * samplesPerByte, samplesPerByte));
                }

                tables[GetTableIndex(bits, invert)] = table;
            }
        }

        return tables;
    }
}
