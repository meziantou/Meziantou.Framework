namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The shaping model of Myanmar.</summary>
/// <remarks>
/// <para>
/// Microsoft, "Creating and Supporting OpenType Fonts for Myanmar Script". A syllable is an optional kinzi (nga,
/// asat and the stacker, drawn above the consonant that follows), a consonant with the consonants stacked under it,
/// medial consonants, vowels, and tone marks.
/// </para>
/// <para>
/// <b>Reordering.</b> The kinzi goes after the base consonant; the medial ra, which is drawn around the base, and the
/// vowel that is drawn before the syllable go before it; an anusvara typed after a vowel below is drawn before it.
/// </para>
/// <para>
/// <b>Features.</b> <c>rphf</c> (the kinzi), <c>pref</c> (the medial ra), <c>blwf</c> and <c>pstf</c> are applied
/// one after the other, inside each syllable, then the presentation forms.
/// </para>
/// </remarks>
internal static class MyanmarShaper
{
    // The categories: the letters of the syllable patterns.
    private const byte Other = 0;
    private const byte Consonant = 1;
    private const byte IndependentVowel = 2;
    private const byte DotBelow = 3;
    private const byte Stacker = 4;
    private const byte NonJoiner = 5;
    private const byte Joiner = 6;
    private const byte ToneSign = 8;
    private const byte Anusvara = 9;
    private const byte GenericBase = 10;
    private const byte DottedCircle = 11;
    private const byte Ra = 15;
    private const byte ConsonantWithStacker = 18;
    private const byte VowelAbove = 20;
    private const byte VowelBelow = 21;
    private const byte VowelPre = 22;
    private const byte VowelPost = 23;
    private const byte Asat = 32;
    private const byte MedialHa = 35;
    private const byte MedialRa = 36;
    private const byte MedialWa = 37;
    private const byte MedialYa = 38;
    private const byte PwoTone = 39;
    private const byte VariationSelector = 40;
    private const byte MedialLa = 41;

    private const int ConsonantSyllable = 0;
    private const int BrokenCluster = 1;
    private const int NonMyanmarCluster = 2;

    private const uint ConsonantCategories = (1u << Consonant) | (1u << ConsonantWithStacker) | (1u << Ra) | (1u << IndependentVowel) | (1u << GenericBase) | (1u << DottedCircle);

    private const ShaperFeatureFlags Basic = ShaperFeatureFlags.ManualZeroWidthJoiner | ShaperFeatureFlags.PerSyllable;

    /// <summary>The stage after which the syllables are reordered: the localized forms are applied.</summary>
    public const int ReorderingStage = 2;

    /// <summary>The features of the model, in the order of the specification.</summary>
    public static ShaperPlan Plan { get; } = new(
        [
            new(ShapingFeature.ToTag("locl"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("ccmp"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("rphf"), 3, Basic),
            new(ShapingFeature.ToTag("pref"), 4, Basic),
            new(ShapingFeature.ToTag("blwf"), 5, Basic),
            new(ShapingFeature.ToTag("pstf"), 6, Basic),
            new(ShapingFeature.ToTag("pres"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("abvs"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("blws"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("psts"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
        ],
        finalStage: 7);

    private static readonly Dictionary<string, ulong> PatternCategories = new(StringComparer.Ordinal)
    {
        ["C"] = 1UL << Consonant,
        ["IV"] = 1UL << IndependentVowel,
        ["DB"] = 1UL << DotBelow,
        ["H"] = 1UL << Stacker,
        ["ZWNJ"] = 1UL << NonJoiner,
        ["ZWJ"] = 1UL << Joiner,
        ["SM"] = 1UL << ToneSign,
        ["A"] = 1UL << Anusvara,
        ["GB"] = 1UL << GenericBase,
        ["DOTTEDCIRCLE"] = 1UL << DottedCircle,
        ["Ra"] = 1UL << Ra,
        ["CS"] = 1UL << ConsonantWithStacker,
        ["VAbv"] = 1UL << VowelAbove,
        ["VBlw"] = 1UL << VowelBelow,
        ["VPre"] = 1UL << VowelPre,
        ["VPst"] = 1UL << VowelPost,
        ["As"] = 1UL << Asat,
        ["MH"] = 1UL << MedialHa,
        ["MR"] = 1UL << MedialRa,
        ["MW"] = 1UL << MedialWa,
        ["MY"] = 1UL << MedialYa,
        ["PT"] = 1UL << PwoTone,
        ["VS"] = 1UL << VariationSelector,
        ["ML"] = 1UL << MedialLa,
    };

    // The syllable of the specification: an optional kinzi, a base with its variation selector, stacked consonants,
    // an asat, the medials in their order (ya, ra, wa, ha), the vowels (before, above, below), the anusvara, the dot
    // below, vowels after the base with their own marks, tones.
    private static readonly Dictionary<string, string> PatternParts = new(StringComparer.Ordinal)
    {
        ["j"] = "ZWJ | ZWNJ",
        ["k"] = "Ra As H",
        ["c"] = "C | Ra",
        ["medial_group"] = "MY? As? MR? ((MW MH? ML? | MH ML? | ML) As?)?",
        ["main_vowel_group"] = "(VPre VS?)* VAbv* VBlw* A* (DB As?)?",
        ["post_vowel_group"] = "VPst MH? ML? As* VAbv* A* (DB As?)?",
        ["tone_group"] = "SM | PT A* DB? As?",
        ["complex_syllable_tail"] = "As* medial_group main_vowel_group post_vowel_group* tone_group* j?",
        ["syllable_tail"] = "(H (c | IV) VS?)* (H | complex_syllable_tail)",
    };

    private static readonly SyllablePattern ConsonantPattern = SyllablePattern.Compile("(k | CS)? (c | IV | GB | DOTTEDCIRCLE) VS? syllable_tail", PatternCategories, PatternParts);
    private static readonly SyllablePattern BrokenPattern = SyllablePattern.Compile("k? VS? syllable_tail", PatternCategories, PatternParts);

    private static byte GetCategory(int codePoint)
    {
        switch (codePoint)
        {
            // The letters that make a kinzi: nga, and the nga of Shan and Mon.
            case 0x1004 or 0x101B or 0x105A:
                return Ra;

            case 0x1039:
                return Stacker;

            case 0x103A:
                return Asat;

            case 0x1032 or 0x1036:
                return Anusvara;

            // The dot below, and the tone marks of Tai Laing, which are written at the same place of a syllable.
            case 0x1037 or 0xAA7C or 0xAA7D:
                return DotBelow;

            case 0x103B or 0x105E or 0x105F:
                return MedialYa;

            case 0x103C:
                return MedialRa;

            case 0x103D or 0x1082:
                return MedialWa;

            case 0x103E:
                return MedialHa;

            case 0x1060:
                return MedialLa;

            case 0x1063 or 0x1064 or (>= 0x1069 and <= 0x106D) or 0xAA7B:
                return PwoTone;

            case 0x1038 or (>= 0x1087 and <= 0x108D) or 0x108F or (>= 0x109A and <= 0x109C):
                return ToneSign;

            // The symbol that is used as a letter, and the letters of Khamti that the property does not make consonants.
            case 0x104E or (>= 0xAA74 and <= 0xAA76):
                return Consonant;

            case 0x25CC:
                return DottedCircle;

            // The generic bases of the specification, the digits and the little section sign, which take marks as a
            // letter does.
            case 0x002D or 0x00A0 or 0x00D7 or (>= 0x2012 and <= 0x2015) or 0x2022 or (>= 0x25FB and <= 0x25FE) or (>= 0x1040 and <= 0x104A) or (>= 0x1090 and <= 0x1099):
                return GenericBase;

            case >= 0xFE00 and <= 0xFE0F:
                return VariationSelector;
        }

        switch (UnicodeProperties.GetIndicSyllabicCategory(codePoint))
        {
            case UnicodeIndicSyllabicCategory.Consonant or UnicodeIndicSyllabicCategory.ConsonantDead or UnicodeIndicSyllabicCategory.ConsonantHeadLetter:
                return Consonant;

            case UnicodeIndicSyllabicCategory.VowelIndependent or UnicodeIndicSyllabicCategory.Vowel:
                return IndependentVowel;

            case UnicodeIndicSyllabicCategory.ConsonantPlaceholder or UnicodeIndicSyllabicCategory.Number or UnicodeIndicSyllabicCategory.BrahmiJoiningNumber or UnicodeIndicSyllabicCategory.NumberJoiner:
                return GenericBase;

            case UnicodeIndicSyllabicCategory.ConsonantWithStacker:
                return ConsonantWithStacker;

            case UnicodeIndicSyllabicCategory.Joiner:
                return Joiner;

            case UnicodeIndicSyllabicCategory.NonJoiner:
                return NonJoiner;

            case UnicodeIndicSyllabicCategory.Bindu or UnicodeIndicSyllabicCategory.Visarga or UnicodeIndicSyllabicCategory.SyllableModifier or UnicodeIndicSyllabicCategory.GeminationMark or UnicodeIndicSyllabicCategory.ToneMark:
                return ToneSign;

            case UnicodeIndicSyllabicCategory.ConsonantMedial or UnicodeIndicSyllabicCategory.ConsonantSubjoined or UnicodeIndicSyllabicCategory.ConsonantFinal:
                return MedialYa;

            case UnicodeIndicSyllabicCategory.VowelDependent or UnicodeIndicSyllabicCategory.PureKiller or UnicodeIndicSyllabicCategory.ConsonantKiller:
                return UnicodeProperties.GetIndicPositionalCategory(codePoint) switch
                {
                    UnicodeIndicPositionalCategory.Left or UnicodeIndicPositionalCategory.VisualOrderLeft => VowelPre,
                    UnicodeIndicPositionalCategory.Top => VowelAbove,
                    UnicodeIndicPositionalCategory.Bottom => VowelBelow,
                    _ => VowelPost,
                };

            default:
                return Other;
        }
    }

    /// <summary>Gives each character its category and cuts the text into syllables.</summary>
    public static void FindSyllables(Span<GlyphInfo> info)
    {
        foreach (ref var glyph in info)
        {
            glyph.SyllableCategory = GetCategory(glyph.CodePoint);
        }

        Span<int> states = stackalloc int[Math.Max(ConsonantPattern.StateCount, BrokenPattern.StateCount) * 2];
        var serial = 0;
        var start = 0;
        while (start < info.Length)
        {
            var length = ConsonantPattern.Match(info, start, states);
            var kind = ConsonantSyllable;

            // A joiner alone is not a syllable without its base.
            var broken = info[start].SyllableCategory is Joiner or NonJoiner ? 0 : BrokenPattern.Match(info, start, states);
            if (broken > length)
            {
                length = broken;
                kind = BrokenCluster;
            }

            if (length == 0)
            {
                length = 1;
                kind = NonMyanmarCluster;
            }

            serial = serial == 15 ? 1 : serial + 1;
            for (var i = start; i < start + length; i++)
            {
                info[i].Syllable = (byte)((serial << 4) | kind);
            }

            start += length;
        }
    }

    /// <summary>Reorders the syllables.</summary>
    /// <exception cref="ImageResourceLimitException">The glyphs exceed <see cref="ImageResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void Reorder(GlyphBuffer buffer, FontFace face)
    {
        InsertDottedCircles(buffer, face);
        var info = buffer.Info;
        for (var start = 0; start < info.Length;)
        {
            var end = start + 1;
            while (end < info.Length && info[end].Syllable == info[start].Syllable)
            {
                end++;
            }

            if ((info[start].Syllable & 0x0F) is ConsonantSyllable or BrokenCluster)
            {
                ReorderSyllable(buffer, start, end);
            }

            start = end;
        }
    }

    // A syllable without a base is shown on a dotted circle.
    private static void InsertDottedCircles(GlyphBuffer buffer, FontFace face)
    {
        var circle = face.MapCodePoint(0x25CC);
        if (circle == 0)
            return;

        var lastSyllable = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var syllable = buffer.Info[i].Syllable;
            if (syllable == lastSyllable || (syllable & 0x0F) != BrokenCluster)
                continue;

            lastSyllable = syllable;
            var template = buffer.Info[i];
            buffer.Resize(i, 0, 1);
            buffer.Info[i] = new GlyphInfo
            {
                Glyph = circle,
                CodePoint = 0x25CC,
                Cluster = template.Cluster,
                Mask = template.Mask,
                Syllable = syllable,
                SyllableCategory = DottedCircle,
                Category = UnicodeCategory.OtherSymbol,
            };
        }
    }

    private static void ReorderSyllable(GlyphBuffer buffer, int start, int end)
    {
        var info = buffer.Info;

        // The base is the first consonant, after the kinzi when the syllable starts with one.
        var hasKinzi = start + 3 <= end && info[start].SyllableCategory == Ra && info[start + 1].SyllableCategory == Asat && info[start + 2].SyllableCategory == Stacker;
        var limit = hasKinzi ? start + 3 : start;
        var @base = hasKinzi ? start : limit;
        for (var i = limit; i < end; i++)
        {
            if (((1u << info[i].SyllableCategory) & ConsonantCategories) != 0)
            {
                @base = i;
                break;
            }
        }

        // The places: the kinzi after the base; what precedes the base before it; then, in the order of the text,
        // what is after the base, with the exceptions below.
        {
            var i = start;
            for (; i < limit; i++)
            {
                info[i].SyllablePosition = IndicCategories.AfterMain;
            }

            for (; i < @base; i++)
            {
                info[i].SyllablePosition = IndicCategories.PreConsonant;
            }

            if (i < end)
            {
                info[i].SyllablePosition = IndicCategories.Base;
                i++;
            }

            var position = IndicCategories.AfterMain;
            for (; i < end; i++)
            {
                var category = info[i].SyllableCategory;
                if (category == MedialRa)
                {
                    // The medial ra is drawn around the base, from its left.
                    info[i].SyllablePosition = IndicCategories.PreConsonant;
                }
                else if (category == VowelPre)
                {
                    info[i].SyllablePosition = IndicCategories.PreMatra;
                }
                else if (category == VariationSelector)
                {
                    info[i].SyllablePosition = info[i - 1].SyllablePosition;
                }
                else if (position == IndicCategories.AfterMain && category == VowelBelow)
                {
                    position = IndicCategories.BelowConsonant;
                    info[i].SyllablePosition = position;
                }
                else if (position == IndicCategories.BelowConsonant && category == Anusvara)
                {
                    // An anusvara after a vowel below is drawn before it.
                    info[i].SyllablePosition = IndicCategories.BeforeSubjoined;
                }
                else if (position == IndicCategories.BelowConsonant && category == VowelBelow)
                {
                    info[i].SyllablePosition = position;
                }
                else if (position == IndicCategories.BelowConsonant)
                {
                    position = IndicCategories.AfterSubjoined;
                    info[i].SyllablePosition = position;
                }
                else
                {
                    info[i].SyllablePosition = position;
                }
            }
        }

        // The characters are put in the order of their places; a character that moves takes what it passes into its
        // cluster.
        for (var i = start + 1; i < end; i++)
        {
            var j = i;
            while (j > start && info[j - 1].SyllablePosition > info[i].SyllablePosition)
            {
                j--;
            }

            if (j == i)
                continue;

            buffer.MergeClusters(j, i + 1);
            var moved = info[i];
            info[j..i].CopyTo(info[(j + 1)..]);
            info[j] = moved;
        }

        // Several vowels drawn before the syllable are drawn from the base outward, each with its variation selector.
        var firstVowel = end;
        var lastVowel = end;
        for (var i = start; i < end; i++)
        {
            if (info[i].SyllablePosition == IndicCategories.PreMatra)
            {
                firstVowel = firstVowel == end ? i : firstVowel;
                lastVowel = i;
            }
        }

        if (firstVowel < lastVowel)
        {
            info[firstVowel..(lastVowel + 1)].Reverse();
            var groupStart = firstVowel;
            for (var j = firstVowel; j <= lastVowel; j++)
            {
                if (info[j].SyllableCategory == VowelPre)
                {
                    info[groupStart..(j + 1)].Reverse();
                    groupStart = j + 1;
                }
            }
        }
    }
}
