namespace Meziantou.Framework.Barcodes.Internal;

internal static class DataEncoder
{
    public static byte[] Encode(string data, QRSegment[] segments, int version, ErrorCorrectionLevel ecLevel)
    {
        var buffer = new BitBuffer();
        foreach (var segment in segments)
        {
            buffer.Append((int)segment.Mode, 4);
            buffer.Append(segment.GetCharacterCount(data), QRCodeVersion.GetCharacterCountBits(version, segment.Mode));
            DataEncoderHelper.EncodeData(buffer, data.Substring(segment.Start, segment.Length), segment.Mode);
        }

        DataEncoderHelper.AddTerminatorAndPadding(buffer, QRCodeVersion.GetDataCodewords(version, ecLevel) * 8, terminatorMaxBits: 4);
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
}
