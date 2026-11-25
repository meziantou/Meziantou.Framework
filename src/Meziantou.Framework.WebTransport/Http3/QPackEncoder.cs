using System.Text;

namespace Meziantou.Framework.WebTransport.Http3;

// Encodes a field section with the static table only, and Huffman-encoded strings when they are shorter
internal static class QPackEncoder
{
    public static byte[] Encode(IEnumerable<QPackField> fields)
    {
        // Required Insert Count 0, Delta Base 0
        var result = new List<byte> { 0, 0 };
        foreach (var field in fields)
        {
            var nameIndex = -1;
            var exactIndex = -1;
            for (var i = 0; i < QPackStaticTable.Entries.Length; i++)
            {
                var entry = QPackStaticTable.Entries[i];
                if (string.Equals(entry.Name, field.Name, StringComparison.Ordinal))
                {
                    if (nameIndex < 0)
                    {
                        nameIndex = i;
                    }

                    if (string.Equals(entry.Value, field.Value, StringComparison.Ordinal))
                    {
                        exactIndex = i;
                        break;
                    }
                }
            }

            if (exactIndex >= 0)
            {
                // Indexed field line, static table: 1 1 index(6+)
                WritePrefixedInteger(result, 0xC0, prefixBits: 6, exactIndex);
            }
            else if (nameIndex >= 0)
            {
                // Literal field line with name reference, static table: 0 1 0 1 index(4+)
                WritePrefixedInteger(result, 0x50, prefixBits: 4, nameIndex);
                WriteString(result, field.Value, flags: 0, prefixBits: 7);
            }
            else
            {
                // Literal field line with literal name: 0 0 1 0 H length(3+)
                WriteString(result, field.Name, flags: 0x20, prefixBits: 3);
                WriteString(result, field.Value, flags: 0, prefixBits: 7);
            }
        }

        return [.. result];
    }

    private static void WriteString(List<byte> destination, string value, byte flags, int prefixBits)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        var huffmanLength = Huffman.GetEncodedLength(bytes);
        if (huffmanLength < bytes.Length)
        {
            WritePrefixedInteger(destination, (byte)(flags | (1 << prefixBits)), prefixBits, huffmanLength);
            Huffman.Encode(bytes, destination);
        }
        else
        {
            WritePrefixedInteger(destination, flags, prefixBits, bytes.Length);
            destination.AddRange(bytes);
        }
    }

    internal static void WritePrefixedInteger(List<byte> destination, byte flags, int prefixBits, long value)
    {
        var mask = (1 << prefixBits) - 1;
        if (value < mask)
        {
            destination.Add((byte)(flags | value));
            return;
        }

        destination.Add((byte)(flags | mask));
        value -= mask;
        while (value >= 0x80)
        {
            destination.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        destination.Add((byte)value);
    }
}
