namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The lookup flags (https://learn.microsoft.com/typography/opentype/spec/chapter2#lookup-table).</summary>
[Flags]
internal enum LookupFlags : uint
{
    None = 0,

    /// <summary>The cursive attachments of the lookup go from right to left (the last glyph stays on the baseline).</summary>
    RightToLeft = 0x1,

    /// <summary>The base glyphs are skipped (the same bit as <see cref="GlyphProperties.BaseGlyph"/>).</summary>
    IgnoreBaseGlyphs = 0x2,

    /// <summary>The ligatures are skipped (the same bit as <see cref="GlyphProperties.Ligature"/>).</summary>
    IgnoreLigatures = 0x4,

    /// <summary>The marks are skipped (the same bit as <see cref="GlyphProperties.Mark"/>).</summary>
    IgnoreMarks = 0x8,

    /// <summary>The skipped glyph classes.</summary>
    IgnoreFlags = 0xE,

    /// <summary>The marks that are not in the mark glyph set of the lookup are skipped (the index of the set is in the high 16 bits of the lookup properties).</summary>
    UseMarkFilteringSet = 0x10,

    /// <summary>The marks of another mark attachment class than the one of the lookup (when it is not 0) are skipped.</summary>
    MarkAttachmentType = 0xFF00,
}

/// <summary>
/// A lookup of a GSUB or GPOS table (https://learn.microsoft.com/typography/opentype/spec/chapter2#lookup-table): its type,
/// its flags and its subtables (the extension subtables are resolved: <see cref="SubtableOffsets"/> are the offsets of the
/// real subtables in the table). <see cref="Digest"/> approximates the glyphs the lookup can apply to (the glyphs of the
/// first coverage of each subtable), to skip the lookup quickly. Immutable.
/// </summary>
internal sealed class OpenTypeLookup
{
    // The lookup types of GSUB (https://learn.microsoft.com/typography/opentype/spec/gsub#table-organization)
    public const int SingleSubstitution = 1;
    public const int MultipleSubstitution = 2;
    public const int AlternateSubstitution = 3;
    public const int LigatureSubstitution = 4;
    public const int ContextSubstitution = 5;
    public const int ChainedContextSubstitution = 6;
    public const int ExtensionSubstitution = 7;
    public const int ReverseChainedSingleSubstitution = 8;

    // The lookup types of GPOS (https://learn.microsoft.com/typography/opentype/spec/gpos#table-organization)
    public const int SingleAdjustment = 1;
    public const int PairAdjustment = 2;
    public const int CursiveAttachment = 3;
    public const int MarkToBaseAttachment = 4;
    public const int MarkToLigatureAttachment = 5;
    public const int MarkToMarkAttachment = 6;
    public const int ContextPositioning = 7;
    public const int ChainedContextPositioning = 8;
    public const int ExtensionPositioning = 9;

    private OpenTypeLookup(int index, int type, ushort flags, ushort markFilteringSet, int[] subtables, GlyphDigest digest)
    {
        Index = index;
        Type = type;
        Flags = (LookupFlags)flags;
        MarkFilteringSet = markFilteringSet;
        SubtableOffsets = subtables;
        Digest = digest;
    }

    /// <summary>The index of the lookup in the lookup list.</summary>
    public int Index { get; }

    /// <summary>The type of the lookup (after the extensions are resolved).</summary>
    public int Type { get; }

    /// <summary>The lookup flags.</summary>
    public LookupFlags Flags { get; }

    /// <summary>The index of the mark glyph set of the GDEF table (with <see cref="LookupFlags.UseMarkFilteringSet"/>).</summary>
    public ushort MarkFilteringSet { get; }

    /// <summary>The offsets of the subtables in the table.</summary>
    public int[] SubtableOffsets { get; }

    /// <summary>The glyphs the lookup may apply to.</summary>
    public GlyphDigest Digest { get; }

    /// <summary>The properties used to match the glyphs: the flags and, with <see cref="LookupFlags.UseMarkFilteringSet"/>, the mark filtering set in the high 16 bits.</summary>
    public uint Properties => (uint)Flags | ((Flags & LookupFlags.UseMarkFilteringSet) != LookupFlags.None ? (uint)MarkFilteringSet << 16 : 0);

    /// <summary>Creates a lookup from its offset in a table.</summary>
    internal static OpenTypeLookup Create(OpenTypeLayoutTable layout, int index, int offset)
    {
        var table = layout.Data;
        var type = FontBinary.U16(table, offset);
        var flags = FontBinary.U16(table, offset + 2);
        var count = FontBinary.U16(table, offset + 4);
        var markFilteringSet = (flags & (ushort)LookupFlags.UseMarkFilteringSet) != 0 ? FontBinary.U16(table, offset + 6 + (count * 2)) : (ushort)0;
        var extensionType = layout.IsSubstitution ? ExtensionSubstitution : ExtensionPositioning;
        var subtables = new List<int>(count);
        var resolvedType = (int)type;
        var digest = default(GlyphDigest);
        for (var i = 0; i < count; i++)
        {
            var subtable = OpenTypeLayoutTable.Offset16(table, offset, offset + 6 + (i * 2));
            if (subtable == 0)
                continue;

            if (type == extensionType)
            {
                // https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-7-extension-substitution: the subtables of
                // an extension lookup have the same type
                var extended = FontBinary.U32(table, subtable + 4);
                resolvedType = FontBinary.U16(table, subtable + 2);
                if (extended == 0 || extended > int.MaxValue - subtable || resolvedType == extensionType)
                    continue;

                subtable += (int)extended;
            }

            subtables.Add(subtable);
            AddFirstCoverage(table, layout.IsSubstitution, resolvedType, subtable, ref digest);
        }

        return new OpenTypeLookup(index, resolvedType, flags, markFilteringSet, [.. subtables], digest);
    }

    /// <summary>Adds the glyphs of the coverage of the first glyph matched by a subtable to a digest (all the glyphs when it is unknown).</summary>
    private static void AddFirstCoverage(ReadOnlySpan<byte> table, bool isSubstitution, int type, int subtable, ref GlyphDigest digest)
    {
        var format = FontBinary.U16(table, subtable);
        var isContext = isSubstitution ? type == ContextSubstitution : type == ContextPositioning;
        var isChainedContext = isSubstitution ? type == ChainedContextSubstitution : type == ChainedContextPositioning;
        int coverage;
        if (isContext && format == 3)
        {
            // glyphCount, seqLookupCount, then the coverages of the input sequence
            coverage = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6);
        }
        else if (isChainedContext && format == 3)
        {
            // The backtrack coverages, then the input coverages
            var backtrackCount = FontBinary.U16(table, subtable + 2);
            coverage = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4 + (backtrackCount * 2) + 2);
        }
        else if (format is 1 or 2 || (isSubstitution && type == ReverseChainedSingleSubstitution))
        {
            coverage = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2);
        }
        else
        {
            digest.Add(GlyphDigest.Full);
            return;
        }

        if (coverage == 0)
            return;

        OpenTypeLayoutTable.AddCoverage(table, coverage, ref digest);
    }
}
