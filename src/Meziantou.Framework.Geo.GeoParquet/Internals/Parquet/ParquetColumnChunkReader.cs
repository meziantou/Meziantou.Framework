using System.Buffers.Binary;
using static Meziantou.Framework.Geo.Internals.Parquet.ParquetMetadata;

namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>
/// Decodes the pages of a column chunk: the dictionary page, then the data pages (versions 1 and 2) with their definition
/// levels and their values, in the PLAIN, dictionary, RLE, delta and BYTE_STREAM_SPLIT encodings.
/// </summary>
internal static class ParquetColumnChunkReader
{
    // A page is at most 2 GB: the decompressed size comes from the file, so it is checked before the allocation
    private const int MaxPageLength = int.MaxValue / 2;

    public static object?[] Read(byte[] data, ParquetColumn column, int codec, int rowCount)
    {
        var values = new object?[rowCount];
        object?[]? dictionary = null;
        var row = 0;
        var position = 0;
        while (row < rowCount)
        {
            if (position >= data.Length)
                throw new FormatException($"The column chunk of '{string.Join('.', column.Path)}' has fewer values than its row group.");

            var header = ReadPageHeader(data.AsSpan(position), out var headerLength);
            position += headerLength;
            if (header.CompressedPageSize < 0 || header.CompressedPageSize > data.Length - position || header.UncompressedPageSize is < 0 or > MaxPageLength || header.NumValues < 0)
                throw new FormatException($"A page of the column '{string.Join('.', column.Path)}' of the Parquet file is not valid.");

            var pageStart = position;
            position += header.CompressedPageSize;
            switch (header.Type)
            {
                case PageHeader.DictionaryPage:
                    var dictionaryData = Decompress(codec, data, pageStart, header.CompressedPageSize, header.UncompressedPageSize);
                    if (header.Encoding is not (ParquetEncodings.Plain or ParquetEncodings.PlainDictionary))
                        throw new FormatException($"The encoding {header.Encoding} of a dictionary page of the Parquet file is not valid.");

                    dictionary = DecodePlain(dictionaryData, column, header.NumValues);
                    break;

                case PageHeader.DataPage:
                    if (header.NumValues > rowCount - row)
                        throw new FormatException($"The column chunk of '{string.Join('.', column.Path)}' has more values than its row group.");

                    var page = Decompress(codec, data, pageStart, header.CompressedPageSize, header.UncompressedPageSize);
                    var levelsLength = 0;
                    int[]? levels = null;
                    if (column.MaxDefinitionLevel > 0)
                    {
                        var bitWidth = ParquetEncodings.GetBitWidth(column.MaxDefinitionLevel);
                        if (header.DefinitionLevelEncoding is ParquetEncodings.BitPacked)
                        {
                            levels = ParquetEncodings.DecodeBitPacked(page, bitWidth, header.NumValues, out levelsLength);
                        }
                        else
                        {
                            var encoded = ParquetEncodings.ReadLengthPrefixed(page, out levelsLength);
                            levels = ParquetEncodings.DecodeRleBitPacked(encoded, bitWidth, header.NumValues);
                        }
                    }

                    ReadValues(page.AsSpan(levelsLength), column, header, levels, dictionary, values, row);
                    row += header.NumValues;
                    break;

                case PageHeader.DataPageV2:
                    if (header.NumValues > rowCount - row)
                        throw new FormatException($"The column chunk of '{string.Join('.', column.Path)}' has more values than its row group.");

                    // The levels are not compressed; the values are, unless is_compressed is false
                    var levelsSize = (long)header.RepetitionLevelsByteLength + header.DefinitionLevelsByteLength;
                    if (header.RepetitionLevelsByteLength < 0 || header.DefinitionLevelsByteLength < 0 || levelsSize > header.CompressedPageSize || levelsSize > header.UncompressedPageSize)
                        throw new FormatException($"A page of the column '{string.Join('.', column.Path)}' of the Parquet file is not valid.");

                    int[]? levelsV2 = null;
                    if (column.MaxDefinitionLevel > 0)
                    {
                        var definitionLevels = data.AsSpan(pageStart + header.RepetitionLevelsByteLength, header.DefinitionLevelsByteLength);
                        levelsV2 = ParquetEncodings.DecodeRleBitPacked(definitionLevels, ParquetEncodings.GetBitWidth(column.MaxDefinitionLevel), header.NumValues);
                    }

                    var valuesStart = pageStart + (int)levelsSize;
                    var valuesLength = header.CompressedPageSize - (int)levelsSize;
                    var valuesData = header.IsCompressed
                        ? Decompress(codec, data, valuesStart, valuesLength, header.UncompressedPageSize - (int)levelsSize)
                        : Decompress(ParquetCompression.Uncompressed, data, valuesStart, valuesLength, valuesLength);
                    ReadValues(valuesData, column, header, levelsV2, dictionary, values, row);
                    row += header.NumValues;
                    break;

                default:
                    // An index page, or a page type of a later version
                    break;
            }
        }

        return values;
    }

    private static byte[] Decompress(int codec, byte[] data, int offset, int length, int decompressedLength) =>
        ParquetCompression.Decompress(codec, data, offset, length, decompressedLength);

    // Decodes the values of a page, and puts them in the rows whose definition level is the maximum one: the others are null
    private static void ReadValues(ReadOnlySpan<byte> data, ParquetColumn column, PageHeader header, int[]? levels, object?[]? dictionary, object?[] values, int row)
    {
        var count = header.NumValues;
        var nonNull = count;
        if (levels is not null)
        {
            nonNull = 0;
            foreach (var level in levels)
            {
                if (level == column.MaxDefinitionLevel)
                {
                    nonNull++;
                }
                else if (level > column.MaxDefinitionLevel)
                {
                    throw new FormatException($"A definition level of the column '{string.Join('.', column.Path)}' of the Parquet file is not valid.");
                }
            }
        }

        var decoded = header.Encoding switch
        {
            ParquetEncodings.Plain => DecodePlain(data, column, nonNull),
            ParquetEncodings.PlainDictionary or ParquetEncodings.RleDictionary => DecodeDictionary(data, column, dictionary, nonNull),
            ParquetEncodings.Rle when column.PhysicalType is ParquetColumn.Boolean => DecodeRleBooleans(data, nonNull),
            ParquetEncodings.DeltaBinaryPacked => DecodeDeltaBinaryPacked(data, column, nonNull),
            ParquetEncodings.DeltaLengthByteArray when column.PhysicalType is ParquetColumn.ByteArray => DecodeDeltaLengthByteArray(data, column, nonNull),
            ParquetEncodings.DeltaByteArray when column.PhysicalType is ParquetColumn.ByteArray or ParquetColumn.FixedLenByteArray => DecodeDeltaByteArray(data, column, nonNull),
            ParquetEncodings.ByteStreamSplit => DecodeByteStreamSplit(data, column, nonNull),
            _ => throw new NotSupportedException($"The encoding {header.Encoding} of the column '{string.Join('.', column.Path)}' of the Parquet file is not supported."),
        };

        var next = 0;
        for (var i = 0; i < count; i++)
        {
            if (levels is null || levels[i] == column.MaxDefinitionLevel)
            {
                values[row + i] = decoded[next++];
            }
        }
    }

    private static object?[] DecodePlain(ReadOnlySpan<byte> data, ParquetColumn column, int count)
    {
        var result = new object?[count];
        var position = 0;
        switch (column.PhysicalType)
        {
            case ParquetColumn.Boolean:
                if ((count + 7) / 8 > data.Length)
                    throw Truncated(column);

                for (var i = 0; i < count; i++)
                {
                    result[i] = ParquetColumn.FromBoolean(((data[i >> 3] >> (i & 7)) & 1) is 1);
                }

                break;

            case ParquetColumn.ByteArray:
                for (var i = 0; i < count; i++)
                {
                    if (position + 4 > data.Length)
                        throw Truncated(column);

                    var length = BinaryPrimitives.ReadInt32LittleEndian(data[position..]);
                    position += 4;
                    if (length < 0 || length > data.Length - position)
                        throw Truncated(column);

                    result[i] = column.FromBytes(data.Slice(position, length));
                    position += length;
                }

                break;

            default:
                var width = GetWidth(column);
                if ((long)count * width > data.Length)
                    throw Truncated(column);

                for (var i = 0; i < count; i++)
                {
                    result[i] = FromFixedWidth(column, data.Slice(i * width, width));
                }

                break;
        }

        return result;
    }

    private static object?[] DecodeDictionary(ReadOnlySpan<byte> data, ParquetColumn column, object?[]? dictionary, int count)
    {
        if (dictionary is null)
            throw new FormatException($"The column '{string.Join('.', column.Path)}' of the Parquet file uses a dictionary, but has no dictionary page.");

        if (count is 0)
            return [];

        if (data.IsEmpty)
            throw Truncated(column);

        var indexes = ParquetEncodings.DecodeRleBitPacked(data[1..], data[0], count);
        var result = new object?[count];
        for (var i = 0; i < count; i++)
        {
            var index = indexes[i];
            if ((uint)index >= (uint)dictionary.Length)
                throw new FormatException($"An index of the dictionary of the column '{string.Join('.', column.Path)}' of the Parquet file is out of range.");

            // The arrays of bytes are copied: each feature gets its own
            result[i] = dictionary[index] is byte[] bytes ? bytes.Clone() : dictionary[index];
        }

        return result;
    }

    private static object?[] DecodeRleBooleans(ReadOnlySpan<byte> data, int count)
    {
        var encoded = ParquetEncodings.ReadLengthPrefixed(data, out _);
        var bits = ParquetEncodings.DecodeRleBitPacked(encoded, bitWidth: 1, count);
        return Array.ConvertAll(bits, bit => (object?)ParquetColumn.FromBoolean(bit is not 0));
    }

    private static object?[] DecodeDeltaBinaryPacked(ReadOnlySpan<byte> data, ParquetColumn column, int count)
    {
        var integers = ParquetEncodings.DecodeDeltaBinaryPacked(data, count, out _);
        if (integers.Length != count)
            throw Truncated(column);

        return column.PhysicalType switch
        {
            ParquetColumn.Int32 => Array.ConvertAll(integers, value => (object?)column.FromInt32(unchecked((int)value))),
            ParquetColumn.Int64 => Array.ConvertAll(integers, value => (object?)column.FromInt64(value)),
            _ => throw new FormatException($"The column '{string.Join('.', column.Path)}' of the Parquet file has the DELTA_BINARY_PACKED encoding, which only integers can have."),
        };
    }

    // The lengths with the delta encoding, then the bytes of the values one after the other
    private static object?[] DecodeDeltaLengthByteArray(ReadOnlySpan<byte> data, ParquetColumn column, int count)
    {
        var lengths = ParquetEncodings.DecodeDeltaBinaryPacked(data, count, out var position);
        if (lengths.Length != count)
            throw Truncated(column);

        var result = new object?[count];
        for (var i = 0; i < count; i++)
        {
            if (lengths[i] < 0 || lengths[i] > data.Length - position)
                throw Truncated(column);

            result[i] = column.FromBytes(data.Slice(position, (int)lengths[i]));
            position += (int)lengths[i];
        }

        return result;
    }

    // Each value is a prefix of the previous value followed by a suffix: the prefix lengths with the delta encoding, then the suffixes
    private static object?[] DecodeDeltaByteArray(ReadOnlySpan<byte> data, ParquetColumn column, int count)
    {
        var prefixLengths = ParquetEncodings.DecodeDeltaBinaryPacked(data, count, out var position);
        var suffixLengths = ParquetEncodings.DecodeDeltaBinaryPacked(data[position..], count, out var suffixLengthsLength);
        if (prefixLengths.Length != count || suffixLengths.Length != count)
            throw Truncated(column);

        position += suffixLengthsLength;
        var result = new object?[count];
        var previous = Array.Empty<byte>();
        for (var i = 0; i < count; i++)
        {
            var prefix = prefixLengths[i];
            var suffix = suffixLengths[i];
            if (prefix < 0 || prefix > previous.Length || suffix < 0 || suffix > data.Length - position)
                throw Truncated(column);

            var value = new byte[prefix + suffix];
            previous.AsSpan(0, (int)prefix).CopyTo(value);
            data.Slice(position, (int)suffix).CopyTo(value.AsSpan((int)prefix));
            position += (int)suffix;
            result[i] = column.PhysicalType is ParquetColumn.FixedLenByteArray ? FromFixedWidth(column, value) : column.FromBytes(value);
            previous = value;
        }

        return result;
    }

    // The k-th byte of every value is in the k-th stream
    private static object?[] DecodeByteStreamSplit(ReadOnlySpan<byte> data, ParquetColumn column, int count)
    {
        if (column.PhysicalType is ParquetColumn.Boolean or ParquetColumn.ByteArray or ParquetColumn.Int96)
            throw new FormatException($"The column '{string.Join('.', column.Path)}' of the Parquet file has the BYTE_STREAM_SPLIT encoding, which its type cannot have.");

        var width = GetWidth(column);
        if ((long)count * width > data.Length)
            throw Truncated(column);

        var result = new object?[count];
        var value = new byte[width];
        for (var i = 0; i < count; i++)
        {
            for (var k = 0; k < width; k++)
            {
                value[k] = data[(k * count) + i];
            }

            result[i] = FromFixedWidth(column, value);
        }

        return result;
    }

    private static int GetWidth(ParquetColumn column) => column.PhysicalType switch
    {
        ParquetColumn.Int32 or ParquetColumn.Float => 4,
        ParquetColumn.Int64 or ParquetColumn.Double => 8,
        ParquetColumn.Int96 => 12,
        ParquetColumn.FixedLenByteArray => column.TypeLength,
        _ => throw new FormatException($"The physical type {column.PhysicalType} of the column '{string.Join('.', column.Path)}' of the Parquet file is not valid."),
    };

    private static object FromFixedWidth(ParquetColumn column, ReadOnlySpan<byte> value) => column.PhysicalType switch
    {
        ParquetColumn.Int32 => column.FromInt32(BinaryPrimitives.ReadInt32LittleEndian(value)),
        ParquetColumn.Int64 => column.FromInt64(BinaryPrimitives.ReadInt64LittleEndian(value)),
        ParquetColumn.Float => ParquetColumn.FromFloat(BinaryPrimitives.ReadSingleLittleEndian(value)),
        ParquetColumn.Double => ParquetColumn.FromDouble(BinaryPrimitives.ReadDoubleLittleEndian(value)),
        ParquetColumn.Int96 => ParquetColumn.FromInt96(value),
        _ => column.FromBytes(value),
    };

    private static FormatException Truncated(ParquetColumn column) => new($"A page of the column '{string.Join('.', column.Path)}' of the Parquet file is truncated.");
}
