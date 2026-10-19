namespace Meziantou.Framework.Fonts;

/// <summary>
/// The pair adjustments of the legacy <c>kern</c> table: OpenType specification, "kern - Kerning". They are applied
/// when a font has no <c>GPOS</c> kerning.
/// </summary>
/// <remarks>
/// <para>
/// Version 0 of the table (OpenType specification) and version 1.0 (Apple TrueType Reference Manual, "The 'kern'
/// table") are read: their horizontal subtables of format 0 (sorted pairs) that hold kerning values. Minimum values,
/// cross-stream adjustments, the variation subtables of version 1.0 and the other formats are skipped: the font is
/// then used without them.
/// </para>
/// <para>
/// The subtables are validated when the table is read, so a lookup never fails. A subtable declares its length in 16
/// bits, which cannot hold more than 10,920 pairs: as other readers do, the number of pairs is trusted over the length
/// when the table has one subtable.
/// </para>
/// </remarks>
internal sealed class KernTable
{
    private const int HeaderLength = 4;
    private const int SubtableHeaderLength = 6;
    private const int Format0HeaderLength = 8;
    private const int PairLength = 6;

    private readonly Subtable[] _subtables;

    private KernTable(Subtable[] subtables)
    {
        _subtables = subtables;
    }

    /// <summary>Gets a value indicating whether the table has no pair that can be applied.</summary>
    public bool IsEmpty => _subtables.Length == 0;

    public static KernTable Read(ReadOnlySpan<byte> table)
    {
        if (table.Length >= 8 && FontBinary.UInt32(table, 0) == 0x00010000)
            return ReadAppleTable(table);

        if (table.Length < HeaderLength || FontBinary.UInt16(table, 0) != 0)
            return new KernTable([]);

        var count = FontBinary.UInt16(table, 2);
        var subtables = new List<Subtable>();
        long offset = HeaderLength;
        for (var i = 0; i < count; i++)
        {
            if (offset + SubtableHeaderLength > table.Length)
                break;

            var header = table.Slice((int)offset, SubtableHeaderLength);
            long length = FontBinary.UInt16(header, 2);
            var coverage = FontBinary.UInt16(header, 4);
            var format = coverage >> 8;

            // Bit 0: horizontal. Bit 1: minimum values. Bit 2: cross-stream. Bit 3: the values replace the previous ones.
            if (format == 0 && (coverage & 0x07) == 0x01 && offset + SubtableHeaderLength + Format0HeaderLength <= table.Length)
            {
                var pairCount = FontBinary.UInt16(table, (int)offset + SubtableHeaderLength);
                var pairs = offset + SubtableHeaderLength + Format0HeaderLength;
                var pairsLength = (long)pairCount * PairLength;
                if (count == 1)
                {
                    length = Math.Max(length, SubtableHeaderLength + Format0HeaderLength + pairsLength);
                }

                if (pairs + pairsLength <= Math.Min(table.Length, offset + length) && pairCount > 0)
                {
                    subtables.Add(new Subtable((int)pairs, pairCount, IsOverride: (coverage & 0x08) != 0));
                }
            }

            // A subtable shorter than its header would not advance.
            if (length < SubtableHeaderLength)
                break;

            offset += length;
        }

        return new KernTable([.. subtables]);
    }

    /// <summary>
    /// Reads version 1.0 of the table: a 32-bit version and count, then subtables whose header is a 32-bit length, a
    /// coverage (bit 15: vertical, bit 14: cross-stream, bit 13: variation; the low byte is the format) and a tuple
    /// index. The data of format 0 is the one of version 0.
    /// </summary>
    private static KernTable ReadAppleTable(ReadOnlySpan<byte> table)
    {
        const int AppleHeaderLength = 8;
        const int AppleSubtableHeaderLength = 8;
        var count = FontBinary.UInt32(table, 4);
        var subtables = new List<Subtable>();
        long offset = AppleHeaderLength;
        for (var i = 0L; i < count; i++)
        {
            if (offset + AppleSubtableHeaderLength > table.Length)
                break;

            long length = FontBinary.UInt32(table, (int)offset);
            var coverage = FontBinary.UInt16(table, (int)offset + 4);
            if ((coverage & 0xFF) == 0 && (coverage & 0xE000) == 0 && offset + AppleSubtableHeaderLength + Format0HeaderLength <= table.Length)
            {
                var pairCount = FontBinary.UInt16(table, (int)offset + AppleSubtableHeaderLength);
                var pairs = offset + AppleSubtableHeaderLength + Format0HeaderLength;
                var pairsLength = (long)pairCount * PairLength;
                if (pairs + pairsLength <= Math.Min(table.Length, offset + length) && pairCount > 0)
                {
                    subtables.Add(new Subtable((int)pairs, pairCount, IsOverride: false));
                }
            }

            // A subtable shorter than its header would not advance.
            if (length < AppleSubtableHeaderLength)
                break;

            offset += length;
        }

        return new KernTable([.. subtables]);
    }

    /// <summary>Gets the adjustment of the advance of a glyph when another glyph follows it, in font units.</summary>
    public int GetAdjustment(ReadOnlySpan<byte> table, int left, int right)
    {
        var result = 0;
        var key = ((uint)left << 16) | (uint)right;
        foreach (var subtable in _subtables)
        {
            var low = 0;
            var high = subtable.PairCount - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var position = subtable.Pairs + (middle * PairLength);
                var pair = FontBinary.UInt32(table, position);
                if (pair == key)
                {
                    var value = FontBinary.Int16(table, position + 4);
                    result = subtable.IsOverride ? value : result + value;
                    break;
                }

                if (pair < key)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
        }

        return result;
    }

    private readonly record struct Subtable(int Pairs, int PairCount, bool IsOverride);
}
