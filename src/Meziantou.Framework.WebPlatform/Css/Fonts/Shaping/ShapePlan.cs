using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The flags of a feature added to a <see cref="ShapePlanBuilder"/> (HarfBuzz's <c>hb_ot_map_feature_flags_t</c>).</summary>
[Flags]
internal enum FeatureFlags
{
    None = 0,

    /// <summary>The feature applies to all the characters (it gets the global mask bit when its value is 1).</summary>
    Global = 0x1,

    /// <summary>The feature has a fallback implementation: its mask is allocated even when the font does not have it.</summary>
    HasFallback = 0x2,

    /// <summary>The ZWNJ characters are not skipped when the context is matched.</summary>
    ManualZwnj = 0x4,

    /// <summary>The ZWJ characters are not skipped when the input is matched.</summary>
    ManualZwj = 0x8,

    ManualJoiners = ManualZwnj | ManualZwj,
    GlobalManualJoiners = Global | ManualJoiners,
    GlobalHasFallback = Global | HasFallback,

    /// <summary>When the language system does not have the feature, the first feature with its tag in the feature list is used.</summary>
    GlobalSearch = 0x10,

    /// <summary>The alternate substitutions pick a random alternate (the <c>rand</c> feature).</summary>
    Random = 0x20,

    /// <summary>The lookups match within a syllable.</summary>
    PerSyllable = 0x40,
}

/// <summary>A callback between two stages of the lookups (e.g. the reordering of a syllabic shaper); returns whether it changed the glyphs.</summary>
internal delegate bool ShapingPause(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer);

/// <summary>A feature of a <see cref="ShapePlan"/>: its lookups apply to the glyphs that have its mask.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapePlanFeature(uint Tag, int GsubIndex, int GposIndex, int GsubStage, int GposStage, int Shift, uint Mask, uint OneMask, bool NeedsFallback, bool AutoZwnj, bool AutoZwj, bool Random, bool PerSyllable);

/// <summary>A lookup of a <see cref="ShapePlan"/>, with the mask of its features.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapePlanLookup(int Index, uint Mask, bool AutoZwnj, bool AutoZwj, bool Random, bool PerSyllable, uint FeatureTag);

/// <summary>The end of a stage of the lookups (the index after its last lookup) and the callback that follows it.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapePlanStage(int LastLookup, ShapingPause? Pause);

/// <summary>
/// The shaping plan of a font for a script, a language, a direction and a set of features (HarfBuzz's
/// <c>hb_ot_shape_plan_t</c> and <c>hb_ot_map_t</c>, https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-map.cc and
/// hb-ot-shape.cc, MIT license): the shaper of the script, the features with the bits of their masks, the lookups of GSUB and
/// GPOS in the order they are applied (sorted by index within a stage) and the decisions of the positioning (GPOS, the
/// <c>kern</c> table or the fallback positioning). Plans are immutable and cached per font (<see cref="OpenTypeFont.GetShapePlan"/>).
/// </summary>
internal sealed class ShapePlan
{
    /// <summary>The maximum value of a feature (a feature has at most 8 bits of mask).</summary>
    public const uint MaxFeatureValue = 255;

    private readonly ShapePlanFeature[] _features;
    private readonly ShapePlanLookup[][] _lookups;
    private readonly ShapePlanStage[][] _stages;

    // Whether the GPOS lookups are only pair kerning (0 not computed yet, 1 yes, 2 no), the masks of the lookups and the cache of the pairs
    private int _pairKerningState;
    private uint _pairKerningMask;
    private PairKerningCache? _pairKerningCache;

    internal ShapePlan(ShapePlanBuilder builder, ShapePlanFeature[] features, ShapePlanLookup[][] lookups, ShapePlanStage[][] stages, uint globalMask)
    {
        Font = builder.Font;
        Script = builder.Script;
        Direction = builder.Direction;
        Shaper = builder.Shaper;
        Gsub = builder.Gsub;
        Gpos = builder.Gpos;
        Gdef = builder.Font.LayoutGdef;
        ChosenScript = builder.ChosenScript;
        FoundScript = builder.FoundScript;
        _features = features;
        _lookups = lookups;
        _stages = stages;
        GlobalMask = globalMask;
    }

    /// <summary>The font of the plan.</summary>
    public OpenTypeFont Font { get; }

    public UnicodeScript Script { get; }

    public ShapingDirection Direction { get; }

    /// <summary>The shaper of the script.</summary>
    public OpenTypeShaper Shaper { get; }

    /// <summary>The data of the shaper (<see cref="OpenTypeShaper.CreateData"/>).</summary>
    public object? ShaperData { get; internal set; }

    public OpenTypeLayoutTable? Gsub { get; }

    public OpenTypeLayoutTable? Gpos { get; }

    public OpenTypeGdef? Gdef { get; }

    /// <summary>The script tags chosen in GSUB and GPOS (0 when the table has no script).</summary>
    public uint[] ChosenScript { get; }

    /// <summary>Whether the script tags were found in GSUB and GPOS (else a fallback script was chosen).</summary>
    public bool[] FoundScript { get; }

    /// <summary>The mask of the global features (and of the default values of the other features).</summary>
    public uint GlobalMask { get; }

    // The decisions of hb_ot_shape_planner_t::compile
    public uint FracMask { get; internal set; }

    public uint NumrMask { get; internal set; }

    public uint DnomMask { get; internal set; }

    public bool HasFrac { get; internal set; }

    public uint RtlmMask { get; internal set; }

    public bool HasVert { get; internal set; }

    public uint KernMask { get; internal set; }

    public bool RequestedKerning { get; internal set; }

    public bool FallbackGlyphClasses { get; internal set; }

    public bool ApplyGpos { get; internal set; }

    /// <summary>Whether the <c>kern</c> table is applied (the font has no GPOS kerning).</summary>
    public bool ApplyKern { get; internal set; }

    public bool ZeroMarks { get; internal set; }

    public bool HasGposMark { get; internal set; }

    public bool AdjustMarkPositioningWhenZeroing { get; internal set; }

    public bool FallbackMarkPositioning { get; internal set; }

    /// <summary>The features of the plan, sorted by tag.</summary>
    public ReadOnlySpan<ShapePlanFeature> Features => _features;

    /// <summary>The lookups of GSUB (0) or GPOS (1), in the order they are applied.</summary>
    public ReadOnlySpan<ShapePlanLookup> GetLookups(int table) => _lookups[table];

    /// <summary>The stages of GSUB (0) or GPOS (1).</summary>
    public ReadOnlySpan<ShapePlanStage> GetStages(int table) => _stages[table];

    private bool TryGetFeature(uint tag, out ShapePlanFeature feature)
    {
        var features = _features;
        int low = 0, high = features.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var value = features[middle].Tag;
            if (value < tag)
            {
                low = middle + 1;
            }
            else if (value > tag)
            {
                high = middle - 1;
            }
            else
            {
                feature = features[middle];
                return true;
            }
        }

        feature = default;
        return false;
    }

    /// <summary>The mask of a feature, and the shift of its value; 0 when the plan does not have it.</summary>
    public uint GetMask(uint tag, out int shift)
    {
        if (TryGetFeature(tag, out var feature))
        {
            shift = feature.Shift;
            return feature.Mask;
        }

        shift = 0;
        return 0;
    }

    /// <summary>The mask of a feature, 0 when the plan does not have it.</summary>
    public uint GetMask(uint tag) => GetMask(tag, out _);

    /// <summary>The mask of the value 1 of a feature, 0 when the plan does not have it.</summary>
    public uint GetOneMask(uint tag) => TryGetFeature(tag, out var feature) ? feature.OneMask : 0;

    /// <summary>Whether a feature of the plan is not in the font (its fallback implementation applies).</summary>
    public bool NeedsFallback(uint tag) => TryGetFeature(tag, out var feature) && feature.NeedsFallback;

    /// <summary>The index of a feature in GSUB (0) or GPOS (1), or <see cref="OpenTypeLayoutTable.NoIndex"/>.</summary>
    public int GetFeatureIndex(int table, uint tag) => TryGetFeature(tag, out var feature) ? (table == 0 ? feature.GsubIndex : feature.GposIndex) : OpenTypeLayoutTable.NoIndex;

    /// <summary>The stage of a feature in GSUB (0) or GPOS (1), or -1 when the plan does not have it (HarfBuzz's <c>get_feature_stage</c>).</summary>
    public int GetFeatureStage(int table, uint tag) => TryGetFeature(tag, out var feature) ? (table == 0 ? feature.GsubStage : feature.GposStage) : -1;

    /// <summary>The lookups of a stage of GSUB (0) or GPOS (1) (HarfBuzz's <c>get_stage_lookups</c>).</summary>
    public ReadOnlySpan<ShapePlanLookup> GetStageLookups(int table, int stage)
    {
        var stages = _stages[table];
        if ((uint)stage > (uint)stages.Length)
            return [];

        var start = stage > 0 ? stages[stage - 1].LastLookup : 0;
        var end = stage < stages.Length ? stages[stage].LastLookup : _lookups[table].Length;
        return _lookups[table].AsSpan(start, end - start);
    }

    /// <summary>
    /// Whether the lookups of the stage of a GSUB feature would substitute a sequence of glyphs (HarfBuzz's
    /// <c>hb_indic_would_substitute_feature_t</c>): the syllabic shapers use it to find the forms of the consonants.
    /// </summary>
    public bool WouldSubstitute(uint feature, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        if (Gsub is null)
            return false;

        var stage = GetFeatureStage(0, feature);
        if (stage < 0)
            return false;

        foreach (var lookup in GetStageLookups(0, stage))
        {
            if (Gsub.WouldSubstitute(lookup.Index, glyphs, zeroContext))
                return true;
        }

        return false;
    }

    /// <summary>Applies the GSUB lookups to the buffer, stage by stage.</summary>
    public void Substitute(in ShapingFont font, ShapingBuffer buffer)
    {
        if (Gsub is not null)
        {
            Apply(0, Gsub, font, buffer);
        }
        else
        {
            RunPauses(0, font, buffer);
        }
    }

    /// <summary>Applies the GPOS lookups to the buffer, stage by stage.</summary>
    public void Position(in ShapingFont font, ShapingBuffer buffer)
    {
        if (Gpos is not null)
        {
            if (CanApplyPairKerning(buffer))
            {
                // The usual kerning of a text without marks: the same result with a cache of the pairs
                var cache = font.Instance is null || font.Instance.IsDefault ? _pairKerningCache : null;
                buffer.GetApplyContext(Gpos, Gdef, font.Instance).ApplyPairKerning(this, _lookups[1], cache);
                return;
            }

            Apply(1, Gpos, font, buffer);
        }
        else
        {
            RunPauses(1, font, buffer);
        }
    }

    /// <summary>
    /// Whether the GPOS lookups are only pair kerning (<see cref="OpenTypeApplyContext.IsPairKerningOnly"/>), without pause,
    /// and the buffer has neither marks nor default ignorable characters and the masks of its glyphs select the lookups of
    /// the global features only: the lookups then apply to each pair of consecutive glyphs.
    /// </summary>
    private bool CanApplyPairKerning(ShapingBuffer buffer)
    {
        var state = Volatile.Read(ref _pairKerningState);
        if (state == 0)
        {
            var lookups = _lookups[1];
            var pairKerning = OpenTypeApplyContext.IsPairKerningOnly(Gpos!, lookups);
            var mask = 0u;
            foreach (var stage in _stages[1])
            {
                pairKerning &= stage.Pause is null;
            }

            foreach (var entry in lookups)
            {
                mask |= entry.Mask;
            }

            if (pairKerning)
            {
                _pairKerningMask = mask;
                _pairKerningCache = new PairKerningCache();
            }

            state = pairKerning ? 1 : 2;
            Volatile.Write(ref _pairKerningState, state);
        }

        if (state != 1)
            return false;

        var lookupMask = _pairKerningMask;
        var globalMask = GlobalMask & lookupMask;
        foreach (ref readonly var info in buffer.Info.AsSpan(0, buffer.Length))
        {
            if ((info.Mask & lookupMask) != globalMask || info.IsMark || (info.UnicodeProperties & (UnicodeGlyphProperties.Ignorable | UnicodeGlyphProperties.Hidden)) != UnicodeGlyphProperties.None)
                return false;
        }

        return true;
    }

    /// <summary>The pauses of a table without lookups (a shaper may reorder the glyphs in them).</summary>
    private void RunPauses(int tableIndex, in ShapingFont font, ShapingBuffer buffer)
    {
        foreach (var stage in _stages[tableIndex])
        {
            stage.Pause?.Invoke(this, font, buffer);
        }
    }

    /// <summary>Applies the lookups of a table (HarfBuzz's <c>hb_ot_map_t::apply</c>).</summary>
    private void Apply(int tableIndex, OpenTypeLayoutTable table, in ShapingFont font, ShapingBuffer buffer)
    {
        var context = buffer.GetApplyContext(table, Gdef, font.Instance);
        var lookups = _lookups[tableIndex];
        var i = 0;
        foreach (var stage in _stages[tableIndex])
        {
            for (; i < stage.LastLookup; i++)
            {
                var entry = lookups[i];
                var lookup = table.GetLookup(entry.Index);
                if (lookup is null || !lookup.Digest.MayIntersect(buffer.Digest))
                    continue;

                context.LookupIndex = entry.Index;
                context.SetLookupMask(entry.Mask, init: false);
                context.SetAutoZwj(entry.AutoZwj, init: false);
                context.SetAutoZwnj(entry.AutoZwnj, init: false);
                context.Random = entry.Random;
                context.SetPerSyllable(entry.PerSyllable, init: false);

                // The substituted glyphs are added to the digest of the buffer as they are substituted
                context.ApplyLookup(lookup);
            }

            if (stage.Pause is { } pause && pause(this, font, buffer))
            {
                buffer.UpdateDigest();
            }
        }
    }
}

/// <summary>
/// The advances of the pairs of glyphs adjusted by the pair kerning of a <see cref="ShapePlan"/> (a direct-mapped cache; each
/// entry is written atomically: the pair of glyphs in the high 32 bits, the advance as a float in the low 32 bits, NaN when no
/// value applies). Only the exactly representable advances are cached.
/// </summary>
internal sealed class PairKerningCache
{
    private const int Size = 2048;
    private readonly ulong[] _entries = new ulong[Size];

    private static int GetIndex(uint key) => (int)((key * 2654435769u) >> 21);

    public bool TryGet(int first, int second, out double advance)
    {
        var key = ((uint)first << 16) | (uint)second;
        var entry = Volatile.Read(ref _entries[GetIndex(key)]);
        if ((uint)(entry >> 32) == key && entry != 0)
        {
            advance = BitConverter.Int32BitsToSingle((int)(uint)entry);
            return true;
        }

        advance = 0;
        return false;
    }

    public void Set(int first, int second, double advance)
    {
        var value = (float)advance;
        if ((uint)first > ushort.MaxValue || (uint)second > ushort.MaxValue || (!double.IsNaN(advance) && value != advance))
            return;

        var key = ((uint)first << 16) | (uint)second;
        var entry = ((ulong)key << 32) | (uint)BitConverter.SingleToInt32Bits(value);
        if (entry != 0)
        {
            Volatile.Write(ref _entries[GetIndex(key)], entry);
        }
    }
}
