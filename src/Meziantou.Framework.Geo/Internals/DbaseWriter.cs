using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Writes the properties of features as a dBASE III file (<c>.dbf</c>) encoded in UTF-8.</summary>
internal sealed class DbaseWriter
{
    private const int MaxNameLength = 10;
    private const int MaxCharacterLength = 254;
    private const int RealLength = 24;
    private const int RealDecimals = 15;

    // Below this magnitude, a whole double is written as an integer; above it, a double no longer has a unit precision for every value
    private const double MaxWholeNumber = 1e15;

    private readonly GeoFeatureCollection _features;
    private readonly List<Field> _fields;
    private readonly int _recordLength;

    private DbaseWriter(GeoFeatureCollection features, List<Field> fields, string paramName)
    {
        _features = features;
        _fields = fields;
        _recordLength = 1 + fields.Sum(field => field.Length);
        if (32 + (32 * fields.Count) + 1 > ushort.MaxValue || _recordLength > ushort.MaxValue)
            throw new ArgumentException("The properties have too many fields, or too long values, for a dBASE file.", paramName);
    }

    /// <summary>Infers the fields from the properties of the features.</summary>
    public static DbaseWriter Create(GeoFeatureCollection features, string paramName)
    {
        var fieldsByKey = new Dictionary<string, Field>(StringComparer.Ordinal);
        var fields = new List<Field>();
        foreach (var feature in features)
        {
            foreach (var (key, value) in feature.Properties)
            {
                if (!fieldsByKey.TryGetValue(key, out var field))
                {
                    field = new Field(key);
                    fieldsByKey.Add(key, field);
                    fields.Add(field);
                }

                var kind = GetKind(value);
                field.Kind = (field.Kind, kind) switch
                {
                    (_, Kind.None) => field.Kind,
                    (Kind.None, _) => kind,
                    (Kind.Integer, Kind.Real) or (Kind.Real, Kind.Integer) => Kind.Real,
                    _ when field.Kind == kind => kind,
                    _ => Kind.Text,
                };
            }
        }

        // ArcGIS requires a field: GDAL adds this one when a layer has none
        if (fields.Count is 0)
        {
            fields.Add(new Field("FID") { Kind = Kind.Integer, IsFeatureIndex = true });
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            field.NameBytes = GetUniqueName(names, field.Key);
            (field.Type, field.Length, field.Decimals) = field.Kind switch
            {
                Kind.Integer => ('N', 1, 0),
                Kind.Real => ('N', RealLength, RealDecimals),
                Kind.Boolean => ('L', 1, 0),
                Kind.Date => ('D', 8, 0),
                _ => ('C', 1, 0),
            };
        }

        // The width of the integer and character fields fits their longest value
        for (var i = 0; i < features.Count; i++)
        {
            foreach (var field in fields)
            {
                if (field.Kind is Kind.Integer or Kind.Text or Kind.None)
                {
                    var length = Encoding.UTF8.GetByteCount(FormatValue(field, GetValue(features[i], field, i)) ?? "");
                    field.Length = Math.Min(Math.Max(field.Length, length), MaxCharacterLength);
                }
            }
        }

        return new DbaseWriter(features, fields, paramName);
    }

    public void Write(Stream stream)
    {
        var headerLength = 32 + (32 * _fields.Count) + 1;
        var header = new byte[headerLength];
        var today = DateTime.UtcNow;
        header[0] = 0x03; // dBASE III without memo
        header[1] = (byte)(today.Year - 1900);
        header[2] = (byte)today.Month;
        header[3] = (byte)today.Day;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)_features.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)_recordLength);
        for (var i = 0; i < _fields.Count; i++)
        {
            var field = _fields[i];
            var descriptor = header.AsSpan(32 + (32 * i), 32);
            field.NameBytes.CopyTo(descriptor);
            descriptor[11] = (byte)field.Type;
            descriptor[16] = (byte)field.Length;
            descriptor[17] = (byte)field.Decimals;
        }

        header[^1] = 0x0D;
        stream.Write(header);

        var record = new byte[_recordLength];
        for (var i = 0; i < _features.Count; i++)
        {
            record[0] = (byte)' ';
            var offset = 1;
            foreach (var field in _fields)
            {
                WriteValue(record.AsSpan(offset, field.Length), field, GetValue(_features[i], field, i));
                offset += field.Length;
            }

            stream.Write(record);
        }

        stream.WriteByte(0x1A);
    }

    private static object? GetValue(GeoFeature feature, Field field, int index)
    {
        if (field.IsFeatureIndex)
            return index;

        return feature.Properties.TryGetValue(field.Key, out var value) ? value : null;
    }

    private static void WriteValue(Span<byte> destination, Field field, object? value)
    {
        var text = FormatValue(field, value);
        if (text is null)
        {
            // The null values of shapelib: blanks, '*' for a number, '?' for a boolean, zeros for a date
            destination.Fill(field.Type switch
            {
                'N' => (byte)'*',
                'L' => (byte)'?',
                'D' => (byte)'0',
                _ => (byte)' ',
            });
            return;
        }

        destination.Fill((byte)' ');
        var bytes = Encoding.UTF8.GetBytes(text);
        var length = Math.Min(bytes.Length, destination.Length);

        // A character value too long is cut before a UTF-8 sequence, so the text stays valid
        while (length < bytes.Length && length > 0 && (bytes[length] & 0xC0) is 0x80)
        {
            length--;
        }

        // The numbers are right-aligned, the other values left-aligned
        var start = field.Type is 'N' ? destination.Length - length : 0;
        bytes.AsSpan(0, length).CopyTo(destination[start..]);
    }

    private static string? FormatValue(Field field, object? value)
    {
        if (value is null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
            return null;

        switch (field.Kind)
        {
            case Kind.Integer:
                if (value is double or float or Half or decimal or JsonElement)
                {
                    GeoPropertyValue.TryGetNumber(value, out var whole);
                    return double.IsFinite(whole) ? ((long)whole).ToString(CultureInfo.InvariantCulture) : null;
                }

                return GeoPropertyValue.ToText(value);

            case Kind.Real:
                if (!GeoPropertyValue.TryGetNumber(value, out var number) || !double.IsFinite(number))
                    return null;

                // The shortest text that gives the value back, as the number type writes it, or as a double when it is too long (a decimal)
                var text = value is JsonElement ? number.ToString(CultureInfo.InvariantCulture) : GeoPropertyValue.ToText(value)!;
                return text.Length <= RealLength ? text : number.ToString(CultureInfo.InvariantCulture);

            case Kind.Boolean:
                return (bool)value ? "T" : "F";

            case Kind.Date:
                var date = value switch
                {
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    DateTimeOffset dateTimeOffset => DateOnly.FromDateTime(dateTimeOffset.DateTime),
                    _ => (DateOnly)value,
                };
                return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            default:
                return GeoPropertyValue.ToText(value);
        }
    }

    private static Kind GetKind(object? value)
    {
        switch (value)
        {
            case null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }:
                return Kind.None;
            case string:
                return Kind.Text;
            case bool:
                return Kind.Boolean;
            case DateOnly or DateTime or DateTimeOffset:
                return Kind.Date;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Kind.Integer;
        }

        if (GeoPropertyValue.TryGetNumber(value, out var number))
        {
            if (!double.IsFinite(number))
                return Kind.None;

            return Math.Abs(number) < MaxWholeNumber && Math.Floor(number) == number ? Kind.Integer : Kind.Real;
        }

        return Kind.Text;
    }

    // As GDAL: the UTF-8 name, cut to 10 bytes, then "name_1" to "name_99", "nam_100"... when another field has the same name, ignoring the case
    private static byte[] GetUniqueName(HashSet<string> names, string key)
    {
        var name = key.Replace("\0", "", StringComparison.Ordinal);
        if (name.Length is 0)
        {
            name = "FIELD";
        }

        var candidate = Truncate(name, MaxNameLength);
        for (var i = 1; !names.Add(candidate); i++)
        {
            var suffix = "_" + i.ToString(CultureInfo.InvariantCulture);
            candidate = Truncate(name, MaxNameLength - suffix.Length) + suffix;
        }

        return Encoding.UTF8.GetBytes(candidate);
    }

    private static string Truncate(string name, int maxBytes)
    {
        var length = 0;
        var byteCount = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            byteCount += rune.Utf8SequenceLength;
            if (byteCount > maxBytes)
                break;

            length += rune.Utf16SequenceLength;
        }

        return name[..length];
    }

    private enum Kind
    {
        None,
        Text,
        Integer,
        Real,
        Boolean,
        Date,
    }

    private sealed class Field(string key)
    {
        public string Key { get; } = key;

        public Kind Kind { get; set; }

        public bool IsFeatureIndex { get; init; }

        public byte[] NameBytes { get; set; } = [];

        public char Type { get; set; }

        public int Length { get; set; }

        public int Decimals { get; set; }
    }
}
