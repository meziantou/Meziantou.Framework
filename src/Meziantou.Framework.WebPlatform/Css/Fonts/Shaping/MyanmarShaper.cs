using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Myanmar script with the <c>mym2</c> script tag of the fonts (HarfBuzz's hb-ot-shaper-myanmar.cc, MIT
/// license; https://learn.microsoft.com/typography/script-development/myanmar): the syllables
/// (<see cref="MyanmarSyllableMachine"/>), the reordering of the kinzi, the medial Ra and the left matras, then the basic
/// features applied one by one in the syllable (<c>rphf</c>, <c>pref</c>, <c>blwf</c>, <c>pstf</c>) and the presentation
/// features (<c>pres</c>, <c>abvs</c>, <c>blws</c>, <c>psts</c>). The fonts with the old <c>mymr</c> tag use the default
/// shaper (as HarfBuzz).
/// </summary>
internal sealed class MyanmarShaper : OpenTypeShaper
{
    public static MyanmarShaper Instance { get; } = new();

    private static readonly uint[] BasicFeatures = [ShapePlanBuilder.Tag("rphf"), ShapePlanBuilder.Tag("pref"), ShapePlanBuilder.Tag("blwf"), ShapePlanBuilder.Tag("pstf")];

    private static readonly uint[] OtherFeatures = [ShapePlanBuilder.Tag("pres"), ShapePlanBuilder.Tag("abvs"), ShapePlanBuilder.Tag("blws"), ShapePlanBuilder.Tag("psts")];

    // The consonants (the medials are not, the vowels and the placeholders are)
    private const uint ConsonantFlags = (1u << IndicCategory.C) | (1u << IndicCategory.CS) | (1u << IndicCategory.Ra) | (1u << IndicCategory.IV) | (1u << IndicCategory.GB) | (1u << IndicCategory.DottedCircle);

    private MyanmarShaper()
    {
    }

    public override NormalizationMode NormalizationPreference => NormalizationMode.ComposedDiacriticsNoShortCircuit;

    public override ZeroWidthMarks ZeroWidthMarks => ZeroWidthMarks.ByGdefEarly;

    public override bool FallbackPosition => false;

    public override void CollectFeatures(ShapePlanBuilder builder)
    {
        // Before any lookup is applied
        builder.AddGsubPause(SetupSyllables);

        // The specification does not require ccmp, but it is applied here since its uses are at the beginning
        builder.EnableFeature(ShapePlanBuilder.Tag("locl"), FeatureFlags.PerSyllable);
        builder.EnableFeature(ShapePlanBuilder.Tag("ccmp"), FeatureFlags.PerSyllable);
        builder.AddGsubPause(Reorder);
        foreach (var feature in BasicFeatures)
        {
            builder.EnableFeature(feature, FeatureFlags.ManualZwj | FeatureFlags.PerSyllable);
            builder.AddGsubPause(null);
        }

        builder.AddGsubPause(SyllabicShaping.ClearSyllables);
        foreach (var feature in OtherFeatures)
        {
            builder.EnableFeature(feature, FeatureFlags.ManualZwj);
        }
    }

    public override void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        // No masks: the categories of the characters are saved
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].ShaperCategory = (byte)IndicCategoryData.Values[IndicCategoryData.Get(info[i].Codepoint)];
        }
    }

    private static uint Flag(int value) => value < 32 ? 1u << value : 0;

    private static bool IsConsonant(in GlyphInfo info) => !info.IsLigated && (Flag(info.ShaperCategory) & ConsonantFlags) != 0;

    private static IndicPosition GetPosition(in GlyphInfo info) => (IndicPosition)info.ShaperAuxiliary;

    private static void SetPosition(ref GlyphInfo info, IndicPosition position) => info.ShaperAuxiliary = (byte)position;

    private static bool SetupSyllables(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        SyllabicShaping.FindSyllables(buffer, MyanmarSyllableMachine.Classes, MyanmarSyllableMachine.ClassCount, MyanmarSyllableMachine.Transitions, MyanmarSyllableMachine.Accepts, (int)MyanmarSyllableMachine.BrokenSyllable);
        SyllabicShaping.SetUnsafeToBreak(buffer);
        return false;
    }

    private static bool Reorder(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var changed = SyllabicShaping.InsertDottedCircles(font, buffer, (int)MyanmarSyllableType.BrokenCluster, IndicCategory.DottedCircle);
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);

            // The dotted circles are inserted in the broken clusters: they are consonant syllables
            if ((MyanmarSyllableType)SyllabicShaping.GetSyllableType(buffer.Info[start]) is MyanmarSyllableType.BrokenCluster or MyanmarSyllableType.ConsonantSyllable)
            {
                InitialReorderingConsonantSyllable(buffer, start, end);
            }
        }

        return changed;
    }

    /// <summary>The reordering of a syllable (HarfBuzz's <c>initial_reordering_consonant_syllable</c> of the Myanmar shaper).</summary>
    private static void InitialReorderingConsonantSyllable(ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;

        // The kinzi (Ra,Asat,Halant) and the base
        var baseIndex = end;
        var hasReph = false;
        var limit = start;
        if (start + 3 <= end && info[start].ShaperCategory == IndicCategory.Ra && info[start + 1].ShaperCategory == IndicCategory.As && info[start + 2].ShaperCategory == IndicCategory.H)
        {
            limit += 3;
            baseIndex = start;
            hasReph = true;
        }

        if (!hasReph)
        {
            baseIndex = limit;
        }

        for (var i = limit; i < end; i++)
        {
            if (IsConsonant(info[i]))
            {
                baseIndex = i;
                break;
            }
        }

        // The positions
        var index = start;
        for (; index < start + (hasReph ? 3 : 0); index++)
        {
            SetPosition(ref info[index], IndicPosition.AfterMain);
        }

        for (; index < baseIndex; index++)
        {
            SetPosition(ref info[index], IndicPosition.PreC);
        }

        if (index < end)
        {
            SetPosition(ref info[index], IndicPosition.BaseC);
            index++;
        }

        var position = IndicPosition.AfterMain;
        for (; index < end; index++)
        {
            var category = info[index].ShaperCategory;
            if (category == IndicCategory.MR)
            {
                // The pre-base reordering medial Ra
                SetPosition(ref info[index], IndicPosition.PreC);
                continue;
            }

            if (category == IndicCategory.VPre)
            {
                // The left matra
                SetPosition(ref info[index], IndicPosition.PreM);
                continue;
            }

            if (category == IndicCategory.VS)
            {
                SetPosition(ref info[index], GetPosition(info[index - 1]));
                continue;
            }

            if (position == IndicPosition.AfterMain && category == IndicCategory.VBlw)
            {
                position = IndicPosition.BelowC;
                SetPosition(ref info[index], position);
                continue;
            }

            if (position == IndicPosition.BelowC && category == IndicCategory.A)
            {
                SetPosition(ref info[index], IndicPosition.BeforeSub);
                continue;
            }

            if (position == IndicPosition.BelowC && category == IndicCategory.VBlw)
            {
                SetPosition(ref info[index], position);
                continue;
            }

            if (position == IndicPosition.BelowC && category != IndicCategory.A)
            {
                position = IndicPosition.AfterSub;
                SetPosition(ref info[index], position);
                continue;
            }

            SetPosition(ref info[index], position);
        }

        buffer.Sort(start, end, static (a, b) => a.ShaperAuxiliary - b.ShaperAuxiliary);

        // Flip the sequence of left matras (https://github.com/harfbuzz/harfbuzz/issues/3863)
        var firstLeftMatra = end;
        var lastLeftMatra = end;
        for (var i = start; i < end; i++)
        {
            if (GetPosition(info[i]) == IndicPosition.PreM)
            {
                if (firstLeftMatra == end)
                {
                    firstLeftMatra = i;
                }

                lastLeftMatra = i;
            }
        }

        if (firstLeftMatra < lastLeftMatra)
        {
            buffer.Reverse(firstLeftMatra, lastLeftMatra + 1);

            // Reverse back the variation selectors...
            var k = firstLeftMatra;
            for (var j = k; j <= lastLeftMatra; j++)
            {
                if (info[j].ShaperCategory == IndicCategory.VPre)
                {
                    buffer.Reverse(k, j + 1);
                    k = j + 1;
                }
            }
        }
    }
}
