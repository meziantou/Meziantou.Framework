namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads the <c>MATH</c> table: OpenType specification, "MATH - The Mathematical Typesetting Table". It gives the
/// constants of mathematical layout, the italics corrections and the top accent attachments of the glyphs, the glyphs
/// that are extended shapes, and the larger forms and the assemblies of the glyphs that stretch.
/// </summary>
/// <remarks>
/// <para>
/// The header and the constants are validated when the table is read; a table whose header is not understood is left
/// out. The subtables of one glyph are read with bounds checks when the glyph is looked up: a malformed one is an
/// <see cref="InvalidFontException"/>.
/// </para>
/// <para>
/// A value whose device table is a variation index varies with the instance of a variable font, with the item
/// variation store of the <c>GDEF</c> table. The device tables that adjust a value for one size, and the kerning of
/// the corners of the glyphs (<c>MathKernInfo</c>), are not read.
/// </para>
/// </remarks>
internal sealed class MathTable
{
    private const int HeaderLength = 10;
    private const int ConstantsLength = 214;
    private const int ValueRecordsOffset = 8;
    private const int VariationIndexFormat = 0x8000;

    private readonly int _constants;
    private readonly int _italicsCorrections;
    private readonly int _topAccentAttachments;
    private readonly int _extendedShapes;
    private readonly int _variants;

    private MathTable(int constants, int italicsCorrections, int topAccentAttachments, int extendedShapes, int variants)
    {
        _constants = constants;
        _italicsCorrections = italicsCorrections;
        _topAccentAttachments = topAccentAttachments;
        _extendedShapes = extendedShapes;
        _variants = variants;
    }

    /// <summary>Reads the header of the table.</summary>
    /// <returns><see langword="null"/> when the header is not understood.</returns>
    public static MathTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < HeaderLength || FontBinary.UInt16(table, 0) != 1)
            return null;

        var constants = FontBinary.UInt16(table, 4);
        var glyphInfo = FontBinary.UInt16(table, 6);
        var variants = FontBinary.UInt16(table, 8);
        if ((constants != 0 && (long)constants + ConstantsLength > table.Length) || (glyphInfo != 0 && glyphInfo + 8 > table.Length) || (variants != 0 && variants + 10 > table.Length))
            return null;

        int italicsCorrections = 0, topAccentAttachments = 0, extendedShapes = 0;
        if (glyphInfo != 0)
        {
            italicsCorrections = GetOffset(table, glyphInfo, FontBinary.UInt16(table, glyphInfo));
            topAccentAttachments = GetOffset(table, glyphInfo, FontBinary.UInt16(table, glyphInfo + 2));
            extendedShapes = GetOffset(table, glyphInfo, FontBinary.UInt16(table, glyphInfo + 4));
        }

        return new MathTable(constants, italicsCorrections, topAccentAttachments, extendedShapes, variants);

        static int GetOffset(ReadOnlySpan<byte> table, int parent, int offset) => offset == 0 || parent + offset >= table.Length ? 0 : parent + offset;
    }

    /// <summary>Gets a constant, in font units, or as a percentage for the scales and the raise of the degree of a radical; 0 when the table has no constants.</summary>
    public int GetConstant(ReadOnlySpan<byte> table, FontFace face, MathConstant constant)
    {
        if (_constants == 0)
            return 0;

        return constant switch
        {
            MathConstant.ScriptPercentScaleDown => FontBinary.Int16(table, _constants),
            MathConstant.ScriptScriptPercentScaleDown => FontBinary.Int16(table, _constants + 2),
            MathConstant.DelimitedSubFormulaMinHeight => FontBinary.UInt16(table, _constants + 4),
            MathConstant.DisplayOperatorMinHeight => FontBinary.UInt16(table, _constants + 6),
            MathConstant.RadicalDegreeBottomRaisePercent => FontBinary.Int16(table, _constants + ConstantsLength - 2),
            _ => ReadValue(table, face, _constants, _constants + ValueRecordsOffset + (((int)constant - (int)MathConstant.MathLeading) * 4)),
        };
    }

    /// <summary>Gets the italics correction of a glyph: how far a subscript or a superscript that follows it moves, in font units.</summary>
    public bool TryGetItalicsCorrection(ReadOnlySpan<byte> table, FontFace face, int glyphId, out int value) => TryGetGlyphValue(table, face, _italicsCorrections, glyphId, out value);

    /// <summary>Gets the top accent attachment of a glyph: where an accent above it is centered, in font units from its origin.</summary>
    public bool TryGetTopAccentAttachment(ReadOnlySpan<byte> table, FontFace face, int glyphId, out int value) => TryGetGlyphValue(table, face, _topAccentAttachments, glyphId, out value);

    /// <summary>Tells whether a glyph is an extended shape: a glyph whose height is not that of the text, such as a large operator.</summary>
    public bool IsExtendedShape(ReadOnlySpan<byte> table, int glyphId) => _extendedShapes != 0 && LayoutCommon.GetCoverage(table, _extendedShapes, glyphId) != LayoutCommon.NotCovered;

    /// <summary>Gets how much two parts of an assembly overlap at least, in font units.</summary>
    public int GetMinConnectorOverlap(ReadOnlySpan<byte> table) => _variants == 0 ? 0 : FontBinary.UInt16(table, _variants);

    /// <summary>Gets how a glyph grows along a direction, or <see langword="null"/> when it does not.</summary>
    public MathGlyphConstruction? GetConstruction(ReadOnlySpan<byte> table, FontFace face, int glyphId, bool isVertical)
    {
        if (_variants == 0)
            return null;

        // "MathVariants table": the coverages of the vertical and the horizontal glyphs, then the offsets of their
        // constructions, vertical first.
        var coverage = FontBinary.UInt16(table, _variants + (isVertical ? 2 : 4));
        var verticalCount = FontBinary.UInt16(table, _variants + 6);
        var count = isVertical ? verticalCount : FontBinary.UInt16(table, _variants + 8);
        if (coverage == 0)
            return null;

        var index = LayoutCommon.GetCoverage(table, _variants + coverage, glyphId);
        if (index == LayoutCommon.NotCovered || index >= count)
            return null;

        var offset = FontBinary.UInt16(table, _variants + 10 + ((isVertical ? index : verticalCount + index) * 2));
        if (offset == 0)
            return null;

        // "MathGlyphConstruction table": the assembly, then the larger forms.
        var construction = _variants + offset;
        var assembly = FontBinary.UInt16(table, construction);
        var variantCount = FontBinary.UInt16(table, construction + 2);
        var records = FontBinary.Slice(table, construction + 4, variantCount * 4L);
        var variants = new MathGlyphVariant[variantCount];
        for (var i = 0; i < variants.Length; i++)
        {
            variants[i] = new MathGlyphVariant(FontBinary.UInt16(records, i * 4), FontBinary.UInt16(records, (i * 4) + 2));
        }

        if (assembly == 0)
            return new MathGlyphConstruction(variants, [], 0);

        // "GlyphAssembly table": the italics correction of the assembly, then its parts.
        var start = construction + assembly;
        var italicsCorrection = ReadValue(table, face, start, start);
        var partCount = FontBinary.UInt16(table, start + 4);
        var partRecords = FontBinary.Slice(table, start + 6, partCount * 10L);
        var parts = new MathGlyphPart[partCount];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = partRecords.Slice(i * 10, 10);
            parts[i] = new MathGlyphPart(FontBinary.UInt16(part, 0), FontBinary.UInt16(part, 2), FontBinary.UInt16(part, 4), FontBinary.UInt16(part, 6), (FontBinary.UInt16(part, 8) & 0x0001) != 0);
        }

        return new MathGlyphConstruction(variants, parts, italicsCorrection);
    }

    /// <summary>Gets the value of a glyph in a subtable made of a coverage and a list of values (italics corrections, top accent attachments).</summary>
    private static bool TryGetGlyphValue(ReadOnlySpan<byte> table, FontFace face, int subtable, int glyphId, out int value)
    {
        value = 0;
        if (subtable == 0)
            return false;

        var coverage = FontBinary.UInt16(table, subtable);
        if (coverage == 0)
            return false;

        var index = LayoutCommon.GetCoverage(table, subtable + coverage, glyphId);
        if (index == LayoutCommon.NotCovered || index >= FontBinary.UInt16(table, subtable + 2))
            return false;

        value = ReadValue(table, face, subtable, subtable + 4 + (index * 4));
        return true;
    }

    /// <summary>
    /// Reads a <c>MathValueRecord</c>: a value and the offset of its device table from its parent table. A device table
    /// that is a variation index gives the delta of the instance.
    /// </summary>
    private static int ReadValue(ReadOnlySpan<byte> table, FontFace face, int parent, int record)
    {
        var value = FontBinary.Int16(table, record);
        var device = FontBinary.UInt16(table, record + 2);
        if (device == 0 || face.Instance is null)
            return value;

        var position = parent + device;
        return FontBinary.UInt16(table, position + 4) == VariationIndexFormat
            ? value + face.GetLayoutDelta(FontBinary.UInt16(table, position), FontBinary.UInt16(table, position + 2))
            : value;
    }
}
