using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// Whether the GSUB features of the font substitute the glyphs of characters: the font synthesis of <c>font-variant-caps</c>
/// and <c>font-variant-position</c> (https://drafts.csswg.org/css-fonts-4/#font-synthesis-small-caps) applies when the font
/// has no glyph for them.
/// </summary>
internal sealed partial class OpenTypeFont
{
    private readonly ConcurrentDictionary<(UnicodeScript Script, uint Feature), int[]> _substitutionFeatureLookups = new();

    /// <summary>
    /// Whether the lookups of a GSUB feature of the default language system of a script (or of the fallback scripts) may
    /// substitute a glyph (as HarfBuzz's <c>hb_ot_layout_lookup_would_substitute</c>, and Firefox's check of the
    /// subscripts and superscripts): the glyph is in the coverage of the first glyph of a subtable of a lookup of the feature.
    /// The contexts of the contextual lookups are not matched.
    /// </summary>
    public bool WouldSubstitute(UnicodeScript script, uint feature, ushort glyph)
    {
        var gsub = LayoutGsub;
        if (gsub is null)
            return false;

        var lookups = _substitutionFeatureLookups.GetOrAdd((script, feature), static (key, gsub) => GetSubstitutionFeatureLookups(gsub, key.Script, key.Feature), gsub);
        if (lookups.Length == 0)
            return false;

        var table = gsub.Data;
        foreach (var index in lookups)
        {
            if (gsub.GetLookup(index) is not { } lookup || !lookup.Digest.MayHave(glyph))
                continue;

            foreach (var subtable in lookup.SubtableOffsets)
            {
                if (OpenTypeLayoutTable.GetCoverageIndex(table, GetFirstCoverage(table, lookup.Type, subtable), glyph) >= 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether the default language system of a script (or of the fallback scripts) has a GSUB feature.</summary>
    public bool HasSubstitutionFeature(UnicodeScript script, uint feature)
    {
        var gsub = LayoutGsub;
        return gsub is not null && _substitutionFeatureLookups.GetOrAdd((script, feature), static (key, gsub) => GetSubstitutionFeatureLookups(gsub, key.Script, key.Feature), gsub).Length > 0;
    }

    private static int[] GetSubstitutionFeatureLookups(OpenTypeLayoutTable gsub, UnicodeScript script, uint feature)
    {
        Span<uint> scriptTags = stackalloc uint[3];
        var scriptCount = OpenTypeScriptTags.GetTags(script, scriptTags);
        gsub.SelectScript(scriptTags[..scriptCount], out var scriptIndex, out _);
        if (scriptIndex == OpenTypeLayoutTable.NoIndex)
            return [];

        var features = new Dictionary<uint, int>();
        gsub.CollectFeatures(scriptIndex, gsub.SelectLanguage(scriptIndex, []), features);
        return features.TryGetValue(feature, out var featureIndex) ? gsub.GetFeatureLookups(featureIndex, variationsIndex: -1) : [];
    }

    /// <summary>
    /// The offset of the coverage of the first glyph matched by a GSUB subtable (https://learn.microsoft.com/typography/opentype/spec/gsub):
    /// the coverage of the formats 1 and 2 and of the reverse chained substitutions, the first input coverage of the
    /// contextual formats 3.
    /// </summary>
    private static int GetFirstCoverage(ReadOnlySpan<byte> table, int type, int subtable)
    {
        var format = FontBinary.U16(table, subtable);
        return (type, format) switch
        {
            // glyphCount, seqLookupCount, then the coverages of the input sequence
            (OpenTypeLookup.ContextSubstitution, 3) => OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6),

            // The backtrack coverages, then the input coverages
            (OpenTypeLookup.ChainedContextSubstitution, 3) => OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4 + (FontBinary.U16(table, subtable + 2) * 2) + 2),
            (_, 1 or 2) or (OpenTypeLookup.ReverseChainedSingleSubstitution, _) => OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2),
            _ => 0,
        };
    }
}
