namespace Meziantou.Framework.Fonts;

/// <summary>The shaping model of Khmer.</summary>
/// <remarks>
/// <para>
/// Microsoft, "Developing OpenType Fonts for Khmer Script". A syllable starts with a consonant, an independent vowel
/// or a number; subscript letters follow it, each written with the coeng and the letter; then come a vowel, the
/// register shifters and the signs.
/// </para>
/// <para>
/// <b>Reordering.</b> A vowel drawn before the syllable goes to its start, and a vowel with two parts is drawn with
/// the pre-base vowel in front of the syllable and its own character, which a feature turns into the part that
/// remains. The subscript ro, drawn on the left of the base, goes to the start too ("subscript type 2").
/// </para>
/// <para>
/// <b>Features.</b> The forms of the subscripts and of the split vowels (<c>pref</c>, <c>blwf</c>, <c>abvf</c>,
/// <c>pstf</c>, <c>cfar</c>) are applied together, inside each syllable, to what follows its first character; the
/// presentation forms and <c>clig</c> come after them.
/// </para>
/// </remarks>
internal static class KhmerShaper
{
    // The categories: the letters of the syllable patterns.
    private const byte Other = 0;
    private const byte Consonant = 1;
    private const byte Vowel = 2;
    private const byte Coeng = 4;
    private const byte NonJoiner = 5;
    private const byte Joiner = 6;
    private const byte Placeholder = 10;
    private const byte DottedCircle = 11;
    private const byte Ra = 15;
    private const byte VowelAbove = 20;
    private const byte VowelBelow = 21;
    private const byte VowelPre = 22;
    private const byte VowelPost = 23;
    private const byte Robatic = 25;
    private const byte SignAbove = 26;
    private const byte SignPost = 27;

    private const int ConsonantSyllable = 0;
    private const int BrokenCluster = 1;
    private const int NonKhmerCluster = 2;

    // The features that apply to a part of a syllable, in the order of the plan.
    private const int Pref = 0;
    private const int Blwf = 1;
    private const int Abvf = 2;
    private const int Pstf = 3;
    private const int Cfar = 4;

    private const ShaperFeatureFlags Basic = ShaperFeatureFlags.PerGlyph | ShaperFeatureFlags.ManualZeroWidthJoiner | ShaperFeatureFlags.ManualZeroWidthNonJoiner | ShaperFeatureFlags.PerSyllable;
    private const ShaperFeatureFlags Presentation = ShaperFeatureFlags.ManualZeroWidthJoiner | ShaperFeatureFlags.ManualZeroWidthNonJoiner;

    /// <summary>The stage after which the syllables are reordered: before any feature of the script.</summary>
    public const int ReorderingStage = 1;

    /// <summary>The features of the model, in the order of the specification.</summary>
    public static ShaperPlan Plan { get; } = new(
        [
            new(ShapingFeature.ToTag("pref"), 2, Basic),
            new(ShapingFeature.ToTag("blwf"), 2, Basic),
            new(ShapingFeature.ToTag("abvf"), 2, Basic),
            new(ShapingFeature.ToTag("pstf"), 2, Basic),
            new(ShapingFeature.ToTag("cfar"), 2, Basic),
            new(ShapingFeature.ToTag("locl"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("ccmp"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("pres"), 3, Presentation),
            new(ShapingFeature.ToTag("abvs"), 3, Presentation),
            new(ShapingFeature.ToTag("blws"), 3, Presentation),
            new(ShapingFeature.ToTag("psts"), 3, Presentation),
            new(ShapingFeature.ToTag("clig"), 3),
        ],
        finalStage: 3,
        disabledFeatures: [ShapingFeature.ToTag("liga")]);

    private static readonly Dictionary<string, ulong> PatternCategories = new(StringComparer.Ordinal)
    {
        ["C"] = 1UL << Consonant,
        ["V"] = 1UL << Vowel,
        ["H"] = 1UL << Coeng,
        ["ZWNJ"] = 1UL << NonJoiner,
        ["ZWJ"] = 1UL << Joiner,
        ["PLACEHOLDER"] = 1UL << Placeholder,
        ["DOTTEDCIRCLE"] = 1UL << DottedCircle,
        ["Ra"] = 1UL << Ra,
        ["VAbv"] = 1UL << VowelAbove,
        ["VBlw"] = 1UL << VowelBelow,
        ["VPre"] = 1UL << VowelPre,
        ["VPst"] = 1UL << VowelPost,
        ["Robatic"] = 1UL << Robatic,
        ["Xgroup"] = 1UL << SignAbove,
        ["Ygroup"] = 1UL << SignPost,
    };

    // "Cons + {COENG + (Cons | IndV)} + [PreV | BlwV] + [RegShift] + [AbvV] + {AbvS} + [PstV] + [PstS]", loosened
    // to what a text can hold: joiners, a register shifter after any consonant, signs between the parts of the vowel.
    private static readonly Dictionary<string, string> PatternParts = new(StringComparer.Ordinal)
    {
        ["c"] = "C | Ra | V",
        ["joiner"] = "ZWJ | ZWNJ",
        ["cn"] = "c (joiner? Robatic)?",
        ["xgroup"] = "(joiner* Xgroup)*",
        ["matra_group"] = "VPre? xgroup VBlw? xgroup (joiner? VAbv)? xgroup VPst?",
        ["syllable_tail"] = "xgroup matra_group xgroup (H c)? Ygroup*",
        ["broken_cluster"] = "Robatic? (H cn)* (H | syllable_tail)",
    };

    private static readonly SyllablePattern ConsonantPattern = SyllablePattern.Compile("(cn | PLACEHOLDER | DOTTEDCIRCLE) broken_cluster", PatternCategories, PatternParts);
    private static readonly SyllablePattern BrokenPattern = SyllablePattern.Compile("broken_cluster", PatternCategories, PatternParts);

    /// <summary>Gets the two characters a vowel with two parts is drawn with: the pre-base vowel, and itself.</summary>
    public static bool TryDecompose(int codePoint, out int first, out int second)
    {
        if (codePoint is 0x17BE or 0x17BF or 0x17C0 or 0x17C4 or 0x17C5)
        {
            first = 0x17C1;
            second = codePoint;
            return true;
        }

        first = second = 0;
        return false;
    }

    private static byte GetCategory(int codePoint)
    {
        return codePoint switch
        {
            0x179A => Ra,
            0x17D2 => Coeng,
            0x17C9 or 0x17CA or 0x17CC => Robatic,
            0x17C6 or 0x17CB or (>= 0x17CD and <= 0x17D1) => SignAbove,
            0x17C7 or 0x17C8 or 0x17DD or 0x17D3 => SignPost,
            (>= 0x17B7 and <= 0x17BA) or 0x17BE => VowelAbove,
            >= 0x17BB and <= 0x17BD => VowelBelow,
            >= 0x17C1 and <= 0x17C3 => VowelPre,
            0x17B6 or 0x17BF or 0x17C0 or 0x17C4 or 0x17C5 => VowelPost,

            // The sign that opens a text takes what follows it as a consonant does.
            0x17D9 => Placeholder,
            _ => GetGenericCategory(codePoint),
        };
    }

    private static byte GetGenericCategory(int codePoint)
    {
        IndicCategories.Get(codePoint, out var category, out _);
        return category switch
        {
            IndicCategories.Consonant => Consonant,
            IndicCategories.Vowel => Vowel,
            IndicCategories.Placeholder => Placeholder,
            IndicCategories.DottedCircle => DottedCircle,
            IndicCategories.Joiner => Joiner,
            IndicCategories.NonJoiner => NonJoiner,
            _ => Other,
        };
    }

    /// <summary>Gives each character its category and cuts the text into syllables.</summary>
    public static void FindSyllables(Span<GlyphInfo> info)
    {
        foreach (ref var glyph in info)
        {
            glyph.SyllableCategory = GetCategory(glyph.CodePoint);
        }

        var serial = 0;
        var start = 0;
        while (start < info.Length)
        {
            var length = ConsonantPattern.Match(info, start);
            var kind = ConsonantSyllable;
            var broken = BrokenPattern.Match(info, start);
            if (broken > length)
            {
                length = broken;
                kind = BrokenCluster;
            }

            if (length == 0)
            {
                length = 1;
                kind = NonKhmerCluster;
            }

            serial = serial == 15 ? 1 : serial + 1;
            for (var i = start; i < start + length; i++)
            {
                info[i].Syllable = (byte)((serial << 4) | kind);
            }

            start += length;
        }
    }

    /// <summary>Reorders the syllables and marks the glyphs the features of the model apply to.</summary>
    /// <param name="buffer">The glyphs of the run.</param>
    /// <param name="face">The font.</param>
    /// <param name="firstBit">The bit of the first feature of the plan in the masks of the glyphs.</param>
    /// <param name="hasCfar">Whether the font has the <c>cfar</c> feature.</param>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void Reorder(GlyphBuffer buffer, FontFace face, int firstBit, bool hasCfar)
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
                ReorderSyllable(buffer, start, end, firstBit, hasCfar);
            }

            start = end;
        }
    }

    // A syllable that does not start with a consonant is shown on a dotted circle.
    private static void InsertDottedCircles(GlyphBuffer buffer, FontFace face)
    {
        var circle = face.MapCodePoint(0x25CC);
        if (circle == 0)
            return;

        var lastSyllable = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var syllable = buffer.Info[i].Syllable;
            // A syllable is told from the one before it: the numbers of the syllables come back after fifteen.
            if (syllable == lastSyllable || (syllable & 0x0F) != BrokenCluster)
            {
                lastSyllable = syllable;
                continue;
            }

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

    private static void ReorderSyllable(GlyphBuffer buffer, int start, int end, int firstBit, bool hasCfar)
    {
        var info = buffer.Info;

        // What follows the first character of the syllable can take a form below, above or after it.
        var forms = (1u << (firstBit + Blwf)) | (1u << (firstBit + Abvf)) | (1u << (firstBit + Pstf));
        for (var i = start + 1; i < end; i++)
        {
            info[i].Mask |= forms;
        }

        var subscripts = 0;
        for (var i = start + 1; i < end; i++)
        {
            // "When a COENG + (Cons | IndV) combination are found (and subscript count is less than two) the
            // character combination is handled according to the subscript type of the character following the
            // COENG. [...] Subscript Type 2 - The COENG + RO characters are reordered to immediately before the base
            // glyph. Then the COENG + RO characters are assigned to have the 'pref' OpenType feature applied to them."
            if (info[i].SyllableCategory == Coeng && subscripts <= 2 && i + 1 < end)
            {
                subscripts++;
                if (info[i + 1].SyllableCategory != Ra)
                    continue;

                info[i].Mask |= 1u << (firstBit + Pref);
                info[i + 1].Mask |= 1u << (firstBit + Pref);
                buffer.MergeClusters(start, i + 2);
                var coeng = info[i];
                var ro = info[i + 1];
                info[start..i].CopyTo(info[(start + 2)..]);
                info[start] = coeng;
                info[start + 1] = ro;

                // What follows a subscript ro can take another form: the feature for it tells a subscript written
                // after the ro from one written before.
                if (hasCfar)
                {
                    for (var j = i + 2; j < end; j++)
                    {
                        info[j].Mask |= 1u << (firstBit + Cfar);
                    }
                }

                subscripts = 2;
            }
            else if (info[i].SyllableCategory == VowelPre)
            {
                // The vowel drawn before the syllable, or the first part of a vowel with two parts.
                buffer.MergeClusters(start, i + 1);
                var vowel = info[i];
                info[start..i].CopyTo(info[(start + 1)..]);
                info[start] = vowel;
            }
        }
    }
}
