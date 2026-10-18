using System.Collections.Concurrent;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// A GSUB or GPOS table of a font (https://learn.microsoft.com/typography/opentype/spec/chapter2): the script list and its
/// language systems, the feature list, the feature variations and the lookups. The table is read from the data of the font
/// with bounds-checked reads (a malformed table reads zeros, it never throws); the lookups are parsed lazily and cached
/// (<see cref="GetLookup"/>). It is immutable once built, and shared by the threads that use the font.
/// </summary>
/// <remarks>The selection of the script and of the language system follows HarfBuzz (hb-ot-layout.cc, MIT license).</remarks>
internal sealed class OpenTypeLayoutTable
{
    /// <summary>The value of a missing index (no script, no feature, the default language system).</summary>
    public const int NoIndex = 0xFFFF;

    private const uint DefaultLanguageTag = 0x64666C74; // dflt

    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _length;
    private readonly int _scriptList;
    private readonly int _featureList;
    private readonly int _lookupList;
    private readonly int _featureVariations;
    private readonly OpenTypeLookup?[] _lookups;
    private readonly ConcurrentDictionary<int, int[]> _featureLookups = new();

    private OpenTypeLayoutTable(OpenTypeFont font, bool isSubstitution, int start, int length)
        : this(font, isSubstitution, font.Data, start, length)
    {
    }

    private OpenTypeLayoutTable(OpenTypeFont font, bool isSubstitution, byte[] data, int start, int length)
    {
        Font = font;
        IsSubstitution = isSubstitution;
        _data = data;
        _start = start;
        _length = length;

        // https://learn.microsoft.com/typography/opentype/spec/gsub#gsub-header: version 1.0 or 1.1 (with the feature variations)
        var table = Data;
        _scriptList = FontBinary.U16(table, 4);
        _featureList = FontBinary.U16(table, 6);
        _lookupList = FontBinary.U16(table, 8);
        _featureVariations = FontBinary.U16(table, 0) == 1 && FontBinary.U16(table, 2) >= 1 ? (int)Math.Min(FontBinary.U32(table, 10), int.MaxValue) : 0;
        LookupCount = _lookupList == 0 ? 0 : FontBinary.U16(table, _lookupList);
        FeatureCount = _featureList == 0 ? 0 : FontBinary.U16(table, _featureList);
        ScriptCount = _scriptList == 0 ? 0 : FontBinary.U16(table, _scriptList);
        _lookups = new OpenTypeLookup?[LookupCount];
    }

    /// <summary>The font of the table.</summary>
    public OpenTypeFont Font { get; }

    /// <summary>Whether the table is the GSUB table (else GPOS).</summary>
    public bool IsSubstitution { get; }

    /// <summary>The data of the table.</summary>
    public ReadOnlySpan<byte> Data => _data.AsSpan(_start, _length);

    /// <summary>The number of lookups.</summary>
    public int LookupCount { get; }

    /// <summary>The number of features of the feature list.</summary>
    public int FeatureCount { get; }

    /// <summary>The number of scripts of the script list.</summary>
    public int ScriptCount { get; }

    /// <summary>Reads the GSUB or GPOS table of a font, or returns null when the font does not have it (or it is empty).</summary>
    internal static OpenTypeLayoutTable? Create(OpenTypeFont font, uint tag, int offset, int length)
    {
        if (offset < 0 || length < 10)
            return null;

        var table = new OpenTypeLayoutTable(font, tag == OpenTypeTag.Gsub, offset, length);
        return table.LookupCount == 0 ? null : table;
    }

    /// <summary>
    /// Creates a GSUB table from data built for a font (the lookups synthesized by a shaper, e.g. the Arabic fallback shaping
    /// from the presentation forms of the font): the table has no script and no feature, only lookups.
    /// </summary>
    internal static OpenTypeLayoutTable CreateSynthetic(OpenTypeFont font, byte[] data) => new(font, isSubstitution: true, data, 0, data.Length);

    // ----------------------------------------------------------------------------------------------------------------
    // Scripts and language systems (https://learn.microsoft.com/typography/opentype/spec/chapter2#script-list-table-and-script-record)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The tag of a script of the script list.</summary>
    public uint GetScriptTag(int scriptIndex) => FontBinary.U32(Data, _scriptList + 2 + (scriptIndex * 6));

    /// <summary>The index of a script in the script list, or -1.</summary>
    public int FindScript(uint tag)
    {
        var count = ScriptCount;
        var table = Data;
        for (var i = 0; i < count; i++)
        {
            if (FontBinary.U32(table, _scriptList + 2 + (i * 6)) == tag)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Selects the script of the table for the OpenType tags of a script, in order of preference (HarfBuzz's
    /// <c>hb_ot_layout_table_select_script</c>): the first tag the table has, else <c>DFLT</c>, <c>dflt</c> or <c>latn</c>
    /// (some fonts put their features there). Returns whether one of the tags was found; <paramref name="scriptIndex"/> is
    /// <see cref="NoIndex"/> when the table has none of them.
    /// </summary>
    public bool SelectScript(ReadOnlySpan<uint> tags, out int scriptIndex, out uint chosenTag)
    {
        foreach (var tag in tags)
        {
            scriptIndex = FindScript(tag);
            if (scriptIndex >= 0)
            {
                chosenTag = tag;
                return true;
            }
        }

        foreach (var fallback in (ReadOnlySpan<uint>)[OpenTypeTag.DefaultScript, DefaultLanguageTag, OpenTypeTag.LatinScript])
        {
            scriptIndex = FindScript(fallback);
            if (scriptIndex >= 0)
            {
                chosenTag = fallback;
                return false;
            }
        }

        scriptIndex = NoIndex;
        chosenTag = 0;
        return false;
    }

    /// <summary>
    /// Selects the language system of a script for the OpenType tags of a language, in order of preference (HarfBuzz's
    /// <c>hb_ot_layout_script_select_language</c>): the first tag the script has, else <c>dflt</c>, else the default
    /// language system of the script (<see cref="NoIndex"/>).
    /// </summary>
    public int SelectLanguage(int scriptIndex, ReadOnlySpan<uint> tags)
    {
        if (scriptIndex == NoIndex)
            return NoIndex;

        foreach (var tag in tags)
        {
            if (FindLanguage(scriptIndex, tag) is var index and >= 0)
                return index;
        }

        return FindLanguage(scriptIndex, DefaultLanguageTag) is var defaultIndex and >= 0 ? defaultIndex : NoIndex;
    }

    private int FindLanguage(int scriptIndex, uint tag)
    {
        var table = Data;
        var script = GetScriptOffset(scriptIndex);
        if (script == 0)
            return -1;

        var count = FontBinary.U16(table, script + 2);
        for (var i = 0; i < count; i++)
        {
            if (FontBinary.U32(table, script + 4 + (i * 6)) == tag)
                return i;
        }

        return -1;
    }

    private int GetScriptOffset(int scriptIndex)
    {
        if ((uint)scriptIndex >= (uint)ScriptCount)
            return 0;

        var offset = FontBinary.U16(Data, _scriptList + 2 + (scriptIndex * 6) + 4);
        return offset == 0 ? 0 : _scriptList + offset;
    }

    /// <summary>The offset in the table of a language system (the default one for <see cref="NoIndex"/>), or 0 when it does not exist.</summary>
    private int GetLanguageSystemOffset(int scriptIndex, int languageIndex)
    {
        var script = GetScriptOffset(scriptIndex);
        if (script == 0)
            return 0;

        var table = Data;
        int offset;
        if (languageIndex == NoIndex)
        {
            offset = FontBinary.U16(table, script);
        }
        else
        {
            if (languageIndex >= FontBinary.U16(table, script + 2))
                return 0;

            offset = FontBinary.U16(table, script + 4 + (languageIndex * 6) + 4);
        }

        return offset == 0 ? 0 : script + offset;
    }

    /// <summary>The index of the required feature of a language system, or <see cref="NoIndex"/>.</summary>
    public int GetRequiredFeature(int scriptIndex, int languageIndex)
    {
        var languageSystem = GetLanguageSystemOffset(scriptIndex, languageIndex);
        return languageSystem == 0 ? NoIndex : FontBinary.U16(Data, languageSystem + 2);
    }

    /// <summary>
    /// Adds the features of a language system to a map from their tag to their index; the first feature of a tag wins (as
    /// HarfBuzz's <c>hb_ot_layout_collect_features_map</c>: some fonts have duplicate features).
    /// </summary>
    public void CollectFeatures(int scriptIndex, int languageIndex, Dictionary<uint, int> features)
    {
        var languageSystem = GetLanguageSystemOffset(scriptIndex, languageIndex);
        if (languageSystem == 0)
            return;

        var table = Data;
        var count = FontBinary.U16(table, languageSystem + 4);
        for (var i = 0; i < count; i++)
        {
            var index = FontBinary.U16(table, languageSystem + 6 + (i * 2));
            if (index < FeatureCount)
            {
                features.TryAdd(GetFeatureTag(index), index);
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Features (https://learn.microsoft.com/typography/opentype/spec/chapter2#feature-list-table)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The tag of a feature of the feature list.</summary>
    public uint GetFeatureTag(int featureIndex) => (uint)featureIndex < (uint)FeatureCount ? FontBinary.U32(Data, _featureList + 2 + (featureIndex * 6)) : 0;

    /// <summary>The index of the first feature of the feature list with a tag (of any script), or -1.</summary>
    public int FindFeature(uint tag)
    {
        for (var i = 0; i < FeatureCount; i++)
        {
            if (GetFeatureTag(i) == tag)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The indexes of the lookups of a feature, with the substitution of the feature table of the feature variations record
    /// <paramref name="variationsIndex"/> (-1 for none, https://learn.microsoft.com/typography/opentype/spec/chapter2#featurevariations-table).
    /// The lookups are in the order of the feature table (the shape plan sorts them).
    /// </summary>
    public int[] GetFeatureLookups(int featureIndex, int variationsIndex)
    {
        if ((uint)featureIndex >= (uint)FeatureCount)
            return [];

        var key = (featureIndex << 16) | (variationsIndex & 0xFFFF);
        if (_featureLookups.TryGetValue(key, out var cached))
            return cached;

        var table = Data;
        var feature = FindSubstitutedFeature(featureIndex, variationsIndex);
        if (feature == 0)
        {
            var offset = FontBinary.U16(table, _featureList + 2 + (featureIndex * 6) + 4);
            feature = offset == 0 ? 0 : _featureList + offset;
        }

        int[] result = [];
        if (feature != 0)
        {
            var count = FontBinary.U16(table, feature + 2);
            result = new int[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = FontBinary.U16(table, feature + 4 + (i * 2));
            }
        }

        _featureLookups.TryAdd(key, result);
        return result;
    }

    /// <summary>
    /// The index of the first feature variations record whose conditions match an instance of the variable font (the
    /// normalized coordinates of its axes), or -1 (https://learn.microsoft.com/typography/opentype/spec/chapter2#featurevariations-table).
    /// </summary>
    public int FindFeatureVariations(FontVariationInstance? instance)
    {
        if (_featureVariations == 0)
            return -1;

        var table = Data;
        var count = (int)Math.Min(FontBinary.U32(table, _featureVariations + 4), 0xFFFF);
        for (var i = 0; i < count; i++)
        {
            var record = _featureVariations + 8 + (i * 8);
            var conditionSet = FontBinary.U32(table, record);
            if (conditionSet == 0 || EvaluateConditionSet(table, _featureVariations + (int)Math.Min(conditionSet, int.MaxValue - _featureVariations), instance))
                return i;
        }

        return -1;
    }

    private static bool EvaluateConditionSet(ReadOnlySpan<byte> table, int conditionSet, FontVariationInstance? instance)
    {
        var count = FontBinary.U16(table, conditionSet);
        for (var i = 0; i < count; i++)
        {
            var condition = FontBinary.U32(table, conditionSet + 2 + (i * 4));
            if (!EvaluateCondition(table, conditionSet + (int)Math.Min(condition, int.MaxValue - conditionSet), instance, depth: 0))
                return false;
        }

        return true;
    }

    /// <summary>Evaluates a condition: a range of an axis (format 1), or a conjunction, a disjunction or a negation of conditions (formats 3 to 5).</summary>
    private static bool EvaluateCondition(ReadOnlySpan<byte> table, int condition, FontVariationInstance? instance, int depth)
    {
        if (depth > 8)
            return false;

        switch (FontBinary.U16(table, condition))
        {
            case 1:
            {
                var coordinate = instance?.GetCoordinate(FontBinary.U16(table, condition + 2)) ?? 0;
                return coordinate >= FontBinary.F2Dot14(table, condition + 4) && coordinate <= FontBinary.F2Dot14(table, condition + 6);
            }

            case 3 or 4:
            {
                var isAnd = FontBinary.U16(table, condition) == 3;
                var count = FontBinary.U8(table, condition + 2);
                for (var i = 0; i < count; i++)
                {
                    var child = condition + (int)Math.Min(FontBinary.U24(table, condition + 3 + (i * 3)), int.MaxValue - condition);
                    if (EvaluateCondition(table, child, instance, depth + 1) != isAnd)
                        return !isAnd;
                }

                return isAnd;
            }

            case 5:
                return !EvaluateCondition(table, condition + (int)FontBinary.U24(table, condition + 2), instance, depth + 1);
            default:
                return false;
        }
    }

    /// <summary>The offset of the alternate feature table of a feature in a feature variations record, or 0.</summary>
    private int FindSubstitutedFeature(int featureIndex, int variationsIndex)
    {
        if (variationsIndex < 0 || _featureVariations == 0)
            return 0;

        var table = Data;
        var substitutionOffset = FontBinary.U32(table, _featureVariations + 8 + (variationsIndex * 8) + 4);
        if (substitutionOffset == 0 || substitutionOffset > int.MaxValue - _featureVariations)
            return 0;

        var substitution = _featureVariations + (int)substitutionOffset;
        var count = FontBinary.U16(table, substitution + 4);
        for (var i = 0; i < count; i++)
        {
            var record = substitution + 6 + (i * 6);
            if (FontBinary.U16(table, record) == featureIndex)
            {
                var alternate = FontBinary.U32(table, record + 2);
                return alternate == 0 || alternate > int.MaxValue - substitution ? 0 : substitution + (int)alternate;
            }
        }

        return 0;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Lookups (https://learn.microsoft.com/typography/opentype/spec/chapter2#lookup-list-table)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The lookup of an index (parsed on first use), or null when the index is invalid.</summary>
    public OpenTypeLookup? GetLookup(int index)
    {
        if ((uint)index >= (uint)LookupCount)
            return null;

        // The lookups are immutable: a concurrent read creates an equivalent instance
        return _lookups[index] ??= OpenTypeLookup.Create(this, index, _lookupList + FontBinary.U16(Data, _lookupList + 2 + (index * 2)));
    }

    /// <summary>
    /// Whether a GSUB lookup would substitute a sequence of glyphs (HarfBuzz's <c>hb_ot_layout_lookup_would_substitute</c>, the
    /// <c>would_apply</c> of the subtables, hb-ot-layout-gsubgpos.hh, MIT license): the sequence is the whole input of a
    /// substitution, a ligature or a contextual rule (the glyphs are not skipped and the lookup flags are ignored); with
    /// <paramref name="zeroContext"/>, the chained rules that have a backtrack or a lookahead do not match. The syllabic shapers
    /// use it to find the forms of the consonants of a font (e.g. whether a consonant has a below-base form).
    /// </summary>
    public bool WouldSubstitute(int lookupIndex, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        if (!IsSubstitution || glyphs.IsEmpty || GetLookup(lookupIndex) is not { } lookup || !lookup.Digest.MayHave(glyphs[0]))
            return false;

        var table = Data;
        foreach (var subtable in lookup.SubtableOffsets)
        {
            if (WouldApply(table, lookup.Type, subtable, glyphs, zeroContext))
                return true;
        }

        return false;
    }

    private static bool WouldApply(ReadOnlySpan<byte> table, int type, int subtable, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        var format = FontBinary.U16(table, subtable);
        switch (type)
        {
            case OpenTypeLookup.SingleSubstitution or OpenTypeLookup.MultipleSubstitution or OpenTypeLookup.AlternateSubstitution or OpenTypeLookup.ReverseChainedSingleSubstitution:
                return glyphs.Length == 1 && GetCoverageIndex(table, Offset16(table, subtable, subtable + 2), glyphs[0]) >= 0;

            case OpenTypeLookup.LigatureSubstitution when format == 1:
            {
                var index = GetCoverageIndex(table, Offset16(table, subtable, subtable + 2), glyphs[0]);
                if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
                    return false;

                var ligatureSet = Offset16(table, subtable, subtable + 6 + (index * 2));
                if (ligatureSet == 0)
                    return false;

                var count = FontBinary.U16(table, ligatureSet);
                for (var i = 0; i < count; i++)
                {
                    var ligature = Offset16(table, ligatureSet, ligatureSet + 2 + (i * 2));
                    if (ligature != 0 && WouldMatchInput(table, glyphs, FontBinary.U16(table, ligature + 2), ligature + 4, GlyphMatchKind.Glyph, 0))
                        return true;
                }

                return false;
            }

            case OpenTypeLookup.ContextSubstitution:
                return WouldApplyContext(table, subtable, format, glyphs);

            case OpenTypeLookup.ChainedContextSubstitution:
                return WouldApplyChainedContext(table, subtable, format, glyphs, zeroContext);

            default:
                return false;
        }
    }

    private static bool WouldApplyContext(ReadOnlySpan<byte> table, int subtable, int format, ReadOnlySpan<int> glyphs)
    {
        int ruleSet;
        GlyphMatchKind kind;
        var matchData = 0;
        switch (format)
        {
            case 1:
            {
                var index = GetCoverageIndex(table, Offset16(table, subtable, subtable + 2), glyphs[0]);
                if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
                    return false;

                ruleSet = Offset16(table, subtable, subtable + 6 + (index * 2));
                kind = GlyphMatchKind.Glyph;
                break;
            }

            case 2:
            {
                // The rule set of the class of the first glyph (its coverage is not checked, as HarfBuzz)
                matchData = Offset16(table, subtable, subtable + 4);
                var glyphClass = GetClass(table, matchData, glyphs[0]);
                if (glyphClass >= FontBinary.U16(table, subtable + 6))
                    return false;

                ruleSet = Offset16(table, subtable, subtable + 8 + (glyphClass * 2));
                kind = GlyphMatchKind.Class;
                break;
            }

            case 3:
                // The coverages after the first one
                return WouldMatchInput(table, glyphs, FontBinary.U16(table, subtable + 2), subtable + 8, GlyphMatchKind.Coverage, subtable);

            default:
                return false;
        }

        if (ruleSet == 0)
            return false;

        var count = FontBinary.U16(table, ruleSet);
        for (var i = 0; i < count; i++)
        {
            var rule = Offset16(table, ruleSet, ruleSet + 2 + (i * 2));
            if (rule != 0 && WouldMatchInput(table, glyphs, FontBinary.U16(table, rule), rule + 4, kind, matchData))
                return true;
        }

        return false;
    }

    private static bool WouldApplyChainedContext(ReadOnlySpan<byte> table, int subtable, int format, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        int ruleSet;
        GlyphMatchKind kind;
        var matchData = 0;
        switch (format)
        {
            case 1:
            {
                var index = GetCoverageIndex(table, Offset16(table, subtable, subtable + 2), glyphs[0]);
                if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
                    return false;

                ruleSet = Offset16(table, subtable, subtable + 6 + (index * 2));
                kind = GlyphMatchKind.Glyph;
                break;
            }

            case 2:
            {
                matchData = Offset16(table, subtable, subtable + 6);
                var glyphClass = GetClass(table, matchData, glyphs[0]);
                if (glyphClass >= FontBinary.U16(table, subtable + 10))
                    return false;

                ruleSet = Offset16(table, subtable, subtable + 12 + (glyphClass * 2));
                kind = GlyphMatchKind.Class;
                break;
            }

            case 3:
            {
                var backtrackCount = FontBinary.U16(table, subtable + 2);
                var input = subtable + 4 + (backtrackCount * 2);
                var inputCount = FontBinary.U16(table, input);
                var lookaheadCount = FontBinary.U16(table, input + 2 + (inputCount * 2));
                return (!zeroContext || (backtrackCount == 0 && lookaheadCount == 0)) && WouldMatchInput(table, glyphs, inputCount, input + 4, GlyphMatchKind.Coverage, subtable);
            }

            default:
                return false;
        }

        if (ruleSet == 0)
            return false;

        var count = FontBinary.U16(table, ruleSet);
        for (var i = 0; i < count; i++)
        {
            var rule = Offset16(table, ruleSet, ruleSet + 2 + (i * 2));
            if (rule == 0)
                continue;

            var backtrackCount = FontBinary.U16(table, rule);
            var input = rule + 2 + (backtrackCount * 2);
            var inputCount = FontBinary.U16(table, input);
            if (inputCount == 0)
                continue;

            var lookaheadCount = FontBinary.U16(table, input + 2 + ((inputCount - 1) * 2));
            if ((!zeroContext || (backtrackCount == 0 && lookaheadCount == 0)) && WouldMatchInput(table, glyphs, inputCount, input + 2, kind, matchData))
                return true;
        }

        return false;
    }

    /// <summary>Whether the glyphs after the first one match the values of an input sequence of <paramref name="count"/> glyphs (HarfBuzz's <c>would_match_input</c>).</summary>
    private static bool WouldMatchInput(ReadOnlySpan<byte> table, ReadOnlySpan<int> glyphs, int count, int values, GlyphMatchKind kind, int matchData)
    {
        if (count != glyphs.Length)
            return false;

        for (var i = 1; i < count; i++)
        {
            var value = FontBinary.U16(table, values + ((i - 1) * 2));
            var matched = kind switch
            {
                GlyphMatchKind.Glyph => glyphs[i] == value,
                GlyphMatchKind.Class => GetClass(table, matchData, glyphs[i]) == value,
                _ => value != 0 && GetCoverageIndex(table, matchData + value, glyphs[i]) >= 0,
            };

            if (!matched)
                return false;
        }

        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Common structures
    // ----------------------------------------------------------------------------------------------------------------

    private const int LookupCacheSize = 4096;
    private readonly ulong[] _lookupCache = new ulong[LookupCacheSize];

    /// <summary>
    /// The coverage index of a glyph in a coverage table of the table (<see cref="GetCoverageIndex(ReadOnlySpan{byte}, int, int)"/>),
    /// through a direct-mapped cache of the recent lookups (the lookups of the shaping read the same coverages for the same glyphs).
    /// </summary>
    public int GetCachedCoverageIndex(ReadOnlySpan<byte> table, int coverage, int glyph)
    {
        if (coverage <= 0)
            return -1;

        var value = GetCached(coverage, glyph, isClass: false, out var found);
        if (found)
            return value == 0xFFFF ? -1 : value;

        var index = GetCoverageIndex(table, coverage, glyph);
        SetCached(coverage, glyph, isClass: false, index < 0 ? 0xFFFF : index);
        return index;
    }

    /// <summary>The class of a glyph in a class definition of the table (<see cref="GetClass"/>), through the cache of the recent lookups.</summary>
    public int GetCachedClass(ReadOnlySpan<byte> table, int classDefinition, int glyph)
    {
        if (classDefinition <= 0)
            return 0;

        var value = GetCached(classDefinition, glyph, isClass: true, out var found);
        if (found)
            return value;

        var result = GetClass(table, classDefinition, glyph);
        SetCached(classDefinition, glyph, isClass: true, result);
        return result;
    }

    // An entry is the offset of the structure (31 bits, plus the bit of the class definitions), the glyph and the value, written atomically
    private int GetCached(int offset, int glyph, bool isClass, out bool found)
    {
        var key = ((ulong)(uint)((offset << 1) | (isClass ? 1 : 0)) << 32) | ((ulong)(uint)glyph << 16);
        var entry = Volatile.Read(ref _lookupCache[GetCacheIndex(offset, glyph)]);
        found = (entry & 0xFFFFFFFFFFFF0000) == key && entry != 0;
        return (int)(entry & 0xFFFF);
    }

    private void SetCached(int offset, int glyph, bool isClass, int value)
    {
        if (value is < 0 or > 0xFFFF || offset > int.MaxValue >> 1)
            return;

        var key = ((ulong)(uint)((offset << 1) | (isClass ? 1 : 0)) << 32) | ((ulong)(uint)glyph << 16);
        Volatile.Write(ref _lookupCache[GetCacheIndex(offset, glyph)], key | (uint)value);
    }

    private static int GetCacheIndex(int offset, int glyph) => unchecked((int)(((uint)offset * 0x9E3779B1u) ^ ((uint)glyph * 0x85EBCA77u)) >>> 20) & (LookupCacheSize - 1);

    /// <summary>The index of a glyph in a coverage table, or -1 (https://learn.microsoft.com/typography/opentype/spec/chapter2#coverage-table).</summary>
    internal static int GetCoverageIndex(ReadOnlySpan<byte> table, int coverage, int glyph)
    {
        if (coverage <= 0)
            return -1;

        var format = FontBinary.U16(table, coverage);
        var count = FontBinary.U16(table, coverage + 2);
        if (format == 1)
        {
            int low = 0, high = Math.Min(count, (table.Length - coverage - 4) / 2) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var value = FontBinary.U16(table, coverage + 4 + (middle * 2));
                if (value < glyph)
                {
                    low = middle + 1;
                }
                else if (value > glyph)
                {
                    high = middle - 1;
                }
                else
                {
                    return middle;
                }
            }
        }
        else if (format == 2)
        {
            int low = 0, high = Math.Min(count, (table.Length - coverage - 4) / 6) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var range = coverage + 4 + (middle * 6);
                if (glyph < FontBinary.U16(table, range))
                {
                    high = middle - 1;
                }
                else if (glyph > FontBinary.U16(table, range + 2))
                {
                    low = middle + 1;
                }
                else
                {
                    return FontBinary.U16(table, range + 4) + glyph - FontBinary.U16(table, range);
                }
            }
        }

        return -1;
    }

    /// <summary>Adds the glyphs of a coverage table to a digest.</summary>
    internal static void AddCoverage(ReadOnlySpan<byte> table, int coverage, ref GlyphDigest digest)
    {
        if (coverage <= 0)
            return;

        var format = FontBinary.U16(table, coverage);
        var count = FontBinary.U16(table, coverage + 2);
        if (format == 1)
        {
            count = (ushort)Math.Min(count, Math.Max(0, (table.Length - coverage - 4) / 2));
            for (var i = 0; i < count; i++)
            {
                digest.Add(FontBinary.U16(table, coverage + 4 + (i * 2)));
            }
        }
        else if (format == 2)
        {
            count = (ushort)Math.Min(count, Math.Max(0, (table.Length - coverage - 4) / 6));
            for (var i = 0; i < count; i++)
            {
                digest.AddRange(FontBinary.U16(table, coverage + 4 + (i * 6)), FontBinary.U16(table, coverage + 4 + (i * 6) + 2));
            }
        }
    }

    /// <summary>The class of a glyph in a class definition table, 0 when the glyph is not listed (https://learn.microsoft.com/typography/opentype/spec/chapter2#class-definition-table).</summary>
    internal static int GetClass(ReadOnlySpan<byte> table, int classDefinition, int glyph)
    {
        if (classDefinition <= 0)
            return 0;

        var format = FontBinary.U16(table, classDefinition);
        if (format == 1)
        {
            var start = FontBinary.U16(table, classDefinition + 2);
            var count = FontBinary.U16(table, classDefinition + 4);
            return glyph >= start && glyph < start + count ? FontBinary.U16(table, classDefinition + 6 + ((glyph - start) * 2)) : 0;
        }

        if (format == 2)
        {
            var count = FontBinary.U16(table, classDefinition + 2);
            int low = 0, high = Math.Min(count, (table.Length - classDefinition - 4) / 6) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var range = classDefinition + 4 + (middle * 6);
                if (glyph < FontBinary.U16(table, range))
                {
                    high = middle - 1;
                }
                else if (glyph > FontBinary.U16(table, range + 2))
                {
                    low = middle + 1;
                }
                else
                {
                    return FontBinary.U16(table, range + 4);
                }
            }
        }

        return 0;
    }

    /// <summary>The offset of a subtable from an offset field of 16 bits at <paramref name="field"/> relative to <paramref name="baseOffset"/>, or 0 when it is null.</summary>
    internal static int Offset16(ReadOnlySpan<byte> table, int baseOffset, int field)
    {
        var offset = FontBinary.U16(table, field);
        return offset == 0 ? 0 : baseOffset + offset;
    }
}
