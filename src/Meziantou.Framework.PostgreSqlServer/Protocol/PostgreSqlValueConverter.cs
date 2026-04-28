using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlValueConverter
{
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

    public static byte[] EncodeResultValue(PostgreSqlColumnType columnType, object? value)
    {
        if (value is null)
        {
            return [];
        }

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

    private static object DecodeTextValue(uint typeOid, byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return typeOid switch
        {
            16 => string.Equals(text, "t", StringComparison.OrdinalIgnoreCase),
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
            25 or 1043 or 114 or 3802 => Encoding.UTF8.GetString(bytes),
            17 => bytes,
            _ => bytes,
        };
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
