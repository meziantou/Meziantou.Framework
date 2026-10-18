using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The Universal Shaping Engine (HarfBuzz's hb-ot-shaper-use.cc, MIT license;
/// https://learn.microsoft.com/typography/script-development/use): the shaper of the many scripts of the USE (Tibetan,
/// Sinhala, Balinese, Javanese, Tai Tham, Cham, Batak, Brahmi, Chakma, Grantha, Newa..., the joining scripts Mongolian, N'Ko,
/// Adlam, Phags-pa, Manichaean..., and the Indic scripts with the <c>dev3</c>-style script tags). The USE categories of the
/// characters (<see cref="UseCategory"/>, from the Unicode Character Database and the additional categories of Microsoft)
/// split the text into clusters (<see cref="UseSyllableMachine"/>); the features are applied in groups: the preprocessing
/// (<c>locl</c>, <c>ccmp</c>, <c>nukt</c>, <c>akhn</c>), the reordering features (<c>rphf</c>, <c>pref</c>, whose results
/// are recorded), the orthographic features (<c>rkrf</c>, <c>abvf</c>, <c>blwf</c>, <c>half</c>, <c>pstf</c>, <c>vatu</c>,
/// <c>cjct</c>), then the glyphs are reordered (the repha after the base, the pre-base vowels and modifiers before it), the
/// topographical features (<c>isol</c>, <c>init</c>, <c>medi</c>, <c>fina</c>: the cursive joining of the scripts of
/// ArabicShaping.txt, else of the clusters), and the presentation features (<c>abvs</c>, <c>blws</c>, <c>haln</c>,
/// <c>pres</c>, <c>psts</c>).
/// </summary>
internal sealed class UseShaper : OpenTypeShaper
{
    public static UseShaper Instance { get; } = new();

    private static readonly uint[] BasicFeatures =
    [
        ShapePlanBuilder.Tag("rkrf"),
        ShapePlanBuilder.Tag("abvf"),
        ShapePlanBuilder.Tag("blwf"),
        ShapePlanBuilder.Tag("half"),
        ShapePlanBuilder.Tag("pstf"),
        ShapePlanBuilder.Tag("vatu"),
        ShapePlanBuilder.Tag("cjct"),
    ];

    // The topographical features (the order of the joining forms of the clusters)
    private const int Isol = 0;
    private const int Init = 1;
    private const int Medi = 2;
    private const int Fina = 3;
    private const int NoForm = 4;

    private static readonly uint[] TopographicalFeatures = [ShapePlanBuilder.Tag("isol"), ShapePlanBuilder.Tag("init"), ShapePlanBuilder.Tag("medi"), ShapePlanBuilder.Tag("fina")];

    private static readonly uint[] OtherFeatures =
    [
        ShapePlanBuilder.Tag("abvs"),
        ShapePlanBuilder.Tag("blws"),
        ShapePlanBuilder.Tag("haln"),
        ShapePlanBuilder.Tag("pres"),
        ShapePlanBuilder.Tag("psts"),
    ];

    private static readonly uint RphfTag = ShapePlanBuilder.Tag("rphf");

    // The categories of the glyphs after the base
    private const ulong PostBaseFlags = (1UL << UseCategory.FAbv) | (1UL << UseCategory.FBlw) | (1UL << UseCategory.FPst) | (1UL << UseCategory.FMAbv) | (1UL << UseCategory.FMBlw)
        | (1UL << UseCategory.FMPst) | (1UL << UseCategory.MAbv) | (1UL << UseCategory.MBlw) | (1UL << UseCategory.MPst) | (1UL << UseCategory.MPre) | (1UL << UseCategory.VAbv)
        | (1UL << UseCategory.VBlw) | (1UL << UseCategory.VPst) | (1UL << UseCategory.VPre) | (1UL << UseCategory.VMAbv) | (1UL << UseCategory.VMBlw) | (1UL << UseCategory.VMPst)
        | (1UL << UseCategory.VMPre);

    private UseShaper()
    {
    }

    public override NormalizationMode NormalizationPreference => NormalizationMode.ComposedDiacriticsNoShortCircuit;

    public override ZeroWidthMarks ZeroWidthMarks => ZeroWidthMarks.ByGdefEarly;

    public override bool FallbackPosition => false;

    public override void CollectFeatures(ShapePlanBuilder builder)
    {
        // Before any lookup is applied
        builder.AddGsubPause(SetupSyllables);

        // The default glyph pre-processing group
        builder.EnableFeature(ShapePlanBuilder.Tag("locl"), FeatureFlags.PerSyllable);
        builder.EnableFeature(ShapePlanBuilder.Tag("ccmp"), FeatureFlags.PerSyllable);
        builder.EnableFeature(ShapePlanBuilder.Tag("nukt"), FeatureFlags.PerSyllable);
        builder.EnableFeature(ShapePlanBuilder.Tag("akhn"), FeatureFlags.ManualZwj | FeatureFlags.PerSyllable);

        // The reordering group
        builder.AddGsubPause(SyllabicShaping.ClearSubstitutionFlags);
        builder.AddFeature(RphfTag, FeatureFlags.ManualZwj | FeatureFlags.PerSyllable);
        builder.AddGsubPause(RecordRphf);
        builder.AddGsubPause(SyllabicShaping.ClearSubstitutionFlags);
        builder.EnableFeature(ShapePlanBuilder.Tag("pref"), FeatureFlags.ManualZwj | FeatureFlags.PerSyllable);
        builder.AddGsubPause(RecordPref);

        // The orthographic unit shaping group
        foreach (var feature in BasicFeatures)
        {
            builder.EnableFeature(feature, FeatureFlags.ManualZwj | FeatureFlags.PerSyllable);
        }

        builder.AddGsubPause(Reorder);
        builder.AddGsubPause(SyllabicShaping.ClearSyllables);

        // The topographical features
        foreach (var feature in TopographicalFeatures)
        {
            builder.AddFeature(feature);
        }

        builder.AddGsubPause(null);

        // The standard typographic presentation
        foreach (var feature in OtherFeatures)
        {
            builder.EnableFeature(feature, FeatureFlags.ManualZwj);
        }
    }

    public override object? CreateData(ShapePlan plan)
    {
        var joining = ArabicJoiningScripts.HasJoining(plan.Script) ? ArabicShaper.CreateJoiningData(plan) : null;
        var topographicalMasks = new uint[TopographicalFeatures.Length];
        for (var i = 0; i < topographicalMasks.Length; i++)
        {
            // A feature of the global mask applies to all the glyphs (no mask to set)
            topographicalMasks[i] = plan.GetOneMask(TopographicalFeatures[i]);
            if (topographicalMasks[i] == plan.GlobalMask)
            {
                topographicalMasks[i] = 0;
            }
        }

        return new UsePlanData(plan.GetOneMask(RphfTag), joining, topographicalMasks);
    }

    public override void PreprocessText(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font) => SyllabicShaping.PreprocessVowelConstraints(buffer);

    public override bool Compose(ShapePlan plan, in ShapingFont font, int a, int b, out int ab)
    {
        // The split matras are not recomposed
        if (IndicShaper.IsMark(UnicodeProperties.GetGeneralCategory(a)))
        {
            ab = 0;
            return false;
        }

        return ShapingNormalizer.ComposeUnicode(a, b, out ab);
    }

    public override void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        // The joining of the cursive scripts (before the categories are set: the joining uses the auxiliary field)
        var data = (UsePlanData)plan.ShaperData!;
        if (data.Joining is { } joining)
        {
            ArabicShaper.SetupJoiningMasks(joining, buffer, plan.Script);
        }

        // The masks are set later, in a pause: the categories of the characters are saved
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].ShaperCategory = UseCategoryData.Get(info[i].Codepoint);
        }
    }

    private static bool SetupSyllables(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        SyllabicShaping.FindUseSyllables(buffer);
        SyllabicShaping.SetUnsafeToBreak(buffer);
        var data = (UsePlanData)plan.ShaperData!;
        SetupRphfMask(data, buffer);
        SetupTopographicalMasks(data, buffer);
        return false;
    }

    /// <summary>The repha is the first glyph of a cluster that starts with R, else up to the first 3 glyphs.</summary>
    private static void SetupRphfMask(UsePlanData data, ShapingBuffer buffer)
    {
        var mask = data.RphfMask;
        if (mask == 0)
            return;

        var info = buffer.Info;
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            var limit = info[start].ShaperCategory == UseCategory.R ? 1 : Math.Min(3, end - start);
            for (var i = start; i < start + limit; i++)
            {
                info[i].Mask |= mask;
            }
        }
    }

    /// <summary>The joining forms of the clusters of the scripts that have no cursive joining (HarfBuzz's <c>setup_topographical_masks</c>).</summary>
    private static void SetupTopographicalMasks(UsePlanData data, ShapingBuffer buffer)
    {
        if (data.Joining is not null)
            return;

        var masks = data.TopographicalMasks;
        var allMasks = masks[Isol] | masks[Init] | masks[Medi] | masks[Fina];
        if (allMasks == 0)
            return;

        var otherMasks = ~allMasks;
        var lastStart = 0;
        var lastForm = NoForm;
        var info = buffer.Info;
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            switch ((UseSyllableType)SyllabicShaping.GetSyllableType(info[start]))
            {
                case UseSyllableType.HieroglyphCluster or UseSyllableType.NonCluster:
                    // These do not join
                    lastForm = NoForm;
                    break;

                default:
                {
                    var join = lastForm is Fina or Isol;
                    if (join)
                    {
                        // Fix up the form of the previous cluster
                        lastForm = lastForm == Fina ? Medi : Init;
                        for (var i = lastStart; i < start; i++)
                        {
                            info[i].Mask = (info[i].Mask & otherMasks) | masks[lastForm];
                        }
                    }

                    // The form of this cluster
                    lastForm = join ? Fina : Isol;
                    for (var i = start; i < end; i++)
                    {
                        info[i].Mask = (info[i].Mask & otherMasks) | masks[lastForm];
                    }

                    break;
                }
            }

            lastStart = start;
        }
    }

    /// <summary>A substituted repha is a repha (R).</summary>
    private static bool RecordRphf(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var mask = ((UsePlanData)plan.ShaperData!).RphfMask;
        if (mask == 0)
            return false;

        var info = buffer.Info;
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            for (var i = start; i < end && (info[i].Mask & mask) != 0; i++)
            {
                if (info[i].IsSubstituted)
                {
                    info[i].ShaperCategory = UseCategory.R;
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>A substituted pref glyph is a pre-base vowel (VPre): they behave the same way.</summary>
    private static bool RecordPref(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var info = buffer.Info;
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            for (var i = start; i < end; i++)
            {
                if (info[i].IsSubstituted)
                {
                    info[i].ShaperCategory = UseCategory.VPre;
                    break;
                }
            }
        }

        return false;
    }

    private static bool IsHalant(in GlyphInfo info) => (info.ShaperCategory is UseCategory.H or UseCategory.Hvm or UseCategory.IS) && !info.IsLigated;

    private static bool Reorder(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var changed = SyllabicShaping.InsertDottedCircles(font, buffer, (int)UseSyllableType.BrokenCluster, UseCategory.B, UseCategory.R);
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            ReorderSyllable(buffer, start, end);
        }

        return changed;
    }

    /// <summary>The reordering of a cluster (HarfBuzz's <c>reorder_syllable_use</c>).</summary>
    private static void ReorderSyllable(ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;

        // Only a few types of clusters are reordered
        if ((UseSyllableType)SyllabicShaping.GetSyllableType(info[start]) is not (UseSyllableType.ViramaTerminatedCluster or UseSyllableType.SakotTerminatedCluster or UseSyllableType.StandardCluster or UseSyllableType.SymbolCluster or UseSyllableType.BrokenCluster))
            return;

        // Move a repha forward: towards the end, but before the first post-base glyph
        if (info[start].ShaperCategory == UseCategory.R && end - start > 1)
        {
            for (var i = start + 1; i < end; i++)
            {
                var isPostBase = (Flag64(info[i].ShaperCategory) & PostBaseFlags) != 0 || IsHalant(info[i]);
                if (isPostBase || i == end - 1)
                {
                    // Before the post-base glyph, else at the end; the glyphs in between move backward
                    if (isPostBase)
                    {
                        i--;
                    }

                    buffer.MergeClusters(start, i + 1);
                    var repha = info[start];
                    Array.Copy(info, start + 1, info, start, i - start);
                    info[i] = repha;
                    break;
                }
            }
        }

        // Move the pre-base glyphs backward: after the last halant, else to the start
        var j = start;
        for (var i = start; i < end; i++)
        {
            var category = info[i].ShaperCategory;
            if (IsHalant(info[i]))
            {
                j = i + 1;
            }
            else if ((category is UseCategory.VPre or UseCategory.VMPre) && info[i].LigatureComponent == 0 && j < i)
            {
                // Only the first component of a multiple substitution moves
                buffer.MergeClusters(j, i + 1);
                var glyph = info[i];
                Array.Copy(info, j, info, j + 1, i - j);
                info[j] = glyph;
            }
        }
    }

    private static ulong Flag64(int value) => value < 64 ? 1UL << value : 0;

    /// <summary>The data of the Universal Shaping Engine for a plan (HarfBuzz's <c>use_shape_plan_t</c>).</summary>
    private sealed record UsePlanData(uint RphfMask, ArabicJoiningMasks? Joining, uint[] TopographicalMasks);
}
