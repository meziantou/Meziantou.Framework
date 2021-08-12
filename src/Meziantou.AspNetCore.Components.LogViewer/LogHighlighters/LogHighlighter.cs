using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Components;

internal static class LogHighlighter
{
    public static MarkupString Highlight(string? text, IEnumerable<ILogHighlighter> highlighters, string? attributeName)
    {
        if (text == null)
            return new MarkupString();

        if (highlighters != null)
        {
            var allMatches = highlighters
                .SelectMany(highlighter => highlighter.Process(text))
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
                    sb.Append(HtmlEncoder.Default.Encode(text[lastIndex..match.Index]));

                    lastIndex = match.Index + match.Length;
                    var matchedText = text[match.Index..lastIndex];

                    if (match.Link != null)
                    {
                        sb.Append("<a ").Append(attributeName).Append(" class='log-message-match-link' target='_blank' href='");
                        sb.Append(HtmlEncoder.Default.Encode(match.Link));
                        sb.Append('\'');
                    }
                    else
                    {
                        sb.Append("<span ").Append(attributeName).Append(" class='log-message-match'");
                    }

                    if (match.Title != null)
                    {
                        sb.Append(" title='")
                          .Append(HtmlEncoder.Default.Encode(match.Title))
                          .Append('\'');
                    }

                    sb.Append('>');

                    sb.Append(HtmlEncoder.Default.Encode(match.ReplacementText ?? matchedText));

                    if (match.Link != null)
                    {
                        sb.Append("</a>");
                    }
                    else
                    {
                        sb.Append("</span>");
                    }
                }

                sb.Append(HtmlEncoder.Default.Encode(text[lastIndex..]));
                return new MarkupString(sb.ToString());
            }
        }

        return new MarkupString(HtmlEncoder.Default.Encode(text));
    }
}
