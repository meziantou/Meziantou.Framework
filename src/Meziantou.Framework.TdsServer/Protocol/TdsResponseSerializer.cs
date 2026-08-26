using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsResponseSerializer
{
    private const ushort MaxVariableColumnLength = 8000;
    private const ushort PartiallyLengthPrefixedMarker = 0xFFFF;
    private static readonly byte[] DefaultCollation = [0x09, 0x04, 0xD0, 0x00, 0x34];

    public static byte[] CreateLoginSuccess(TdsAuthenticationResult authenticationResult)
    {
        ArgumentNullException.ThrowIfNull(authenticationResult);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        WriteLoginAckToken(writer, "Meziantou.TdsServer");
        if (!string.IsNullOrEmpty(authenticationResult.Database))
        {
            WriteEnvironmentChangeToken(writer, environmentType: 1, authenticationResult.Database, oldValue: string.Empty);
        }

        WriteCollationEnvironmentChangeToken(writer, DefaultCollation);
        WriteDoneToken(writer, status: 0x0000, rowCount: 0);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateLoginError(TdsAuthenticationResult authenticationResult)
    {
        ArgumentNullException.ThrowIfNull(authenticationResult);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        WriteErrorToken(
            writer,
            authenticationResult.ErrorNumber,
            authenticationResult.ErrorState,
            authenticationResult.ErrorClass,
            authenticationResult.ErrorMessage ?? "Login failed");

        WriteDoneToken(writer, status: 0x0102, rowCount: 0);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateProtocolError(uint errorNumber, string message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        WriteErrorToken(writer, errorNumber, state: 1, @class: 16, message);
        WriteDoneToken(writer, status: 0x0102, rowCount: 0);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateQueryResponse(TdsQueryResult result, int payloadSizePerPacket)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        if (result.Error is not null)
        {
            WriteErrorToken(writer, result.Error.Number, result.Error.State, result.Error.Class, result.Error.Message);
            WriteDoneToken(writer, status: 0x0102, rowCount: 0);
            writer.Flush();
            return stream.ToArray();
        }

        foreach (var message in result.InfoMessages)
        {
            WriteInfoToken(writer, message);
        }

        for (var i = 0; i < result.ResultSets.Count; i++)
        {
            var resultSet = result.ResultSets[i];
            var hasMoreResults = i + 1 < result.ResultSets.Count;
            WriteResultSet(writer, resultSet, hasMoreResults, payloadSizePerPacket);
        }

        if (result.ResultSets.Count == 0)
        {
            WriteDoneToken(writer, status: 0x0000, rowCount: 0);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateAttentionResponse()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        WriteDoneToken(writer, status: 0x0020, rowCount: 0);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteResultSet(BinaryWriter writer, TdsResultSet resultSet, bool hasMoreResults, int payloadSizePerPacket)
    {
        var usePartialLength = GetPartiallyLengthPrefixedColumns(resultSet);
        WriteColumnMetadataToken(writer, resultSet.Columns, usePartialLength);

        foreach (var row in resultSet.Rows)
        {
            WriteRowToken(writer, resultSet.Columns, usePartialLength, payloadSizePerPacket, row);
        }

        var status = hasMoreResults ? (ushort)0x0001 : (ushort)0x0000;
        WriteDoneToken(writer, status, (ulong)resultSet.Rows.Count);
    }

    /// <summary>
    /// Determines, per column, whether values must be sent partially-length-prefixed because at least one of
    /// them exceeds what a fixed-length column can carry. Short columns keep the cheaper 2-byte framing.
    /// </summary>
    private static bool[] GetPartiallyLengthPrefixedColumns(TdsResultSet resultSet)
    {
        var result = new bool[resultSet.Columns.Count];
        for (var i = 0; i < resultSet.Columns.Count; i++)
        {
            var column = resultSet.Columns[i];
            if (column.ColumnType is TdsColumnType.Json || IsFixedLengthColumn(column.ColumnType))
            {
                continue;
            }

            foreach (var row in resultSet.Rows)
            {
                if (i < row.Count && ExceedsFixedColumnLength(column, row[i]))
                {
                    result[i] = true;
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsFixedLengthColumn(TdsColumnType columnType)
    {
        return columnType is TdsColumnType.TinyInt
            or TdsColumnType.SmallInt
            or TdsColumnType.Int32
            or TdsColumnType.Int64
            or TdsColumnType.Boolean
            or TdsColumnType.Real
            or TdsColumnType.Double;
    }

    private static bool ExceedsFixedColumnLength(TdsColumn column, object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (column.ColumnType is TdsColumnType.Binary)
        {
            return value is byte[] bytes
                ? bytes.Length > MaxVariableColumnLength
                : Encoding.UTF8.GetByteCount(value.ToString() ?? string.Empty) > MaxVariableColumnLength;
        }

        return Encoding.Unicode.GetByteCount(ConvertToSqlText(value, column.ColumnType)) > MaxVariableColumnLength;
    }

    private static void WriteLoginAckToken(BinaryWriter writer, string programName)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write((byte)0x01); // SQL_DFLT
        WriteUInt32BigEndian(bodyWriter, 0x74000004u);
        WriteBVarChar(bodyWriter, programName);
        bodyWriter.Write((byte)1);
        bodyWriter.Write((byte)0);
        bodyWriter.Write((byte)0);
        bodyWriter.Write((byte)0);
        bodyWriter.Flush();

        writer.Write((byte)0xAD);
        writer.Write((ushort)bodyStream.Length);
        writer.Write(bodyStream.ToArray());
    }

    private static void WriteEnvironmentChangeToken(BinaryWriter writer, byte environmentType, string newValue, string oldValue)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write(environmentType);
        WriteBVarChar(bodyWriter, newValue);
        WriteBVarChar(bodyWriter, oldValue);
        bodyWriter.Flush();

        writer.Write((byte)0xE3);
        writer.Write((ushort)bodyStream.Length);
        writer.Write(bodyStream.ToArray());
    }

    private static void WriteInfoToken(BinaryWriter writer, string message)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write((uint)0);
        bodyWriter.Write((byte)1);
        bodyWriter.Write((byte)10);
        WriteUsVarChar(bodyWriter, message);
        WriteBVarChar(bodyWriter, "TdsServer");
        WriteBVarChar(bodyWriter, string.Empty);
        bodyWriter.Write((uint)1);
        bodyWriter.Flush();

        writer.Write((byte)0xAB);
        writer.Write((ushort)bodyStream.Length);
        writer.Write(bodyStream.ToArray());
    }

    private static void WriteErrorToken(BinaryWriter writer, uint number, byte state, byte @class, string message)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write(number);
        bodyWriter.Write(state);
        bodyWriter.Write(@class);
        WriteUsVarChar(bodyWriter, message);
        WriteBVarChar(bodyWriter, "TdsServer");
        WriteBVarChar(bodyWriter, string.Empty);
        bodyWriter.Write((uint)1);
        bodyWriter.Flush();

        writer.Write((byte)0xAA);
        writer.Write((ushort)bodyStream.Length);
        writer.Write(bodyStream.ToArray());
    }

    private static void WriteDoneToken(BinaryWriter writer, ushort status, ulong rowCount)
    {
        writer.Write((byte)0xFD);
        writer.Write(status);
        writer.Write((ushort)0);
        writer.Write(rowCount);
    }

    private static void WriteColumnMetadataToken(BinaryWriter writer, Collection<TdsColumn> columns, bool[] usePartialLength)
    {
        writer.Write((byte)0x81);
        writer.Write((ushort)columns.Count);
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            var declaredLength = usePartialLength[columnIndex] ? PartiallyLengthPrefixedMarker : MaxVariableColumnLength;
            writer.Write((uint)0);
            var flags = column.IsNullable ? (ushort)0x0001 : (ushort)0x0000;
            writer.Write(flags);

            switch (column.ColumnType)
            {
                case TdsColumnType.TinyInt:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x26); // INTN
                        writer.Write((byte)1);
                    }
                    else
                    {
                        writer.Write((byte)0x30); // TINYINT
                    }
                    break;
                case TdsColumnType.SmallInt:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x26); // INTN
                        writer.Write((byte)2);
                    }
                    else
                    {
                        writer.Write((byte)0x34); // SMALLINT
                    }
                    break;
                case TdsColumnType.Int32:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x26); // INTN
                        writer.Write((byte)4);
                    }
                    else
                    {
                        writer.Write((byte)0x38); // INT
                    }
                    break;
                case TdsColumnType.Int64:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x26); // INTN
                        writer.Write((byte)8);
                    }
                    else
                    {
                        writer.Write((byte)0x7F); // BIGINT
                    }
                    break;
                case TdsColumnType.Boolean:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x68); // BITN
                        writer.Write((byte)1);
                    }
                    else
                    {
                        writer.Write((byte)0x32); // BIT
                    }
                    break;
                case TdsColumnType.Real:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x6D); // FLTN
                        writer.Write((byte)4);
                    }
                    else
                    {
                        writer.Write((byte)0x3B); // REAL
                    }
                    break;
                case TdsColumnType.Double:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x6D); // FLTN
                        writer.Write((byte)8);
                    }
                    else
                    {
                        writer.Write((byte)0x3E); // FLOAT
                    }
                    break;
                case TdsColumnType.Binary:
                    writer.Write((byte)0xA5); // VARBINARY
                    writer.Write(declaredLength);
                    break;
                case TdsColumnType.Json:
                    writer.Write((byte)0xF4); // JSON
                    break;
                default:
                    writer.Write((byte)0xE7); // NVARCHAR
                    writer.Write(declaredLength);
                    writer.Write(DefaultCollation);
                    break;
            }

            WriteBVarChar(writer, column.Name);
        }
    }

    private static void WriteRowToken(BinaryWriter writer, Collection<TdsColumn> columns, bool[] usePartialLength, int payloadSizePerPacket, IReadOnlyList<object?> values)
    {
        writer.Write((byte)0xD1);
        for (var i = 0; i < columns.Count; i++)
        {
            var value = i < values.Count ? values[i] : null;
            WriteColumnValue(writer, columns[i], usePartialLength[i], payloadSizePerPacket, value);
        }
    }

    private static void WriteColumnValue(BinaryWriter writer, TdsColumn column, bool usePartialLength, int payloadSizePerPacket, object? value)
    {
        if (value is null)
        {
            WriteNullValue(writer, column, usePartialLength);
            return;
        }

        switch (column.ColumnType)
        {
            case TdsColumnType.TinyInt:
                if (column.IsNullable)
                {
                    writer.Write((byte)1);
                }

                writer.Write(Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TdsColumnType.SmallInt:
                if (column.IsNullable)
                {
                    writer.Write((byte)2);
                }

                writer.Write(Convert.ToInt16(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TdsColumnType.Int32:
                if (column.IsNullable)
                {
                    writer.Write((byte)4);
                }

                writer.Write(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TdsColumnType.Int64:
                if (column.IsNullable)
                {
                    writer.Write((byte)8);
                }

                writer.Write(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TdsColumnType.Boolean:
                if (column.IsNullable)
                {
                    writer.Write((byte)1);
                }

                writer.Write(Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture) ? (byte)1 : (byte)0);
                break;
            case TdsColumnType.Real:
                if (column.IsNullable)
                {
                    writer.Write((byte)4);
                }

                writer.Write(Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TdsColumnType.Double:
                if (column.IsNullable)
                {
                    writer.Write((byte)8);
                }

                writer.Write(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TdsColumnType.Binary:
                var bytes = value as byte[] ?? Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty);
                if (usePartialLength)
                {
                    WritePartiallyLengthPrefixed(writer, bytes, payloadSizePerPacket);
                }
                else
                {
                    writer.Write((ushort)bytes.Length);
                    writer.Write(bytes);
                }

                break;
            case TdsColumnType.Json:
                var json = ConvertToSqlText(value, column.ColumnType);
                WritePartiallyLengthPrefixed(writer, Encoding.UTF8.GetBytes(json), payloadSizePerPacket);
                break;
            default:
                var text = ConvertToSqlText(value, column.ColumnType);
                var payload = Encoding.Unicode.GetBytes(text);
                if (usePartialLength)
                {
                    WritePartiallyLengthPrefixed(writer, payload, payloadSizePerPacket);
                }
                else
                {
                    writer.Write((ushort)payload.Length);
                    writer.Write(payload);
                }

                break;
        }
    }

    private static void WriteCollationEnvironmentChangeToken(BinaryWriter writer, ReadOnlySpan<byte> collation)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write((byte)7);
        bodyWriter.Write((byte)collation.Length);
        bodyWriter.Write(collation);
        bodyWriter.Write((byte)0);
        bodyWriter.Flush();

        writer.Write((byte)0xE3);
        writer.Write((ushort)bodyStream.Length);
        writer.Write(bodyStream.ToArray());
    }

    private static void WriteNullValue(BinaryWriter writer, TdsColumn column, bool usePartialLength)
    {
        if (usePartialLength)
        {
            WritePartiallyLengthPrefixedNull(writer);
            return;
        }

        switch (column.ColumnType)
        {
            case TdsColumnType.TinyInt:
                writer.Write((byte)0);
                break;
            case TdsColumnType.SmallInt:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write((short)0);
                }
                break;
            case TdsColumnType.Int32:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write(0);
                }
                break;
            case TdsColumnType.Int64:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write(0L);
                }
                break;
            case TdsColumnType.Boolean:
                writer.Write((byte)0);
                break;
            case TdsColumnType.Real:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write(0f);
                }
                break;
            case TdsColumnType.Double:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write(0d);
                }
                break;
            case TdsColumnType.Json:
                WritePartiallyLengthPrefixedNull(writer);
                break;
            default:
                if (column.IsNullable)
                {
                    writer.Write((ushort)0xFFFF);
                }
                else
                {
                    writer.Write((ushort)0);
                }
                break;
        }
    }

    private static string ConvertToSqlText(object value, TdsColumnType columnType)
    {
        return columnType switch
        {
            TdsColumnType.Date => value switch
            {
                DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                DateTime dateTime => dateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            },
            TdsColumnType.Time => value switch
            {
                TimeOnly timeOnly => timeOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                TimeSpan timeSpan => timeSpan.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            },
            TdsColumnType.DateTime => value is DateTime dt ? dt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            TdsColumnType.DateTime2 => value is DateTime dt ? dt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            TdsColumnType.DateTimeOffset => value is DateTimeOffset dto ? dto.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            TdsColumnType.Guid => value is Guid guid ? guid.ToString("D") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            TdsColumnType.Xml => value.ToString() ?? string.Empty,
            // The JSON TDS payload is textual JSON encoded as UTF-8.
            TdsColumnType.Json => value switch
            {
                string text => text,
                JsonNode jsonNode => jsonNode.ToJsonString(),
                _ => JsonSerializer.Serialize(value),
            },
            TdsColumnType.Decimal => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
            TdsColumnType.Money => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
            TdsColumnType.SmallMoney => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
            TdsColumnType.UserDefined => value.ToString() ?? string.Empty,
            TdsColumnType.Table => value.ToString() ?? string.Empty,
            TdsColumnType.Variant => value.ToString() ?? string.Empty,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static void WriteUsVarChar(BinaryWriter writer, string value)
    {
        var length = Math.Min(value.Length, ushort.MaxValue);
        writer.Write((ushort)length);
        writer.Write(Encoding.Unicode.GetBytes(value[..length]));
    }

    private static void WriteBVarChar(BinaryWriter writer, string value)
    {
        var length = Math.Min(value.Length, byte.MaxValue);
        writer.Write((byte)length);
        writer.Write(Encoding.Unicode.GetBytes(value[..length]));
    }

    private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WritePartiallyLengthPrefixed(BinaryWriter writer, byte[] payload, int payloadSizePerPacket)
    {
        writer.Write((ulong)payload.Length);

        var offset = 0;
        while (offset < payload.Length)
        {
            // End each chunk on a TDS packet boundary so no chunk is ever continued in the next packet.
            var positionInPacket = (int)(writer.BaseStream.Position % payloadSizePerPacket);
            var remainingInPacket = payloadSizePerPacket - positionInPacket - sizeof(int);
            var count = remainingInPacket > 0 ? remainingInPacket : remainingInPacket + payloadSizePerPacket;
            count = Math.Min(count, payload.Length - offset);

            writer.Write(count);
            writer.Write(payload, offset, count);
            offset += count;
        }

        writer.Write(0);
    }

    private static void WritePartiallyLengthPrefixedNull(BinaryWriter writer)
    {
        writer.Write(ulong.MaxValue);
    }
}
