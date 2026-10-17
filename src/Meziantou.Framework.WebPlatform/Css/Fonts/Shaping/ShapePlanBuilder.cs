using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The key of a cached <see cref="ShapePlan"/> of a font.</summary>
/// <param name="Script">The script of the run.</param>
/// <param name="Direction">The direction of the run.</param>
/// <param name="Language">The BCP 47 language tag of the run (the content language), or null.</param>
/// <param name="LanguageOverride">The OpenType language system tag of <c>font-language-override</c>, or 0.</param>
/// <param name="FaceFeatures">The features of the <c>font-feature-settings</c> descriptor of the font face, or null.</param>
/// <param name="Features">The features of the run (from the CSS properties), or null.</param>
/// <param name="GsubVariations">The index of the feature variations record of GSUB for the instance of the font, or -1.</param>
/// <param name="GposVariations">The index of the feature variations record of GPOS, or -1.</param>
/// <param name="DisableKerning">Whether the kerning is disabled (<c>font-kerning: none</c>): the <c>kern</c> feature is disabled after the other features.</param>
/// <param name="ProportionalAlternates">Whether the <c>palt</c> (or <c>vpal</c>) feature is enabled before the features of the run (the kerning of the CJK text).</param>
/// <param name="VerticalKerning">Whether the <c>vkrn</c> feature is enabled for vertical text (<c>font-kerning: normal</c>).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapePlanKey(UnicodeScript Script, ShapingDirection Direction, string? Language, uint LanguageOverride, FontFeatureList? FaceFeatures, FontFeatureList? Features, int GsubVariations, int GposVariations, bool DisableKerning, bool ProportionalAlternates = false, bool VerticalKerning = false);

/// <summary>
/// Builds a <see cref="ShapePlan"/> (HarfBuzz's <c>hb_ot_map_builder_t</c> and <c>hb_ot_shape_planner_t</c>, hb-ot-map.cc and
/// hb-ot-shape.cc, MIT license): selects the script and the language system of GSUB and GPOS, collects the features of the
/// shaping (the default features, the ones of the shaper of the script and the features of the run), allocates the bits of
/// their masks and collects their lookups by stage.
/// </summary>
internal sealed class ShapePlanBuilder
{
    private static readonly uint MarkTag = Tag("mark");
    private static readonly uint VertTag = Tag("vert");

    private readonly List<FeatureInfo> _features = [];
    private readonly int[] _currentStage = new int[2];
    private readonly List<(int Index, ShapingPause? Pause)>[] _stages = [[], []];
    private readonly int[] _scriptIndex = new int[2];
    private readonly int[] _languageIndex = new int[2];
    private readonly ShapePlanKey _key;

    private ShapePlanBuilder(OpenTypeFont font, in ShapePlanKey key)
    {
        _key = key;
        Font = font;
        Script = key.Script;
        Direction = key.Direction;
        Gsub = font.LayoutGsub;
        Gpos = font.LayoutGpos;

        // https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-map.cc: the script and the language system of each table
        Span<uint> scriptTags = stackalloc uint[3];
        var scriptCount = OpenTypeScriptTags.GetTags(Script, scriptTags);
        var languageTags = key.LanguageOverride != 0 ? [key.LanguageOverride] : OpenTypeLanguageTags.GetTags(key.Language);
        for (var table = 0; table < 2; table++)
        {
            var layout = table == 0 ? Gsub : Gpos;
            if (layout is null)
            {
                _scriptIndex[table] = OpenTypeLayoutTable.NoIndex;
                _languageIndex[table] = OpenTypeLayoutTable.NoIndex;
                continue;
            }

            FoundScript[table] = layout.SelectScript(scriptTags[..scriptCount], out _scriptIndex[table], out ChosenScript[table]);
            _languageIndex[table] = layout.SelectLanguage(_scriptIndex[table], languageTags);
        }

        Shaper = OpenTypeShaper.Categorize(Script, Direction, ChosenScript[0]);
    }

    public OpenTypeFont Font { get; }

    public UnicodeScript Script { get; }

    public ShapingDirection Direction { get; }

    public OpenTypeLayoutTable? Gsub { get; }

    public OpenTypeLayoutTable? Gpos { get; }

    /// <summary>The shaper of the script.</summary>
    public OpenTypeShaper Shaper { get; }

    /// <summary>The script tags chosen in GSUB and GPOS.</summary>
    public uint[] ChosenScript { get; } = new uint[2];

    /// <summary>Whether the script tags were found in GSUB and GPOS.</summary>
    public bool[] FoundScript { get; } = new bool[2];

    /// <summary>Creates the plan of a font for a key.</summary>
    public static ShapePlan Build(OpenTypeFont font, in ShapePlanKey key)
    {
        var builder = new ShapePlanBuilder(font, key);
        builder.CollectFeatures();
        return builder.Compile();
    }

    /// <summary>Adds a feature, with the current stages of GSUB and GPOS (a later addition of the same feature overrides its value).</summary>
    public void AddFeature(uint tag, FeatureFlags flags = FeatureFlags.None, uint value = 1)
    {
        if (tag == 0)
            return;

        _features.Add(new FeatureInfo
        {
            Tag = tag,
            Sequence = _features.Count + 1,
            MaxValue = value,
            Flags = flags,
            DefaultValue = (flags & FeatureFlags.Global) != FeatureFlags.None ? value : 0,
            GsubStage = _currentStage[0],
            GposStage = _currentStage[1],
        });
    }

    /// <summary>Adds a feature that applies to all the characters.</summary>
    public void EnableFeature(uint tag, FeatureFlags flags = FeatureFlags.None, uint value = 1) => AddFeature(tag, flags | FeatureFlags.Global, value);

    /// <summary>Disables a feature.</summary>
    public void DisableFeature(uint tag) => AddFeature(tag, FeatureFlags.Global, 0);

    /// <summary>Ends a stage of the GSUB lookups; the pause runs after its lookups.</summary>
    public void AddGsubPause(ShapingPause? pause) => AddPause(0, pause);

    /// <summary>Ends a stage of the GPOS lookups; the pause runs after its lookups.</summary>
    public void AddGposPause(ShapingPause? pause) => AddPause(1, pause);

    private void AddPause(int table, ShapingPause? pause)
    {
        _stages[table].Add((_currentStage[table], pause));
        _currentStage[table]++;
    }

    /// <summary>Whether the language system of GSUB or GPOS has a feature.</summary>
    public bool HasFeature(uint tag)
    {
        for (var table = 0; table < 2; table++)
        {
            var layout = table == 0 ? Gsub : Gpos;
            if (layout is null)
                continue;

            var features = new Dictionary<uint, int>();
            layout.CollectFeatures(_scriptIndex[table], _languageIndex[table], features);
            if (features.ContainsKey(tag))
                return true;
        }

        return false;
    }

    /// <summary>Collects the features of the shaping in the order of HarfBuzz (hb_ot_shape_collect_features).</summary>
    private void CollectFeatures()
    {
        EnableFeature(Tag("rvrn"));
        AddGsubPause(null);

        switch (Direction)
        {
            case ShapingDirection.LeftToRight:
                EnableFeature(Tag("ltra"));
                EnableFeature(Tag("ltrm"));
                break;
            case ShapingDirection.RightToLeft:
                EnableFeature(Tag("rtla"));
                AddFeature(Tag("rtlm"));
                break;
        }

        // The automatic fractions
        AddFeature(Tag("frac"));
        AddFeature(Tag("numr"));
        AddFeature(Tag("dnom"));

        EnableFeature(Tag("rand"), FeatureFlags.Random, ShapePlan.MaxFeatureValue);

        Shaper.CollectFeatures(this);

        // The common features
        EnableFeature(Tag("abvm"));
        EnableFeature(Tag("blwm"));
        EnableFeature(Tag("ccmp"));
        EnableFeature(Tag("locl"));
        EnableFeature(MarkTag, FeatureFlags.ManualJoiners);
        EnableFeature(Tag("mkmk"), FeatureFlags.ManualJoiners);
        EnableFeature(Tag("rlig"));

        if (Direction != ShapingDirection.TopToBottom)
        {
            EnableFeature(Tag("calt"));
            EnableFeature(Tag("clig"));
            EnableFeature(Tag("curs"));
            EnableFeature(Tag("dist"));
            EnableFeature(OpenTypeTag.Kern, FeatureFlags.HasFallback);
            EnableFeature(Tag("liga"));
            EnableFeature(Tag("rclt"));
        }
        else
        {
            // Only the vert feature applies to vertical text, found in any script of the font; the vertical kerning is requested
            // by font-kerning: normal (as Chrome)
            EnableFeature(VertTag, FeatureFlags.GlobalSearch);
            if (_key.VerticalKerning)
            {
                EnableFeature(OpenTypeTag.VerticalKerning);
            }
        }

        // The features of the font face, then the ones of the run (a later value of a feature wins)
        AddUserFeatures(_key.FaceFeatures);
        if (_key.ProportionalAlternates)
        {
            EnableFeature(Tag(Direction != ShapingDirection.TopToBottom ? "palt" : "vpal"));
        }

        AddUserFeatures(_key.Features);
        if (_key.DisableKerning)
        {
            DisableFeature(Direction != ShapingDirection.TopToBottom ? OpenTypeTag.Kern : OpenTypeTag.VerticalKerning);
        }

        Shaper.OverrideFeatures(this);

        void AddUserFeatures(FontFeatureList? features)
        {
            if (features is null)
                return;

            foreach (var feature in features.Items)
            {
                AddFeature(feature.Tag, FeatureFlags.Global, feature.Value);
            }
        }
    }

    /// <summary>Compiles the features and their lookups (HarfBuzz's <c>hb_ot_map_builder_t::compile</c> and <c>hb_ot_shape_planner_t::compile</c>).</summary>
    private ShapePlan Compile()
    {
        const int GlobalBitShift = 31;
        const uint GlobalBitMask = 1u << GlobalBitShift;
        var globalMask = GlobalBitMask;

        // The required features of the language systems
        Span<int> requiredFeatureIndex = stackalloc int[2];
        Span<uint> requiredFeatureTag = stackalloc uint[2];
        Span<int> requiredFeatureStage = stackalloc int[2];
        var featureIndexes = new Dictionary<uint, int>[2];
        for (var table = 0; table < 2; table++)
        {
            var layout = table == 0 ? Gsub : Gpos;
            requiredFeatureIndex[table] = layout?.GetRequiredFeature(_scriptIndex[table], _languageIndex[table]) ?? OpenTypeLayoutTable.NoIndex;
            requiredFeatureTag[table] = requiredFeatureIndex[table] != OpenTypeLayoutTable.NoIndex ? layout!.GetFeatureTag(requiredFeatureIndex[table]) : 0;
            requiredFeatureStage[table] = 0;
            featureIndexes[table] = [];
            layout?.CollectFeatures(_scriptIndex[table], _languageIndex[table], featureIndexes[table]);
        }

        // Sort the features by tag (the later additions last) and merge the duplicates
        _features.Sort(static (a, b) => a.Tag != b.Tag ? a.Tag.CompareTo(b.Tag) : a.Sequence.CompareTo(b.Sequence));
        var merged = new List<FeatureInfo>(_features.Count);
        foreach (var feature in _features)
        {
            if (merged.Count == 0 || merged[^1].Tag != feature.Tag)
            {
                merged.Add(feature);
                continue;
            }

            var last = merged[^1];
            if ((feature.Flags & FeatureFlags.Global) != FeatureFlags.None)
            {
                last.Flags |= FeatureFlags.Global;
                last.MaxValue = feature.MaxValue;
                last.DefaultValue = feature.DefaultValue;
            }
            else
            {
                last.Flags &= ~FeatureFlags.Global;
                last.MaxValue = Math.Max(last.MaxValue, feature.MaxValue);
            }

            last.Flags |= feature.Flags & FeatureFlags.HasFallback;
            last.GsubStage = Math.Min(last.GsubStage, feature.GsubStage);
            last.GposStage = Math.Min(last.GposStage, feature.GposStage);
            merged[^1] = last;
        }

        // Allocate the bits of the masks
        var nextBit = System.Numerics.BitOperations.PopCount(GlyphFlags.Defined) + 1;
        var features = new List<ShapePlanFeature>(merged.Count);
        Span<int> featureIndex = stackalloc int[2];
        foreach (var info in merged)
        {
            var usesGlobalBit = (info.Flags & FeatureFlags.Global) != FeatureFlags.None && info.MaxValue == 1;
            var bitsNeeded = usesGlobalBit ? 0 : Math.Min(8, 32 - System.Numerics.BitOperations.LeadingZeroCount(info.MaxValue));
            if (info.MaxValue == 0 || nextBit + bitsNeeded >= GlobalBitShift)
                continue;

            var found = false;
            for (var table = 0; table < 2; table++)
            {
                if (requiredFeatureTag[table] == info.Tag)
                {
                    requiredFeatureStage[table] = table == 0 ? info.GsubStage : info.GposStage;
                }

                if (featureIndexes[table].TryGetValue(info.Tag, out var index))
                {
                    featureIndex[table] = index;
                    found = true;
                }
                else
                {
                    featureIndex[table] = OpenTypeLayoutTable.NoIndex;
                }
            }

            if (!found && (info.Flags & FeatureFlags.GlobalSearch) != FeatureFlags.None)
            {
                for (var table = 0; table < 2; table++)
                {
                    var layout = table == 0 ? Gsub : Gpos;
                    if (layout?.FindFeature(info.Tag) is { } index and >= 0)
                    {
                        featureIndex[table] = index;
                        found = true;
                    }
                }
            }

            if (!found && (info.Flags & FeatureFlags.HasFallback) == FeatureFlags.None)
                continue;

            int shift;
            uint mask;
            if (usesGlobalBit)
            {
                shift = GlobalBitShift;
                mask = GlobalBitMask;
            }
            else
            {
                shift = nextBit;
                mask = (uint)(((1UL << (nextBit + bitsNeeded)) - 1) & ~((1UL << nextBit) - 1));
                nextBit += bitsNeeded;
                globalMask |= (info.DefaultValue << shift) & mask;
            }

            features.Add(new ShapePlanFeature(
                info.Tag,
                featureIndex[0],
                featureIndex[1],
                info.GsubStage,
                info.GposStage,
                shift,
                mask,
                (1u << shift) & mask,
                NeedsFallback: !found,
                AutoZwnj: (info.Flags & FeatureFlags.ManualZwnj) == FeatureFlags.None,
                AutoZwj: (info.Flags & FeatureFlags.ManualZwj) == FeatureFlags.None,
                Random: (info.Flags & FeatureFlags.Random) != FeatureFlags.None,
                PerSyllable: (info.Flags & FeatureFlags.PerSyllable) != FeatureFlags.None));
        }

        AddGsubPause(null);
        AddGposPause(null);

        // The lookups of the features, by stage
        var lookups = new ShapePlanLookup[2][];
        var stages = new ShapePlanStage[2][];
        var variations = (Span<int>)[_key.GsubVariations, _key.GposVariations];
        for (var table = 0; table < 2; table++)
        {
            var layout = table == 0 ? Gsub : Gpos;
            var tableLookups = new List<ShapePlanLookup>();
            var tableStages = new List<ShapePlanStage>();
            var stageIndex = 0;
            var lastLookupCount = 0;
            for (var stage = 0; stage < _currentStage[table]; stage++)
            {
                if (layout is not null)
                {
                    if (requiredFeatureIndex[table] != OpenTypeLayoutTable.NoIndex && requiredFeatureStage[table] == stage)
                    {
                        AddLookups(layout, tableLookups, requiredFeatureIndex[table], variations[table], GlobalBitMask, autoZwnj: true, autoZwj: true, random: false, perSyllable: false, featureTag: 0);
                    }

                    foreach (var feature in features)
                    {
                        if ((table == 0 ? feature.GsubStage : feature.GposStage) == stage)
                        {
                            AddLookups(layout, tableLookups, table == 0 ? feature.GsubIndex : feature.GposIndex, variations[table], feature.Mask, feature.AutoZwnj, feature.AutoZwj, feature.Random, feature.PerSyllable, feature.Tag);
                        }
                    }

                    // Sort the lookups of the stage and merge the duplicates
                    if (lastLookupCount + 1 < tableLookups.Count)
                    {
                        tableLookups.Sort(lastLookupCount, tableLookups.Count - lastLookupCount, Comparer<ShapePlanLookup>.Create(static (a, b) => a.Index.CompareTo(b.Index)));
                        var j = lastLookupCount;
                        for (var i = j + 1; i < tableLookups.Count; i++)
                        {
                            if (tableLookups[i].Index != tableLookups[j].Index)
                            {
                                tableLookups[++j] = tableLookups[i];
                            }
                            else
                            {
                                var current = tableLookups[j];
                                tableLookups[j] = current with
                                {
                                    Mask = current.Mask | tableLookups[i].Mask,
                                    AutoZwnj = current.AutoZwnj && tableLookups[i].AutoZwnj,
                                    AutoZwj = current.AutoZwj && tableLookups[i].AutoZwj,
                                };
                            }
                        }

                        tableLookups.RemoveRange(j + 1, tableLookups.Count - j - 1);
                    }
                }

                lastLookupCount = tableLookups.Count;
                if (stageIndex < _stages[table].Count && _stages[table][stageIndex].Index == stage)
                {
                    tableStages.Add(new ShapePlanStage(lastLookupCount, _stages[table][stageIndex].Pause));
                    stageIndex++;
                }
            }

            lookups[table] = [.. tableLookups];
            stages[table] = [.. tableStages];
        }

        var plan = new ShapePlan(this, [.. features], lookups, stages, globalMask);
        CompileShaping(plan);
        plan.ShaperData = Shaper.CreateData(plan);
        return plan;
    }

    private static void AddLookups(OpenTypeLayoutTable layout, List<ShapePlanLookup> lookups, int featureIndex, int variationsIndex, uint mask, bool autoZwnj, bool autoZwj, bool random, bool perSyllable, uint featureTag)
    {
        if (featureIndex == OpenTypeLayoutTable.NoIndex)
            return;

        foreach (var lookup in layout.GetFeatureLookups(featureIndex, variationsIndex))
        {
            if (lookup < layout.LookupCount)
            {
                lookups.Add(new ShapePlanLookup(lookup, mask, autoZwnj, autoZwj, random, perSyllable, featureTag));
            }
        }
    }

    /// <summary>The decisions of the substitution and of the positioning (HarfBuzz's <c>hb_ot_shape_planner_t::compile</c>).</summary>
    private void CompileShaping(ShapePlan plan)
    {
        plan.FracMask = plan.GetOneMask(Tag("frac"));
        plan.NumrMask = plan.GetOneMask(Tag("numr"));
        plan.DnomMask = plan.GetOneMask(Tag("dnom"));
        plan.HasFrac = plan.FracMask != 0 || (plan.NumrMask != 0 && plan.DnomMask != 0);
        plan.RtlmMask = plan.GetOneMask(OpenTypeTag.RightToLeftMirroredForms);
        plan.HasVert = plan.GetOneMask(VertTag) != 0;

        var kernTag = Direction != ShapingDirection.TopToBottom ? OpenTypeTag.Kern : OpenTypeTag.VerticalKerning;
        plan.KernMask = plan.GetMask(kernTag);
        plan.RequestedKerning = plan.KernMask != 0;
        var hasGposKern = plan.GetFeatureIndex(1, kernTag) != OpenTypeLayoutTable.NoIndex;
        var disableGpos = Shaper.GposTag != 0 && Shaper.GposTag != ChosenScript[1];

        // Who provides the glyph classes: GDEF or Unicode
        plan.FallbackGlyphClasses = Font.LayoutGdef is not { HasGlyphClasses: true };

        // Who does the positioning: GPOS, the kern table, or the fallback positioning
        var fallbackPosition = Shaper.FallbackPosition;
        plan.ApplyGpos = !disableGpos && Gpos is not null;
        if ((!hasGposKern || !plan.ApplyGpos) && Font.HasTable(OpenTypeTag.Kern))
        {
            plan.ApplyKern = fallbackPosition;
        }

        plan.ZeroMarks = Shaper.ZeroWidthMarks != ZeroWidthMarks.None;
        plan.HasGposMark = plan.GetOneMask(MarkTag) != 0;
        plan.AdjustMarkPositioningWhenZeroing = !plan.ApplyGpos && !plan.ApplyKern;
        plan.FallbackMarkPositioning = plan.AdjustMarkPositioningWhenZeroing && fallbackPosition;
    }

    /// <summary>The tag of 4 characters.</summary>
    public static uint Tag(string value) => ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];

    [StructLayout(LayoutKind.Auto)]
    private struct FeatureInfo
    {
        public uint Tag;
        public int Sequence;
        public uint MaxValue;
        public FeatureFlags Flags;
        public uint DefaultValue;
        public int GsubStage;
        public int GposStage;
    }
}
