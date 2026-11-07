using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Components;

internal static class LogHighlighter
{
    public static MarkupString Highlight(string? text, IEnumerable<ILogHighlighter>? highlighters, string? attributeName)
    {
        if (text is null)
            return new MarkupString();

        var parsedText = Meziantou.Framework.AnsiTextProcessor.ParseTextWithAnsiStyles(text);
        var visibleText = parsedText.Text;
        var ansiRuns = parsedText.Runs;

        var sb = new StringBuilder();
        var lastIndex = 0;
        foreach (var match in SelectMatches(visibleText, highlighters))
        {
            AppendStyledText(sb, visibleText, ansiRuns, lastIndex, match.Index, attributeName);
            AppendMatch(sb, visibleText, ansiRuns, match, attributeName);
            lastIndex = match.Index + match.Length;
        }

        AppendStyledText(sb, visibleText, ansiRuns, lastIndex, visibleText.Length, attributeName);
        return new MarkupString(sb.ToString());
    }

    // Selects the non-overlapping set of highlights to render. A higher-priority match always wins
    // over every match it overlaps, even when the lower-priority one starts earlier. Ties are broken
    // on the lowest index, then on the longest match. Results that fall outside the text are dropped.
    private static List<LogHighlighterResult> SelectMatches(string text, IEnumerable<ILogHighlighter>? highlighters)
    {
        var selected = new List<LogHighlighterResult>();
        if (highlighters is null)
            return selected;

        var candidates = highlighters
            .SelectMany(highlighter => highlighter.Process(text))
            .Where(result => result.Length > 0 && result.Index >= 0 && result.Index <= text.Length - result.Length)
            .OrderByDescending(result => result.Priority)
            .ThenBy(result => result.Index)
            .ThenByDescending(result => result.Length);

        foreach (var candidate in candidates)
        {
            var insertionIndex = FindInsertionIndex(selected, candidate.Index);

            var previous = insertionIndex > 0 ? selected[insertionIndex - 1] : null;
            if (previous is not null && previous.Index + previous.Length > candidate.Index)
                continue;

            var next = insertionIndex < selected.Count ? selected[insertionIndex] : null;
            if (next is not null && candidate.Index + candidate.Length > next.Index)
                continue;

            selected.Insert(insertionIndex, candidate);
        }

        return selected;
    }

    // Returns the position of the first selected match whose index is greater than or equal to <paramref name="index"/>.
    private static int FindInsertionIndex(List<LogHighlighterResult> selected, int index)
    {
        var low = 0;
        var high = selected.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (selected[middle].Index < index)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static void AppendMatch(StringBuilder sb, string text, IReadOnlyList<Meziantou.Framework.AnsiTextProcessor.AnsiTextRun> ansiRuns, LogHighlighterResult match, string? attributeName)
    {
        // A highlighter can produce any string as a link, including "javascript:". Only http(s) links are
        // rendered as anchors; anything else falls back to a plain highlight so the text is still visible.
        var link = match.Link is not null && IsSafeLink(match.Link) ? match.Link : null;
        if (link is not null)
        {
            sb.Append("<a ").Append(attributeName).Append(" class='log-message-match-link' target='_blank' rel='noopener noreferrer' href='");
            sb.Append(HtmlEncoder.Default.Encode(link));
            sb.Append('\'');
        }
        else
        {
            sb.Append("<span ").Append(attributeName).Append(" class='log-message-match'");
        }

        if (match.Title is not null)
        {
            sb.Append(" title='")
              .Append(HtmlEncoder.Default.Encode(match.Title))
              .Append('\'');
        }

        sb.Append('>');

        if (match.ReplacementText is not null)
        {
            sb.Append(HtmlEncoder.Default.Encode(match.ReplacementText));
        }
        else
        {
            AppendStyledText(sb, text, ansiRuns, match.Index, match.Index + match.Length, attributeName);
        }

        sb.Append(link is not null ? "</a>" : "</span>");
    }

    private static bool IsSafeLink(string link)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static void AppendStyledText(StringBuilder sb, string text, IReadOnlyList<Meziantou.Framework.AnsiTextProcessor.AnsiTextRun> ansiRuns, int start, int end, string? attributeName)
    {
        if (start >= end)
            return;

        if (ansiRuns.Count is 0)
        {
            sb.Append(HtmlEncoder.Default.Encode(text[start..end]));
            return;
        }

        foreach (var run in ansiRuns)
        {
            if (run.End <= start)
                continue;

            if (run.Start >= end)
                break;

            var runStart = Math.Max(start, run.Start);
            var runEnd = Math.Min(end, run.End);
            if (runEnd <= runStart)
                continue;

            AppendStyledSegment(sb, text[runStart..runEnd], run.Style, attributeName);
        }
    }

    private static void AppendStyledSegment(StringBuilder sb, string text, Meziantou.Framework.AnsiTextProcessor.AnsiStyle style, string? attributeName)
    {
        if (style == Meziantou.Framework.AnsiTextProcessor.AnsiStyle.None)
        {
            sb.Append(HtmlEncoder.Default.Encode(text));
            return;
        }

        sb.Append("<span ");
        if (attributeName is not null)
        {
            sb.Append(attributeName).Append(' ');
        }

        sb.Append("class='log-ansi");
        if (style.Bold)
        {
            sb.Append(" log-ansi-bold");
        }

        if (style.Italic)
        {
            sb.Append(" log-ansi-italic");
        }

        if (style.Underline)
        {
            sb.Append(" log-ansi-underline");
        }

        sb.Append('\'');

        var css = BuildInlineStyle(style);
        if (css.Length > 0)
        {
            sb.Append(" style='").Append(css).Append('\'');
        }

        sb.Append('>');
        sb.Append(HtmlEncoder.Default.Encode(text));
        sb.Append("</span>");
    }

    private static string BuildInlineStyle(Meziantou.Framework.AnsiTextProcessor.AnsiStyle style)
    {
        var sb = new StringBuilder();
        var foregroundColor = style.Foreground;
        var backgroundColor = style.Background;
        if (style.Inverse)
        {
            var swappedForeground = backgroundColor;
            backgroundColor = foregroundColor;
            foregroundColor = swappedForeground;
        }

        if (foregroundColor is not null)
        {
            sb.Append("color: ").Append(ConvertToCssColor(foregroundColor)).Append(';');
        }
        else if (style.Inverse)
        {
            sb.Append("color: var(--color-background);");
        }

        if (backgroundColor is not null)
        {
            sb.Append("background-color: ").Append(ConvertToCssColor(backgroundColor)).Append(';');
        }
        else if (style.Inverse)
        {
            sb.Append("background-color: currentColor;");
        }

        return sb.ToString();
    }

    private static string ConvertToCssColor(Meziantou.Framework.AnsiTextProcessor.AnsiColor color)
    {
        if (color.Kind is Meziantou.Framework.AnsiTextProcessor.AnsiColorKind.Rgb)
            return $"rgb({color.Red}, {color.Green}, {color.Blue})";

        return color.IndexedValue switch
        {
            0 => "rgb(0, 0, 0)",
            1 => "rgb(205, 49, 49)",
            2 => "rgb(13, 188, 121)",
            3 => "rgb(229, 229, 16)",
            4 => "rgb(36, 114, 200)",
            5 => "rgb(188, 63, 188)",
            6 => "rgb(17, 168, 205)",
            7 => "rgb(229, 229, 229)",
            8 => "rgb(102, 102, 102)",
            9 => "rgb(241, 76, 76)",
            10 => "rgb(35, 209, 139)",
            11 => "rgb(245, 245, 67)",
            12 => "rgb(59, 142, 234)",
            13 => "rgb(214, 112, 214)",
            14 => "rgb(41, 184, 219)",
            15 => "rgb(255, 255, 255)",
            <= 231 => BuildAnsi256Color(color.IndexedValue),
            _ => BuildGrayscaleColor(color.IndexedValue),
        };
    }

    private static string BuildAnsi256Color(byte index)
    {
        var value = index - 16;
        var red = value / 36;
        var green = (value % 36) / 6;
        var blue = value % 6;

        return $"rgb({ConvertCubeValue(red)}, {ConvertCubeValue(green)}, {ConvertCubeValue(blue)})";
    }

    private static string BuildGrayscaleColor(byte index)
    {
        var value = 8 + ((index - 232) * 10);
        return $"rgb({value}, {value}, {value})";
    }

    private static int ConvertCubeValue(int value)
    {
        return value is 0 ? 0 : 55 + (value * 40);
    }
}
