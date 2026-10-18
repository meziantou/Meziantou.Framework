using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The constants of the MathConstants table (https://learn.microsoft.com/typography/opentype/spec/math#mathconstants-table), in the order of its MathValueRecords.</summary>
internal enum MathConstant
{
    MathLeading,
    AxisHeight,
    AccentBaseHeight,
    FlattenedAccentBaseHeight,
    SubscriptShiftDown,
    SubscriptTopMax,
    SubscriptBaselineDropMin,
    SuperscriptShiftUp,
    SuperscriptShiftUpCramped,
    SuperscriptBottomMin,
    SuperscriptBaselineDropMax,
    SubSuperscriptGapMin,
    SuperscriptBottomMaxWithSubscript,
    SpaceAfterScript,
    UpperLimitGapMin,
    UpperLimitBaselineRiseMin,
    LowerLimitGapMin,
    LowerLimitBaselineDropMin,
    StackTopShiftUp,
    StackTopDisplayStyleShiftUp,
    StackBottomShiftDown,
    StackBottomDisplayStyleShiftDown,
    StackGapMin,
    StackDisplayStyleGapMin,
    StretchStackTopShiftUp,
    StretchStackBottomShiftDown,
    StretchStackGapAboveMin,
    StretchStackGapBelowMin,
    FractionNumeratorShiftUp,
    FractionNumeratorDisplayStyleShiftUp,
    FractionDenominatorShiftDown,
    FractionDenominatorDisplayStyleShiftDown,
    FractionNumeratorGapMin,
    FractionNumDisplayStyleGapMin,
    FractionRuleThickness,
    FractionDenominatorGapMin,
    FractionDenomDisplayStyleGapMin,
    SkewedFractionHorizontalGap,
    SkewedFractionVerticalGap,
    OverbarVerticalGap,
    OverbarRuleThickness,
    OverbarExtraAscender,
    UnderbarVerticalGap,
    UnderbarRuleThickness,
    UnderbarExtraDescender,
    RadicalVerticalGap,
    RadicalDisplayStyleVerticalGap,
    RadicalRuleThickness,
    RadicalExtraAscender,
    RadicalKernBeforeDegree,
    RadicalKernAfterDegree,
}

/// <summary>A size variant of a glyph (MathGlyphVariantRecord): the variant glyph and its advance in the direction of the stretching, in font units.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MathGlyphVariant(ushort Glyph, int AdvanceMeasurement);

/// <summary>A part of a glyph assembly (GlyphPart), in font units.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MathGlyphPart(ushort Glyph, int StartConnectorLength, int EndConnectorLength, int FullAdvance, bool IsExtender);

/// <summary>
/// The constructions of a stretchy glyph in one direction (MathGlyphConstruction,
/// https://learn.microsoft.com/typography/opentype/spec/math#mathglyphconstruction-table): its size variants and its glyph
/// assembly (empty when it has none).
/// </summary>
internal sealed class MathGlyphConstruction(MathGlyphVariant[] variants, MathGlyphPart[] parts, int assemblyItalicsCorrection)
{
    public IReadOnlyList<MathGlyphVariant> Variants { get; } = variants;

    public IReadOnlyList<MathGlyphPart> AssemblyParts { get; } = parts;

    /// <summary>The italics correction of the glyph assembly, in font units.</summary>
    public int AssemblyItalicsCorrection { get; } = assemblyItalicsCorrection;
}

/// <summary>
/// The OpenType MATH table of a font (https://learn.microsoft.com/typography/opentype/spec/math): the layout constants, the
/// glyph information (italics corrections, top accent attachments) and the size variants and assemblies of the stretchy
/// glyphs. The values are in font units; the device tables are ignored. The table is read on demand with bounds checks.
/// </summary>
internal sealed class OpenTypeMath
{
    private const int ConstantRecordsOffset = 8;
    private const int RadicalDegreeBottomRaisePercentOffset = ConstantRecordsOffset + (51 * 4);

    private readonly byte[] _data;
    private readonly int _offset;
    private readonly int _length;
    private readonly int _constants;
    private readonly int _italicsCorrections;
    private readonly int _topAccentAttachments;
    private readonly int _variants;

    private OpenTypeMath(byte[] data, int offset, int length)
    {
        _data = data;
        _offset = offset;
        _length = length;
        var table = Table;
        _constants = Offset(table, 0, FontBinary.U16(table, 4));
        var glyphInfo = Offset(table, 0, FontBinary.U16(table, 6));
        _italicsCorrections = glyphInfo < 0 ? -1 : Offset(table, glyphInfo, FontBinary.U16(table, glyphInfo));
        _topAccentAttachments = glyphInfo < 0 ? -1 : Offset(table, glyphInfo, FontBinary.U16(table, glyphInfo + 2));
        _variants = Offset(table, 0, FontBinary.U16(table, 8));
    }

    private ReadOnlySpan<byte> Table => _data.AsSpan(_offset, _length);

    /// <summary>Returns the MATH table of a font, or null when it has none (or it is not a version 1 table).</summary>
    public static OpenTypeMath? Create(OpenTypeFont font, (int Offset, int Length) range)
    {
        if (range.Offset < 0 || range.Length < 10 || FontBinary.U16(font.Data.AsSpan(range.Offset, range.Length), 0) != 1)
            return null;

        return new OpenTypeMath(font.Data, range.Offset, range.Length);
    }

    private static int Offset(ReadOnlySpan<byte> table, int baseOffset, int offset) => offset == 0 || baseOffset < 0 || baseOffset + offset >= table.Length ? -1 : baseOffset + offset;

    /// <summary>Whether the table has a MathConstants subtable.</summary>
    public bool HasConstants => _constants >= 0;

    /// <summary>scriptPercentScaleDown: the scale down of the first level of scripts, in percent (0 when it is not set).</summary>
    public int ScriptPercentScaleDown => _constants < 0 ? 0 : FontBinary.I16(Table, _constants);

    /// <summary>scriptScriptPercentScaleDown: the scale down of the second level of scripts, in percent (0 when it is not set).</summary>
    public int ScriptScriptPercentScaleDown => _constants < 0 ? 0 : FontBinary.I16(Table, _constants + 2);

    /// <summary>delimitedSubFormulaMinHeight, in font units.</summary>
    public int DelimitedSubFormulaMinHeight => _constants < 0 ? 0 : FontBinary.U16(Table, _constants + 4);

    /// <summary>displayOperatorMinHeight, in font units.</summary>
    public int DisplayOperatorMinHeight => _constants < 0 ? 0 : FontBinary.U16(Table, _constants + 6);

    /// <summary>radicalDegreeBottomRaisePercent, in percent.</summary>
    public int RadicalDegreeBottomRaisePercent => _constants < 0 ? 0 : FontBinary.I16(Table, _constants + RadicalDegreeBottomRaisePercentOffset);

    /// <summary>The value of a MathValueRecord constant, in font units.</summary>
    public int GetConstant(MathConstant constant) => _constants < 0 ? 0 : FontBinary.I16(Table, _constants + ConstantRecordsOffset + ((int)constant * 4));

    /// <summary>Returns the italics correction of a glyph (MathItalicsCorrectionInfo), in font units, or null when the table has none for it.</summary>
    public int? GetItalicsCorrection(ushort glyph) => GetGlyphValue(_italicsCorrections, glyph);

    /// <summary>Returns the top accent attachment of a glyph (MathTopAccentAttachment), in font units, or null when the table has none for it.</summary>
    public int? GetTopAccentAttachment(ushort glyph) => GetGlyphValue(_topAccentAttachments, glyph);

    /// <summary>minConnectorOverlap of the MathVariants table, in font units.</summary>
    public int MinConnectorOverlap => _variants < 0 ? 0 : FontBinary.U16(Table, _variants);

    /// <summary>
    /// Returns the construction of a glyph that stretches in the vertical direction (<paramref name="vertical"/>) or in the
    /// horizontal direction (MathVariants.vertGlyphConstructionOffsets or horizGlyphConstructionOffsets), or null.
    /// </summary>
    public MathGlyphConstruction? GetConstruction(ushort glyph, bool vertical)
    {
        if (_variants < 0)
            return null;

        var table = Table;
        var vertCount = FontBinary.U16(table, _variants + 6);
        var coverage = Offset(table, _variants, FontBinary.U16(table, _variants + (vertical ? 2 : 4)));
        var index = GetCoverageIndex(table, coverage, glyph);
        if (index < 0 || index >= (vertical ? vertCount : FontBinary.U16(table, _variants + 8)))
            return null;

        var record = _variants + 10 + ((vertical ? index : vertCount + index) * 2);
        var construction = Offset(table, _variants, FontBinary.U16(table, record));
        if (construction < 0)
            return null;

        var variantCount = FontBinary.U16(table, construction + 2);
        var variants = new MathGlyphVariant[variantCount];
        for (var i = 0; i < variantCount; i++)
        {
            var variant = construction + 4 + (i * 4);
            variants[i] = new MathGlyphVariant(FontBinary.U16(table, variant), FontBinary.U16(table, variant + 2));
        }

        var parts = Array.Empty<MathGlyphPart>();
        var italicsCorrection = 0;
        var assembly = Offset(table, construction, FontBinary.U16(table, construction));
        if (assembly >= 0)
        {
            italicsCorrection = FontBinary.I16(table, assembly);
            var partCount = FontBinary.U16(table, assembly + 4);
            parts = new MathGlyphPart[partCount];
            for (var i = 0; i < partCount; i++)
            {
                var part = assembly + 6 + (i * 10);
                parts[i] = new MathGlyphPart(FontBinary.U16(table, part), FontBinary.U16(table, part + 2), FontBinary.U16(table, part + 4), FontBinary.U16(table, part + 6), (FontBinary.U16(table, part + 8) & 1) != 0);
            }
        }

        return new MathGlyphConstruction(variants, parts, italicsCorrection);
    }

    /// <summary>A value of a subtable made of a coverage table and of MathValueRecords (the italics corrections, the top accent attachments).</summary>
    private int? GetGlyphValue(int subtable, ushort glyph)
    {
        if (subtable < 0)
            return null;

        var table = Table;
        var index = GetCoverageIndex(table, Offset(table, subtable, FontBinary.U16(table, subtable)), glyph);
        if (index < 0 || index >= FontBinary.U16(table, subtable + 2))
            return null;

        return FontBinary.I16(table, subtable + 4 + (index * 4));
    }

    /// <summary>The coverage index of a glyph, or -1 (the coverage tables of the layout tables: <see cref="OpenTypeLayoutTable.GetCoverageIndex"/>).</summary>
    private static int GetCoverageIndex(ReadOnlySpan<byte> table, int coverage, ushort glyph) => coverage < 0 ? -1 : OpenTypeLayoutTable.GetCoverageIndex(table, coverage, glyph);
}
