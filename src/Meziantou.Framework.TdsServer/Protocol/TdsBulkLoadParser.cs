using System.Buffers.Binary;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Protocol;

/// <summary>Reads the payload of a BULK LOAD message: a COLMETADATA token, ROW tokens and a DONE token.</summary>
internal static class TdsBulkLoadParser
{
    private const byte ColumnMetadataToken = 0x81;
    private const byte RowToken = 0xD1;
    private const byte DoneToken = 0xFD;
    private const ushort NullableFlag = 0x0001;
    private const ushort UnknownLength = 0xFFFF;

    // The parameter parser reads a TYPE_INFO followed by its value, as RPC parameters are encoded. A row value comes
    // without its TYPE_INFO, so it is decoded behind a copy of the TYPE_INFO of its column, after an empty parameter
    // name and a status byte.
    private const int ParameterPrefixLength = 2;

    private static readonly byte[] DefaultCollation = [0x09, 0x04, 0xD0, 0x00, 0x34];

    public static (IReadOnlyList<TdsColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows) Parse(ReadOnlySpan<byte> payload)
    {
        BulkColumn[]? columns = null;
        var rows = new List<IReadOnlyList<object?>>();
        var position = 0;
        while (position < payload.Length)
        {
            var token = payload[position++];
            switch (token)
            {
                case ColumnMetadataToken when columns is null:
                    columns = ReadColumnMetadata(payload, ref position);
                    break;

                case RowToken when columns is not null:
                    rows.Add(ReadRow(payload, ref position, columns));
                    break;

                case DoneToken:
                    // The client ends the rows with a DONE token, and nothing follows it
                    return (columns?.Select(column => column.Column).ToArray() ?? [], rows);

                default:
                    throw new InvalidDataException($"Unexpected token 0x{token:X2} in the bulk load data.");
            }
        }

        throw new InvalidDataException("The bulk load data does not end with a DONE token.");
    }

    private static BulkColumn[] ReadColumnMetadata(ReadOnlySpan<byte> payload, ref int position)
    {
        var count = ReadUInt16(payload, ref position);
        var columns = new BulkColumn[count];
        for (var i = 0; i < count; i++)
        {
            _ = ReadBytes(payload, ref position, 4); // user type
            var flags = ReadUInt16(payload, ref position);
            var typeToken = ReadBytes(payload, ref position, 1)[0];
            var (typeInfo, valueKind, fixedLength, columnType) = ReadTypeInfo(payload, ref position, typeToken);
            var nameLength = ReadBytes(payload, ref position, 1)[0];
            var name = Encoding.Unicode.GetString(ReadBytes(payload, ref position, nameLength * 2));
            columns[i] = new BulkColumn(new TdsColumn(name.Length > 0 ? name : $"Column{i + 1}", columnType, (flags & NullableFlag) != 0), typeInfo, valueKind, fixedLength);
        }

        return columns;
    }

    /// <summary>Reads a TYPE_INFO, and returns it as the parameter parser expects it, with the framing of its values.</summary>
    private static (byte[] TypeInfo, ValueKind ValueKind, int FixedLength, TdsColumnType ColumnType) ReadTypeInfo(ReadOnlySpan<byte> payload, ref int position, byte typeToken)
    {
        switch (typeToken)
        {
            // Fixed-length types, for columns that are not nullable. The parameter parser knows their nullable
            // counterparts, whose values carry their length.
            case 0x30: return Fixed(0x26, 1, TdsColumnType.TinyInt);
            case 0x34: return Fixed(0x26, 2, TdsColumnType.SmallInt);
            case 0x38: return Fixed(0x26, 4, TdsColumnType.Int32);
            case 0x7F: return Fixed(0x26, 8, TdsColumnType.Int64);
            case 0x32: return Fixed(0x68, 1, TdsColumnType.Boolean);
            case 0x3B: return Fixed(0x6D, 4, TdsColumnType.Real);
            case 0x3E: return Fixed(0x6D, 8, TdsColumnType.Double);
            case 0x7A: return Fixed(0x6E, 4, TdsColumnType.SmallMoney);
            case 0x3C: return Fixed(0x6E, 8, TdsColumnType.Money);
            case 0x3A: return Fixed(0x6F, 4, TdsColumnType.DateTime);
            case 0x3D: return Fixed(0x6F, 8, TdsColumnType.DateTime);

            case 0x26 or 0x68 or 0x6D or 0x6E or 0x6F or 0x24:
                var maxLength = ReadBytes(payload, ref position, 1)[0];
                var columnType = (typeToken, maxLength) switch
                {
                    (0x26, 1) => TdsColumnType.TinyInt,
                    (0x26, 2) => TdsColumnType.SmallInt,
                    (0x26, 4) => TdsColumnType.Int32,
                    (0x26, 8) => TdsColumnType.Int64,
                    (0x68, _) => TdsColumnType.Boolean,
                    (0x6D, 4) => TdsColumnType.Real,
                    (0x6D, _) => TdsColumnType.Double,
                    (0x6E, 4) => TdsColumnType.SmallMoney,
                    (0x6E, _) => TdsColumnType.Money,
                    (0x6F, _) => TdsColumnType.DateTime,
                    (0x24, _) => TdsColumnType.Guid,
                    _ => throw new InvalidDataException($"Invalid length {maxLength} for the type 0x{typeToken:X2}."),
                };

                return ([typeToken, maxLength], ValueKind.ByteLength, 0, columnType);

            case 0x6A or 0x6C:
                return ([typeToken, .. ReadBytes(payload, ref position, 3)], ValueKind.ByteLength, 0, TdsColumnType.Decimal);

            case 0x28:
                return ([typeToken], ValueKind.ByteLength, 0, TdsColumnType.Date);

            case 0x29 or 0x2A or 0x2B:
                var scale = ReadBytes(payload, ref position, 1)[0];
                return ([typeToken, scale], ValueKind.ByteLength, 0, typeToken switch
                {
                    0x29 => TdsColumnType.Time,
                    0x2A => TdsColumnType.DateTime2,
                    _ => TdsColumnType.DateTimeOffset,
                });

            case 0xA5 or 0xAD:
                var binaryLength = ReadBytes(payload, ref position, 2);
                return ([typeToken, .. binaryLength], GetVariableValueKind(binaryLength), 0, TdsColumnType.Binary);

            case 0xA7 or 0xAF or 0xE7 or 0xEF:
                var textInfo = ReadBytes(payload, ref position, 2 + TdsCollation.Size);
                var textValueKind = GetVariableValueKind(textInfo[..2]);
                if (textValueKind == ValueKind.PartiallyLengthPrefixed && !textInfo[2..].ContainsAnyExcept((byte)0))
                {
                    // SqlClient describes xml columns as nvarchar(max) and json columns as varchar(max), with no collation
                    return typeToken == 0xA7
                        ? ([0xF4], textValueKind, 0, TdsColumnType.Json)
                        : ([typeToken, 0xFF, 0xFF, .. DefaultCollation], textValueKind, 0, TdsColumnType.Xml);
                }

                return ([typeToken, .. textInfo], textValueKind, 0, TdsColumnType.NVarChar);

            case 0xF1:
                // XML values are UTF-16 PLP data, as nvarchar(max) values are
                if (ReadBytes(payload, ref position, 1)[0] != 0)
                {
                    throw new InvalidDataException("XML schema collections are not supported in bulk loads.");
                }

                return ([0xE7, 0xFF, 0xFF, .. DefaultCollation], ValueKind.PartiallyLengthPrefixed, 0, TdsColumnType.Xml);

            case 0xF4:
                return ([typeToken], ValueKind.PartiallyLengthPrefixed, 0, TdsColumnType.Json);

            default:
                throw new InvalidDataException($"The type 0x{typeToken:X2} is not supported in bulk loads.");
        }

        static (byte[] TypeInfo, ValueKind ValueKind, int FixedLength, TdsColumnType ColumnType) Fixed(byte nullableToken, byte length, TdsColumnType columnType)
        {
            return ([nullableToken, length], ValueKind.Fixed, length, columnType);
        }
    }

    private static ValueKind GetVariableValueKind(ReadOnlySpan<byte> maxLength)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(maxLength) == UnknownLength ? ValueKind.PartiallyLengthPrefixed : ValueKind.UInt16Length;
    }

    private static object?[] ReadRow(ReadOnlySpan<byte> payload, ref int position, BulkColumn[] columns)
    {
        var row = new object?[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            var start = position;
            byte[] value;
            if (column.ValueKind == ValueKind.Fixed)
            {
                // A fixed-length value has no length, which the nullable TYPE_INFO used to decode it expects
                value = [(byte)column.FixedLength, .. ReadBytes(payload, ref position, column.FixedLength)];
            }
            else
            {
                SkipValue(payload, ref position, column.ValueKind);
                value = payload[start..position].ToArray();
            }

            var buffer = new byte[ParameterPrefixLength + column.TypeInfo.Length + value.Length];
            column.TypeInfo.CopyTo(buffer, ParameterPrefixLength);
            value.CopyTo(buffer, ParameterPrefixLength + column.TypeInfo.Length);

            var parameterPosition = 0;
            var parameter = TdsQueryRequestParser.TryParseParameter(buffer, ref parameterPosition)
                ?? throw new InvalidDataException($"Cannot decode the value of the column '{column.Column.Name}'.");
            row[i] = parameter.Value is DBNull ? null : parameter.Value;
        }

        return row;
    }

    private static void SkipValue(ReadOnlySpan<byte> payload, ref int position, ValueKind valueKind)
    {
        switch (valueKind)
        {
            case ValueKind.ByteLength:
                var length = ReadBytes(payload, ref position, 1)[0];
                _ = ReadBytes(payload, ref position, length);
                break;

            case ValueKind.UInt16Length:
                var variableLength = ReadUInt16(payload, ref position);
                if (variableLength != UnknownLength)
                {
                    _ = ReadBytes(payload, ref position, variableLength);
                }

                break;

            case ValueKind.PartiallyLengthPrefixed:
                var totalLength = BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(payload, ref position, 8));
                if (totalLength == ulong.MaxValue)
                {
                    break; // NULL
                }

                while (true)
                {
                    var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(payload, ref position, 4));
                    if (chunkLength == 0)
                    {
                        break;
                    }

                    if (chunkLength > int.MaxValue)
                    {
                        throw new InvalidDataException("Invalid PLP chunk length.");
                    }

                    _ = ReadBytes(payload, ref position, (int)chunkLength);
                }

                break;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> payload, ref int position)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(payload, ref position, 2));
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> payload, ref int position, int length)
    {
        if (length > payload.Length - position)
        {
            throw new InvalidDataException("The bulk load data is truncated.");
        }

        var bytes = payload.Slice(position, length);
        position += length;
        return bytes;
    }

    private enum ValueKind
    {
        ByteLength,
        UInt16Length,
        PartiallyLengthPrefixed,
        Fixed,
    }

    private sealed record BulkColumn(TdsColumn Column, byte[] TypeInfo, ValueKind ValueKind, int FixedLength);
}
