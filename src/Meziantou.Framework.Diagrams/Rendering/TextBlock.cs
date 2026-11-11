using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A label laid out in lines of runs, measured with the text measurer of the diagram.</summary>
internal sealed class TextBlock
{
    private const double LineSpacing = 1.2;

    private TextBlock(List<List<TextRun>> lines, double fontSize, ITextMeasurer measurer)
    {
        Lines = lines;
        FontSize = fontSize;
        Measurer = measurer;
        var metrics = measurer.Measure("x", new SceneFont(fontSize));
        Ascent = metrics.Ascent;
        Descent = metrics.Descent;
        LineHeight = Math.Max(metrics.Height, fontSize * LineSpacing);
        LineWidths = [.. lines.Select(MeasureLine)];
        Width = LineWidths.Count is 0 ? 0 : LineWidths.Max();
        Height = lines.Count * LineHeight;
    }

    public List<List<TextRun>> Lines { get; }

    public List<double> LineWidths { get; }

    public double FontSize { get; }

    public ITextMeasurer Measurer { get; }

    public double Ascent { get; }

    public double Descent { get; }

    public double LineHeight { get; }

    public double Width { get; }

    public double Height { get; }

    public bool IsEmpty => Lines.All(line => line.All(run => run.Text.Length is 0));

    /// <summary>Gets the text of the block, the lines separated by line feeds.</summary>
    public string PlainText => string.Join('\n', Lines.Select(line => string.Concat(line.Select(run => run.Text))));

    public static TextBlock Empty(ITextMeasurer measurer, double fontSize) => new([], fontSize, measurer);

    /// <summary>Lays a text out.</summary>
    /// <param name="text">The text.</param>
    /// <param name="measurer">The measurer.</param>
    /// <param name="fontSize">The size of the font.</param>
    /// <param name="bold">Whether the text is bold, before the markdown emphasis.</param>
    /// <param name="italic">Whether the text is italic, before the markdown emphasis.</param>
    /// <param name="maxWidth">The width at which the text wraps, or <see langword="null"/> to wrap only the markdown at the line feeds.</param>
    public static TextBlock Create(DiagramText? text, ITextMeasurer measurer, double fontSize, bool bold = false, bool italic = false, double? maxWidth = null)
    {
        if (text is null || text.IsEmpty)
            return Empty(measurer, fontSize);

        var runs = text.Format is DiagramTextFormat.Markdown ? ParseMarkdown(text.Text, bold, italic) : [new TextRun(text.Text, bold, italic)];
        var lines = Wrap(runs, measurer, fontSize, maxWidth is > 0 && double.IsFinite(maxWidth.Value) ? maxWidth.Value : double.PositiveInfinity);
        return new TextBlock(lines, fontSize, measurer);
    }

    /// <summary>Draws the block centered on a point.</summary>
    public void Draw(SceneGroup group, double centerX, double centerY, SceneColor color, SceneTextAnchor anchor = SceneTextAnchor.Middle)
    {
        var top = centerY - (Height / 2);
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            var baseline = top + (i * LineHeight) + ((LineHeight - Ascent - Descent) / 2) + Ascent;
            var lineWidth = LineWidths[i];
            if (line.Count == 1)
            {
                if (line[0].Text.Length > 0)
                {
                    var x = anchor switch
                    {
                        SceneTextAnchor.Start => centerX - (Width / 2),
                        SceneTextAnchor.End => centerX + (Width / 2),
                        _ => centerX,
                    };
                    group.Add(new SceneText(x, baseline, line[0].Text, Font(line[0]), color) { Anchor = anchor, MeasuredWidth = lineWidth });
                }

                continue;
            }

            var start = anchor switch
            {
                SceneTextAnchor.Start => centerX - (Width / 2),
                SceneTextAnchor.End => centerX + (Width / 2) - lineWidth,
                _ => centerX - (lineWidth / 2),
            };
            foreach (var run in line)
            {
                if (run.Text.Length is 0)
                    continue;

                var width = Measurer.Measure(run.Text, Font(run)).Width;
                group.Add(new SceneText(start, baseline, run.Text, Font(run), color) { MeasuredWidth = width });
                start += width;
            }
        }
    }

    private SceneFont Font(TextRun run) => new(FontSize, run.Bold ? SceneFont.BoldWeight : SceneFont.NormalWeight, run.Italic);

    private double MeasureLine(List<TextRun> line)
    {
        var width = 0.0;
        foreach (var run in line)
        {
            if (run.Text.Length > 0)
            {
                width += Measurer.Measure(run.Text, Font(run)).Width;
            }
        }

        return width;
    }

    /// <summary>Splits markdown into runs: <c>**</c> and <c>__</c> toggle bold, <c>*</c> and <c>_</c> toggle italic, and a backslash escapes the next character.</summary>
    internal static List<TextRun> ParseMarkdown(string text, bool bold, bool italic)
    {
        var runs = new List<TextRun>();
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\\' && i + 1 < text.Length && text[i + 1] is '*' or '_' or '\\')
            {
                builder.Append(text[i + 1]);
                i++;
                continue;
            }

            if (c is '*' or '_')
            {
                // An underscore inside a word is a character, as in snake_case
                if (c is '_' && i > 0 && i + 1 < text.Length && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]))
                {
                    builder.Append(c);
                    continue;
                }

                Flush();
                if (i + 1 < text.Length && text[i + 1] == c)
                {
                    bold = !bold;
                    i++;
                }
                else
                {
                    italic = !italic;
                }

                continue;
            }

            builder.Append(c);
        }

        Flush();
        return runs;

        void Flush()
        {
            if (builder.Length > 0)
            {
                runs.Add(new TextRun(builder.ToString(), bold, italic));
                builder.Clear();
            }
        }
    }

    /// <summary>Breaks runs into lines at the line feeds, and at the line break opportunities of Unicode where a line is wider than the width.</summary>
    private static List<List<TextRun>> Wrap(List<TextRun> runs, ITextMeasurer measurer, double fontSize, double maxWidth)
    {
        var text = string.Concat(runs.Select(run => run.Text));
        var runStarts = new int[runs.Count];
        for (int i = 0, offset = 0; i < runs.Count; i++)
        {
            runStarts[i] = offset;
            offset += runs[i].Text.Length;
        }

        var lines = new List<List<TextRun>>();
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
                if (lastFit > start)
                {
                    lines.Add(Slice(start, lastFit));
                    start = lastFit;
                }

                while (!Fits(start, end))
                {
                    var cut = LongestFittingPrefix(start, end);
                    if (cut == end)
                        break;

                    lines.Add(Slice(start, cut));
                    start = cut;
                }

                lastFit = end;
            }

            if (lineBreak.IsMandatory)
            {
                lines.Add(Slice(start, end));
                start = end;
                lastFit = -1;
            }
        }

        if (start < text.Length || lines.Count is 0)
        {
            lines.Add(Slice(start, text.Length));
        }

        return lines;

        bool Fits(int lineStart, int lineEnd)
        {
            if (double.IsPositiveInfinity(maxWidth))
                return true;

            var width = 0.0;
            foreach (var run in Slice(lineStart, lineEnd))
            {
                width += measurer.Measure(run.Text, new SceneFont(fontSize, run.Bold ? SceneFont.BoldWeight : SceneFont.NormalWeight, run.Italic)).Width;
            }

            return width <= maxWidth;
        }

        int LongestFittingPrefix(int lineStart, int lineEnd)
        {
            var result = -1;
            var segment = text[lineStart..lineEnd];
            foreach (var range in Unicode.EnumerateGraphemeClusters(segment))
            {
                var clusterEnd = lineStart + range.End.GetOffset(segment.Length);
                if (result >= 0 && !Fits(lineStart, clusterEnd))
                    break;

                result = clusterEnd;
            }

            return result < 0 ? lineEnd : result;
        }

        List<TextRun> Slice(int sliceStart, int sliceEnd)
        {
            // The spaces and line terminators at the end of a line are not drawn
            while (sliceEnd > sliceStart && IsTrimmed(text[sliceEnd - 1]))
            {
                sliceEnd--;
            }

            var result = new List<TextRun>();
            for (var i = 0; i < runs.Count; i++)
            {
                var runStart = runStarts[i];
                var runEnd = runStart + runs[i].Text.Length;
                var from = Math.Max(runStart, sliceStart);
                var to = Math.Min(runEnd, sliceEnd);
                if (from < to)
                {
                    result.Add(runs[i] with { Text = text[from..to] });
                }
            }

            if (result.Count is 0)
            {
                result.Add(new TextRun(string.Empty, Bold: false, Italic: false));
            }

            return result;
        }
    }

    private static bool IsTrimmed(char c) => c is ' ' or '\t' or '\r' or '\n' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029';
}
