namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// A Huffman table of JBIG2 (T.88, Annex B): lines of a prefix length, a range length and the low value of the range,
/// with the lower and upper range lines (32-bit offsets) and an out-of-band line. The codes are assigned by B.3.
/// </summary>
internal sealed class Jbig2HuffmanTable
{
    private const int MaxPrefixLength = 32;

    private readonly Dictionary<(int Length, uint Code), Line> _lines = [];
    private readonly int _maxLength;
    private int? Constant { get; init; }

    private Jbig2HuffmanTable(List<Line> lines)
    {
        // B.3: the codes of each length are consecutive, in the order of the lines
        var counts = new int[MaxPrefixLength + 2];
        foreach (var line in lines)
        {
            if (line.PrefixLength is < 0 or > MaxPrefixLength)
                throw new InvalidPdfContentException("A JBIG2 Huffman table has a prefix longer than 32 bits");

            counts[line.PrefixLength]++;
            _maxLength = Math.Max(_maxLength, line.PrefixLength);
        }

        counts[0] = 0;
        long firstCode = 0;
        for (var length = 1; length <= _maxLength; length++)
        {
            firstCode = (firstCode + counts[length - 1]) * 2;
            var code = firstCode;
            foreach (var line in lines)
            {
                if (line.PrefixLength == length)
                {
                    if (code >= 1L << length)
                        throw new InvalidPdfContentException("A JBIG2 Huffman table has more codes than its prefix lengths allow");

                    _lines[(length, (uint)code)] = line;
                    code++;
                }
            }
        }
    }

    public static Jbig2HuffmanTable[] StandardTables { get; } = CreateStandardTables();

    /// <summary>Creates the table of codes of a fixed length: the values 0 to <paramref name="count"/> - 1 on <paramref name="codeLength"/> bits.</summary>
    public static Jbig2HuffmanTable CreateFixedLength(int codeLength, int count)
    {
        if (codeLength == 0)
            return new Jbig2HuffmanTable([]) { Constant = 0 };

        var lines = new List<Line>(count);
        for (var i = 0; i < count; i++)
        {
            lines.Add(new Line(codeLength, 0, i, LineKind.Normal));
        }

        return new Jbig2HuffmanTable(lines);
    }

    /// <summary>Decodes a value; <see langword="null"/> for the out-of-band value.</summary>
    public int? Decode(Jbig2BitReader reader)
    {
        if (Constant is { } constant)
            return constant;

        uint code = 0;
        for (var length = 1; length <= _maxLength; length++)
        {
            code = (code << 1) | (uint)reader.ReadBit();
            if (_lines.TryGetValue((length, code), out var line))
            {
                if (line.Kind == LineKind.OutOfBand)
                    return null;

                var offset = (long)reader.ReadBits(line.RangeLength);
                var value = line.Kind == LineKind.Lower ? line.RangeLow - offset : line.RangeLow + offset;
                if (value is < int.MinValue or > int.MaxValue)
                    throw new InvalidPdfContentException("A JBIG2 Huffman value is out of range");

                return (int)value;
            }
        }

        throw new InvalidPdfContentException("The JBIG2 data has a Huffman code that is not in its table");
    }

    /// <summary>Decodes a value that is not out of band.</summary>
    public int DecodeValue(Jbig2BitReader reader) => Decode(reader) ?? throw new InvalidPdfContentException("The JBIG2 data has an out-of-band value where a value is expected");

    /// <summary>Reads a table of a tables segment (7.4.13).</summary>
    public static Jbig2HuffmanTable Read(byte[] data, int offset, int length)
    {
        if (length < 9)
            throw new InvalidPdfContentException("A JBIG2 tables segment is too short");

        var flags = data[offset];
        var hasOutOfBand = (flags & 1) != 0;
        var prefixSize = ((flags >> 1) & 7) + 1;
        var rangeSize = ((flags >> 4) & 7) + 1;
        var low = ReadInt32(data, offset + 1);
        var high = ReadInt32(data, offset + 5);
        var reader = new Jbig2BitReader(data, offset + 9, length - 9);
        var lines = new List<Line>();
        long current = low;
        while (current < high)
        {
            if (reader.IsPastEnd || lines.Count > 65536)
                throw new InvalidPdfContentException("A JBIG2 tables segment is truncated");

            var prefix = (int)reader.ReadBits(prefixSize);
            var range = (int)reader.ReadBits(rangeSize);
            if (range > 32)
                throw new InvalidPdfContentException("A JBIG2 tables segment has a range longer than 32 bits");

            lines.Add(new Line(prefix, range, current, LineKind.Normal));
            current += 1L << range;
        }

        lines.Add(new Line((int)reader.ReadBits(prefixSize), 32, low - 1L, LineKind.Lower));
        lines.Add(new Line((int)reader.ReadBits(prefixSize), 32, high, LineKind.Upper));
        if (hasOutOfBand)
        {
            lines.Add(new Line((int)reader.ReadBits(prefixSize), 0, 0, LineKind.OutOfBand));
        }

        return new Jbig2HuffmanTable(lines);
    }

    /// <summary>Creates a table of the symbol identifiers of a text region (7.4.3.1.7): their code lengths, with the prefix lengths of the code lengths given before them.</summary>
    public static Jbig2HuffmanTable ReadSymbolIdTable(Jbig2BitReader reader, int symbolCount)
    {
        var runCodeLines = new List<Line>(35);
        for (var i = 0; i < 35; i++)
        {
            runCodeLines.Add(new Line((int)reader.ReadBits(4), 0, i, LineKind.Normal));
        }

        var runCodes = new Jbig2HuffmanTable(runCodeLines);
        var lengths = new List<Line>(symbolCount);
        var previous = 0;
        while (lengths.Count < symbolCount)
        {
            if (reader.IsPastEnd)
                throw new InvalidPdfContentException("The symbol identifier table of a JBIG2 text region is truncated");

            var runCode = runCodes.DecodeValue(reader);
            int repeat;
            int value;
            switch (runCode)
            {
                case < 32:
                    repeat = 1;
                    value = runCode;
                    break;
                case 32:
                    repeat = (int)reader.ReadBits(2) + 3;
                    value = previous;
                    break;
                case 33:
                    repeat = (int)reader.ReadBits(3) + 3;
                    value = 0;
                    break;
                default:
                    repeat = (int)reader.ReadBits(7) + 11;
                    value = 0;
                    break;
            }

            for (var i = 0; i < repeat && lengths.Count < symbolCount; i++)
            {
                lengths.Add(new Line(value, 0, lengths.Count, LineKind.Normal));
            }

            previous = value;
        }

        reader.AlignToByte();
        return new Jbig2HuffmanTable(lengths);
    }

    private static int ReadInt32(byte[] data, int offset) => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static Jbig2HuffmanTable[] CreateStandardTables()
    {
        // The tables B.1 to B.15: (prefix length, range length, range low) per line, then the lower range line, the
        // upper range line and the out-of-band line when the table has them
        static Jbig2HuffmanTable Table((int Prefix, int Range, int Low)[] lines, (int Prefix, int Low)? lower, (int Prefix, int Low)? upper, int? outOfBand)
        {
            var result = new List<Line>();
            foreach (var (prefix, range, low) in lines)
            {
                result.Add(new Line(prefix, range, low, LineKind.Normal));
            }

            if (lower is { } lowerLine)
            {
                result.Add(new Line(lowerLine.Prefix, 32, lowerLine.Low, LineKind.Lower));
            }

            if (upper is { } upperLine)
            {
                result.Add(new Line(upperLine.Prefix, 32, upperLine.Low, LineKind.Upper));
            }

            if (outOfBand is { } outOfBandPrefix)
            {
                result.Add(new Line(outOfBandPrefix, 0, 0, LineKind.OutOfBand));
            }

            return new Jbig2HuffmanTable(result);
        }

        return
        [
            null!,
            Table([(1, 4, 0), (2, 8, 16), (3, 16, 272)], null, (3, 65808), null),
            Table([(1, 0, 0), (2, 0, 1), (3, 0, 2), (4, 3, 3), (5, 6, 11)], null, (6, 75), 6),
            Table([(8, 8, -256), (1, 0, 0), (2, 0, 1), (3, 0, 2), (4, 3, 3), (5, 6, 11)], (8, -257), (7, 75), 6),
            Table([(1, 0, 1), (2, 0, 2), (3, 0, 3), (4, 3, 4), (5, 6, 12)], null, (5, 76), null),
            Table([(7, 8, -255), (1, 0, 1), (2, 0, 2), (3, 0, 3), (4, 3, 4), (5, 6, 12)], (7, -256), (6, 76), null),
            Table([(5, 10, -2048), (4, 9, -1024), (4, 8, -512), (4, 7, -256), (5, 6, -128), (5, 5, -64), (4, 5, -32), (2, 7, 0), (3, 7, 128), (3, 8, 256), (4, 9, 512), (4, 10, 1024)], (6, -2049), (6, 2048), null),
            Table([(4, 9, -1024), (3, 8, -512), (4, 7, -256), (5, 6, -128), (5, 5, -64), (4, 5, -32), (4, 5, 0), (5, 5, 32), (5, 6, 64), (4, 7, 128), (3, 8, 256), (3, 9, 512), (3, 10, 1024)], (5, -1025), (5, 2048), null),
            Table([(8, 3, -15), (9, 1, -7), (8, 1, -5), (9, 0, -3), (7, 0, -2), (4, 0, -1), (2, 1, 0), (5, 0, 2), (6, 0, 3), (3, 4, 4), (6, 1, 20), (4, 4, 22), (4, 5, 38), (5, 6, 70), (5, 7, 134), (6, 7, 262), (7, 8, 390), (6, 10, 646)], (9, -16), (9, 1670), 2),
            Table([(8, 4, -31), (9, 2, -15), (8, 2, -11), (9, 1, -7), (7, 1, -5), (4, 1, -3), (3, 1, -1), (3, 1, 1), (5, 1, 3), (6, 1, 5), (3, 5, 7), (6, 2, 39), (4, 5, 43), (4, 6, 75), (5, 7, 139), (5, 8, 267), (6, 8, 523), (7, 9, 779), (6, 11, 1291)], (9, -32), (9, 3339), 2),
            Table([(7, 4, -21), (8, 0, -5), (7, 0, -4), (5, 0, -3), (2, 2, -2), (5, 0, 2), (6, 0, 3), (7, 0, 4), (8, 0, 5), (2, 6, 6), (5, 5, 70), (6, 5, 102), (6, 6, 134), (6, 7, 198), (6, 8, 326), (6, 9, 582), (6, 10, 1094), (7, 11, 2118)], (8, -22), (8, 4166), 2),
            Table([(1, 0, 1), (2, 1, 2), (4, 0, 4), (4, 1, 5), (5, 1, 7), (5, 2, 9), (6, 2, 13), (7, 2, 17), (7, 3, 21), (7, 4, 29), (7, 5, 45), (7, 6, 77)], null, (7, 141), null),
            Table([(1, 0, 1), (2, 0, 2), (3, 1, 3), (5, 0, 5), (5, 1, 6), (6, 1, 8), (7, 0, 10), (7, 1, 11), (7, 2, 13), (7, 3, 17), (7, 4, 25), (8, 5, 41)], null, (8, 73), null),
            Table([(1, 0, 1), (3, 0, 2), (4, 0, 3), (5, 0, 4), (4, 1, 5), (3, 3, 7), (6, 1, 15), (6, 2, 17), (6, 3, 21), (6, 4, 29), (6, 5, 45), (7, 6, 77)], null, (7, 141), null),
            Table([(3, 0, -2), (3, 0, -1), (1, 0, 0), (3, 0, 1), (3, 0, 2)], null, null, null),
            Table([(7, 4, -24), (6, 2, -8), (5, 1, -4), (4, 0, -2), (3, 0, -1), (1, 0, 0), (3, 0, 1), (4, 0, 2), (5, 1, 3), (6, 2, 5), (7, 4, 9)], (7, -25), (7, 25), null),
        ];
    }

    private enum LineKind
    {
        Normal,
        Lower,
        Upper,
        OutOfBand,
    }

    private sealed record Line(int PrefixLength, int RangeLength, long RangeLow, LineKind Kind);
}
