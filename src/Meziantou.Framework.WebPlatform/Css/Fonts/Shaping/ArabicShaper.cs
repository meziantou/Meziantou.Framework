using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Arabic and Syriac scripts (HarfBuzz's hb-ot-shaper-arabic.cc, MIT license; https://learn.microsoft.com/typography/script-development/arabic).
/// Its joining is also used by the Universal Shaping Engine for the other joining scripts (Mongolian, N'Ko, Adlam, Mandaic,
/// Manichaean, Psalter Pahlavi, Phags-pa...: <see cref="CreateJoiningData"/>, <see cref="SetupJoiningMasks"/>). The joining
/// state machine of the Joining_Type of the characters
/// (ArabicShaping.txt, across ZWJ and with the joining type of the characters around the run) selects the form of each
/// letter: the masks of the <c>isol</c>, <c>fina</c>, <c>fin2</c>, <c>fin3</c>, <c>medi</c>, <c>med2</c> and <c>init</c>
/// features, applied in this order with pauses, before <c>rlig</c>, <c>calt</c>, <c>liga</c>, <c>clig</c> and <c>mset</c>.
/// The modifier combining marks are reordered (UAX #53), the Mongolian free variation selectors take the form of their base,
/// and the <c>stch</c> feature stretches the Syriac abbreviation mark.
/// </summary>
/// <remarks>
/// The Arabic fonts without the joining features get the fallback shaping (<see cref="ArabicFallbackPlan"/>): their
/// presentation forms replace the letters.
/// </remarks>
internal sealed class ArabicShaper : OpenTypeShaper
{
    public static ArabicShaper Instance { get; } = new();

    // The actions of the joining (the index of the feature), stored in GlyphInfo.ShaperAuxiliary
    private const byte Isol = 0;
    private const byte Fina = 1;
    private const byte Fin2 = 2;
    private const byte Fin3 = 3;
    private const byte Medi = 4;
    private const byte Med2 = 5;
    private const byte Init = 6;
    private const byte None = 7;
    private const byte StretchFixed = 8;
    private const byte StretchRepeating = 9;
    private const int FeatureCount = 7;

    private const ShapingScratchFlags HasStretch = ShapingScratchFlags.Shaper0;

    private static readonly uint[] JoiningFeatures =
    [
        ShapePlanBuilder.Tag("isol"),
        ShapePlanBuilder.Tag("fina"),
        ShapePlanBuilder.Tag("fin2"),
        ShapePlanBuilder.Tag("fin3"),
        ShapePlanBuilder.Tag("medi"),
        ShapePlanBuilder.Tag("med2"),
        ShapePlanBuilder.Tag("init"),
    ];

    private static readonly uint StretchTag = ShapePlanBuilder.Tag("stch");

    /// <summary>
    /// The joining state machine (HarfBuzz's <c>arabic_state_table</c>): for each state and joining type of the next
    /// character (U, L, R, D, ALAPH, DALATH RISH), the action on the previous character, the action on the current one and
    /// the next state.
    /// </summary>
    private static ReadOnlySpan<byte> StateTable =>
    [
        // State 0: prev was U, not willing to join
        None, None, 0, None, Isol, 2, None, Isol, 1, None, Isol, 2, None, Isol, 1, None, Isol, 6,

        // State 1: prev was R or ISOL/ALAPH, not willing to join
        None, None, 0, None, Isol, 2, None, Isol, 1, None, Isol, 2, None, Fin2, 5, None, Isol, 6,

        // State 2: prev was D/L in ISOL form, willing to join
        None, None, 0, None, Isol, 2, Init, Fina, 1, Init, Fina, 3, Init, Fina, 4, Init, Fina, 6,

        // State 3: prev was D in FINA form, willing to join
        None, None, 0, None, Isol, 2, Medi, Fina, 1, Medi, Fina, 3, Medi, Fina, 4, Medi, Fina, 6,

        // State 4: prev was FINA ALAPH, not willing to join
        None, None, 0, None, Isol, 2, Med2, Isol, 1, Med2, Isol, 2, Med2, Fin2, 5, Med2, Isol, 6,

        // State 5: prev was FIN2/FIN3 ALAPH, not willing to join
        None, None, 0, None, Isol, 2, Isol, Isol, 1, Isol, Isol, 2, Isol, Fin2, 5, Isol, Isol, 6,

        // State 6: prev was DALATH/RISH, not willing to join
        None, None, 0, None, Isol, 2, None, Isol, 1, None, Isol, 2, None, Fin3, 5, None, Isol, 6,
    ];

    private ArabicShaper()
    {
    }

    /// <summary>
    /// The joining type of a character for the state machine (0 U, 1 L, 2 R, 3 D, 4 ALAPH, 5 DALATH RISH, 6 T): the Joining_Type
    /// of the character, with the joining groups of Syriac that have their own forms. The characters that ArabicShaping.txt does
    /// not list are transparent when they are marks or format characters (DerivedJoiningType.txt).
    /// </summary>
    public static int GetJoiningType(int codePoint)
    {
        var type = UnicodeProperties.GetJoiningType(codePoint) switch
        {
            UnicodeJoiningType.Transparent => ArabicJoiningType.T,
            UnicodeJoiningType.LeftJoining => ArabicJoiningType.L,
            UnicodeJoiningType.DualJoining or UnicodeJoiningType.JoinCausing => ArabicJoiningType.D,
            UnicodeJoiningType.RightJoining => UnicodeProperties.GetJoiningGroup(codePoint) switch
            {
                UnicodeJoiningGroup.Alaph => ArabicJoiningType.Alaph,
                UnicodeJoiningGroup.DalathRish => ArabicJoiningType.DalathRish,
                _ => ArabicJoiningType.R,
            },
            _ => ArabicJoiningType.U,
        };

        return (int)type;
    }

    /// <summary>Whether a feature tag is one of the Syriac forms (fin2, fin3, med2), which have no fallback.</summary>
    private static bool IsSyriacFeature(uint tag) => (byte)tag is (byte)'2' or (byte)'3';

    public override void CollectFeatures(ShapePlanBuilder builder)
    {
        // The features are applied as the Arabic specification says, with pauses between them; the pause between the
        // joining forms and rlig is required
        builder.EnableFeature(StretchTag);
        builder.AddGsubPause(RecordStretch);

        builder.EnableFeature(ShapePlanBuilder.Tag("ccmp"), FeatureFlags.ManualZwj);
        builder.EnableFeature(ShapePlanBuilder.Tag("locl"), FeatureFlags.ManualZwj);
        builder.AddGsubPause(null);

        foreach (var feature in JoiningFeatures)
        {
            var hasFallback = builder.Script == UnicodeScript.Arabic && !IsSyriacFeature(feature);
            builder.AddFeature(feature, FeatureFlags.ManualZwj | (hasFallback ? FeatureFlags.HasFallback : FeatureFlags.None));
            builder.AddGsubPause(null);
        }

        // In Arabic, a ZWJ also means "do not ligate": the ligating features match the joiners manually
        builder.EnableFeature(ShapePlanBuilder.Tag("rlig"), FeatureFlags.ManualZwj | FeatureFlags.HasFallback);
        if (builder.Script == UnicodeScript.Arabic)
        {
            builder.AddGsubPause(ApplyFallbackShaping);
        }

        builder.EnableFeature(ShapePlanBuilder.Tag("calt"), FeatureFlags.ManualZwj);
        if (!builder.HasFeature(ShapePlanBuilder.Tag("rclt")))
        {
            builder.AddGsubPause(null);
        }

        builder.EnableFeature(ShapePlanBuilder.Tag("liga"), FeatureFlags.ManualZwj);
        builder.EnableFeature(ShapePlanBuilder.Tag("clig"), FeatureFlags.ManualZwj);
        builder.EnableFeature(ShapePlanBuilder.Tag("mset"), FeatureFlags.ManualZwj);
    }

    public override object? CreateData(ShapePlan plan)
    {
        // The fallback shaping applies to the Arabic fonts that have none of the joining features (fin2, fin3 and med2 are
        // Syriac only)
        var fallback = plan.Script == UnicodeScript.Arabic;
        foreach (var feature in JoiningFeatures)
        {
            fallback &= IsSyriacFeature(feature) || plan.NeedsFallback(feature);
        }

        return new ArabicPlanData(CreateJoiningData(plan), fallback);
    }

    /// <summary>Creates the masks of the joining forms of a plan (also used by the shapers of other scripts with joining).</summary>
    public static ArabicJoiningMasks CreateJoiningData(ShapePlan plan)
    {
        var masks = new uint[FeatureCount + 1];
        for (var i = 0; i < FeatureCount; i++)
        {
            masks[i] = plan.GetOneMask(JoiningFeatures[i]);
        }

        return new ArabicJoiningMasks(masks, plan.GetOneMask(StretchTag) != 0);
    }

    public override void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font) => SetupJoiningMasks(((ArabicPlanData)plan.ShaperData!).Joining, buffer, plan.Script);

    /// <summary>Applies the joining state machine to the buffer and sets the masks of the forms (HarfBuzz's <c>setup_masks_arabic_plan</c>).</summary>
    public static void SetupJoiningMasks(ArabicJoiningMasks masks, ShapingBuffer buffer, UnicodeScript script)
    {
        ApplyJoining(buffer);
        if (script == UnicodeScript.Mongolian)
        {
            CopyMongolianVariationSelectorActions(buffer);
        }

        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].Mask |= masks.Masks[Math.Min(info[i].ShaperAuxiliary, None)];
        }
    }

    /// <summary>The joining state machine (HarfBuzz's <c>arabic_joining</c>), with the context of the run.</summary>
    private static void ApplyJoining(ShapingBuffer buffer)
    {
        var info = buffer.Info;
        var count = buffer.Length;
        var previous = -1;
        var state = 0;
        var table = StateTable;

        // The context before the run: the first character that is not transparent
        foreach (var character in buffer.PreContext)
        {
            var type = GetJoiningType(character);
            if (type == (int)ArabicJoiningType.T)
                continue;

            state = table[(((state * 6) + type) * 3) + 2];
            break;
        }

        for (var i = 0; i < count; i++)
        {
            var type = GetJoiningType(info[i].Codepoint);
            if (type == (int)ArabicJoiningType.T)
            {
                info[i].ShaperAuxiliary = None;
                continue;
            }

            var entry = ((state * 6) + type) * 3;
            var previousAction = table[entry];
            // The unsafe-to-concat flags of HarfBuzz are not produced (the layout only uses the unsafe-to-break flags)
            if (previousAction != None && previous >= 0)
            {
                info[previous].ShaperAuxiliary = previousAction;
                buffer.SafeToInsertTatweel(previous, i + 1);
            }

            info[i].ShaperAuxiliary = table[entry + 1];
            previous = i;
            state = table[entry + 2];
        }

        // The context after the run
        foreach (var character in buffer.PostContext)
        {
            var type = GetJoiningType(character);
            if (type == (int)ArabicJoiningType.T)
                continue;

            var entry = ((state * 6) + type) * 3;
            if (table[entry] != None && previous >= 0)
            {
                info[previous].ShaperAuxiliary = table[entry];
                buffer.SafeToInsertTatweel(previous, buffer.Length);
            }

            break;
        }
    }

    /// <summary>The Mongolian free variation selectors take the action of their base (HarfBuzz's <c>mongolian_variation_selectors</c>).</summary>
    private static void CopyMongolianVariationSelectorActions(ShapingBuffer buffer)
    {
        var info = buffer.Info;
        for (var i = 1; i < buffer.Length; i++)
        {
            if (info[i].Codepoint is (>= 0x180B and <= 0x180D) or 0x180F)
            {
                info[i].ShaperAuxiliary = info[i - 1].ShaperAuxiliary;
            }
        }
    }

    /// <summary>
    /// After the <c>stch</c> feature, records the glyphs it multiplied (HarfBuzz's <c>record_stch</c>): the odd components
    /// repeat, the even ones are fixed.
    /// </summary>
    private static bool RecordStretch(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        if (plan.ShaperData is not ArabicPlanData { Joining.HasStretch: true })
            return false;

        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (info[i].IsMultiplied)
            {
                info[i].ShaperAuxiliary = info[i].LigatureComponent % 2 != 0 ? StretchRepeating : StretchFixed;
                buffer.ScratchFlags |= HasStretch;
            }
        }

        return false;
    }

    /// <summary>
    /// After rlig, the fallback shaping of the fonts without the joining features (HarfBuzz's <c>arabic_fallback_shape</c>):
    /// the lookups synthesized from the presentation forms of the font (<see cref="ArabicFallbackPlan"/>, created on first use).
    /// </summary>
    private static bool ApplyFallbackShaping(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var data = (ArabicPlanData)plan.ShaperData!;
        return data.UseFallback && data.GetFallbackPlan(plan, font).Shape(plan, font, buffer);
    }

    public override void PostprocessGlyphs(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font) => ApplyStretch(buffer, font);

    /// <summary>Whether a general category belongs to a word for the stretching (HarfBuzz's <c>HB_ARABIC_GENERAL_CATEGORY_IS_WORD</c>).</summary>
    private static bool IsWordCategory(UnicodeCategory category) => category is UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.ModifierLetter
        or UnicodeCategory.OtherLetter or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.NonSpacingMark or UnicodeCategory.DecimalDigitNumber
        or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.MathSymbol
        or UnicodeCategory.OtherSymbol;

    /// <summary>
    /// Stretches the recorded <c>stch</c> sequences to the width of the rest of their word (HarfBuzz's <c>apply_stch</c>): the
    /// repeating tiles are copied as many times as needed, in two passes (measure, then cut).
    /// </summary>
    private static void ApplyStretch(ShapingBuffer buffer, in ShapingFont font)
    {
        if ((buffer.ScratchFlags & HasStretch) == ShapingScratchFlags.None)
            return;

        var rtl = buffer.Direction == ShapingDirection.RightToLeft;
        if (!rtl)
        {
            buffer.Reverse();
        }

        const int MaxGlyphs = 256;
        var extraGlyphsNeeded = 0;
        for (var step = 0; step < 2; step++)
        {
            var measure = step == 0;
            var count = buffer.Length;
            var info = buffer.Info;
            var positions = buffer.Positions;
            var newLength = count + extraGlyphsNeeded;
            var j = newLength;
            for (var i = count; i > 0; i--)
            {
                if (info[i - 1].ShaperAuxiliary is not (StretchFixed or StretchRepeating))
                {
                    if (!measure)
                    {
                        --j;
                        info[j] = info[i - 1];
                        positions[j] = positions[i - 1];
                    }

                    continue;
                }

                // The widths of the tiles and of the rest of the word
                var totalWidth = 0d;
                var fixedWidth = 0d;
                var repeatingWidth = 0d;
                var fixedCount = 0;
                var repeatingCount = 0;
                var end = i;
                while (i > 0 && info[i - 1].ShaperAuxiliary is StretchFixed or StretchRepeating)
                {
                    i--;
                    var width = font.GetHorizontalAdvance(info[i].Codepoint);
                    if (info[i].ShaperAuxiliary == StretchFixed)
                    {
                        fixedWidth += width;
                        fixedCount++;
                    }
                    else
                    {
                        repeatingWidth += width;
                        repeatingCount++;
                    }
                }

                var start = i;
                var context = i;
                while (context > 0 && info[context - 1].ShaperAuxiliary is not (StretchFixed or StretchRepeating) && (info[context - 1].IsDefaultIgnorable || IsWordCategory(info[context - 1].GeneralCategory)))
                {
                    context--;
                    totalWidth += positions[context].XAdvance;
                }

                i++;

                // The number of additional copies of each repeating tile
                var copies = 0;
                var remaining = totalWidth - fixedWidth;
                if (remaining > repeatingWidth && repeatingWidth > 0)
                {
                    copies = (int)(remaining / repeatingWidth) - 1;
                }

                // An extra copy squeezed a bit may fit better
                var overlap = 0d;
                var shortfall = remaining - (repeatingWidth * (copies + 1));
                if (shortfall > 0 && repeatingCount > 0)
                {
                    ++copies;
                    var excess = ((copies + 1) * repeatingWidth) - remaining;
                    if (excess > 0)
                    {
                        overlap = excess / (copies * repeatingCount);
                        remaining = 0;
                    }
                }

                var maxCopies = 0;
                if (repeatingCount > 0 && fixedCount + repeatingCount < MaxGlyphs)
                {
                    maxCopies = (MaxGlyphs - fixedCount - repeatingCount) / repeatingCount;
                }

                copies = Math.Min(copies, maxCopies);
                if (measure)
                {
                    extraGlyphsNeeded += copies * repeatingCount;
                }
                else
                {
                    buffer.UnsafeToBreak(context, end);
                    var xOffset = remaining / 2;
                    for (var k = end; k > start; k--)
                    {
                        var width = font.GetHorizontalAdvance(info[k - 1].Codepoint);
                        var repeat = 1;
                        if (info[k - 1].ShaperAuxiliary == StretchRepeating)
                        {
                            repeat += copies;
                        }

                        positions[k - 1].XAdvance = 0;
                        for (var n = 0; n < repeat; n++)
                        {
                            if (rtl)
                            {
                                xOffset -= width;
                                if (n > 0)
                                {
                                    xOffset += overlap;
                                }
                            }

                            positions[k - 1].XOffset = xOffset;

                            // Append a copy
                            --j;
                            info[j] = info[k - 1];
                            positions[j] = positions[k - 1];
                            if (!rtl)
                            {
                                xOffset += width;
                                if (n > 0)
                                {
                                    xOffset -= overlap;
                                }
                            }
                        }
                    }
                }
            }

            if (measure)
            {
                if (!buffer.Ensure(count + extraGlyphsNeeded))
                    break;
            }
            else
            {
                buffer.Length = newLength;
            }
        }

        if (!rtl)
        {
            buffer.Reverse();
        }
    }

    /// <summary>The modifier combining marks (https://www.unicode.org/reports/tr53/).</summary>
    private static bool IsModifierCombiningMark(int codePoint) => codePoint is 0x0654 or 0x0655 or 0x0658 or 0x06DC or 0x06E3 or 0x06E7 or 0x06E8 or 0x08CA or 0x08CB or 0x08CD or 0x08CE or 0x08CF or 0x08D3 or 0x08F3;

    /// <summary>
    /// Moves the modifier combining marks of the classes 220 and 230 to the start of the marks of their class, so that they
    /// attach to the base before the other marks (UAX #53, HarfBuzz's <c>reorder_marks_arabic</c>).
    /// </summary>
    public override void ReorderMarks(ShapePlan plan, ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;
        var i = start;
        for (var combiningClass = 220; combiningClass <= 230; combiningClass += 10)
        {
            while (i < end && info[i].ModifiedCombiningClass < combiningClass)
            {
                i++;
            }

            if (i == end)
                break;

            if (info[i].ModifiedCombiningClass > combiningClass)
                continue;

            var j = i;
            while (j < end && info[j].ModifiedCombiningClass == combiningClass && IsModifierCombiningMark(info[j].Codepoint))
            {
                j++;
            }

            if (i == j)
                continue;

            // Shift the marks from i to j before the ones from start to i
            buffer.MergeClusters(start, j);
            var moved = info.AsSpan(i, j - i).ToArray();
            Array.Copy(info, start, info, start + j - i, i - start);
            moved.CopyTo(info, start);

            // Renumber the classes so that the sequence is still sorted (22 and 26 are smaller than the Arabic classes)
            var newStart = start + j - i;
            var newClass = combiningClass == 220 ? 25 : 26;
            while (start < newStart)
            {
                info[start].SetModifiedCombiningClass(newClass);
                start++;
            }

            i = j;
        }
    }
}

/// <summary>The masks of the joining forms of a plan (the data of the <see cref="ArabicShaper"/>), by joining action (the last one, "none", is 0).</summary>
internal sealed record ArabicJoiningMasks(uint[] Masks, bool HasStretch);

/// <summary>The data of the <see cref="ArabicShaper"/> for a plan (HarfBuzz's <c>arabic_shape_plan_t</c>).</summary>
internal sealed class ArabicPlanData(ArabicJoiningMasks joining, bool useFallback)
{
    private ArabicFallbackPlan? _fallbackPlan;

    public ArabicJoiningMasks Joining { get; } = joining;

    /// <summary>Whether the font has no joining feature: the fallback shaping applies.</summary>
    public bool UseFallback { get; } = useFallback;

    /// <summary>The fallback plan (the plan is for one font: it is created once, a concurrent creation creates an equivalent plan).</summary>
    public ArabicFallbackPlan GetFallbackPlan(ShapePlan plan, in ShapingFont font) => Volatile.Read(ref _fallbackPlan) ?? (_fallbackPlan = ArabicFallbackPlan.Create(plan, font));
}
