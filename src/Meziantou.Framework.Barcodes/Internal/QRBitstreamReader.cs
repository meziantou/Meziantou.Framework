using Meziantou.Framework.Barcodes.Internal.MicroQR;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Reads the segments of the data codewords of standard QR, Micro QR and rMQR codes (ISO/IEC 18004, 7.4 and
/// ISO/IEC 23941, 7.4): the inverse of <see cref="QRCodeSegment.Write"/>.
/// </summary>
internal static class QRBitstreamReader
{
    private const string AlphanumericCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    /// <summary>Reads the segments, or returns an error message.</summary>
    public static string? TryRead(byte[] dataCodewords, int dataBitCount, QRCodeType type, int version, List<QRCodeSegment> segments, out QRCodeStructuredAppend? structuredAppend)
    {
        structuredAppend = null;
        var reader = new BitReader(dataCodewords, dataBitCount);
        var modeIndicatorBits = QRSymbolFormat.GetModeIndicatorBits(type, version);
        var terminatorBits = type switch
        {
            QRCodeType.Standard => 4,
            QRCodeType.MicroQR => MicroQRVersion.GetTerminatorBits(version),
            _ => 3,
        };

        while (reader.Remaining > 0)
        {
            // The terminator is a run of zeros, shortened when the data fills the symbol (ISO/IEC 18004, 7.4.9). In Micro QR
            // codes, it reads as an empty numeric segment.
            if (reader.Peek(Math.Min(terminatorBits, reader.Remaining)) is 0)
                break;

            var indicator = reader.Read(modeIndicatorBits);
            if (indicator < 0)
                break;

            var mode = GetMode(type, version, indicator);
            switch (mode)
            {
                case EncodingMode.StructuredAppend:
                    if (segments.Count > 0 || structuredAppend is not null)
                        return "The structured append header is not at the start of the data.";

                    var index = reader.Read(4);
                    var count = reader.Read(4);
                    var parity = reader.Read(8);
                    if (parity < 0)
                        return "The data ends in the structured append header.";

                    if (index > count)
                        return "The structured append header has a symbol position greater than the number of symbols.";

                    structuredAppend = new QRCodeStructuredAppend(index, count + 1, (byte)parity);
                    break;

                case EncodingMode.Eci:
                    var assignmentNumber = ReadEciDesignator(reader);
                    if (assignmentNumber < 0)
                        return "The ECI designator is not valid.";

                    segments.Add(QRCodeSegment.FromDecoded(EncodingMode.Eci, text: null, bytes: null, assignmentNumber));
                    break;

                case EncodingMode.Numeric or EncodingMode.Alphanumeric or EncodingMode.Byte or EncodingMode.Kanji:
                    var characterCount = reader.Read(QRSymbolFormat.GetCharacterCountBits(type, version, mode.Value));
                    if (characterCount < 0)
                        return "The data ends in a character count indicator.";

                    var error = ReadSegment(reader, mode.Value, characterCount, segments);
                    if (error is not null)
                        return error;

                    break;

                default:
                    return $"The mode indicator {indicator} is not supported.";
            }
        }

        return null;
    }

    private static EncodingMode? GetMode(QRCodeType type, int version, int indicator)
    {
        return type switch
        {
            QRCodeType.Standard => indicator switch
            {
                1 => EncodingMode.Numeric,
                2 => EncodingMode.Alphanumeric,
                3 => EncodingMode.StructuredAppend,
                4 => EncodingMode.Byte,
                7 => EncodingMode.Eci,
                8 => EncodingMode.Kanji,
                _ => null,
            },
            QRCodeType.MicroQR => indicator switch
            {
                0 => EncodingMode.Numeric,
                1 when version >= 2 => EncodingMode.Alphanumeric,
                2 when version >= 3 => EncodingMode.Byte,
                3 when version >= 3 => EncodingMode.Kanji,
                _ => null,
            },
            _ => indicator switch
            {
                1 => EncodingMode.Numeric,
                2 => EncodingMode.Alphanumeric,
                3 => EncodingMode.Byte,
                4 => EncodingMode.Kanji,
                7 => EncodingMode.Eci,
                _ => null,
            },
        };
    }

    // ISO/IEC 18004, 7.4.2.2: 0bbbbbbb, 10bbbbbb bbbbbbbb, or 110bbbbb bbbbbbbb bbbbbbbb
    private static int ReadEciDesignator(BitReader reader)
    {
        var first = reader.Read(8);
        if (first < 0)
            return -1;

        if ((first & 0x80) is 0)
            return first;

        if ((first & 0xC0) is 0x80)
        {
            var second = reader.Read(8);
            return second < 0 ? -1 : ((first & 0x3F) << 8) | second;
        }

        if ((first & 0xE0) is 0xC0)
        {
            var rest = reader.Read(16);
            if (rest < 0)
                return -1;

            var value = ((first & 0x1F) << 16) | rest;
            return value <= 999999 ? value : -1;
        }

        return -1;
    }

    private static string? ReadSegment(BitReader reader, EncodingMode mode, int characterCount, List<QRCodeSegment> segments)
    {
        switch (mode)
        {
            case EncodingMode.Numeric:
            {
                var digits = new char[characterCount];
                for (var i = 0; i < characterCount; i += 3)
                {
                    var groupLength = Math.Min(3, characterCount - i);
                    var value = reader.Read(groupLength switch { 3 => 10, 2 => 7, _ => 4 });
                    if (value < 0)
                        return "The data ends in a numeric segment.";

                    if (value >= groupLength switch { 3 => 1000, 2 => 100, _ => 10 })
                        return "A numeric segment has a group of digits out of range.";

                    for (var j = groupLength - 1; j >= 0; j--)
                    {
                        digits[i + j] = (char)('0' + (value % 10));
                        value /= 10;
                    }
                }

                segments.Add(QRCodeSegment.FromDecoded(EncodingMode.Numeric, new string(digits), bytes: null, eciAssignmentNumber: 0));
                return null;
            }

            case EncodingMode.Alphanumeric:
            {
                var characters = new char[characterCount];
                for (var i = 0; i < characterCount; i += 2)
                {
                    if (i + 1 < characterCount)
                    {
                        var value = reader.Read(11);
                        if (value < 0)
                            return "The data ends in an alphanumeric segment.";

                        if (value >= 45 * 45)
                            return "An alphanumeric segment has a pair of characters out of range.";

                        characters[i] = AlphanumericCharacters[value / 45];
                        characters[i + 1] = AlphanumericCharacters[value % 45];
                    }
                    else
                    {
                        var value = reader.Read(6);
                        if (value < 0)
                            return "The data ends in an alphanumeric segment.";

                        if (value >= 45)
                            return "An alphanumeric segment has a character out of range.";

                        characters[i] = AlphanumericCharacters[value];
                    }
                }

                segments.Add(QRCodeSegment.FromDecoded(EncodingMode.Alphanumeric, new string(characters), bytes: null, eciAssignmentNumber: 0));
                return null;
            }

            case EncodingMode.Byte:
            {
                if (characterCount * 8 > reader.Remaining)
                    return "The data ends in a byte segment.";

                var bytes = new byte[characterCount];
                for (var i = 0; i < characterCount; i++)
                {
                    bytes[i] = (byte)reader.Read(8);
                }

                segments.Add(QRCodeSegment.FromDecoded(EncodingMode.Byte, text: null, bytes, eciAssignmentNumber: 0));
                return null;
            }

            default:
            {
                // ISO/IEC 18004, 7.4.6: the 13-bit value is the Shift JIS code minus 0x8140 or 0xC140, in base 0xC0
                if (characterCount * 13 > reader.Remaining)
                    return "The data ends in a Kanji segment.";

                var shiftJis = new byte[characterCount * 2];
                for (var i = 0; i < characterCount; i++)
                {
                    var value = reader.Read(13);
                    var code = ((value / 0xC0) << 8) | (value % 0xC0);
                    code += code < 0x1F00 ? 0x8140 : 0xC140;
                    shiftJis[2 * i] = (byte)(code >> 8);
                    shiftJis[(2 * i) + 1] = (byte)code;
                }

                var text = DataAnalyzer.GetShiftJisString(shiftJis);
                if (text is null)
                    return "A Kanji segment has a character that is not in the Shift JIS character set.";

                segments.Add(QRCodeSegment.FromDecoded(EncodingMode.Kanji, text, bytes: null, eciAssignmentNumber: 0));
                return null;
            }
        }
    }
}
