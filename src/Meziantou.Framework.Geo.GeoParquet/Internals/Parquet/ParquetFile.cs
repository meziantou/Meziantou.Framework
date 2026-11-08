using System.Buffers.Binary;
using static Meziantou.Framework.Geo.Internals.Parquet.ParquetMetadata;

namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>
/// Reads Parquet files (https://parquet.apache.org/docs/file-format/): the metadata of the footer, then the column chunks
/// of a row group on demand. Only the columns that are not repeated (not in a list or a map) can be read.
/// </summary>
internal sealed class ParquetFile
{
    private const int FooterLength = 8;

    // The metadata of a file with thousands of row groups and columns is a few megabytes
    private const int MaxMetadataLength = 256 * 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "PAR1"u8;

    private readonly Stream _stream;
    private readonly long _origin;
    private readonly long _dataLength;
    private readonly FileMetadata _metadata;

    private ParquetFile(Stream stream, long origin, long dataLength, FileMetadata metadata, ParquetColumn[] columns)
    {
        _stream = stream;
        _origin = origin;
        _dataLength = dataLength;
        _metadata = metadata;
        Columns = columns;
    }

    /// <summary>Gets the leaf columns of the schema, in the order of the column chunks.</summary>
    public ParquetColumn[] Columns { get; }

    public IReadOnlyDictionary<string, string> KeyValueMetadata => _metadata.KeyValues;

    public int RowGroupCount => _metadata.RowGroups.Length;

    public static async ValueTask<ParquetFile> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
            throw new NotSupportedException("The stream of a Parquet file must be seekable: its metadata is at its end.");

        var origin = stream.Position;
        var length = stream.Length - origin;
        if (length < Magic.Length + FooterLength)
            throw new FormatException("The data is not a Parquet file: it is too short.");

        var footer = new byte[FooterLength];
        stream.Seek(-FooterLength, SeekOrigin.End);
        await ReadExactlyAsync(stream, footer, cancellationToken).ConfigureAwait(false);
        if (footer.AsSpan(4).SequenceEqual("PARE"u8))
            throw new NotSupportedException("The Parquet file is encrypted, which is not supported.");

        if (!footer.AsSpan(4).SequenceEqual(Magic))
            throw new FormatException("The data is not a Parquet file: it does not end with 'PAR1'.");

        var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(footer);
        if (metadataLength <= 0 || metadataLength > MaxMetadataLength || metadataLength > length - Magic.Length - FooterLength)
            throw new FormatException("The length of the metadata of the Parquet file is not valid.");

        var data = new byte[metadataLength];
        stream.Seek(origin + length - FooterLength - metadataLength, SeekOrigin.Begin);
        await ReadExactlyAsync(stream, data, cancellationToken).ConfigureAwait(false);
        var metadata = ReadFileMetadata(data);
        var columns = GetLeafColumns(metadata.Schema);
        foreach (var rowGroup in metadata.RowGroups)
        {
            if (rowGroup.Columns.Length != columns.Length)
                throw new FormatException("A row group of the Parquet file does not have a column chunk for each column.");

            if (rowGroup.NumRows < 0 || rowGroup.NumRows > int.MaxValue)
                throw new FormatException("A row group of the Parquet file has an invalid number of rows.");
        }

        return new ParquetFile(stream, origin, length - FooterLength - metadataLength, metadata, columns);
    }

    public int GetRowCount(int rowGroup) => (int)_metadata.RowGroups[rowGroup].NumRows;

    /// <summary>Gets the minimum and the maximum of a column of doubles in a row group, from its statistics.</summary>
    public (double? Min, double? Max) GetDoubleStatistics(int rowGroup, int column)
    {
        if (Columns[column].PhysicalType is not ParquetColumn.Double || _metadata.RowGroups[rowGroup].Columns[column]?.Statistics is not { } statistics)
            return (null, null);

        return (ToDouble(statistics.MinValue ?? statistics.Min), ToDouble(statistics.MaxValue ?? statistics.Max));

        static double? ToDouble(byte[]? value) => value is { Length: 8 } && BinaryPrimitives.ReadDoubleLittleEndian(value) is var result && !double.IsNaN(result) ? result : null;
    }

    /// <summary>Reads the values of a column in a row group, <see langword="null"/> for a missing value.</summary>
    public async ValueTask<object?[]> ReadColumnAsync(int rowGroup, int column, CancellationToken cancellationToken)
    {
        var leaf = Columns[column];
        if (leaf.MaxRepetitionLevel > 0)
            throw new NotSupportedException($"The column '{string.Join('.', leaf.Path)}' of the Parquet file is repeated, which is not supported.");

        var rowCount = GetRowCount(rowGroup);
        var chunk = _metadata.RowGroups[rowGroup].Columns[column]
            ?? throw new NotSupportedException("A column chunk of the Parquet file is in another file, which is not supported.");
        if (chunk.Type != leaf.PhysicalType)
            throw new FormatException($"The column chunk of '{string.Join('.', leaf.Path)}' does not have the type of its column.");

        // The dictionary page, when there is one, is before the data pages: some writers set its offset to 0 when there is none
        var start = chunk.DictionaryPageOffset is { } dictionaryOffset && dictionaryOffset > 0 && dictionaryOffset < chunk.DataPageOffset ? dictionaryOffset : chunk.DataPageOffset;
        if (start < Magic.Length || chunk.TotalCompressedSize <= 0 || chunk.TotalCompressedSize > _dataLength - start)
            throw new FormatException($"The column chunk of '{string.Join('.', leaf.Path)}' is outside of the data of the Parquet file.");

        var data = new byte[chunk.TotalCompressedSize];
        _stream.Seek(_origin + start, SeekOrigin.Begin);
        await ReadExactlyAsync(_stream, data, cancellationToken).ConfigureAwait(false);
        return ParquetColumnChunkReader.Read(data, leaf, chunk.Codec, rowCount);
    }

    // Walks the schema, flattened depth-first, to find the leaves: an optional field adds a definition level, and a
    // repeated field a definition level and a repetition level
    private static ParquetColumn[] GetLeafColumns(SchemaElement[] schema)
    {
        if (schema.Length is 0)
            throw new FormatException("The Parquet file has no schema.");

        var columns = new List<ParquetColumn>();
        var index = 1;
        for (var i = 0; i < schema[0].NumChildren; i++)
        {
            Visit(schema, ref index, [], 0, 0, columns, depth: 0);
        }

        return [.. columns];

        static void Visit(SchemaElement[] schema, ref int index, string[] parent, int definitionLevel, int repetitionLevel, List<ParquetColumn> columns, int depth)
        {
            if (index >= schema.Length || depth > 64)
                throw new FormatException("The schema of the Parquet file is not valid.");

            var element = schema[index++];
            string[] path = [.. parent, element.Name];
            if (element.RepetitionType is not SchemaElement.RepetitionRequired)
            {
                definitionLevel++;
            }

            if (element.RepetitionType is SchemaElement.RepetitionRepeated)
            {
                repetitionLevel++;
            }

            if (element.NumChildren <= 0)
            {
                columns.Add(new ParquetColumn(path, element, definitionLevel, repetitionLevel));
                return;
            }

            for (var i = 0; i < element.NumChildren; i++)
            {
                Visit(schema, ref index, path, definitionLevel, repetitionLevel, columns, depth + 1);
            }
        }
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) < buffer.Length)
            throw new FormatException("The Parquet file is truncated.");
    }
}
