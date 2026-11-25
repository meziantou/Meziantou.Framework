using System.Text;

namespace Meziantou.Framework.WebTransport.Http3;

// Decodes a field section (RFC 9204, section 4.5) with the static table only: the endpoint advertises a dynamic table of
// capacity 0 (the default of SETTINGS_QPACK_MAX_TABLE_CAPACITY), so a reference to the dynamic table is an error.
internal static class QPackDecoder
{
    private static readonly Encoding StrictLatin1 = Encoding.Latin1;

    public static List<QPackField> Decode(ReadOnlySpan<byte> fieldSection, int maxFieldSectionSize)
    {
        var position = 0;
        var requiredInsertCount = ReadPrefixedInteger(fieldSection, ref position, prefixBits: 8);
        if (requiredInsertCount != 0)
            throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "The field section references the dynamic table.");

        _ = ReadPrefixedInteger(fieldSection, ref position, prefixBits: 7); // Sign bit and Delta Base: ignored when the Required Insert Count is 0

        var fields = new List<QPackField>();
        var size = 0;
        while (position < fieldSection.Length)
        {
            var first = fieldSection[position];
            QPackField field;
            if ((first & 0x80) != 0)
            {
                // Indexed field line: 1 T index(6+)
                if ((first & 0x40) == 0)
                    throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "The field section references the dynamic table.");

                var index = ReadPrefixedInteger(fieldSection, ref position, prefixBits: 6);
                var entry = GetStaticEntry(index);
                field = new QPackField(entry.Name, entry.Value);
            }
            else if ((first & 0x40) != 0)
            {
                // Literal field line with name reference: 0 1 N T index(4+) value
                if ((first & 0x10) == 0)
                    throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "The field section references the dynamic table.");

                var index = ReadPrefixedInteger(fieldSection, ref position, prefixBits: 4);
                var name = GetStaticEntry(index).Name;
                var value = ReadString(fieldSection, ref position, prefixBits: 7);
                field = new QPackField(name, value);
            }
            else if ((first & 0x20) != 0)
            {
                // Literal field line with literal name: 0 0 1 N H length(3+) name value
                var name = ReadString(fieldSection, ref position, prefixBits: 3);
                var value = ReadString(fieldSection, ref position, prefixBits: 7);
                field = new QPackField(name, value);
            }
            else
            {
                // Indexed field line with post-base index (0001), literal with post-base name reference (0000)
                throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "The field section references the dynamic table.");
            }

            size += field.Size;
            if (size > maxFieldSectionSize)
                throw new Http3ProtocolException(Http3ErrorCode.ExcessiveLoad, "The field section is too large.");

            fields.Add(field);
        }

        return fields;
    }

    private static (string Name, string Value) GetStaticEntry(long index)
    {
        if (index >= QPackStaticTable.Entries.Length)
            throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, $"Invalid static table index {index}.");

        return QPackStaticTable.Entries[index];
    }

    // A string literal (RFC 7541, section 5.2): the H bit is the bit before the prefix of the length
    private static string ReadString(ReadOnlySpan<byte> data, ref int position, int prefixBits)
    {
        if (position >= data.Length)
            throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Truncated field section.");

        var isHuffman = (data[position] & (1 << prefixBits)) != 0;
        var length = ReadPrefixedInteger(data, ref position, prefixBits);
        if (length > data.Length - position)
            throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Truncated field section.");

        var bytes = data.Slice(position, (int)length);
        position += (int)length;
        return isHuffman ? StrictLatin1.GetString(Huffman.Decode(bytes)) : StrictLatin1.GetString(bytes);
    }

    // A prefixed integer (RFC 7541, section 5.1)
    internal static long ReadPrefixedInteger(ReadOnlySpan<byte> data, ref int position, int prefixBits)
    {
        if (position >= data.Length)
            throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Truncated field section.");

        var mask = (1 << prefixBits) - 1;
        long value = data[position++] & mask;
        if (value < mask)
            return value;

        var shift = 0;
        while (true)
        {
            if (position >= data.Length)
                throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Truncated field section.");

            var b = data[position++];
            if (shift > 56)
                throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Integer overflow.");

            value += (long)(b & 0x7F) << shift;
            if (value > VariableLengthInteger.MaxValue)
                throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Integer overflow.");

            shift += 7;
            if ((b & 0x80) == 0)
                return value;
        }
    }
}
