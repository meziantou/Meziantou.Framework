using System.Text;

namespace Meziantou.Framework.Internal;

internal static class DataEncoder
{
    public static byte[] Encode(string data, int version, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var buffer = new BitBuffer();

        // Mode indicator (4 bits)
        buffer.Append((int)mode, 4);

        // Character count indicator
        var cciBits = QRCodeVersion.GetCharacterCountBits(version, mode);
        var charCount = mode switch
        {
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data),
            EncodingMode.Kanji => data.Length,
            _ => data.Length,
        };
        buffer.Append(charCount, cciBits);

        // Data encoding
        switch (mode)
        {
            case EncodingMode.Numeric:
                EncodeNumeric(buffer, data);
                break;
            case EncodingMode.Alphanumeric:
                EncodeAlphanumeric(buffer, data);
                break;
            case EncodingMode.Byte:
                EncodeByte(buffer, data);
                break;
            case EncodingMode.Kanji:
                EncodeKanji(buffer, data);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var totalDataBits = QRCodeVersion.GetDataCodewords(version, ecLevel) * 8;
        if (buffer.BitCount > totalDataBits)
        {
            throw new InvalidOperationException("The data is too long to be encoded in a QR code.");
        }

        // Add terminator
        var terminatorLength = Math.Min(4, totalDataBits - buffer.BitCount);
        if (terminatorLength > 0)
        {
            buffer.Append(0, terminatorLength);
        }

        // Pad to byte boundary
        var padBits = (8 - (buffer.BitCount % 8)) % 8;
        if (padBits > 0)
        {
            buffer.Append(0, padBits);
        }

        // Add pad bytes to fill capacity
        var padIndex = 0;
        while (buffer.BitCount < totalDataBits)
        {
            buffer.Append(padIndex % 2 == 0 ? 0xEC : 0x11, 8);
            padIndex++;
        }

        return buffer.ToByteArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> data, int version, ErrorCorrectionLevel ecLevel)
    {
        var buffer = new BitBuffer();

        // Mode indicator (4 bits) - byte mode
        buffer.Append((int)EncodingMode.Byte, 4);

        // Character count indicator
        var cciBits = QRCodeVersion.GetCharacterCountBits(version, EncodingMode.Byte);
        buffer.Append(data.Length, cciBits);

        // Data encoding
        foreach (var b in data)
        {
            buffer.Append(b, 8);
        }

        var totalDataBits = QRCodeVersion.GetDataCodewords(version, ecLevel) * 8;
        if (buffer.BitCount > totalDataBits)
        {
            throw new InvalidOperationException("The data is too long to be encoded in a QR code.");
        }

        // Add terminator
        var terminatorLength = Math.Min(4, totalDataBits - buffer.BitCount);
        if (terminatorLength > 0)
        {
            buffer.Append(0, terminatorLength);
        }

        // Pad to byte boundary
        var padBits = (8 - (buffer.BitCount % 8)) % 8;
        if (padBits > 0)
        {
            buffer.Append(0, padBits);
        }

        // Add pad bytes to fill capacity
        var padIndex = 0;
        while (buffer.BitCount < totalDataBits)
        {
            buffer.Append(padIndex % 2 == 0 ? 0xEC : 0x11, 8);
            padIndex++;
        }

        return buffer.ToByteArray();
    }

    private static void EncodeNumeric(BitBuffer buffer, string data)
    {
        var i = 0;
        while (i + 2 < data.Length)
        {
            var value = (data[i] - '0') * 100 + (data[i + 1] - '0') * 10 + (data[i + 2] - '0');
            buffer.Append(value, 10);
            i += 3;
        }

        if (i + 1 < data.Length)
        {
            var value = (data[i] - '0') * 10 + (data[i + 1] - '0');
            buffer.Append(value, 7);
        }
        else if (i < data.Length)
        {
            var value = data[i] - '0';
            buffer.Append(value, 4);
        }
    }

    private static void EncodeAlphanumeric(BitBuffer buffer, string data)
    {
        var i = 0;
        while (i + 1 < data.Length)
        {
            var value = DataAnalyzer.GetAlphanumericValue(data[i]) * 45 + DataAnalyzer.GetAlphanumericValue(data[i + 1]);
            buffer.Append(value, 11);
            i += 2;
        }

        if (i < data.Length)
        {
            buffer.Append(DataAnalyzer.GetAlphanumericValue(data[i]), 6);
        }
    }

    private static void EncodeByte(BitBuffer buffer, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        foreach (var b in bytes)
        {
            buffer.Append(b, 8);
        }
    }

    private static void EncodeKanji(BitBuffer buffer, string data)
    {
        var sjisBytes = DataAnalyzer.GetShiftJisBytes(data);
        for (var i = 0; i < sjisBytes.Length; i += 2)
        {
            var value = (sjisBytes[i] << 8) | sjisBytes[i + 1];

            // Subtract offset based on range
            if (value is >= 0x8140 and <= 0x9FFC)
            {
                value -= 0x8140;
            }
            else
            {
                value -= 0xC140;
            }

            // Multiply high byte by 0xC0 and add low byte
            value = ((value >> 8) * 0xC0) + (value & 0xFF);
            buffer.Append(value, 13);
        }
    }
}
