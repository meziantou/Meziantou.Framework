using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Breaks a text into lines that fit a width, at the break opportunities of the Unicode line breaking algorithm
/// (UAX #14): after the spaces, after the hyphens, between ideographs. A word that does not fit is hyphenated when a
/// hyphenator is given, and a word wider than the width alone is broken between its grapheme clusters. The mandatory
/// breaks of the text (line feeds, paragraph separators) end the paragraphs.
/// </summary>
internal static class PdfTextWrapper
{
    private const char SoftHyphen = '\u00AD';
    private const string Hyphen = "-";

    /// <summary>A line: the text to show (without the spaces around it, with a hyphen when a word is broken), the position of its first character in the text, and whether it ends a paragraph.</summary>
    internal readonly record struct Line(string Text, int Start, bool EndsParagraph);

    public static List<Line> Wrap(string text, PdfFont font, double fontSize, double width, IPdfHyphenator? hyphenator = null)
    {
        var context = new WrapContext(text, font, fontSize, width, hyphenator, font.CanEncode(Hyphen));
        if (text.Length == 0)
        {
            context.Lines.Add(new Line("", 0, EndsParagraph: true));
            return context.Lines;
        }

        var opportunities = new List<int>();
        var paragraphStart = 0;
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, UnicodeLineBreakStrictness.Normal))
        {
            if (!lineBreak.IsMandatory)
            {
                opportunities.Add(lineBreak.Index);
                continue;
            }

            var paragraphEnd = lineBreak.Index;
            while (paragraphEnd > paragraphStart && IsMandatoryBreak(text[paragraphEnd - 1]))
            {
                paragraphEnd--;
            }

            context.WrapParagraph(paragraphStart, paragraphEnd, opportunities);
            opportunities.Clear();
            paragraphStart = lineBreak.Index;
        }

        return context.Lines;
    }

    /// <summary>Gets the widest text of a paragraph without breaks, and the widest text between two break opportunities, for the automatic widths of the columns of a table.</summary>
    public static (double Min, double Max) MeasureWidths(string text, PdfFont font, double fontSize)
    {
        var canShowHyphen = font.CanEncode(Hyphen);
        var min = 0d;
        var max = 0d;
        var segmentStart = 0;
        var paragraphStart = 0;
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, UnicodeLineBreakStrictness.Normal))
        {
            min = Math.Max(min, font.GetTextWidth(GetLineText(text, segmentStart, lineBreak.Index, canShowHyphen), fontSize));
            segmentStart = lineBreak.Index;
            if (lineBreak.IsMandatory)
            {
                max = Math.Max(max, font.GetTextWidth(GetLineText(text, paragraphStart, lineBreak.Index, hyphenAtSoftHyphen: false), fontSize));
                paragraphStart = lineBreak.Index;
            }
        }

        return (min, max);
    }

    /// <summary>Gets the text shown for the characters of a line: without the spaces and the mandatory breaks at its ends, and without soft hyphens except the last one, shown as a hyphen.</summary>
    private static string GetLineText(string text, int start, int end, bool hyphenAtSoftHyphen)
    {
        while (start < end && IsSpace(text[start]))
        {
            start++;
        }

        while (end > start && (IsSpace(text[end - 1]) || IsMandatoryBreak(text[end - 1])))
        {
            end--;
        }

        var span = text.AsSpan(start, end - start);
        var hyphenate = hyphenAtSoftHyphen && span.Length > 0 && span[^1] == SoftHyphen;
        if (!span.Contains(SoftHyphen))
            return span.ToString();

        var builder = new StringBuilder(span.Length);
        foreach (var c in span)
        {
            if (c != SoftHyphen)
            {
                builder.Append(c);
            }
        }

        if (hyphenate)
        {
            builder.Append('-');
        }

        return builder.ToString();
    }

    // The spaces after which UAX #14 allows a break (classes SP, ZW and the spaces of BA): they are not shown at the end of a line
    private static bool IsSpace(char c) => c is ' ' or '\t' or '\u200B' or '\u1680' or '\u205F' or '\u3000' or (>= '\u2000' and <= '\u200A' and not '\u2007');

    // The characters that end a paragraph (classes BK, CR, LF and NL)
    private static bool IsMandatoryBreak(char c) => c is '\n' or '\r' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029';

    private static bool IsWordCharacter(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or
        UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter or
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark;

    private sealed class WrapContext(string text, PdfFont font, double fontSize, double width, IPdfHyphenator? hyphenator, bool canShowHyphen)
    {
        public List<Line> Lines { get; } = [];

        public void WrapParagraph(int start, int end, List<int> opportunities)
        {
            var first = Lines.Count;
            var lineStart = start;
            while (lineStart < end && IsSpace(text[lineStart]))
            {
                lineStart++;
            }

            // The break opportunities after the start of the line, then the end of the paragraph
            var candidates = new List<int>(opportunities.Count + 1);
            foreach (var opportunity in opportunities)
            {
                if (opportunity > lineStart && opportunity < end)
                {
                    candidates.Add(opportunity);
                }
            }

            candidates.Add(end);

            // The last break opportunity where the line fits, or -1
            var fittingEnd = -1;
            var index = 0;
            while (index < candidates.Count)
            {
                var candidate = candidates[index];
                if (Fits(lineStart, candidate))
                {
                    fittingEnd = candidate;
                    index++;
                    continue;
                }

                // The text from the last fitting break to the candidate does not fit: hyphenate its word, end the line at
                // the last fitting break, or break the text between its grapheme clusters when it is alone on the line
                var segmentStart = fittingEnd >= 0 ? fittingEnd : lineStart;
                if (TryHyphenate(lineStart, segmentStart, candidate, out var hyphenIndex))
                {
                    AddLine(lineStart, GetLineText(text, lineStart, hyphenIndex, hyphenAtSoftHyphen: false) + Hyphen, endsParagraph: false);
                    lineStart = hyphenIndex;
                }
                else if (fittingEnd >= 0)
                {
                    AddLine(lineStart, GetLineText(text, lineStart, fittingEnd, hyphenAtSoftHyphen: canShowHyphen), endsParagraph: false);
                    lineStart = fittingEnd;
                }
                else
                {
                    var pieceEnd = GetFittingClusters(lineStart, candidate);
                    AddLine(lineStart, GetLineText(text, lineStart, pieceEnd, hyphenAtSoftHyphen: false), endsParagraph: false);
                    lineStart = pieceEnd;
                }

                fittingEnd = -1;
            }

            var lastText = GetLineText(text, lineStart, end, hyphenAtSoftHyphen: false);
            if (lastText.Length > 0 || Lines.Count == first)
            {
                AddLine(lineStart, lastText, endsParagraph: true);
            }
            else
            {
                Lines[^1] = Lines[^1] with { EndsParagraph = true };
            }
        }

        private void AddLine(int start, string lineText, bool endsParagraph) => Lines.Add(new Line(lineText, start, endsParagraph));

        private bool Fits(int start, int end)
        {
            var lineText = GetLineText(text, start, end, hyphenAtSoftHyphen: canShowHyphen);
            return lineText.Length == 0 || font.GetTextWidth(lineText, fontSize) <= width;
        }

        // The end of the longest run of grapheme clusters from the start that fits, at least one cluster
        private int GetFittingClusters(int start, int end)
        {
            var span = text.AsSpan(start, end - start);
            var result = -1;
            foreach (var cluster in Unicode.EnumerateGraphemeClusters(span))
            {
                var clusterEnd = start + cluster.End.GetOffset(span.Length);
                if (result >= 0 && font.GetTextWidth(GetLineText(text, start, clusterEnd, hyphenAtSoftHyphen: false), fontSize) > width)
                    break;

                result = clusterEnd;
            }

            return result < 0 ? end : result;
        }

        // Finds the last position of the word of a segment where the line, ended by a hyphen, still fits
        private bool TryHyphenate(int lineStart, int segmentStart, int segmentEnd, out int hyphenIndex)
        {
            hyphenIndex = -1;
            if (hyphenator is null || !canShowHyphen)
                return false;

            var wordStart = segmentStart;
            while (wordStart < segmentEnd && !IsWordCharacterAt(wordStart))
            {
                wordStart++;
            }

            var wordEnd = wordStart;
            while (wordEnd < segmentEnd && IsWordCharacterAt(wordEnd))
            {
                wordEnd += char.IsHighSurrogate(text[wordEnd]) && wordEnd + 1 < segmentEnd ? 2 : 1;
            }

            if (wordEnd - wordStart < 2)
                return false;

            var word = text.AsSpan(wordStart, wordEnd - wordStart);
            var positions = new int[word.Length];
            var count = hyphenator.Hyphenate(word, positions);
            if (count < 0 || count > positions.Length)
                throw new InvalidOperationException("The hyphenator returned an invalid number of positions");

            for (var i = count - 1; i >= 0; i--)
            {
                var position = positions[i];
                if (position <= 0 || position >= word.Length)
                    continue;

                var candidate = wordStart + position;
                if (candidate <= lineStart)
                    continue;

                if (font.GetTextWidth(GetLineText(text, lineStart, candidate, hyphenAtSoftHyphen: false) + Hyphen, fontSize) <= width)
                {
                    hyphenIndex = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool IsWordCharacterAt(int index) => Rune.TryGetRuneAt(text, index, out var rune) && IsWordCharacter(rune);
    }
}
