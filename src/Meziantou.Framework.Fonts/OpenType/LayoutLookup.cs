using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A lookup of a GSUB or GPOS table: its type, the glyphs it skips, and where its subtables are.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct LayoutLookup
{
    // Lookup flags: OpenType specification, "Lookup Table".
    public const int RightToLeft = 0x0001;
    public const int IgnoreBaseGlyphs = 0x0002;
    public const int IgnoreLigatures = 0x0004;
    public const int IgnoreMarks = 0x0008;
    public const int UseMarkFilteringSet = 0x0010;
    public const int MarkAttachmentTypeMask = 0xFF00;

    public LayoutLookup(int offset, int type, int flag, int subtableCount, int markFilteringSet)
    {
        Offset = offset;
        Type = type;
        Flag = flag;
        SubtableCount = subtableCount;
        MarkFilteringSet = markFilteringSet;
    }

    /// <summary>Gets the position of the lookup in its table.</summary>
    public int Offset { get; }

    public int Type { get; }

    public int Flag { get; }

    public int SubtableCount { get; }

    /// <summary>Gets the mark glyph set of the GDEF table that says which marks the lookup does not skip, when the flag asks for one.</summary>
    public int MarkFilteringSet { get; }
}
