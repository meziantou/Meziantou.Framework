using System.Runtime.CompilerServices;
using GraphemeBreak = Meziantou.Framework.UnicodeGraphemeClusterBreak;
using SentenceBreak = Meziantou.Framework.UnicodeSentenceBreak;
using WordBreak = Meziantou.Framework.UnicodeWordBreak;

namespace Meziantou.Framework;

public static partial class Unicode
{
    // The first code point that can extend a grapheme cluster: below it, every code point is Control, CR, LF or Other.
    private const int MinGraphemeExtendingCodePoint = 0x300;

    /// <summary>Gets the length of the first extended grapheme cluster of a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The number of UTF-16 code units of the cluster; 0 when <paramref name="text"/> is empty.</returns>
    /// <remarks>
    /// <para>
    /// A grapheme cluster is what a reader perceives as one character: a base with its combining marks, a Hangul
    /// syllable, an emoji sequence, a flag. The boundaries are those of the extended grapheme clusters of Unicode
    /// Standard Annex #29 for <see cref="UnicodeProperties.UnicodeVersion"/>, whatever the version of the runtime.
    /// </para>
    /// <para>An unpaired surrogate is a cluster on its own.</para>
    /// </remarks>
    /// <seealso href="https://www.unicode.org/reports/tr29/" />
    public static int GetGraphemeClusterLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        // Every code point below U+0300 is Control, CR, LF or Other, and none is Prepend, Extend or Linker: there is a
        // boundary between two of them (rules GB4, GB5 and GB999), except between CR and LF (GB3).
        if (text.Length == 1)
            return 1;

        if (text[0] < MinGraphemeExtendingCodePoint && text[1] < MinGraphemeExtendingCodePoint)
            return text[0] == '\r' && text[1] == '\n' ? 2 : 1;

        return GetGraphemeClusterLengthCore(text);
    }

    // Rules GB3 to GB9b only depend on the classes of the two code points around a position: bit n of
    // GraphemeJoins[p] is set when one of them keeps a code point of class p with a following one of class n, and bit n
    // of GraphemeBreaks[p] when GB4 or GB5 separates them. The other pairs are separated unless GB9c, GB11 or GB12
    // and GB13 keep them, which depends on what precedes them.
    private static readonly uint[] GraphemeJoins = CreateGraphemePairTable(joins: true);
    private static readonly uint[] GraphemeBreaks = CreateGraphemePairTable(joins: false);

    private static int GetGraphemeClusterLengthCore(ReadOnlySpan<char> text)
    {
        var tuple = UnicodeProperties.GetSegmentationTuple(DecodeAt(text, 0, out var index));
        return GetGraphemeClusterLengthCore(text, tuple, index, out _);
    }

    /// <summary>Gets the length of the first extended grapheme cluster of a text, whose first code point is already read.</summary>
    /// <param name="text">The text.</param>
    /// <param name="tuple">The segmentation tuple of the first code point of <paramref name="text"/>.</param>
    /// <param name="index">The length of the first code point.</param>
    /// <param name="next">The segmentation tuple of the code point that follows the cluster, with its length in bits 16 and up; -1 at the end of the text.</param>
    /// <returns>The length of the cluster.</returns>
    internal static int GetGraphemeClusterLengthCore(ReadOnlySpan<char> text, int tuple, int index, out int next)
    {
        var previous = tuple & 0x1F;

        // GB12 and GB13: the number of regional indicators that end at the previous code point.
        var regionalIndicators = previous == (int)GraphemeBreak.RegionalIndicator ? 1 : 0;

        // GB11: 1 after Extended_Pictographic Extend*, 2 after Extended_Pictographic Extend* ZWJ.
        var emoji = (tuple & 0x80) != 0 ? 1 : 0;

        // GB9c: after InCB=Linker InCB=Extend*.
        var linker = (UnicodeIndicConjunctBreak)((tuple >> 5) & 0x3) is UnicodeIndicConjunctBreak.Linker;

        while (index < text.Length)
        {
            tuple = UnicodeProperties.GetSegmentationTuple(DecodeAt(text, index, out var length));
            var current = tuple & 0x1F;
            var isPictographic = (tuple & 0x80) != 0;
            var conjunct = (UnicodeIndicConjunctBreak)((tuple >> 5) & 0x3);

            var bit = 1u << current;
            if ((GraphemeJoins[previous] & bit) == 0)
            {
                // GB4, GB5, then GB9c, GB11, GB12 and GB13, then GB999
                if ((GraphemeBreaks[previous] & bit) != 0
                    || !((linker && conjunct is UnicodeIndicConjunctBreak.Consonant)
                        || (emoji == 2 && isPictographic)
                        || (current == (int)GraphemeBreak.RegionalIndicator && previous == (int)GraphemeBreak.RegionalIndicator && (regionalIndicators & 1) != 0)))
                {
                    next = tuple | (length << 16);
                    return index;
                }
            }

            regionalIndicators = current == (int)GraphemeBreak.RegionalIndicator ? regionalIndicators + 1 : 0;
            emoji = isPictographic ? 1
                : emoji == 1 && current == (int)GraphemeBreak.Extend ? 1
                : emoji == 1 && current == (int)GraphemeBreak.ZWJ ? 2
                : 0;
            linker = conjunct switch
            {
                UnicodeIndicConjunctBreak.Linker => true,
                UnicodeIndicConjunctBreak.Extend => linker,
                _ => false,
            };

            previous = current;
            index += length;
        }

        next = -1;
        return index;
    }

    private static uint[] CreateGraphemePairTable(bool joins)
    {
        var table = new uint[32];
        for (var previous = 0; previous < 32; previous++)
        {
            for (var next = 0; next < 32; next++)
            {
                var value = GetGraphemePairRule((GraphemeBreak)previous, (GraphemeBreak)next);
                if (value == (joins ? 1 : -1))
                {
                    table[previous] |= 1u << next;
                }
            }
        }

        return table;
    }

    /// <summary>Applies the rules GB3 to GB9b to the classes around a position.</summary>
    /// <returns>1 when a rule keeps them together, -1 when GB4 or GB5 separates them, 0 when the rules that follow decide.</returns>
    private static int GetGraphemePairRule(GraphemeBreak previous, GraphemeBreak next)
    {
        // GB3
        if (previous is GraphemeBreak.CR && next is GraphemeBreak.LF)
            return 1;

        // GB4, GB5
        if (previous is GraphemeBreak.Control or GraphemeBreak.CR or GraphemeBreak.LF || next is GraphemeBreak.Control or GraphemeBreak.CR or GraphemeBreak.LF)
            return -1;

        // GB6, GB7, GB8
        if ((previous is GraphemeBreak.L && next is GraphemeBreak.L or GraphemeBreak.V or GraphemeBreak.LV or GraphemeBreak.LVT)
            || (previous is GraphemeBreak.LV or GraphemeBreak.V && next is GraphemeBreak.V or GraphemeBreak.T)
            || (previous is GraphemeBreak.LVT or GraphemeBreak.T && next is GraphemeBreak.T))
        {
            return 1;
        }

        // GB9, GB9a, GB9b
        if (next is GraphemeBreak.Extend or GraphemeBreak.ZWJ or GraphemeBreak.SpacingMark || previous is GraphemeBreak.Prepend)
            return 1;

        return 0;
    }

    /// <summary>Enumerates the extended grapheme clusters of a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>An enumerator of the range of each cluster, in order; the ranges cover the whole text.</returns>
    /// <remarks>See <see cref="GetGraphemeClusterLength(ReadOnlySpan{char})"/> for the definition of a cluster. The enumeration does not allocate.</remarks>
    /// <seealso href="https://www.unicode.org/reports/tr29/" />
    public static UnicodeGraphemeClusterEnumerator EnumerateGraphemeClusters(ReadOnlySpan<char> text) => new(text);

    /// <summary>Enumerates the segments that the word boundaries of a text delimit.</summary>
    /// <param name="text">The text.</param>
    /// <returns>An enumerator of the range of each segment, in order; the ranges cover the whole text.</returns>
    /// <remarks>
    /// <para>
    /// The boundaries are the default word boundaries of Unicode Standard Annex #29 for
    /// <see cref="UnicodeProperties.UnicodeVersion"/>. A segment is a word, a number, a run of spaces, or a single
    /// punctuation mark or ideograph: there is a boundary on each side of a word, not only between words. No
    /// dictionary is used, so the scripts that are written without spaces are not split into words.
    /// </para>
    /// <para>The enumeration does not allocate.</para>
    /// </remarks>
    /// <seealso href="https://www.unicode.org/reports/tr29/" />
    public static UnicodeWordEnumerator EnumerateWordBoundaries(ReadOnlySpan<char> text) => new(text);

    /// <summary>Enumerates the sentences of a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>An enumerator of the range of each sentence, in order; the ranges cover the whole text.</returns>
    /// <remarks>
    /// <para>
    /// The boundaries are the default sentence boundaries of Unicode Standard Annex #29 for
    /// <see cref="UnicodeProperties.UnicodeVersion"/>. A sentence ends after a paragraph separator, or after its
    /// terminal punctuation (a full stop, a question mark, an exclamation mark...) with the closing punctuation, the
    /// spaces and the paragraph separator that follow it. A full stop does not end a sentence when it is followed by a
    /// digit, by an uppercase letter without a space between them (<c>U.S.A.</c>), or by a lowercase word
    /// (<c>etc. and</c>). No list of abbreviations is used: <c>Mr. Smith</c> is two sentences.
    /// </para>
    /// <para>The enumeration does not allocate.</para>
    /// </remarks>
    /// <seealso href="https://www.unicode.org/reports/tr29/" />
    public static UnicodeSentenceEnumerator EnumerateSentenceBoundaries(ReadOnlySpan<char> text) => new(text);

    /// <summary>Enumerates the positions where a line can or must break.</summary>
    /// <param name="text">The text.</param>
    /// <param name="strictness">How strict the rules are.</param>
    /// <param name="wordBreak">Whether a line can break inside a word.</param>
    /// <returns>An enumerator of the breaks, in order. The last one is at the end of the text and is mandatory; an empty text has no break.</returns>
    /// <remarks>
    /// <para>
    /// The breaks are those of the Unicode Line Breaking Algorithm (Unicode Standard Annex #14) for
    /// <see cref="UnicodeProperties.UnicodeVersion"/>, whatever the version of the runtime. With the default arguments
    /// the rules are not tailored: the classes that need information from outside the algorithm are resolved as the
    /// annex specifies (rule LB1), so Thai, Lao, Khmer and the other scripts that need a dictionary only break at
    /// spaces, and an object replacement character can break on both sides. The overload that takes a
    /// <see cref="LineBreakDictionary"/> also breaks such text between the words of a word list.
    /// </para>
    /// <para>
    /// A break position is the index of the first character of the next line. A soft hyphen (U+00AD) gives an
    /// opportunity after it like any other: the caller knows it by looking at the character before the break.
    /// </para>
    /// <para>The enumeration does not allocate.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strictness"/> or <paramref name="wordBreak"/> is not a defined value.</exception>
    /// <seealso href="https://www.unicode.org/reports/tr14/" />
    public static UnicodeLineBreakEnumerator EnumerateLineBreaks(ReadOnlySpan<char> text, UnicodeLineBreakStrictness strictness = UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior wordBreak = UnicodeWordBreakBehavior.Normal)
    {
        if (strictness is < UnicodeLineBreakStrictness.Strict or > UnicodeLineBreakStrictness.Anywhere)
            throw new ArgumentOutOfRangeException(nameof(strictness), strictness, "The value is not defined");

        if (wordBreak is < UnicodeWordBreakBehavior.Normal or > UnicodeWordBreakBehavior.KeepAll)
            throw new ArgumentOutOfRangeException(nameof(wordBreak), wordBreak, "The value is not defined");

        return new UnicodeLineBreakEnumerator(text, strictness, wordBreak);
    }

    /// <summary>Enumerates the positions where a line can or must break, with a dictionary for the text that is written without spaces between words.</summary>
    /// <param name="text">The text.</param>
    /// <param name="dictionary">The words of the scripts that are written without spaces between words, or <see langword="null"/> to only apply the rules of the Unicode Line Breaking Algorithm.</param>
    /// <param name="strictness">How strict the rules are.</param>
    /// <param name="wordBreak">Whether a line can break inside a word.</param>
    /// <returns>An enumerator of the breaks, in order. The last one is at the end of the text and is mandatory; an empty text has no break.</returns>
    /// <remarks>
    /// <para>
    /// The breaks are those of <see cref="EnumerateLineBreaks(ReadOnlySpan{char}, UnicodeLineBreakStrictness, UnicodeWordBreakBehavior)"/>,
    /// and the breaks that <paramref name="dictionary"/> finds in the text that the Unicode Line Breaking Algorithm
    /// leaves to a dictionary: the characters whose line breaking class is Complex_Context (SA), those of Thai, Lao,
    /// Khmer, Myanmar and a few other scripts. A break of the dictionary is not mandatory.
    /// </para>
    /// <list type="bullet">
    /// <item><description>A run is a maximal sequence of consecutive Complex_Context characters that starts with a letter: the combining marks before the first letter belong to the character before them.</description></item>
    /// <item><description>Each run is segmented into words as the remarks of <see cref="LineBreakDictionary"/> specify, and a line can break before each word of the dictionary, except at the start of the run. The breaks of a run are those of <see cref="LineBreakDictionary.FindBreaks(ReadOnlySpan{char}, Span{int})"/> for the run.</description></item>
    /// <item><description>The characters of a run that are in no word stay with what precedes them. A break is never inside a grapheme cluster, nor before a combining mark.</description></item>
    /// <item><description>The breaks at the start and at the end of a run are those of the Unicode Line Breaking Algorithm, where a Complex_Context character is a letter or a combining mark (rule LB1). The other characters, such as spaces, digits, punctuation and zero width spaces, are not looked up in the dictionary.</description></item>
    /// <item><description><see cref="UnicodeWordBreakBehavior.KeepAll"/> keeps the breaks of the dictionary, as the <c>word-break: keep-all</c> of CSS does: they are between words, not inside a word.</description></item>
    /// <item><description>With <see cref="UnicodeWordBreakBehavior.BreakAll"/> or <see cref="UnicodeLineBreakStrictness.Anywhere"/> a line already breaks between any two letters or grapheme clusters: the dictionary is not used and the breaks are those of the other overload.</description></item>
    /// </list>
    /// <para>The enumeration does not allocate, and its cost is proportional to the length of the text.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strictness"/> or <paramref name="wordBreak"/> is not a defined value.</exception>
    /// <seealso href="https://www.unicode.org/reports/tr14/" />
    public static UnicodeLineBreakEnumerator EnumerateLineBreaks(ReadOnlySpan<char> text, LineBreakDictionary? dictionary, UnicodeLineBreakStrictness strictness = UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior wordBreak = UnicodeWordBreakBehavior.Normal)
    {
        if (strictness is < UnicodeLineBreakStrictness.Strict or > UnicodeLineBreakStrictness.Anywhere)
            throw new ArgumentOutOfRangeException(nameof(strictness), strictness, "The value is not defined");

        if (wordBreak is < UnicodeWordBreakBehavior.Normal or > UnicodeWordBreakBehavior.KeepAll)
            throw new ArgumentOutOfRangeException(nameof(wordBreak), wordBreak, "The value is not defined");

        return new UnicodeLineBreakEnumerator(text, strictness, wordBreak, dictionary);
    }

    /// <summary>Enumerates the runs of a text that are written in one script.</summary>
    /// <param name="text">The text.</param>
    /// <returns>An enumerator of the runs, in order; the runs cover the whole text.</returns>
    /// <remarks>
    /// <para>
    /// A run is what a text layout engine shapes with the rules and the font of one script. The Unicode Standard
    /// defines the scripts of a character (Unicode Standard Annex #24) and recommends how to resolve the characters
    /// that are shared between scripts, without specifying an algorithm. This method applies those recommendations as follows:
    /// </para>
    /// <list type="bullet">
    /// <item><description>A run never ends inside an extended grapheme cluster: a combining mark has the script of its base.</description></item>
    /// <item><description>A character whose script is <see cref="UnicodeScript.Common"/> or <see cref="UnicodeScript.Inherited"/>, such as a space, a digit or a punctuation mark, belongs to the run before it, or to the run after it at the start of the text.</description></item>
    /// <item><description>The Script_Extensions of a character are honored: a character that is only used with some scripts does not continue a run of another script.</description></item>
    /// <item><description>A closing bracket has the script of the run of its opening bracket, so that both brackets of a pair are in the same script.</description></item>
    /// </list>
    /// <para>The enumeration does not allocate.</para>
    /// </remarks>
    /// <seealso href="https://www.unicode.org/reports/tr24/" />
    public static UnicodeScriptRunEnumerator EnumerateScriptRuns(ReadOnlySpan<char> text) => new(text);

    /// <summary>Reads the code point at an index. An unpaired surrogate is returned as is.</summary>
    internal static int DecodeAt(ReadOnlySpan<char> text, int index, out int length)
    {
        var first = text[index];
        if (char.IsHighSurrogate(first) && (uint)(index + 1) < (uint)text.Length)
        {
            var second = text[index + 1];
            if (char.IsLowSurrogate(second))
            {
                length = 2;
                return char.ConvertToUtf32(first, second);
            }
        }

        length = 1;
        return first;
    }

    /// <summary>Reads the code point that ends before an index. An unpaired surrogate is returned as is.</summary>
    internal static int DecodeBefore(ReadOnlySpan<char> text, int index, out int length)
    {
        var last = text[index - 1];
        if (char.IsLowSurrogate(last) && index >= 2)
        {
            var first = text[index - 2];
            if (char.IsHighSurrogate(first))
            {
                length = 2;
                return char.ConvertToUtf32(first, last);
            }
        }

        length = 1;
        return last;
    }

    /// <summary>Gets the length of the first segment of a text delimited by a word boundary, when it is one of the segments that start with an ASCII character and need no lookup.</summary>
    /// <returns>The length of the segment, or 0 when the rules must be applied: see <see cref="GetWordSegmentLength(ReadOnlySpan{char})"/>.</returns>
    /// <remarks>
    /// Most segments of a text are an ASCII word, a run of spaces or a punctuation mark followed by an ASCII character
    /// that no rule joins to them. The method is small so that it is inlined in the enumerator, and the other segments
    /// do not pay for a call.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetAsciiWordSegmentLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        var first = text[0];
        if (first >= 0x80)
            return 0;

        if (IsAsciiWordCharacter(first))
        {
            // A letter, a digit or a low line (WB5, WB8 to WB10, WB13a, WB13b) is followed by a character that does
            // not join it: an ASCII character other than the punctuation of WB6, WB7, WB11 and WB12.
            var end = GetAsciiWordRunEnd(text, 0);
            return end == text.Length || (text[end] < 0x80 && text[end] is not ('\'' or '.' or ':' or ',' or ';')) ? end : 0;
        }

        if (first == ' ')
            return GetSpaceSegmentLength(text);

        // Any other ASCII character is a segment of its own before an ASCII character: the rules that join it to the
        // next character need a character before it (WB6, WB7, WB11, WB12) or are WB3 (CR LF), WB3d (spaces) and WB4
        // (Extend, Format and ZWJ are not ASCII).
        return first is not '\r' && (text.Length == 1 || text[1] < 0x80) ? 1 : 0;
    }

    /// <summary>Gets the length of a segment that starts with a space.</summary>
    /// <remarks>
    /// WB3d joins the spaces, and WB4 the Extend, Format and ZWJ characters that follow them: no other rule joins a
    /// space to the character after it. The ASCII characters are none of those, and the other ones are looked up.
    /// </remarks>
    private static int GetSpaceSegmentLength(ReadOnlySpan<char> text)
    {
        var end = 1;
        while (end < text.Length && text[end] == ' ')
        {
            end++;
        }

        if (end == text.Length || text[end] < 0x80)
            return end;

        var next = (WordBreak)(UnicodeProperties.GetSegmentationTuple(DecodeAt(text, end, out _)) >> 8);
        return next is WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ or WordBreak.WSegSpace ? 0 : end;
    }

    /// <summary>Gets the length of the first segment of a text delimited by a word boundary.</summary>
    internal static int GetWordSegmentLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        var tuple = UnicodeProperties.GetSegmentationTuple(DecodeAt(text, 0, out var index));

        // WB4: the class before the break, ignoring the Extend, Format and ZWJ that follow a character. The actual
        // previous class is still needed by the rules that come before WB4.
        var actualPrevious = (WordBreak)(tuple >> 8);
        var previous = actualPrevious;
        var beforePrevious = WordBreak.Other;
        var hasBeforePrevious = false;

        // WB15 and WB16: the number of regional indicators that end at the previous character.
        var regionalIndicators = previous is WordBreak.RegionalIndicator ? 1 : 0;

        while (index < text.Length)
        {
            // A run of ASCII letters, digits and low lines after a letter, a digit or a low line is part of the
            // segment (rules WB5, WB8, WB9, WB10, WB13a and WB13b): skip it with a vectorized search.
            if (previous is WordBreak.ALetter or WordBreak.HebrewLetter or WordBreak.Numeric or WordBreak.ExtendNumLet
                && IsAsciiWordCharacter(text[index]))
            {
                var end = GetAsciiWordRunEnd(text, index);
                beforePrevious = end - index >= 2 ? GetAsciiWordBreak(text[end - 2]) : previous;
                hasBeforePrevious = true;
                previous = GetAsciiWordBreak(text[end - 1]);
                actualPrevious = previous;
                regionalIndicators = 0;
                index = end;
                continue;
            }

            tuple = UnicodeProperties.GetSegmentationTuple(DecodeAt(text, index, out var length));
            var next = (WordBreak)(tuple >> 8);

            if (actualPrevious is WordBreak.CR && next is WordBreak.LF)
            {
                // WB3
            }
            else if (actualPrevious is WordBreak.Newline or WordBreak.CR or WordBreak.LF
                || next is WordBreak.Newline or WordBreak.CR or WordBreak.LF)
            {
                // WB3a, WB3b
                break;
            }
            else if ((actualPrevious is WordBreak.ZWJ && (tuple & 0x80) != 0)
                || (actualPrevious is WordBreak.WSegSpace && next is WordBreak.WSegSpace))
            {
                // WB3c, WB3d
            }
            else if (next is WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ)
            {
                // WB4: the character is ignored by the rules that follow.
                actualPrevious = next;
                index += length;
                continue;
            }
            else if (!IsWordJoined(hasBeforePrevious ? beforePrevious : WordBreak.Other, previous, next, text, index + length, regionalIndicators))
            {
                break;
            }

            if (next is not (WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ))
            {
                beforePrevious = previous;
                hasBeforePrevious = true;
                previous = next;
                regionalIndicators = next is WordBreak.RegionalIndicator ? regionalIndicators + 1 : 0;
            }

            actualPrevious = next;
            index += length;
        }

        return index;
    }

    /// <summary>Gets the kind of a segment delimited by word boundaries that ends with an ASCII letter or digit, without a lookup.</summary>
    /// <returns><see langword="false"/> when the segment does not end with one: see <see cref="GetWordKind(ReadOnlySpan{char})"/>.</returns>
    /// <remarks>The kind is the one of the last character that WB4 does not ignore, and no ASCII character is ignored.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetAsciiWordKind(ReadOnlySpan<char> segment, out UnicodeWordKind kind)
    {
        if (!segment.IsEmpty)
        {
            var last = segment[^1];
            if (char.IsAsciiLetter(last))
            {
                kind = UnicodeWordKind.Letter;
                return true;
            }

            if (char.IsAsciiDigit(last))
            {
                kind = UnicodeWordKind.Number;
                return true;
            }
        }

        kind = UnicodeWordKind.None;
        return false;
    }

    /// <summary>Gets the kind of a segment delimited by word boundaries: see <see cref="UnicodeWordKind"/>.</summary>
    internal static UnicodeWordKind GetWordKind(ReadOnlySpan<char> segment)
    {
        var index = FindLastWordBase(segment, segment.Length, out var codePoint, out var wordBreak);
        if (index < 0)
            return UnicodeWordKind.None;

        switch (wordBreak)
        {
            case WordBreak.ExtendNumLet:
                // ICU (rules 13a and 13b): the kind of the character before it, a letter after another ExtendNumLet, not
                // a word on its own.
                if (FindLastWordBase(segment, index, out codePoint, out wordBreak) < 0)
                    return UnicodeWordKind.None;

                return wordBreak is WordBreak.ExtendNumLet ? UnicodeWordKind.Letter : UnicodeProperties.GetWordKind(codePoint);

            case WordBreak.SingleQuote:
                // ICU (rule 7a): a Hebrew letter and a single quote are a word.
                return FindLastWordBase(segment, index, out _, out wordBreak) >= 0 && wordBreak is WordBreak.HebrewLetter ? UnicodeWordKind.Letter : UnicodeWordKind.None;

            default:
                return UnicodeProperties.GetWordKind(codePoint);
        }
    }

    /// <summary>Finds the last code point before an index that rule WB4 does not ignore.</summary>
    /// <returns>The index of the code point, or -1 when there is none.</returns>
    private static int FindLastWordBase(ReadOnlySpan<char> segment, int index, out int codePoint, out WordBreak wordBreak)
    {
        while (index > 0)
        {
            codePoint = DecodeBefore(segment, index, out var length);
            index -= length;
            wordBreak = UnicodeProperties.GetWordBreak(codePoint);
            if (wordBreak is not (WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ))
                return index;
        }

        codePoint = 0;
        wordBreak = WordBreak.Other;
        return -1;
    }

    private static bool IsAsciiWordCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';

    // Not inlined: it would make the loop of GetWordSegmentLength slower for the text it does not apply to. The runs
    // are words: a loop is faster than a vectorized search for them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int GetAsciiWordRunEnd(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length && IsAsciiWordCharacter(text[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>Gets the Word_Break value of an ASCII letter, digit or low line.</summary>
    private static WordBreak GetAsciiWordBreak(char value) => value switch
    {
        '_' => WordBreak.ExtendNumLet,
        >= '0' and <= '9' => WordBreak.Numeric,
        _ => WordBreak.ALetter,
    };

    /// <summary>Applies the rules WB5 to WB16 to the classes around a position.</summary>
    private static bool IsWordJoined(WordBreak beforePrevious, WordBreak previous, WordBreak next, ReadOnlySpan<char> text, int afterNext, int regionalIndicators)
    {
        if (IsLetter(previous))
        {
            // WB5, WB9
            if (IsLetter(next) || next is WordBreak.Numeric)
                return true;

            // WB6
            if (next is WordBreak.MidLetter or WordBreak.MidNumLet or WordBreak.SingleQuote && IsLetter(PeekWordBreak(text, afterNext)))
                return true;

            if (previous is WordBreak.HebrewLetter)
            {
                // WB7a
                if (next is WordBreak.SingleQuote)
                    return true;

                // WB7b
                if (next is WordBreak.DoubleQuote && PeekWordBreak(text, afterNext) is WordBreak.HebrewLetter)
                    return true;
            }
        }

        // WB7
        if (IsLetter(beforePrevious) && previous is WordBreak.MidLetter or WordBreak.MidNumLet or WordBreak.SingleQuote && IsLetter(next))
            return true;

        // WB7c
        if (beforePrevious is WordBreak.HebrewLetter && previous is WordBreak.DoubleQuote && next is WordBreak.HebrewLetter)
            return true;

        if (previous is WordBreak.Numeric)
        {
            // WB8, WB10
            if (next is WordBreak.Numeric || IsLetter(next))
                return true;

            // WB12
            if (next is WordBreak.MidNum or WordBreak.MidNumLet or WordBreak.SingleQuote && PeekWordBreak(text, afterNext) is WordBreak.Numeric)
                return true;
        }

        // WB11
        if (beforePrevious is WordBreak.Numeric && previous is WordBreak.MidNum or WordBreak.MidNumLet or WordBreak.SingleQuote && next is WordBreak.Numeric)
            return true;

        // WB13
        if (previous is WordBreak.Katakana && next is WordBreak.Katakana)
            return true;

        // WB13a
        if (next is WordBreak.ExtendNumLet && (IsLetter(previous) || previous is WordBreak.Numeric or WordBreak.Katakana or WordBreak.ExtendNumLet))
            return true;

        // WB13b
        if (previous is WordBreak.ExtendNumLet && (IsLetter(next) || next is WordBreak.Numeric or WordBreak.Katakana))
            return true;

        // WB15, WB16
        return previous is WordBreak.RegionalIndicator && next is WordBreak.RegionalIndicator && (regionalIndicators & 1) != 0;

        static bool IsLetter(WordBreak value) => value is WordBreak.ALetter or WordBreak.HebrewLetter;
    }

    /// <summary>Gets the class of the next character that WB4 does not ignore, or Other at the end of the text.</summary>
    private static WordBreak PeekWordBreak(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            var value = (WordBreak)(UnicodeProperties.GetSegmentationTuple(DecodeAt(text, index, out var length)) >> 8);
            if (value is not (WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ))
                return value;

            index += length;
        }

        return WordBreak.Other;
    }

    /// <summary>Gets the length of the first sentence of a text.</summary>
    internal static int GetSentenceLength(ReadOnlySpan<char> text) => GetSentenceLength(text, skipContinuations: true);

    /// <summary>Gets the length of the first sentence of a text, applying the rules to every code point: the reference of the tests of <see cref="SkipSentenceContinuation"/>.</summary>
    internal static int GetSentenceLengthByRules(ReadOnlySpan<char> text) => GetSentenceLength(text, skipContinuations: false);

    private static int GetSentenceLength(ReadOnlySpan<char> text, bool skipContinuations)
    {
        if (text.IsEmpty)
            return 0;

        // The class of the previous character, then the classes of the last two characters SB5 does not ignore: an
        // Extend or a Format after another character takes the class of that character.
        var actualPrevious = UnicodeProperties.GetSentenceBreak(DecodeAt(text, 0, out var index));
        var previous = actualPrevious;
        var beforePrevious = SentenceBreak.Other;

        // SB8 to SB11: the terminal punctuation (ATerm or STerm) that starts the sequence SATerm Close* Sp* the text
        // ends with, or Other when it does not end with such a sequence, and whether the sequence has a space.
        var terminal = previous is SentenceBreak.ATerm or SentenceBreak.STerm ? previous : SentenceBreak.Other;
        var hasSpace = false;

        while (index < text.Length)
        {
            if (skipContinuations && terminal is SentenceBreak.Other && actualPrevious is not (SentenceBreak.Sep or SentenceBreak.CR or SentenceBreak.LF))
            {
                // Most of a sentence is not after a terminal punctuation: only a paragraph separator or a terminal
                // punctuation can change that.
                index = SkipSentenceContinuation(text, index, ref previous);
                if (index >= text.Length)
                    break;

                actualPrevious = previous;
            }

            var next = UnicodeProperties.GetSentenceBreak(DecodeAt(text, index, out var length));
            if (actualPrevious is SentenceBreak.CR && next is SentenceBreak.LF)
            {
                // SB3
            }
            else if (actualPrevious is SentenceBreak.Sep or SentenceBreak.CR or SentenceBreak.LF)
            {
                // SB4
                break;
            }
            else if (next is SentenceBreak.Extend or SentenceBreak.Format)
            {
                // SB5: the character is ignored by the rules that follow.
                actualPrevious = next;
                index += length;
                continue;
            }
            else if (!IsSentenceJoined(beforePrevious, previous, next, terminal, hasSpace, text, index))
            {
                break;
            }

            if (next is SentenceBreak.ATerm or SentenceBreak.STerm)
            {
                terminal = next;
                hasSpace = false;
            }
            else if (terminal is not SentenceBreak.Other && next is SentenceBreak.Sp)
            {
                hasSpace = true;
            }
            else if (next is not SentenceBreak.Close || hasSpace)
            {
                terminal = SentenceBreak.Other;
            }

            beforePrevious = previous;
            previous = next;
            actualPrevious = next;
            index += length;
        }

        return index;
    }

    // The classes of the ASCII characters, and the classes that end SkipSentenceContinuation.
    private static readonly byte[] AsciiSentenceBreaks = CreateAsciiSentenceBreaks();
    private const int SentenceContinuationStops = (1 << (int)SentenceBreak.ATerm) | (1 << (int)SentenceBreak.STerm) | (1 << (int)SentenceBreak.Sep) | (1 << (int)SentenceBreak.CR) | (1 << (int)SentenceBreak.LF);

    /// <summary>Skips the code points that the rules join to the text before them when it does not end with a terminal punctuation, a paragraph separator or a CR.</summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index of the first code point to read.</param>
    /// <param name="previous">The class of the last code point that SB5 does not ignore; updated with the code points that are skipped.</param>
    /// <returns>The index of the first code point that is a terminal punctuation or a paragraph separator, or the length of the text.</returns>
    /// <remarks>
    /// When the text before a position does not end with SATerm Close* Sp* nor with a paragraph separator, rules SB3 and
    /// SB4 do not apply, and SB5, SB6, SB7 and SB998 join the next code point to it. Only an ATerm, an STerm or a
    /// paragraph separator changes that. The class before the stop is all the general loop needs: SB7 reads the class
    /// before an ATerm only once the ATerm is the previous code point.
    /// </remarks>
    private static int SkipSentenceContinuation(ReadOnlySpan<char> text, int index, ref SentenceBreak previous)
    {
        var classes = AsciiSentenceBreaks;
        while ((uint)index < (uint)text.Length)
        {
            var c = text[index];
            if (c < (uint)classes.Length)
            {
                var start = index;
                do
                {
                    if ((SentenceContinuationStops & (1 << classes[c])) != 0)
                        break;

                    index++;
                }
                while ((uint)index < (uint)text.Length && (c = text[index]) < (uint)classes.Length);

                if (index > start)
                {
                    UpdatePrevious(classes[text[index - 1]], ref previous);
                }

                if ((uint)index >= (uint)text.Length || c < (uint)classes.Length)
                    break;
            }

            int value;
            if (!char.IsSurrogate(c))
            {
                value = (int)UnicodeProperties.GetSentenceBreak(c);
                if ((SentenceContinuationStops & (1 << value)) != 0)
                    break;

                index++;
            }
            else
            {
                value = (int)UnicodeProperties.GetSentenceBreak(DecodeAt(text, index, out var length));
                if ((SentenceContinuationStops & (1 << value)) != 0)
                    break;

                index += length;
            }

            UpdatePrevious(value, ref previous);
        }

        return index;

        static void UpdatePrevious(int value, ref SentenceBreak previous)
        {
            if ((SentenceBreak)value is not (SentenceBreak.Extend or SentenceBreak.Format))
            {
                previous = (SentenceBreak)value;
            }
        }
    }

    private static byte[] CreateAsciiSentenceBreaks()
    {
        var result = new byte[128];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (byte)UnicodeProperties.GetSentenceBreak(i);
        }

        return result;
    }

    /// <summary>Applies the rules SB6 to SB11 to the classes around a position.</summary>
    private static bool IsSentenceJoined(SentenceBreak beforePrevious, SentenceBreak previous, SentenceBreak next, SentenceBreak terminal, bool hasSpace, ReadOnlySpan<char> text, int index)
    {
        // SB6
        if (previous is SentenceBreak.ATerm && next is SentenceBreak.Numeric)
            return true;

        // SB7
        if (beforePrevious is SentenceBreak.Upper or SentenceBreak.Lower && previous is SentenceBreak.ATerm && next is SentenceBreak.Upper)
            return true;

        // SB998: the rules that follow only apply after SATerm Close* Sp*.
        if (terminal is SentenceBreak.Other)
            return true;

        // SB8a
        if (next is SentenceBreak.SContinue or SentenceBreak.STerm or SentenceBreak.ATerm)
            return true;

        // SB9
        if (!hasSpace && next is SentenceBreak.Close)
            return true;

        // SB9, SB10
        if (next is SentenceBreak.Sp or SentenceBreak.Sep or SentenceBreak.CR or SentenceBreak.LF)
            return true;

        // SB8 is applied last because it looks ahead: when it does not apply, SB11 breaks, and when it does, the text
        // no longer ends with SATerm Close* Sp*, so the lookahead is never repeated over the same characters.
        return terminal is SentenceBreak.ATerm && IsFollowedByLowercase(text, index);
    }

    /// <summary>
    /// Determines whether a text continues with [^OLetter Upper Lower ParaSep SATerm]* Lower: whether the
    /// full stop before it is followed by a lowercase word (rule SB8).
    /// </summary>
    private static bool IsFollowedByLowercase(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            switch (UnicodeProperties.GetSentenceBreak(DecodeAt(text, index, out var length)))
            {
                case SentenceBreak.Lower:
                    return true;

                case SentenceBreak.OLetter or SentenceBreak.Upper or SentenceBreak.Sep or SentenceBreak.CR or SentenceBreak.LF or SentenceBreak.STerm or SentenceBreak.ATerm:
                    return false;
            }

            index += length;
        }

        return false;
    }
}
