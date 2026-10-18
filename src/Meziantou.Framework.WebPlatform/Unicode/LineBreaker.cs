using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Meziantou.Framework.WebPlatform.Unicode.LineBreakClass;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// Line break opportunities of a text: the Unicode Line Breaking Algorithm (https://www.unicode.org/reports/tr14/) with the
/// tailorings of CSS Text (https://drafts.csswg.org/css-text-4/#line-breaking): <c>line-break</c>, <c>word-break</c>,
/// <c>overflow-wrap</c>, <c>hyphens</c> (soft hyphens) and <c>white-space-collapse: break-spaces</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rules are applied in order on the sequence of the combining character sequences (rule LB9), as written in UAX #14
/// (the pair table of the previous revisions cannot express the context of the current rules). The text is the content of an
/// inline formatting context after the white space processing: an atomic inline is represented by U+FFFC (class CB, so there
/// are break opportunities around it, LB20); the layout forces the opportunities around the atomic inlines that the
/// neighboring characters forbid (https://drafts.csswg.org/css-text-4/#line-break-details).
/// </para>
/// <para>
/// With the CSS tailorings, a soft wrap opportunity is never inside a grapheme cluster (the typographic character unit).
/// The Southeast Asian scripts (class SA) are broken at their word boundaries (the lexical word breaking of CSS Text,
/// https://drafts.csswg.org/css-text-4/#lexical-breaking): the words of Thai, Lao, Myanmar and Khmer are found with the word
/// lists of ICU (<see cref="SoutheastAsianWordBreaker"/>), the other SA scripts use the fallback breaking (a soft wrap
/// opportunity between the typographic letter units, https://drafts.csswg.org/css-text-4/#fallback-breaking); there is no
/// break between their letters with <c>word-break: manual</c>. With <c>word-break: auto-phrase</c> and a Japanese content
/// language, the opportunities inside the phrases of the kanji and kana (<see cref="JapanesePhraseBreaker"/>) are
/// <see cref="LineBreakOpportunity.Phrase"/>.
/// </para>
/// <para>An instance reuses its buffers: it is not thread-safe.</para>
/// </remarks>
internal sealed class LineBreaker
{
    private Unit[] _units = [];
    private readonly List<int> _wordBreaks = [];
    private int[] _phraseCodePoints = [];
    private bool[] _phraseBoundaries = [];

    [Flags]
    private enum UnitFlags : ushort
    {
        None = 0,
        GraphemeBoundary = 1 << 0,
        InitialQuote = 1 << 1,         // [\p{Pi}&QU]
        FinalQuote = 1 << 2,           // [\p{Pf}&QU]
        SoutheastAsianLetter = 1 << 3, // SA resolved to AL, broken at its word boundaries (DictionaryLetter) or between the letters
        Letter = 1 << 4,               // typographic letter unit or NU/AL/AI/ID (word-break: keep-all)
        InNumber = 1 << 5,             // ends a NU (SY|IS)* sequence (LB25)
        OddRegionalIndicators = 1 << 6, // ends an odd sequence of RI (LB30a)
        EndsWithSpace = 1 << 7,        // the last code point is U+0020, U+0009 or, with break-spaces, another space separator
        SoftHyphen = 1 << 8,
        CjkHyphen = 1 << 9,            // U+2010 or U+2013 (line-break: loose)
        DictionaryLetter = 1 << 10,    // SA letter of a script whose words are found with a word list (lexical word breaking)
        PhraseCharacter = 1 << 11,     // kanji or kana whose phrases are kept together (word-break: auto-phrase)
        SegmentBoundary = 1 << 12,     // a word (DictionaryLetter) or phrase (PhraseCharacter) boundary before the sequence
    }

    // A combining character sequence (LB9), or a single character
    [StructLayout(LayoutKind.Auto)]
    private struct Unit
    {
        public int Start;
        public int BaseCodePoint;
        public LineBreakClass Class;     // resolved class of the sequence (LB1, the CSS tailorings, LB9 and LB10)
        public LineBreakClass LastClass; // resolved class of the last code point (LB4 to LB8a)
        public UnitFlags Flags;
        public int Run;
    }

    /// <summary>Returns the line break opportunities of the text (see <see cref="FindOpportunities(ReadOnlySpan{char}, ReadOnlySpan{LineBreakOptionsRun}, Span{LineBreakOpportunity})"/>).</summary>
    public static LineBreakOpportunity[] GetOpportunities(ReadOnlySpan<char> text, LineBreakOptions options = default)
    {
        var result = new LineBreakOpportunity[text.Length];
        new LineBreaker().FindOpportunities(text, options, result);
        return result;
    }

    /// <inheritdoc cref="FindOpportunities(ReadOnlySpan{char}, ReadOnlySpan{LineBreakOptionsRun}, Span{LineBreakOpportunity})"/>
    public void FindOpportunities(ReadOnlySpan<char> text, LineBreakOptions options, Span<LineBreakOpportunity> opportunities)
    {
        ReadOnlySpan<LineBreakOptionsRun> runs = [new LineBreakOptionsRun(0, options)];
        FindOpportunities(text, runs, opportunities);
    }

    /// <summary>
    /// Sets <c>opportunities[i]</c> to the line break opportunity before <c>text[i]</c>. The first entry is <see cref="LineBreakOpportunity.None"/>
    /// (no break at the start of the text, LB2), as are the low surrogates. The break at the end of the text (LB3) is not included.
    /// </summary>
    /// <param name="runs">The options of the ranges of the text, sorted by start, the first one starting at 0. The class of a character is resolved with its options; a break between two characters uses the options of the second one, except the break after a soft hyphen.</param>
    public void FindOpportunities(ReadOnlySpan<char> text, ReadOnlySpan<LineBreakOptionsRun> runs, Span<LineBreakOpportunity> opportunities)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(opportunities.Length, text.Length);
        if (runs.IsEmpty || runs[0].Start != 0)
            throw new ArgumentException("The first run must start at 0", nameof(runs));

        opportunities[..text.Length].Clear();
        if (text.IsEmpty)
            return;

        if (_units.Length < text.Length)
        {
            _units = new Unit[Math.Max(text.Length, Math.Min(_units.Length * 2, 1 << 20))];
        }

        var count = BuildUnits(text, runs, opportunities, out var hasSegments);
        var units = _units.AsSpan(0, count);
        if (hasSegments)
        {
            FindSegmentBoundaries(text, units);
        }

        var lastNonSpace = -1;
        for (var k = 1; k < units.Length; k++)
        {
            if (units[k - 1].Class != SP)
            {
                lastNonSpace = k - 1;
            }

            opportunities[units[k].Start] = Decide(units, k, lastNonSpace, runs);
        }
    }

    /// <summary>
    /// The context-dependent part of the segment break transformation rules of CSS Text
    /// (https://drafts.csswg.org/css-text-4/#line-break-transform): returns whether the collapsible segment break between
    /// <paramref name="before"/> and <paramref name="after"/> (the code points around it, after the removal of the spaces and tabs
    /// around the segment break and of the default ignorable characters, as in Firefox) is removed instead of being transformed into
    /// a space. The rules are UA-defined; these are the rules of the previous drafts of CSS Text, which the tentative WPT tests
    /// (css/css-text/white-space/seg-break-transformation-*) check, as Firefox applies them: wide emoji and U+20A9 WON SIGN are not
    /// East Asian characters. The rules that depend on the writing system are <see cref="IsSegmentBreakRemovedInChineseJapaneseOrYi"/>.
    /// </summary>
    public static bool IsSegmentBreakRemoved(int before, int after)
    {
        // If the character immediately before or immediately after the segment break is the zero-width space character (U+200B),
        // then the break is removed, leaving behind the zero-width space
        if (before == 0x200B || after == 0x200B)
            return true;

        // If the East Asian Width property of both the character before and after the segment break is F, W, or H (not A), and
        // neither side is Hangul, then the segment break is removed
        return IsSpaceDiscarding(before) && IsSpaceDiscarding(after);
    }

    /// <summary>
    /// The segment break transformation rules that apply when the writing system of the segment break is Chinese, Japanese or Yi
    /// (in addition to <see cref="IsSegmentBreakRemoved"/>): the segment break is removed when the character on one side is
    /// a fullwidth or wide punctuation mark (as in Firefox, WPT css/css-text/line-breaking/segment-break-transformation-punctuation-*),
    /// or a punctuation mark or a symbol (not a currency symbol) whose East Asian Width is A (or an emoji) and the character on the
    /// other side is a fullwidth, wide or halfwidth character that is not Hangul (the rule of the previous drafts of CSS Text 4,
    /// WPT css/css-text/writing-system/writing-system-segment-break-001.html).
    /// </summary>
    public static bool IsSegmentBreakRemovedInChineseJapaneseOrYi(int before, int after)
        => IsEastAsianPunctuation(before) || IsEastAsianPunctuation(after)
            || (IsAmbiguousPunctuationOrSymbol(before) && IsSpaceDiscarding(after))
            || (IsAmbiguousPunctuationOrSymbol(after) && IsSpaceDiscarding(before));

    // A character whose East Asian Width is F, W (except the emoji) or H, and that is not Hangul (nor U+20A9 WON SIGN, a Korean
    // character whose script is Common)
    private static bool IsSpaceDiscarding(int codePoint)
    {
        if (codePoint < 0x1100 || codePoint == 0x20A9)
            return false;

        return UnicodeProperties.GetEastAsianWidth(codePoint) switch
        {
            EastAsianWidth.F or EastAsianWidth.H => !UnicodeProperties.IsHangul(codePoint),
            EastAsianWidth.W => !UnicodeProperties.IsHangul(codePoint) && !UnicodeProperties.IsEmojiPresentationParticipating(codePoint),
            _ => false,
        };
    }

    // A fullwidth, wide or halfwidth punctuation mark (General Category P*), U+FF5E FULLWIDTH TILDE (which is used as U+301C WAVE
    // DASH) or U+3000 IDEOGRAPHIC SPACE (Firefox's IsEastAsianPunctuation)
    private static bool IsEastAsianPunctuation(int codePoint)
    {
        if (codePoint < 0x1100)
            return false;

        if (codePoint is 0xFF5E or 0x3000)
            return true;

        return UnicodeProperties.IsEastAsianFullwidthWideOrHalfwidth(codePoint)
            && UnicodeProperties.GetGeneralCategory(codePoint) is >= UnicodeCategory.ConnectorPunctuation and <= UnicodeCategory.OtherPunctuation;
    }

    // A punctuation mark or a symbol whose East Asian Width is A, or an emoji whose East Asian Width is W or N ("treated as having
    // an East Asian Width property of Ambiguous"). The currency symbols are not: a segment break around an amount is a space, as
    // in Firefox (WPT css/css-text/line-breaking/segment-break-transformation-currency-001.html: U+20AC EURO SIGN is Ambiguous)
    private static bool IsAmbiguousPunctuationOrSymbol(int codePoint)
    {
        var width = UnicodeProperties.GetEastAsianWidth(codePoint);
        if (width == EastAsianWidth.A)
            return UnicodeProperties.GetGeneralCategory(codePoint) is (>= UnicodeCategory.ConnectorPunctuation and <= UnicodeCategory.OtherPunctuation) or UnicodeCategory.MathSymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;

        return width is EastAsianWidth.W or EastAsianWidth.N && codePoint > 0x7F && UnicodeProperties.IsEmojiPresentationParticipating(codePoint);
    }

    // Splits the text into combining character sequences (LB9, LB10) and resolves the classes (LB1 and the CSS tailorings). The
    // break opportunities inside the sequences (at the grapheme cluster boundaries) are only the ones of line-break: anywhere and
    // overflow-wrap.
    private int BuildUnits(ReadOnlySpan<char> text, ReadOnlySpan<LineBreakOptionsRun> runs, Span<LineBreakOpportunity> opportunities, out bool hasSegments)
    {
        hasSegments = false;
        var units = _units;
        var count = 0;
        var run = 0;
        var grapheme = default(GraphemeClusterBreaker);
        var previousClass = BK; // any class that prevents LB9; only read after the first code point
        for (var i = 0; i < text.Length;)
        {
            var codePoint = GraphemeClusters.ReadCodePoint(text, i, out var length);
            while (run + 1 < runs.Length && runs[run + 1].Start <= i)
            {
                run++;
            }

            var options = runs[run].Options;
            var resolved = Resolve(codePoint, UnicodeProperties.GetLineBreakClass(codePoint), options, out var southeastAsianLetter);
            var graphemeBoundary = options.IsUnicodeDefault || grapheme.IsBoundaryBefore(UnicodeProperties.GetSegmentationProperties(codePoint));

            if (count > 0 && resolved is CM or ZWJ && previousClass is not (BK or CR or LF or NL or SP or ZW))
            {
                // LB9: X (CM | ZWJ)* is treated as X
                ref var unit = ref units[count - 1];
                unit.LastClass = resolved;
                unit.Flags &= ~UnitFlags.EndsWithSpace;
                if (graphemeBoundary && !options.IsUnicodeDefault)
                {
                    opportunities[i] = options.Strictness == LineBreakStrictness.Anywhere ? LineBreakOpportunity.Allowed
                        : options.OverflowWrap != OverflowWrapMode.Normal ? LineBreakOpportunity.Emergency
                        : LineBreakOpportunity.None;
                }
            }
            else
            {
                ref var unit = ref units[count];
                unit.Start = i;
                unit.Run = run;
                unit.LastClass = resolved;
                if (resolved is CM or ZWJ)
                {
                    // LB10: as if it were U+0041 LATIN CAPITAL LETTER A
                    unit.Class = AL;
                    unit.BaseCodePoint = 'A';
                }
                else
                {
                    unit.Class = resolved;
                    unit.BaseCodePoint = codePoint;
                }

                var flags = graphemeBoundary ? UnitFlags.GraphemeBoundary : UnitFlags.None;
                if (unit.Class == QU)
                {
                    var category = UnicodeProperties.GetGeneralCategory(unit.BaseCodePoint);
                    if (category == UnicodeCategory.InitialQuotePunctuation)
                    {
                        flags |= UnitFlags.InitialQuote;
                    }
                    else if (category == UnicodeCategory.FinalQuotePunctuation)
                    {
                        flags |= UnitFlags.FinalQuote;
                    }
                }

                if (southeastAsianLetter)
                {
                    flags |= UnitFlags.SoutheastAsianLetter;
                    if (SoutheastAsianWordBreaker.GetDictionaryScript(codePoint) != UnicodeScript.Unknown)
                    {
                        flags |= UnitFlags.DictionaryLetter;
                        hasSegments = true;
                    }
                }
                else if (options.WordBreak == WordBreakMode.AutoPhrase && options.IsJapanese && JapanesePhraseBreaker.IsPhraseCharacter(unit.BaseCodePoint))
                {
                    flags |= UnitFlags.PhraseCharacter;
                    hasSegments = true;
                }

                if (options.WordBreak == WordBreakMode.KeepAll && (unit.Class is NU or AL or ID || IsLetterOrNumber(unit.BaseCodePoint)))
                {
                    flags |= UnitFlags.Letter;
                }

                var previous = count > 0 ? units[count - 1] : default;
                if (unit.Class == NU || (unit.Class is SY or IS && count > 0 && ((previous.Flags & UnitFlags.InNumber) != UnitFlags.None)))
                {
                    flags |= UnitFlags.InNumber;
                }

                if (unit.Class == RI && !(count > 0 && previous.Class == RI && ((previous.Flags & UnitFlags.OddRegionalIndicators) != UnitFlags.None)))
                {
                    flags |= UnitFlags.OddRegionalIndicators;
                }

                // https://drafts.csswg.org/css-text-4/#valdef-white-space-collapse-break-spaces: "after every preserved white space
                // character and after every other space separator" (the no-break spaces of class GL keep their meaning)
                if (codePoint is ' ' or '\t' || (options.BreakSpaces && resolved != GL && IsOtherSpaceSeparator(codePoint)))
                {
                    flags |= UnitFlags.EndsWithSpace;
                }
                else if (codePoint == 0xAD)
                {
                    flags |= UnitFlags.SoftHyphen;
                }
                else if (codePoint is 0x2010 or 0x2013)
                {
                    flags |= UnitFlags.CjkHyphen;
                }

                unit.Flags = flags;
                count++;
            }

            previousClass = resolved;
            i += length;
        }

        return count;
    }

    // The word boundaries of the runs of SA letters that have a word list, and the phrase boundaries of the runs of kanji and kana
    // of word-break: auto-phrase (SegmentBoundary)
    private void FindSegmentBoundaries(ReadOnlySpan<char> text, Span<Unit> units)
    {
        var k = 0;
        while (k < units.Length)
        {
            var flags = units[k].Flags;
            if ((flags & UnitFlags.DictionaryLetter) != UnitFlags.None)
            {
                // The range of the characters of the engine (the dictionary characters of the script, marks included)
                var breaker = SoutheastAsianWordBreaker.Get(SoutheastAsianWordBreaker.GetDictionaryScript(units[k].BaseCodePoint));
                var rangeStart = units[k].Start;
                var rangeEnd = rangeStart;
                while (rangeEnd < text.Length)
                {
                    var codePoint = GraphemeClusters.ReadCodePoint(text, rangeEnd, out var length);
                    if (!breaker.Handles(codePoint))
                        break;

                    rangeEnd += length;
                }

                _wordBreaks.Clear();
                breaker.FindBreaks(text, rangeStart, rangeEnd, _wordBreaks);
                var next = 0;
                k++;
                while (k < units.Length && units[k].Start < rangeEnd)
                {
                    while (next < _wordBreaks.Count && _wordBreaks[next] < units[k].Start)
                    {
                        next++;
                    }

                    if (next < _wordBreaks.Count && _wordBreaks[next] == units[k].Start)
                    {
                        units[k].Flags |= UnitFlags.SegmentBoundary;
                    }

                    k++;
                }
            }
            else if ((flags & UnitFlags.PhraseCharacter) != UnitFlags.None)
            {
                var end = k + 1;
                while (end < units.Length && (units[end].Flags & UnitFlags.PhraseCharacter) != UnitFlags.None)
                {
                    end++;
                }

                var length = end - k;
                if (_phraseCodePoints.Length < length)
                {
                    _phraseCodePoints = new int[Math.Max(length, _phraseCodePoints.Length * 2)];
                    _phraseBoundaries = new bool[_phraseCodePoints.Length];
                }

                for (var i = 0; i < length; i++)
                {
                    _phraseCodePoints[i] = units[k + i].BaseCodePoint;
                }

                JapanesePhraseBreaker.FindBoundaries(_phraseCodePoints.AsSpan(0, length), _phraseBoundaries);
                for (var i = 1; i < length; i++)
                {
                    if (_phraseBoundaries[i])
                    {
                        units[k + i].Flags |= UnitFlags.SegmentBoundary;
                    }
                }

                k = end;
            }
            else
            {
                k++;
            }
        }
    }

    // LB1 (https://www.unicode.org/reports/tr14/#LB1) and the CSS tailorings that change the class of a character
    private static LineBreakClass Resolve(int codePoint, LineBreakClass value, LineBreakOptions options, out bool southeastAsianLetter)
    {
        southeastAsianLetter = false;
        switch (value)
        {
            case AI or SG or XX:
                value = AL;
                break;

            case SA:
                if (UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                {
                    value = CM;
                }
                else
                {
                    // The Southeast Asian scripts are broken at their word boundaries (with a word list) or between their letters
                    // (https://drafts.csswg.org/css-text-4/#fallback-breaking), except with word-break: manual (see LB28)
                    value = AL;
                    southeastAsianLetter = !options.IsUnicodeDefault && options.WordBreak != WordBreakMode.Manual;
                }

                break;

            case CJ:
                // https://drafts.csswg.org/css-text-4/#line-break-property forbids the breaks before the small kana with normal
                // since w3c/csswg-drafts#10363, but the browsers still allow them with normal and loose (as UAX #14 suggests:
                // "CJ: resolve to ID for normal line breaking"): CJ is NS only with strict
                value = !options.IsUnicodeDefault && options.Strictness is LineBreakStrictness.Normal or LineBreakStrictness.Loose ? ID : NS;
                break;
        }

        if (options.IsUnicodeDefault)
            return value;

        // https://drafts.csswg.org/css-text-4/#white-space-processing: the control characters other than the tabs and the
        // segment breaks "must be otherwise treated as any other character of the Other Symbols (So) general category": they
        // are not combining marks (CM). U+000B, U+000C (BK) and U+0085 (NL) remain forced line breaks ("any Unicode character
        // with the BK, CR, LF, and NL line breaking class, must be treated as forced line breaks")
        if (value == CM && codePoint is < 0x20 or (>= 0x7F and < 0xA0))
            return AL;

        // https://drafts.csswg.org/css-text-4/#line-break-property
        if (options.Strictness == LineBreakStrictness.Loose)
        {
            // Breaks before the iteration marks and between the inseparable characters
            if (value == IN || codePoint is 0x3005 or 0x303B or 0x309D or 0x309E or 0x30FD or 0x30FE)
            {
                value = ID;
            }
            else if (options.IsChineseOrJapanese)
            {
                // Breaks before the centered punctuation marks, before the suffixes and after the prefixes whose East_Asian_Width is A, F or W
                if (codePoint is 0x30FB or 0xFF1A or 0xFF1B or 0xFF65 or 0x203C or 0x2047 or 0x2048 or 0x2049 or 0xFF01 or 0xFF1F
                    || (value is PO or PR && UnicodeProperties.GetEastAsianWidth(codePoint) is EastAsianWidth.A or EastAsianWidth.F or EastAsianWidth.W))
                {
                    value = ID;
                }
            }
        }

        // Breaks before the CJK hyphen-like characters
        if (options.IsChineseOrJapanese && options.Strictness is LineBreakStrictness.Normal or LineBreakStrictness.Loose && codePoint is 0x301C or 0x30A0)
        {
            value = ID;
        }

        // https://drafts.csswg.org/css-text-4/#valdef-word-break-break-all: the typographic letter units, and the NU, AL and SA
        // characters, are treated as ID. The Hangul jamo and the Brahmic letters keep the classes that keep their syllables together.
        if (options.WordBreak == WordBreakMode.BreakAll
            && (value is AL or HL or NU || (value is not (CM or ZWJ or ID or H2 or H3 or JL or JV or JT or AK or AP or AS or VF or VI) && IsLetterOrNumber(codePoint))))
        {
            value = ID;
            southeastAsianLetter = false;
        }

        return value;
    }

    /// <summary>
    /// Whether a character is one of the other space separators of CSS Text (https://drafts.csswg.org/css-text-4/#other-space-separators):
    /// General_Category Zs except U+0020 and U+00A0.
    /// </summary>
    internal static bool IsOtherSpaceSeparator(int codePoint) => codePoint is 0x1680 or (>= 0x2000 and <= 0x200A) or 0x202F or 0x205F or 0x3000;

    private static bool IsLetterOrNumber(int codePoint)
        => UnicodeProperties.GetGeneralCategory(codePoint) is <= UnicodeCategory.OtherLetter or (>= UnicodeCategory.DecimalDigitNumber and <= UnicodeCategory.OtherNumber);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEastAsian(in Unit unit) => UnicodeProperties.IsEastAsianFullwidthWideOrHalfwidth(unit.BaseCodePoint);

    // [AK ◌ AS] of LB28a
    private static bool IsAksara(in Unit unit) => unit.Class is AK or AS || unit.BaseCodePoint == 0x25CC;

    private static LineBreakOpportunity Decide(ReadOnlySpan<Unit> units, int k, int lastNonSpace, ReadOnlySpan<LineBreakOptionsRun> runs)
    {
        ref readonly var before = ref units[k - 1];
        ref readonly var after = ref units[k];
        var options = runs[after.Run].Options;
        if (options.IsUnicodeDefault)
            return ApplyRules(units, k, lastNonSpace, options);

        // https://drafts.csswg.org/css-text-4/#valdef-line-break-anywhere: a soft wrap opportunity around every typographic
        // character unit, disregarding any prohibition (the forced breaks remain)
        if (options.Strictness == LineBreakStrictness.Anywhere)
        {
            if (before.LastClass == CR && after.Class == LF)
                return LineBreakOpportunity.None;

            if (before.LastClass is BK or CR or LF or NL)
                return LineBreakOpportunity.Mandatory;

            // LB6: a soft wrap opportunity before a forced break would be useless
            if (after.Class is BK or CR or LF or NL)
                return LineBreakOpportunity.None;

            return ((after.Flags & UnitFlags.GraphemeBoundary) != UnitFlags.None) ? LineBreakOpportunity.Allowed : LineBreakOpportunity.None;
        }

        var result = ApplyRules(units, k, lastNonSpace, options);
        if (result == LineBreakOpportunity.Mandatory)
            return result;

        // https://drafts.csswg.org/css-text-4/#valdef-word-break-keep-all: no implicit soft wrap opportunity between letters,
        // "except where opportunities exist due to dictionary-based breaking"
        var bothFlags = before.Flags & after.Flags;
        if (result == LineBreakOpportunity.Allowed && options.WordBreak == WordBreakMode.KeepAll && ((bothFlags & UnitFlags.Letter) != UnitFlags.None)
            && (bothFlags & UnitFlags.DictionaryLetter) == UnitFlags.None)
        {
            result = LineBreakOpportunity.None;
        }

        // https://drafts.csswg.org/css-text-4/#hyphens-property: the soft hyphens are hyphenation opportunities, except with
        // hyphens: none (word-break: auto-phrase suppresses them while another opportunity fits, see the inline layout)
        if (result == LineBreakOpportunity.Allowed && ((before.Flags & UnitFlags.SoftHyphen) != UnitFlags.None))
        {
            result = runs[before.Run].Options.Hyphens == HyphensMode.None ? LineBreakOpportunity.None : LineBreakOpportunity.Hyphen;
        }

        // https://drafts.csswg.org/css-text-4/#valdef-word-break-auto-phrase: the opportunities inside a phrase, and between a
        // letter or a digit and the start of a phrase (as with lw=phrase in ICU: "abc漢字" is a phrase), are only taken when the
        // line would overflow
        if (result == LineBreakOpportunity.Allowed && ((after.Flags & UnitFlags.PhraseCharacter) != UnitFlags.None)
            && ((before.Flags & UnitFlags.PhraseCharacter) != UnitFlags.None ? (after.Flags & UnitFlags.SegmentBoundary) == UnitFlags.None : IsLetterOrNumber(before.BaseCodePoint)))
        {
            result = LineBreakOpportunity.Phrase;
        }

        // https://drafts.csswg.org/css-text-4/#valdef-white-space-collapse-break-spaces
        if (options.BreakSpaces && ((before.Flags & UnitFlags.EndsWithSpace) != UnitFlags.None))
        {
            result = LineBreakOpportunity.Allowed;
        }

        // The typographic character units are not broken (https://drafts.csswg.org/css-text-4/#typographic-character-unit); a
        // mark or a joiner after a space starts a unit of its own (LB10), the space keeps its opportunity
        if (!((after.Flags & UnitFlags.GraphemeBoundary) != UnitFlags.None) && before.Class != SP)
            return LineBreakOpportunity.None;

        // There is no emergency opportunity before the characters that cannot start a line (LB6, LB7). The spaces of
        // break-spaces take up space as the other characters do: a line can be broken before them when nothing else fits. At the
        // boundary of two runs, the overflow-wrap of either character allows it (which elements control the opportunities at
        // such boundaries "is undefined", https://drafts.csswg.org/css-text-4/#line-break-details; as in Safari, WPT
        // css/css-text/overflow-wrap/overflow-wrap-anywhere-inline-*.tentative.html)
        if (result == LineBreakOpportunity.None && (options.OverflowWrap != OverflowWrapMode.Normal || runs[before.Run].Options.OverflowWrap != OverflowWrapMode.Normal)
            && after.Class is not (BK or CR or LF or NL or ZW)
            && (after.Class != SP || options.BreakSpaces))
        {
            return LineBreakOpportunity.Emergency;
        }

        return result;
    }

    // https://www.unicode.org/reports/tr14/#Algorithm: the rules from LB4, between the sequences k - 1 and k
    private static LineBreakOpportunity ApplyRules(ReadOnlySpan<Unit> units, int k, int lastNonSpace, LineBreakOptions options)
    {
        const LineBreakOpportunity Break = LineBreakOpportunity.Allowed;
        const LineBreakOpportunity NoBreak = LineBreakOpportunity.None;

        ref readonly var a = ref units[k - 1];
        ref readonly var b = ref units[k];
        var aClass = a.Class;
        var bClass = b.Class;

        // LB4, LB5: mandatory breaks
        if (a.LastClass == BK)
            return LineBreakOpportunity.Mandatory;

        if (a.LastClass == CR && bClass == LF)
            return NoBreak;

        if (a.LastClass is CR or LF or NL)
            return LineBreakOpportunity.Mandatory;

        // LB6, LB7
        if (bClass is BK or CR or LF or NL or SP or ZW)
            return NoBreak;

        // LB8: ZW SP* ÷
        if (lastNonSpace >= 0 && units[lastNonSpace].Class == ZW)
            return Break;

        // LB8a: ZWJ ×
        if (a.LastClass == ZWJ)
            return NoBreak;

        // As in the browsers, a space is a soft wrap opportunity whatever follows it: UAX #14 forbids the breaks before a word
        // joiner (LB11), a closing punctuation, an exclamation mark, a solidus (LB13) or an infix separator (LB15d), and after an
        // opening punctuation (LB14) or an initial quotation mark (LB15a), even after spaces
        if (aClass == SP && !options.IsUnicodeDefault)
            return Break;

        // LB11: × WJ, WJ ×
        if (aClass == WJ || bClass == WJ)
            return NoBreak;

        // LB12: GL ×
        if (aClass == GL)
            return NoBreak;

        // LB12a: [^SP HY HH] × GL
        if (bClass == GL && aClass is not (SP or HY or HH))
            return NoBreak;

        // LB13: × CL, × CP, × EX, × SY
        if (bClass is CL or CP or EX or SY)
            return NoBreak;

        var lastNonSpaceClass = lastNonSpace >= 0 ? units[lastNonSpace].Class : (LineBreakClass)byte.MaxValue;

        // LB14: OP SP* ×
        if (lastNonSpaceClass == OP)
            return NoBreak;

        // LB15a: (sot | BK | CR | LF | NL | OP | QU | GL | SP | ZW) [\p{Pi}&QU] SP* ×
        if (lastNonSpace >= 0 && ((units[lastNonSpace].Flags & UnitFlags.InitialQuote) != UnitFlags.None)
            && (lastNonSpace == 0 || units[lastNonSpace - 1].Class is BK or CR or LF or NL or OP or QU or GL or SP or ZW))
        {
            return NoBreak;
        }

        // LB15b: × [\p{Pf}&QU] ( SP | GL | WJ | CL | QU | CP | EX | IS | SY | BK | CR | LF | NL | ZW | eot)
        if (((b.Flags & UnitFlags.FinalQuote) != UnitFlags.None)
            && (k + 1 == units.Length || units[k + 1].Class is SP or GL or WJ or CL or QU or CP or EX or IS or SY or BK or CR or LF or NL or ZW))
        {
            return NoBreak;
        }

        // LB15c: SP ÷ IS NU
        if (aClass == SP && bClass == IS && k + 1 < units.Length && units[k + 1].Class == NU)
            return Break;

        // LB15d: × IS
        if (bClass == IS)
            return NoBreak;

        // LB16: (CL | CP) SP* × NS
        if (lastNonSpaceClass is CL or CP && bClass == NS)
            return NoBreak;

        // LB17: B2 SP* × B2
        if (lastNonSpaceClass == B2 && bClass == B2)
            return NoBreak;

        // LB18: SP ÷
        if (aClass == SP)
            return Break;

        // LB19: × [ QU - \p{Pi} ], [ QU - \p{Pf} ] ×
        if (bClass == QU && !((b.Flags & UnitFlags.InitialQuote) != UnitFlags.None))
            return NoBreak;

        if (aClass == QU && !((a.Flags & UnitFlags.FinalQuote) != UnitFlags.None))
            return NoBreak;

        // LB19a: unless surrounded by East Asian characters, do not break either side of any unresolved quotation marks
        if (bClass == QU && (!IsEastAsian(a) || k + 1 == units.Length || !IsEastAsian(units[k + 1])))
            return NoBreak;

        if (aClass == QU && (!IsEastAsian(b) || k == 1 || !IsEastAsian(units[k - 2])))
            return NoBreak;

        // LB20: ÷ CB, CB ÷
        if (aClass == CB || bClass == CB)
            return Break;

        // LB20a: ( sot | BK | CR | LF | NL | SP | ZW | CB | GL ) ( HY | HH ) × ( AL | HL )
        if (aClass is HY or HH && bClass is AL or HL && (k == 1 || units[k - 2].Class is BK or CR or LF or NL or SP or ZW or CB or GL))
            return NoBreak;

        // LB21: × BA, × HH, × HY, × NS, BB ×
        if (bClass is BA or HH)
        {
            // https://drafts.csswg.org/css-text-4/#line-break-property: breaks before the hyphens U+2010 (HH) and U+2013 (BA) after an ID with line-break: loose
            if (!options.IsUnicodeDefault && options.Strictness == LineBreakStrictness.Loose && ((b.Flags & UnitFlags.CjkHyphen) != UnitFlags.None) && aClass == ID)
                return Break;

            return NoBreak;
        }

        if (bClass is HY or NS || aClass == BB)
            return NoBreak;

        // LB21a: HL (HY | HH) × [^HL]
        if (aClass is HY or HH && bClass != HL && k >= 2 && units[k - 2].Class == HL)
            return NoBreak;

        // LB21b: SY × HL
        if (aClass == SY && bClass == HL)
            return NoBreak;

        // LB22: × IN
        if (bClass == IN)
            return NoBreak;

        // LB23: (AL | HL) × NU, NU × (AL | HL)
        if ((aClass is AL or HL && bClass == NU) || (aClass == NU && bClass is AL or HL))
            return NoBreak;

        // LB23a: PR × (ID | EB | EM), (ID | EB | EM) × PO
        if ((aClass == PR && bClass is ID or EB or EM) || (aClass is ID or EB or EM && bClass == PO))
            return NoBreak;

        // LB24: (PR | PO) × (AL | HL), (AL | HL) × (PR | PO)
        if ((aClass is PR or PO && bClass is AL or HL) || (aClass is AL or HL && bClass is PR or PO))
            return NoBreak;

        // LB25: numbers
        if (bClass is PO or PR)
        {
            // NU ( SY | IS )* (CL | CP)? × (PO | PR)
            if (((a.Flags & UnitFlags.InNumber) != UnitFlags.None) || (aClass is CL or CP && k >= 2 && ((units[k - 2].Flags & UnitFlags.InNumber) != UnitFlags.None)))
                return NoBreak;
        }

        if (aClass is PO or PR)
        {
            // (PO | PR) × OP NU, (PO | PR) × OP IS NU, (PO | PR) × NU
            if (bClass == NU)
                return NoBreak;

            if (bClass == OP && k + 1 < units.Length
                && (units[k + 1].Class == NU || (units[k + 1].Class == IS && k + 2 < units.Length && units[k + 2].Class == NU)))
            {
                return NoBreak;
            }
        }

        // HY × NU, IS × NU, NU ( SY | IS )* × NU
        if (bClass == NU && (aClass is HY or IS || ((a.Flags & UnitFlags.InNumber) != UnitFlags.None)))
            return NoBreak;

        // LB26: Korean syllable blocks
        if ((aClass == JL && bClass is JL or JV or H2 or H3) || (aClass is JV or H2 && bClass is JV or JT) || (aClass is JT or H3 && bClass == JT))
            return NoBreak;

        // LB27
        if ((aClass is JL or JV or JT or H2 or H3 && bClass == PO) || (aClass == PR && bClass is JL or JV or JT or H2 or H3))
            return NoBreak;

        // LB28: (AL | HL) × (AL | HL), except between the letters of the Southeast Asian scripts: at the word boundaries of the
        // scripts that have a word list (lexical word breaking), else between the letters (fallback breaking)
        if (aClass is AL or HL && bClass is AL or HL)
        {
            var flags = a.Flags & b.Flags;
            if ((flags & UnitFlags.SoutheastAsianLetter) == UnitFlags.None)
                return NoBreak;

            if ((flags & UnitFlags.DictionaryLetter) != UnitFlags.None && (b.Flags & UnitFlags.SegmentBoundary) == UnitFlags.None)
                return NoBreak;
        }

        // LB28a: orthographic syllables of the Brahmic scripts
        if (aClass == AP && (bClass is AK or AS || b.BaseCodePoint == 0x25CC))
            return NoBreak;

        if (IsAksara(a) && bClass is VF or VI)
            return NoBreak;

        if (aClass == VI && k >= 2 && IsAksara(units[k - 2]) && (bClass == AK || b.BaseCodePoint == 0x25CC))
            return NoBreak;

        if (IsAksara(a) && IsAksara(b) && k + 1 < units.Length && units[k + 1].Class == VF)
            return NoBreak;

        // LB29: IS × (AL | HL)
        if (aClass == IS && bClass is AL or HL)
            return NoBreak;

        // LB30: (AL | HL | NU) × [OP-$EastAsian], [CP-$EastAsian] × (AL | HL | NU)
        if (aClass is AL or HL or NU && bClass == OP && !IsEastAsian(b))
            return NoBreak;

        if (aClass == CP && bClass is AL or HL or NU && !IsEastAsian(a))
            return NoBreak;

        // LB30a: regional indicators
        if (aClass == RI && bClass == RI && ((a.Flags & UnitFlags.OddRegionalIndicators) != UnitFlags.None))
            return NoBreak;

        // LB30b: EB × EM, [\p{Extended_Pictographic}&\p{Cn}] × EM
        if (bClass == EM && (aClass == EB || (UnicodeProperties.IsExtendedPictographic(a.BaseCodePoint) && UnicodeProperties.GetGeneralCategory(a.BaseCodePoint) == UnicodeCategory.OtherNotAssigned)))
            return NoBreak;

        // LB31
        return Break;
    }
}
