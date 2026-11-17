using System.Buffers.Binary;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>A filter of the output of a RAR decoder: the block of <see cref="Length"/> bytes at <see cref="Start"/> is transformed before it is written.</summary>
internal sealed class RarFilter
{
    /// <summary>Gets or sets the absolute position of the block in the output of the solid stream.</summary>
    public long Start { get; set; }

    public int Length { get; set; }

    public RarFilterType Type { get; set; }

    /// <summary>Gets or sets the number of channels (DELTA, AUDIO), or the width of a line plus 3 (RGB).</summary>
    public uint Channels { get; set; }

    /// <summary>Gets or sets the position of the red byte in a pixel (RGB).</summary>
    public uint RedPosition { get; set; }

    /// <summary>
    /// Applies the filter to the block in <c>buffer[0..length]</c>, using <c>buffer[length..2*length]</c> as scratch space,
    /// and returns the offset of the filtered block in <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The block, followed by as many bytes of scratch space.</param>
    /// <param name="fileOffset">The position of the block in the file (the E8, ARM and ITANIUM filters convert absolute addresses).</param>
    /// <param name="rar5">Whether the filter is a filter of RAR5 (which wraps the E8 offsets at 16 MiB).</param>
    public int Apply(byte[] buffer, long fileOffset, bool rar5)
    {
        var length = Length;
        switch (Type)
        {
            case RarFilterType.E8:
            case RarFilterType.E8E9:
                ApplyE8(buffer.AsSpan(0, length), (uint)fileOffset, Type is RarFilterType.E8E9, rar5);
                return 0;

            case RarFilterType.Arm:
                ApplyArm(buffer.AsSpan(0, length), (uint)fileOffset);
                return 0;

            case RarFilterType.Itanium:
                ApplyItanium(buffer.AsSpan(0, length), (uint)fileOffset);
                return 0;

            case RarFilterType.Delta:
                ApplyDelta(buffer.AsSpan(0, length), buffer.AsSpan(length, length), Channels);
                return length;

            case RarFilterType.Rgb:
                ApplyRgb(buffer.AsSpan(0, length), buffer.AsSpan(length, length), Channels, RedPosition);
                return length;

            case RarFilterType.Audio:
                ApplyAudio(buffer.AsSpan(0, length), buffer.AsSpan(length, length), Channels);
                return length;

            default:
                throw new InvalidDataException("The compressed data use an unknown filter.");
        }
    }

    // The filters follow the descriptions of the standard RarVM filters of libarchive (BSD-2-Clause) and of the RAR 5.0 technote
    private static void ApplyE8(Span<byte> data, uint fileOffset, bool e9, bool rar5)
    {
        const uint FileSize = 0x1000000;
        for (var position = 0; position + 4 < data.Length;)
        {
            var value = data[position++];
            if (value is not 0xE8 && !(e9 && value is 0xE9))
                continue;

            var offset = (uint)position + fileOffset;
            if (rar5)
            {
                offset %= FileSize;
            }

            var address = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            if ((address & 0x80000000) != 0)
            {
                if (((address + offset) & 0x80000000) == 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(data[position..], address + FileSize);
                }
            }
            else if (((address - FileSize) & 0x80000000) != 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data[position..], address - offset);
            }

            position += 4;
        }
    }

    private static void ApplyArm(Span<byte> data, uint fileOffset)
    {
        for (var position = 0; position + 3 < data.Length; position += 4)
        {
            if (data[position + 3] is not 0xEB)
                continue;

            var offset = data[position] | ((uint)data[position + 1] << 8) | ((uint)data[position + 2] << 16);
            offset -= (fileOffset + (uint)position) / 4;
            data[position] = (byte)offset;
            data[position + 1] = (byte)(offset >> 8);
            data[position + 2] = (byte)(offset >> 16);
        }
    }

    private static void ApplyItanium(Span<byte> data, uint fileOffset)
    {
        ReadOnlySpan<byte> masks = [4, 4, 6, 6, 0, 0, 7, 7, 4, 4, 0, 0, 4, 4, 0, 0];
        fileOffset >>= 4;
        for (var position = 0; position + 21 < data.Length; position += 16, fileOffset++)
        {
            var bundle = data.Slice(position, 16);
            var template = (bundle[0] & 0x1F) - 0x10;
            if (template < 0)
                continue;

            var mask = masks[template];
            if (mask is 0)
                continue;

            for (var slot = 0; slot <= 2; slot++)
            {
                if ((mask & (1 << slot)) == 0)
                    continue;

                var start = (slot * 41) + 5;
                if (GetBits(bundle, start + 37, 4) is 5)
                {
                    var offset = GetBits(bundle, start + 13, 20);
                    SetBits(bundle, (offset - fileOffset) & 0xFFFFF, start + 13, 20);
                }
            }
        }

        static uint GetBits(Span<byte> data, int bitPosition, int bitCount)
        {
            var index = bitPosition >> 3;
            var value = ReadUInt32(data, index);
            return (value >> (bitPosition & 7)) & (0xFFFFFFFF >> (32 - bitCount));
        }

        static void SetBits(Span<byte> data, uint value, int bitPosition, int bitCount)
        {
            var index = bitPosition >> 3;
            var shift = bitPosition & 7;
            var mask = ~((0xFFFFFFFF >> (32 - bitCount)) << shift);
            var current = ReadUInt32(data, index);
            current = (current & mask) | (value << shift);
            for (var i = 0; i < 4 && index + i < data.Length; i++)
            {
                data[index + i] = (byte)(current >> (i * 8));
            }
        }

        static uint ReadUInt32(Span<byte> data, int index)
        {
            uint value = 0;
            for (var i = 0; i < 4 && index + i < data.Length; i++)
            {
                value |= (uint)data[index + i] << (i * 8);
            }

            return value;
        }
    }

    private static void ApplyDelta(ReadOnlySpan<byte> source, Span<byte> destination, uint channels)
    {
        var sourcePosition = 0;
        for (var channel = 0; channel < channels; channel++)
        {
            byte previous = 0;
            for (var position = channel; position < destination.Length; position += (int)channels)
            {
                previous -= source[sourcePosition++];
                destination[position] = previous;
            }
        }
    }

    private static void ApplyRgb(ReadOnlySpan<byte> source, Span<byte> destination, uint lineWidth, uint redPosition)
    {
        var width = (int)(lineWidth - 3);
        var sourcePosition = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            var previous = 0;
            for (var position = channel; position < destination.Length; position += 3)
            {
                int predicted;
                if (position >= width + 3)
                {
                    int upper = destination[position - width];
                    int upperLeft = destination[position - width - 3];
                    predicted = previous + upper - upperLeft;
                    var pa = Math.Abs(predicted - previous);
                    var pb = Math.Abs(predicted - upper);
                    var pc = Math.Abs(predicted - upperLeft);
                    if (pa <= pb && pa <= pc)
                    {
                        predicted = previous;
                    }
                    else if (pb <= pc)
                    {
                        predicted = upper;
                    }
                    else
                    {
                        predicted = upperLeft;
                    }
                }
                else
                {
                    predicted = previous;
                }

                previous = (byte)(predicted - source[sourcePosition++]);
                destination[position] = (byte)previous;
            }
        }

        for (var position = (int)redPosition; position < destination.Length - 2; position += 3)
        {
            var green = destination[position + 1];
            destination[position] += green;
            destination[position + 2] += green;
        }
    }

    private static void ApplyAudio(ReadOnlySpan<byte> source, Span<byte> destination, uint channels)
    {
        var sourcePosition = 0;
        Span<uint> differences = stackalloc uint[7];
        for (var channel = 0; channel < channels; channel++)
        {
            uint previousByte = 0;
            var previousDelta = 0;
            int d1 = 0, d2 = 0;
            int k1 = 0, k2 = 0, k3 = 0;
            differences.Clear();
            var byteCount = 0;
            for (var position = channel; position < destination.Length; position += (int)channels, byteCount++)
            {
                var d3 = d2;
                d2 = previousDelta - d1;
                d1 = previousDelta;
                var predicted = (uint)((8 * (int)previousByte) + (k1 * d1) + (k2 * d2) + (k3 * d3));
                predicted = (predicted >> 3) & 0xFF;
                uint current = source[sourcePosition++];
                predicted = (predicted - current) & 0xFF;
                destination[position] = (byte)predicted;
                previousDelta = (sbyte)(byte)(predicted - previousByte);
                previousByte = predicted;

                var d = (sbyte)current * 8;
                differences[0] += (uint)Math.Abs(d);
                differences[1] += (uint)Math.Abs(d - d1);
                differences[2] += (uint)Math.Abs(d + d1);
                differences[3] += (uint)Math.Abs(d - d2);
                differences[4] += (uint)Math.Abs(d + d2);
                differences[5] += (uint)Math.Abs(d - d3);
                differences[6] += (uint)Math.Abs(d + d3);
                if ((byteCount & 0x1F) == 0)
                {
                    var minimum = differences[0];
                    var index = 0;
                    differences[0] = 0;
                    for (var i = 1; i < differences.Length; i++)
                    {
                        if (differences[i] < minimum)
                        {
                            minimum = differences[i];
                            index = i;
                        }

                        differences[i] = 0;
                    }

                    switch (index)
                    {
                        case 1 when k1 >= -16: k1--; break;
                        case 2 when k1 < 16: k1++; break;
                        case 3 when k2 >= -16: k2--; break;
                        case 4 when k2 < 16: k2++; break;
                        case 5 when k3 >= -16: k3--; break;
                        case 6 when k3 < 16: k3++; break;
                    }
                }
            }
        }
    }
}
