using System.Text;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>Fits texts in the space a layout gives them: truncation with an ellipsis, and wrapping on several lines.</summary>
public static class TextFitting
{
    /// <summary>The ellipsis <see cref="Truncate"/> appends, <c>…</c> (U+2026).</summary>
    public const string Ellipsis = "…";

    /// <summary>Shortens a text by whole grapheme clusters and appends an ellipsis, so it fits a width.</summary>
    /// <param name="measurer">The measurer of the font that draws the text.</param>
    /// <param name="text">The text, on one line.</param>
    /// <param name="font">The font.</param>
    /// <param name="maxWidth">The available width.</param>
    /// <returns>The text when it fits, the shortened text, or <see langword="null"/> when not even the first character fits with the ellipsis.</returns>
    public static string? Truncate(ITextMeasurer measurer, string text, SceneFont font, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(text);
        if (measurer.Measure(text, font).Width <= maxWidth)
            return text;

        var boundaries = GetGraphemeClusterEnds(text);

        // The longest prefix that fits with the ellipsis, found by bisection
        var low = 0;
        var high = boundaries.Count - 1;
        string? best = null;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidate = string.Concat(text.AsSpan(0, boundaries[middle - 1]).TrimEnd(), Ellipsis);
            if (measurer.Measure(candidate, font).Width <= maxWidth)
            {
                best = candidate;
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }

    /// <summary>
    /// Splits a text into lines that fit a width. The text is broken at the line break opportunities of the Unicode line
    /// breaking algorithm (UAX #14), and at its line feeds; a word wider than the width is broken between grapheme clusters.
    /// </summary>
    /// <param name="measurer">The measurer of the font that draws the text.</param>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="maxWidth">The available width. A value that is not positive and finite keeps every line of the text whole.</param>
    /// <returns>The lines, without their trailing spaces and line terminators. An empty text returns one empty line.</returns>
    public static IReadOnlyList<string> Wrap(ITextMeasurer measurer, string text, SceneFont font, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();
        var start = 0;
        var lastFit = -1;
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text))
        {
            var end = lineBreak.Index;
            if (Fits(start, end))
            {
                lastFit = end;
            }
            else
            {
                // The text up to the previous opportunity fits: the line ends there
                if (lastFit > start)
                {
                    lines.Add(TrimLine(text, start, lastFit));
                    start = lastFit;
                }

                // A segment wider than a line is broken between grapheme clusters; its last piece starts the next line
                while (!Fits(start, end))
                {
                    var cut = GetLongestFittingPrefix(start, end);
                    if (cut == end)
                        break;

                    lines.Add(TrimLine(text, start, cut));
                    start = cut;
                }

                lastFit = end;
            }

            if (lineBreak.IsMandatory)
            {
                lines.Add(TrimLine(text, start, end));
                start = end;
                lastFit = -1;
            }
        }

        if (start < text.Length || lines.Count is 0)
        {
            lines.Add(TrimLine(text, start, text.Length));
        }

        return lines;

        bool Fits(int lineStart, int lineEnd)
        {
            if (!(maxWidth > 0) || !double.IsFinite(maxWidth))
                return true;

            return measurer.Measure(TrimLine(text, lineStart, lineEnd), font).Width <= maxWidth;
        }

        // The end of the longest prefix of whole grapheme clusters that fits, and at least one cluster
        int GetLongestFittingPrefix(int lineStart, int lineEnd)
        {
            var result = -1;
            foreach (var clusterEnd in GetGraphemeClusterEnds(text.Substring(lineStart, lineEnd - lineStart)))
            {
                if (result >= 0 && !Fits(lineStart, lineStart + clusterEnd))
                    break;

                result = lineStart + clusterEnd;
            }

            return result < 0 ? lineEnd : result;
        }
    }

    private static string TrimLine(string text, int start, int end)
    {
        var span = text.AsSpan(start, end - start);
        return span.TrimEnd(" \t\r\n\u000B\u000C\u0085\u2028\u2029").ToString();
    }

    internal static List<int> GetGraphemeClusterEnds(string text)
    {
        var boundaries = new List<int>(text.Length);

        // In ASCII, each character is a grapheme cluster, except CR LF
        if (Ascii.IsValid(text))
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is '\r' && i + 1 < text.Length && text[i + 1] is '\n')
                {
                    i++;
                }

                boundaries.Add(i + 1);
            }

            return boundaries;
        }

        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            boundaries.Add(range.End.GetOffset(text.Length));
        }

        return boundaries;
    }
}
