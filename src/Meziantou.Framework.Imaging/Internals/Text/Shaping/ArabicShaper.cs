namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The shaping model of the scripts whose letters join: Arabic and Syriac.</summary>
/// <remarks>
/// <para>
/// Microsoft, "Developing OpenType Fonts for Arabic Script" and "Developing OpenType Fonts for Syriac Script"; the
/// Unicode Standard, sections 9.2 "Arabic" (the joining rules R1 to R7) and 9.3 "Syriac" (the forms of alaph); Unicode
/// Technical Report #53 for the order of the marks.
/// </para>
/// <para>
/// <b>Forms.</b> A letter takes the form its neighbors ask for: isolated, initial, medial or final. The Joining_Type
/// property says on which sides a letter joins; marks and format characters are transparent, and the neighbors of a
/// letter are the letters on each side of them. The font has a feature per form, applied to the letters that take it.
/// </para>
/// <para>
/// <b>Stages.</b> The features are applied in the order of the specifications, a stage after the other: a feature sees
/// the glyphs of the features before it. Composition and the forms come first, one form per stage; the required
/// ligatures see the forms; the features of typographic refinement come last, with the features every script has.
/// </para>
/// <para>
/// <b>Joiners.</b> A zero width joiner asks the letters around it to take their joining forms and, in these scripts,
/// also keeps them from forming a ligature: the features of the model do not look through it.
/// </para>
/// </remarks>
internal static class ArabicShaper
{
    /// <summary>The value of <see cref="GlyphInfo.ShaperData"/> of the parts of a stretching glyph that are drawn once.</summary>
    public const byte StretchFixed = 0x20;

    /// <summary>The value of <see cref="GlyphInfo.ShaperData"/> of the parts of a stretching glyph that are repeated.</summary>
    public const byte StretchRepeating = 0x21;

    // The forms, in the order of their features in the plans.
    private const int Isolated = 0;
    private const int Final = 1;
    private const int Final2 = 2;
    private const int Final3 = 3;
    private const int Medial = 4;
    private const int Medial2 = 5;
    private const int Initial = 6;
    private const int NoForm = -1;

    private const ShaperFeatureFlags Joiner = ShaperFeatureFlags.ManualZeroWidthJoiner;
    private const ShaperFeatureFlags Form = ShaperFeatureFlags.PerGlyph | ShaperFeatureFlags.ManualZeroWidthJoiner;

    private static readonly ShaperPlan ArabicPlan = CreatePlan(isArabic: true, hasRequiredAlternates: false);
    private static readonly ShaperPlan ArabicPlanWithRequiredAlternates = CreatePlan(isArabic: true, hasRequiredAlternates: true);
    private static readonly ShaperPlan SyriacPlan = CreatePlan(isArabic: false, hasRequiredAlternates: false);
    private static readonly ShaperPlan SyriacPlanWithRequiredAlternates = CreatePlan(isArabic: false, hasRequiredAlternates: true);

    /// <summary>The tag of the feature of the required contextual alternates, which changes the stages of a plan.</summary>
    public static uint RequiredAlternates { get; } = ShapingFeature.ToTag("rclt");

    /// <summary>Gets the features of the model, in stages.</summary>
    /// <param name="script">The script of the run.</param>
    /// <param name="hasRequiredAlternates">Whether the font has the <c>rclt</c> feature for the script.</param>
    public static ShaperPlan GetPlan(UnicodeScript script, bool hasRequiredAlternates)
    {
        if (script == UnicodeScript.Arabic)
            return hasRequiredAlternates ? ArabicPlanWithRequiredAlternates : ArabicPlan;

        return hasRequiredAlternates ? SyriacPlanWithRequiredAlternates : SyriacPlan;
    }

    private static ShaperPlan CreatePlan(bool isArabic, bool hasRequiredAlternates)
    {
        var features = new List<ShaperFeature>
        {
            // The parts of a stretching glyph, before anything changes them.
            new(ShapingFeature.ToTag("stch"), 1, ShaperFeatureFlags.Stretching),
            new(ShapingFeature.ToTag("ccmp"), 2, Joiner),
            new(ShapingFeature.ToTag("locl"), 2, Joiner),

            // The forms, in the order of the Syriac specification, which has them all.
            new(ShapingFeature.ToTag("isol"), 3, Form),
            new(ShapingFeature.ToTag("fina"), 4, Form),
            new(ShapingFeature.ToTag("fin2"), 5, Form),
            new(ShapingFeature.ToTag("fin3"), 6, Form),
            new(ShapingFeature.ToTag("medi"), 7, Form),
            new(ShapingFeature.ToTag("med2"), 8, Form),
            new(ShapingFeature.ToTag("init"), 9, Form),
            new(ShapingFeature.ToTag("rlig"), 10, Joiner),
        };

        // The contextual alternates see the required ligatures of an Arabic font, and are seen by the ligatures that
        // follow. A font that has required contextual alternates was made for a shaper that applies them with the
        // other alternates and the ligatures, in the order of its lookups: the stages are then one.
        var stage = isArabic ? 11 : 10;
        var finalStage = hasRequiredAlternates ? stage : stage + 1;
        features.Add(new(ShapingFeature.ToTag("calt"), stage, Joiner));
        features.Add(new(ShapingFeature.ToTag("liga"), finalStage, Joiner));
        features.Add(new(ShapingFeature.ToTag("clig"), finalStage, Joiner));
        features.Add(new(ShapingFeature.ToTag("mset"), finalStage, Joiner));
        return new ShaperPlan([.. features], finalStage);
    }

    /// <summary>Gives each letter the bit of the feature of its form.</summary>
    /// <param name="info">The characters of the run, in the order of the text.</param>
    /// <param name="firstBit">The bit of the first form in the masks of the glyphs.</param>
    public static void SetFormMasks(Span<GlyphInfo> info, int firstBit)
    {
        // The letter before the current one, the marks and format characters between them left out.
        var previous = -1;
        var previousType = UnicodeJoiningType.NonJoining;
        var previousForm = NoForm;
        for (var i = 0; i < info.Length; i++)
        {
            var type = UnicodeProperties.GetJoiningType(info[i].CodePoint);
            if (type == UnicodeJoiningType.Transparent)
                continue;

            var form = NoForm;
            if (type != UnicodeJoiningType.NonJoining)
            {
                // R1 to R7: two letters join when the first one joins on the side of the second and the second on
                // the side of the first. A join-causing character (the tatweel, the zero width joiner) joins on both
                // sides.
                var joins = previous >= 0 && JoinsNext(previousType) && JoinsPrevious(type);
                form = joins ? Final : Isolated;
                if (joins)
                {
                    previousForm = previousForm == Final ? Medial : Initial;
                }

                if (UnicodeProperties.GetJoiningGroup(info[i].CodePoint) == UnicodeJoiningGroup.Alaph)
                {
                    // An alaph that does not join the letter before it has two forms of its own at the end of a
                    // word: one after dalath and rish, one after the other letters that do not join it.
                    if (!joins && previous >= 0 && previousType == UnicodeJoiningType.RightJoining)
                    {
                        form = UnicodeProperties.GetJoiningGroup(info[previous].CodePoint) == UnicodeJoiningGroup.DalathRish ? Final3 : Final2;
                    }
                }

                // The alaph before this letter is not at the end of its word: joined, it has a form of its own;
                // not joined, it is the plain letter.
                if (previous >= 0 && JoinsPrevious(type) && UnicodeProperties.GetJoiningGroup(info[previous].CodePoint) == UnicodeJoiningGroup.Alaph)
                {
                    previousForm = previousForm switch
                    {
                        Final => Medial2,
                        Final2 or Final3 => Isolated,
                        _ => previousForm,
                    };
                }
            }

            if (previous >= 0 && previousForm != NoForm)
            {
                info[previous].Mask |= 1u << (firstBit + previousForm);
            }

            previous = i;
            previousType = type;
            previousForm = form;
        }

        if (previous >= 0 && previousForm != NoForm)
        {
            info[previous].Mask |= 1u << (firstBit + previousForm);
        }
    }

    private static bool JoinsNext(UnicodeJoiningType type) => type is UnicodeJoiningType.DualJoining or UnicodeJoiningType.LeftJoining or UnicodeJoiningType.JoinCausing;

    private static bool JoinsPrevious(UnicodeJoiningType type) => type is UnicodeJoiningType.DualJoining or UnicodeJoiningType.RightJoining or UnicodeJoiningType.JoinCausing;

    /// <summary>
    /// Puts the marks of a letter in the order they are drawn in: Unicode Technical Report #53. The marks that modify
    /// the letter itself (a hamza, a small high letter) come first, those below the letter then those above it; the
    /// shadda comes before the vowel marks, which <see cref="ShapingCombiningClass"/> already orders.
    /// </summary>
    /// <param name="marks">The marks of a letter, in the order of shaping.</param>
    /// <returns><see langword="true"/> when marks were moved.</returns>
    public static bool ReorderMarks(Span<GlyphInfo> marks)
    {
        var moved = false;
        var front = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            var combiningClass = pass == 0 ? ShapingCombiningClass.Below : ShapingCombiningClass.Above;
            var first = front;
            while (first < marks.Length && marks[first].CombiningClass < combiningClass)
            {
                first++;
            }

            // The modifier marks that start the marks of the class.
            var end = first;
            while (end < marks.Length && marks[end].CombiningClass == combiningClass && IsModifierMark(marks[end].CodePoint))
            {
                end++;
            }

            if (end == first)
                continue;

            for (var i = first; i < end; i++)
            {
                var mark = marks[i];
                mark.CombiningClass = (byte)(pass == 0 ? ShapingCombiningClass.FrontBelow : ShapingCombiningClass.FrontAbove);
                marks[front..i].CopyTo(marks[(front + 1)..]);
                marks[front] = mark;
                moved |= i != front;
                front++;
            }
        }

        return moved;
    }

    // The "modifier combining marks" of Unicode Technical Report #53.
    private static bool IsModifierMark(int codePoint)
    {
        return codePoint is 0x0654 or 0x0655 or 0x0658 or 0x06DC or 0x06E3 or 0x06E7 or 0x06E8
            or 0x08CA or 0x08CB or 0x08CD or 0x08CE or 0x08CF or 0x08D3 or 0x08F3;
    }

    /// <summary>
    /// Marks the parts of the stretching glyphs, after the stage of the <c>stch</c> feature: the feature puts an odd
    /// number of glyphs in place of one, the odd ones drawn once and the even ones repeated.
    /// </summary>
    public static void RecordStretching(Span<GlyphInfo> info)
    {
        foreach (ref var glyph in info)
        {
            if ((glyph.Props & GlyphInfo.Multiplied) != 0)
            {
                glyph.ShaperData = (glyph.LigatureComponent & 1) != 0 ? StretchFixed : StretchRepeating;
            }
        }
    }

    /// <summary>
    /// Stretches the glyphs that enclose the text that follows them, such as the Syriac abbreviation mark: the parts
    /// that repeat are repeated to fill the width of the word, and every part is placed over it.
    /// </summary>
    /// <param name="buffer">The positioned glyphs of a right-to-left run, in the order they are drawn in.</param>
    /// <param name="face">The font.</param>
    /// <exception cref="ImageResourceLimitException">The glyphs exceed <see cref="ImageResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void ApplyStretching(GlyphBuffer buffer, FontFace face)
    {
        var index = buffer.Length;
        while (index > 0)
        {
            if (!IsStretching(buffer.Info[index - 1]))
            {
                index--;
                continue;
            }

            var end = index;
            var start = end;
            long fixedWidth = 0;
            long repeatingWidth = 0;
            var repeatingCount = 0;
            while (start > 0 && IsStretching(buffer.Info[start - 1]))
            {
                start--;
                var width = face.GetAdvanceWidth(buffer.Info[start].Glyph);
                if (buffer.Info[start].ShaperData == StretchRepeating)
                {
                    repeatingWidth += width;
                    repeatingCount++;
                }
                else
                {
                    fixedWidth += width;
                }
            }

            // The text the glyph encloses: the word that follows it in the text, which is before it in this order.
            long textWidth = 0;
            for (var i = start; i > 0 && !IsStretching(buffer.Info[i - 1]) && IsEnclosed(buffer.Info[i - 1]); i--)
            {
                textWidth += buffer.Positions[i - 1].XAdvance;
            }

            // The room the repeating parts have, and the number of times they fill it. When they do not fill it
            // exactly, they are drawn once more and overlap, each copy by the same amount.
            var remaining = textWidth - fixedWidth;
            long copies = 0;
            long overlap = 0;
            if (repeatingWidth > 0 && remaining > repeatingWidth)
            {
                copies = (remaining / repeatingWidth) - 1;
                if (remaining % repeatingWidth != 0)
                {
                    copies++;
                    overlap = (((copies + 1) * repeatingWidth) - remaining) / (copies * repeatingCount);
                    remaining = 0;
                }
            }

            // From the end of the word to its start: each part is placed before the one that follows it in this order.
            var x = remaining / 2;
            for (var k = end - 1; k >= start; k--)
            {
                var width = face.GetAdvanceWidth(buffer.Info[k].Glyph);
                var count = buffer.Info[k].ShaperData == StretchRepeating ? (int)Math.Min(copies, int.MaxValue - 1) + 1 : 1;
                buffer.Repeat(k, count - 1);
                for (var n = 0; n < count; n++)
                {
                    x -= width;
                    if (n > 0)
                    {
                        x += overlap;
                    }

                    // The parts are drawn over the word: they do not move the pen.
                    ref var position = ref buffer.Positions[k + count - 1 - n];
                    position.XOffset = (int)Math.Clamp(x, int.MinValue, int.MaxValue);
                    position.XAdvance = 0;
                }
            }

            index = start;
        }
    }

    private static bool IsStretching(in GlyphInfo info) => info.ShaperData is StretchFixed or StretchRepeating;

    // The characters of a word of these scripts: letters, marks, digits and symbols, and the characters without
    // visible form between them.
    private static bool IsEnclosed(in GlyphInfo info)
    {
        return info.IsIgnorable || info.Category is UnicodeCategory.OtherLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.MathSymbol or UnicodeCategory.OtherSymbol
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
    }
}
