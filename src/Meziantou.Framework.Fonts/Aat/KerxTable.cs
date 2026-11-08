using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The extended kerning table of Apple Advanced Typography (<c>kerx</c>): Apple TrueType Reference Manual, "The 'kerx'
/// table". Its subtables kern pairs of glyphs (formats 0, 2 and 6), kern glyphs in context (format 1), and attach a
/// glyph to another one by a point of each (format 4).
/// </summary>
/// <remarks>
/// <para>
/// Versions 2, 3 and 4 are read. The subtables whose values vary with the instance of a variable font (a tuple count,
/// in version 4) are left out, and so are the actions of format 4 that name the control points of the outlines.
/// </para>
/// <para>
/// The headers of the subtables are validated when the table is read; their content is read when they are applied,
/// through <see cref="FontBinary"/>.
/// </para>
/// </remarks>
internal sealed class KerxTable
{
    /// <summary>The subtables a table can have: far more than any font has.</summary>
    private const int MaxSubtables = 4096;

    private KerxTable(Subtable[] subtables)
    {
        Subtables = subtables;
    }

    /// <summary>Gets the subtables, in the order they are applied.</summary>
    public Subtable[] Subtables { get; }

    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static KerxTable Read(ReadOnlySpan<byte> table)
    {
        var version = FontBinary.UInt16(table, 0);
        if (version is < 2 or > 4)
            throw FontErrors.InvalidTable(FontTags.Kerx, $"the version {version} is not supported.");

        var count = FontBinary.Offset32(table, 4);
        if (count > MaxSubtables)
            throw FontErrors.InvalidTable(FontTags.Kerx, "it has more subtables than it can hold.");

        var subtables = new List<Subtable>(count);
        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            var length = FontBinary.Offset32(table, offset);
            if (length < 12)
                throw FontErrors.InvalidTable(FontTags.Kerx, "a subtable is shorter than its header.");

            FontBinary.EnsureRange(table, offset, length);
            var coverage = FontBinary.UInt32(table, offset + 4);
            var tupleCount = version >= 4 ? FontBinary.Offset32(table, offset + 8) : 0;
            subtables.Add(new Subtable(offset, length, coverage, tupleCount));
            offset = FontBinary.AddOffset(offset, length);
        }

        return new KerxTable([.. subtables]);
    }

    /// <summary>A subtable: where it is in the table, from its header, and how it applies.</summary>
    /// <param name="Offset">The offset of the subtable, from its header, from the start of the table.</param>
    /// <param name="Length">The length of the subtable, its header included.</param>
    /// <param name="Coverage">The orientation, the direction of the adjustments and the format.</param>
    /// <param name="TupleCount">The number of values of each adjustment of a variable font, or 0.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Subtable(int Offset, int Length, uint Coverage, int TupleCount)
    {
        /// <summary>Gets the size of the header of a subtable.</summary>
        public const int HeaderLength = 12;

        /// <summary>Gets the format of the subtable.</summary>
        public int Format => (int)(Coverage & 0xFF);

        /// <summary>Gets a value indicating whether the subtable kerns vertical text.</summary>
        public bool IsVertical => (Coverage & 0x80000000) != 0;

        /// <summary>Gets a value indicating whether the adjustments are perpendicular to the line.</summary>
        public bool IsCrossStream => (Coverage & 0x40000000) != 0;

        /// <summary>Gets a value indicating whether the state machine of the subtable reads the glyphs from the last one.</summary>
        public bool IsBackward => (Coverage & 0x10000000) != 0;
    }
}
