using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Khmer script (HarfBuzz's hb-ot-shaper-khmer.cc, MIT license;
/// https://learn.microsoft.com/typography/script-development/khmer): the syllables (<see cref="KhmerSyllableMachine"/>), the
/// reordering of the Coeng,Ro sequences (<c>pref</c>) and of the left matras before the base, then the basic features
/// (<c>pref</c>, <c>blwf</c>, <c>abvf</c>, <c>pstf</c>, <c>cfar</c>) applied together in the syllable, and the presentation
/// features (<c>pres</c>, <c>abvs</c>, <c>blws</c>, <c>psts</c>, then <c>clig</c>). The split vowels are decomposed.
/// </summary>
internal sealed class KhmerShaper : OpenTypeShaper
{
    public static KhmerShaper Instance { get; } = new();

    private const int Pref = 0;
    private const int Blwf = 1;
    private const int Abvf = 2;
    private const int Pstf = 3;
    private const int Cfar = 4;
    private const int BasicFeatureCount = 5;

    private static readonly (uint Tag, FeatureFlags Flags)[] Features =
    [
        // The basic features, applied all at once before the reordering, in the syllable
        (ShapePlanBuilder.Tag("pref"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("blwf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("abvf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("pstf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("cfar"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),

        // The other features, applied all at once after the syllables are cleared
        (ShapePlanBuilder.Tag("pres"), FeatureFlags.GlobalManualJoiners),
        (ShapePlanBuilder.Tag("abvs"), FeatureFlags.GlobalManualJoiners),
        (ShapePlanBuilder.Tag("blws"), FeatureFlags.GlobalManualJoiners),
        (ShapePlanBuilder.Tag("psts"), FeatureFlags.GlobalManualJoiners),
    ];

    private KhmerShaper()
    {
    }

    public override NormalizationMode NormalizationPreference => NormalizationMode.ComposedDiacriticsNoShortCircuit;

    public override ZeroWidthMarks ZeroWidthMarks => ZeroWidthMarks.None;

    public override bool FallbackPosition => false;

    public override void CollectFeatures(ShapePlanBuilder builder)
    {
        // Before any lookup is applied
        builder.AddGsubPause(SetupSyllables);
        builder.AddGsubPause(Reorder);

        // Uniscribe does not pause between the basic features (https://github.com/harfbuzz/harfbuzz/issues/974)
        builder.EnableFeature(ShapePlanBuilder.Tag("locl"), FeatureFlags.PerSyllable);
        builder.EnableFeature(ShapePlanBuilder.Tag("ccmp"), FeatureFlags.PerSyllable);
        var i = 0;
        for (; i < BasicFeatureCount; i++)
        {
            builder.AddFeature(Features[i].Tag, Features[i].Flags);
        }

        // https://github.com/harfbuzz/harfbuzz/issues/3531
        builder.AddGsubPause(SyllabicShaping.ClearSyllables);
        for (; i < Features.Length; i++)
        {
            builder.AddFeature(Features[i].Tag, Features[i].Flags);
        }
    }

    public override void OverrideFeatures(ShapePlanBuilder builder)
    {
        // The Khmer specification has clig in the required features ("ligatures that are desired for typographical correctness")
        builder.EnableFeature(ShapePlanBuilder.Tag("clig"));
        builder.DisableFeature(ShapePlanBuilder.Tag("liga"));
    }

    public override object? CreateData(ShapePlan plan)
    {
        var masks = new uint[Features.Length];
        for (var i = 0; i < Features.Length; i++)
        {
            masks[i] = (Features[i].Flags & FeatureFlags.Global) != FeatureFlags.None ? 0 : plan.GetOneMask(Features[i].Tag);
        }

        return masks;
    }

    public override bool Decompose(ShapePlan plan, in ShapingFont font, int ab, out int a, out int b)
    {
        // The split matras that have no Unicode decomposition
        if (ab is 0x17BE or 0x17BF or 0x17C0 or 0x17C4 or 0x17C5)
        {
            a = 0x17C1;
            b = ab;
            return true;
        }

        return ShapingNormalizer.DecomposeUnicode(ab, out a, out b);
    }

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
        // The masks are set later, in a pause: the categories of the characters are saved
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].ShaperCategory = (byte)IndicCategoryData.Values[IndicCategoryData.Get(info[i].Codepoint)];
        }
    }

    private static bool SetupSyllables(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        SyllabicShaping.FindSyllables(buffer, KhmerSyllableMachine.Classes, KhmerSyllableMachine.ClassCount, KhmerSyllableMachine.Transitions, KhmerSyllableMachine.Accepts, (int)KhmerSyllableMachine.BrokenSyllable);
        SyllabicShaping.SetUnsafeToBreak(buffer);
        return false;
    }

    private static bool Reorder(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var changed = SyllabicShaping.InsertDottedCircles(font, buffer, (int)KhmerSyllableType.BrokenCluster, IndicCategory.DottedCircle);
        var masks = (uint[])plan.ShaperData!;
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);

            // The dotted circles are inserted in the broken clusters: they are consonant syllables
            if ((KhmerSyllableType)SyllabicShaping.GetSyllableType(buffer.Info[start]) is KhmerSyllableType.BrokenCluster or KhmerSyllableType.ConsonantSyllable)
            {
                ReorderConsonantSyllable(masks, buffer, start, end);
            }
        }

        return changed;
    }

    /// <summary>The reordering of a syllable (HarfBuzz's <c>reorder_consonant_syllable</c> of the Khmer shaper).</summary>
    private static void ReorderConsonantSyllable(uint[] masks, ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;

        // The post-base masks
        var mask = masks[Blwf] | masks[Abvf] | masks[Pstf];
        for (var i = start + 1; i < end; i++)
        {
            info[i].Mask |= mask;
        }

        var coengs = 0;
        for (var i = start + 1; i < end; i++)
        {
            // When a COENG + (Cons | IndV) combination is found (and the subscript count is less than two), it is handled
            // according to the subscript type of the character following the COENG: the COENG + RO characters (subscript type
            // 2) are reordered immediately before the base glyph, and get the pref feature
            if (info[i].ShaperCategory == IndicCategory.H && coengs <= 2 && i + 1 < end)
            {
                coengs++;
                if (info[i + 1].ShaperCategory == IndicCategory.Ra)
                {
                    info[i].Mask |= masks[Pref];
                    info[i + 1].Mask |= masks[Pref];

                    // Move the Coeng,Ro sequence to the start
                    buffer.MergeClusters(start, i + 2);
                    var coeng = info[i];
                    var ro = info[i + 1];
                    Array.Copy(info, start, info, start + 2, i - start);
                    info[start] = coeng;
                    info[start + 1] = ro;

                    // The following glyphs get cfar (it distinguishes U+1784,U+17D2,U+179A,U+17D2,U+1782 from
                    // U+1784,U+17D2,U+1782,U+17D2,U+179A with the Khmer fonts of Microsoft)
                    if (masks[Cfar] != 0)
                    {
                        for (var j = i + 2; j < end; j++)
                        {
                            info[j].Mask |= masks[Cfar];
                        }
                    }

                    coengs = 2;
                }
            }
            else if (info[i].ShaperCategory == IndicCategory.VPre)
            {
                // Move the left matra to the start
                buffer.MergeClusters(start, i + 1);
                var matra = info[i];
                Array.Copy(info, start, info, start + 1, i - start);
                info[start] = matra;
            }
        }
    }
}
