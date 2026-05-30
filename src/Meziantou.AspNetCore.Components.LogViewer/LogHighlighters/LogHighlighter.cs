using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Components;

internal static class LogHighlighter
{
    public static MarkupString Highlight(string? text, IEnumerable<ILogHighlighter> highlighters, string? attributeName)
    {
        if (text is null)
            return new MarkupString();

        var parsedText = AnsiTextParser.Parse(text);
        var visibleText = parsedText.Text;
        var ansiRuns = parsedText.Runs;

        if (highlighters is not null)
        {
            var allMatches = highlighters
                .SelectMany(highlighter => highlighter.Process(visibleText))
                .OrderBy(result => result.Index)
                .ToArray();

            if (allMatches.Length > 0)
            {
                // highlights
                var lastIndex = 0;
                var sb = new StringBuilder();

                for (var i = 0; i < allMatches.Length; i++)
                {
                    var match = allMatches[i];
                    if (match.Index < lastIndex)
                        continue; // overlap

                    // Find the best match in case of overlaps (highest priority and lowest index)
                    var matchEnd = match.Index + match.Length;
                    for (var j = i + 1; j < allMatches.Length; j++)
                    {
                        var potentialMatch = allMatches[j];
                        if (potentialMatch.Index > matchEnd)
                            break; // allMatches is sorted by index

                        if (potentialMatch.Priority < match.Priority)
                            continue; // only consider higher priority match

                        if (potentialMatch.Index > match.Index)
                            continue; // only consider lowest index

                        match = allMatches[j];
                    }

                    // Highlights
                    AppendStyledText(sb, visibleText, ansiRuns, lastIndex, match.Index, attributeName);

                    lastIndex = match.Index + match.Length;

                    if (match.Link is not null)
                    {
                        sb.Append("<a ").Append(attributeName).Append(" class='log-message-match-link' target='_blank' href='");
                        sb.Append(HtmlEncoder.Default.Encode(match.Link));
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
                        AppendStyledText(sb, visibleText, ansiRuns, match.Index, lastIndex, attributeName);
                    }

                    if (match.Link is not null)
                    {
                        sb.Append("</a>");
                    }
                    else
                    {
                        sb.Append("</span>");
                    }
                }

                AppendStyledText(sb, visibleText, ansiRuns, lastIndex, visibleText.Length, attributeName);
                return new MarkupString(sb.ToString());
            }
        }

        var result = new StringBuilder();
        AppendStyledText(result, visibleText, ansiRuns, 0, visibleText.Length, attributeName);
        return new MarkupString(result.ToString());
    }

    private static void AppendStyledText(StringBuilder sb, string text, IReadOnlyList<AnsiTextParser.TextRun> ansiRuns, int start, int end, string? attributeName)
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

    private static void AppendStyledSegment(StringBuilder sb, string text, AnsiTextParser.AnsiStyle style, string? attributeName)
    {
        if (style == AnsiTextParser.AnsiStyle.None)
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

    private static string BuildInlineStyle(AnsiTextParser.AnsiStyle style)
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

    private static string ConvertToCssColor(AnsiTextParser.AnsiColor color)
    {
        if (color.Kind is AnsiTextParser.AnsiColorKind.Rgb)
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
