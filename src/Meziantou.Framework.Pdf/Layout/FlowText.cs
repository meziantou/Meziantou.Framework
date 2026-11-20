using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Breaks the runs of a paragraph into lines (Unicode line breaking, UAX #14): a line ends at the last break opportunity
/// that fits, at a mandatory break, or inside a word longer than the line, between its grapheme clusters.
/// </summary>
internal static class FlowText
{
    public static List<FlowLine> Break(FlowRun[] runs, Func<FlowRun, (PdfFont Font, double Size, PdfColor Color)> resolve, double width, double lineHeight)
    {
        var text = new StringBuilder();
        var starts = new int[runs.Length + 1];
        var styles = new (PdfFont Font, double Size, PdfColor Color)[runs.Length];
        for (var i = 0; i < runs.Length; i++)
        {
            starts[i] = text.Length;
            styles[i] = resolve(runs[i]);
            text.Append(runs[i].Text.Replace('\t', ' ').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        }

        starts[runs.Length] = text.Length;
        var full = text.ToString();
        var context = new LineContext(runs, starts, styles, full, width, lineHeight);
        if (full.Length == 0)
        {
            context.AddLine(0, 0, endsParagraph: true);
            return context.Lines;
        }

        var start = 0;
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(full, UnicodeLineBreakStrictness.Normal))
        {
            context.AddSegment(start, lineBreak.Index);
            if (lineBreak.IsMandatory)
            {
                context.EndLine(endsParagraph: true);
            }

            start = lineBreak.Index;
        }

        // The end of the text is a mandatory break (UAX #14, LB3): the last line is already ended, unless the enumeration stopped early
        if (start < full.Length)
        {
            context.AddSegment(start, full.Length);
            context.EndLine(endsParagraph: true);
        }

        if (context.Lines.Count == 0)
        {
            context.AddLine(0, 0, endsParagraph: true);
        }

        return context.Lines;
    }

    private sealed class LineContext(FlowRun[] runs, int[] starts, (PdfFont Font, double Size, PdfColor Color)[] styles, string text, double width, double lineHeight)
    {
        private int _lineStart = -1;
        private int _lineEnd;
        private double _lineWidth;

        public List<FlowLine> Lines { get; } = [];

        /// <summary>Adds the text between two break opportunities to the line, or to a new line when it does not fit.</summary>
        public void AddSegment(int start, int end)
        {
            var contentEnd = TrimEnd(start, end);
            var contentWidth = Measure(start, contentEnd);
            if (_lineStart >= 0 && _lineWidth + Measure(_lineEnd, start) + contentWidth > width)
            {
                EndLine(endsParagraph: false);
            }

            if (_lineStart < 0 && contentWidth > width)
            {
                // A word longer than the line: it is broken between its grapheme clusters
                AddLongWord(start, contentEnd);
                _lineEnd = contentEnd;
                return;
            }

            if (_lineStart < 0)
            {
                _lineStart = start;
                _lineWidth = 0;
            }
            else
            {
                _lineWidth += Measure(_lineEnd, start);
            }

            // The spaces after the word are counted only when a word follows on the line
            _lineWidth += contentWidth;
            _lineEnd = contentEnd;
        }

        public void EndLine(bool endsParagraph)
        {
            if (_lineStart < 0)
            {
                if (endsParagraph)
                {
                    // An empty line: a mandatory break after a mandatory break
                    AddLine(_lineEnd, _lineEnd, endsParagraph);
                }

                return;
            }

            AddLine(_lineStart, _lineEnd, endsParagraph);
            _lineStart = -1;
            _lineWidth = 0;
        }

        public void AddLine(int start, int end, bool endsParagraph)
        {
            var fragments = new List<FlowFragment>();
            var lineWidth = 0d;
            var ascent = 0d;
            var descent = 0d;
            var maxSize = 0d;
            for (var run = 0; run < runs.Length; run++)
            {
                var (font, size, color) = styles[run];
                var a = Math.Max(start, starts[run]);
                var b = Math.Min(end, starts[run + 1]);
                if (b > a)
                {
                    var fragmentText = text[a..b].Replace("\n", "", StringComparison.Ordinal);
                    if (fragmentText.Length > 0)
                    {
                        var fragmentWidth = font.GetTextWidth(fragmentText, size);
                        fragments.Add(new FlowFragment(fragmentText, font, size, color, runs[run].Link, run, fragmentWidth));
                        lineWidth += fragmentWidth;
                    }
                }

                // The metrics of the line: the runs it contains, or the run of an empty line
                if (b > a || (fragments.Count == 0 && starts[run] <= start && start <= starts[run + 1]))
                {
                    ascent = Math.Max(ascent, font.Ascent * size / 1000);
                    descent = Math.Max(descent, -font.Descent * size / 1000);
                    maxSize = Math.Max(maxSize, size);
                }
            }

            if (maxSize == 0)
            {
                var (font, size, _) = styles.Length > 0 ? styles[^1] : (PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, PdfColor.Black);
                ascent = font.Ascent * size / 1000;
                descent = -font.Descent * size / 1000;
                maxSize = size;
            }

            var height = Math.Max(maxSize * lineHeight, ascent + descent);
            Lines.Add(new FlowLine(fragments, lineWidth, height, ascent + ((height - ascent - descent) / 2), endsParagraph));
        }

        private void AddLongWord(int start, int end)
        {
            var pieceStart = start;
            var pieceWidth = 0d;
            var enumerator = StringInfo.GetTextElementEnumerator(text[start..end]);
            while (enumerator.MoveNext())
            {
                var elementStart = start + enumerator.ElementIndex;
                var elementEnd = elementStart + ((string)enumerator.Current).Length;
                var elementWidth = Measure(elementStart, elementEnd);
                if (pieceWidth > 0 && pieceWidth + elementWidth > width)
                {
                    AddLine(pieceStart, elementStart, endsParagraph: false);
                    pieceStart = elementStart;
                    pieceWidth = 0;
                }

                pieceWidth += elementWidth;
            }

            _lineStart = pieceStart;
            _lineWidth = pieceWidth;
        }

        /// <summary>Gets the end of the text without its trailing spaces and line feeds.</summary>
        private int TrimEnd(int start, int end)
        {
            while (end > start && text[end - 1] is ' ' or '\n' or '\u3000')
            {
                end--;
            }

            return end;
        }

        private double Measure(int start, int end)
        {
            var total = 0d;
            for (var run = 0; run < runs.Length && end > start; run++)
            {
                var a = Math.Max(start, starts[run]);
                var b = Math.Min(end, starts[run + 1]);
                if (b > a)
                {
                    var segment = text[a..b].Replace("\n", "", StringComparison.Ordinal);
                    if (segment.Length > 0)
                    {
                        total += styles[run].Font.GetTextWidth(segment, styles[run].Size);
                    }
                }
            }

            return total;
        }
    }
}
