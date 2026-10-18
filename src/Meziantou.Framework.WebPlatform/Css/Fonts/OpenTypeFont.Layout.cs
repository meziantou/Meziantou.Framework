using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The adjustment of the position and of the advance of a glyph, in font units (https://learn.microsoft.com/typography/opentype/spec/gpos#value-record).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GlyphAdjustment(double XPlacement, double YPlacement, double XAdvance, double YAdvance)
{
    public bool IsEmpty => XPlacement == 0 && YPlacement == 0 && XAdvance == 0 && YAdvance == 0;
}

/// <summary>The adjustments of a pair of glyphs (kerning): the first glyph, then the second one.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PairAdjustment(GlyphAdjustment First, GlyphAdjustment Second)
{
    public bool IsEmpty => First.IsEmpty && Second.IsEmpty;
}

/// <summary>
/// The OpenType layout tables of the font (GDEF, GSUB and GPOS, https://learn.microsoft.com/typography/opentype/spec/ttochap1),
/// parsed on first use, the shaping plans (<see cref="GetShapePlan"/>) and the <c>kern</c> table.
/// </summary>
internal sealed partial class OpenTypeFont
{
    private OpenTypeGdef? _gdef;
    private OpenTypeLayoutTable? _gsub;
    private OpenTypeLayoutTable? _gpos;
    private int _layoutParsed;
    private readonly ConcurrentDictionary<ShapePlanKey, ShapePlan> _shapePlans = new();

    /// <summary>The GDEF table, or null.</summary>
    public OpenTypeGdef? LayoutGdef
    {
        get
        {
            EnsureLayoutTables();
            return _gdef;
        }
    }

    /// <summary>The GSUB table, or null when the font has none (or it has no lookups).</summary>
    public OpenTypeLayoutTable? LayoutGsub
    {
        get
        {
            EnsureLayoutTables();
            return _gsub;
        }
    }

    /// <summary>The GPOS table, or null when the font has none (or it has no lookups).</summary>
    public OpenTypeLayoutTable? LayoutGpos
    {
        get
        {
            EnsureLayoutTables();
            return _gpos;
        }
    }

    private void EnsureLayoutTables()
    {
        if (Volatile.Read(ref _layoutParsed) != 0)
            return;

        // The tables are immutable: a concurrent parse creates equivalent instances
        var (gdefOffset, gdefLength) = GetTableRange(OpenTypeTag.Gdef);
        var (gsubOffset, gsubLength) = GetTableRange(OpenTypeTag.Gsub);
        var (gposOffset, gposLength) = GetTableRange(OpenTypeTag.Gpos);
        _gdef = OpenTypeGdef.Create(Data, gdefOffset, gdefLength);
        _gsub = OpenTypeLayoutTable.Create(this, OpenTypeTag.Gsub, gsubOffset, gsubLength);
        _gpos = OpenTypeLayoutTable.Create(this, OpenTypeTag.Gpos, gposOffset, gposLength);
        Volatile.Write(ref _layoutParsed, 1);
    }

    /// <summary>The shaping plan of the font for a script, a direction, a language and features (cached).</summary>
    public ShapePlan GetShapePlan(in ShapePlanKey key)
    {
        if (_shapePlans.TryGetValue(key, out var plan))
            return plan;

        plan = ShapePlanBuilder.Build(this, key);

        // The plans are immutable: a concurrent build creates an equivalent plan
        return _shapePlans.GetOrAdd(key, plan);
    }

    private HashSet<int>? _verticalAlternates;

    /// <summary>
    /// Whether the <c>vert</c> feature of the font (of any script) substitutes a glyph by a single substitution: a character
    /// whose Vertical_Orientation is Tr is upright with its vertical alternate when the font has one, else rotated
    /// (https://www.unicode.org/reports/tr50/#vo).
    /// </summary>
    public bool HasVerticalAlternate(int glyph)
    {
        var alternates = _verticalAlternates;
        if (alternates is null)
        {
            // Immutable once built: a concurrent build creates an equivalent set
            alternates = [];
            if (LayoutGsub is { } gsub)
            {
                var table = gsub.Data;
                var verticalTag = ShapePlanBuilder.Tag("vert");
                for (var feature = 0; feature < gsub.FeatureCount; feature++)
                {
                    if (gsub.GetFeatureTag(feature) != verticalTag)
                        continue;

                    foreach (var index in gsub.GetFeatureLookups(feature, variationsIndex: -1))
                    {
                        if (gsub.GetLookup(index) is not { Type: OpenTypeLookup.SingleSubstitution } lookup)
                            continue;

                        foreach (var subtable in lookup.SubtableOffsets)
                        {
                            AddCoverageGlyphs(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), GlyphCount, alternates);
                        }
                    }
                }
            }

            _verticalAlternates = alternates;
        }

        return alternates.Contains(glyph);

        static void AddCoverageGlyphs(ReadOnlySpan<byte> table, int coverage, int glyphCount, HashSet<int> glyphs)
        {
            if (coverage == 0)
                return;

            var format = FontBinary.U16(table, coverage);
            var count = FontBinary.U16(table, coverage + 2);
            for (var i = 0; i < count; i++)
            {
                if (format == 1)
                {
                    glyphs.Add(FontBinary.U16(table, coverage + 4 + (i * 2)));
                }
                else if (format == 2)
                {
                    var end = Math.Min((int)FontBinary.U16(table, coverage + 4 + (i * 6) + 2), glyphCount - 1);
                    for (int glyph = FontBinary.U16(table, coverage + 4 + (i * 6)); glyph <= end; glyph++)
                    {
                        glyphs.Add(glyph);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The class of a glyph in the glyph class definition table of the GDEF table
    /// (https://learn.microsoft.com/typography/opentype/spec/gdef#glyph-class-definition-table): 1 base, 2 ligature, 3 mark,
    /// 4 component; 0 when the font does not classify the glyph (or has no GDEF table).
    /// </summary>
    public int GetGlyphClass(ushort glyph) => LayoutGdef?.GetGlyphClass(glyph) ?? 0;

    /// <summary>Whether the font classifies its glyphs (a GDEF table with a glyph class definition table).</summary>
    public bool HasGlyphClasses => LayoutGdef is { HasGlyphClasses: true };

    /// <summary>
    /// The kerning of a pair of glyphs in font units (the adjustment of the advance of the first glyph and of the position of
    /// the second one): the lookups of the <c>kern</c> feature of the GPOS table when the font has it, else the kern table
    /// (as HarfBuzz does).
    /// </summary>
    public double GetKerning(ushort first, ushort second, FontVariationInstance? instance = null, uint script = OpenTypeTag.DefaultScript)
    {
        var adjustment = GetPairAdjustment(first, second, OpenTypeTag.Kern, script, instance);
        return adjustment.First.XAdvance + adjustment.Second.XPlacement;
    }

    /// <summary>
    /// The adjustments of two consecutive glyphs by the lookups of a feature of the GPOS table for a script (the default
    /// language system of the script, else of <c>DFLT</c>, <c>dflt</c> or <c>latn</c>), applied by the layout engine
    /// (<see cref="OpenTypeApplyContext"/>). For the <c>kern</c> feature, the kern table is used when the GPOS table does not
    /// have the feature. The y placements are upwards.
    /// </summary>
    public PairAdjustment GetPairAdjustment(ushort first, ushort second, uint feature, uint script = OpenTypeTag.DefaultScript, FontVariationInstance? instance = null)
    {
        var gpos = LayoutGpos;
        var lookups = gpos is null ? [] : GetFeatureLookups(gpos, script, feature);
        if (lookups.Length == 0)
            return feature == OpenTypeTag.Kern ? new PairAdjustment(new GlyphAdjustment(0, 0, GetKernTableValue(first, second), 0), default) : default;

        using var buffer = new ShapingBuffer();
        buffer.Reset(2);
        buffer.Add(first, 0);
        buffer.Add(second, 1);
        PrepareGlyphs(buffer);
        buffer.ClearPositions();
        var context = new OpenTypeApplyContext(gpos!, LayoutGdef, buffer, instance);
        foreach (var index in lookups)
        {
            if (gpos!.GetLookup(index) is { } lookup)
            {
                context.SetLookupMask(1);
                context.ApplyLookup(lookup);
            }
        }

        var positions = buffer.Positions;
        return new PairAdjustment(
            new GlyphAdjustment(positions[0].XOffset, positions[0].YOffset, positions[0].XAdvance, positions[0].YAdvance),
            new GlyphAdjustment(positions[1].XOffset, positions[1].YOffset, positions[1].XAdvance, positions[1].YAdvance));
    }

    /// <summary>
    /// Returns the glyph that the lookups of a feature of the GSUB table substitute for a glyph (e.g. <c>rtlm</c>, the
    /// right-to-left mirrored forms), or the glyph itself when the feature does not substitute it by one glyph.
    /// </summary>
    public ushort GetSingleSubstitution(ushort glyph, uint feature, uint script = OpenTypeTag.DefaultScript)
    {
        var gsub = LayoutGsub;
        var lookups = gsub is null ? [] : GetFeatureLookups(gsub, script, feature);
        if (lookups.Length == 0)
            return glyph;

        using var buffer = new ShapingBuffer();
        buffer.Reset(1);
        buffer.Add(glyph, 0);
        PrepareGlyphs(buffer);
        var context = new OpenTypeApplyContext(gsub!, LayoutGdef, buffer, instance: null);
        foreach (var index in lookups)
        {
            if (gsub!.GetLookup(index) is { } lookup)
            {
                context.SetLookupMask(1);
                context.ApplyLookup(lookup);
            }
        }

        return buffer.Length == 1 ? (ushort)buffer.Info[0].Codepoint : glyph;
    }

    /// <summary>
    /// Shapes a text with the font alone, without the font fallback nor the script itemization (the fuzzing and the tests of
    /// the shaping engine): the glyphs and their positions in font units are in the buffer.
    /// </summary>
    internal void Shape(ReadOnlySpan<char> text, UnicodeScript script, ShapingDirection direction, string? language, FontFeatureList? features, FontVariationInstance? instance, ShapingBuffer buffer)
    {
        buffer.Reset(text.Length);
        buffer.Direction = direction;
        buffer.Script = script;
        for (var i = 0; i < text.Length; i++)
        {
            var codePoint = (int)text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                buffer.Add(codePoint, i++);
            }
            else
            {
                buffer.Add(codePoint, i);
            }
        }

        var key = new ShapePlanKey(script, direction, language, LanguageOverride: 0, FaceFeatures: null, features, LayoutGsub?.FindFeatureVariations(instance) ?? -1, LayoutGpos?.FindFeatureVariations(instance) ?? -1, DisableKerning: false);
        OpenTypeShapingEngine.Shape(GetShapePlan(key), buffer, new ShapingFont(this, instance));
    }

    /// <summary>Sets the mask and the glyph properties of glyphs added to a buffer outside of a shaping.</summary>
    private void PrepareGlyphs(ShapingBuffer buffer)
    {
        var gdef = LayoutGdef;
        for (var i = 0; i < buffer.Length; i++)
        {
            ref var info = ref buffer.Info[i];
            info.Mask = 1;
            info.GlyphProperties = gdef?.GetGlyphProperties(info.Codepoint) ?? GlyphProperties.BaseGlyph;
            buffer.Digest.Add(info.Codepoint);
        }
    }

    /// <summary>The sorted indexes of the lookups of a feature of the default language system of a script (or of the fallback scripts).</summary>
    private static int[] GetFeatureLookups(OpenTypeLayoutTable table, uint script, uint feature)
    {
        table.SelectScript([script], out var scriptIndex, out _);
        var features = new Dictionary<uint, int>();
        table.CollectFeatures(scriptIndex, table.SelectLanguage(scriptIndex, []), features);
        if (!features.TryGetValue(feature, out var featureIndex))
            return [];

        var lookups = table.GetFeatureLookups(featureIndex, variationsIndex: -1).ToArray();
        Array.Sort(lookups);
        return lookups;
    }

    /// <summary>
    /// The kerning of a pair of glyphs in the kern table (https://learn.microsoft.com/typography/opentype/spec/kern and the
    /// Apple version, https://developer.apple.com/fonts/TrueType-Reference-Manual/RM06/Chap6kern.html): the sum of the
    /// horizontal kerning values of the subtables of format 0 (a subtable with the override flag replaces the sum).
    /// </summary>
    public int GetKernTableValue(ushort first, ushort second)
    {
        var kern = GetTable(OpenTypeTag.Kern);
        if (kern.Length < 4)
            return 0;

        var isApple = FontBinary.U32(kern, 0) == 0x00010000;
        var subtableCount = isApple ? FontBinary.U32(kern, 4) : FontBinary.U16(kern, 2);
        var offset = isApple ? 8 : 4;
        var result = 0;
        for (var i = 0u; i < subtableCount && offset < kern.Length; i++)
        {
            int length, format, pairs;
            bool isHorizontal, isOverride;
            if (isApple)
            {
                length = (int)Math.Min(FontBinary.U32(kern, offset), int.MaxValue);
                var coverage = FontBinary.U16(kern, offset + 4);
                format = coverage & 0xFF;
                isHorizontal = (coverage & 0xE000) == 0;
                isOverride = false;
                pairs = offset + 8;
            }
            else
            {
                length = FontBinary.U16(kern, offset + 2);
                var coverage = FontBinary.U16(kern, offset + 4);
                format = coverage >> 8;
                isHorizontal = (coverage & 0x07) == 0x01;
                isOverride = (coverage & 0x08) != 0;
                pairs = offset + 6;
            }

            if (format == 0 && isHorizontal)
            {
                var pairCount = FontBinary.U16(kern, pairs);
                if (FindKernPair(kern, pairs + 8, pairCount, first, second) is { } value)
                {
                    result = isOverride ? value : result + value;
                }

                // The length of a large subtable of the Microsoft version overflows: its pairs are the end of the table
                if (!isApple && pairs + 8 + (pairCount * 6) > offset + length)
                {
                    length = pairs + 8 + (pairCount * 6) - offset;
                }
            }

            if (length <= 0)
                break;

            offset += length;
        }

        return result;

        static int? FindKernPair(ReadOnlySpan<byte> kern, int start, int count, ushort first, ushort second)
        {
            var key = ((uint)first << 16) | second;
            int low = 0, high = Math.Min(count, Math.Max(0, (kern.Length - start) / 6)) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var pair = FontBinary.U32(kern, start + (middle * 6));
                if (pair < key)
                {
                    low = middle + 1;
                }
                else if (pair > key)
                {
                    high = middle - 1;
                }
                else
                {
                    return FontBinary.I16(kern, start + (middle * 6) + 4);
                }
            }

            return null;
        }
    }
}
