using System.Buffers.Binary;
using System.Numerics;
using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlValueConverter
{
    /// <summary>The PostgreSQL binary date/time epoch. It is 2000-01-01, not the Unix epoch.</summary>
    private static readonly DateTime PostgreSqlEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static object? DecodeParameterValue(uint typeOid, int formatCode, byte[]? rawValue)
    {
        if (rawValue is null)
        {
            return null;
        }

        if (formatCode == 0)
        {
            return DecodeTextValue(typeOid, rawValue);
        }

        return DecodeBinaryValue(typeOid, rawValue);
    }

    public static byte[] EncodeResultValue(PostgreSqlColumnType columnType, object? value, int formatCode)
    {
        if (value is null or DBNull)
        {
            return [];
        }

        return formatCode == 1
            ? EncodeBinaryResultValue(columnType, value)
            : EncodeTextResultValue(columnType, value);
    }

    private static byte[] EncodeTextResultValue(PostgreSqlColumnType columnType, object value)
    {
        var text = columnType switch
        {
            PostgreSqlColumnType.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "t" : "f",
            PostgreSqlColumnType.Int16 => Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            PostgreSqlColumnType.Int32 => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            PostgreSqlColumnType.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            PostgreSqlColumnType.Single => Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            PostgreSqlColumnType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            PostgreSqlColumnType.Numeric => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            PostgreSqlColumnType.Text or PostgreSqlColumnType.VarChar => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            PostgreSqlColumnType.Bytea => EncodeBytea(value),
            PostgreSqlColumnType.Uuid => value switch
            {
                Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
                string textValue => textValue,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            },
            PostgreSqlColumnType.Date => value switch
            {
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            },
            PostgreSqlColumnType.Timestamp => value switch
            {
                DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            },
            PostgreSqlColumnType.TimestampTz => value switch
            {
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss.ffffffK", CultureInfo.InvariantCulture),
                DateTime dateTime => new DateTimeOffset(dateTime, dateTime.Kind == DateTimeKind.Unspecified ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(dateTime)).ToString("yyyy-MM-dd HH:mm:ss.ffffffK", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            },
            PostgreSqlColumnType.Json or PostgreSqlColumnType.Jsonb => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

        return Encoding.UTF8.GetBytes(text);
    }

    private static byte[] EncodeBinaryResultValue(PostgreSqlColumnType columnType, object value)
    {
        switch (columnType)
        {
            case PostgreSqlColumnType.Boolean:
                return [Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? (byte)1 : (byte)0];
            case PostgreSqlColumnType.Int16:
            {
                var buffer = new byte[2];
                BinaryPrimitives.WriteInt16BigEndian(buffer, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                return buffer;
            }

            case PostgreSqlColumnType.Int32:
            {
                var buffer = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(buffer, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                return buffer;
            }

            case PostgreSqlColumnType.Int64:
            {
                var buffer = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buffer, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return buffer;
            }

            case PostgreSqlColumnType.Single:
            {
                var buffer = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(buffer, BitConverter.SingleToInt32Bits(Convert.ToSingle(value, CultureInfo.InvariantCulture)));
                return buffer;
            }

            case PostgreSqlColumnType.Double:
            {
                var buffer = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buffer, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
                return buffer;
            }

            case PostgreSqlColumnType.Numeric:
                return EncodeBinaryNumeric(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
            case PostgreSqlColumnType.Bytea:
                return value as byte[] ?? Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            case PostgreSqlColumnType.Uuid:
            {
                var guid = value switch
                {
                    Guid guidValue => guidValue,
                    string text when Guid.TryParse(text, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => throw new InvalidOperationException($"Value of type '{value.GetType()}' cannot be encoded as a uuid."),
                };

                return guid.ToByteArray(bigEndian: true);
            }

            case PostgreSqlColumnType.Date:
            {
                var date = value switch
                {
                    DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    DateTime dateTime => dateTime,
                    DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
                    _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
                };

                var buffer = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(buffer, (int)(date.Date - PostgreSqlEpoch).TotalDays);
                return buffer;
            }

            case PostgreSqlColumnType.Timestamp:
            case PostgreSqlColumnType.TimestampTz:
            {
                var timestamp = value switch
                {
                    DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
                    DateTime dateTime => dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime,
                    DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
                };

                var microseconds = (timestamp - PostgreSqlEpoch).Ticks / (TimeSpan.TicksPerMillisecond / 1000);
                var buffer = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buffer, microseconds);
                return buffer;
            }

            default:
                // text, varchar, json and jsonb use the same octets in both formats.
                return Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static byte[] EncodeBinaryNumeric(decimal value)
    {
        // Layout: int16 digit count, int16 weight, uint16 sign, int16 display scale, then base-10000 digits.
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0xFF;
        var isNegative = (bits[3] & unchecked((int)0x80000000)) != 0;
        var unscaled = new BigInteger(new[] { bits[0], bits[1], bits[2] }.SelectMany(BitConverter.GetBytes).ToArray(), isUnsigned: true, isBigEndian: false);

        // Align the fraction on a group boundary so the decimal point falls between two base-10000 digits.
        var paddedScale = ((scale + 3) / 4) * 4;
        unscaled *= BigInteger.Pow(10, paddedScale - scale);

        var digits = new List<short>();
        if (unscaled.IsZero)
        {
            digits.Add(0);
        }
        else
        {
            while (!unscaled.IsZero)
            {
                unscaled = BigInteger.DivRem(unscaled, 10000, out var remainder);
                digits.Add((short)remainder);
            }
        }

        digits.Reverse();
        var weight = digits.Count - 1 - (paddedScale / 4);

        // PostgreSQL stores neither leading nor trailing all-zero groups.
        var start = 0;
        while (start < digits.Count - 1 && digits[start] == 0)
        {
            start++;
            weight--;
        }

        var end = digits.Count;
        while (end > start + 1 && digits[end - 1] == 0)
        {
            end--;
        }

        var significant = digits.GetRange(start, end - start);
        if (significant is [0])
        {
            significant.Clear();
            weight = 0;
        }

        var buffer = new byte[8 + (significant.Count * 2)];
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)significant.Count);
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(2, 2), (short)weight);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), isNegative ? (ushort)0x4000 : (ushort)0);
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(6, 2), (short)scale);
        for (var i = 0; i < significant.Count; i++)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(8 + (i * 2), 2), significant[i]);
        }

        return buffer;
    }

    private static object DecodeTextValue(uint typeOid, byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return typeOid switch
        {
            16 => ParseBoolean(text),
            21 => short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int16) ? int16 : text,
            23 => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int32) ? int32 : text,
            20 => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int64) ? int64 : text,
            700 => float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var single) ? single : text,
            701 => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue) ? doubleValue : text,
            1700 => decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var decimalValue) ? decimalValue : text,
            2950 => Guid.TryParse(text, out var guid) ? guid : text,
            1082 => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : text,
            1114 => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ? timestamp : text,
            1184 => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestampTz) ? timestampTz : text,
            _ => text,
        };
    }

    private static object DecodeBinaryValue(uint typeOid, byte[] bytes)
    {
        return typeOid switch
        {
            16 when bytes.Length >= 1 => bytes[0] != 0,
            21 when bytes.Length == 2 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            23 when bytes.Length == 4 => BinaryPrimitives.ReadInt32BigEndian(bytes),
            20 when bytes.Length == 8 => BinaryPrimitives.ReadInt64BigEndian(bytes),
            700 when bytes.Length == 4 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            701 when bytes.Length == 8 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes)),
            1082 when bytes.Length == 4 => DecodeBinaryDate(bytes),
            1114 when bytes.Length == 8 => DecodeBinaryTimestamp(bytes),
            1184 when bytes.Length == 8 => new DateTimeOffset(DecodeBinaryTimestamp(bytes), TimeSpan.Zero),
            1700 => DecodeBinaryNumeric(bytes),
            2950 when bytes.Length == 16 => new Guid(bytes, bigEndian: true),
            25 or 1043 or 114 or 3802 => Encoding.UTF8.GetString(bytes),
            17 => bytes,
            _ => bytes,
        };
    }

    private static object DecodeBinaryDate(byte[] bytes)
    {
        var days = BinaryPrimitives.ReadInt32BigEndian(bytes);
        try
        {
            return DateOnly.FromDateTime(PostgreSqlEpoch.AddDays(days));
        }
        catch (ArgumentOutOfRangeException)
        {
            // PostgreSQL uses sentinel values for infinity, which have no DateOnly representation.
            return bytes;
        }
    }

    private static DateTime DecodeBinaryTimestamp(byte[] bytes)
    {
        var microseconds = BinaryPrimitives.ReadInt64BigEndian(bytes);
        return PostgreSqlEpoch.AddTicks(microseconds * (TimeSpan.TicksPerMillisecond / 1000));
    }

    private static object DecodeBinaryNumeric(byte[] bytes)
    {
        // Layout: int16 digit count, int16 weight, uint16 sign, int16 display scale, then base-10000 digits.
        if (bytes.Length < 8)
        {
            return bytes;
        }

        var digitCount = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0, 2));
        var weight = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2, 2));
        var sign = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
        var scale = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(6, 2));
        if (digitCount < 0 || scale < 0 || bytes.Length < 8 + (digitCount * 2))
        {
            return bytes;
        }

        // 0xC000 is NaN; the infinity sentinels have no decimal representation either.
        if (sign is not 0 and not 0x4000)
        {
            return bytes;
        }

        try
        {
            decimal value = 0;
            for (var i = 0; i < digitCount; i++)
            {
                var digit = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(8 + (i * 2), 2));
                value += digit * Pow10000(weight - i);
            }

            if (sign == 0x4000)
            {
                value = -value;
            }

            return Math.Round(value, Math.Min((int)scale, 28), MidpointRounding.ToEven);
        }
        catch (OverflowException)
        {
            return bytes;
        }
        catch (ArgumentOutOfRangeException)
        {
            return bytes;
        }
    }

    private static decimal Pow10000(int exponent)
    {
        decimal result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10000m;
        }

        for (var i = 0; i > exponent; i--)
        {
            result /= 10000m;
        }

        return result;
    }

    private static object ParseBoolean(string text)
    {
        // PostgreSQL accepts the full set below, case-insensitively and with surrounding whitespace trimmed.
        var value = text.Trim();
        if (value.Equals("t", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("y", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Equals("f", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("n", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.Ordinal))
        {
            return false;
        }

        return text;
    }

    private static string EncodeBytea(object value)
    {
        if (value is byte[] bytes)
        {
            return "\\x" + Convert.ToHexString(bytes).ToLowerInvariant();
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
