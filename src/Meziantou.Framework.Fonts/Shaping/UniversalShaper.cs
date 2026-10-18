using System.Buffers;

namespace Meziantou.Framework.Fonts;

/// <summary>The universal shaping model: the scripts that are written in clusters and have no model of their own.</summary>
/// <remarks>
/// <para>
/// Microsoft, "Creating and supporting OpenType fonts for the Universal Shaping Engine". The class of a character
/// is derived from its Unicode properties (Indic_Syllabic_Category, Indic_Positional_Category, General_Category,
/// Joining_Type), with the values the data files of the specification add or change
/// (<c>IndicSyllabicCategory-Additional.txt</c>, <c>IndicPositionalCategory-Additional.txt</c>). The text is cut into
/// clusters by the cluster schemas of the specification.
/// </para>
/// <para>
/// <b>Features.</b> "Basic cluster formation": <c>locl</c>, <c>ccmp</c>, <c>nukt</c>, <c>akhn</c> together, then
/// <c>rphf</c>, then <c>pref</c>, then <c>rkrf</c>, <c>abvf</c>, <c>blwf</c>, <c>half</c>, <c>pstf</c>, <c>vatu</c>,
/// <c>cjct</c> together, each inside a cluster. Then the clusters are reordered, the topographical features
/// (<c>isol</c>, <c>init</c>, <c>medi</c>, <c>fina</c>) are applied, and after them the presentation features with the
/// default features of every script.
/// </para>
/// <para>
/// <b>Reordering</b> is done once, after the basic features: the glyph <c>rphf</c> made goes after the base; the
/// glyph <c>pref</c> made, the vowels and the vowel modifiers that are drawn before the base go to the start of the
/// cluster, or after the last halant that is still there.
/// </para>
/// <para>
/// <b>Joining scripts</b> (N'Ko, Mongolian, Mandaic, Adlam...) get the topographical features from the joining types
/// of their letters, as Arabic does; the others from the clusters: a cluster next to another one is the start, the
/// middle or the end of a word.
/// </para>
/// </remarks>
internal static class UniversalShaper
{
    // The classes of the specification, with its sigla.
    private const byte O = 0;
    private const byte B = 1;
    private const byte N = 2;
    private const byte GB = 3;
    private const byte SUB = 4;
    private const byte H = 5;
    private const byte HN = 6;
    private const byte ZWNJ = 7;
    private const byte WJ = 8;
    private const byte R = 9;
    private const byte CS = 10;
    private const byte IS = 11;
    private const byte Sk = 12;
    private const byte G = 13;
    private const byte J = 14;
    private const byte SB = 15;
    private const byte SE = 16;
    private const byte HVM = 17;
    private const byte HM = 18;
    private const byte HR = 19;
    private const byte RK = 20;
    private const byte CGJ = 21;
    private const byte FAbv = 22;
    private const byte FBlw = 23;
    private const byte FPst = 24;
    private const byte FMAbv = 25;
    private const byte FMBlw = 26;
    private const byte FMPst = 27;
    private const byte MAbv = 28;
    private const byte MBlw = 29;
    private const byte MPst = 30;
    private const byte MPre = 31;
    private const byte CMAbv = 32;
    private const byte CMBlw = 33;
    private const byte VAbv = 34;
    private const byte VBlw = 35;
    private const byte VPst = 36;
    private const byte VPre = 37;
    private const byte VMAbv = 38;
    private const byte VMBlw = 39;
    private const byte VMPst = 40;
    private const byte VMPre = 41;
    private const byte SMAbv = 42;
    private const byte SMBlw = 43;

    // The kinds of clusters, in the order they are tried in: of two kinds that match the same characters, the first
    // one is the kind of the cluster. The number-joiner terminated cluster is 3 and the numeral cluster 4.
    private const int ViramaTerminatedCluster = 0;
    private const int SakotTerminatedCluster = 1;
    private const int StandardCluster = 2;
    private const int SymbolCluster = 5;
    private const int HieroglyphCluster = 6;
    private const int BrokenCluster = 7;
    private const int NonCluster = 8;

    // The features that apply to a part of the glyphs, in the order of the plan.
    private const int Rphf = 0;
    private const int Isol = 1;
    private const int Init = 2;
    private const int Medi = 3;
    private const int Fina = 4;

    private const ulong PostBaseClasses = (1UL << FAbv) | (1UL << FBlw) | (1UL << FPst) | (1UL << FMAbv) | (1UL << FMBlw) | (1UL << FMPst)
        | (1UL << MAbv) | (1UL << MBlw) | (1UL << MPst) | (1UL << MPre)
        | (1UL << VAbv) | (1UL << VBlw) | (1UL << VPst) | (1UL << VPre)
        | (1UL << VMAbv) | (1UL << VMBlw) | (1UL << VMPst) | (1UL << VMPre);

    private const ShaperFeatureFlags Basic = ShaperFeatureFlags.ManualZeroWidthJoiner | ShaperFeatureFlags.PerSyllable;

    /// <summary>The stage after which the glyphs forget that a feature made them: the default glyph pre-processing group.</summary>
    public const int PreprocessingStage = 2;

    /// <summary>The stage of the reph: the glyph it makes is the one that is moved after the base.</summary>
    public const int RephStage = 3;

    /// <summary>The stage of the pre-base forms: the glyph it makes is the one that is moved before the base.</summary>
    public const int PreBaseStage = 4;

    /// <summary>The stage after which the clusters are reordered: the orthographic unit shaping group.</summary>
    public const int ReorderingStage = 5;

    /// <summary>The features of the model, in the order of the specification.</summary>
    public static ShaperPlan Plan { get; } = new(
        [
            new(ShapingFeature.ToTag("locl"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("ccmp"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("nukt"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("akhn"), 2, Basic),
            new(ShapingFeature.ToTag("rphf"), 3, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("isol"), 6, ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("init"), 6, ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("medi"), 6, ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("fina"), 6, ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("pref"), 4, Basic),
            new(ShapingFeature.ToTag("rkrf"), 5, Basic),
            new(ShapingFeature.ToTag("abvf"), 5, Basic),
            new(ShapingFeature.ToTag("blwf"), 5, Basic),
            new(ShapingFeature.ToTag("half"), 5, Basic),
            new(ShapingFeature.ToTag("pstf"), 5, Basic),
            new(ShapingFeature.ToTag("vatu"), 5, Basic),
            new(ShapingFeature.ToTag("cjct"), 5, Basic),
            new(ShapingFeature.ToTag("abvs"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("blws"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("haln"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("pres"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
            new(ShapingFeature.ToTag("psts"), 7, ShaperFeatureFlags.ManualZeroWidthJoiner),
        ],
        finalStage: 7);

    private static readonly Dictionary<string, ulong> PatternCategories = new(StringComparer.Ordinal)
    {
        ["O"] = 1UL << O,
        ["B"] = 1UL << B,
        ["N"] = 1UL << N,
        ["GB"] = 1UL << GB,
        ["SUB"] = 1UL << SUB,
        ["H"] = 1UL << H,
        ["HN"] = 1UL << HN,
        ["ZWNJ"] = 1UL << ZWNJ,
        ["R"] = 1UL << R,
        ["CS"] = 1UL << CS,
        ["IS"] = 1UL << IS,
        ["Sk"] = 1UL << Sk,
        ["G"] = 1UL << G,
        ["J"] = 1UL << J,
        ["SB"] = 1UL << SB,
        ["SE"] = 1UL << SE,
        ["HVM"] = 1UL << HVM,
        ["HM"] = 1UL << HM,
        ["HR"] = 1UL << HR,
        ["RK"] = 1UL << RK,
        ["FAbv"] = 1UL << FAbv,
        ["FBlw"] = 1UL << FBlw,
        ["FPst"] = 1UL << FPst,
        ["FMAbv"] = 1UL << FMAbv,
        ["FMBlw"] = 1UL << FMBlw,
        ["FMPst"] = 1UL << FMPst,
        ["MAbv"] = 1UL << MAbv,
        ["MBlw"] = 1UL << MBlw,
        ["MPst"] = 1UL << MPst,
        ["MPre"] = 1UL << MPre,
        ["CMAbv"] = 1UL << CMAbv,
        ["CMBlw"] = 1UL << CMBlw,
        ["VAbv"] = 1UL << VAbv,
        ["VBlw"] = 1UL << VBlw,
        ["VPst"] = 1UL << VPst,
        ["VPre"] = 1UL << VPre,
        ["VMAbv"] = 1UL << VMAbv,
        ["VMBlw"] = 1UL << VMBlw,
        ["VMPst"] = 1UL << VMPst,
        ["VMPre"] = 1UL << VMPre,
        ["SMAbv"] = 1UL << SMAbv,
        ["SMBlw"] = 1UL << SMBlw,
    };

    // The cluster schemas of the specification. A base, with a reph or a consonant with stacker before it; the
    // consonant modifiers and the consonants joined by a halant or subjoined; one medial consonant, the vowels, the
    // vowel modifiers and the final consonants of each place, in the order above, below, after; a final modifier.
    private static readonly Dictionary<string, string> PatternParts = new(StringComparer.Ordinal)
    {
        ["h"] = "H | HVM | IS | Sk",
        ["consonant_modifiers"] = "CMAbv* CMBlw* ((h B | SUB) CMAbv* CMBlw*)*",
        ["medial_consonants"] = "MPre? MAbv? MBlw? MPst?",
        ["dependent_vowels"] = "VPre* VAbv* VBlw* VPst* | H",
        ["vowel_modifiers"] = "HVM? VMPre* VMAbv* VMBlw* VMPst*",
        ["final_consonants"] = "FAbv* FBlw* FPst*",
        ["final_modifiers"] = "FMAbv* FMBlw* | FMPst?",
        ["complex_syllable_start"] = "(R | CS)? (B | GB)",
        ["complex_syllable_middle"] = "consonant_modifiers medial_consonants (dependent_vowels) vowel_modifiers (Sk B)*",
        ["complex_syllable_tail"] = "complex_syllable_middle final_consonants (final_modifiers)",
        ["number_joiner_terminated_cluster_tail"] = "(HN N)* HN",
        ["numeral_cluster_tail"] = "(HN N)+",
        ["symbol_cluster_tail"] = "SMAbv+ SMBlw* | SMBlw+",
        ["virama_terminated_cluster_tail"] = "consonant_modifiers (IS | RK)",
        ["sakot_terminated_cluster_tail"] = "complex_syllable_middle Sk",
        ["tail"] = "(complex_syllable_tail) | (sakot_terminated_cluster_tail) | (symbol_cluster_tail) | (virama_terminated_cluster_tail)",
    };

    // The kinds of clusters, in the order of their constants. A zero width non-joiner ends the cluster it follows.
    private static readonly SyllablePattern[] Patterns =
    [
        SyllablePattern.Compile("complex_syllable_start (virama_terminated_cluster_tail) ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("complex_syllable_start (sakot_terminated_cluster_tail) ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("complex_syllable_start (complex_syllable_tail) ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("N (number_joiner_terminated_cluster_tail) ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("N (numeral_cluster_tail)? ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("(O | GB | SB) (tail)? ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("SB* G HR? HM? SE* (J SB* (G HR? HM? SE*)?)* ZWNJ?", PatternCategories, PatternParts),
        SyllablePattern.Compile("R? ((tail) | (number_joiner_terminated_cluster_tail) | (numeral_cluster_tail)) ZWNJ?", PatternCategories, PatternParts),
    ];

    private static readonly int MaxStateCount = Patterns.Max(pattern => pattern.StateCount);

    /// <summary>Determines whether a script is shaped with the universal model: "Registered tags for the Universal Shaping Engine".</summary>
    public static bool IsUniversalScript(UnicodeScript script)
    {
        return script is UnicodeScript.Adlam or UnicodeScript.Ahom or UnicodeScript.Balinese or UnicodeScript.Batak or UnicodeScript.BeriaErfe
            or UnicodeScript.Bhaiksuki or UnicodeScript.Brahmi or UnicodeScript.Buginese or UnicodeScript.Buhid or UnicodeScript.Chakma
            or UnicodeScript.Cham or UnicodeScript.Chorasmian or UnicodeScript.CyproMinoan or UnicodeScript.DivesAkuru or UnicodeScript.Dogra
            or UnicodeScript.Duployan or UnicodeScript.EgyptianHieroglyphs or UnicodeScript.Elymaic or UnicodeScript.Garay or UnicodeScript.Grantha
            or UnicodeScript.GunjalaGondi or UnicodeScript.GurungKhema or UnicodeScript.HanifiRohingya or UnicodeScript.Hanunoo
            or UnicodeScript.Javanese or UnicodeScript.Jurchen or UnicodeScript.Kaithi or UnicodeScript.Kawi or UnicodeScript.KayahLi
            or UnicodeScript.Kharoshthi or UnicodeScript.KhitanSmallScript or UnicodeScript.Khojki or UnicodeScript.Khudawadi
            or UnicodeScript.KiratRai or UnicodeScript.Lepcha or UnicodeScript.Limbu or UnicodeScript.Mahajani or UnicodeScript.Makasar
            or UnicodeScript.Mandaic or UnicodeScript.Manichaean or UnicodeScript.Marchen or UnicodeScript.MasaramGondi
            or UnicodeScript.Medefaidrin or UnicodeScript.MeeteiMayek or UnicodeScript.Miao or UnicodeScript.Modi or UnicodeScript.Mongolian
            or UnicodeScript.Multani or UnicodeScript.NagMundari or UnicodeScript.Nandinagari or UnicodeScript.Newa or UnicodeScript.Nko
            or UnicodeScript.NyiakengPuachueHmong or UnicodeScript.OldSogdian or UnicodeScript.OldUyghur or UnicodeScript.OlOnal
            or UnicodeScript.PahawhHmong or UnicodeScript.PhagsPa or UnicodeScript.ProtoCuneiform or UnicodeScript.PsalterPahlavi
            or UnicodeScript.Rejang or UnicodeScript.Saurashtra or UnicodeScript.Seal or UnicodeScript.Sharada or UnicodeScript.Siddham
            or UnicodeScript.Sidetic or UnicodeScript.Sinhala or UnicodeScript.Sogdian or UnicodeScript.Soyombo or UnicodeScript.Sunuwar
            or UnicodeScript.Sundanese or UnicodeScript.SylotiNagri or UnicodeScript.Tagalog or UnicodeScript.Tagbanwa or UnicodeScript.TaiLe
            or UnicodeScript.TaiTham or UnicodeScript.TaiViet or UnicodeScript.TaiYo or UnicodeScript.Takri or UnicodeScript.Tangsa
            or UnicodeScript.Tibetan or UnicodeScript.Tifinagh or UnicodeScript.Tirhuta or UnicodeScript.Todhri or UnicodeScript.TolongSiki
            or UnicodeScript.Toto or UnicodeScript.TuluTigalari or UnicodeScript.Vithkuqi or UnicodeScript.Wancho or UnicodeScript.Yezidi
            or UnicodeScript.ZanabazarSquare;
    }

    /// <summary>
    /// Determines whether the letters of a script join: the scripts that have letters with a joining type in
    /// <c>ArabicShaping.txt</c>.
    /// </summary>
    private static bool HasJoiningLetters(UnicodeScript script)
    {
        return script is UnicodeScript.Adlam or UnicodeScript.Arabic or UnicodeScript.Chorasmian or UnicodeScript.HanifiRohingya
            or UnicodeScript.Mandaic or UnicodeScript.Manichaean or UnicodeScript.Mongolian or UnicodeScript.Nko or UnicodeScript.OldUyghur
            or UnicodeScript.PhagsPa or UnicodeScript.PsalterPahlavi or UnicodeScript.Sogdian or UnicodeScript.Syriac;
    }

    /// <summary>Gets the class of a character: "Character classification".</summary>
    private static byte GetCategory(int codePoint)
    {
        // The classes of the hieroglyphs: the "extended syllabic category" of the specification.
        switch (codePoint)
        {
            case (>= 0x13000 and <= 0x1342F) or (>= 0x1343C and <= 0x1343F) or (>= 0x13441 and <= 0x13446) or (>= 0x13460 and <= 0x143FA):
                return G;

            case (>= 0x13430 and <= 0x13436) or (>= 0x13439 and <= 0x1343B):
                return J;

            case 0x005B or 0x007B or 0x27E6 or 0x27E8 or 0x2E22 or 0x2E24 or 0x13437:
                return SB;

            case 0x005D or 0x007D or 0x27E7 or 0x27E9 or 0x2E23 or 0x2E25 or 0x13438:
                return SE;

            case 0x13440:
                return HR;

            case >= 0x13447 and <= 0x13455:
                return HM;

            // The Sinhala al-lakuna is a halant that can also follow a vowel; the sakot of Tai Tham stacks a consonant
            // after the vowels as well as before them.
            case 0x0DCA:
                return HVM;

            case 0x1A60:
                return Sk;

            // "Symbol_Modifier": the marks of the Balinese musical symbols.
            case >= 0x1B6B and <= 0x1B73:
                return GetPositionalCategory(codePoint) == UnicodeIndicPositionalCategory.Bottom ? SMBlw : SMAbv;

            // "Consonant_Final_Modifier": the Lepcha sign ran.
            case 0x1C36:
                return FMAbv;
        }

        var general = UnicodeProperties.GetGeneralCategory(codePoint);
        var isLetter = general == UnicodeCategory.OtherLetter;
        var isMark = general is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
        var syllabic = GetSyllabicCategory(codePoint);
        if (syllabic == UnicodeIndicSyllabicCategory.NonJoiner)
            return ZWNJ;

        // The joiner, the combining grapheme joiner and the variation selectors are of the cluster they are in, at
        // any place of it.
        if (syllabic == UnicodeIndicSyllabicCategory.Joiner || (isMark && UnicodeProperties.IsDefaultIgnorable(codePoint)))
            return CGJ;

        var position = GetPositionalCategory(codePoint);
        switch (syllabic)
        {
            case UnicodeIndicSyllabicCategory.Number or UnicodeIndicSyllabicCategory.Consonant or UnicodeIndicSyllabicCategory.ConsonantHeadLetter
                or UnicodeIndicSyllabicCategory.ToneLetter or UnicodeIndicSyllabicCategory.VowelIndependent:
                return B;

            case UnicodeIndicSyllabicCategory.BrahmiJoiningNumber:
                return N;

            case UnicodeIndicSyllabicCategory.ConsonantPlaceholder:
                return GB;

            case UnicodeIndicSyllabicCategory.ConsonantWithStacker:
                return CS;

            case UnicodeIndicSyllabicCategory.ConsonantSucceedingRepha:
                return GetFinal(position);

            case UnicodeIndicSyllabicCategory.ConsonantFinal:
                return isLetter ? B : GetFinal(position);

            case UnicodeIndicSyllabicCategory.SyllableModifier:
                return position switch
                {
                    UnicodeIndicPositionalCategory.Top => FMAbv,
                    UnicodeIndicPositionalCategory.Bottom => FMBlw,
                    _ => FMPst,
                };

            case UnicodeIndicSyllabicCategory.ConsonantMedial:
                return isLetter ? B : GetMedial(position);

            case UnicodeIndicSyllabicCategory.ConsonantInitialPostfixed:
                return GetMedial(position);

            case UnicodeIndicSyllabicCategory.Nukta or UnicodeIndicSyllabicCategory.GeminationMark or UnicodeIndicSyllabicCategory.ConsonantKiller:
                return position == UnicodeIndicPositionalCategory.Top ? CMAbv : CMBlw;

            case UnicodeIndicSyllabicCategory.ConsonantSubjoined:
                return isLetter ? B : SUB;

            case UnicodeIndicSyllabicCategory.Virama:
                return H;

            case UnicodeIndicSyllabicCategory.InvisibleStacker:
                return IS;

            case UnicodeIndicSyllabicCategory.NumberJoiner:
                return HN;

            case UnicodeIndicSyllabicCategory.ConsonantPrecedingRepha or UnicodeIndicSyllabicCategory.ConsonantPrefixed:
                return R;

            case UnicodeIndicSyllabicCategory.ReorderingKiller:
                return RK;

            case UnicodeIndicSyllabicCategory.PureKiller:
                return GetVowel(position);

            case UnicodeIndicSyllabicCategory.Vowel or UnicodeIndicSyllabicCategory.VowelDependent:
                return isLetter ? B : GetVowel(position);

            case UnicodeIndicSyllabicCategory.ToneMark or UnicodeIndicSyllabicCategory.CantillationMark or UnicodeIndicSyllabicCategory.RegisterShifter
                or UnicodeIndicSyllabicCategory.Visarga:
                return GetVowelModifier(position);

            case UnicodeIndicSyllabicCategory.Bindu:
                return isLetter ? B : GetVowelModifier(position);

            case UnicodeIndicSyllabicCategory.Avagraha:
                return isLetter ? B : O;
        }

        // The generic bases that are not consonant placeholders.
        if (codePoint is 0x2015 or 0x2022 or (>= 0x25FB and <= 0x25FE))
            return GB;

        // The letters of the scripts that join are bases.
        if (UnicodeProperties.GetJoiningType(codePoint) is UnicodeJoiningType.JoinCausing or UnicodeJoiningType.DualJoining or UnicodeJoiningType.LeftJoining or UnicodeJoiningType.RightJoining)
            return B;

        // The characters without a visible form are not of a cluster. A code point that is no character yet is taken
        // as a symbol, which marks can follow: it can be a character of a later version of Unicode.
        if (UnicodeProperties.IsDefaultIgnorable(codePoint) && codePoint is not (0x115F or 0x1160 or 0x3164 or 0xFFA0 or (>= 0x1BCA0 and <= 0x1BCA3)))
            return WJ;

        return O;
    }

    private static byte GetFinal(UnicodeIndicPositionalCategory position)
    {
        return position switch
        {
            UnicodeIndicPositionalCategory.Top => FAbv,
            UnicodeIndicPositionalCategory.Bottom => FBlw,
            _ => FPst,
        };
    }

    private static byte GetMedial(UnicodeIndicPositionalCategory position)
    {
        return position switch
        {
            UnicodeIndicPositionalCategory.Top => MAbv,
            UnicodeIndicPositionalCategory.Bottom or UnicodeIndicPositionalCategory.BottomAndLeft or UnicodeIndicPositionalCategory.BottomAndRight => MBlw,
            UnicodeIndicPositionalCategory.Left or UnicodeIndicPositionalCategory.TopAndBottomAndLeft => MPre,
            _ => MPst,
        };
    }

    // A vowel that has several parts has the class of its first part: "the validation schemas only take into account
    // the cardinal positions".
    private static byte GetVowel(UnicodeIndicPositionalCategory position)
    {
        return position switch
        {
            UnicodeIndicPositionalCategory.Top or UnicodeIndicPositionalCategory.TopAndBottom or UnicodeIndicPositionalCategory.TopAndBottomAndRight
                or UnicodeIndicPositionalCategory.TopAndRight => VAbv,
            UnicodeIndicPositionalCategory.Bottom or UnicodeIndicPositionalCategory.Overstruck or UnicodeIndicPositionalCategory.BottomAndRight => VBlw,
            UnicodeIndicPositionalCategory.Left or UnicodeIndicPositionalCategory.TopAndLeft or UnicodeIndicPositionalCategory.TopAndLeftAndRight
                or UnicodeIndicPositionalCategory.LeftAndRight => VPre,
            _ => VPst,
        };
    }

    private static byte GetVowelModifier(UnicodeIndicPositionalCategory position)
    {
        return position switch
        {
            UnicodeIndicPositionalCategory.Top => VMAbv,
            UnicodeIndicPositionalCategory.Bottom or UnicodeIndicPositionalCategory.Overstruck => VMBlw,
            UnicodeIndicPositionalCategory.Left => VMPre,
            _ => VMPst,
        };
    }

    /// <summary>
    /// Gets the syllabic category of a character: the property of Unicode, and the values of
    /// <c>IndicSyllabicCategory-Additional.txt</c> for the characters the property does not describe, or not as shaping
    /// needs it.
    /// </summary>
    private static UnicodeIndicSyllabicCategory GetSyllabicCategory(int codePoint)
    {
        switch (codePoint)
        {
            case 0x193A or 0xAA29 or 0x10A0D or 0x113CE:
                return UnicodeIndicSyllabicCategory.Bindu;

            // The dotted circle is a generic base; the final letters of New Tai Lue do not form a cluster with a
            // consonant.
            case (>= 0x19C1 and <= 0x19C7) or 0x25CC:
                return UnicodeIndicSyllabicCategory.Consonant;

            case 0x0F7F:
                return UnicodeIndicSyllabicCategory.ConsonantDead;

            case >= 0x11A3B and <= 0x11A3E:
                return UnicodeIndicSyllabicCategory.ConsonantSubjoined;

            case 0x11134 or 0x10D27:
                return UnicodeIndicSyllabicCategory.GeminationMark;

            case 0x0F71 or 0x113CF or (>= 0x0859 and <= 0x085B) or 0x0F39 or 0x1885 or 0x1886 or 0x18A9 or 0x10AE5 or 0x10AE6 or 0x16F4F
                or 0x1BC9D or 0x1BC9E or (>= 0x1E944 and <= 0x1E94A) or (>= 0x10F82 and <= 0x10F85):
                return UnicodeIndicSyllabicCategory.Nukta;

            case 0x1A7B or 0x1A7C or 0x1A7F or (>= 0x07EB and <= 0x07F3) or 0x07FD or 0x0F86 or 0x0F87 or 0x17CF or (>= 0x10D24 and <= 0x10D26)
                or (>= 0x10F46 and <= 0x10F50) or (>= 0x16B30 and <= 0x16B36) or (>= 0x16F8F and <= 0x16F92) or (>= 0x1E130 and <= 0x1E136)
                or 0x1E2AE or (>= 0x1E2EC and <= 0x1E2EF):
                return UnicodeIndicSyllabicCategory.ToneMark;

            case 0xAAB1 or 0xAABA or 0xAABD:
                return UnicodeIndicSyllabicCategory.VowelIndependent;

            // The letters of the scripts the property does not cover.
            case (>= 0x0800 and <= 0x0815) or (>= 0x0840 and <= 0x0858) or 0x0F00 or 0x0F01 or (>= 0x0F04 and <= 0x0F06)
                or 0x1800 or 0x1807 or 0x180A or (>= 0x1820 and <= 0x1878) or (>= 0x2D30 and <= 0x2D67) or 0x2D6F
                or (>= 0x10570 and <= 0x1057A) or (>= 0x1057C and <= 0x1058A) or (>= 0x1058C and <= 0x10592) or 0x10594 or 0x10595
                or (>= 0x10597 and <= 0x105A1) or (>= 0x105A3 and <= 0x105B1) or (>= 0x105B3 and <= 0x105B9) or 0x105BB or 0x105BC
                or (>= 0x10AC0 and <= 0x10AC7) or (>= 0x10AC9 and <= 0x10AE4) or (>= 0x10D00 and <= 0x10D23) or (>= 0x10D4A and <= 0x10D65)
                or (>= 0x10D6F and <= 0x10D85) or (>= 0x10E80 and <= 0x10EA9) or 0x10EB0 or 0x10EB1 or (>= 0x10F30 and <= 0x10F45)
                or (>= 0x10F70 and <= 0x10F81) or 0x111DA or (>= 0x16B00 and <= 0x16B2F) or (>= 0x16F00 and <= 0x16F4A) or 0x16FE4
                or (>= 0x18B00 and <= 0x18CD5) or 0x18CFF or (>= 0x1BC00 and <= 0x1BC6A) or (>= 0x1BC70 and <= 0x1BC7C)
                or (>= 0x1BC80 and <= 0x1BC88) or (>= 0x1BC90 and <= 0x1BC99) or (>= 0x1E100 and <= 0x1E12C) or (>= 0x1E137 and <= 0x1E13D)
                or 0x1E14E or 0x1E14F or (>= 0x1E290 and <= 0x1E2AD) or (>= 0x1E2C0 and <= 0x1E2EB) or (>= 0x1E4D0 and <= 0x1E4EB)
                or (>= 0x1E5D0 and <= 0x1E5ED) or 0x1E5F0 or (>= 0x1E900 and <= 0x1E943) or 0x1E94B:
                return UnicodeIndicSyllabicCategory.Consonant;

            case >= 0x1880 and <= 0x1884:
                return UnicodeIndicSyllabicCategory.ConsonantPlaceholder;

            case 0x16F50:
                return UnicodeIndicSyllabicCategory.ModifyingLetter;

            case (>= 0x10D30 and <= 0x10D39) or (>= 0x10F51 and <= 0x10F54) or (>= 0x16AC0 and <= 0x16AC9) or (>= 0x1E140 and <= 0x1E149)
                or (>= 0x1E2F0 and <= 0x1E2F9) or (>= 0x1E4F0 and <= 0x1E4F9) or (>= 0x1E5F1 and <= 0x1E5FA) or (>= 0x1E950 and <= 0x1E959):
                return UnicodeIndicSyllabicCategory.Number;

            case 0x2D7F:
                return UnicodeIndicSyllabicCategory.Virama;

            case 0x0B55 or (>= 0x10D69 and <= 0x10D6D) or 0x10EAB or 0x10EAC or (>= 0x16F51 and <= 0x16F87) or (>= 0x1E4EC and <= 0x1E4EF)
                or 0x1E5EE or 0x1E5EF:
                return UnicodeIndicSyllabicCategory.VowelDependent;

            // The Vedic signs written over or under a visarga are marks of the cluster, as the other Vedic tone marks.
            case 0x1CF8 or 0x1CF9 or (>= 0x1CE2 and <= 0x1CE8):
                return UnicodeIndicSyllabicCategory.CantillationMark;

            case 0x1CED:
                return UnicodeIndicSyllabicCategory.ToneMark;

            // The astrological signs of Tibetan are written after a digit or a letter, as its vowel signs are.
            case 0x0F18 or 0x0F19 or 0x0F3E or 0x0F3F:
                return UnicodeIndicSyllabicCategory.VowelDependent;

            default:
                return UnicodeProperties.GetIndicSyllabicCategory(codePoint);
        }
    }

    /// <summary>
    /// Gets the positional category of a character: the property of Unicode, and the values of
    /// <c>IndicPositionalCategory-Additional.txt</c>.
    /// </summary>
    private static UnicodeIndicPositionalCategory GetPositionalCategory(int codePoint)
    {
        switch (codePoint)
        {
            case 0x0F72 or (>= 0x0F7A and <= 0x0F7D) or 0x0F80 or 0xA9BF or 0x10A38 or (>= 0x11127 and <= 0x11129) or 0x1112D or 0x11130
                or 0x1BF2 or 0x1BF3 or 0xAAB0 or 0xAAB2 or 0xAAB3 or 0xAAB7 or 0xAAB8 or 0xAABE
                or (>= 0x0859 and <= 0x085B) or 0x18A9 or 0x10AE5 or 0x10AE6 or (>= 0x10F46 and <= 0x10F50) or (>= 0x10F82 and <= 0x10F85)
                or 0x113CF or 0x16F4F or (>= 0x16F51 and <= 0x16F87) or (>= 0x16F8F and <= 0x16F92) or 0x1E5EE or 0x1E5EF:
                return UnicodeIndicPositionalCategory.Bottom;

            case 0x1C29 or 0x103C:
                return UnicodeIndicPositionalCategory.Left;

            case 0xA9BE or 0x10A0C or 0x11942:
                return UnicodeIndicPositionalCategory.Right;

            case 0x0F74 or 0x1A18 or 0xAA35 or 0xAAB4 or 0x1112A or 0x1112B or 0x11131 or 0x11132 or (>= 0x1E4EC and <= 0x1E4EF)
                or (>= 0x07EB and <= 0x07F3) or 0x07FD or 0x1885 or 0x1886 or 0x1B6C or 0x1CF8 or 0x1CF9 or (>= 0x10D24 and <= 0x10D27)
                or 0x10EAB or 0x10EAC or (>= 0x10D69 and <= 0x10D6D) or (>= 0x16B30 and <= 0x16B36) or (>= 0x1E130 and <= 0x1E136)
                or 0x1E2AE or (>= 0x1E2EC and <= 0x1E2EF) or (>= 0x1E944 and <= 0x1E94A):
                return UnicodeIndicPositionalCategory.Top;

            // The anusvara and the visarga of Grantha and the anusvara of Tirhuta are written with the marks above,
            // before the Vedic marks that follow them.
            case 0x11302 or 0x11303 or 0x114C1:
                return UnicodeIndicPositionalCategory.Top;

            case 0x0E33 or 0x0EB3:
                return UnicodeIndicPositionalCategory.TopAndRight;

            case 0x1BC9D or 0x1BC9E:
                return UnicodeIndicPositionalCategory.Overstruck;

            case (>= 0x180B and <= 0x180D) or 0x180F or 0x2D7F:
                return UnicodeIndicPositionalCategory.NotApplicable;

            default:
                return UnicodeProperties.GetIndicPositionalCategory(codePoint);
        }
    }

    /// <summary>Gives each character its class and cuts the text into clusters: "Cluster validation".</summary>
    public static void FindSyllables(Span<GlyphInfo> info)
    {
        foreach (ref var glyph in info)
        {
            glyph.SyllableCategory = GetCategory(glyph.CodePoint);
            glyph.Syllable = 0;
        }

        // The characters that are of a cluster at any place of it are not seen by the schemas: the joiner, the
        // combining grapheme joiner, the variation selectors; and the non-joiner before a mark, which "continues a
        // preceding cluster but causes a cluster break after itself when the following character is not a mark".
        var categories = ArrayPool<byte>.Shared.Rent(info.Length);
        var indexes = ArrayPool<int>.Shared.Rent(info.Length + 1);
        try
        {
            var count = 0;
            for (var i = 0; i < info.Length; i++)
            {
                var category = info[i].SyllableCategory;
                if (category == CGJ || (category == ZWNJ && IsFollowedByMark(info, i)))
                    continue;

                categories[count] = category;
                indexes[count++] = i;
            }

            indexes[count] = info.Length;
            var seen = categories.AsSpan(0, count);
            Span<int> states = stackalloc int[MaxStateCount * 2];
            var serial = 0;
            var start = 0;
            while (start < count)
            {
                var length = 0;
                var kind = NonCluster;
                for (var i = 0; i < Patterns.Length; i++)
                {
                    // A final modifier that is drawn after the cluster is, alone, not a cluster without its base.
                    if (i == BrokenCluster && length == 0 && seen[start] == FMPst)
                    {
                        length = 1;
                    }

                    var matched = Patterns[i].Match(seen, start, states);
                    if (matched > length)
                    {
                        length = matched;
                        kind = i;
                    }
                }

                if (length == 0)
                {
                    length = 1;
                    kind = NonCluster;
                }

                serial = serial == 15 ? 1 : serial + 1;
                for (var i = indexes[start]; i < indexes[start + length]; i++)
                {
                    info[i].Syllable = (byte)((serial << 4) | kind);
                }

                start += length;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(categories);
            ArrayPool<int>.Shared.Return(indexes);
        }
    }

    private static bool IsFollowedByMark(ReadOnlySpan<GlyphInfo> info, int index)
    {
        for (var i = index + 1; i < info.Length; i++)
        {
            if (info[i].SyllableCategory != CGJ)
                return info[i].Category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
        }

        return false;
    }

    /// <summary>
    /// Gives the glyphs the bits of the features that apply to a part of them: the reph at the start of a cluster, and
    /// the topographical features.
    /// </summary>
    /// <param name="info">The characters of the run, cut into clusters.</param>
    /// <param name="firstBit">The bit of the first feature of the plan in the masks of the glyphs.</param>
    /// <param name="script">The script of the run.</param>
    public static void SetMasks(Span<GlyphInfo> info, int firstBit, UnicodeScript script)
    {
        // "Feature application is scoped to the first three glyphs in a cluster."
        for (var start = 0; start < info.Length;)
        {
            var end = GetSyllableEnd(info, start);
            var limit = info[start].SyllableCategory == R ? 1 : Math.Min(3, end - start);
            for (var i = start; i < start + limit; i++)
            {
                info[i].Mask |= 1u << (firstBit + Rphf);
            }

            start = end;
        }

        if (HasJoiningLetters(script))
        {
            ArabicShaper.SetFormMasks(info, [firstBit + Isol, firstBit + Fina, -1, -1, firstBit + Medi, -1, firstBit + Init]);

            // A free variation selector of Mongolian is of the form of its letter: the lookups of the form see both.
            var forms = (1u << (firstBit + Isol)) | (1u << (firstBit + Init)) | (1u << (firstBit + Medi)) | (1u << (firstBit + Fina));
            for (var i = 1; i < info.Length; i++)
            {
                if (info[i].CodePoint is (>= 0x180B and <= 0x180D) or 0x180F)
                {
                    info[i].Mask = (info[i].Mask & ~forms) | (info[i - 1].Mask & forms);
                }
            }

            return;
        }

        // "USE applies these as required features on a per cluster basis": a cluster next to another one is at the
        // start, in the middle or at the end of a sequence of clusters. The specification leaves what separates two
        // sequences to be specified for the scripts that do not join; a symbol (a space, a digit, a punctuation mark)
        // is a cluster like another one here, as it is for the reference shaper, the one fonts are tested with.
        var lastStart = 0;
        var lastForm = -1;
        for (var start = 0; start < info.Length;)
        {
            var end = GetSyllableEnd(info, start);
            var kind = info[start].Syllable & 0x0F;
            if (kind is HieroglyphCluster or NonCluster)
            {
                lastForm = -1;
            }
            else
            {
                var joins = lastForm is Fina or Isol;
                if (joins)
                {
                    lastForm = lastForm == Fina ? Medi : Init;
                    SetForm(info[lastStart..start], firstBit, lastForm);
                }

                lastForm = joins ? Fina : Isol;
                SetForm(info[start..end], firstBit, lastForm);
            }

            lastStart = start;
            start = end;
        }
    }

    private static void SetForm(Span<GlyphInfo> info, int firstBit, int form)
    {
        var forms = (1u << (firstBit + Isol)) | (1u << (firstBit + Init)) | (1u << (firstBit + Medi)) | (1u << (firstBit + Fina));
        foreach (ref var glyph in info)
        {
            glyph.Mask = (glyph.Mask & ~forms) | (1u << (firstBit + form));
        }
    }

    private static int GetSyllableEnd(ReadOnlySpan<GlyphInfo> info, int start)
    {
        var end = start + 1;
        while (end < info.Length && info[end].Syllable == info[start].Syllable)
        {
            end++;
        }

        return end;
    }

    /// <summary>Forgets which glyphs a feature made, before a feature whose glyphs are to be told from the others.</summary>
    public static void ClearSubstitutions(Span<GlyphInfo> info)
    {
        foreach (ref var glyph in info)
        {
            glyph.Flags &= unchecked((byte)~GlyphInfo.Substituted);
        }
    }

    /// <summary>
    /// After <c>rphf</c>: "the output of this feature reorders after the following full base". The glyph the feature
    /// made is a reph.
    /// </summary>
    public static void RecordReph(Span<GlyphInfo> info, int firstBit)
    {
        var mask = 1u << (firstBit + Rphf);
        for (var start = 0; start < info.Length;)
        {
            var end = GetSyllableEnd(info, start);
            for (var i = start; i < end && (info[i].Mask & mask) != 0; i++)
            {
                if ((info[i].Flags & GlyphInfo.Substituted) != 0)
                {
                    info[i].SyllableCategory = R;
                    break;
                }
            }

            start = end;
        }
    }

    /// <summary>
    /// After <c>pref</c>: "cases which require reordering should use the 'pref' feature to identify the glyph [...]
    /// Only one such glyph is reordered per cluster". The glyph the feature made is moved as a vowel drawn before the
    /// base is.
    /// </summary>
    public static void RecordPreBaseForm(Span<GlyphInfo> info)
    {
        for (var start = 0; start < info.Length;)
        {
            var end = GetSyllableEnd(info, start);
            for (var i = start; i < end; i++)
            {
                if ((info[i].Flags & GlyphInfo.Substituted) != 0)
                {
                    info[i].SyllableCategory = VPre;
                    break;
                }
            }

            start = end;
        }
    }

    /// <summary>Reorders the clusters: "Glyph reordering".</summary>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void Reorder(GlyphBuffer buffer, FontFace face)
    {
        InsertDottedCircles(buffer, face);
        var info = buffer.Info;
        for (var start = 0; start < info.Length;)
        {
            var end = GetSyllableEnd(info, start);
            if ((info[start].Syllable & 0x0F) is ViramaTerminatedCluster or SakotTerminatedCluster or StandardCluster or SymbolCluster or BrokenCluster)
            {
                ReorderSyllable(buffer, start, end);
            }

            start = end;
        }
    }

    // "When a cluster starts with any character that has UGC=Mc or UGC=Mn, USE inserts a dotted circle glyph (U+25CC)
    // to indicate a broken cluster": the circle is the base of the cluster, after its reph.
    private static void InsertDottedCircles(GlyphBuffer buffer, FontFace face)
    {
        var circle = face.MapCodePoint(0x25CC);
        if (circle == 0)
            return;

        var lastSyllable = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            // A cluster is told from the one before it: the numbers of the clusters come back after fifteen.
            var syllable = buffer.Info[i].Syllable;
            if (syllable == lastSyllable || (syllable & 0x0F) != BrokenCluster)
            {
                lastSyllable = syllable;
                continue;
            }

            lastSyllable = syllable;
            var template = buffer.Info[i];
            while (i < buffer.Length && buffer.Info[i].Syllable == syllable && buffer.Info[i].SyllableCategory == R)
            {
                i++;
            }

            buffer.Resize(i, 0, 1);
            buffer.Info[i] = new GlyphInfo
            {
                Glyph = circle,
                CodePoint = 0x25CC,
                Cluster = template.Cluster,
                Mask = template.Mask,
                Syllable = syllable,
                SyllableCategory = B,
                Category = UnicodeCategory.OtherSymbol,
            };
        }
    }

    private static void ReorderSyllable(GlyphBuffer buffer, int start, int end)
    {
        var info = buffer.Info;

        // The reph goes after the base: before the first glyph that follows the base, or a halant that is still
        // there, and at the end of a cluster that has neither.
        if (info[start].SyllableCategory == R && end - start > 1)
        {
            for (var i = start + 1; i < end; i++)
            {
                var isPostBase = ((1UL << info[i].SyllableCategory) & PostBaseClasses) != 0 || IsHalant(info[i]);
                if (!isPostBase && i != end - 1)
                    continue;

                var target = isPostBase ? i - 1 : i;
                buffer.MergeClusters(start, target + 1);
                var reph = info[start];
                info[(start + 1)..(target + 1)].CopyTo(info[start..]);
                info[target] = reph;
                break;
            }
        }

        // What is drawn before the base goes to the start of the cluster: "reordering components do not reorder past
        // an explicit virama".
        var first = start;
        for (var i = start; i < end; i++)
        {
            if (IsHalant(info[i]))
            {
                first = i + 1;
            }
            else if (info[i].SyllableCategory is VPre or VMPre && IsFirstOfItsSequence(info[i]) && first < i)
            {
                buffer.MergeClusters(first, i + 1);
                var moved = info[i];
                info[first..i].CopyTo(info[(first + 1)..]);
                info[first] = moved;
            }
        }
    }

    // A halant a ligature was made with is no longer there.
    private static bool IsHalant(in GlyphInfo info) => info.SyllableCategory is H or HVM or IS && (info.Props & GlyphInfo.Ligated) == 0;

    // Of the glyphs a substitution put in place of one, the first is the one that moves.
    private static bool IsFirstOfItsSequence(in GlyphInfo info)
    {
        if (info.LigatureId != 0)
            return (info.Props & GlyphInfo.LigatureWithComponents) != 0 || info.LigatureComponent == 0;

        return (info.Props & GlyphInfo.Multiplied) != 0 ? info.LigatureComponent <= 1 : info.LigatureComponent == 0;
    }
}
