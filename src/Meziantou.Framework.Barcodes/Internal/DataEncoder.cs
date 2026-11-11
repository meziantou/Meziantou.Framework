namespace Meziantou.Framework.Barcodes.Internal;

internal static class DataEncoder
{
    /// <summary>The number of bits of a structured append header: the mode indicator, the position, the count and the parity.</summary>
    public const int StructuredAppendBitCount = 4 + 4 + 4 + 8;

    /// <summary>Gets the data codewords of a standard QR code that holds the segments, after a structured append header when <paramref name="structuredAppend"/> is set.</summary>
    public static byte[] Encode(IReadOnlyList<QRCodeSegment> segments, int version, ErrorCorrectionLevel ecLevel, QRCodeStructuredAppend? structuredAppend)
    {
        var buffer = new BitBuffer();
        if (structuredAppend is not null)
        {
            // ISO/IEC 18004, 8.3: the position and the count minus 1 on 4 bits each, then the parity of the whole data
            buffer.Append((int)EncodingMode.StructuredAppend, 4);
            buffer.Append(structuredAppend.Index, 4);
            buffer.Append(structuredAppend.Count - 1, 4);
            buffer.Append(structuredAppend.Parity, 8);
        }

        QRSymbolFormat.Write(buffer, segments, QRCodeType.Standard, version);
        DataEncoderHelper.AddTerminatorAndPadding(buffer, QRCodeVersion.GetDataCodewords(version, ecLevel) * 8, terminatorMaxBits: 4);
        return buffer.ToByteArray();
    }

    /// <summary>Gets the number of bits of an ECI header: the mode indicator and the designator of 1 to 3 bytes.</summary>
    public static int GetEciBitCount(int assignmentNumber)
    {
        return assignmentNumber switch
        {
            < 128 => 4 + 8,
            < 16384 => 4 + 16,
            _ => 4 + 24,
        };
    }

    // ISO/IEC 18004, 7.4.3.2: 0bbbbbbb, 10bbbbbb bbbbbbbb, or 110bbbbb bbbbbbbb bbbbbbbb
    public static void AppendEci(BitBuffer buffer, int assignmentNumber)
    {
        buffer.Append((int)EncodingMode.Eci, 4);
        switch (assignmentNumber)
        {
            case < 128:
                buffer.Append(assignmentNumber, 8);
                break;
            case < 16384:
                buffer.Append(0b10, 2);
                buffer.Append(assignmentNumber, 14);
                break;
            default:
                buffer.Append(0b110, 3);
                buffer.Append(assignmentNumber, 21);
                break;
        }
    }
}
