using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>Converts the bytes of a property to a .NET value, and back.</summary>
internal static class PropertyValueCodec
{
    private static readonly DateTime MinFileTime = DateTime.FromFileTimeUtc(0);

    public static object? Decode(MapiPropertyType type, ReadOnlySpan<byte> data, Encoding string8Encoding)
    {
        if (PropertyTypes.IsMultiValued(type))
        {
            return DecodeMultiple(type, data, string8Encoding);
        }

        var fixedSize = PropertyTypes.GetFixedSize(type);
        if (fixedSize > 0 && data.Length < fixedSize)
        {
            throw new InvalidPstException($"A value of type {type} has {data.Length} bytes instead of {fixedSize}.");
        }

        return type switch
        {
            MapiPropertyType.Null => null,
            MapiPropertyType.Integer16 => BinaryPrimitives.ReadInt16LittleEndian(data),
            MapiPropertyType.Integer32 or MapiPropertyType.ErrorCode => BinaryPrimitives.ReadInt32LittleEndian(data),
            MapiPropertyType.Floating32 => BinaryPrimitives.ReadSingleLittleEndian(data),
            MapiPropertyType.Floating64 => BinaryPrimitives.ReadDoubleLittleEndian(data),
            MapiPropertyType.Currency => (decimal)BinaryPrimitives.ReadInt64LittleEndian(data) / 10000m,
            MapiPropertyType.FloatingTime => DecodeOleDate(BinaryPrimitives.ReadDoubleLittleEndian(data)),
            MapiPropertyType.Boolean => data[0] != 0,
            MapiPropertyType.Integer64 => BinaryPrimitives.ReadInt64LittleEndian(data),
            MapiPropertyType.Time => DecodeFileTime(BinaryPrimitives.ReadInt64LittleEndian(data)),
            MapiPropertyType.ClassId => new Guid(data[..16]),
            MapiPropertyType.Unicode => DecodeUnicode(data),
            MapiPropertyType.String8 => DecodeString8(data, string8Encoding),
            _ => data.ToArray(),
        };
    }

    /// <summary>Converts a .NET value to the bytes of a property of a type.</summary>
    /// <exception cref="ArgumentException">The value cannot be converted to the type.</exception>
    public static byte[] Encode(MapiPropertyType type, object value, Encoding string8Encoding)
    {
        if (PropertyTypes.IsMultiValued(type))
        {
            return EncodeMultiple(type, value, string8Encoding);
        }

        var result = type switch
        {
            MapiPropertyType.Null => [],
            MapiPropertyType.Integer16 => Write(2, span => BinaryPrimitives.WriteInt16LittleEndian(span, Convert.ToInt16(value, CultureInfo.InvariantCulture))),
            MapiPropertyType.Integer32 or MapiPropertyType.ErrorCode => Write(4, span => BinaryPrimitives.WriteInt32LittleEndian(span, Convert.ToInt32(value, CultureInfo.InvariantCulture))),
            MapiPropertyType.Floating32 => Write(4, span => BinaryPrimitives.WriteSingleLittleEndian(span, Convert.ToSingle(value, CultureInfo.InvariantCulture))),
            MapiPropertyType.Floating64 => Write(8, span => BinaryPrimitives.WriteDoubleLittleEndian(span, Convert.ToDouble(value, CultureInfo.InvariantCulture))),
            MapiPropertyType.Currency => Write(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, (long)decimal.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 10000m, MidpointRounding.ToEven))),
            MapiPropertyType.FloatingTime => Write(8, span => BinaryPrimitives.WriteDoubleLittleEndian(span, value is DateTime date ? date.ToOADate() : Convert.ToDouble(value, CultureInfo.InvariantCulture))),
            MapiPropertyType.Boolean => [value is true ? (byte)1 : (byte)0],
            MapiPropertyType.Integer64 => Write(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            MapiPropertyType.Time => Write(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, EncodeFileTime(value is DateTimeOffset offset ? offset.UtcDateTime : (DateTime)value))),
            MapiPropertyType.ClassId => ((Guid)value).ToByteArray(),
            MapiPropertyType.Unicode => EncodeUnicode((string)value),
            MapiPropertyType.String8 => string8Encoding.GetBytes((string)value),
            _ => value is byte[] bytes ? bytes : throw new ArgumentException($"A value of type {value.GetType()} cannot be stored in a property of type {type}.", nameof(value)),
        };

        return result;
    }

    public static byte[] EncodeUnicode(string value)
    {
        var result = new byte[value.Length * 2];
        for (var i = 0; i < value.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2), value[i]);
        }

        return result;
    }

    private static byte[] EncodeMultiple(MapiPropertyType type, object value, Encoding string8Encoding)
    {
        var elementType = PropertyTypes.GetElementType(type);
        if (value is not System.Collections.IEnumerable values || value is string)
        {
            throw new ArgumentException($"A value of a multi-valued property must be a collection, not {value.GetType()}.", nameof(value));
        }

        var items = values.Cast<object>().Select(item => Encode(elementType, item, string8Encoding)).ToList();
        if (PropertyTypes.GetFixedSize(elementType) > 0)
        {
            return [.. items.SelectMany(item => item)];
        }

        return JoinVariable(items);
    }

    private static byte[] Write(int size, SpanAction write)
    {
        var result = new byte[size];
        write(result);
        return result;
    }

    private delegate void SpanAction(Span<byte> span);

    public static DateTime DecodeFileTime(long value)
    {
        // Outlook stores "no date" as 0 or as the maximum FILETIME (0x7FFFFFFFFFFFFFFF or 4501-01-01)
        if (value <= 0 || value > DateTime.MaxValue.ToFileTimeUtc())
        {
            return value <= 0 ? MinFileTime : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        }

        return DateTime.FromFileTimeUtc(value);
    }

    public static long EncodeFileTime(DateTime value)
    {
        if (value.Kind == DateTimeKind.Local)
        {
            value = value.ToUniversalTime();
        }

        if (value <= MinFileTime)
        {
            return 0;
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToFileTimeUtc();
    }

    private static DateTime DecodeOleDate(double value)
    {
        // DateTime.FromOADate throws outside [-657435, 2958466)
        if (double.IsNaN(value) || value <= -657435 || value >= 2958466)
        {
            return DateTime.MinValue;
        }

        return DateTime.FromOADate(value);
    }

    public static string DecodeUnicode(ReadOnlySpan<byte> data)
    {
        var length = data.Length / 2;
        var result = string.Create(length, data.ToArray(), (span, bytes) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2));
            }
        });

        // Some writers keep the terminating null character
        return result.TrimEnd('\0');
    }

    public static string DecodeString8(ReadOnlySpan<byte> data, Encoding encoding)
    {
        var end = data.IndexOf((byte)0);
        if (end >= 0)
        {
            data = data[..end];
        }

        return encoding.GetString(data);
    }

    private static object DecodeMultiple(MapiPropertyType type, ReadOnlySpan<byte> data, Encoding string8Encoding)
    {
        var elementType = PropertyTypes.GetElementType(type);
        var fixedSize = PropertyTypes.GetFixedSize(elementType);
        if (fixedSize > 0)
        {
            var count = data.Length / fixedSize;
            return elementType switch
            {
                MapiPropertyType.Integer16 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadInt16LittleEndian(d)),
                MapiPropertyType.Integer32 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadInt32LittleEndian(d)),
                MapiPropertyType.Floating32 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadSingleLittleEndian(d)),
                MapiPropertyType.Floating64 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadDoubleLittleEndian(d)),
                MapiPropertyType.Currency => DecodeArray(data, count, fixedSize, d => (decimal)BinaryPrimitives.ReadInt64LittleEndian(d) / 10000m),
                MapiPropertyType.FloatingTime => DecodeArray(data, count, fixedSize, d => DecodeOleDate(BinaryPrimitives.ReadDoubleLittleEndian(d))),
                MapiPropertyType.Integer64 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadInt64LittleEndian(d)),
                MapiPropertyType.Time => DecodeArray(data, count, fixedSize, d => DecodeFileTime(BinaryPrimitives.ReadInt64LittleEndian(d))),
                MapiPropertyType.ClassId => DecodeArray(data, count, fixedSize, d => new Guid(d[..16])),
                _ => data.ToArray(),
            };
        }

        // Variable-size elements: a count, the offsets of the elements, then the elements
        var items = SplitVariable(data);
        return elementType switch
        {
            MapiPropertyType.Unicode => items.Select(item => DecodeUnicode(item)).ToArray(),
            MapiPropertyType.String8 => items.Select(item => DecodeString8(item, string8Encoding)).ToArray(),
            _ => items.ToArray(),
        };
    }

    /// <summary>Removes the null terminator the files of a single item (.msg, TNEF) store after a string, so that the value has the format of the values of an Outlook data file.</summary>
    public static byte[] RemoveTerminator(MapiPropertyType type, byte[] value) => type switch
    {
        MapiPropertyType.Unicode when value.Length >= 2 && value[^1] == 0 && value[^2] == 0 && value.Length % 2 == 0 => value[..^2],
        MapiPropertyType.String8 when value.Length >= 1 && value[^1] == 0 => value[..^1],
        _ => value,
    };

    /// <summary>Joins variable-size values in the format of the multi-valued properties: a count, the offsets of the values, then the values.</summary>
    public static byte[] JoinVariable(IReadOnlyList<byte[]> values)
    {
        var result = new byte[4 + (values.Count * 4) + values.Sum(value => value.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)values.Count);
        var offset = 4 + (values.Count * 4);
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + (i * 4)), (uint)offset);
            values[i].CopyTo(result, offset);
            offset += values[i].Length;
        }

        return result;
    }

    public static List<byte[]> SplitVariable(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return [];
        }

        if (data.Length < 4)
        {
            throw new InvalidPstException("A multi-valued property is truncated.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count > (uint)(data.Length - 4) / 4)
        {
            throw new InvalidPstException("A multi-valued property has more values than it has bytes.");
        }

        var result = new List<byte[]>((int)count);
        for (var i = 0; i < count; i++)
        {
            var start = BinaryPrimitives.ReadUInt32LittleEndian(data[(4 + (i * 4))..]);
            var end = i + 1 < count ? BinaryPrimitives.ReadUInt32LittleEndian(data[(4 + ((i + 1) * 4))..]) : (uint)data.Length;
            if (start < 4 + (count * 4) || end < start || end > data.Length)
            {
                throw new InvalidPstException("A value of a multi-valued property is outside the property.");
            }

            result.Add(data[(int)start..(int)end].ToArray());
        }

        return result;
    }

    private static T[] DecodeArray<T>(ReadOnlySpan<byte> data, int count, int size, DecodeElement<T> decode)
    {
        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = decode(data.Slice(i * size, size));
        }

        return result;
    }

    private delegate T DecodeElement<T>(ReadOnlySpan<byte> data);
}
