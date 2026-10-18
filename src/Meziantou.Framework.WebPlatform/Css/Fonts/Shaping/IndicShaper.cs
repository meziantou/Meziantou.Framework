using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Indic scripts (HarfBuzz's hb-ot-shaper-indic.cc, MIT license;
/// https://learn.microsoft.com/typography/script-development/devanagari and the specifications of the other Indic scripts):
/// Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada and Malayalam, with the old (<c>deva</c>) and the
/// new (<c>dev2</c>) script tags of the fonts (the fonts with the <c>dev3</c> tags use the Universal Shaping Engine).
/// The text is split into syllables (<see cref="IndicSyllableMachine"/>), the characters of each syllable are reordered
/// (the base consonant, the reph, the pre-base matras), the basic features are applied one by one in the syllable (<c>nukt</c>,
/// <c>akhn</c>, <c>rphf</c>, <c>rkrf</c>, <c>pref</c>, <c>blwf</c>, <c>abvf</c>, <c>half</c>, <c>pstf</c>, <c>vatu</c>,
/// <c>cjct</c>), the glyphs are reordered again from the forms the font produced (the final reordering), then the
/// presentation features are applied (<c>init</c>, <c>pres</c>, <c>abvs</c>, <c>blws</c>, <c>psts</c>, <c>haln</c>).
/// The categories and the positions of the characters (<see cref="IndicCategory"/>, <see cref="IndicPosition"/>) are in
/// <see cref="GlyphInfo.ShaperCategory"/> and <see cref="GlyphInfo.ShaperAuxiliary"/>.
/// </summary>
internal sealed class IndicShaper : OpenTypeShaper
{
    public static IndicShaper Instance { get; } = new();

    // The features, in the order they are applied (indexes of the masks of the plan): nukt, akhn, rphf, rkrf, pref, blwf,
    // abvf, half, pstf, vatu, cjct, then init, pres, abvs, blws, psts, haln
    private const int Rphf = 2;
    private const int Pref = 4;
    private const int Blwf = 5;
    private const int Abvf = 6;
    private const int Half = 7;
    private const int Pstf = 8;
    private const int Init = 11;
    private const int BasicFeatureCount = Init;

    private static readonly (uint Tag, FeatureFlags Flags)[] Features =
    [
        // The basic features, applied one by one after the initial reordering, in the syllable
        (ShapePlanBuilder.Tag("nukt"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("akhn"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("rphf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("rkrf"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("pref"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("blwf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("abvf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("half"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("pstf"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("vatu"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("cjct"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),

        // The other features, applied all at once after the final reordering, in the syllable (the default Bengali font of
        // Windows has intermixed lookups for init, pres, abvs and blws)
        (ShapePlanBuilder.Tag("init"), FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("pres"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("abvs"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("blws"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("psts"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        (ShapePlanBuilder.Tag("haln"), FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
    ];

    private static readonly uint RphfTag = ShapePlanBuilder.Tag("rphf");
    private static readonly uint PrefTag = ShapePlanBuilder.Tag("pref");
    private static readonly uint BlwfTag = ShapePlanBuilder.Tag("blwf");
    private static readonly uint PstfTag = ShapePlanBuilder.Tag("pstf");
    private static readonly uint VatuTag = ShapePlanBuilder.Tag("vatu");

    // The categories of the consonants: the vowels and the placeholders are consonants too (they never are in a consonant
    // syllable, so the vowel syllables can use the logic of the consonant syllables)
    private const uint ConsonantFlags = (1u << IndicCategory.C) | (1u << IndicCategory.CS) | (1u << IndicCategory.Ra) | (1u << IndicCategory.CM) | (1u << IndicCategory.V) | (1u << IndicCategory.Placeholder) | (1u << IndicCategory.DottedCircle);
    private const uint JoinerFlags = (1u << IndicCategory.Zwj) | (1u << IndicCategory.Zwnj);
    private const uint MatraFlags = (1u << IndicCategory.M) | (1u << IndicCategory.MPst);

    /// <summary>The configurations of the scripts (HarfBuzz's <c>indic_configs</c>); the first one is the default.</summary>
    private static readonly IndicConfig[] Configs =
    [
        new(UnicodeScript.Unknown, HasOldSpec: false, Virama: 0, IndicPosition.BeforePost, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Devanagari, HasOldSpec: true, Virama: 0x094D, IndicPosition.BeforePost, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Bengali, HasOldSpec: true, Virama: 0x09CD, IndicPosition.AfterSub, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Gurmukhi, HasOldSpec: true, Virama: 0x0A4D, IndicPosition.BeforeSub, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Gujarati, HasOldSpec: true, Virama: 0x0ACD, IndicPosition.BeforePost, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Oriya, HasOldSpec: true, Virama: 0x0B4D, IndicPosition.AfterMain, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Tamil, HasOldSpec: true, Virama: 0x0BCD, IndicPosition.AfterPost, RephMode.Implicit, BlwfMode.PreAndPost),
        new(UnicodeScript.Telugu, HasOldSpec: true, Virama: 0x0C4D, IndicPosition.AfterPost, RephMode.Explicit, BlwfMode.PostOnly),
        new(UnicodeScript.Kannada, HasOldSpec: true, Virama: 0x0CCD, IndicPosition.AfterPost, RephMode.Implicit, BlwfMode.PostOnly),
        new(UnicodeScript.Malayalam, HasOldSpec: true, Virama: 0x0D4D, IndicPosition.AfterMain, RephMode.LogicalRepha, BlwfMode.PreAndPost),
    ];

    private IndicShaper()
    {
    }

    public override NormalizationMode NormalizationPreference => NormalizationMode.ComposedDiacriticsNoShortCircuit;

    public override ZeroWidthMarks ZeroWidthMarks => ZeroWidthMarks.None;

    public override bool FallbackPosition => false;

    public override void CollectFeatures(ShapePlanBuilder builder)
    {
        // Before any lookup is applied
        builder.AddGsubPause(SetupSyllables);

        // The Indic specifications do not require ccmp, but it is applied here since its uses are at the beginning
        builder.EnableFeature(ShapePlanBuilder.Tag("locl"), FeatureFlags.PerSyllable);
        builder.EnableFeature(ShapePlanBuilder.Tag("ccmp"), FeatureFlags.PerSyllable);
        builder.AddGsubPause(InitialReordering);

        var i = 0;
        for (; i < BasicFeatureCount; i++)
        {
            builder.AddFeature(Features[i].Tag, Features[i].Flags);
            builder.AddGsubPause(null);
        }

        builder.AddGsubPause(FinalReordering);
        for (; i < Features.Length; i++)
        {
            builder.AddFeature(Features[i].Tag, Features[i].Flags);
        }
    }

    public override void OverrideFeatures(ShapePlanBuilder builder)
    {
        builder.DisableFeature(ShapePlanBuilder.Tag("liga"));
        builder.AddGsubPause(SyllabicShaping.ClearSyllables);
    }

    public override object? CreateData(ShapePlan plan)
    {
        var config = Configs[0];
        foreach (var candidate in Configs.AsSpan(1))
        {
            if (candidate.Script == plan.Script)
            {
                config = candidate;
                break;
            }
        }

        // The old specification (deva, beng...): the new tags end with '2'
        var isOldSpec = config.HasOldSpec && (plan.ChosenScript[0] & 0xFF) != '2';

        // The new specification of the scripts that have two specifications says that the forms of the consonants are found
        // without context, but testing shows that Malayalam (old and new) uses the context (HarfBuzz's heuristic)
        var zeroContext = !isOldSpec && plan.Script != UnicodeScript.Malayalam;
        var masks = new uint[Features.Length];
        for (var i = 0; i < Features.Length; i++)
        {
            masks[i] = (Features[i].Flags & FeatureFlags.Global) != FeatureFlags.None ? 0 : plan.GetOneMask(Features[i].Tag);
        }

        return new IndicPlanData(config, isOldSpec, zeroContext, masks);
    }

    public override void PreprocessText(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font) => SyllabicShaping.PreprocessVowelConstraints(buffer);

    public override bool Decompose(ShapePlan plan, in ShapingFont font, int ab, out int a, out int b)
    {
        // These characters are not decomposed (DEVANAGARI LETTER RRA, BENGALI LETTER RRA and RHA, TAMIL LETTER AU)
        if (ab is 0x0931 or 0x09DC or 0x09DD or 0x0B94)
        {
            a = ab;
            b = 0;
            return false;
        }

        return ShapingNormalizer.DecomposeUnicode(ab, out a, out b);
    }

    public override bool Compose(ShapePlan plan, in ShapingFont font, int a, int b, out int ab)
    {
        // The split matras are not recomposed
        if (IsMark(UnicodeProperties.GetGeneralCategory(a)))
        {
            ab = 0;
            return false;
        }

        // A composition exclusion that is recomposed
        if (a == 0x09AF && b == 0x09BC)
        {
            ab = 0x09DF;
            return true;
        }

        return ShapingNormalizer.ComposeUnicode(a, b, out ab);
    }

    public override void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        // The masks are set later, in a pause: the categories of the characters are saved
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            SetProperties(ref info[i]);
        }
    }

    /// <summary>Sets the Indic category and position of a character (HarfBuzz's <c>set_indic_properties</c>).</summary>
    public static void SetProperties(ref GlyphInfo info)
    {
        var value = IndicCategoryData.Values[IndicCategoryData.Get(info.Codepoint)];
        info.ShaperCategory = (byte)value;
        info.ShaperAuxiliary = (byte)(value >> 8);
    }

    internal static bool IsMark(UnicodeCategory category) => category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static uint Flag(int value) => value < 32 ? 1u << value : 0;

    private static IndicPosition GetPosition(in GlyphInfo info) => (IndicPosition)info.ShaperAuxiliary;

    private static void SetPosition(ref GlyphInfo info, IndicPosition position) => info.ShaperAuxiliary = (byte)position;

    /// <summary>Whether the category of a glyph is in a set (false when the glyph is a ligature: its category is lost).</summary>
    private static bool IsOneOf(in GlyphInfo info, uint flags) => !info.IsLigated && (Flag(info.ShaperCategory) & flags) != 0;

    private static bool IsConsonant(in GlyphInfo info) => IsOneOf(info, ConsonantFlags);

    private static bool IsJoiner(in GlyphInfo info) => IsOneOf(info, JoinerFlags);

    private static bool IsHalant(in GlyphInfo info) => IsOneOf(info, Flag(IndicCategory.H));

    private static bool SetupSyllables(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        SyllabicShaping.FindSyllables(buffer, IndicSyllableMachine.Classes, IndicSyllableMachine.ClassCount, IndicSyllableMachine.Transitions, IndicSyllableMachine.Accepts, (int)IndicSyllableMachine.BrokenSyllable);
        SyllabicShaping.SetUnsafeToBreak(buffer);
        return false;
    }

    /// <summary>
    /// The position of a consonant from the forms of the font (HarfBuzz's <c>consonant_position_from_face</c>): below when the
    /// font has a below-base form of the consonant with a virama, after when it has a post-base or a pre-base-reordering form.
    /// Both orders are matched (virama, consonant for the new specification, consonant, virama for the old one) since some
    /// fonts copied the lookups of the old specification to the new one, and Uniscribe respects them.
    /// </summary>
    private static IndicPosition GetConsonantPositionFromFont(ShapePlan plan, IndicPlanData data, int consonant, int virama)
    {
        ReadOnlySpan<int> glyphs = [virama, consonant, virama];
        var zeroContext = data.ZeroContext;
        if (plan.WouldSubstitute(BlwfTag, glyphs[..2], zeroContext) || plan.WouldSubstitute(BlwfTag, glyphs[1..], zeroContext)
            || plan.WouldSubstitute(VatuTag, glyphs[..2], zeroContext) || plan.WouldSubstitute(VatuTag, glyphs[1..], zeroContext))
        {
            return IndicPosition.BelowC;
        }

        if (plan.WouldSubstitute(PstfTag, glyphs[..2], zeroContext) || plan.WouldSubstitute(PstfTag, glyphs[1..], zeroContext))
            return IndicPosition.PostC;

        if (plan.WouldSubstitute(PrefTag, glyphs[..2], zeroContext) || plan.WouldSubstitute(PrefTag, glyphs[1..], zeroContext))
            return IndicPosition.PostC;

        return IndicPosition.BaseC;
    }

    private static void UpdateConsonantPositions(ShapePlan plan, IndicPlanData data, in ShapingFont font, ShapingBuffer buffer)
    {
        if (!data.TryGetViramaGlyph(font, out var virama))
            return;

        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (GetPosition(info[i]) != IndicPosition.BaseC)
                continue;

            var consonant = info[i].Codepoint;
            if (!data.TryGetConsonantPosition(consonant, out var position))
            {
                position = GetConsonantPositionFromFont(plan, data, consonant, virama);
                data.SetConsonantPosition(consonant, position);
            }

            SetPosition(ref info[i], position);
        }
    }

    private static bool InitialReordering(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var data = (IndicPlanData)plan.ShaperData!;
        UpdateConsonantPositions(plan, data, font, buffer);
        var changed = SyllabicShaping.InsertDottedCircles(font, buffer, (int)IndicSyllableType.BrokenCluster, IndicCategory.DottedCircle, IndicCategory.Repha, (int)IndicPosition.End);
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            switch ((IndicSyllableType)SyllabicShaping.GetSyllableType(buffer.Info[start]))
            {
                // The vowels, the placeholders and the dotted circles inserted in the broken clusters are consonants
                case IndicSyllableType.VowelSyllable or IndicSyllableType.ConsonantSyllable or IndicSyllableType.BrokenCluster or IndicSyllableType.StandaloneCluster:
                    InitialReorderingConsonantSyllable(plan, data, buffer, start, end);
                    break;
            }
        }

        return changed;
    }

    /// <summary>The initial reordering of a syllable (HarfBuzz's <c>initial_reordering_consonant_syllable</c>).</summary>
    private static void InitialReorderingConsonantSyllable(ShapePlan plan, IndicPlanData data, ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;
        var config = data.Config;
        var script = buffer.Script;

        // https://github.com/harfbuzz/harfbuzz/issues/435#issuecomment-335560167: for the compatibility with the legacy usage
        // in Kannada, Ra+H+ZWJ behaves like Ra+ZWJ+H
        if (script == UnicodeScript.Kannada && start + 3 <= end && IsOneOf(info[start], Flag(IndicCategory.Ra)) && IsOneOf(info[start + 1], Flag(IndicCategory.H)) && IsOneOf(info[start + 2], Flag(IndicCategory.Zwj)))
        {
            buffer.MergeClusters(start + 1, start + 3);
            (info[start + 1], info[start + 2]) = (info[start + 2], info[start + 1]);
        }

        // 1. Find the base consonant: starting from the end of the syllable, move backwards until a consonant is found that
        // does not have a below-base or post-base form (post-base forms have to follow below-base forms), or that is not a
        // pre-base-reordering Ra, or arrive at the first consonant. If the syllable starts with Ra + Halant (in a script that
        // has Reph) and has more than one consonant, Ra is excluded from the candidates.
        var baseIndex = end;
        var hasReph = false;
        {
            var limit = start;
            if (data.Masks[Rphf] != 0 && start + 3 <= end
                && ((config.RephMode == RephMode.Implicit && !IsJoiner(info[start + 2])) || (config.RephMode == RephMode.Explicit && info[start + 2].ShaperCategory == IndicCategory.Zwj)))
            {
                // See whether it matches the rphf feature
                ReadOnlySpan<int> glyphs = [info[start].Codepoint, info[start + 1].Codepoint, config.RephMode == RephMode.Explicit ? info[start + 2].Codepoint : 0];
                if (plan.WouldSubstitute(RphfTag, glyphs[..2], data.ZeroContext) || (config.RephMode == RephMode.Explicit && plan.WouldSubstitute(RphfTag, glyphs, data.ZeroContext)))
                {
                    limit += 2;
                    while (limit < end && IsJoiner(info[limit]))
                    {
                        limit++;
                    }

                    baseIndex = start;
                    hasReph = true;
                }
            }
            else if (config.RephMode == RephMode.LogicalRepha && info[start].ShaperCategory == IndicCategory.Repha)
            {
                limit++;
                while (limit < end && IsJoiner(info[limit]))
                {
                    limit++;
                }

                baseIndex = start;
                hasReph = true;
            }

            var i = end;
            var seenBelow = false;
            do
            {
                i--;
                if (IsConsonant(info[i]))
                {
                    // A consonant that does not have a below-base or post-base form
                    var position = GetPosition(info[i]);
                    if (position != IndicPosition.BelowC && (position != IndicPosition.PostC || seenBelow))
                    {
                        baseIndex = i;
                        break;
                    }

                    if (position == IndicPosition.BelowC)
                    {
                        seenBelow = true;
                    }

                    // The pre-base-reordering Ra are post-base (skipped above); else the first consonant is the base
                    baseIndex = i;
                }
                else if (start < i && info[i].ShaperCategory == IndicCategory.Zwj && info[i - 1].ShaperCategory == IndicCategory.H)
                {
                    // A ZWJ after a Halant stops the search and requests an explicit half form; a ZWJ before a Halant requests a
                    // subjoined form instead, and the search continues (Bengali Ra,H,Ya forms Ya-Phalaa by subjoining Ya)
                    break;
                }
            }
            while (i > limit);

            // Only for the unforced Reph (not Ra,H,ZWJ): without another consonant, the Reph is not formed and Ra is the base
            if (hasReph && baseIndex == start && limit - baseIndex <= 2)
            {
                hasReph = false;
            }
        }

        // 2. The matras are decomposed by the normalization; 3. the nuktas are before the halants (canonical order)

        // Reorder the characters
        for (var i = start; i < baseIndex; i++)
        {
            SetPosition(ref info[i], (IndicPosition)Math.Min((int)IndicPosition.PreC, (int)GetPosition(info[i])));
        }

        if (baseIndex < end)
        {
            SetPosition(ref info[baseIndex], IndicPosition.BaseC);
        }

        if (hasReph)
        {
            SetPosition(ref info[start], IndicPosition.RaToBecomeReph);
        }

        // With the old script tags, the first post-base Halant is moved after the last consonant (Uniscribe does it only when
        // there is no Halant after the last consonant in Kannada, unconditionally in the other scripts)
        if (data.IsOldSpec)
        {
            var disallowDoubleHalants = script == UnicodeScript.Kannada;
            for (var i = baseIndex + 1; i < end; i++)
            {
                if (info[i].ShaperCategory != IndicCategory.H)
                    continue;

                int j;
                for (j = end - 1; j > i; j--)
                {
                    if (IsConsonant(info[j]) || (disallowDoubleHalants && info[j].ShaperCategory == IndicCategory.H))
                        break;
                }

                if (info[j].ShaperCategory != IndicCategory.H && j > i)
                {
                    // Move the Halant after the last consonant
                    var halant = info[i];
                    Array.Copy(info, i + 1, info, i, j - i);
                    info[j] = halant;
                }

                break;
            }
        }

        // The marks move with the previous character
        {
            var lastPosition = IndicPosition.Start;
            for (var i = start; i < end; i++)
            {
                if ((Flag(info[i].ShaperCategory) & (JoinerFlags | Flag(IndicCategory.N) | Flag(IndicCategory.RS) | Flag(IndicCategory.CM) | Flag(IndicCategory.H))) != 0)
                {
                    SetPosition(ref info[i], lastPosition);
                    if (info[i].ShaperCategory == IndicCategory.H && GetPosition(info[i]) == IndicPosition.PreM)
                    {
                        // Uniscribe does not move the Halant with the left matra (U+092B,U+093F,U+094D)
                        for (var j = i; j > start; j--)
                        {
                            if (GetPosition(info[j - 1]) != IndicPosition.PreM)
                            {
                                SetPosition(ref info[i], GetPosition(info[j - 1]));
                                break;
                            }
                        }
                    }
                }
                else if (GetPosition(info[i]) != IndicPosition.Smvd)
                {
                    if (info[i].ShaperCategory == IndicCategory.MPst && i > start && info[i - 1].ShaperCategory == IndicCategory.SM)
                    {
                        SetPosition(ref info[i - 1], GetPosition(info[i]));
                    }

                    lastPosition = GetPosition(info[i]);
                }
            }
        }

        // The post-base consonants own everything before them since the last consonant or matra
        {
            var last = baseIndex;
            for (var i = baseIndex + 1; i < end; i++)
            {
                if (IsConsonant(info[i]))
                {
                    for (var j = last + 1; j < i; j++)
                    {
                        if (GetPosition(info[j]) < IndicPosition.Smvd)
                        {
                            SetPosition(ref info[j], GetPosition(info[i]));
                        }
                    }

                    last = i;
                }
                else if ((Flag(info[i].ShaperCategory) & MatraFlags) != 0)
                {
                    last = i;
                }
            }
        }

        {
            // The syllable is used for the accounting of the sort
            var syllable = info[start].Syllable;
            for (var i = start; i < end; i++)
            {
                info[i].Syllable = (byte)(i - start);
            }

            StableSortByPosition(info, start, end);

            // Find the base again, and flip the sequence of left matras (https://github.com/harfbuzz/harfbuzz/issues/3863)
            var firstLeftMatra = end;
            var lastLeftMatra = end;
            baseIndex = end;
            for (var i = start; i < end; i++)
            {
                var position = GetPosition(info[i]);
                if (position == IndicPosition.BaseC)
                {
                    baseIndex = i;
                    break;
                }

                if (position == IndicPosition.PreM)
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
                // The clusters are merged later; the nuktas... are reversed back
                buffer.Reverse(firstLeftMatra, lastLeftMatra + 1);
                var k = firstLeftMatra;
                for (var j = k; j <= lastLeftMatra; j++)
                {
                    if ((Flag(info[j].ShaperCategory) & MatraFlags) != 0)
                    {
                        buffer.Reverse(k, j + 1);
                        k = j + 1;
                    }
                }
            }

            // The post-base glyphs may move a lot: with the old specification (whose Halants move), all the clusters after the
            // base are merged; else the clusters of the glyphs that moved are merged (the pre-base glyphs are merged by the final
            // reordering, https://github.com/harfbuzz/harfbuzz/issues/2272)
            if (data.IsOldSpec || end - start > 127)
            {
                buffer.MergeClusters(baseIndex, end);
            }
            else
            {
                for (var i = baseIndex; i < end; i++)
                {
                    if (info[i].Syllable == 255)
                        continue;

                    var min = i;
                    var max = i;
                    var j = start + info[i].Syllable;
                    while (j != i)
                    {
                        min = Math.Min(min, j);
                        max = Math.Max(max, j);
                        var next = start + info[j].Syllable;
                        info[j].Syllable = 255;
                        j = next;
                    }

                    buffer.MergeClusters(Math.Max(baseIndex, min), max + 1);
                }
            }

            for (var i = start; i < end; i++)
            {
                info[i].Syllable = syllable;
            }
        }

        // The masks
        var masks = data.Masks;
        for (var i = start; i < end && GetPosition(info[i]) == IndicPosition.RaToBecomeReph; i++)
        {
            info[i].Mask |= masks[Rphf];
        }

        var mask = masks[Half];
        if (!data.IsOldSpec && config.BlwfMode == BlwfMode.PreAndPost)
        {
            mask |= masks[Blwf];
        }

        for (var i = start; i < baseIndex; i++)
        {
            info[i].Mask |= mask;
        }

        mask = masks[Blwf] | masks[Abvf] | masks[Pstf];
        for (var i = baseIndex + 1; i < end; i++)
        {
            info[i].Mask |= mask;
        }

        if (data.IsOldSpec && script == UnicodeScript.Devanagari)
        {
            // The old eyelash Ra: the below-base form applies to the Ra below the half forms too, except with Ra,Halant,ZWJ,
            // which requests the eyelash form (U+0924,U+094D,U+0930,U+094D,U+0915 with Sanskrit 2003)
            for (var i = start; i + 1 < baseIndex; i++)
            {
                if (info[i].ShaperCategory == IndicCategory.Ra && info[i + 1].ShaperCategory == IndicCategory.H && (i + 2 == baseIndex || info[i + 2].ShaperCategory != IndicCategory.Zwj))
                {
                    info[i].Mask |= masks[Blwf];
                    info[i + 1].Mask |= masks[Blwf];
                }
            }
        }

        // A Halant,Ra sequence for the pre-base-reordering processing
        const int PrefLength = 2;
        if (masks[Pref] != 0 && baseIndex + PrefLength < end)
        {
            for (var i = baseIndex + 1; i + PrefLength - 1 < end; i++)
            {
                if (plan.WouldSubstitute(PrefTag, [info[i].Codepoint, info[i + 1].Codepoint], data.ZeroContext))
                {
                    info[i].Mask |= masks[Pref];
                    info[i + 1].Mask |= masks[Pref];
                    break;
                }
            }
        }

        // The effects of ZWJ and ZWNJ: a ZWNJ disables the half forms (ZWJ and ZWNJ disable cjct by being there: the feature
        // does not skip them)
        for (var i = start + 1; i < end; i++)
        {
            if (!IsJoiner(info[i]))
                continue;

            var nonJoiner = info[i].ShaperCategory == IndicCategory.Zwnj;
            var j = i;
            do
            {
                j--;
                if (nonJoiner)
                {
                    info[j].Mask &= ~masks[Half];
                }
            }
            while (j > start && !IsConsonant(info[j]));
        }
    }

    /// <summary>A stable sort of the glyphs of a syllable by position (HarfBuzz's <c>hb_stable_sort</c> with <c>compare_indic_order</c>).</summary>
    private static void StableSortByPosition(GlyphInfo[] info, int start, int end)
    {
        for (var i = start + 1; i < end; i++)
        {
            var item = info[i];
            var j = i;
            while (j > start && info[j - 1].ShaperAuxiliary > item.ShaperAuxiliary)
            {
                info[j] = info[j - 1];
                j--;
            }

            info[j] = item;
        }
    }

    private static bool FinalReordering(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        if (buffer.Length == 0)
            return false;

        var data = (IndicPlanData)plan.ShaperData!;
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            FinalReorderingSyllable(data, buffer, start, end);
        }

        return false;
    }

    /// <summary>
    /// The final reordering of a syllable, after the basic features (HarfBuzz's <c>final_reordering_syllable_indic</c>): the
    /// pre-base matras move closer to the base according to the half forms that were formed, the reph moves to its position,
    /// the pre-base-reordering consonants move before the base, and the left matra at the start of a word gets <c>init</c>.
    /// </summary>
    private static void FinalReorderingSyllable(IndicPlanData data, ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;
        var masks = data.Masks;
        var script = buffer.Script;

        // The ligations and the multiple substitutions of the basic features may have lost the category of a halant: a
        // multiplied virama glyph is a halant again
        var viramaGlyph = data.ViramaGlyph;
        if (viramaGlyph > 0)
        {
            for (var i = start; i < end; i++)
            {
                if (info[i].Codepoint == viramaGlyph && info[i].IsLigated && info[i].IsMultiplied)
                {
                    info[i].ShaperCategory = IndicCategory.H;
                    info[i].ClearLigatedAndMultiplied();
                }
            }
        }

        // 4. Final reordering: find the base again
        var tryPref = masks[Pref] != 0;
        int baseIndex;
        for (baseIndex = start; baseIndex < end; baseIndex++)
        {
            if (GetPosition(info[baseIndex]) < IndicPosition.BaseC)
                continue;

            if (tryPref && baseIndex + 1 < end)
            {
                for (var i = baseIndex + 1; i < end; i++)
                {
                    if ((info[i].Mask & masks[Pref]) == 0)
                        continue;

                    if (!(info[i].IsSubstituted && info[i].IsLigatedAndDidNotMultiply))
                    {
                        // A pref candidate that did not form: the base is around here
                        baseIndex = i;
                        while (baseIndex < end && IsHalant(info[baseIndex]))
                        {
                            baseIndex++;
                        }

                        if (baseIndex < end)
                        {
                            SetPosition(ref info[baseIndex], IndicPosition.BaseC);
                        }

                        tryPref = false;
                    }

                    break;
                }

                if (baseIndex == end)
                    break;
            }

            // In Malayalam, skip the below-base (but not the post-base) forms that did not form
            if (script == UnicodeScript.Malayalam)
            {
                for (var i = baseIndex + 1; i < end; i++)
                {
                    while (i < end && IsJoiner(info[i]))
                    {
                        i++;
                    }

                    if (i == end || !IsHalant(info[i]))
                        break;

                    i++;
                    while (i < end && IsJoiner(info[i]))
                    {
                        i++;
                    }

                    if (i < end && IsConsonant(info[i]) && GetPosition(info[i]) == IndicPosition.BelowC)
                    {
                        baseIndex = i;
                        SetPosition(ref info[baseIndex], IndicPosition.BaseC);
                    }
                }
            }

            if (start < baseIndex && GetPosition(info[baseIndex]) > IndicPosition.BaseC)
            {
                baseIndex--;
            }

            break;
        }

        if (baseIndex == end && start < baseIndex && IsOneOf(info[baseIndex - 1], Flag(IndicCategory.Zwj)))
        {
            baseIndex--;
        }

        if (baseIndex < end)
        {
            while (start < baseIndex && IsOneOf(info[baseIndex], Flag(IndicCategory.N) | Flag(IndicCategory.H)))
            {
                baseIndex--;
            }
        }

        // Reorder the matras: a pre-base matra moves closer to the main consonant, after the last standalone halant glyph,
        // after the initial matra position and before the main consonant. Testing Uniscribe shows that it is not moved after
        // a halant followed by ZWJ, and it is after a ZWNJ (which ends the syllable: the following matras are in the next one).
        if (start + 1 < end && start < baseIndex)
        {
            // When the base is lost, before the last glyph
            var newPosition = baseIndex == end ? baseIndex - 2 : baseIndex - 1;

            // Malayalam and Tamil have no half forms nor explicit virama forms: the glyphs formed by half are chillus or
            // ligated explicit viramas, and the matra is after them
            if (script != UnicodeScript.Malayalam && script != UnicodeScript.Tamil)
            {
                while (true)
                {
                    while (newPosition > start && !IsOneOf(info[newPosition], MatraFlags | Flag(IndicCategory.H)))
                    {
                        newPosition--;
                    }

                    // Without a Halant, done; else only when the Halant does not belong to the matra
                    if (IsHalant(info[newPosition]) && GetPosition(info[newPosition]) != IndicPosition.PreM)
                    {
                        if (newPosition + 1 < end && info[newPosition + 1].ShaperCategory == IndicCategory.Zwj && newPosition > start)
                        {
                            // Not after a Halant followed by ZWJ: keep searching
                            newPosition--;
                            continue;
                        }
                    }
                    else
                    {
                        newPosition = start;
                    }

                    break;
                }
            }

            if (start < newPosition && GetPosition(info[newPosition]) != IndicPosition.PreM)
            {
                // Move the matras
                for (var i = newPosition; i > start; i--)
                {
                    if (GetPosition(info[i - 1]) != IndicPosition.PreM)
                        continue;

                    var oldPosition = i - 1;
                    if (oldPosition < baseIndex && baseIndex <= newPosition)
                    {
                        baseIndex--;
                    }

                    var matra = info[oldPosition];
                    Array.Copy(info, oldPosition + 1, info, oldPosition, newPosition - oldPosition);
                    info[newPosition] = matra;

                    // The clusters are merged after the reordering (the matra reordering is special)
                    buffer.MergeClusters(newPosition, Math.Min(end, baseIndex + 1));
                    newPosition--;
                }
            }
            else
            {
                for (var i = start; i < baseIndex; i++)
                {
                    if (GetPosition(info[i]) == IndicPosition.PreM)
                    {
                        buffer.MergeClusters(i, Math.Min(end, baseIndex + 1));
                        break;
                    }
                }
            }
        }

        // Reorder the reph: a reph encoded as Ra,H or Ra,H,ZWJ moves only if it ligated to the repha form; a repha encoded
        // separately moves only if it did not ligate (the font makes it work without the reordering)
        if (start + 1 < end && GetPosition(info[start]) == IndicPosition.RaToBecomeReph && ((info[start].ShaperCategory == IndicCategory.Repha) ^ info[start].IsLigatedAndDidNotMultiply))
        {
            var newRephPosition = FindRephPosition(info, data.Config.RephPosition, start, end, baseIndex);
            buffer.MergeClusters(start, newRephPosition + 1);
            var reph = info[start];
            Array.Copy(info, start + 1, info, start, newRephPosition - start);
            info[newRephPosition] = reph;
            if (start < baseIndex && baseIndex <= newRephPosition)
            {
                baseIndex--;
            }
        }

        // Reorder the pre-base-reordering consonants
        if (tryPref && baseIndex + 1 < end)
        {
            for (var i = baseIndex + 1; i < end; i++)
            {
                if ((info[i].Mask & masks[Pref]) == 0)
                    continue;

                // 1. Only a glyph produced by the substitution of pref (it ligated)
                if (info[i].IsLigatedAndDidNotMultiply)
                {
                    // 2. The position of a pre-base matra, else 3. before the main consonant
                    var newPosition = baseIndex;
                    if (script != UnicodeScript.Malayalam && script != UnicodeScript.Tamil)
                    {
                        while (newPosition > start && !IsOneOf(info[newPosition - 1], MatraFlags | Flag(IndicCategory.H)))
                        {
                            newPosition--;
                        }
                    }

                    // After a ZWJ or a ZWNJ that follows the halant
                    if (newPosition > start && IsHalant(info[newPosition - 1]) && newPosition < end && IsJoiner(info[newPosition]))
                    {
                        newPosition++;
                    }

                    // (The base moves forward when the glyph moves before it, but it is not used after)
                    var oldPosition = i;
                    buffer.MergeClusters(newPosition, oldPosition + 1);
                    var glyph = info[oldPosition];
                    Array.Copy(info, newPosition, info, newPosition + 1, oldPosition - newPosition);
                    info[newPosition] = glyph;
                }

                break;
            }
        }

        // The left matra at the start of a word gets init
        if (GetPosition(info[start]) == IndicPosition.PreM)
        {
            if (start == 0 || !IsLetterMarkOrFormat(info[start - 1].GeneralCategory))
            {
                info[start].Mask |= masks[Init];
            }
            else
            {
                buffer.UnsafeToBreak(start - 1, start + 1);
            }
        }
    }

    /// <summary>The general categories from Cf to Mn in the order of HarfBuzz (Cf, Cn, Co, Cs, the letters and the marks).</summary>
    private static bool IsLetterMarkOrFormat(UnicodeCategory category) => category is UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse
        or UnicodeCategory.Surrogate or UnicodeCategory.LowercaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.UppercaseLetter or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.NonSpacingMark;

    /// <summary>The new position of the reph in a syllable (the steps of the specification, as HarfBuzz interprets them).</summary>
    private static int FindRephPosition(GlyphInfo[] info, IndicPosition rephPosition, int start, int end, int baseIndex)
    {
        int newRephPosition;

        // 1. If the reph is positioned after the post-base consonant forms, step 5
        if (rephPosition != IndicPosition.AfterPost)
        {
            // 2. The position after the first explicit halant between the first post-reph consonant and the last main
            // consonant (after a following ZWJ or ZWNJ)
            if (TryFindHalant(info, start, baseIndex, out newRephPosition))
                return newRephPosition;

            // 3. After the main consonant: the first consonant not ligated with the main one
            if (rephPosition == IndicPosition.AfterMain)
            {
                newRephPosition = baseIndex;
                while (newRephPosition + 1 < end && GetPosition(info[newRephPosition + 1]) <= IndicPosition.AfterMain)
                {
                    newRephPosition++;
                }

                if (newRephPosition < end)
                    return newRephPosition;
            }

            // 4. Before the post-base consonants: the first post-base consonant not ligated with the main one, else before the
            // first matra, syllable modifier sign or vedic sign
            if (rephPosition == IndicPosition.AfterSub)
            {
                newRephPosition = baseIndex;
                while (newRephPosition + 1 < end && (Flag((int)GetPosition(info[newRephPosition + 1])) & (Flag((int)IndicPosition.PostC) | Flag((int)IndicPosition.AfterPost) | Flag((int)IndicPosition.Smvd))) == 0)
                {
                    newRephPosition++;
                }

                if (newRephPosition < end)
                    return newRephPosition;
            }
        }

        // 5. Before the first post-base matra, syllable modifier sign or vedic sign after the intended position (HarfBuzz
        // repeats step 2, https://github.com/harfbuzz/harfbuzz/issues/2298#issuecomment-615318654)
        if (TryFindHalant(info, start, baseIndex, out newRephPosition))
            return newRephPosition;

        // 6. Else the end of the syllable (before a Halant that follows a Matra, so that it can interact with the Matra; not
        // after a plain Consonant,Halant as Uniscribe: U+0930,U+094D,U+0915,U+094B,U+094D)
        newRephPosition = end - 1;
        while (newRephPosition > start && GetPosition(info[newRephPosition]) == IndicPosition.Smvd)
        {
            newRephPosition--;
        }

        if (IsHalant(info[newRephPosition]))
        {
            for (var i = baseIndex + 1; i < newRephPosition; i++)
            {
                if ((Flag(info[i].ShaperCategory) & MatraFlags) != 0)
                {
                    newRephPosition--;
                }
            }
        }

        return newRephPosition;

        static bool TryFindHalant(GlyphInfo[] info, int start, int baseIndex, out int position)
        {
            position = start + 1;
            while (position < baseIndex && !IsHalant(info[position]))
            {
                position++;
            }

            if (position < baseIndex && IsHalant(info[position]))
            {
                if (position + 1 < baseIndex && IsJoiner(info[position + 1]))
                {
                    position++;
                }

                return true;
            }

            return false;
        }
    }

    private enum RephMode
    {
        /// <summary>The reph is formed of an initial Ra,H.</summary>
        Implicit,

        /// <summary>The reph is formed of an initial Ra,H,ZWJ.</summary>
        Explicit,

        /// <summary>The repha is a character (that needs reordering).</summary>
        LogicalRepha,
    }

    private enum BlwfMode
    {
        /// <summary>The below-base forms apply to the pre-base and the post-base consonants.</summary>
        PreAndPost,

        /// <summary>The below-base forms apply to the post-base consonants only.</summary>
        PostOnly,
    }

    private sealed record IndicConfig(UnicodeScript Script, bool HasOldSpec, int Virama, IndicPosition RephPosition, RephMode RephMode, BlwfMode BlwfMode);

    /// <summary>The data of the Indic shaper for a plan (HarfBuzz's <c>indic_shape_plan_t</c>).</summary>
    private sealed class IndicPlanData(IndicConfig config, bool isOldSpec, bool zeroContext, uint[] masks)
    {
        // The positions of the consonants of the font (the glyph in the high bits, the position plus one in the low byte)
        private readonly ulong[] _consonantPositions = new ulong[256];
        private int _viramaGlyph = -1;

        public IndicConfig Config { get; } = config;

        public bool IsOldSpec { get; } = isOldSpec;

        public bool ZeroContext { get; } = zeroContext;

        public uint[] Masks { get; } = masks;

        /// <summary>The glyph of the virama of the script in the font (0 when the font does not have it, -1 when not loaded yet).</summary>
        public int ViramaGlyph => Volatile.Read(ref _viramaGlyph);

        /// <summary>The glyph of the virama (the plan is for one font: the glyph is loaded once, HarfBuzz's <c>load_virama_glyph</c>).</summary>
        public bool TryGetViramaGlyph(in ShapingFont font, out int glyph)
        {
            glyph = Volatile.Read(ref _viramaGlyph);
            if (glyph < 0)
            {
                if (Config.Virama == 0 || !font.TryGetNominalGlyph(Config.Virama, out glyph))
                {
                    glyph = 0;
                }

                Volatile.Write(ref _viramaGlyph, glyph);
            }

            return glyph != 0;
        }

        /// <summary>The position of a consonant from the forms of the font, when it is cached (a direct-mapped cache written atomically).</summary>
        public bool TryGetConsonantPosition(int glyph, out IndicPosition position)
        {
            var value = Volatile.Read(ref _consonantPositions[glyph & 0xFF]);
            position = (IndicPosition)((value & 0xFF) - 1);
            return value != 0 && (int)(value >> 8) == glyph;
        }

        public void SetConsonantPosition(int glyph, IndicPosition position) => Volatile.Write(ref _consonantPositions[glyph & 0xFF], ((ulong)(uint)glyph << 8) | (uint)((int)position + 1));
    }
}
