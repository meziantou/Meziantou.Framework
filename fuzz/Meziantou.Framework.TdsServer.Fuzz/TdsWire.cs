using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.TdsServer.Fuzz;

/// <summary>
/// Builds what a TDS client sends: packets, PRELOGIN and LOGIN7 payloads, SQLBatch and RPC payloads. Decodes what the
/// server answers: the tokens of a tabular result.
/// </summary>
internal static class TdsWire
{
    public const byte SqlBatch = 0x01;
    public const byte Rpc = 0x03;
    public const byte TabularResult = 0x04;
    public const byte Attention = 0x06;
    public const byte Login7 = 0x10;
    public const byte PreLogin = 0x12;

    public const byte EncryptionOff = 0x00;
    public const byte EncryptionNotSupported = 0x02;
    public const byte EncryptionRequired = 0x03;

    /// <summary>The collation SQL Server uses by default (SQL_Latin1_General_CP1_CI_AS: LCID 0x0409, sort id 52, code page 1252).</summary>
    public static ReadOnlySpan<byte> DefaultCollation => [0x09, 0x04, 0xD0, 0x00, 0x34];

    /// <summary>Splits a message into packets of at most <paramref name="chunkSize"/> payload bytes, the last one marked end of message.</summary>
    public static void WriteMessage(List<byte> output, byte packetType, ReadOnlySpan<byte> payload, int chunkSize = int.MaxValue, byte extraStatus = 0)
    {
        chunkSize = Math.Clamp(chunkSize, 1, ushort.MaxValue - 8);
        var packetId = (byte)1;
        var offset = 0;
        Span<byte> header = stackalloc byte[8];
        do
        {
            var count = Math.Min(chunkSize, payload.Length - offset);
            var isLast = offset + count >= payload.Length;
            header[0] = packetType;
            header[1] = (byte)((isLast ? 0x01 : 0x00) | extraStatus);
            BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(count + 8));
            header[6] = packetId++;
            output.AddRange(header);
            output.AddRange(payload.Slice(offset, count));
            offset += count;
        }
        while (offset < payload.Length);
    }

    public static byte[] Message(byte packetType, ReadOnlySpan<byte> payload, int chunkSize = int.MaxValue)
    {
        var output = new List<byte>();
        WriteMessage(output, packetType, payload, chunkSize);
        return [.. output];
    }

    /// <summary>A PRELOGIN payload with the VERSION and ENCRYPTION options.</summary>
    public static byte[] PreLoginPayload(byte encryption)
    {
        return
        [
            0x00, 0x00, 0x0B, 0x00, 0x06, // VERSION at 11, 6 bytes
            0x01, 0x00, 0x11, 0x00, 0x01, // ENCRYPTION at 17, 1 byte
            0xFF,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00,
            encryption,
        ];
    }

    /// <summary>A LOGIN7 payload: the 94-byte fixed part, then the variable-length fields it points to.</summary>
    public static byte[] Login7Payload(string? userName, string? password, string? applicationName, string? database, byte[]? sspi = null)
    {
        var fixedPart = new byte[94];
        var data = new List<byte>();
        BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(4), 0x74000004); // TDS 7.4
        BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(8), 4096); // packet size
        AddString(40, userName, obfuscate: false);
        AddString(44, password, obfuscate: true);
        AddString(48, applicationName, obfuscate: false);
        AddString(68, database, obfuscate: false);
        if (sspi is { Length: > 0 })
        {
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(78), (ushort)(94 + data.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(80), (ushort)sspi.Length);
            data.AddRange(sspi);
        }

        var result = (byte[])[.. fixedPart, .. data];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)result.Length);
        return result;

        void AddString(int offsetIndex, string? value, bool obfuscate)
        {
            if (value is null)
                return;

            var bytes = Encoding.Unicode.GetBytes(value);
            if (obfuscate)
            {
                // LOGIN7 swaps the nibbles of every byte of the password, then XORs it with 0xA5
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)((((bytes[i] << 4) | (bytes[i] >> 4)) & 0xFF) ^ 0xA5);
                }
            }

            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(offsetIndex), (ushort)(94 + data.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(offsetIndex + 2), (ushort)value.Length);
            data.AddRange(bytes);
        }
    }

    /// <summary>The ALL_HEADERS block of TDS 7.2+: a single transaction descriptor header.</summary>
    public static byte[] AllHeaders()
    {
        var result = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 22);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 18);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(18), 1);
        return result;
    }

    public static byte[] SqlBatchPayload(string text, bool withAllHeaders = true)
    {
        return [.. withAllHeaders ? AllHeaders() : [], .. Encoding.Unicode.GetBytes(text)];
    }

    /// <summary>An RPC payload calling a procedure by its well-known id (sp_executesql is 10).</summary>
    public static byte[] RpcPayload(ushort procedureId, params byte[][] parameters)
    {
        var result = new List<byte>(AllHeaders());
        result.AddRange([0xFF, 0xFF]);
        AddUInt16(result, procedureId);
        result.AddRange([0x00, 0x00]); // option flags
        foreach (var parameter in parameters)
        {
            result.AddRange(parameter);
        }

        return [.. result];
    }

    /// <summary>An RPC payload calling a procedure by its name, without ALL_HEADERS (as TDS 7.1 clients do).</summary>
    public static byte[] RpcPayload(string procedureName, params byte[][] parameters)
    {
        var result = new List<byte>();
        AddUInt16(result, (ushort)procedureName.Length);
        result.AddRange(Encoding.Unicode.GetBytes(procedureName));
        result.AddRange([0x00, 0x00]);
        foreach (var parameter in parameters)
        {
            result.AddRange(parameter);
        }

        return [.. result];
    }

    /// <summary>A parameter: its name, its status flags (0x01 is an output parameter), then its TYPE_INFO and value.</summary>
    public static byte[] Parameter(string name, ReadOnlySpan<byte> typeAndValue, byte status = 0)
    {
        return [(byte)name.Length, .. Encoding.Unicode.GetBytes(name), status, .. typeAndValue];
    }

    public static byte[] IntN(int maxLength, long? value)
    {
        if (value is null)
            return [0x26, (byte)maxLength, 0];

        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value.Value);
        return [0x26, (byte)maxLength, (byte)maxLength, .. bytes.AsSpan(0, maxLength)];
    }

    public static byte[] BitN(bool? value) => value is null ? [0x68, 1, 0] : [0x68, 1, 1, value.Value ? (byte)1 : (byte)0];

    public static byte[] FloatN(double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        return [0x6D, 8, 8, .. bytes];
    }

    public static byte[] RealN(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return [0x6D, 4, 4, .. bytes];
    }

    public static byte[] GuidN(Guid value) => [0x24, 16, 16, .. value.ToByteArray()];

    public static byte[] DecimalN(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (byte)((bits[3] >> 16) & 0xFF);
        var magnitude = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(magnitude, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(magnitude.AsSpan(4), bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(magnitude.AsSpan(8), bits[2]);
        return [0x6A, 17, 38, scale, 17, value < 0 ? (byte)0 : (byte)1, .. magnitude];
    }

    public static byte[] MoneyN(decimal value)
    {
        var units = (long)(value * 10000m);
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)(units >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)units);
        return [0x6E, 8, 8, .. bytes];
    }

    public static byte[] SmallMoneyN(decimal value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)(value * 10000m));
        return [0x6E, 4, 4, .. bytes];
    }

    public static byte[] DateTimeN(int days, uint ticks300)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, days);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), ticks300);
        return [0x6F, 8, 8, .. bytes];
    }

    public static byte[] SmallDateTimeN(ushort days, ushort minutes)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, days);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), minutes);
        return [0x6F, 4, 4, .. bytes];
    }

    public static byte[] DateN(DateOnly value) => [0x28, 3, .. UInt24(value.DayNumber)];

    public static byte[] TimeN(TimeOnly value) => [0x29, 7, 5, .. Time(value.Ticks)];

    public static byte[] DateTime2N(DateTime value) => [0x2A, 7, 8, .. Time(value.TimeOfDay.Ticks), .. UInt24(value.Date.Subtract(DateTime.MinValue).Days)];

    public static byte[] DateTimeOffsetN(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var offset = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(offset, (short)value.Offset.TotalMinutes);
        return [0x2B, 7, 10, .. Time(utc.TimeOfDay.Ticks), .. UInt24(utc.Date.Subtract(DateTime.MinValue).Days), .. offset];
    }

    public static byte[] NVarChar(string? value)
    {
        var result = new List<byte> { 0xE7 };
        AddUInt16(result, 8000);
        result.AddRange(DefaultCollation);
        if (value is null)
        {
            AddUInt16(result, 0xFFFF);
        }
        else
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            AddUInt16(result, (ushort)bytes.Length);
            result.AddRange(bytes);
        }

        return [.. result];
    }

    /// <summary>An nvarchar(max) value, sent as a PLP stream split in chunks.</summary>
    public static byte[] NVarCharMax(string value, int chunkSize = 7)
    {
        var result = new List<byte> { 0xE7, 0xFF, 0xFF };
        result.AddRange(DefaultCollation);
        AddPlp(result, Encoding.Unicode.GetBytes(value), chunkSize);
        return [.. result];
    }

    public static byte[] VarChar(ReadOnlySpan<byte> collation, ReadOnlySpan<byte> value)
    {
        var result = new List<byte> { 0xA7 };
        AddUInt16(result, 8000);
        result.AddRange(collation);
        AddUInt16(result, (ushort)value.Length);
        result.AddRange(value);
        return [.. result];
    }

    public static byte[] VarBinary(ReadOnlySpan<byte> value)
    {
        var result = new List<byte> { 0xA5 };
        AddUInt16(result, 8000);
        AddUInt16(result, (ushort)value.Length);
        result.AddRange(value);
        return [.. result];
    }

    public static byte[] VarBinaryMax(ReadOnlySpan<byte> value)
    {
        var result = new List<byte> { 0xA5, 0xFF, 0xFF };
        AddPlp(result, value, chunkSize: 3);
        return [.. result];
    }

    public static byte[] Json(string value)
    {
        var result = new List<byte> { 0xF4 };
        AddPlp(result, Encoding.UTF8.GetBytes(value), chunkSize: 16);
        return [.. result];
    }

    private static void AddPlp(List<byte> result, ReadOnlySpan<byte> value, int chunkSize)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)value.Length);
        result.AddRange(buffer);
        for (var offset = 0; offset < value.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, value.Length - offset);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)count);
            result.AddRange(buffer[..4]);
            result.AddRange(value.Slice(offset, count));
        }

        result.AddRange([0, 0, 0, 0]);
    }

    /// <summary>
    /// Decodes the tokens of a tabular result as a client reads them, failing on anything a client could not read:
    /// an unknown token, a length that overruns the token or the message, a value that does not match its TYPE_INFO.
    /// When <paramref name="payloadSizePerPacket"/> is set, the chunks of a PLP value must not span two packets.
    /// </summary>
    public static DecodedTabularResult ReadTabularResult(ReadOnlySpan<byte> message, int? payloadSizePerPacket = null)
    {
        var result = new DecodedTabularResult();
        var reader = new Reader(message);
        DecodedResultSet? resultSet = null;
        while (!reader.IsEmpty)
        {
            var token = reader.ReadByte();
            switch (token)
            {
                case 0xAA or 0xAB:
                    var body = new Reader(reader.ReadBytes(reader.ReadUInt16()));
                    var number = body.ReadUInt32();
                    _ = body.ReadByte(); // state
                    _ = body.ReadByte(); // class
                    var text = body.ReadUtf16(body.ReadUInt16() * 2);
                    _ = body.ReadUtf16(body.ReadByte() * 2); // server name
                    _ = body.ReadUtf16(body.ReadByte() * 2); // procedure name
                    _ = body.ReadUInt32(); // line number
                    FuzzAssert.True(body.IsEmpty, $"The 0x{token:X2} token is longer than its fields");
                    if (token is 0xAA)
                    {
                        result.Errors.Add((number, text));
                    }
                    else
                    {
                        result.InfoMessages.Add(text);
                    }

                    break;

                case 0xAD or 0xE3:
                    // LOGINACK, ENVCHANGE
                    _ = reader.ReadBytes(reader.ReadUInt16());
                    break;

                case 0x81:
                    FuzzAssert.True(resultSet is null, "A COLMETADATA token must follow the DONE token of the previous result set");
                    resultSet = new DecodedResultSet();
                    var columnCount = reader.ReadUInt16();
                    for (var i = 0; i < columnCount; i++)
                    {
                        _ = reader.ReadUInt32(); // user type
                        var flags = reader.ReadUInt16();
                        var column = ReadTypeInfo(ref reader, (flags & 0x0001) != 0);
                        column.Name = reader.ReadUtf16(reader.ReadByte() * 2);
                        resultSet.Columns.Add(column);
                    }

                    break;

                case 0xD1:
                    FuzzAssert.True(resultSet is not null, "A ROW token must follow a COLMETADATA token");
                    var row = new object?[resultSet.Columns.Count];
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = ReadValue(ref reader, resultSet.Columns[i], payloadSizePerPacket);
                    }

                    resultSet.Rows.Add(row);
                    break;

                case 0xFD or 0xFE or 0xFF:
                    var status = reader.ReadUInt16();
                    _ = reader.ReadUInt16(); // current command
                    var rowCount = reader.ReadUInt64();
                    result.DoneStatuses.Add(status);
                    if (resultSet is not null)
                    {
                        FuzzAssert.Equal((ulong)resultSet.Rows.Count, rowCount, "The DONE token must count the rows of the result set");
                        result.ResultSets.Add(resultSet);
                        resultSet = null;
                    }

                    break;

                default:
                    throw new FuzzAssertionException($"Unexpected token 0x{token:X2} at offset {reader.Position - 1}");
            }
        }

        FuzzAssert.True(resultSet is null, "A result set must end with a DONE token");
        FuzzAssert.True(result.DoneStatuses.Count > 0 && (result.DoneStatuses[^1] & 0x0001) == 0, "A tabular result must end with a final DONE token");
        return result;
    }

    private static DecodedColumn ReadTypeInfo(ref Reader reader, bool isNullable)
    {
        var type = reader.ReadByte();
        var column = new DecodedColumn { Type = type, IsNullable = isNullable };
        switch (type)
        {
            case 0x30 or 0x32:
                column.Length = 1;
                break;
            case 0x34:
                column.Length = 2;
                break;
            case 0x38 or 0x3B or 0x7A:
                column.Length = 4;
                break;
            case 0x7F or 0x3E or 0x3C or 0x3D:
                column.Length = 8;
                break;
            case 0x26 or 0x68 or 0x6D or 0x6E or 0x6F or 0x24:
                column.Length = reader.ReadByte();
                FuzzAssert.True(type switch
                {
                    0x26 => column.Length is 1 or 2 or 4 or 8,
                    0x68 => column.Length is 1,
                    0x6D or 0x6E or 0x6F => column.Length is 4 or 8,
                    _ => column.Length is 16,
                }, $"Invalid length {column.Length} in the TYPE_INFO of type 0x{type:X2}");
                break;
            case 0x6A or 0x6C:
                column.Length = reader.ReadByte();
                column.Precision = reader.ReadByte();
                column.Scale = reader.ReadByte();
                FuzzAssert.True(column.Length is >= 5 and <= 17 && column.Precision is >= 1 and <= 38 && column.Scale <= column.Precision, $"Invalid decimal TYPE_INFO ({column.Length}, {column.Precision}, {column.Scale})");
                break;
            case 0x28:
                break;
            case 0x29 or 0x2A or 0x2B:
                column.Scale = reader.ReadByte();
                FuzzAssert.True(column.Scale <= 7, $"Invalid temporal scale {column.Scale}");
                break;
            case 0xF1:
                FuzzAssert.Equal(0, reader.ReadByte(), "The XML columns have no schema collection");
                column.Length = 0xFFFF;
                break;
            case 0xF4:
                column.Length = 0xFFFF;
                break;
            case 0xA5:
                column.Length = reader.ReadUInt16();
                break;
            case 0xE7:
                column.Length = reader.ReadUInt16();
                _ = reader.ReadBytes(5); // collation
                FuzzAssert.True(column.Length is 0xFFFF || (column.Length <= 8000 && column.Length % 2 == 0), $"Invalid nvarchar length {column.Length}");
                break;
            default:
                throw new FuzzAssertionException($"Unexpected column type 0x{type:X2}");
        }

        return column;
    }

    private static object? ReadValue(ref Reader reader, DecodedColumn column, int? payloadSizePerPacket)
    {
        switch (column.Type)
        {
            case 0x30:
                return (long)reader.ReadByte();
            case 0x32:
                return ReadBit(reader.ReadByte());
            case 0x34:
                return (long)BinaryPrimitives.ReadInt16LittleEndian(reader.ReadBytes(2));
            case 0x38:
                return (long)BinaryPrimitives.ReadInt32LittleEndian(reader.ReadBytes(4));
            case 0x7F:
                return BinaryPrimitives.ReadInt64LittleEndian(reader.ReadBytes(8));
            case 0x3B:
                return BinaryPrimitives.ReadSingleLittleEndian(reader.ReadBytes(4));
            case 0x3E:
                return BinaryPrimitives.ReadDoubleLittleEndian(reader.ReadBytes(8));
            case 0x3C or 0x7A:
                return ReadMoney(reader.ReadBytes(column.Length));
            case 0x3D:
                return ReadDateTime(reader.ReadBytes(8));
            case 0xF1 or 0xF4:
                var document = ReadPlp(ref reader, payloadSizePerPacket);
                return document is null ? null : column.Type is 0xF1 ? ReadUtf16(document) : Encoding.UTF8.GetString(document);
            case 0xA5 or 0xE7:
                byte[]? bytes;
                if (column.Length is 0xFFFF)
                {
                    bytes = ReadPlp(ref reader, payloadSizePerPacket);
                }
                else
                {
                    var length = reader.ReadUInt16();
                    bytes = length is 0xFFFF ? null : reader.ReadBytes(length).ToArray();
                    FuzzAssert.True(bytes is null || bytes.Length <= column.Length, $"A value of {bytes?.Length} bytes overruns the length {column.Length} of its column");
                }

                if (column.Type is 0xA5 || bytes is null)
                    return bytes;

                FuzzAssert.True(bytes.Length % 2 == 0, "An nvarchar value must have an even number of bytes");
                return ReadUtf16(bytes);
        }

        // The types whose value starts with a one-byte length, 0 for NULL
        var valueLength = reader.ReadByte();
        if (valueLength == 0)
            return null;

        var value = reader.ReadBytes(valueLength);
        var expectedLength = column.Type switch
        {
            0x28 => 3,
            0x29 => TimeLength(column.Scale),
            0x2A => TimeLength(column.Scale) + 3,
            0x2B => TimeLength(column.Scale) + 5,
            _ => column.Length,
        };
        FuzzAssert.Equal(expectedLength, (int)valueLength, $"Invalid length of a value of type 0x{column.Type:X2}");
        return column.Type switch
        {
            0x26 => valueLength switch
            {
                1 => (long)value[0],
                2 => (long)BinaryPrimitives.ReadInt16LittleEndian(value),
                4 => (long)BinaryPrimitives.ReadInt32LittleEndian(value),
                _ => BinaryPrimitives.ReadInt64LittleEndian(value),
            },
            0x68 => ReadBit(value[0]),
            0x6D => valueLength is 4 ? BinaryPrimitives.ReadSingleLittleEndian(value) : (object)BinaryPrimitives.ReadDoubleLittleEndian(value),
            0x6E => ReadMoney(value),
            0x6F => valueLength is 8 ? ReadDateTime(value) : throw new FuzzAssertionException("The server does not send smalldatetime values"),
            0x24 => new Guid(value),
            0x6A or 0x6C => ReadDecimal(value, column.Scale),
            0x28 => DateOnly.FromDayNumber(ReadUInt24(value)),
            0x29 => new TimeSpan(ReadTime(value, column.Scale)),
            0x2A => DateTime.MinValue.AddDays(ReadUInt24(value[^3..])).AddTicks(ReadTime(value[..^3], column.Scale)),
            _ => ReadDateTimeOffset(value, column.Scale),
        };
    }

    private static bool ReadBit(byte value)
    {
        FuzzAssert.True(value is 0 or 1, $"Invalid bit value {value}");
        return value == 1;
    }

    private static decimal ReadMoney(ReadOnlySpan<byte> value)
    {
        var units = value.Length is 4
            ? BinaryPrimitives.ReadInt32LittleEndian(value)
            : ((long)BinaryPrimitives.ReadInt32LittleEndian(value) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
        return units / 10000m;
    }

    /// <summary>The days since 1900-01-01 and the 1/300 seconds since midnight.</summary>
    private static DecodedDateTime ReadDateTime(ReadOnlySpan<byte> value)
    {
        var ticks300 = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
        FuzzAssert.True(ticks300 < 300u * 86400, $"Invalid datetime time {ticks300}");
        return new DecodedDateTime(BinaryPrimitives.ReadInt32LittleEndian(value), ticks300);
    }

    /// <summary>A decimal as its sign, its unscaled magnitude and its scale: the magnitude can exceed <see cref="decimal"/>.</summary>
    private static DecodedDecimal ReadDecimal(ReadOnlySpan<byte> value, byte scale)
    {
        FuzzAssert.True(value[0] is 0 or 1, $"Invalid decimal sign {value[0]}");
        Span<byte> magnitude = stackalloc byte[16];
        magnitude.Clear();
        value[1..].CopyTo(magnitude);
        return new DecodedDecimal(value[0] == 0, BinaryPrimitives.ReadUInt128LittleEndian(magnitude), scale);
    }

    private static DateTimeOffset ReadDateTimeOffset(ReadOnlySpan<byte> value, byte scale)
    {
        var offset = BinaryPrimitives.ReadInt16LittleEndian(value[^2..]);
        FuzzAssert.True(offset is >= -840 and <= 840, $"Invalid offset {offset}");
        var utc = DateTime.MinValue.AddDays(ReadUInt24(value[^5..^2])).AddTicks(ReadTime(value[..^5], scale));
        return new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offset));
    }

    private static int TimeLength(byte scale) => scale switch
    {
        <= 2 => 3,
        <= 4 => 4,
        _ => 5,
    };

    private static long ReadTime(ReadOnlySpan<byte> value, byte scale)
    {
        var units = 0L;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            units = (units << 8) | value[i];
        }

        for (var i = scale; i < 7; i++)
        {
            units *= 10;
        }

        FuzzAssert.True(units < TimeSpan.TicksPerDay, $"Invalid time {units}");
        return units;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> value)
    {
        var days = value[0] | (value[1] << 8) | (value[2] << 16);
        FuzzAssert.True(days <= DateOnly.MaxValue.DayNumber, $"Invalid day number {days}");
        return days;
    }

    private static byte[]? ReadPlp(ref Reader reader, int? payloadSizePerPacket)
    {
        var totalLength = reader.ReadUInt64();
        if (totalLength == ulong.MaxValue)
            return null;

        var result = new List<byte>();
        while (true)
        {
            var chunkLength = reader.ReadUInt32();
            if (chunkLength == 0)
                break;

            FuzzAssert.True(chunkLength <= int.MaxValue, "Invalid PLP chunk length");
            var start = reader.Position;
            result.AddRange(reader.ReadBytes((int)chunkLength));
            if (payloadSizePerPacket is { } size)
            {
                FuzzAssert.Equal(start / size, (reader.Position - 1) / size, "A PLP chunk must not span two packets");
            }
        }

        FuzzAssert.True(totalLength == 0xFFFFFFFFFFFFFFFE || totalLength == (ulong)result.Count, $"The PLP chunks hold {result.Count} bytes, not the {totalLength} of the PLP length");
        return [.. result];
    }

    /// <summary>Reads the UTF-16 code units as they are: an unpaired surrogate is valid in UCS-2.</summary>
    private static string ReadUtf16(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
        }

        return new string(chars);
    }

    private static byte[] UInt24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];

    private static byte[] Time(long ticks) => [(byte)ticks, (byte)(ticks >> 8), (byte)(ticks >> 16), (byte)(ticks >> 24), (byte)(ticks >> 32)];

    private static void AddUInt16(List<byte> result, ushort value)
    {
        result.Add((byte)value);
        result.Add((byte)(value >> 8));
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        public readonly bool IsEmpty => Position >= _data.Length;

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            FuzzAssert.True(count >= 0 && count <= _data.Length - Position, $"A field of {count} bytes at offset {Position} overruns the {_data.Length} bytes of the data");
            var result = _data.Slice(Position, count);
            Position += count;
            return result;
        }

        public byte ReadByte() => ReadBytes(1)[0];

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));

        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8));

        public string ReadUtf16(int byteCount) => TdsWire.ReadUtf16(ReadBytes(byteCount));
    }

    /// <summary>The tokens of a tabular result.</summary>
    public sealed class DecodedTabularResult
    {
        public List<(uint Number, string Message)> Errors { get; } = [];

        public List<string> InfoMessages { get; } = [];

        public List<DecodedResultSet> ResultSets { get; } = [];

        public List<ushort> DoneStatuses { get; } = [];
    }

    public sealed class DecodedResultSet
    {
        public List<DecodedColumn> Columns { get; } = [];

        public List<object?[]> Rows { get; } = [];
    }

    public sealed class DecodedColumn
    {
        public string Name { get; set; } = "";

        public byte Type { get; init; }

        public bool IsNullable { get; init; }

        public int Length { get; set; }

        public byte Precision { get; set; }

        public byte Scale { get; set; }
    }

    /// <summary>A decimal value as the wire carries it: the magnitude can exceed <see cref="decimal"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct DecodedDecimal(bool IsNegative, UInt128 Magnitude, byte Scale);

    /// <summary>A datetime value: the days since 1900-01-01 and the 1/300 seconds since midnight.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct DecodedDateTime(int Days, uint Ticks300);
}
