using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mail.Internals.Ltp;

/// <summary>Converts the bytes of a property to a .NET value, and back.</summary>
internal static class PropertyValueCodec
{
    private static readonly DateTime MinFileTime = DateTime.FromFileTimeUtc(0);

    public static object? Decode(PstPropertyType type, ReadOnlySpan<byte> data, Encoding string8Encoding)
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
            PstPropertyType.Null => null,
            PstPropertyType.Integer16 => BinaryPrimitives.ReadInt16LittleEndian(data),
            PstPropertyType.Integer32 or PstPropertyType.ErrorCode => BinaryPrimitives.ReadInt32LittleEndian(data),
            PstPropertyType.Floating32 => BinaryPrimitives.ReadSingleLittleEndian(data),
            PstPropertyType.Floating64 => BinaryPrimitives.ReadDoubleLittleEndian(data),
            PstPropertyType.Currency => (decimal)BinaryPrimitives.ReadInt64LittleEndian(data) / 10000m,
            PstPropertyType.FloatingTime => DecodeOleDate(BinaryPrimitives.ReadDoubleLittleEndian(data)),
            PstPropertyType.Boolean => data[0] != 0,
            PstPropertyType.Integer64 => BinaryPrimitives.ReadInt64LittleEndian(data),
            PstPropertyType.Time => DecodeFileTime(BinaryPrimitives.ReadInt64LittleEndian(data)),
            PstPropertyType.ClassId => new Guid(data[..16]),
            PstPropertyType.Unicode => DecodeUnicode(data),
            PstPropertyType.String8 => DecodeString8(data, string8Encoding),
            _ => data.ToArray(),
        };
    }

    /// <summary>Converts a .NET value to the bytes of a property of a type.</summary>
    /// <exception cref="ArgumentException">The value cannot be converted to the type.</exception>
    public static byte[] Encode(PstPropertyType type, object value, Encoding string8Encoding)
    {
        if (PropertyTypes.IsMultiValued(type))
        {
            return EncodeMultiple(type, value, string8Encoding);
        }

        var result = type switch
        {
            PstPropertyType.Null => [],
            PstPropertyType.Integer16 => Write(2, span => BinaryPrimitives.WriteInt16LittleEndian(span, Convert.ToInt16(value, CultureInfo.InvariantCulture))),
            PstPropertyType.Integer32 or PstPropertyType.ErrorCode => Write(4, span => BinaryPrimitives.WriteInt32LittleEndian(span, Convert.ToInt32(value, CultureInfo.InvariantCulture))),
            PstPropertyType.Floating32 => Write(4, span => BinaryPrimitives.WriteSingleLittleEndian(span, Convert.ToSingle(value, CultureInfo.InvariantCulture))),
            PstPropertyType.Floating64 => Write(8, span => BinaryPrimitives.WriteDoubleLittleEndian(span, Convert.ToDouble(value, CultureInfo.InvariantCulture))),
            PstPropertyType.Currency => Write(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, (long)decimal.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 10000m, MidpointRounding.ToEven))),
            PstPropertyType.FloatingTime => Write(8, span => BinaryPrimitives.WriteDoubleLittleEndian(span, value is DateTime date ? date.ToOADate() : Convert.ToDouble(value, CultureInfo.InvariantCulture))),
            PstPropertyType.Boolean => [value is true ? (byte)1 : (byte)0],
            PstPropertyType.Integer64 => Write(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            PstPropertyType.Time => Write(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, EncodeFileTime(value is DateTimeOffset offset ? offset.UtcDateTime : (DateTime)value))),
            PstPropertyType.ClassId => ((Guid)value).ToByteArray(),
            PstPropertyType.Unicode => EncodeUnicode((string)value),
            PstPropertyType.String8 => string8Encoding.GetBytes((string)value),
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

    private static byte[] EncodeMultiple(PstPropertyType type, object value, Encoding string8Encoding)
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

        var result = new byte[4 + (items.Count * 4) + items.Sum(item => item.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)items.Count);
        var offset = 4 + (items.Count * 4);
        for (var i = 0; i < items.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + (i * 4)), (uint)offset);
            items[i].CopyTo(result, offset);
            offset += items[i].Length;
        }

        return result;
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

    private static object DecodeMultiple(PstPropertyType type, ReadOnlySpan<byte> data, Encoding string8Encoding)
    {
        var elementType = PropertyTypes.GetElementType(type);
        var fixedSize = PropertyTypes.GetFixedSize(elementType);
        if (fixedSize > 0)
        {
            var count = data.Length / fixedSize;
            return elementType switch
            {
                PstPropertyType.Integer16 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadInt16LittleEndian(d)),
                PstPropertyType.Integer32 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadInt32LittleEndian(d)),
                PstPropertyType.Floating32 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadSingleLittleEndian(d)),
                PstPropertyType.Floating64 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadDoubleLittleEndian(d)),
                PstPropertyType.Currency => DecodeArray(data, count, fixedSize, d => (decimal)BinaryPrimitives.ReadInt64LittleEndian(d) / 10000m),
                PstPropertyType.FloatingTime => DecodeArray(data, count, fixedSize, d => DecodeOleDate(BinaryPrimitives.ReadDoubleLittleEndian(d))),
                PstPropertyType.Integer64 => DecodeArray(data, count, fixedSize, d => BinaryPrimitives.ReadInt64LittleEndian(d)),
                PstPropertyType.Time => DecodeArray(data, count, fixedSize, d => DecodeFileTime(BinaryPrimitives.ReadInt64LittleEndian(d))),
                PstPropertyType.ClassId => DecodeArray(data, count, fixedSize, d => new Guid(d[..16])),
                _ => data.ToArray(),
            };
        }

        // Variable-size elements: a count, the offsets of the elements, then the elements
        var items = SplitVariable(data);
        return elementType switch
        {
            PstPropertyType.Unicode => items.Select(item => DecodeUnicode(item)).ToArray(),
            PstPropertyType.String8 => items.Select(item => DecodeString8(item, string8Encoding)).ToArray(),
            _ => items.ToArray(),
        };
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
