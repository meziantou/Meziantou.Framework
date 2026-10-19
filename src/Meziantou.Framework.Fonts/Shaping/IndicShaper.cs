using static Meziantou.Framework.Fonts.IndicCategories;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The shaping model of the Indic scripts: Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada and
/// Malayalam.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft, "Developing OpenType Fonts for Devanagari Script" and the specifications of the eight other scripts,
/// which describe the same engine. The steps are the ones of these documents, and the comments quote their names:
/// the text is cut into syllables; in each syllable the base consonant is found, the reph and the consonants around
/// the base are identified from what the font does with them (the "dynamic properties"), and the characters are
/// reordered; the basic shaping features are applied one at a time, each to the part of the syllable it is for; the
/// syllable is reordered again from what these features produced; the presentation features are applied.
/// </para>
/// <para>
/// The specifications leave much to the implementation: what a syllable is when the text is not a well-formed one,
/// what happens to the marks around a consonant that moves, how a font made for the first version of the
/// specifications ("old behavior") differs. There, the engine does what the reference shaper does, because fonts are
/// made and tested with it.
/// </para>
/// </remarks>
internal sealed class IndicShaper
{
    // The kinds of syllables: the low bits of GlyphInfo.Syllable.
    private const int ConsonantSyllable = 0;
    private const int VowelSyllable = 1;
    private const int StandaloneCluster = 2;
    private const int SymbolCluster = 3;
    private const int BrokenCluster = 4;
    private const int NonIndicCluster = 5;

    // The features that apply to a part of a syllable, in the order of the plan: the bits of the masks.
    private const int Rphf = 0;
    private const int Pref = 1;
    private const int Blwf = 2;
    private const int Abvf = 3;
    private const int Half = 4;
    private const int Pstf = 5;
    private const int Init = 6;

    private const ShaperFeatureFlags Basic = ShaperFeatureFlags.ManualZeroWidthJoiner | ShaperFeatureFlags.ManualZeroWidthNonJoiner | ShaperFeatureFlags.PerSyllable;

    /// <summary>The stage after which the syllables are reordered for the first time: the localized forms are applied.</summary>
    public const int InitialReorderingStage = 2;

    /// <summary>The stage after which the syllables are reordered for the last time: the basic shaping forms are applied.</summary>
    public const int FinalReorderingStage = 13;

    /// <summary>The features of the model, in the order of the specifications.</summary>
    public static ShaperPlan Plan { get; } = new(
        [
            new(ShapingFeature.ToTag("locl"), 2, ShaperFeatureFlags.PerSyllable),
            new(ShapingFeature.ToTag("ccmp"), 2, ShaperFeatureFlags.PerSyllable),

            // The basic shaping forms, one at a time. Those marked for a part of the syllable are first in the order
            // of the bits above.
            new(ShapingFeature.ToTag("rphf"), 5, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("pref"), 7, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("blwf"), 8, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("abvf"), 9, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("half"), 10, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("pstf"), 11, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("init"), 14, Basic | ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("nukt"), 3, Basic),
            new(ShapingFeature.ToTag("akhn"), 4, Basic),
            new(ShapingFeature.ToTag("rkrf"), 6, Basic),
            new(ShapingFeature.ToTag("vatu"), 12, Basic),
            new(ShapingFeature.ToTag("cjct"), 13, Basic),

            // The presentation forms, together.
            new(ShapingFeature.ToTag("pres"), 14, Basic),
            new(ShapingFeature.ToTag("abvs"), 14, Basic),
            new(ShapingFeature.ToTag("blws"), 14, Basic),
            new(ShapingFeature.ToTag("psts"), 14, Basic),
            new(ShapingFeature.ToTag("haln"), 14, Basic),
        ],
        finalStage: 14,
        disabledFeatures: [ShapingFeature.ToTag("liga")]);

    private static readonly Dictionary<string, ulong> PatternCategories = new(StringComparer.Ordinal)
    {
        ["C"] = 1UL << Consonant,
        ["V"] = 1UL << Vowel,
        ["N"] = 1UL << Nukta,
        ["H"] = 1UL << Halant,
        ["ZWNJ"] = 1UL << NonJoiner,
        ["ZWJ"] = 1UL << Joiner,
        ["M"] = 1UL << Matra,
        ["SM"] = 1UL << SyllableModifier,
        ["A"] = 1UL << VedicSign,
        ["PLACEHOLDER"] = 1UL << Placeholder,
        ["DOTTEDCIRCLE"] = 1UL << DottedCircle,
        ["RS"] = 1UL << RegisterShifter,
        ["MPst"] = 1UL << MatraPost,
        ["Repha"] = 1UL << Repha,
        ["Ra"] = 1UL << Ra,
        ["CM"] = 1UL << ConsonantMedial,
        ["Symbol"] = 1UL << Symbol,
        ["CS"] = 1UL << ConsonantWithStacker,
    };

    // The syllables of the specifications ("Find next syllable in the input"), completed for what a text can hold:
    // two nuktas, joiners around halants and vowel signs, any number of consonants and of vowel signs, two modifiers,
    // signs of other blocks. A syllable is the longest sequence one of the patterns matches.
    private static readonly Dictionary<string, string> PatternParts = new(StringComparer.Ordinal)
    {
        ["c"] = "C | Ra",
        ["n"] = "(ZWNJ? RS)? (N N?)?",
        ["z"] = "ZWJ | ZWNJ",
        ["reph"] = "Ra H | Repha",
        ["cn"] = "c ZWJ? n",
        ["matra_group"] = "z* (M | SM? MPst) N? H?",
        ["syllable_tail"] = "(z? SM SM? ZWNJ?)? A*",
        ["halant_group"] = "z? H (ZWJ N?)?",
        ["final_halant_group"] = "halant_group | H ZWNJ",
        ["halant_or_matra_group"] = "final_halant_group | matra_group*",
        ["complex_syllable_tail"] = "(halant_group cn)* CM? halant_or_matra_group syllable_tail",
    };

    // In the order of preference when two patterns match the same sequence.
    private static readonly (SyllablePattern Pattern, int Kind)[] Patterns =
    [
        (Compile("(Repha | CS)? cn complex_syllable_tail"), ConsonantSyllable),
        (Compile("reph? V n (ZWJ | complex_syllable_tail)"), VowelSyllable),
        (Compile("((Repha | CS)? PLACEHOLDER | reph? DOTTEDCIRCLE) n complex_syllable_tail"), StandaloneCluster),
        (Compile("Symbol N? syllable_tail"), SymbolCluster),
        (Compile("reph? n complex_syllable_tail"), BrokenCluster),
    ];

    private readonly FontFace _face;
    private readonly UnicodeScript _script;
    private readonly int _firstBit;
    private readonly int _virama;
    private readonly byte _rephPosition;
    private readonly RephMode _rephMode;
    private readonly bool _hasPreAndPostBelowForms;
    private readonly bool _isOldSpecification;
    private readonly bool _hasPrebaseFeature;
    private readonly int[] _rephLookups;
    private readonly int[] _prebaseLookups;
    private readonly int[] _belowLookups;
    private readonly int[] _postLookups;
    private readonly int[] _vattuLookups;
    private readonly Dictionary<(int Feature, int First, int Second), bool> _answers = [];
    private GlyphBuffer? _queryBuffer;
    private LayoutApplier? _query;

    /// <summary>Creates the state of the model for a run.</summary>
    /// <param name="face">The font.</param>
    /// <param name="script">The script of the run.</param>
    /// <param name="scriptTag">The tag the substitution table of the font has for the script.</param>
    /// <param name="firstBit">The bit of the first feature of the plan in the masks of the glyphs.</param>
    /// <param name="getLookups">Gives the lookups of a feature of the substitution table, for the script and the language of the run.</param>
    public IndicShaper(FontFace face, UnicodeScript script, uint scriptTag, int firstBit, Func<string, int[]> getLookups)
    {
        _face = face;
        _script = script;
        _firstBit = firstBit;

        // The reph is "after post-script" in the tables of the specifications of Tamil, Telugu and Kannada, "after
        // main" for Oriya and Malayalam, "after subscript" for Bengali, "before subscript" for Gurmukhi and "before
        // postscript" for Devanagari and Gujarati. Telugu shows a reph only when a zero width joiner asks for it, and
        // Malayalam has it as a character of its own.
        (_virama, _rephPosition, _rephMode, _hasPreAndPostBelowForms) = script switch
        {
            UnicodeScript.Devanagari => (0x094D, BeforePost, RephMode.Implicit, true),
            UnicodeScript.Bengali => (0x09CD, AfterSubjoined, RephMode.Implicit, true),
            UnicodeScript.Gurmukhi => (0x0A4D, BeforeSubjoined, RephMode.Implicit, true),
            UnicodeScript.Gujarati => (0x0ACD, BeforePost, RephMode.Implicit, true),
            UnicodeScript.Oriya => (0x0B4D, AfterMain, RephMode.Implicit, true),
            UnicodeScript.Tamil => (0x0BCD, AfterPost, RephMode.Implicit, true),
            UnicodeScript.Telugu => (0x0C4D, AfterPost, RephMode.Explicit, false),
            UnicodeScript.Kannada => (0x0CCD, AfterPost, RephMode.Implicit, false),
            _ => (0x0D4D, AfterMain, RephMode.Character, true),
        };

        // A font made for the first version of the specifications has the tag of the script without a version; its
        // lookups are for a consonant followed by a halant, where the current ones are for a halant and a consonant.
        _isOldSpecification = (scriptTag & 0xFF) != '2';
        _rephLookups = getLookups("rphf");
        _prebaseLookups = getLookups("pref");
        _belowLookups = getLookups("blwf");
        _postLookups = getLookups("pstf");
        _vattuLookups = getLookups("vatu");
        _hasPrebaseFeature = _prebaseLookups.Length > 0;
    }

    private enum RephMode
    {
        /// <summary>Ra and halant at the start of a syllable, unless a joiner follows.</summary>
        Implicit,

        /// <summary>Ra, halant and a zero width joiner.</summary>
        Explicit,

        /// <summary>A character of its own, written where the reph is.</summary>
        Character,
    }

    private static SyllablePattern Compile(string pattern) => SyllablePattern.Compile(pattern, PatternCategories, PatternParts);

    /// <summary>Gives each character its category and its place, and cuts the text into syllables.</summary>
    public static void FindSyllables(Span<GlyphInfo> info)
    {
        foreach (ref var glyph in info)
        {
            Get(glyph.CodePoint, out glyph.SyllableCategory, out glyph.SyllablePosition);
        }

        var serial = 0;
        var start = 0;
        while (start < info.Length)
        {
            // The longest match; the first pattern when several match it.
            var length = 0;
            var kind = NonIndicCluster;
            foreach (var (pattern, patternKind) in Patterns)
            {
                var matched = pattern.Match(info, start);
                if (matched > length)
                {
                    length = matched;
                    kind = patternKind;
                }
            }

            length = Math.Max(length, 1);
            serial = serial == 15 ? 1 : serial + 1;
            for (var i = start; i < start + length; i++)
            {
                info[i].Syllable = (byte)((serial << 4) | kind);
            }

            start += length;
        }
    }

    /// <summary>
    /// Shows the sequences of a vowel letter and a vowel sign that look like another vowel letter with a dotted circle
    /// between them: the Unicode Standard, chapter 12, the tables of the vowel letters of each script ("to represent,
    /// use, do not use"). A text is then not read as the letter it does not have.
    /// </summary>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void SeparateVowelSequences(GlyphBuffer buffer, UnicodeScript script)
    {
        for (var i = 0; i + 1 < buffer.Length; i++)
        {
            var first = buffer.Info[i].CodePoint;
            var second = buffer.Info[i + 1].CodePoint;
            var isSeparated = script switch
            {
                UnicodeScript.Devanagari => first switch
                {
                    0x0905 => second is 0x093A or 0x093B or 0x093E or 0x0945 or 0x0946 or 0x0949 or 0x094A or 0x094B or 0x094C or 0x094F or 0x0956 or 0x0957,
                    0x0906 => second is 0x093A or 0x0945 or 0x0946 or 0x0947 or 0x0948,
                    0x0909 => second == 0x0941,
                    0x090F => second is 0x0945 or 0x0946 or 0x0947,
                    _ => false,
                },
                UnicodeScript.Bengali => (first == 0x0985 && second == 0x09BE) || (first == 0x098B && second == 0x09C3) || (first == 0x098C && second == 0x09E2),
                UnicodeScript.Gurmukhi => first switch
                {
                    0x0A05 => second is 0x0A3E or 0x0A48 or 0x0A4C,
                    0x0A72 => second is 0x0A3F or 0x0A40 or 0x0A47,
                    0x0A73 => second is 0x0A41 or 0x0A42 or 0x0A4B,
                    _ => false,
                },
                UnicodeScript.Gujarati => (first == 0x0A85 && second is 0x0ABE or 0x0AC5 or 0x0AC7 or 0x0AC8 or 0x0AC9 or 0x0ACB or 0x0ACC) || (first == 0x0AC5 && second == 0x0ABE),
                UnicodeScript.Oriya => (first == 0x0B05 && second == 0x0B3E) || (first is 0x0B0F or 0x0B13 && second == 0x0B57),
                UnicodeScript.Tamil => first == 0x0B85 && second == 0x0BC2,
                UnicodeScript.Telugu => (first == 0x0C12 && second is 0x0C4C or 0x0C55) || (first is 0x0C3F or 0x0C46 or 0x0C4A && second == 0x0C55),
                UnicodeScript.Kannada => (first is 0x0C89 or 0x0C8B && second == 0x0CBE) || (first == 0x0C92 && second == 0x0CCC),
                UnicodeScript.Malayalam => (first is 0x0D07 or 0x0D09 && second == 0x0D57) || (first == 0x0D0E && second == 0x0D46) || (first == 0x0D12 && second is 0x0D3E or 0x0D57),

                // The scripts of the universal model: its data file of the clusters that are not valid.
                UnicodeScript.Sinhala => first switch
                {
                    0x0D85 => second is 0x0DCF or 0x0DD0 or 0x0DD1,
                    0x0D8B or 0x0D8F or 0x0D94 => second == 0x0DDF,
                    0x0D8D => second == 0x0DD8,
                    0x0D91 => second is 0x0DCA or 0x0DD9 or 0x0DDA or 0x0DDC or 0x0DDD or 0x0DDE,
                    _ => false,
                },
                UnicodeScript.Brahmi => (first == 0x11005 && second == 0x11038) || (first == 0x1100B && second == 0x1103E) || (first == 0x1100F && second == 0x11042),
                UnicodeScript.Takri => (first == 0x11680 && second is 0x116AD or 0x116B4 or 0x116B5) || (first == 0x11686 && second == 0x116B2),
                UnicodeScript.Khojki => first switch
                {
                    0x11200 => second is 0x1122C or 0x11231 or 0x11233,
                    0x11206 => second == 0x1122C,
                    0x1122C => second is 0x11230 or 0x11231,
                    0x11240 => second == 0x1122E,
                    _ => false,
                },
                UnicodeScript.Khudawadi => first == 0x112B0 && second is 0x112E0 or 0x112E5 or 0x112E6 or 0x112E7 or 0x112E8,
                UnicodeScript.Tirhuta => (first == 0x11481 && second == 0x114B0) || (first == 0x114AA && second is 0x114B5 or 0x114B6) || (first is 0x1148B or 0x1148D && second == 0x114BA),
                UnicodeScript.Modi => first is 0x11600 or 0x11601 && second is 0x11639 or 0x1163A,
                _ => false,
            };

            // Devanagari: a ra, a halant and the letter i are not the letter ii.
            if (script == UnicodeScript.Devanagari && first == 0x0930 && second == 0x094D && i + 2 < buffer.Length && buffer.Info[i + 2].CodePoint == 0x0907)
            {
                isSeparated = true;
                i++;
            }

            if (!isSeparated)
                continue;

            // The circle is in the cluster of what follows it. The character after the circle is not looked at as
            // the first of another sequence.
            var cluster = buffer.Info[i + 1].Cluster;
            buffer.Resize(i + 1, 0, 1);
            TextShaper.SetCharacterProperties(ref buffer.Info[i + 1], 0x25CC);
            buffer.Info[i + 1].Cluster = cluster;
            i += 2;
        }
    }

    /// <summary>
    /// The first reordering, before the basic shaping forms: "Find base consonant", "Decompose and reorder matras",
    /// "Reorder marks to canonical order", and the marking of the glyphs each basic feature applies to.
    /// </summary>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public void ReorderBeforeBasicForms(GlyphBuffer buffer)
    {
        SetConsonantPositions(buffer.Info);
        InsertDottedCircles(buffer);
        var info = buffer.Info;
        for (var start = 0; start < info.Length;)
        {
            var end = start + 1;
            while (end < info.Length && info[end].Syllable == info[start].Syllable)
            {
                end++;
            }

            if ((info[start].Syllable & 0x0F) is ConsonantSyllable or VowelSyllable or StandaloneCluster or BrokenCluster)
            {
                ReorderSyllable(buffer, start, end);
            }

            start = end;
        }
    }

    /// <summary>
    /// "Retrieving dynamic character properties from Indic fonts": a consonant has a below-base form, a post-base
    /// form or a pre-base-reordering form when the font replaces it and the halant by one glyph.
    /// </summary>
    private void SetConsonantPositions(Span<GlyphInfo> info)
    {
        var virama = _face.MapCodePoint(_virama);
        if (virama == 0)
            return;

        foreach (ref var glyph in info)
        {
            if (glyph.SyllablePosition != Base)
                continue;

            // The lookups of a font of either version of the specifications are found in fonts of the other one:
            // both orders are tried.
            if (WouldSubstitute(Blwf, _belowLookups, virama, glyph.Glyph) || WouldSubstitute(Blwf, _belowLookups, glyph.Glyph, virama)
                || WouldSubstitute(Init + 1, _vattuLookups, virama, glyph.Glyph) || WouldSubstitute(Init + 1, _vattuLookups, glyph.Glyph, virama))
            {
                glyph.SyllablePosition = BelowConsonant;
            }
            else if (WouldSubstitute(Pstf, _postLookups, virama, glyph.Glyph) || WouldSubstitute(Pstf, _postLookups, glyph.Glyph, virama)
                || WouldSubstitute(Pref, _prebaseLookups, virama, glyph.Glyph) || WouldSubstitute(Pref, _prebaseLookups, glyph.Glyph, virama))
            {
                glyph.SyllablePosition = PostConsonant;
            }
        }
    }

    private bool WouldSubstitute(int feature, int[] lookups, int first, int second)
    {
        if (lookups.Length == 0 || _face.Gsub is not { } table)
            return false;

        if (_answers.TryGetValue((feature, first, second), out var answer))
            return answer;

        if (_query is null)
        {
            _queryBuffer = new GlyphBuffer(_face.Data.Limits);
            _query = new LayoutApplier(_queryBuffer, _face);
            _query.Begin(table, _face.GsubData, isPositioning: false, isRightToLeft: false);
        }

        // A rule with a context is not a property of the consonant alone, except for the fonts of the first version
        // of the specifications and of Malayalam, whose rules have one.
        var withoutContext = !_isOldSpecification && _script != UnicodeScript.Malayalam;
        ReadOnlySpan<int> glyphs = [first, second];
        foreach (var lookup in lookups)
        {
            if (_query.WouldSubstitute(lookup, glyphs, withoutContext))
            {
                answer = true;
                break;
            }
        }

        _answers.Add((feature, first, second), answer);
        return answer;
    }

    /// <summary>"Invalid combining marks": a syllable without a base is shown on a dotted circle, after its reph.</summary>
    private void InsertDottedCircles(GlyphBuffer buffer)
    {
        var circle = _face.MapCodePoint(0x25CC);
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

            // The circle takes the cluster and the features of the first glyph of the syllable.
            lastSyllable = syllable;
            var template = buffer.Info[i];
            while (i < buffer.Length && buffer.Info[i].Syllable == syllable && buffer.Info[i].SyllableCategory == Repha)
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
                SyllableCategory = DottedCircle,
                SyllablePosition = End,
                Category = UnicodeCategory.OtherSymbol,
            };
        }
    }

    private static bool IsOneOf(in GlyphInfo info, uint categories)
    {
        // A glyph that is a ligature is no longer the character it was.
        return (info.Props & GlyphInfo.Ligated) == 0 && ((1u << info.SyllableCategory) & categories) != 0;
    }

    private static bool IsConsonant(in GlyphInfo info) => IsOneOf(info, ConsonantCategories);

    private static bool IsJoiner(in GlyphInfo info) => IsOneOf(info, JoinerCategories);

    private static bool IsHalant(in GlyphInfo info) => IsOneOf(info, 1u << Halant);

    private void ReorderSyllable(GlyphBuffer buffer, int start, int end)
    {
        var info = buffer.Info;

        // Kannada: a ra, a halant and a zero width joiner at the start of a syllable are the ra and what follows it,
        // not a reph.
        if (_script == UnicodeScript.Kannada && start + 3 <= end && IsOneOf(info[start], 1u << Ra) && IsOneOf(info[start + 1], 1u << Halant) && IsOneOf(info[start + 2], 1u << Joiner))
        {
            buffer.MergeClusters(start + 1, start + 3);
            (info[start + 1], info[start + 2]) = (info[start + 2], info[start + 1]);
        }

        // 1. Find base consonant: starting from the end of the syllable, move backwards until a consonant is found
        // that does not have a below-base or post-base form (post-base forms have to follow below-base forms), or
        // that is not a pre-base-reordering ra, or arrive at the first consonant.
        var @base = end;
        var hasReph = false;
        {
            // If the syllable starts with ra and halant (in a script that has a reph) and has more than one
            // consonant, ra is excluded from the candidates for the base.
            var limit = start;
            if (_rephLookups.Length > 0 && start + 3 <= end
                && ((_rephMode == RephMode.Implicit && !IsJoiner(info[start + 2])) || (_rephMode == RephMode.Explicit && info[start + 2].SyllableCategory == Joiner)))
            {
                // The font decides whether its ra and halant make a reph: with the joiner, when one is asked for.
                var rephLength = _rephMode == RephMode.Explicit ? 3 : 2;
                if (WouldSubstituteReph(info, start, 2) || (_rephMode == RephMode.Explicit && WouldSubstituteReph(info, start, rephLength)))
                {
                    limit += 2;
                    while (limit < end && IsJoiner(info[limit]))
                    {
                        limit++;
                    }

                    @base = start;
                    hasReph = true;
                }
            }
            else if (_rephMode == RephMode.Character && info[start].SyllableCategory == Repha)
            {
                limit++;
                while (limit < end && IsJoiner(info[limit]))
                {
                    limit++;
                }

                @base = start;
                hasReph = true;
            }

            var i = end;
            var hasBelow = false;
            do
            {
                i--;
                if (IsConsonant(info[i]))
                {
                    // A consonant that has neither a below-base nor a post-base form is the base; a post-base form
                    // before a below-base form is not one.
                    if (info[i].SyllablePosition != BelowConsonant && (info[i].SyllablePosition != PostConsonant || hasBelow))
                    {
                        @base = i;
                        break;
                    }

                    hasBelow |= info[i].SyllablePosition == BelowConsonant;
                    @base = i;
                }
                else if (start < i && info[i].SyllableCategory == Joiner && info[i - 1].SyllableCategory == Halant)
                {
                    // A zero width joiner after a halant asks for the half form of the consonant before: the
                    // consonants before it are not the base.
                    break;
                }
            }
            while (i > limit);

            // A syllable of one consonant: the ra is the base, and there is no reph.
            if (hasReph && @base == start && limit - @base <= 2)
            {
                hasReph = false;
            }
        }

        // 2. Decompose and reorder matras. The matras were decomposed with the text; each one has the place of its
        // reordering class. The consonants before the base are pre-base forms, with what follows each of them.
        for (var i = start; i < @base; i++)
        {
            info[i].SyllablePosition = Math.Min(PreConsonant, info[i].SyllablePosition);
        }

        if (@base < end)
        {
            info[@base].SyllablePosition = Base;
        }

        if (hasReph)
        {
            info[start].SyllablePosition = RaToBecomeReph;
        }

        // The fonts of the first version of the specifications have their lookups for a consonant followed by a
        // halant: the first halant after the base goes after the last consonant, which is then such a sequence.
        if (_isOldSpecification)
        {
            var allowsTwoHalants = _script != UnicodeScript.Kannada;
            for (var i = @base + 1; i < end; i++)
            {
                if (info[i].SyllableCategory != Halant)
                    continue;

                var j = end - 1;
                while (j > i && !IsConsonant(info[j]) && (allowsTwoHalants || info[j].SyllableCategory != Halant))
                {
                    j--;
                }

                if (info[j].SyllableCategory != Halant && j > i)
                {
                    var halant = info[i];
                    info[(i + 1)..(j + 1)].CopyTo(info[i..]);
                    info[j] = halant;
                }

                break;
            }
        }

        // A nukta, a halant, a joiner and the like stay with the character they follow. A halant that follows a
        // matra drawn before the syllable stays with the consonant instead.
        {
            var last = Start;
            for (var i = start; i < end; i++)
            {
                if (((1u << info[i].SyllableCategory) & (JoinerCategories | (1u << Nukta) | (1u << RegisterShifter) | (1u << ConsonantMedial) | (1u << Halant))) != 0)
                {
                    info[i].SyllablePosition = last;
                    if (info[i].SyllableCategory == Halant && info[i].SyllablePosition == PreMatra)
                    {
                        for (var j = i; j > start; j--)
                        {
                            if (info[j - 1].SyllablePosition != PreMatra)
                            {
                                info[i].SyllablePosition = info[j - 1].SyllablePosition;
                                break;
                            }
                        }
                    }
                }
                else if (info[i].SyllablePosition != ModifiersAndVedic)
                {
                    // A modifier before a matra drawn after the syllable goes with that matra.
                    if (info[i].SyllableCategory == MatraPost && i > start && info[i - 1].SyllableCategory == SyllableModifier)
                    {
                        info[i - 1].SyllablePosition = info[i].SyllablePosition;
                    }

                    last = info[i].SyllablePosition;
                }
            }
        }

        // A consonant after the base owns what precedes it since the consonant or the matra before.
        {
            var last = @base;
            for (var i = @base + 1; i < end; i++)
            {
                if (IsConsonant(info[i]))
                {
                    for (var j = last + 1; j < i; j++)
                    {
                        if (info[j].SyllablePosition < ModifiersAndVedic)
                        {
                            info[j].SyllablePosition = info[i].SyllablePosition;
                        }
                    }

                    last = i;
                }
                else if (((1u << info[i].SyllableCategory) & ((1u << Matra) | (1u << MatraPost))) != 0)
                {
                    last = i;
                }
            }
        }

        // The characters are put in the order of their places; those of one place stay in the order of the text.
        // 3. Reorder marks to canonical order: the place of a nukta is the one of the character it follows.
        {
            // The rank of each character in the text, to see what was moved.
            var count = end - start;
            Span<int> ranks = count <= 256 ? stackalloc int[count] : new int[count];
            for (var i = 0; i < count; i++)
            {
                ranks[i] = i;
            }

            for (var i = start + 1; i < end; i++)
            {
                var j = i;
                while (j > start && info[j - 1].SyllablePosition > info[i].SyllablePosition)
                {
                    j--;
                }

                if (j == i)
                    continue;

                var moved = info[i];
                var rank = ranks[i - start];
                info[j..i].CopyTo(info[(j + 1)..]);
                ranks[(j - start)..(i - start)].CopyTo(ranks[(j - start + 1)..]);
                info[j] = moved;
                ranks[j - start] = rank;
            }

            // The base, again. Several matras drawn before the syllable are drawn from the base outward: the first
            // one of the text is the nearest to the base, each with what follows it (its nukta).
            var firstMatra = end;
            var lastMatra = end;
            @base = end;
            for (var i = start; i < end; i++)
            {
                if (info[i].SyllablePosition == Base)
                {
                    @base = i;
                    break;
                }

                if (info[i].SyllablePosition == PreMatra)
                {
                    firstMatra = firstMatra == end ? i : firstMatra;
                    lastMatra = i;
                }
            }

            if (firstMatra < lastMatra)
            {
                info[firstMatra..(lastMatra + 1)].Reverse();
                ranks[(firstMatra - start)..(lastMatra + 1 - start)].Reverse();
                var groupStart = firstMatra;
                for (var j = firstMatra; j <= lastMatra; j++)
                {
                    if (((1u << info[j].SyllableCategory) & ((1u << Matra) | (1u << MatraPost))) != 0)
                    {
                        info[groupStart..(j + 1)].Reverse();
                        ranks[(groupStart - start)..(j + 1 - start)].Reverse();
                        groupStart = j + 1;
                    }
                }
            }

            // After the base, what changed places is one cluster with what it passed: the characters that took each
            // other's places, from the first to the last of them. The fonts of the first version of the
            // specifications had their halant moved: everything after the base is one cluster. Before the base, the
            // clusters are merged by the last reordering.
            if (_isOldSpecification || count > 127)
            {
                buffer.MergeClusters(@base, end);
            }
            else
            {
                for (var i = @base; i < end; i++)
                {
                    if (ranks[i - start] < 0)
                        continue;

                    var min = i;
                    var max = i;
                    var j = start + ranks[i - start];
                    while (j != i)
                    {
                        min = Math.Min(min, j);
                        max = Math.Max(max, j);
                        var next = start + ranks[j - start];
                        ranks[j - start] = -1;
                        j = next;
                    }

                    buffer.MergeClusters(Math.Max(@base, min), max + 1);
                }
            }
        }

        // The glyphs each basic feature applies to. The reph: its ra and its halant.
        for (var i = start; i < end && info[i].SyllablePosition == RaToBecomeReph; i++)
        {
            info[i].Mask |= Bit(Rphf);
        }

        // Before the base: half forms and, when the font has below-base forms of consonants before the base, these.
        var prebase = Bit(Half);
        if (!_isOldSpecification && _hasPreAndPostBelowForms)
        {
            prebase |= Bit(Blwf);
        }

        for (var i = start; i < @base; i++)
        {
            info[i].Mask |= prebase;
        }

        // After the base: below-base, above-base and post-base forms.
        for (var i = @base + 1; i < end; i++)
        {
            info[i].Mask |= Bit(Blwf) | Bit(Abvf) | Bit(Pstf);
        }

        // Devanagari, first version: a ra and a halant before the base, without a joiner after them, make the
        // "eyelash" form through the below-base feature.
        if (_isOldSpecification && _script == UnicodeScript.Devanagari)
        {
            for (var i = start; i + 1 < @base; i++)
            {
                if (info[i].SyllableCategory == Ra && info[i + 1].SyllableCategory == Halant && (i + 2 == @base || info[i + 2].SyllableCategory != Joiner))
                {
                    info[i].Mask |= Bit(Blwf);
                    info[i + 1].Mask |= Bit(Blwf);
                }
            }
        }

        // A halant and a consonant after the base that the pre-base-reordering feature replaces: "final determination
        // of whether an occurrence of ra can be treated as a pre-base reordering ra can be made only after the 'pref'
        // feature has been applied".
        if (_hasPrebaseFeature && @base + 2 < end)
        {
            for (var i = @base + 1; i + 1 < end; i++)
            {
                if (WouldSubstitute(Pref, _prebaseLookups, info[i].Glyph, info[i + 1].Glyph))
                {
                    info[i].Mask |= Bit(Pref);
                    info[i + 1].Mask |= Bit(Pref);
                    break;
                }
            }
        }

        // "The shaping engine must take specific actions to prevent half forms for a sequence of consonant, halant
        // and zero width non-joiner": the half-form feature is taken from the consonant before the non-joiner.
        for (var i = start + 1; i < end; i++)
        {
            if (!IsJoiner(info[i]))
                continue;

            var isNonJoiner = info[i].SyllableCategory == NonJoiner;
            var j = i;
            do
            {
                j--;
                if (isNonJoiner)
                {
                    info[j].Mask &= ~Bit(Half);
                }
            }
            while (j > start && !IsConsonant(info[j]));
        }
    }

    private bool WouldSubstituteReph(ReadOnlySpan<GlyphInfo> info, int start, int length)
    {
        // The font says what its reph is made of: the glyphs are asked about, whatever their characters are.
        if (length == 2)
            return WouldSubstitute(Rphf, _rephLookups, info[start].Glyph, info[start + 1].Glyph);

        // With the joiner: the answer is not kept, a text has few of these.
        if (_face.Gsub is null)
            return false;

        _ = WouldSubstitute(Rphf, _rephLookups, info[start].Glyph, info[start + 1].Glyph);
        ReadOnlySpan<int> glyphs = [info[start].Glyph, info[start + 1].Glyph, info[start + 2].Glyph];
        foreach (var lookup in _rephLookups)
        {
            if (_query!.WouldSubstitute(lookup, glyphs, withoutContext: !_isOldSpecification && _script != UnicodeScript.Malayalam))
                return true;
        }

        return false;
    }

    private uint Bit(int feature) => 1u << (_firstBit + feature);

    /// <summary>
    /// "Final reordering", after the basic shaping forms: the matras drawn before the syllable go next to the base,
    /// the reph goes to its place, and so does a consonant the pre-base-reordering feature replaced.
    /// </summary>
    public void ReorderAfterBasicForms(GlyphBuffer buffer)
    {
        var info = buffer.Info;
        var virama = _face.MapCodePoint(_virama);
        for (var start = 0; start < info.Length;)
        {
            var end = start + 1;
            while (end < info.Length && info[end].Syllable == info[start].Syllable)
            {
                end++;
            }

            if ((info[start].Syllable & 0x0F) is ConsonantSyllable or VowelSyllable or StandaloneCluster or BrokenCluster)
            {
                ReorderSyllableAfterBasicForms(buffer, start, end, virama);
            }

            start = end;
        }
    }

    private void ReorderSyllableAfterBasicForms(GlyphBuffer buffer, int start, int end, int virama)
    {
        var info = buffer.Info;

        // The reordering looks for halant glyphs. A halant a substitution took out of a ligature and put back is a
        // halant again.
        if (virama != 0)
        {
            for (var i = start; i < end; i++)
            {
                if (info[i].Glyph == virama && (info[i].Props & GlyphInfo.Ligated) != 0 && (info[i].Props & GlyphInfo.Multiplied) != 0)
                {
                    info[i].SyllableCategory = Halant;
                    info[i].Props &= unchecked((byte)~(GlyphInfo.Ligated | GlyphInfo.Multiplied));
                }
            }
        }

        // The base, again: the features may have made ligatures with it.
        var tryPrebase = _hasPrebaseFeature;
        int @base;
        for (@base = start; @base < end; @base++)
        {
            if (info[@base].SyllablePosition < Base)
                continue;

            if (tryPrebase && @base + 1 < end)
            {
                for (var i = @base + 1; i < end; i++)
                {
                    if ((info[i].Mask & Bit(Pref)) == 0)
                        continue;

                    if (!(IsSubstituted(info[i]) && IsLigatedOnly(info[i])))
                    {
                        // The font did not make the pre-base form of this consonant: it is the base.
                        @base = i;
                        while (@base < end && IsHalant(info[@base]))
                        {
                            @base++;
                        }

                        if (@base < end)
                        {
                            info[@base].SyllablePosition = Base;
                        }

                        tryPrebase = false;
                    }

                    break;
                }

                if (@base == end)
                    break;
            }

            // Malayalam: a below-base form the font did not make is the base (a post-base form is not).
            if (_script == UnicodeScript.Malayalam)
            {
                for (var i = @base + 1; i < end; i++)
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

                    if (i < end && IsConsonant(info[i]) && info[i].SyllablePosition == BelowConsonant)
                    {
                        @base = i;
                        info[@base].SyllablePosition = Base;
                    }
                }
            }

            if (start < @base && info[@base].SyllablePosition > Base)
            {
                @base--;
            }

            break;
        }

        if (@base == end && start < @base && IsOneOf(info[@base - 1], 1u << Joiner))
        {
            @base--;
        }

        if (@base < end)
        {
            while (start < @base && IsOneOf(info[@base], (1u << Nukta) | (1u << Halant)))
            {
                @base--;
            }
        }

        // Reorder matras: if a pre-base matra character had been reordered before applying basic features, the glyph
        // can be moved closer to the main consonant based on whether half-forms had been formed. Actual position for
        // the matra is defined as "after last standalone halant glyph, after initial matra position and before the
        // main consonant". If ZWJ or ZWNJ follow this halant, position is moved after it.
        if (start + 1 < end && start < @base)
        {
            // Without a base, before the last glyph.
            var newPosition = @base == end ? @base - 2 : @base - 1;

            // Malayalam and Tamil have no half form: what the half-form feature makes there is a consonant with its
            // visible halant, and the matra goes after it.
            if (_script is not (UnicodeScript.Malayalam or UnicodeScript.Tamil))
            {
                while (true)
                {
                    while (newPosition > start && !IsOneOf(info[newPosition], (1u << Matra) | (1u << MatraPost) | (1u << Halant)))
                    {
                        newPosition--;
                    }

                    // A halant, and not the one of the matra itself.
                    if (IsHalant(info[newPosition]) && info[newPosition].SyllablePosition != PreMatra)
                    {
                        // The matra does not go after a halant a zero width joiner follows: the search goes on.
                        if (newPosition + 1 < end && info[newPosition + 1].SyllableCategory == Joiner && newPosition > start)
                        {
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

            if (start < newPosition && info[newPosition].SyllablePosition != PreMatra)
            {
                for (var i = newPosition; i > start; i--)
                {
                    if (info[i - 1].SyllablePosition != PreMatra)
                        continue;

                    var oldPosition = i - 1;
                    if (oldPosition < @base && @base <= newPosition)
                    {
                        @base--;
                    }

                    var matra = info[oldPosition];
                    info[(oldPosition + 1)..(newPosition + 1)].CopyTo(info[oldPosition..]);
                    info[newPosition] = matra;

                    // The matra and what it passed, up to the base, are one cluster.
                    buffer.MergeClusters(newPosition, Math.Min(end, @base + 1));
                    newPosition--;
                }
            }
            else
            {
                for (var i = start; i < @base; i++)
                {
                    if (info[i].SyllablePosition == PreMatra)
                    {
                        buffer.MergeClusters(i, Math.Min(end, @base + 1));
                        break;
                    }
                }
            }
        }

        // Reorder reph: reph's original position is always at the beginning of the syllable (it is not reordered at
        // the character reordering stage). However, it will be reordered according to the basic-forms shaping results.
        // A ra and a halant are moved when the font made the reph of them; a reph that is a character of its own is
        // moved unless the font made something else of it.
        if (start + 1 < end && info[start].SyllablePosition == RaToBecomeReph && ((info[start].SyllableCategory == Repha) ^ IsLigatedOnly(info[start])))
        {
            var newPosition = FindRephPosition(info, start, end, @base);
            buffer.MergeClusters(start, newPosition + 1);
            var reph = info[start];
            info[(start + 1)..(newPosition + 1)].CopyTo(info[start..]);
            info[newPosition] = reph;
            if (start < @base && @base <= newPosition)
            {
                @base--;
            }
        }

        // Reorder pre-base-reordering consonants: only reorder a glyph produced by substitution during application of
        // the 'pref' feature. Try to find a target position the same way as for pre-base matra; if position is not
        // found, reorder immediately before main consonant.
        if (tryPrebase && @base + 1 < end)
        {
            for (var i = @base + 1; i < end; i++)
            {
                if ((info[i].Mask & Bit(Pref)) == 0)
                    continue;

                if (IsLigatedOnly(info[i]))
                {
                    var newPosition = @base;
                    if (_script is not (UnicodeScript.Malayalam or UnicodeScript.Tamil))
                    {
                        while (newPosition > start && !IsOneOf(info[newPosition - 1], (1u << Matra) | (1u << MatraPost) | (1u << Halant)))
                        {
                            newPosition--;
                        }
                    }

                    // If ZWJ or ZWNJ follow this halant, position is moved after it.
                    if (newPosition > start && IsHalant(info[newPosition - 1]) && newPosition < end && IsJoiner(info[newPosition]))
                    {
                        newPosition++;
                    }

                    buffer.MergeClusters(newPosition, i + 1);
                    var consonant = info[i];
                    info[newPosition..i].CopyTo(info[(newPosition + 1)..]);
                    info[newPosition] = consonant;
                }

                break;
            }
        }

        // A matra drawn before the first syllable of a word has an initial form.
        if (info[start].SyllablePosition == PreMatra
            && (start == 0 || info[start - 1].Category is not (UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate
                or UnicodeCategory.LowercaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.UppercaseLetter
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.NonSpacingMark)))
        {
            info[start].Mask |= Bit(Init);
        }
    }

    private int FindRephPosition(ReadOnlySpan<GlyphInfo> info, int start, int end, int @base)
    {
        // 1. If reph should be positioned after post-base consonant forms, proceed to step 5.
        if (_rephPosition != AfterPost)
        {
            // 2. If the reph repositioning class is not after post-base: target position is after the first explicit
            // halant glyph between the first post-reph consonant and last main consonant. If ZWJ or ZWNJ are
            // following this halant, position is moved after it.
            if (FindHalant(info, start, @base) is >= 0 and var afterHalant)
                return afterHalant;

            // 3. If reph should be repositioned after the main consonant: find the first consonant not ligated with
            // main, or find the first consonant that is not a potential pre-base-reordering ra.
            if (_rephPosition == AfterMain)
            {
                var position = @base;
                while (position + 1 < end && info[position + 1].SyllablePosition <= AfterMain)
                {
                    position++;
                }

                if (position < end)
                    return position;
            }

            // 4. If reph should be positioned before post-base consonant, find first post-base classified consonant
            // not ligated with main. If no consonant is found, the target position should be before the first matra,
            // syllable modifier sign or vedic sign.
            if (_rephPosition == AfterSubjoined)
            {
                var position = @base;
                while (position + 1 < end && info[position + 1].SyllablePosition is not (PostConsonant or AfterPost or ModifiersAndVedic))
                {
                    position++;
                }

                if (position < end)
                    return position;
            }
        }

        // 5. If no consonant is found in steps 3 or 4, move reph to a position immediately before the first post-base
        // matra, syllable modifier sign or vedic sign that has a reordering class after the intended reph position.
        if (FindHalant(info, start, @base) is >= 0 and var halant)
            return halant;

        // 6. Otherwise, reorder reph to the end of the syllable, before the modifiers.
        {
            var position = end - 1;
            while (position > start && info[position].SyllablePosition == ModifiersAndVedic)
            {
                position--;
            }

            // Before the halant of a matra, so that the reph and the matra can be shaped together; not before the
            // halant of a consonant.
            if (IsHalant(info[position]))
            {
                for (var i = @base + 1; i < position; i++)
                {
                    if (((1u << info[i].SyllableCategory) & ((1u << Matra) | (1u << MatraPost))) != 0)
                    {
                        position--;
                    }
                }
            }

            return position;
        }
    }

    // The place after the first halant between the reph and the base, and after the joiner that follows it.
    private static int FindHalant(ReadOnlySpan<GlyphInfo> info, int start, int @base)
    {
        var position = start + 1;
        while (position < @base && !IsHalant(info[position]))
        {
            position++;
        }

        if (position >= @base)
            return -1;

        if (position + 1 < @base && IsJoiner(info[position + 1]))
        {
            position++;
        }

        return position;
    }

    private static bool IsSubstituted(in GlyphInfo info) => (info.Flags & GlyphInfo.Substituted) != 0;

    private static bool IsLigatedOnly(in GlyphInfo info) => (info.Props & GlyphInfo.Ligated) != 0 && (info.Props & GlyphInfo.Multiplied) == 0;
}
