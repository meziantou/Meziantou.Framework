using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Bencode;

internal static class BencodeDecoder
{
    public static BencodeValue Parse(ReadOnlySpan<byte> data)
    {
        var index = 0;
        var value = ParseValue(data, ref index);
        if (index != data.Length)
            throw new FormatException("Unexpected trailing data after bencode value.");

        return value;
    }

    private static BencodeValue ParseValue(ReadOnlySpan<byte> data, ref int index)
    {
        if (index >= data.Length)
            throw new FormatException("Unexpected end of bencode data.");

        var token = data[index];
        return token switch
        {
            (byte)'i' => ParseInteger(data, ref index),
            >= (byte)'0' and <= (byte)'9' => ParseString(data, ref index),
            (byte)'l' => ParseList(data, ref index),
            (byte)'d' => ParseDictionary(data, ref index),
            _ => throw new FormatException($"Invalid bencode token '{(char)token}' at position {index}."),
        };
    }

    private static BencodeInteger ParseInteger(ReadOnlySpan<byte> data, ref int index)
    {
        index++; // i
        var start = index;
        while (index < data.Length && data[index] != (byte)'e')
        {
            index++;
        }

        if (index >= data.Length)
            throw new FormatException("Unterminated bencode integer.");

        var integerText = data[start..index];
        if (!IsValidInteger(integerText))
            throw new FormatException("Invalid bencode integer format.");

        var integerString = Encoding.ASCII.GetString(integerText);
        if (!long.TryParse(integerString, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Invalid bencode integer value.");

        index++; // e
        return new BencodeInteger(value);
    }

    private static BencodeString ParseString(ReadOnlySpan<byte> data, ref int index)
    {
        var lengthStart = index;
        while (index < data.Length && data[index] is >= (byte)'0' and <= (byte)'9')
        {
            index++;
        }

        if (lengthStart == index || index >= data.Length || data[index] != (byte)':')
            throw new FormatException("Invalid bencode string length format.");

        var lengthText = data[lengthStart..index];
        if (lengthText.Length > 1 && lengthText[0] == (byte)'0')
            throw new FormatException("Bencode string length cannot have leading zeroes.");

        var lengthString = Encoding.ASCII.GetString(lengthText);
        if (!int.TryParse(lengthString, CultureInfo.InvariantCulture, out var stringLength) || stringLength < 0)
            throw new FormatException("Invalid bencode string length.");

        index++; // :
        if (data.Length - index < stringLength)
            throw new FormatException("Unexpected end of bencode string data.");

        var bytes = data.Slice(index, stringLength).ToArray();
        index += stringLength;
        return new BencodeString(bytes);
    }

    private static BencodeList ParseList(ReadOnlySpan<byte> data, ref int index)
    {
        index++; // l
        var result = new BencodeList();
        while (true)
        {
            if (index >= data.Length)
                throw new FormatException("Unterminated bencode list.");

            if (data[index] == (byte)'e')
            {
                index++;
                return result;
            }

            result.Add(ParseValue(data, ref index));
        }
    }

    private static BencodeDictionary ParseDictionary(ReadOnlySpan<byte> data, ref int index)
    {
        index++; // d
        var result = new BencodeDictionary();

        while (true)
        {
            if (index >= data.Length)
                throw new FormatException("Unterminated bencode dictionary.");

            if (data[index] == (byte)'e')
            {
                index++;
                return result;
            }

            var keyBytes = ParseString(data, ref index);
            string key;
            try
            {
                key = keyBytes.ToUtf8String();
            }
            catch (DecoderFallbackException ex)
            {
                throw new FormatException("Bencode dictionary keys must be valid UTF-8 strings.", ex);
            }

            var value = ParseValue(data, ref index);
            try
            {
                result.Add(key, value);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Duplicate bencode dictionary key '{key}'.", ex);
            }
        }
    }

    private static bool IsValidInteger(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return false;

        var offset = 0;
        if (value[0] == (byte)'-')
        {
            if (value.Length == 1)
                return false;

            if (value[1] == (byte)'0')
                return false;

            offset = 1;
        }
        else if (value.Length > 1 && value[0] == (byte)'0')
        {
            return false;
        }

        for (var i = offset; i < value.Length; i++)
        {
            if (value[i] is < (byte)'0' or > (byte)'9')
                return false;
        }

        return true;
    }
}
