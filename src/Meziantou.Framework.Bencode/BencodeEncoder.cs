using System.Buffers;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Bencode;

internal static class BencodeEncoder
{
    public static byte[] Encode(BencodeValue value, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(value);

        var buffer = new ArrayBufferWriter<byte>();
        WriteValue(buffer, value, canonical);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteValue(IBufferWriter<byte> writer, BencodeValue value, bool canonical)
    {
        switch (value)
        {
            case BencodeInteger integer:
                WriteByte(writer, (byte)'i');
                WriteAscii(writer, integer.Value.ToString(CultureInfo.InvariantCulture));
                WriteByte(writer, (byte)'e');
                break;

            case BencodeString text:
                WriteString(writer, text.Value.Span);
                break;

            case BencodeList list:
                WriteByte(writer, (byte)'l');
                foreach (var item in list)
                {
                    WriteValue(writer, item, canonical);
                }

                WriteByte(writer, (byte)'e');
                break;

            case BencodeDictionary dictionary:
                WriteByte(writer, (byte)'d');
                foreach (var entry in GetEntries(dictionary, canonical))
                {
                    WriteString(writer, Encoding.UTF8.GetBytes(entry.Key));
                    WriteValue(writer, entry.Value, canonical);
                }

                WriteByte(writer, (byte)'e');
                break;

            default:
                throw new InvalidOperationException($"Unsupported bencode value type '{value.GetType().FullName}'.");
        }
    }

    private static IEnumerable<KeyValuePair<string, BencodeValue>> GetEntries(BencodeDictionary dictionary, bool canonical)
    {
        if (!canonical)
            return dictionary;

        var entries = dictionary.ToArray();
        Array.Sort(entries, CompareByUtf8Key);

        return entries;
    }

    private static int CompareByUtf8Key(KeyValuePair<string, BencodeValue> left, KeyValuePair<string, BencodeValue> right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left.Key);
        var rightBytes = Encoding.UTF8.GetBytes(right.Key);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }

    private static void WriteString(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteAscii(writer, value.Length.ToString(CultureInfo.InvariantCulture));
        WriteByte(writer, (byte)':');
        WriteBytes(writer, value);
    }

    private static void WriteAscii(IBufferWriter<byte> writer, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        WriteBytes(writer, bytes);
    }

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    private static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        var span = writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        writer.Advance(bytes.Length);
    }
}
