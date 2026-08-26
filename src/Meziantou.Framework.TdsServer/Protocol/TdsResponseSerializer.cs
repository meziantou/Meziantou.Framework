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
    private const byte TemporalScale = 7; // matches the resolution of DateTime/TimeSpan
    private const byte DecimalPrecision = 38;
    private const byte DecimalMaxLength = 17; // 1 sign byte + 16 magnitude bytes

    // A token's length field is 16 bits, so a message has to leave room for the rest of the token body.
    private const int MaxTokenMessageLength = 32000;

    private static readonly DateTime SqlEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
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
        var encodings = GetColumnEncodings(resultSet);
        WriteColumnMetadataToken(writer, resultSet.Columns, encodings);

        foreach (var row in resultSet.Rows)
        {
            WriteRowToken(writer, resultSet.Columns, encodings, payloadSizePerPacket, row);
        }

        var status = hasMoreResults ? (ushort)0x0001 : (ushort)0x0000;
        WriteDoneToken(writer, status, (ulong)resultSet.Rows.Count);
    }

    /// <summary>
    /// Determines, per column, whether values must be sent partially-length-prefixed because at least one of
    /// them exceeds what a fixed-length column can carry. Short columns keep the cheaper 2-byte framing.
    /// </summary>
    private static ColumnEncoding[] GetColumnEncodings(TdsResultSet resultSet)
    {
        var result = new ColumnEncoding[resultSet.Columns.Count];
        for (var i = 0; i < resultSet.Columns.Count; i++)
        {
            var column = resultSet.Columns[i];
            if (column.ColumnType is TdsColumnType.Decimal)
            {
                result[i] = new ColumnEncoding(UsePartialLength: false, GetDecimalScale(resultSet, i));
                continue;
            }

            if (!UsesTextEncoding(column.ColumnType) && column.ColumnType is not TdsColumnType.Binary)
            {
                continue;
            }

            foreach (var row in resultSet.Rows)
            {
                if (i < row.Count && ExceedsFixedColumnLength(column, row[i]))
                {
                    result[i] = new ColumnEncoding(UsePartialLength: true, Scale: 0);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A decimal column carries a single scale for every row, so use the largest scale present. .NET decimals
    /// track their own scale, and values are rescaled to the column's when written.
    /// </summary>
    private static byte GetDecimalScale(TdsResultSet resultSet, int columnIndex)
    {
        byte scale = 0;
        foreach (var row in resultSet.Rows)
        {
            if (columnIndex >= row.Count || row[columnIndex] is null)
            {
                continue;
            }

            var value = Convert.ToDecimal(row[columnIndex], CultureInfo.InvariantCulture);
            var valueScale = (byte)((decimal.GetBits(value)[3] >> 16) & 0xFF);
            if (valueScale > scale)
            {
                scale = valueScale;
            }
        }

        return scale;
    }

    /// <summary>Gets a value indicating whether the column has no dedicated TDS type and is sent as text.</summary>
    private static bool UsesTextEncoding(TdsColumnType columnType)
    {
        return columnType is TdsColumnType.NVarChar
            or TdsColumnType.Variant
            or TdsColumnType.UserDefined
            or TdsColumnType.Table;
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

        WriteToken(writer, token: 0xAD, bodyStream);
    }

    private static void WriteEnvironmentChangeToken(BinaryWriter writer, byte environmentType, string newValue, string oldValue)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write(environmentType);
        WriteBVarChar(bodyWriter, newValue);
        WriteBVarChar(bodyWriter, oldValue);
        bodyWriter.Flush();

        WriteToken(writer, token: 0xE3, bodyStream);
    }

    private static void WriteInfoToken(BinaryWriter writer, string message)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write((uint)0);
        bodyWriter.Write((byte)1);
        bodyWriter.Write((byte)10);
        WriteUsVarChar(bodyWriter, Truncate(message, MaxTokenMessageLength));
        WriteBVarChar(bodyWriter, "TdsServer");
        WriteBVarChar(bodyWriter, string.Empty);
        bodyWriter.Write((uint)1);
        bodyWriter.Flush();

        WriteToken(writer, token: 0xAB, bodyStream);
    }

    private static void WriteErrorToken(BinaryWriter writer, uint number, byte state, byte @class, string message)
    {
        using var bodyStream = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyStream, Encoding.Unicode, leaveOpen: true);
        bodyWriter.Write(number);
        bodyWriter.Write(state);
        bodyWriter.Write(@class);
        WriteUsVarChar(bodyWriter, Truncate(message, MaxTokenMessageLength));
        WriteBVarChar(bodyWriter, "TdsServer");
        WriteBVarChar(bodyWriter, string.Empty);
        bodyWriter.Write((uint)1);
        bodyWriter.Flush();

        WriteToken(writer, token: 0xAA, bodyStream);
    }

    private static void WriteDoneToken(BinaryWriter writer, ushort status, ulong rowCount)
    {
        writer.Write((byte)0xFD);
        writer.Write(status);
        writer.Write((ushort)0);
        writer.Write(rowCount);
    }

    private static void WriteColumnMetadataToken(BinaryWriter writer, Collection<TdsColumn> columns, ColumnEncoding[] encodings)
    {
        writer.Write((byte)0x81);
        writer.Write((ushort)columns.Count);
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            var encoding = encodings[columnIndex];
            var declaredLength = encoding.UsePartialLength ? PartiallyLengthPrefixedMarker : MaxVariableColumnLength;
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
                case TdsColumnType.Decimal:
                    writer.Write((byte)0x6A); // DECIMALN
                    writer.Write(DecimalMaxLength);
                    writer.Write(DecimalPrecision);
                    writer.Write(encoding.Scale);
                    break;
                case TdsColumnType.Money:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x6E); // MONEYN
                        writer.Write((byte)8);
                    }
                    else
                    {
                        writer.Write((byte)0x3C); // MONEY
                    }
                    break;
                case TdsColumnType.SmallMoney:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x6E); // MONEYN
                        writer.Write((byte)4);
                    }
                    else
                    {
                        writer.Write((byte)0x7A); // SMALLMONEY
                    }
                    break;
                case TdsColumnType.Guid:
                    writer.Write((byte)0x24); // GUIDN
                    writer.Write((byte)16);
                    break;
                case TdsColumnType.Date:
                    writer.Write((byte)0x28); // DATEN carries no scale
                    break;
                case TdsColumnType.Time:
                    writer.Write((byte)0x29); // TIMEN
                    writer.Write(TemporalScale);
                    break;
                case TdsColumnType.DateTime:
                    if (column.IsNullable)
                    {
                        writer.Write((byte)0x6F); // DATETIMN
                        writer.Write((byte)8);
                    }
                    else
                    {
                        writer.Write((byte)0x3D); // DATETIME
                    }
                    break;
                case TdsColumnType.DateTime2:
                    writer.Write((byte)0x2A); // DATETIME2N
                    writer.Write(TemporalScale);
                    break;
                case TdsColumnType.DateTimeOffset:
                    writer.Write((byte)0x2B); // DATETIMEOFFSETN
                    writer.Write(TemporalScale);
                    break;
                case TdsColumnType.Xml:
                    writer.Write((byte)0xF1); // XML
                    writer.Write((byte)0); // no schema collection
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

    private static void WriteRowToken(BinaryWriter writer, Collection<TdsColumn> columns, ColumnEncoding[] encodings, int payloadSizePerPacket, IReadOnlyList<object?> values)
    {
        writer.Write((byte)0xD1);
        for (var i = 0; i < columns.Count; i++)
        {
            var value = i < values.Count ? values[i] : null;
            WriteColumnValue(writer, columns[i], encodings[i], payloadSizePerPacket, value);
        }
    }

    private static void WriteColumnValue(BinaryWriter writer, TdsColumn column, ColumnEncoding encoding, int payloadSizePerPacket, object? value)
    {
        if (value is null)
        {
            WriteNullValue(writer, column, encoding);
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
            case TdsColumnType.Decimal:
                WriteDecimalValue(writer, Convert.ToDecimal(value, CultureInfo.InvariantCulture), encoding.Scale);
                break;
            case TdsColumnType.Money:
                if (column.IsNullable)
                {
                    writer.Write((byte)8);
                }

                var money = (long)(Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 10000m);
                writer.Write((int)(money >> 32));
                writer.Write((uint)money);
                break;
            case TdsColumnType.SmallMoney:
                if (column.IsNullable)
                {
                    writer.Write((byte)4);
                }

                writer.Write((int)(Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 10000m));
                break;
            case TdsColumnType.Guid:
                writer.Write((byte)16);
                writer.Write(ToGuid(value).ToByteArray());
                break;
            case TdsColumnType.Date:
                writer.Write((byte)3);
                WriteUInt24LittleEndian(writer, ToDateTime(value).Subtract(DateTime.MinValue).Days);
                break;
            case TdsColumnType.Time:
                writer.Write((byte)5);
                WriteScaledTime(writer, ToTimeSpan(value));
                break;
            case TdsColumnType.DateTime:
                if (column.IsNullable)
                {
                    writer.Write((byte)8);
                }

                var dateTime = ToDateTime(value);
                var wholeDays = (int)(dateTime.Date - SqlEpoch).TotalDays;
                writer.Write(wholeDays);
                writer.Write((uint)(dateTime.TimeOfDay.Ticks * 300 / TimeSpan.TicksPerSecond));
                break;
            case TdsColumnType.DateTime2:
                writer.Write((byte)8);
                WriteDateTime2Value(writer, ToDateTime(value));
                break;
            case TdsColumnType.DateTimeOffset:
                writer.Write((byte)10);
                var offsetValue = ToDateTimeOffset(value);
                WriteDateTime2Value(writer, offsetValue.UtcDateTime);
                writer.Write((short)offsetValue.Offset.TotalMinutes);
                break;
            case TdsColumnType.Xml:
                WritePartiallyLengthPrefixed(writer, Encoding.Unicode.GetBytes(value.ToString() ?? string.Empty), payloadSizePerPacket);
                break;
            case TdsColumnType.Binary:
                var bytes = value as byte[] ?? Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty);
                if (encoding.UsePartialLength)
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
                if (encoding.UsePartialLength)
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

        WriteToken(writer, token: 0xE3, bodyStream);
    }

    private static void WriteNullValue(BinaryWriter writer, TdsColumn column, ColumnEncoding encoding)
    {
        if (encoding.UsePartialLength)
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
            case TdsColumnType.Xml:
                WritePartiallyLengthPrefixedNull(writer);
                break;
            case TdsColumnType.Money:
            case TdsColumnType.DateTime:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write(0L);
                }
                break;
            case TdsColumnType.SmallMoney:
                if (column.IsNullable)
                {
                    writer.Write((byte)0);
                }
                else
                {
                    writer.Write(0);
                }
                break;
            case TdsColumnType.Decimal:
            case TdsColumnType.Guid:
            case TdsColumnType.Date:
            case TdsColumnType.Time:
            case TdsColumnType.DateTime2:
            case TdsColumnType.DateTimeOffset:
                writer.Write((byte)0);
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

    private static void WriteToken(BinaryWriter writer, byte token, MemoryStream bodyStream)
    {
        writer.Write(token);
        writer.Write(checked((ushort)bodyStream.Length));
        writer.Write(bodyStream.ToArray());
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        // Do not split a surrogate pair, which would leave an unpaired surrogate in the message.
        var length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length];
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

    private static void WriteDecimalValue(BinaryWriter writer, decimal value, byte scale)
    {
        var isNegative = value < 0;
        var valueBits = decimal.GetBits(Math.Abs(value));
        var valueScale = (byte)((valueBits[3] >> 16) & 0xFF);

        // The wire format carries an unscaled integer, so take the mantissa and lift it to the column's scale.
        var mantissa = new decimal(valueBits[0], valueBits[1], valueBits[2], isNegative: false, scale: 0);
        for (var i = valueScale; i < scale; i++)
        {
            mantissa *= 10m;
        }

        var bits = decimal.GetBits(mantissa);
        writer.Write(DecimalMaxLength);
        writer.Write(isNegative ? (byte)0 : (byte)1);
        writer.Write(bits[0]);
        writer.Write(bits[1]);
        writer.Write(bits[2]);
        writer.Write(0); // the magnitude is at most 96 bits, so the top 4 bytes are always zero
    }

    private static void WriteDateTime2Value(BinaryWriter writer, DateTime value)
    {
        WriteScaledTime(writer, value.TimeOfDay);
        WriteUInt24LittleEndian(writer, value.Subtract(DateTime.MinValue).Days);
    }

    private static void WriteScaledTime(BinaryWriter writer, TimeSpan value)
    {
        // TemporalScale is 7, so one unit is one tick.
        var ticks = value.Ticks;
        writer.Write((byte)ticks);
        writer.Write((byte)(ticks >> 8));
        writer.Write((byte)(ticks >> 16));
        writer.Write((byte)(ticks >> 24));
        writer.Write((byte)(ticks >> 32));
    }

    private static void WriteUInt24LittleEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)value);
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value >> 16));
    }

    private static Guid ToGuid(object value)
    {
        return value as Guid? ?? Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    private static DateTime ToDateTime(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime,
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset dateTimeOffset => dateTimeOffset.DateTime,
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
        };
    }

    private static DateTimeOffset ToDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(dateTime, TimeSpan.Zero),
            _ => DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        };
    }

    private static TimeSpan ToTimeSpan(object value)
    {
        return value switch
        {
            TimeSpan timeSpan => timeSpan,
            TimeOnly timeOnly => timeOnly.ToTimeSpan(),
            DateTime dateTime => dateTime.TimeOfDay,
            _ => TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        };
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

    private readonly record struct ColumnEncoding(bool UsePartialLength, byte Scale);
}
