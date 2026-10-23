using System.Buffers.Binary;
using System.Globalization;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Writes the subset of a CFF table as a CID-keyed font whose CIDs are the glyph identifiers: Adobe Technical Note #5176,
/// "The Compact Font Format Specification". A font whose glyphs have names becomes a CID-keyed font with one font DICT;
/// the charset maps each glyph to the CID of the same value, so that a PDF content stream can show the glyphs by their
/// identifiers (ISO 32000-2, 9.7.4.2). The charstrings of the glyphs that are not kept are <c>endchar</c>; the global
/// and local subroutines are copied whole.
/// </summary>
internal static class CffSubsetWriter
{
    private const int Escape = CffDictReader.Escape;
    private const int CharsetOperator = 15;
    private const int EncodingOperator = 16;
    private const int CharStringsOperator = 17;
    private const int PrivateOperator = 18;
    private const int SubrsOperator = 19;
    private const int RosOperator = Escape + 30;
    private const int CidCountOperator = Escape + 34;
    private const int FdArrayOperator = Escape + 36;
    private const int FdSelectOperator = Escape + 37;

    // The first identifier of the strings of a font: the 391 standard strings come before (Appendix A)
    private const int FirstCustomString = 391;

    // The operators of the Top DICT whose operand is a string identifier or a position, and the identifiers of the
    // original font: they are not copied (table 9)
    private static readonly HashSet<int> NotCopiedTopOperators =
    [
        0, 1, 2, 3, 4, 13, 14, CharsetOperator, EncodingOperator, CharStringsOperator, PrivateOperator,
        Escape + 0, Escape + 20, Escape + 21, Escape + 22, RosOperator, CidCountOperator, Escape + 35, FdArrayOperator, FdSelectOperator, Escape + 38,
    ];

    public static byte[] Write(ReadOnlySpan<byte> table, SortedSet<int> glyphs, string fontName)
    {
        var headerSize = FontBinary.UInt8(table, 2);
        var names = CffIndex.Read(table, headerSize);
        var topDicts = CffIndex.Read(table, names.End);
        var strings = CffIndex.Read(table, topDicts.End);
        var globalSubroutinesStart = strings.End;
        var globalSubroutines = CffIndex.Read(table, globalSubroutinesStart);
        if (topDicts.Count != 1)
            throw FontErrors.InvalidTable(FontTags.Cff, $"holds {topDicts.Count} fonts; the table of an OpenType font holds one.");

        var topEntries = ReadDict(topDicts.Get(table, 0));
        var charStrings = CffIndex.Read(table, ToOffset(GetOperand(topEntries, CharStringsOperator, 0)));
        var glyphCount = charStrings.Count;
        var isCidKeyed = topEntries.Exists(e => e.Operator == RosOperator);

        // The font DICTs: their entries without the private DICT, the entries of the private DICT without the local
        // subroutines, and the local subroutines
        var fonts = new List<(List<(int Operator, double[] Operands)> FontEntries, List<(int Operator, double[] Operands)> PrivateEntries, byte[] Subroutines)>();
        byte[] fdSelect;
        if (isCidKeyed)
        {
            var fdArray = CffIndex.Read(table, ToOffset(GetOperand(topEntries, FdArrayOperator, 0)));
            for (var i = 0; i < fdArray.Count; i++)
            {
                var fontEntries = ReadDict(fdArray.Get(table, i));
                fonts.Add(ReadPrivate(table, fontEntries));
            }

            fdSelect = ReadFdSelect(table, ToOffset(GetOperand(topEntries, FdSelectOperator, 0)), glyphCount, fdArray.Count);
        }
        else
        {
            fonts.Add(ReadPrivate(table, topEntries));

            // Format 3 with one range: every glyph uses the font DICT 0
            fdSelect = new byte[8];
            fdSelect[0] = 3;
            BinaryPrimitives.WriteUInt16BigEndian(fdSelect.AsSpan(1), 1);
            BinaryPrimitives.WriteUInt16BigEndian(fdSelect.AsSpan(3), 0);
            fdSelect[5] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(fdSelect.AsSpan(6), (ushort)glyphCount);
        }

        // charset: glyph i has the CID i (format 2, one range from CID 1)
        var charset = glyphCount > 1 ? new byte[5] : [0];
        if (glyphCount > 1)
        {
            charset[0] = 2;
            BinaryPrimitives.WriteUInt16BigEndian(charset.AsSpan(1), 1);
            BinaryPrimitives.WriteUInt16BigEndian(charset.AsSpan(3), (ushort)(glyphCount - 2));
        }

        var charStringItems = new byte[glyphCount][];
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            charStringItems[glyph] = glyph == 0 || glyphs.Contains(glyph) ? charStrings.Get(table, glyph).ToArray() : [14];
        }

        var nameIndex = WriteIndex([System.Text.Encoding.ASCII.GetBytes(fontName)]);
        var stringIndex = WriteIndex([System.Text.Encoding.ASCII.GetBytes("Adobe"), System.Text.Encoding.ASCII.GetBytes("Identity")]);
        var globalSubroutinesBytes = table[globalSubroutinesStart..globalSubroutines.End].ToArray();
        var charStringsIndex = WriteIndex(charStringItems);

        // The private DICTs, each followed by its local subroutines: the offset of the subroutines is relative to the
        // start of the private DICT
        var privates = new List<byte[]>();
        foreach (var (_, privateEntries, subroutines) in fonts)
        {
            var dict = new List<byte>();
            WriteEntries(dict, privateEntries);
            if (subroutines.Length > 0)
            {
                var length = dict.Count + 6;
                WriteFixedInteger(dict, length);
                WriteOperator(dict, SubrsOperator);
            }

            privates.Add([.. dict, .. subroutines]);
        }

        // Every position is written on five bytes: the lengths do not change when the positions are known
        var topWithoutOffsets = new List<(int Operator, double[] Operands)> { (RosOperator, [FirstCustomString, FirstCustomString + 1, 0]) };
        topWithoutOffsets.AddRange(topEntries.Where(e => !NotCopiedTopOperators.Contains(e.Operator)));
        topWithoutOffsets.Add((CidCountOperator, [glyphCount]));

        byte[] BuildTopDict(int charsetOffset, int fdSelectOffset, int charStringsOffset, int fdArrayOffset)
        {
            var dict = new List<byte>();
            WriteEntries(dict, topWithoutOffsets);
            WriteFixedInteger(dict, charsetOffset);
            WriteOperator(dict, CharsetOperator);
            WriteFixedInteger(dict, fdSelectOffset);
            WriteOperator(dict, FdSelectOperator);
            WriteFixedInteger(dict, charStringsOffset);
            WriteOperator(dict, CharStringsOperator);
            WriteFixedInteger(dict, fdArrayOffset);
            WriteOperator(dict, FdArrayOperator);
            return [.. dict];
        }

        byte[] BuildFdArray(int firstPrivateOffset)
        {
            var items = new List<byte[]>();
            var privateOffset = firstPrivateOffset;
            for (var i = 0; i < fonts.Count; i++)
            {
                var dict = new List<byte>();
                WriteEntries(dict, fonts[i].FontEntries.Where(e => e.Operator is not (PrivateOperator or Escape + 38)));
                WriteFixedInteger(dict, privates[i].Length - fonts[i].Subroutines.Length);
                WriteFixedInteger(dict, privateOffset);
                WriteOperator(dict, PrivateOperator);
                items.Add([.. dict]);
                privateOffset += privates[i].Length;
            }

            return WriteIndex(items);
        }

        const int HeaderLength = 4;
        var topDictIndexLength = WriteIndex([BuildTopDict(0, 0, 0, 0)]).Length;
        var charsetOffset = HeaderLength + nameIndex.Length + topDictIndexLength + stringIndex.Length + globalSubroutinesBytes.Length;
        var fdSelectOffset = charsetOffset + charset.Length;
        var charStringsOffset = fdSelectOffset + fdSelect.Length;
        var fdArrayOffset = charStringsOffset + charStringsIndex.Length;
        var fdArrayLength = BuildFdArray(0).Length;
        var fdArrayIndex = BuildFdArray(fdArrayOffset + fdArrayLength);
        var topDictIndex = WriteIndex([BuildTopDict(charsetOffset, fdSelectOffset, charStringsOffset, fdArrayOffset)]);

        var output = new List<byte>(fdArrayOffset + fdArrayLength + privates.Sum(p => p.Length));
        output.AddRange([1, 0, HeaderLength, 4]);
        output.AddRange(nameIndex);
        output.AddRange(topDictIndex);
        output.AddRange(stringIndex);
        output.AddRange(globalSubroutinesBytes);
        output.AddRange(charset);
        output.AddRange(fdSelect);
        output.AddRange(charStringsIndex);
        output.AddRange(fdArrayIndex);
        foreach (var item in privates)
        {
            output.AddRange(item);
        }

        return [.. output];
    }

    private static (List<(int Operator, double[] Operands)> FontEntries, List<(int Operator, double[] Operands)> PrivateEntries, byte[] Subroutines) ReadPrivate(ReadOnlySpan<byte> table, List<(int Operator, double[] Operands)> fontEntries)
    {
        var privateEntry = fontEntries.FindLast(e => e.Operator == PrivateOperator && e.Operands.Length >= 2);
        if (privateEntry.Operands is null)
            return (fontEntries, [], []);

        var privateLength = ToOffset(privateEntry.Operands[0]);
        var privateOffset = ToOffset(privateEntry.Operands[1]);
        var privateEntries = ReadDict(FontBinary.Slice(table, privateOffset, privateLength));
        var subroutinesEntry = privateEntries.FindLast(e => e.Operator == SubrsOperator && e.Operands.Length >= 1);
        byte[] subroutines = [];
        if (subroutinesEntry.Operands is not null)
        {
            var start = privateOffset + ToOffset(subroutinesEntry.Operands[0]);
            var index = CffIndex.Read(table, start);
            subroutines = table[(int)start..index.End].ToArray();
        }

        return (fontEntries, [.. privateEntries.Where(e => e.Operator != SubrsOperator)], subroutines);
    }

    /// <summary>Copies the font DICT selector: format 0, 3 or 4 (section 19), checked against the number of glyphs and of font DICTs.</summary>
    private static byte[] ReadFdSelect(ReadOnlySpan<byte> table, long offset, int glyphCount, int fontCount)
    {
        var start = (int)offset;
        var format = FontBinary.UInt8(table, start);
        int length;
        switch (format)
        {
            case 0:
                length = 1 + glyphCount;
                break;
            case 3:
                length = 1 + 2 + (FontBinary.UInt16(table, start + 1) * 3) + 2;
                break;
            case 4:
                length = 1 + 4 + (int)Math.Min(FontBinary.UInt32(table, start + 1) * 6L, int.MaxValue - 10) + 4;
                break;
            default:
                throw FontErrors.InvalidTable(FontTags.Cff, $"has a font DICT selector of the unknown format {format}.");
        }

        var result = FontBinary.Slice(table, start, length).ToArray();
        if (format == 0)
        {
            foreach (var fd in result.AsSpan(1))
            {
                if (fd >= fontCount)
                    throw FontErrors.InvalidTable(FontTags.Cff, "selects a font DICT that does not exist.");
            }
        }

        return result;
    }

    private static List<(int Operator, double[] Operands)> ReadDict(ReadOnlySpan<byte> data)
    {
        var entries = new List<(int, double[])>();
        Span<double> operands = stackalloc double[CffDictReader.MaxOperands];
        var reader = new CffDictReader(data);
        while (reader.TryRead(operands, out var count, out var @operator))
        {
            entries.Add((@operator, operands[..count].ToArray()));
        }

        return entries;
    }

    private static double GetOperand(List<(int Operator, double[] Operands)> entries, int @operator, int index)
    {
        var entry = entries.FindLast(e => e.Operator == @operator);
        if (entry.Operands is null || entry.Operands.Length <= index)
            throw FontErrors.InvalidTable(FontTags.Cff, $"does not have the DICT entry {@operator}.");

        return entry.Operands[index];
    }

    private static long ToOffset(double value)
    {
        if (!(value >= 0 && value <= int.MaxValue) || value != Math.Floor(value))
            throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT with an offset that is not a position of the table.");

        return (long)value;
    }

    private static void WriteEntries(List<byte> output, IEnumerable<(int Operator, double[] Operands)> entries)
    {
        foreach (var (@operator, operands) in entries)
        {
            foreach (var operand in operands)
            {
                WriteNumber(output, operand);
            }

            WriteOperator(output, @operator);
        }
    }

    private static void WriteOperator(List<byte> output, int @operator)
    {
        if (@operator >= Escape)
        {
            output.Add(12);
            output.Add((byte)(@operator - Escape));
        }
        else
        {
            output.Add((byte)@operator);
        }
    }

    /// <summary>Writes an integer on five bytes (operand 29), whatever its value.</summary>
    private static void WriteFixedInteger(List<byte> output, int value)
    {
        output.Add(29);
        output.Add((byte)(value >> 24));
        output.Add((byte)(value >> 16));
        output.Add((byte)(value >> 8));
        output.Add((byte)value);
    }

    /// <summary>Writes a number with the shortest of the encodings of table 3, or as a real number (table 5).</summary>
    private static void WriteNumber(List<byte> output, double value)
    {
        if (value == Math.Floor(value) && value is >= int.MinValue and <= int.MaxValue)
        {
            var integer = (int)value;
            switch (integer)
            {
                case >= -107 and <= 107:
                    output.Add((byte)(integer + 139));
                    return;
                case >= 108 and <= 1131:
                    output.Add((byte)(((integer - 108) >> 8) + 247));
                    output.Add((byte)((integer - 108) & 0xFF));
                    return;
                case >= -1131 and <= -108:
                    output.Add((byte)(((-integer - 108) >> 8) + 251));
                    output.Add((byte)((-integer - 108) & 0xFF));
                    return;
                case >= short.MinValue and <= short.MaxValue:
                    output.Add(28);
                    output.Add((byte)(integer >> 8));
                    output.Add((byte)integer);
                    return;
                default:
                    WriteFixedInteger(output, integer);
                    return;
            }
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var nibbles = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case >= '0' and <= '9':
                    nibbles.Add(c - '0');
                    break;
                case '.':
                    nibbles.Add(0x0A);
                    break;
                case '-':
                    nibbles.Add(0x0E);
                    break;
                case 'E' or 'e':
                    if (i + 1 < text.Length && text[i + 1] == '-')
                    {
                        nibbles.Add(0x0C);
                        i++;
                    }
                    else
                    {
                        nibbles.Add(0x0B);
                        if (i + 1 < text.Length && text[i + 1] == '+')
                        {
                            i++;
                        }
                    }

                    break;
            }
        }

        nibbles.Add(0x0F);
        if (nibbles.Count % 2 == 1)
        {
            nibbles.Add(0x0F);
        }

        output.Add(30);
        for (var i = 0; i < nibbles.Count; i += 2)
        {
            output.Add((byte)((nibbles[i] << 4) | nibbles[i + 1]));
        }
    }

    /// <summary>Writes an INDEX (section 5) with the shortest size of offsets.</summary>
    private static byte[] WriteIndex(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0)
            return [0, 0];

        var dataLength = items.Sum(i => (long)i.Length);
        var offsetSize = dataLength + 1 <= 0xFF ? 1 : dataLength + 1 <= 0xFFFF ? 2 : dataLength + 1 <= 0xFFFFFF ? 3 : 4;
        var output = new byte[3 + ((items.Count + 1) * offsetSize) + dataLength];
        BinaryPrimitives.WriteUInt16BigEndian(output, (ushort)items.Count);
        output[2] = (byte)offsetSize;
        var offset = 1L;
        var position = 3;
        var dataPosition = 3 + ((items.Count + 1) * offsetSize);
        for (var i = 0; i <= items.Count; i++)
        {
            for (var b = offsetSize - 1; b >= 0; b--)
            {
                output[position++] = (byte)(offset >> (b * 8));
            }

            if (i < items.Count)
            {
                items[i].CopyTo(output, dataPosition);
                dataPosition += items[i].Length;
                offset += items[i].Length;
            }
        }

        return output;
    }
}
