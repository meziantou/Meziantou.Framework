using System.Globalization;
using System.Runtime.CompilerServices;
using WordBreak = Meziantou.Framework.UnicodeWordBreak;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// Word boundaries (https://www.unicode.org/reports/tr29/#Word_Boundaries), used to move the selection by words
/// (<c>Selection.modify()</c> with the <c>word</c> granularity, Control+Arrow keys). The default rules are tailored as
/// the browsers do (with ICU): the transition between Hangul and the other letters is a boundary (e.g. "희진DJ" is two
/// words). There is no dictionary segmentation: each ideograph is a word, and the scripts without spaces (Thai, Lao...)
/// are one word per run of letters.
/// </summary>
internal static class WordBoundaries
{
    /// <summary>
    /// Sets <c>boundaries[i]</c> to whether there is a word boundary before <c>text[i]</c> (never inside a surrogate pair). The
    /// boundary at the start of the text is included, the one at the end is not. When <paramref name="tailored"/> is false,
    /// the default rules of UAX #29 are applied (the conformance tests).
    /// </summary>
    public static void GetBoundaries(ReadOnlySpan<char> text, Span<bool> boundaries, bool tailored = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundaries.Length, text.Length);
        if (text.IsEmpty)
            return;

        // The code points, and the "effective" sequence of WB4: the Extend, Format and ZWJ characters are attached to the
        // preceding character (except after the start of the text, CR, LF and Newline)
        var count = 0;
        for (var i = 0; i < text.Length; count++)
        {
            GraphemeClusters.ReadCodePoint(text, i, out var length);
            i += length;
        }

        var starts = count <= 256 ? stackalloc int[count] : new int[count];
        var properties = count <= 256 ? stackalloc byte[count] : new byte[count];
        var bases = count <= 256 ? stackalloc int[count] : new int[count]; // the index of the effective character of each code point
        var effective = count <= 256 ? stackalloc int[count] : new int[count]; // the code point of each effective character
        var effectiveCount = 0;
        for (int i = 0, index = 0; i < text.Length; index++)
        {
            var codePoint = GraphemeClusters.ReadCodePoint(text, i, out var length);
            starts[index] = i;
            properties[index] = GetWordBreakProperties(codePoint);
            var wordBreak = (WordBreak)(properties[index] & 0x1F);
            if (index > 0 && IsIgnorable(wordBreak) && (WordBreak)(properties[effective[effectiveCount - 1]] & 0x1F) is not (WordBreak.CR or WordBreak.LF or WordBreak.Newline))
            {
                bases[index] = effectiveCount - 1;
            }
            else
            {
                bases[index] = effectiveCount;
                effective[effectiveCount++] = index;
            }

            i += length;
            if (length == 2)
            {
                boundaries[i - 1] = false;
            }
        }

        boundaries[0] = true;
        for (var index = 1; index < count; index++)
        {
            boundaries[starts[index]] = IsBoundary(text, starts, properties, bases, effective[..effectiveCount], index, tailored);
        }
    }

    /// <summary>
    /// Returns the position of the caret moved by a word from <paramref name="index"/> (the behavior of the browsers on macOS and
    /// Linux): forward to the end of the word that contains the position or of the next word, backward to the start of the word
    /// that contains the position or of the previous word. The segments that are not words (spaces, punctuation) are crossed:
    /// the end (or the start) of the text when there is no word.
    /// </summary>
    public static int Move(string text, int index, bool forward)
    {
        var isBoundary = text.Length <= 1024 ? stackalloc bool[text.Length] : new bool[text.Length];
        GetBoundaries(text, isBoundary);
        if (forward)
        {
            // The segments that end after the position
            var start = 0;
            for (var end = 1; end <= text.Length; end++)
            {
                if (end < text.Length && !isBoundary[end])
                    continue;

                if (end > index && IsWordLike(text.AsSpan(start, end - start)))
                    return end;

                start = end;
            }

            return text.Length;
        }

        // The segments that start before the position
        var segmentEnd = text.Length;
        for (var segmentStart = text.Length - 1; segmentStart >= 0; segmentStart--)
        {
            if (!isBoundary[segmentStart])
                continue;

            if (segmentStart < index && IsWordLike(text.AsSpan(segmentStart, segmentEnd - segmentStart)))
                return segmentStart;

            segmentEnd = segmentStart;
        }

        return 0;
    }

    /// <summary>
    /// Whether a segment between two word boundaries is a word, which the movements by words stop at: it contains a letter, a
    /// number or an ideograph (the segments of spaces, punctuation and symbols are skipped, e.g. "~>>p+++" is one word).
    /// </summary>
    public static bool IsWordLike(ReadOnlySpan<char> segment)
    {
        for (var i = 0; i < segment.Length;)
        {
            var codePoint = GraphemeClusters.ReadCodePoint(segment, i, out var length);
            i += length;
            var wordBreak = UnicodeProperties.GetWordBreak(codePoint);
            if (wordBreak is WordBreak.ALetter or WordBreak.HebrewLetter or WordBreak.Numeric or WordBreak.Katakana)
                return true;

            var category = UnicodeProperties.GetGeneralCategory(codePoint);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber)
                return true;
        }

        return false;
    }

    /// <summary>The packed word break properties of a code point: Word_Break (bits 0-4) and Extended_Pictographic (bit 5).</summary>
    private static byte GetWordBreakProperties(int codePoint) => (byte)((int)UnicodeProperties.GetWordBreak(codePoint) | (UnicodeEmoji.IsExtendedPictographic(codePoint) ? 0x20 : 0));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIgnorable(WordBreak value) => value is WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ;

    private static bool IsAHLetter(WordBreak value) => value is WordBreak.ALetter or WordBreak.HebrewLetter;

    private static bool IsMidNumLetQ(WordBreak value) => value is WordBreak.MidNumLet or WordBreak.SingleQuote;

    /// <summary>Whether there is a boundary before the code point <paramref name="index"/> (https://www.unicode.org/reports/tr29/#Word_Boundary_Rules).</summary>
    private static bool IsBoundary(ReadOnlySpan<char> text, ReadOnlySpan<int> starts, ReadOnlySpan<byte> properties, ReadOnlySpan<int> bases, ReadOnlySpan<int> effective, int index, bool tailored)
    {
        var previous = (WordBreak)(properties[index - 1] & 0x1F);
        var next = (WordBreak)(properties[index] & 0x1F);

        // WB3: CR × LF
        if (previous == WordBreak.CR && next == WordBreak.LF)
            return false;

        // WB3a, WB3b
        if (previous is WordBreak.Newline or WordBreak.CR or WordBreak.LF || next is WordBreak.Newline or WordBreak.CR or WordBreak.LF)
            return true;

        // WB3c: ZWJ × \p{Extended_Pictographic}
        if (previous == WordBreak.ZWJ && (properties[index] & 0x20) != 0)
            return false;

        // WB3d: WSegSpace × WSegSpace
        if (previous == WordBreak.WSegSpace && next == WordBreak.WSegSpace)
            return false;

        // WB4: X (Extend | Format | ZWJ)* → X
        if (bases[index] == bases[index - 1])
            return false;

        // The rules after WB4 apply to the effective characters
        var position = bases[index];
        var left = Get(properties, effective, position - 1);
        var right = Get(properties, effective, position);
        var leftLeft = Get(properties, effective, position - 2);
        var rightRight = Get(properties, effective, position + 1);

        // WB5: AHLetter × AHLetter (tailoring: the transition between Hangul and the other letters is a boundary, as in ICU)
        if (IsAHLetter(left) && IsAHLetter(right))
            return tailored && IsHangul(text, starts, effective[position - 1]) != IsHangul(text, starts, effective[position]);

        // WB6, WB7
        if (IsAHLetter(left) && (right == WordBreak.MidLetter || IsMidNumLetQ(right)) && IsAHLetter(rightRight))
            return false;

        if (IsAHLetter(leftLeft) && (left == WordBreak.MidLetter || IsMidNumLetQ(left)) && IsAHLetter(right))
            return false;

        // WB7a, WB7b, WB7c
        if (left == WordBreak.HebrewLetter && right == WordBreak.SingleQuote)
            return false;

        if (left == WordBreak.HebrewLetter && right == WordBreak.DoubleQuote && rightRight == WordBreak.HebrewLetter)
            return false;

        if (leftLeft == WordBreak.HebrewLetter && left == WordBreak.DoubleQuote && right == WordBreak.HebrewLetter)
            return false;

        // WB8, WB9, WB10
        if ((left == WordBreak.Numeric || IsAHLetter(left)) && (right == WordBreak.Numeric || IsAHLetter(right)))
            return false;

        // WB11, WB12
        if (leftLeft == WordBreak.Numeric && (left == WordBreak.MidNum || IsMidNumLetQ(left)) && right == WordBreak.Numeric)
            return false;

        if (left == WordBreak.Numeric && (right == WordBreak.MidNum || IsMidNumLetQ(right)) && rightRight == WordBreak.Numeric)
            return false;

        // WB13: Katakana × Katakana
        if (left == WordBreak.Katakana && right == WordBreak.Katakana)
            return false;

        // WB13a, WB13b
        if ((IsAHLetter(left) || left is WordBreak.Numeric or WordBreak.Katakana or WordBreak.ExtendNumLet) && right == WordBreak.ExtendNumLet)
            return false;

        if (left == WordBreak.ExtendNumLet && (IsAHLetter(right) || right is WordBreak.Numeric or WordBreak.Katakana))
            return false;

        // WB15, WB16: do not break within emoji flag sequences (an odd number of regional indicators before the position)
        if (left == WordBreak.RegionalIndicator && right == WordBreak.RegionalIndicator)
        {
            var regionalIndicators = 0;
            for (var i = position - 1; i >= 0 && Get(properties, effective, i) == WordBreak.RegionalIndicator; i--)
            {
                regionalIndicators++;
            }

            return regionalIndicators % 2 == 0;
        }

        // WB999
        return true;

        static WordBreak Get(ReadOnlySpan<byte> properties, ReadOnlySpan<int> effective, int position)
            => position >= 0 && position < effective.Length ? (WordBreak)(properties[effective[position]] & 0x1F) : WordBreak.Other;

        static bool IsHangul(ReadOnlySpan<char> text, ReadOnlySpan<int> starts, int index)
            => CharacterProperties.IsHangul(GraphemeClusters.ReadCodePoint(text, starts[index], out _));
    }
}
