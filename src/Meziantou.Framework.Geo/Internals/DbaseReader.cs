using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Reads the records of a dBASE III/IV file (<c>.dbf</c>), in order, from a stream that may not be seekable.</summary>
internal sealed class DbaseReader
{
    private const int HeaderLength = 32;
    private const int FieldDescriptorLength = 32;

    private readonly Stream _stream;
    private readonly Field[] _fields;
    private readonly byte[] _record;
    private readonly DbaseTextDecoder _decoder;

    public DbaseReader(Stream stream, Encoding? encoding, string? codePage)
    {
        _stream = stream;
        var header = new byte[HeaderLength];
        ReadExactly(header, "The .dbf file is too short to have a header.");
        var recordCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
        if (recordCount > int.MaxValue || headerLength < HeaderLength + 1 || recordLength < 1)
            throw new FormatException("The header of the .dbf file is invalid.");

        RecordCount = (int)recordCount;
        _decoder = DbaseTextDecoder.Create(encoding, codePage, header[29]);

        // The field descriptors end with 0x0D; the header may have more bytes after it, such as the database container of Visual FoxPro
        var descriptors = new byte[headerLength - HeaderLength];
        ReadExactly(descriptors, "The .dbf file is truncated.");
        var fields = new List<Field>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var offset = 1; // The deletion flag
        for (var position = 0; position + FieldDescriptorLength <= descriptors.Length && descriptors[position] is not 0x0D; position += FieldDescriptorLength)
        {
            var descriptor = descriptors.AsSpan(position, FieldDescriptorLength);
            var nameBytes = descriptor[..11];
            var nameLength = nameBytes.IndexOf((byte)0);
            var name = _decoder.Decode(nameLength < 0 ? nameBytes : nameBytes[..nameLength]).Trim();
            var type = (char)descriptor[11];

            // shapelib reads the length of a character field on two bytes, as Clipper and FoxPro write the long ones
            var length = type is 'C' ? BinaryPrimitives.ReadUInt16LittleEndian(descriptor[16..]) : descriptor[16];
            fields.Add(new Field(GetUniqueName(names, name), type, offset, length));
            offset += length;
        }

        if (offset > recordLength)
            throw new FormatException("The fields of the .dbf file are longer than its records.");

        _fields = [.. fields];
        _record = new byte[recordLength];
    }

    /// <summary>Gets the number of records, from the header.</summary>
    public int RecordCount { get; }

    /// <summary>Reads the next record.</summary>
    /// <param name="deleted">A value that indicates whether the record is marked as deleted.</param>
    /// <returns>The values of the fields, by name.</returns>
    public Dictionary<string, object?> Read(out bool deleted)
    {
        ReadExactly(_record, "The .dbf file is truncated: it has fewer records than its header says.");
        deleted = _record[0] is (byte)'*';
        var properties = new Dictionary<string, object?>(_fields.Length, StringComparer.Ordinal);
        foreach (var field in _fields)
        {
            properties[field.Name] = ReadValue(field, _record.AsSpan(field.Offset, field.Length));
        }

        return properties;
    }

    private object? ReadValue(Field field, ReadOnlySpan<byte> value)
    {
        switch (field.Type)
        {
            case 'N' or 'F':
                var number = value.Trim((byte)' ');
                if (number.IsEmpty || number[0] is (byte)'*')
                    return null;

                return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

            case 'L':
                return value.IsEmpty ? null : (char)value[0] switch
                {
                    'T' or 't' or 'Y' or 'y' => true,
                    'F' or 'f' or 'N' or 'n' => false,
                    _ => null,
                };

            case 'D':
                var date = value.Trim((byte)' ');
                if (date.Length is 8 && int.TryParse(date[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
                    int.TryParse(date[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var month) &&
                    int.TryParse(date[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var day) &&
                    year >= 1 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                {
                    return new DateOnly(year, month, day);
                }

                return null;

            case 'C':
                var text = value.TrimEnd((byte)' ').TrimEnd((byte)0);
                return text.IsEmpty ? null : _decoder.Decode(text);

            default:
                // The other types (memo block numbers, FoxPro types) as their text, as GDAL does, without the padding
                var other = value.Trim((byte)' ').TrimEnd((byte)0);
                return other.IsEmpty ? null : _decoder.Decode(other);
        }
    }

    private static string GetUniqueName(HashSet<string> names, string name)
    {
        var unique = name;
        for (var i = 1; !names.Add(unique); i++)
        {
            unique = name + "_" + i.ToString(CultureInfo.InvariantCulture);
        }

        return unique;
    }

    private void ReadExactly(Span<byte> buffer, string message)
    {
        if (_stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length)
            throw new FormatException(message);
    }

    private readonly record struct Field(string Name, char Type, int Offset, int Length);
}
