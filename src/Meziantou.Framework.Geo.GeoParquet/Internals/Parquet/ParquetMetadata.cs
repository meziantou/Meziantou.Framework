namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>
/// The structures of the metadata of a Parquet file (parquet.thrift) that the reader uses, and their Thrift decoding.
/// The fields the reader does not use are skipped.
/// </summary>
internal static class ParquetMetadata
{
    public static FileMetadata ReadFileMetadata(ReadOnlySpan<byte> data)
    {
        var reader = new ThriftCompactReader(data);
        var schema = new List<SchemaElement>();
        var rowGroups = new List<RowGroup>();
        var keyValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var numRows = 0L;
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 2:
                    var schemaCount = reader.ReadListHeader(type, out _);
                    for (var i = 0; i < schemaCount; i++)
                    {
                        schema.Add(ReadSchemaElement(ref reader));
                    }

                    break;

                case 3:
                    numRows = reader.ReadI64(type);
                    break;

                case 4:
                    var rowGroupCount = reader.ReadListHeader(type, out _);
                    for (var i = 0; i < rowGroupCount; i++)
                    {
                        rowGroups.Add(ReadRowGroup(ref reader));
                    }

                    break;

                case 5:
                    var keyValueCount = reader.ReadListHeader(type, out _);
                    for (var i = 0; i < keyValueCount; i++)
                    {
                        var (key, value) = ReadKeyValue(ref reader);
                        if (key is not null && value is not null)
                        {
                            keyValues[key] = value;
                        }
                    }

                    break;

                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return new FileMetadata([.. schema], numRows, [.. rowGroups], keyValues);
    }

    /// <summary>Reads the header of a page, and returns its length.</summary>
    public static PageHeader ReadPageHeader(ReadOnlySpan<byte> data, out int length)
    {
        var reader = new ThriftCompactReader(data);
        var header = new PageHeader();
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    header.Type = reader.ReadI32(type);
                    break;
                case 2:
                    header.UncompressedPageSize = reader.ReadI32(type);
                    break;
                case 3:
                    header.CompressedPageSize = reader.ReadI32(type);
                    break;
                case 5:
                    ReadDataPageHeader(ref reader, header);
                    break;
                case 7:
                    ReadDictionaryPageHeader(ref reader, header);
                    break;
                case 8:
                    ReadDataPageHeaderV2(ref reader, header);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        length = reader.Position;
        return header;
    }

    private static SchemaElement ReadSchemaElement(ref ThriftCompactReader reader)
    {
        var element = new SchemaElement();
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    element.Type = reader.ReadI32(type);
                    break;
                case 2:
                    element.TypeLength = reader.ReadI32(type);
                    break;
                case 3:
                    element.RepetitionType = reader.ReadI32(type);
                    break;
                case 4:
                    element.Name = reader.ReadString(type);
                    break;
                case 5:
                    element.NumChildren = reader.ReadI32(type);
                    break;
                case 6:
                    element.ConvertedType = reader.ReadI32(type);
                    break;
                case 7:
                    element.Scale = reader.ReadI32(type);
                    break;
                case 8:
                    element.Precision = reader.ReadI32(type);
                    break;
                case 10:
                    element.LogicalType = ReadLogicalType(ref reader);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return element;
    }

    // A union: the field that is set gives the kind, and its structure the parameters
    private static LogicalType ReadLogicalType(ref ThriftCompactReader reader)
    {
        var logicalType = new LogicalType();
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            logicalType.Kind = id;
            if (type is not ThriftCompactReader.TypeStruct)
            {
                reader.Skip(type);
                continue;
            }

            reader.BeginStruct();
            short parameterId = 0;
            while (reader.ReadFieldHeader(ref parameterId, out var parameterType))
            {
                switch (id, parameterId)
                {
                    case (LogicalType.Decimal, 1):
                        logicalType.Scale = reader.ReadI32(parameterType);
                        break;
                    case (LogicalType.Decimal, 2):
                        logicalType.Precision = reader.ReadI32(parameterType);
                        break;
                    case (LogicalType.Time or LogicalType.Timestamp, 1):
                        logicalType.IsAdjustedToUtc = ThriftCompactReader.ReadBoolean(parameterType);
                        break;
                    case (LogicalType.Time or LogicalType.Timestamp, 2):
                        logicalType.TimeUnit = ReadTimeUnit(ref reader, parameterType);
                        break;
                    case (LogicalType.Integer, 1):
                        logicalType.BitWidth = reader.ReadI32(parameterType);
                        break;
                    case (LogicalType.Integer, 2):
                        logicalType.IsSigned = ThriftCompactReader.ReadBoolean(parameterType);
                        break;
                    default:
                        reader.Skip(parameterType);
                        break;
                }
            }

            reader.EndStruct();
        }

        reader.EndStruct();
        return logicalType;
    }

    // TimeUnit is a union of empty structures: MILLIS (1), MICROS (2), NANOS (3)
    private static int ReadTimeUnit(ref ThriftCompactReader reader, byte type)
    {
        if (type is not ThriftCompactReader.TypeStruct)
        {
            reader.Skip(type);
            return 0;
        }

        var unit = 0;
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var unitType))
        {
            unit = id;
            reader.Skip(unitType);
        }

        reader.EndStruct();
        return unit;
    }

    private static (string? Key, string? Value) ReadKeyValue(ref ThriftCompactReader reader)
    {
        string? key = null;
        string? value = null;
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    key = reader.ReadString(type);
                    break;
                case 2:
                    value = reader.ReadString(type);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return (key, value);
    }

    private static RowGroup ReadRowGroup(ref ThriftCompactReader reader)
    {
        var columns = new List<ColumnMetadata?>();
        var numRows = 0L;
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    var count = reader.ReadListHeader(type, out _);
                    for (var i = 0; i < count; i++)
                    {
                        columns.Add(ReadColumnChunk(ref reader));
                    }

                    break;

                case 3:
                    numRows = reader.ReadI64(type);
                    break;

                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return new RowGroup([.. columns], numRows);
    }

    // The metadata of a column chunk; a chunk in another file (file_path) is not supported
    private static ColumnMetadata? ReadColumnChunk(ref ThriftCompactReader reader)
    {
        ColumnMetadata? metadata = null;
        var isExternal = false;
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    isExternal = reader.ReadBinary(type).Length > 0;
                    break;
                case 3:
                    metadata = ReadColumnMetadata(ref reader);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return isExternal ? null : metadata;
    }

    private static ColumnMetadata ReadColumnMetadata(ref ThriftCompactReader reader)
    {
        var metadata = new ColumnMetadata();
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    metadata.Type = reader.ReadI32(type);
                    break;
                case 4:
                    metadata.Codec = reader.ReadI32(type);
                    break;
                case 5:
                    metadata.NumValues = reader.ReadI64(type);
                    break;
                case 7:
                    metadata.TotalCompressedSize = reader.ReadI64(type);
                    break;
                case 9:
                    metadata.DataPageOffset = reader.ReadI64(type);
                    break;
                case 11:
                    metadata.DictionaryPageOffset = reader.ReadI64(type);
                    break;
                case 12:
                    metadata.Statistics = ReadStatistics(ref reader);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return metadata;
    }

    private static Statistics ReadStatistics(ref ThriftCompactReader reader)
    {
        var statistics = new Statistics();
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    statistics.Max = reader.ReadBinary(type).ToArray();
                    break;
                case 2:
                    statistics.Min = reader.ReadBinary(type).ToArray();
                    break;
                case 5:
                    statistics.MaxValue = reader.ReadBinary(type).ToArray();
                    break;
                case 6:
                    statistics.MinValue = reader.ReadBinary(type).ToArray();
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
        return statistics;
    }

    private static void ReadDataPageHeader(ref ThriftCompactReader reader, PageHeader header)
    {
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    header.NumValues = reader.ReadI32(type);
                    break;
                case 2:
                    header.Encoding = reader.ReadI32(type);
                    break;
                case 3:
                    header.DefinitionLevelEncoding = reader.ReadI32(type);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
    }

    private static void ReadDictionaryPageHeader(ref ThriftCompactReader reader, PageHeader header)
    {
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    header.NumValues = reader.ReadI32(type);
                    break;
                case 2:
                    header.Encoding = reader.ReadI32(type);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
    }

    private static void ReadDataPageHeaderV2(ref ThriftCompactReader reader, PageHeader header)
    {
        reader.BeginStruct();
        short id = 0;
        while (reader.ReadFieldHeader(ref id, out var type))
        {
            switch (id)
            {
                case 1:
                    header.NumValues = reader.ReadI32(type);
                    break;
                case 4:
                    header.Encoding = reader.ReadI32(type);
                    break;
                case 5:
                    header.DefinitionLevelsByteLength = reader.ReadI32(type);
                    break;
                case 6:
                    header.RepetitionLevelsByteLength = reader.ReadI32(type);
                    break;
                case 7:
                    header.IsCompressed = ThriftCompactReader.ReadBoolean(type);
                    break;
                default:
                    reader.Skip(type);
                    break;
            }
        }

        reader.EndStruct();
    }

    /// <summary>The metadata of the file: its schema, flattened depth-first, its row groups and its key-value metadata.</summary>
    internal sealed record FileMetadata(SchemaElement[] Schema, long NumRows, RowGroup[] RowGroups, Dictionary<string, string> KeyValues);

    /// <summary>A row group: the metadata of its column chunks, in the order of the leaf columns of the schema, and its number of rows.</summary>
    internal sealed record RowGroup(ColumnMetadata?[] Columns, long NumRows);

    internal sealed class SchemaElement
    {
        public const int RepetitionRequired = 0;
        public const int RepetitionOptional = 1;
        public const int RepetitionRepeated = 2;

        public int? Type { get; set; }
        public int TypeLength { get; set; }
        public int RepetitionType { get; set; }
        public string Name { get; set; } = "";
        public int NumChildren { get; set; }
        public int? ConvertedType { get; set; }
        public int Scale { get; set; }
        public int Precision { get; set; }
        public LogicalType? LogicalType { get; set; }
    }

    internal sealed class LogicalType
    {
        public const int String = 1;
        public const int Enum = 4;
        public const int Decimal = 5;
        public const int Date = 6;
        public const int Time = 7;
        public const int Timestamp = 8;
        public const int Integer = 10;
        public const int Json = 12;
        public const int Uuid = 14;

        public const int Millis = 1;
        public const int Micros = 2;
        public const int Nanos = 3;

        public int Kind { get; set; }
        public int Scale { get; set; }
        public int Precision { get; set; }
        public bool IsAdjustedToUtc { get; set; }
        public int TimeUnit { get; set; }
        public int BitWidth { get; set; }
        public bool IsSigned { get; set; } = true;
    }

    internal sealed class ColumnMetadata
    {
        public int Type { get; set; }
        public int Codec { get; set; }
        public long NumValues { get; set; }
        public long TotalCompressedSize { get; set; }
        public long DataPageOffset { get; set; }
        public long? DictionaryPageOffset { get; set; }
        public Statistics? Statistics { get; set; }
    }

    internal sealed class Statistics
    {
        public byte[]? Max { get; set; }
        public byte[]? Min { get; set; }
        public byte[]? MaxValue { get; set; }
        public byte[]? MinValue { get; set; }
    }

    internal sealed class PageHeader
    {
        public const int DataPage = 0;
        public const int DictionaryPage = 2;
        public const int DataPageV2 = 3;

        public int Type { get; set; }
        public int UncompressedPageSize { get; set; }
        public int CompressedPageSize { get; set; }
        public int NumValues { get; set; }
        public int Encoding { get; set; }
        public int DefinitionLevelEncoding { get; set; }
        public int DefinitionLevelsByteLength { get; set; }
        public int RepetitionLevelsByteLength { get; set; }
        public bool IsCompressed { get; set; } = true;
    }
}
