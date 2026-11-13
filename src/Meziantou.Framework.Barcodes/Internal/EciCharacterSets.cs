using System.Text.Unicode;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>Decodes the bytes of a symbol with the character set that an ECI designator declares.</summary>
internal static class EciCharacterSets
{
    static EciCharacterSets()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Decodes bytes with the character set of an ECI assignment number. Without ECI, or for an assignment number whose
    /// character set .NET does not have, the bytes are UTF-8 when they are valid UTF-8, and ISO/IEC 8859-1 otherwise:
    /// the generators of this library write UTF-8 without ECI designator, the default of the standards is ISO/IEC 8859-1.
    /// </summary>
    public static string GetString(ReadOnlySpan<byte> bytes, int? assignmentNumber)
    {
        if (assignmentNumber is { } number && GetEncoding(number) is { } encoding)
            return encoding.GetString(bytes);

        return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
    }

    /// <summary>Gets the text of QR code segments: the byte segments are decoded with the ECI designator that precedes them.</summary>
    public static string GetText(IEnumerable<QRCodeSegment> segments)
    {
        var text = new StringBuilder();
        var pendingBytes = new List<byte>();
        int? assignmentNumber = null;
        foreach (var segment in segments)
        {
            switch (segment.Mode)
            {
                case QRCodeSegmentMode.Byte:
                    pendingBytes.AddRange(segment.Bytes.Span);
                    break;

                case QRCodeSegmentMode.Eci:
                    Flush();
                    assignmentNumber = segment.EciAssignmentNumber;
                    break;

                default:
                    Flush();
                    text.Append(segment.Text);
                    break;
            }
        }

        Flush();
        return text.ToString();

        // A character can span two byte segments, for instance across the symbols of a structured append sequence
        void Flush()
        {
            if (pendingBytes.Count is 0)
                return;

            text.Append(GetString(pendingBytes.ToArray(), assignmentNumber));
            pendingBytes.Clear();
        }
    }

    // ISO/IEC 15424 and AIM ECI specification: the character sets of the assignment numbers 0 to 30 and 170
    private static Encoding? GetEncoding(int assignmentNumber)
    {
        var codePage = assignmentNumber switch
        {
            0 or 2 => 437,
            1 or 3 => 28591,
            >= 4 and <= 11 => 28588 + assignmentNumber, // ISO/IEC 8859-2 to 8859-9
            13 => 874, // ISO/IEC 8859-11 (Thai)
            15 => 28603, // ISO/IEC 8859-13
            17 => 28605, // ISO/IEC 8859-15
            20 => 932,
            21 => 1250,
            22 => 1251,
            23 => 1252,
            24 => 1256,
            25 => 1201, // UTF-16 big endian
            26 => 65001,
            27 or 170 => 20127,
            28 => 950,
            29 => 54936,
            30 => 51949,
            _ => 0,
        };

        if (codePage is 0)
            return null;

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
