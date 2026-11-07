namespace Meziantou.Framework.Fonts;

/// <summary>
/// Answers questions about the layout tables of a font without shaping a text: whether it has a feature, what a
/// feature does to a glyph or to a pair of glyphs. The lookups are those of the default language system of a script.
/// </summary>
/// <remarks>Each call reads the tables: a caller that asks often keeps the answers.</remarks>
public static class LayoutQueries
{
    /// <summary>The tag of the default script, and the scripts a feature of a script is looked for in after it.</summary>
    public static readonly uint DefaultScript = ShapingFeature.ToTag("DFLT");

    private static readonly uint LowerCaseDefaultScript = ShapingFeature.ToTag("dflt");
    private static readonly uint LatinScript = ShapingFeature.ToTag("latn");
    private static readonly uint Kern = ShapingFeature.ToTag("kern");
    private static readonly uint Vert = ShapingFeature.ToTag("vert");

    /// <summary>Determines whether a layout table of a font has a feature, whatever the scripts and the language systems that list it.</summary>
    /// <param name="face">The font.</param>
    /// <param name="isPositioning"><see langword="true"/> for the <c>GPOS</c> table, <see langword="false"/> for the <c>GSUB</c> table.</param>
    /// <param name="feature">The tag of the feature.</param>
    public static bool HasFeature(FontFace face, bool isPositioning, uint feature)
    {
        var (table, data) = GetTable(face, isPositioning);
        return table is not null && table.FindFeature(data.Span, feature) != LayoutTable.NoFeature;
    }

    /// <summary>Determines whether the default language system of a script has a feature in a layout table of a font.</summary>
    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static bool HasFeature(FontFace face, bool isPositioning, UnicodeScript script, uint feature)
    {
        var (table, data) = GetTable(face, isPositioning);
        if (table is null)
            return false;

        Span<uint> scripts = stackalloc uint[OpenTypeScript.MaxTagCount];
        scripts = scripts[..OpenTypeScript.GetTags(script, scripts)];
        var span = data.Span;
        var system = table.FindLanguageSystem(span, scripts, languages: []);
        if (system == 0)
            return false;

        var count = LayoutTable.GetFeatureCount(span, system);
        for (var i = 0; i < count; i++)
        {
            if (table.GetFeatureTag(span, LayoutTable.GetFeatureIndex(span, system, i)) == feature)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a feature of the default language system of a script substitutes a glyph on its own: whether
    /// one of its lookups has a rule whose input is the glyph alone, whatever its context.
    /// </summary>
    /// <exception cref="InvalidFontException">The <c>GSUB</c> table is malformed.</exception>
    public static bool WouldSubstitute(FontFace face, UnicodeScript script, uint feature, int glyph)
    {
        if (face.Gsub is not { } gsub)
            return false;

        Span<uint> scripts = stackalloc uint[OpenTypeScript.MaxTagCount];
        scripts = scripts[..OpenTypeScript.GetTags(script, scripts)];
        var lookups = GetFeatureLookups(gsub, face.GsubData.Span, scripts, feature);
        if (lookups.Length == 0)
            return false;

        var applier = new LayoutApplier(new GlyphBuffer(face.Data.Limits), face);
        applier.Begin(gsub, face.GsubData, isPositioning: false, isRightToLeft: false);
        foreach (var lookup in lookups)
        {
            if (applier.WouldSubstitute(lookup, [glyph], withoutContext: false))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the glyph the lookups of a feature of the <c>GSUB</c> table substitute for a glyph (e.g. <c>rtlm</c>, the
    /// right-to-left mirrored forms), or the glyph itself when the feature does not substitute it by one glyph.
    /// </summary>
    /// <param name="face">The font.</param>
    /// <param name="feature">The tag of the feature.</param>
    /// <param name="glyph">The glyph.</param>
    /// <param name="scriptTag">The OpenType tag of the script, whose default language system is used; else the one of <c>DFLT</c>, <c>dflt</c> or <c>latn</c>.</param>
    /// <exception cref="InvalidFontException">The <c>GSUB</c> table is malformed.</exception>
    public static int GetSingleSubstitution(FontFace face, uint feature, int glyph, uint scriptTag)
    {
        if (face.Gsub is not { } gsub)
            return glyph;

        var lookups = GetFeatureLookups(gsub, face.GsubData.Span, [scriptTag], feature);
        if (lookups.Length == 0)
            return glyph;

        var buffer = CreateBuffer(face, [glyph]);
        var applier = new LayoutApplier(buffer, face);
        applier.SetGlyphClasses();
        applier.Begin(gsub, face.GsubData, isPositioning: false, isRightToLeft: false);
        foreach (var lookup in lookups)
        {
            applier.ApplyLookup(lookup, uint.MaxValue);
        }

        return buffer.Length == 1 ? buffer.Info[0].Glyph : glyph;
    }

    /// <summary>
    /// Determines whether the <c>vert</c> feature of a font, of any script, substitutes a glyph by one glyph: a character
    /// whose vertical orientation is "transformed rotated" (Tr) is upright in a vertical line when the font has a
    /// vertical alternate for it (Unicode Standard Annex #50).
    /// </summary>
    /// <exception cref="InvalidFontException">The <c>GSUB</c> table is malformed.</exception>
    public static bool HasVerticalAlternate(FontFace face, int glyph)
    {
        if (face.Gsub is not { } gsub)
            return false;

        var data = face.GsubData.Span;
        LayoutApplier? applier = null;
        GlyphBuffer? buffer = null;
        var count = gsub.GetFeatureListCount(data);
        for (var feature = 0; feature < count; feature++)
        {
            if (gsub.GetFeatureTag(data, feature) != Vert)
                continue;

            var lookupCount = gsub.GetFeatureLookupCount(data, feature);
            for (var i = 0; i < lookupCount; i++)
            {
                buffer ??= new GlyphBuffer(face.Data.Limits);
                if (applier is null)
                {
                    applier = new LayoutApplier(buffer, face);
                    applier.Begin(gsub, face.GsubData, isPositioning: false, isRightToLeft: false, isVertical: true);
                }

                buffer.Clear();
                buffer.Add(0, 0);
                ref var info = ref buffer.Info[0];
                info.Glyph = glyph;
                info.Mask = uint.MaxValue;
                buffer.StartShaping();
                applier.SetGlyphClasses();
                applier.ApplyLookup(gsub.GetFeatureLookup(data, feature, i), uint.MaxValue);
                if (buffer.Length == 1 && buffer.Info[0].Glyph != glyph)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the adjustments the lookups of a feature of the <c>GPOS</c> table make to two consecutive glyphs, in font
    /// units, from positions that are all 0: the advances and the offsets of the two glyphs, the y axis up. For the
    /// <c>kern</c> feature, a font without the feature in its <c>GPOS</c> table has the pairs of its legacy kerning
    /// table, added to the advance of the first glyph.
    /// </summary>
    /// <param name="face">The font.</param>
    /// <param name="feature">The tag of the feature.</param>
    /// <param name="first">The first glyph.</param>
    /// <param name="second">The second glyph.</param>
    /// <param name="scriptTag">The OpenType tag of the script, whose default language system is used; else the one of <c>DFLT</c>, <c>dflt</c> or <c>latn</c>.</param>
    /// <exception cref="InvalidFontException">The <c>GPOS</c> table is malformed.</exception>
    public static (GlyphPosition First, GlyphPosition Second) GetPairAdjustment(FontFace face, uint feature, int first, int second, uint scriptTag)
    {
        var lookups = face.Gpos is { } table ? GetFeatureLookups(table, face.GposData.Span, [scriptTag], feature) : [];
        if (lookups.Length == 0)
            return feature == Kern ? (new GlyphPosition { XAdvance = face.GetLegacyKerning(first, second) }, default) : default;

        var buffer = CreateBuffer(face, [first, second]);
        buffer.CreatePositions();
        var applier = new LayoutApplier(buffer, face);
        applier.SetGlyphClasses();
        applier.Begin(face.Gpos!, face.GposData, isPositioning: true, isRightToLeft: false);
        foreach (var lookup in lookups)
        {
            applier.ApplyLookup(lookup, uint.MaxValue);
        }

        var positions = buffer.Positions;
        return (positions[0], positions[1]);
    }

    /// <summary>
    /// Gets the lookups of a feature in the default language system of the first script the table has, in the order of
    /// the lookup list; the scripts <c>DFLT</c>, <c>dflt</c> and <c>latn</c> are tried after the given ones.
    /// </summary>
    private static int[] GetFeatureLookups(LayoutTable table, ReadOnlySpan<byte> data, ReadOnlySpan<uint> scripts, uint feature)
    {
        Span<uint> tags = stackalloc uint[scripts.Length + 3];
        scripts.CopyTo(tags);
        tags[scripts.Length] = DefaultScript;
        tags[scripts.Length + 1] = LowerCaseDefaultScript;
        tags[scripts.Length + 2] = LatinScript;
        var system = table.FindLanguageSystem(data, tags, languages: []);
        if (system == 0)
            return [];

        var count = LayoutTable.GetFeatureCount(data, system);
        for (var i = 0; i < count; i++)
        {
            var index = LayoutTable.GetFeatureIndex(data, system, i);
            if (table.GetFeatureTag(data, index) != feature)
                continue;

            var lookups = new int[table.GetFeatureLookupCount(data, index)];
            for (var j = 0; j < lookups.Length; j++)
            {
                lookups[j] = table.GetFeatureLookup(data, index, j);
            }

            Array.Sort(lookups);
            return lookups;
        }

        return [];
    }

    private static (LayoutTable? Table, ReadOnlyMemory<byte> Data) GetTable(FontFace face, bool isPositioning)
        => isPositioning ? (face.Gpos, face.GposData) : (face.Gsub, face.GsubData);

    /// <summary>Creates a buffer of glyphs, each its own cluster, which every lookup applies to.</summary>
    private static GlyphBuffer CreateBuffer(FontFace face, ReadOnlySpan<int> glyphs)
    {
        var buffer = new GlyphBuffer(face.Data.Limits);
        for (var i = 0; i < glyphs.Length; i++)
        {
            buffer.Add(0, i);
            ref var info = ref buffer.Info[i];
            info.Glyph = glyphs[i];
            info.Mask = uint.MaxValue;
        }

        buffer.StartShaping();
        return buffer;
    }
}
