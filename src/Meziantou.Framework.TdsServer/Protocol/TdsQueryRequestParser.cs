using System.Buffers.Binary;
using System.Net;
using System.Security.Claims;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsQueryRequestParser
{
    private static readonly DateTime SqlEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>The largest day count that <see cref="DateTime"/> and <see cref="DateOnly"/> can represent.</summary>
    /// <remarks>
    /// Temporal values carry a 24-bit day count, which reaches far beyond year 9999. A value out of range has to
    /// take the same path as any other value the server cannot decode, rather than throwing out of the parser.
    /// </remarks>
    private const int MaxDayCount = 3652058;

    /// <summary>The largest UTC offset, in minutes, that <see cref="DateTimeOffset"/> accepts.</summary>
    private const int MaxOffsetMinutes = 14 * 60;

    public static TdsQueryContext Parse(TdsPacket packet, EndPoint remoteEndPoint, ClaimsPrincipal? userContext)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        return packet.Type switch
        {
            TdsPacketType.SqlBatch => new TdsQueryContext
            {
                RemoteEndPoint = remoteEndPoint,
                RequestType = TdsQueryRequestType.SqlBatch,
                CommandText = DecodeSqlBatchText(packet.Payload),
                UserContext = userContext,
            },
            TdsPacketType.Rpc => CreateRpcContext(packet.Payload, remoteEndPoint, userContext),
            _ => throw new InvalidOperationException($"Unsupported query packet type '{packet.Type}'."),
        };
    }

    private static TdsQueryContext CreateRpcContext(byte[] payload, EndPoint remoteEndPoint, ClaimsPrincipal? userContext)
    {
        var request = TryParseRpc(payload) ?? new TdsRpcRequest
        {
            Parameters = [],
            HasCompleteParameters = false,
        };

        return new TdsQueryContext
        {
            RemoteEndPoint = remoteEndPoint,
            RequestType = TdsQueryRequestType.Rpc,
            ProcedureName = request.ProcedureName,
            Parameters = request.Parameters,
            HasCompleteParameters = request.HasCompleteParameters,
            UserContext = userContext,
        };
    }

    private static TdsRpcRequest? TryParseRpc(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return null;
        }

        var position = GetPayloadOffsetAfterAllHeaders(payload);
        if (position + 4 > payload.Length)
        {
            return null;
        }

        var procedureNameOrMarker = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        string? procedureName;
        if (procedureNameOrMarker == 0xFFFF)
        {
            var procedureId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
            position += 2;

            procedureName = procedureId switch
            {
                10 => "sp_executesql",
                11 => "sp_prepare",
                12 => "sp_execute",
                13 => "sp_prepexec",
                14 => "sp_unprepare",
                _ => $"proc_{procedureId}",
            };
        }
        else
        {
            var byteLength = checked(procedureNameOrMarker * 2);
            if (position + byteLength > payload.Length)
            {
                return null;
            }

            procedureName = Encoding.Unicode.GetString(payload.Slice(position, byteLength));
            position += byteLength;
        }

        if (position + 2 > payload.Length)
        {
            return null;
        }

        position += 2; // Option flags

        var parameters = new List<TdsQueryParameter>();
        var hasCompleteParameters = true;
        while (position < payload.Length)
        {
            var parameter = TryParseParameter(payload, ref position);
            if (parameter is null)
            {
                // The wire length of an undecodable parameter is unknown, so parsing cannot continue past it.
                // Report the truncation instead of silently handing back a short parameter list.
                hasCompleteParameters = false;
                break;
            }

            parameters.Add(parameter);
        }

        return new TdsRpcRequest
        {
            ProcedureName = procedureName,
            Parameters = parameters,
            HasCompleteParameters = hasCompleteParameters,
        };
    }

    private static TdsQueryParameter? TryParseParameter(ReadOnlySpan<byte> payload, ref int position)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var nameLength = payload[position++];
        var nameByteLength = checked(nameLength * 2);
        if (position + nameByteLength + 2 > payload.Length)
        {
            return null;
        }

        var name = Encoding.Unicode.GetString(payload.Slice(position, nameByteLength));
        position += nameByteLength;

        position += 1; // status
        var typeToken = payload[position++];

        return typeToken switch
        {
            0x24 => ParseGuidParameter(payload, ref position, name),
            0x26 => ParseIntNParameter(payload, ref position, name),
            0x28 => ParseDateParameter(payload, ref position, name),
            0x29 => ParseTimeParameter(payload, ref position, name),
            0x2A => ParseDateTime2Parameter(payload, ref position, name),
            0x2B => ParseDateTimeOffsetParameter(payload, ref position, name),
            0x68 => ParseBitNParameter(payload, ref position, name),
            0x6A or 0x6C => ParseDecimalParameter(payload, ref position, name),
            0x6D => ParseFloatNParameter(payload, ref position, name),
            0x6E => ParseMoneyParameter(payload, ref position, name),
            0x6F => ParseDateTimeParameter(payload, ref position, name),
            0xA5 or 0xAD => ParseVarBinaryParameter(payload, ref position, name),
            0xA7 or 0xAF => ParseVarCharParameter(payload, ref position, name),
            0xE7 or 0xEF => ParseNVarCharParameter(payload, ref position, name),
            0xF4 => ParseJsonParameter(payload, ref position, name),
            _ => null,
        };
    }

    private static TdsQueryParameter? ParseIntNParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var maxLength = payload[position++];
        var columnType = GetIntNColumnType(maxLength);
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, columnType);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        object value = valueLength switch
        {
            1 => payload[position],
            2 => BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(position, 2)),
            4 => BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(position, 4)),
            8 => BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(position, 8)),
            _ => payload.Slice(position, valueLength).ToArray(),
        };

        position += valueLength;
        return CreateParameter(name, value, columnType);
    }

    private static TdsQueryParameter? ParseBitNParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        _ = payload[position++]; // max length
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Boolean);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var value = payload[position] != 0;
        position += valueLength;
        return CreateParameter(name, value, TdsColumnType.Boolean);
    }

    private static TdsQueryParameter? ParseFloatNParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var maxLength = payload[position++];
        var columnType = GetFloatNColumnType(maxLength);
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, columnType);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        object value = valueLength switch
        {
            4 => BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(position, 4)),
            8 => BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(position, 8)),
            _ => payload.Slice(position, valueLength).ToArray(),
        };

        position += valueLength;
        return CreateParameter(name, value, columnType);
    }

    private static TdsQueryParameter? ParseNVarCharParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 9 > payload.Length)
        {
            return null;
        }

        var maxLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;
        position += 5; // collation

        if (maxLength == 0xFFFF)
        {
            var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
            if (isNull)
            {
                return CreateParameter(name, rawValue: null, TdsColumnType.NVarChar);
            }

            if (plpPayload is null)
            {
                return null;
            }

            return CreateParameter(name, Encoding.Unicode.GetString(plpPayload), TdsColumnType.NVarChar);
        }

        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (valueLength == 0xFFFF)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.NVarChar);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(payload.Slice(position, valueLength));
        position += valueLength;
        return CreateParameter(name, value, TdsColumnType.NVarChar);
    }

    private static TdsQueryParameter? ParseVarCharParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 9 > payload.Length)
        {
            return null;
        }

        var maxLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;
        position += 5; // collation

        if (maxLength == 0xFFFF)
        {
            var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
            if (isNull)
            {
                return CreateParameter(name, rawValue: null, TdsColumnType.NVarChar);
            }

            if (plpPayload is null)
            {
                return null;
            }

            return CreateParameter(name, Encoding.UTF8.GetString(plpPayload), TdsColumnType.NVarChar);
        }

        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (valueLength == 0xFFFF)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.NVarChar);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var value = Encoding.UTF8.GetString(payload.Slice(position, valueLength));
        position += valueLength;
        return CreateParameter(name, value, TdsColumnType.NVarChar);
    }

    private static TdsQueryParameter? ParseVarBinaryParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 4 > payload.Length)
        {
            return null;
        }

        var maxLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (maxLength == 0xFFFF)
        {
            var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
            if (isNull)
            {
                return CreateParameter(name, rawValue: null, TdsColumnType.Binary);
            }

            if (plpPayload is null)
            {
                return null;
            }

            return CreateParameter(name, plpPayload, TdsColumnType.Binary);
        }

        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (valueLength == 0xFFFF)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Binary);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var bytes = payload.Slice(position, valueLength).ToArray();
        position += valueLength;
        return CreateParameter(name, bytes, TdsColumnType.Binary);
    }

    private static TdsQueryParameter? ParseJsonParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
        if (isNull)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Json);
        }

        if (plpPayload is null)
        {
            return null;
        }

        return CreateParameter(name, Encoding.UTF8.GetString(plpPayload), TdsColumnType.Json);
    }

    private static TdsQueryParameter? ParseGuidParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        _ = payload[position++]; // max length
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Guid);
        }

        if (valueLength != 16 || position + valueLength > payload.Length)
        {
            return null;
        }

        var value = new Guid(payload.Slice(position, 16));
        position += valueLength;
        return CreateParameter(name, value, TdsColumnType.Guid);
    }

    private static TdsQueryParameter? ParseDecimalParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 4 > payload.Length)
        {
            return null;
        }

        _ = payload[position++]; // max length
        _ = payload[position++]; // precision
        var scale = payload[position++];
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Decimal);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var encoded = payload.Slice(position, valueLength);
        position += valueLength;

        // DECIMALN/NUMERICN values are a sign byte followed by a little-endian magnitude.
        var isNegative = encoded[0] == 0;
        var magnitude = encoded[1..];
        if (scale > 28 || !TryReadDecimalMagnitude(magnitude, out var low, out var middle, out var high))
        {
            // The value does not fit in System.Decimal; surface the raw payload rather than losing it.
            return CreateParameter(name, encoded.ToArray(), TdsColumnType.Variant);
        }

        var value = new decimal((int)low, (int)middle, (int)high, isNegative, scale);
        return CreateParameter(name, value, TdsColumnType.Decimal);
    }

    private static bool TryReadDecimalMagnitude(ReadOnlySpan<byte> magnitude, out uint low, out uint middle, out uint high)
    {
        low = 0;
        middle = 0;
        high = 0;

        Span<byte> bits = stackalloc byte[12];
        bits.Clear();
        for (var i = 0; i < magnitude.Length; i++)
        {
            if (i >= bits.Length)
            {
                if (magnitude[i] != 0)
                {
                    return false;
                }

                continue;
            }

            bits[i] = magnitude[i];
        }

        low = BinaryPrimitives.ReadUInt32LittleEndian(bits[..4]);
        middle = BinaryPrimitives.ReadUInt32LittleEndian(bits[4..8]);
        high = BinaryPrimitives.ReadUInt32LittleEndian(bits[8..12]);
        return true;
    }

    private static TdsQueryParameter? ParseMoneyParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var maxLength = payload[position++];
        var columnType = maxLength == 4 ? TdsColumnType.SmallMoney : TdsColumnType.Money;
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, columnType);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var encoded = payload.Slice(position, valueLength);
        position += valueLength;

        // MONEY is stored as an integer number of ten-thousandths, with the high word first.
        var units = valueLength switch
        {
            4 => BinaryPrimitives.ReadInt32LittleEndian(encoded),
            8 => ((long)BinaryPrimitives.ReadInt32LittleEndian(encoded[..4]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(encoded[4..8]),
            _ => (long?)null,
        };

        return units is null
            ? CreateParameter(name, encoded.ToArray(), TdsColumnType.Variant)
            : CreateParameter(name, units.Value / 10000m, columnType);
    }

    private static TdsQueryParameter? ParseDateTimeParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        _ = payload[position++]; // max length
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.DateTime);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var encoded = payload.Slice(position, valueLength);
        position += valueLength;

        // Both forms count days from 1900-01-01; DATETIME uses 1/300s ticks, SMALLDATETIME whole minutes.
        var value = valueLength switch
        {
            4 => SqlEpoch.AddDays(BinaryPrimitives.ReadUInt16LittleEndian(encoded[..2])).AddMinutes(BinaryPrimitives.ReadUInt16LittleEndian(encoded[2..4])),
            8 => TryReadSqlDateTime(encoded),
            _ => (DateTime?)null,
        };

        return value is null
            ? CreateParameter(name, encoded.ToArray(), TdsColumnType.Variant)
            : CreateParameter(name, value.Value, TdsColumnType.DateTime);
    }

    private static TdsQueryParameter? ParseDateParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        // DATE carries no scale byte, only the value length.
        if (position + 1 > payload.Length)
        {
            return null;
        }

        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Date);
        }

        if (valueLength != 3 || position + valueLength > payload.Length)
        {
            return null;
        }

        var days = ReadUInt24LittleEndian(payload.Slice(position, 3));
        if (days > MaxDayCount)
        {
            return null;
        }

        var value = DateOnly.MinValue.AddDays(days);
        position += valueLength;
        return CreateParameter(name, value, TdsColumnType.Date);
    }

    private static TdsQueryParameter? ParseTimeParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var scale = payload[position++];
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.Time);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        if (!TryReadScaledTime(payload.Slice(position, valueLength), scale, out var timeOfDay))
        {
            return null;
        }

        position += valueLength;
        return CreateParameter(name, TimeOnly.FromTimeSpan(timeOfDay), TdsColumnType.Time);
    }

    private static TdsQueryParameter? ParseDateTime2Parameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var scale = payload[position++];
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.DateTime2);
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        if (!TryReadDateTime2(payload.Slice(position, valueLength), scale, out var value))
        {
            return null;
        }

        position += valueLength;
        return CreateParameter(name, value, TdsColumnType.DateTime2);
    }

    private static TdsQueryParameter? ParseDateTimeOffsetParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var scale = payload[position++];
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return CreateParameter(name, rawValue: null, TdsColumnType.DateTimeOffset);
        }

        if (valueLength < 3 || position + valueLength > payload.Length)
        {
            return null;
        }

        var encoded = payload.Slice(position, valueLength);

        // A DATETIMEOFFSET value is a UTC DATETIME2 followed by the offset in signed minutes.
        if (!TryReadDateTime2(encoded[..^2], scale, out var utcValue))
        {
            return null;
        }

        var offsetMinutes = BinaryPrimitives.ReadInt16LittleEndian(encoded[^2..]);
        if (offsetMinutes is < -MaxOffsetMinutes or > MaxOffsetMinutes)
        {
            return null;
        }

        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var localTicks = utcValue.Ticks + offset.Ticks;
        if (localTicks < DateTime.MinValue.Ticks || localTicks > DateTime.MaxValue.Ticks)
        {
            return null;
        }

        position += valueLength;
        return CreateParameter(name, new DateTimeOffset(utcValue, TimeSpan.Zero).ToOffset(offset), TdsColumnType.DateTimeOffset);
    }

    private static bool TryReadDateTime2(ReadOnlySpan<byte> encoded, byte scale, out DateTime value)
    {
        value = default;
        if (encoded.Length < 4)
        {
            return false;
        }

        if (!TryReadScaledTime(encoded[..^3], scale, out var timeOfDay))
        {
            return false;
        }

        var days = ReadUInt24LittleEndian(encoded[^3..]);
        if (days > MaxDayCount)
        {
            return false;
        }

        value = DateTime.MinValue.AddDays(days).Add(timeOfDay);
        return true;
    }

    private static bool TryReadScaledTime(ReadOnlySpan<byte> encoded, byte scale, out TimeSpan value)
    {
        value = default;
        if (scale > 7 || encoded.Length is < 3 or > 5)
        {
            return false;
        }

        ulong units = 0;
        for (var i = encoded.Length - 1; i >= 0; i--)
        {
            units = (units << 8) | encoded[i];
        }

        // The value counts 10^-scale seconds; scale it up to 100ns ticks.
        var ticksPerUnit = 1L;
        for (var i = scale; i < 7; i++)
        {
            ticksPerUnit *= 10;
        }

        var ticks = (long)units * ticksPerUnit;
        if (ticks is < 0 or >= TimeSpan.TicksPerDay)
        {
            return false;
        }

        value = TimeSpan.FromTicks(ticks);
        return true;
    }

    private static DateTime? TryReadSqlDateTime(ReadOnlySpan<byte> encoded)
    {
        var days = BinaryPrimitives.ReadInt32LittleEndian(encoded[..4]);
        var timeOfDayTicks = BinaryPrimitives.ReadUInt32LittleEndian(encoded[4..8]) * TimeSpan.TicksPerSecond / 300;
        if (timeOfDayTicks >= TimeSpan.TicksPerDay)
        {
            return null;
        }

        var daysFromMinValue = (SqlEpoch - DateTime.MinValue).Days + (long)days;
        if (daysFromMinValue is < 0 or > MaxDayCount)
        {
            return null;
        }

        return SqlEpoch.AddDays(days).AddTicks(timeOfDayTicks);
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> value)
    {
        return value[0] | (value[1] << 8) | (value[2] << 16);
    }

    private static byte[]? TryReadPlpPayload(ReadOnlySpan<byte> payload, ref int position, out bool isNull)
    {
        isNull = false;

        if (position + 8 > payload.Length)
        {
            return null;
        }

        var totalLength = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(position, 8));
        position += 8;

        if (totalLength == ulong.MaxValue)
        {
            isNull = true;
            return null;
        }

        using var stream = new MemoryStream();
        while (true)
        {
            if (position + 4 > payload.Length)
            {
                return null;
            }

            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(position, 4));
            position += 4;

            if (chunkLength == 0)
            {
                break;
            }

            if (position + chunkLength > payload.Length)
            {
                return null;
            }

            stream.Write(payload.Slice(position, (int)chunkLength));
            position += (int)chunkLength;
        }

        if (totalLength != ulong.MaxValue - 1 && totalLength != (ulong)stream.Length)
        {
            return null;
        }

        return stream.ToArray();
    }

    private static TdsQueryParameter CreateParameter(string name, object? rawValue, TdsColumnType columnType)
    {
        return new TdsQueryParameter
        {
            Name = name,
            Value = rawValue ?? DBNull.Value,
            Type = columnType,
        };
    }

    private static TdsColumnType GetIntNColumnType(byte maxLength)
    {
        return maxLength switch
        {
            1 => TdsColumnType.TinyInt,
            2 => TdsColumnType.SmallInt,
            4 => TdsColumnType.Int32,
            8 => TdsColumnType.Int64,
            _ => TdsColumnType.Variant,
        };
    }

    private static TdsColumnType GetFloatNColumnType(byte maxLength)
    {
        return maxLength switch
        {
            4 => TdsColumnType.Real,
            8 => TdsColumnType.Double,
            _ => TdsColumnType.Variant,
        };
    }

    private static string DecodeSqlBatchText(byte[] payload)
    {
        return DecodeUnicode(payload.AsSpan(GetPayloadOffsetAfterAllHeaders(payload)));
    }

    /// <summary>
    /// Returns the offset of the payload data that follows the ALL_HEADERS block, or 0 when the
    /// payload does not start with a well-formed one.
    /// </summary>
    /// <remarks>
    /// TDS 7.2 and later prefix SQLBatch and RPC payloads with ALL_HEADERS: a total length
    /// (including itself) followed by headers of the form { length, type, data }. Earlier clients
    /// send the data directly, so the block is validated rather than assumed.
    /// </remarks>
    private static int GetPayloadOffsetAfterAllHeaders(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return 0;
        }

        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
        if (totalLength < 4 || totalLength > (uint)payload.Length)
        {
            return 0;
        }

        // Walk the headers: they must tile the block exactly, otherwise this is not ALL_HEADERS.
        var position = 4u;
        while (position < totalLength)
        {
            if (totalLength - position < 6)
            {
                return 0;
            }

            var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice((int)position, 4));
            if (headerLength < 6 || headerLength > totalLength - position)
            {
                return 0;
            }

            position += headerLength;
        }

        return (int)totalLength;
    }

    private static string DecodeUnicode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(payload);
    }
}
