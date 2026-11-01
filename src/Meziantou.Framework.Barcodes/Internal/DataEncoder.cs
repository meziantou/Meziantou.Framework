namespace Meziantou.Framework.Barcodes.Internal;

internal static class DataEncoder
{
    public static byte[] Encode(string data, QRSegment[] segments, int version, ErrorCorrectionLevel ecLevel, int? eciAssignmentNumber)
    {
        var buffer = new BitBuffer();
        if (eciAssignmentNumber is { } eci)
        {
            AppendEci(buffer, eci);
        }

        foreach (var segment in segments)
        {
            buffer.Append((int)segment.Mode, 4);
            buffer.Append(segment.GetCharacterCount(data), QRCodeVersion.GetCharacterCountBits(version, segment.Mode));
            DataEncoderHelper.EncodeData(buffer, data.Substring(segment.Start, segment.Length), segment.Mode);
        }

        DataEncoderHelper.AddTerminatorAndPadding(buffer, QRCodeVersion.GetDataCodewords(version, ecLevel) * 8, terminatorMaxBits: 4);
        return buffer.ToByteArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> data, int version, ErrorCorrectionLevel ecLevel, int? eciAssignmentNumber)
    {
        var buffer = new BitBuffer();
        if (eciAssignmentNumber is { } eci)
        {
            AppendEci(buffer, eci);
        }

        buffer.Append((int)EncodingMode.Byte, 4);
        buffer.Append(data.Length, QRCodeVersion.GetCharacterCountBits(version, EncodingMode.Byte));
        foreach (var b in data)
        {
            buffer.Append(b, 8);
        }

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
    private static void AppendEci(BitBuffer buffer, int assignmentNumber)
    {
        buffer.Append(0b0111, 4);
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
