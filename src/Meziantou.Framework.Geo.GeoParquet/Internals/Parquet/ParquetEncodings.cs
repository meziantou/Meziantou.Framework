using System.Buffers.Binary;

namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>The integer encodings of Parquet: the RLE/bit-packing hybrid of the levels and the dictionary indexes, and the delta encoding.</summary>
internal static class ParquetEncodings
{
    public const int Plain = 0;
    public const int PlainDictionary = 2;
    public const int Rle = 3;
    public const int BitPacked = 4;
    public const int DeltaBinaryPacked = 5;
    public const int DeltaLengthByteArray = 6;
    public const int DeltaByteArray = 7;
    public const int RleDictionary = 8;
    public const int ByteStreamSplit = 9;

    /// <summary>Gets the number of bits of the values from 0 to a maximum value, as the levels are encoded.</summary>
    public static int GetBitWidth(int maxValue) => maxValue is 0 ? 0 : 32 - int.LeadingZeroCount(maxValue);

    /// <summary>Decodes values of the RLE/bit-packing hybrid encoding, without a length prefix.</summary>
    public static int[] DecodeRleBitPacked(ReadOnlySpan<byte> data, int bitWidth, int count)
    {
        if (bitWidth is < 0 or > 32)
            throw new FormatException($"The bit width {bitWidth} of a column of the Parquet file is not valid.");

        var values = new int[count];
        var position = 0;
        var written = 0;
        var byteWidth = (bitWidth + 7) / 8;
        while (written < count)
        {
            var header = ReadVarint(data, ref position);
            if ((header & 1) is 0)
            {
                // A run of a repeated value
                var runLength = header >> 1;
                if (position + byteWidth > data.Length)
                    throw Truncated();

                var value = 0;
                for (var i = 0; i < byteWidth; i++)
                {
                    value |= data[position + i] << (8 * i);
                }

                position += byteWidth;
                var length = (int)Math.Min(runLength, (ulong)(count - written));
                values.AsSpan(written, length).Fill(value);
                written += length;
            }
            else
            {
                // Groups of 8 bit-packed values; the last group may be padded
                var groups = header >> 1;
                if (groups > (ulong)(data.Length - position))
                    throw Truncated();

                var byteLength = (int)groups * bitWidth;
                if (byteLength > data.Length - position)
                    throw Truncated();

                var packed = data.Slice(position, byteLength);
                var length = (int)Math.Min(groups * 8, (ulong)(count - written));
                for (var i = 0; i < length; i++)
                {
                    values[written + i] = (int)ReadBits(packed, (long)i * bitWidth, bitWidth);
                }

                position += byteLength;
                written += length;
            }
        }

        return values;
    }

    /// <summary>Decodes the deprecated BIT_PACKED encoding of the levels: the bits from the most significant one.</summary>
    public static int[] DecodeBitPacked(ReadOnlySpan<byte> data, int bitWidth, int count, out int length)
    {
        length = (int)(((long)count * bitWidth + 7) / 8);
        if (length > data.Length)
            throw Truncated();

        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            var value = 0;
            for (var bit = 0; bit < bitWidth; bit++)
            {
                var index = ((long)i * bitWidth) + bit;
                value = (value << 1) | ((data[(int)(index >> 3)] >> (7 - (int)(index & 7))) & 1);
            }

            values[i] = value;
        }

        return values;
    }

    /// <summary>Decodes integers of the DELTA_BINARY_PACKED encoding.</summary>
    /// <param name="data">The data.</param>
    /// <param name="maxCount">The number of values of the page: the header cannot declare more.</param>
    /// <param name="length">The number of bytes of the encoded values.</param>
    public static long[] DecodeDeltaBinaryPacked(ReadOnlySpan<byte> data, int maxCount, out int length)
    {
        var position = 0;
        var blockSize = ReadVarint(data, ref position);
        var miniblockCount = ReadVarint(data, ref position);
        var totalCount = ReadVarint(data, ref position);
        var value = ReadZigZag(data, ref position);
        if (blockSize is 0 || blockSize % 128 is not 0 || miniblockCount is 0 || blockSize % miniblockCount is not 0 || blockSize > int.MaxValue)
            throw new FormatException("The header of a DELTA_BINARY_PACKED page of the Parquet file is not valid.");

        // A miniblock of bit width 0 takes no space: the count is checked against the page, not the data
        if (totalCount > (ulong)maxCount)
            throw new FormatException("A DELTA_BINARY_PACKED page of the Parquet file has more values than its header declares.");

        var valuesPerMiniblock = (int)(blockSize / miniblockCount);
        var values = new long[totalCount];
        if (totalCount is 0)
        {
            length = position;
            return values;
        }

        values[0] = value;
        var count = 1;
        while (count < values.Length)
        {
            var minDelta = ReadZigZag(data, ref position);
            if (miniblockCount > (ulong)(data.Length - position))
                throw Truncated();

            var bitWidths = data.Slice(position, (int)miniblockCount);
            position += (int)miniblockCount;
            for (var m = 0; m < bitWidths.Length && count < values.Length; m++)
            {
                var bitWidth = bitWidths[m];
                if (bitWidth > 64)
                    throw new FormatException("A bit width of a DELTA_BINARY_PACKED page of the Parquet file is not valid.");

                var byteLength = valuesPerMiniblock / 8 * bitWidth;
                if (byteLength > data.Length - position)
                    throw Truncated();

                var packed = data.Slice(position, byteLength);
                for (var i = 0; i < valuesPerMiniblock && count < values.Length; i++)
                {
                    var delta = ReadBits(packed, (long)i * bitWidth, bitWidth);
                    value = unchecked(value + minDelta + (long)delta);
                    values[count++] = value;
                }

                position += byteLength;
            }
        }

        length = position;
        return values;
    }

    /// <summary>Reads a value of up to 64 bits, packed from the least significant bit.</summary>
    public static ulong ReadBits(ReadOnlySpan<byte> data, long bitOffset, int bitWidth)
    {
        if (bitWidth is 0)
            return 0;

        var start = (int)(bitOffset >> 3);
        var shift = (int)(bitOffset & 7);
        var byteCount = (shift + bitWidth + 7) / 8;
        if (start + byteCount > data.Length)
            throw Truncated();

        ulong low = 0;
        for (var i = 0; i < Math.Min(byteCount, 8); i++)
        {
            low |= (ulong)data[start + i] << (8 * i);
        }

        var result = low >> shift;
        if (byteCount > 8)
        {
            result |= (ulong)data[start + 8] << (64 - shift);
        }

        return bitWidth is 64 ? result : result & ((1UL << bitWidth) - 1);
    }

    /// <summary>Reads the 4-byte length prefix of levels or booleans encoded with RLE, and returns the encoded data.</summary>
    public static ReadOnlySpan<byte> ReadLengthPrefixed(ReadOnlySpan<byte> data, out int length)
    {
        if (data.Length < 4)
            throw Truncated();

        var size = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (size < 0 || size > data.Length - 4)
            throw Truncated();

        length = 4 + size;
        return data.Slice(4, size);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= data.Length)
                throw Truncated();

            var b = data[position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) is 0)
                return result;
        }

        throw new FormatException("An integer of a page of the Parquet file is too long.");
    }

    private static long ReadZigZag(ReadOnlySpan<byte> data, ref int position)
    {
        var value = ReadVarint(data, ref position);
        return (long)(value >> 1) ^ -(long)(value & 1);
    }

    private static FormatException Truncated() => new("A page of the Parquet file is truncated.");
}
