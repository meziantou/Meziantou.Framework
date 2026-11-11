using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// Reads back the data codewords of a standard QR code (ISO/IEC 18004, 7.4): the structured append header, the ECI
/// designators and the data segments, then checks the terminator and the pad codewords. The sizes of the character count
/// indicators come from the standard, not from the library.
/// </summary>
internal static class QRDataDecoder
{
    private const string AlphanumericCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    public static QRDecodedData Decode(byte[] codewords, int version)
    {
        var reader = new BitReader(codewords);
        (int Index, int Count, int Parity)? structuredAppend = null;
        var segments = new List<QRDecodedSegment>();
        while (reader.Remaining >= 4)
        {
            var mode = reader.Read(4);
            if (mode is 0)
                break;

            switch (mode)
            {
                case 3:
                    FuzzAssert.True(structuredAppend is null && segments.Count is 0, "The structured append header is not at the start of the data");
                    structuredAppend = (reader.Read(4), reader.Read(4) + 1, reader.Read(8));
                    break;

                case 7:
                    var first = reader.Read(8);
                    var assignmentNumber = (first & 0x80) is 0 ? first
                        : (first & 0xC0) is 0x80 ? ((first & 0x3F) << 8) | reader.Read(8)
                        : ((first & 0x1F) << 16) | reader.Read(16);
                    segments.Add(new QRDecodedSegment(QRCodeSegmentMode.Eci, [], assignmentNumber));
                    break;

                case 1:
                    var digits = new List<byte>();
                    for (var remaining = reader.Read(GetCountBits(version, mode)); remaining > 0; remaining -= 3)
                    {
                        var count = Math.Min(remaining, 3);
                        var value = reader.Read(count switch { 3 => 10, 2 => 7, _ => 4 });
                        FuzzAssert.True(value < count switch { 3 => 1000, 2 => 100, _ => 10 }, "A numeric group is out of range");
                        digits.AddRange(Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture).PadLeft(count, '0')));
                    }

                    segments.Add(new QRDecodedSegment(QRCodeSegmentMode.Numeric, [.. digits], 0));
                    break;

                case 2:
                    var characters = new List<byte>();
                    for (var remaining = reader.Read(GetCountBits(version, mode)); remaining > 0; remaining -= 2)
                    {
                        if (remaining >= 2)
                        {
                            var value = reader.Read(11);
                            FuzzAssert.True(value < 45 * 45, "An alphanumeric pair is out of range");
                            characters.Add((byte)AlphanumericCharacters[value / 45]);
                            characters.Add((byte)AlphanumericCharacters[value % 45]);
                        }
                        else
                        {
                            var value = reader.Read(6);
                            FuzzAssert.True(value < 45, "An alphanumeric character is out of range");
                            characters.Add((byte)AlphanumericCharacters[value]);
                        }
                    }

                    segments.Add(new QRDecodedSegment(QRCodeSegmentMode.Alphanumeric, [.. characters], 0));
                    break;

                case 4:
                    var bytes = new byte[reader.Read(GetCountBits(version, mode))];
                    for (var i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] = (byte)reader.Read(8);
                    }

                    segments.Add(new QRDecodedSegment(QRCodeSegmentMode.Byte, bytes, 0));
                    break;

                case 8:
                    // ISO/IEC 18004, 7.4.6: the 13-bit value is the Shift JIS code minus 0x8140 or 0xC140, in base 0xC0
                    var kanjiCount = reader.Read(GetCountBits(version, mode));
                    var shiftJis = new byte[kanjiCount * 2];
                    for (var i = 0; i < kanjiCount; i++)
                    {
                        var value = reader.Read(13);
                        var code = ((value / 0xC0) << 8) | (value % 0xC0);
                        code += code < 0x1F00 ? 0x8140 : 0xC140;
                        shiftJis[2 * i] = (byte)(code >> 8);
                        shiftJis[(2 * i) + 1] = (byte)code;
                    }

                    segments.Add(new QRDecodedSegment(QRCodeSegmentMode.Kanji, shiftJis, 0));
                    break;

                default:
                    FuzzAssert.Fail($"Unexpected mode indicator {mode}");
                    break;
            }
        }

        // The terminator and the bits up to a codeword boundary are 0, then the pad codewords alternate 0xEC and 0x11
        while (reader.Position % 8 is not 0)
        {
            FuzzAssert.Equal(0, reader.Read(1), "The bits after the terminator");
        }

        for (var pad = 0; reader.Remaining > 0; pad++)
        {
            FuzzAssert.Equal(pad % 2 is 0 ? 0xEC : 0x11, reader.Read(8), "The pad codeword");
        }

        return new QRDecodedData(structuredAppend, segments);
    }

    private static int GetCountBits(int version, int mode)
    {
        var group = version switch
        {
            <= 9 => 0,
            <= 26 => 1,
            _ => 2,
        };

        // ISO/IEC 18004, Table 3
        return (mode, group) switch
        {
            (1, 0) => 10,
            (1, 1) => 12,
            (1, _) => 14,
            (2, 0) => 9,
            (2, 1) => 11,
            (2, _) => 13,
            (4, 0) => 8,
            (4, _) => 16,
            (_, 0) => 8,
            (_, 1) => 10,
            _ => 12,
        };
    }

    private sealed class BitReader(byte[] data)
    {
        public int Position { get; private set; }

        public int Remaining => (data.Length * 8) - Position;

        public int Read(int bitCount)
        {
            FuzzAssert.True(bitCount <= Remaining, "The data ends in the middle of a field");
            var value = 0;
            for (var i = 0; i < bitCount; i++)
            {
                value = (value << 1) | ((data[Position >> 3] >> (7 - (Position & 7))) & 1);
                Position++;
            }

            return value;
        }
    }
}
