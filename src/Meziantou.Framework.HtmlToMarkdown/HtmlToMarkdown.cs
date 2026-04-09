using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Meziantou.Framework;

/// <summary>Converts HTML fragments to Markdown text.</summary>
public static class HtmlToMarkdown
{
    /// <summary>Converts an HTML fragment to Markdown using default options.</summary>
    /// <param name="html">The HTML fragment to convert.</param>
    /// <returns>The converted Markdown text.</returns>
    public static string Convert(string html)
    {
        return Convert(html, new HtmlToMarkdownOptions());
    }

    /// <summary>Converts an HTML fragment to Markdown using the specified options.</summary>
    /// <param name="html">The HTML fragment to convert.</param>
    /// <param name="options">The conversion options.</param>
    /// <returns>The converted Markdown text.</returns>
    public static string Convert(string html, HtmlToMarkdownOptions options)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var parser = new HtmlParser();
        var document = parser.ParseDocument("<body>" + html + "</body>");
        var state = new ConversionState(options);
        var result = ConvertChildNodes(document.Body!, state);
        return result.Trim('\n', '\r');
    }

    // =========================================================================
    // Core traversal
    // =========================================================================

    private static string ConvertNode(INode node, ConversionState state)
    {
        return node switch
        {
            IText text => ConvertText(text, state),
            IElement element => ConvertElement(element, state),
            _ => "",
        };
    }

    private static string ConvertText(IText text, ConversionState state)
    {
        var content = CollapseWhitespace(text.Data);
        if (state.Options.UseSimplePunctuation)
            content = ApplySimplePunctuation(content);

        content = EscapeMarkdown(content);
        if (state.Options.EmojiShortcodeMode is not EmojiShortcodeMode.None)
        {
            content = ReplaceEmojiWithShortcodes(content, state.Options.EmojiShortcodeMode);
        }

        return content;
    }

    private static string ConvertElement(IElement element, ConversionState state)
    {
        return element.LocalName switch
        {
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => ConvertHeading(element, state),
            "p" => ConvertParagraph(element, state),
            "blockquote" => ConvertBlockquote(element, state),
            "pre" => ConvertPre(element, state),
            "ul" => ConvertUnorderedList(element, state),
            "ol" => ConvertOrderedList(element, state),
            "hr" => state.Options.ThematicBreak,
            "table" => ConvertTable(element, state),
            "dl" => ConvertDefinitionList(element, state),

            "strong" or "b" => ConvertStrong(element, state),
            "em" or "i" => ConvertEmphasis(element, state),
            "del" or "s" or "strike" => ConvertStrikethrough(element, state),
            "code" => ConvertInlineCode(element),
            "a" => ConvertLink(element, state),
            "img" => ConvertImage(element),
            "br" => ConvertBreak(state),
            "input" => ConvertInput(element),

            "script" or "style" or "noscript" => "",

            "div" or "article" or "section" or "nav" or "aside"
                or "header" or "footer" or "main" or "details" or "summary"
                or "figure" or "figcaption" => ConvertChildNodes(element, state),

            "span" => ConvertInlineContent(element, state),

            "thead" or "tbody" or "tfoot" or "tr" or "th" or "td"
                or "li" or "dt" or "dd" => ConvertChildNodes(element, state),

            _ => ConvertUnknownElement(element, state),
        };
    }

    /// <summary>
    /// Processes children of a container node, grouping inline nodes and
    /// separating block elements with blank lines.
    /// </summary>
    private static string ConvertChildNodes(INode parent, ConversionState state)
    {
        var blocks = new List<string>();
        var inlineBuf = new StringBuilder();

        foreach (var child in parent.ChildNodes)
        {
            if (IsStrippedElement(child))
                continue;

            if (IsBlockElement(child))
            {
                FlushInlineBuffer(inlineBuf, blocks);
                var block = ConvertNode(child, state);
                if (!string.IsNullOrEmpty(block))
                    blocks.Add(block);
            }
            else
            {
                inlineBuf.Append(ConvertNode(child, state));
            }
        }

        FlushInlineBuffer(inlineBuf, blocks);
        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Concatenates all children as inline content (no block grouping).
    /// Used inside inline elements like strong, em, a, etc.
    /// </summary>
    private static string ConvertInlineContent(INode parent, ConversionState state)
    {
        var sb = new StringBuilder();
        foreach (var child in parent.ChildNodes)
        {
            sb.Append(ConvertNode(child, state));
        }
        return sb.ToString();
    }

    // =========================================================================
    // Block converters
    // =========================================================================

    private static string ConvertHeading(IElement element, ConversionState state)
    {
        var content = ConvertInlineContent(element, state).Trim();
        if (content.Length == 0)
            return "";

        var level = element.LocalName[1] - '0'; // h1→1, h6→6

        if (state.Options.HeadingStyle == HeadingStyle.Setext && level <= 2)
        {
            var underline = new string(level == 1 ? '=' : '-', content.Length);
            return content + "\n" + underline;
        }

        return new string('#', level) + " " + content;
    }

    private static string ConvertParagraph(IElement element, ConversionState state)
    {
        var content = ConvertInlineContent(element, state).Trim();
        return PostProcessLineStart(content);
    }

    private static string ConvertBlockquote(IElement element, ConversionState state)
    {
        var content = ConvertChildNodes(element, state);
        if (string.IsNullOrEmpty(content))
            return "";
        return PrefixLines(content, "> ", "> ", ">");
    }

    private static string ConvertPre(IElement element, ConversionState state)
    {
        var codeElement = element.QuerySelector("code");
        var contentElement = codeElement ?? element;
        var content = contentElement.TextContent;
        var language = ExtractLanguage(codeElement) ?? ExtractLanguage(element);

        if (state.Options.CodeBlockStyle == CodeBlockStyle.Indented)
        {
            return PrefixLines(content.TrimEnd('\n'), "    ", "    ", "");
        }

        return FencedCodeBlock(content, language, state.Options.CodeBlockFenceCharacter);
    }

    private static string ConvertUnorderedList(IElement element, ConversionState state)
    {
        var marker = state.Options.UnorderedListMarker + " ";
        var indent = new string(' ', marker.Length);
        var isLoose = IsLooseList(element);
        var items = new List<string>();

        foreach (var li in element.Children)
        {
            if (li.LocalName != "li")
                continue;
            var content = ConvertListItemContent(li, state);
            items.Add(PrefixLines(content, marker, indent, ""));
        }

        return string.Join(isLoose ? "\n\n" : "\n", items);
    }

    private static string ConvertOrderedList(IElement element, ConversionState state)
    {
        var start = int.TryParse(element.GetAttribute("start"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1;
        var isLoose = IsLooseList(element);
        var items = new List<string>();
        var index = start;

        foreach (var li in element.Children)
        {
            if (li.LocalName != "li")
                continue;
            var marker = index + ". ";
            var indent = new string(' ', marker.Length);
            var content = ConvertListItemContent(li, state);
            items.Add(PrefixLines(content, marker, indent, ""));
            index++;
        }

        return string.Join(isLoose ? "\n\n" : "\n", items);
    }

    /// <summary>
    /// Converts the content of a list item, using single newline before
    /// sublists (tight) and double newline before other block elements.
    /// </summary>
    private static string ConvertListItemContent(IElement li, ConversionState state)
    {
        var parts = new List<(string Content, bool IsList)>();
        var inlineBuf = new StringBuilder();

        foreach (var child in li.ChildNodes)
        {
            if (IsStrippedElement(child))
                continue;

            if (IsBlockElement(child))
            {
                var inline = CollapseInlineSpaces(inlineBuf.ToString()).Trim();
                inlineBuf.Clear();
                if (inline.Length > 0)
                    parts.Add((PostProcessLineStart(inline), false));

                var block = ConvertNode(child, state);
                if (!string.IsNullOrEmpty(block))
                {
                    var isList = child is IElement el && el.LocalName is "ul" or "ol";
                    parts.Add((block, isList));
                }
            }
            else
            {
                inlineBuf.Append(ConvertNode(child, state));
            }
        }

        var remaining = CollapseInlineSpaces(inlineBuf.ToString()).Trim();
        if (remaining.Length > 0)
            parts.Add((PostProcessLineStart(remaining), false));

        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                // Sublists after inline text use \n; everything else uses \n\n
                sb.Append(parts[i].IsList && !parts[i - 1].IsList ? "\n" : "\n\n");
            }
            sb.Append(parts[i].Content);
        }

        return sb.ToString();
    }

    private static string ConvertTable(IElement table, ConversionState state)
    {
        var headerRows = new List<List<(string Content, string? Align)>>();
        var bodyRows = new List<List<(string Content, string? Align)>>();
        var footerRows = new List<List<(string Content, string? Align)>>();

        foreach (var child in table.Children)
        {
            switch (child.LocalName)
            {
                case "thead":
                    foreach (var tr in child.Children.Where(c => c.LocalName == "tr"))
                        headerRows.Add(ExtractTableRow(tr, state));
                    break;

                case "tbody":
                    foreach (var tr in child.Children.Where(c => c.LocalName == "tr"))
                        bodyRows.Add(ExtractTableRow(tr, state));
                    break;

                case "tr":
                    bodyRows.Add(ExtractTableRow(child, state));
                    break;

                case "tfoot":
                    foreach (var tr in child.Children.Where(c => c.LocalName == "tr"))
                        footerRows.Add(ExtractTableRow(tr, state));
                    break;
            }
        }

        var rows = new List<List<(string Content, string? Align)>>(headerRows.Count + bodyRows.Count + footerRows.Count);
        rows.AddRange(headerRows);
        rows.AddRange(bodyRows);
        rows.AddRange(footerRows);

        if (rows.Count == 0)
            return "";

        var colCount = rows.Max(r => r.Count);
        var sb = new StringBuilder();

        // Header row (first row)
        var headerRow = rows[0];
        AppendTableRow(sb, headerRow, colCount);
        sb.Append('\n');

        // Separator row with alignment
        AppendSeparatorRow(sb, headerRow, colCount);

        // Body rows
        for (var i = 1; i < rows.Count; i++)
        {
            sb.Append('\n');
            AppendTableRow(sb, rows[i], colCount);
        }

        return sb.ToString();
    }

    private static string ConvertDefinitionList(IElement element, ConversionState state)
    {
        var sb = new StringBuilder();
        var isFirst = true;
        var lastWasDt = false;

        foreach (var child in element.Children)
        {
            if (child.LocalName == "dt")
            {
                if (!isFirst)
                    sb.Append("\n\n");

                sb.Append(ConvertInlineContent(child, state).Trim());
                lastWasDt = true;
                isFirst = false;
            }
            else if (child.LocalName == "dd")
            {
                if (!lastWasDt && !isFirst)
                    sb.Append('\n');
                else if (lastWasDt)
                    sb.Append('\n');

                sb.Append(":   ");
                sb.Append(ConvertInlineContent(child, state).Trim());
                lastWasDt = false;
                isFirst = false;
            }
        }

        return sb.ToString();
    }

    // =========================================================================
    // Inline converters
    // =========================================================================

    private static string ConvertStrong(IElement element, ConversionState state)
    {
        // Collapse redundant nesting: <strong><strong>x</strong></strong>
        var onlyChild = GetSingleSignificantChild(element);
        if (onlyChild is IElement childEl && childEl.LocalName is "strong" or "b")
            return ConvertStrong(childEl, state);

        var content = ConvertInlineContent(element, state);
        var marker = state.Options.EmphasisMarker == EmphasisMarker.Asterisk ? "**" : "__";
        return WrapInlineMarker(content, marker);
    }

    private static string ConvertEmphasis(IElement element, ConversionState state)
    {
        var onlyChild = GetSingleSignificantChild(element);
        if (onlyChild is IElement childEl && childEl.LocalName is "em" or "i")
            return ConvertEmphasis(childEl, state);

        var content = ConvertInlineContent(element, state);
        var marker = state.Options.EmphasisMarker == EmphasisMarker.Asterisk ? "*" : "_";
        return WrapInlineMarker(content, marker);
    }

    private static string ConvertStrikethrough(IElement element, ConversionState state)
    {
        var content = ConvertInlineContent(element, state);
        return WrapInlineMarker(content, "~~");
    }

    private static string ConvertInlineCode(IElement element)
    {
        var content = element.TextContent;
        if (string.IsNullOrEmpty(content))
            return "";

        var needSpace = content.StartsWith('`') || content.EndsWith('`');
        var openCount = CountMaxConsecutiveChars(content, '`') + 1;

        var sb = new StringBuilder();
        sb.Append('`', openCount);
        if (needSpace) sb.Append(' ');
        sb.Append(content);
        if (needSpace) sb.Append(' ');
        sb.Append('`', openCount);
        return sb.ToString();
    }

    private static string ConvertLink(IElement element, ConversionState state)
    {
        var href = element.GetAttribute("href");
        if (href is null && !element.HasAttribute("href"))
            return ConvertInlineContent(element, state);

        var title = element.GetAttribute("title");
        var content = ConvertInlineContent(element, state);

        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(content);
        sb.Append("](");
        sb.Append(href ?? "");
        if (!string.IsNullOrEmpty(title))
        {
            AppendLinkTitle(sb, title);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string ConvertImage(IElement element)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src))
            return "";

        var alt = element.GetAttribute("alt") ?? "";
        var title = element.GetAttribute("title");

        var sb = new StringBuilder();
        sb.Append("![");
        sb.Append(alt);
        sb.Append("](");
        sb.Append(src);
        if (!string.IsNullOrEmpty(title))
        {
            AppendLinkTitle(sb, title);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string ConvertBreak(ConversionState state)
    {
        if (state.InTableCell)
            return "<br>";
        return state.Options.LineBreakStyle == LineBreakStyle.Backslash ? "\\\n" : "  \n";
    }

    private static string ConvertInput(IElement element)
    {
        if (string.Equals(element.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
            return element.HasAttribute("checked") ? "[x]" : "[ ]";
        return "";
    }

    private static string ConvertUnknownElement(IElement element, ConversionState state)
    {
        return state.Options.UnknownElementHandling switch
        {
            UnknownElementHandling.Strip => "",
            UnknownElementHandling.StripKeepContent => ConvertChildNodes(element, state),
            _ => element.OuterHtml,
        };
    }

    // =========================================================================
    // Table helpers
    // =========================================================================

    private static List<(string Content, string? Align)> ExtractTableRow(IElement tr, ConversionState state)
    {
        var cells = new List<(string Content, string? Align)>();
        var prevInTableCell = state.InTableCell;
        state.InTableCell = true;

        foreach (var cell in tr.Children)
        {
            if (cell.LocalName is "th" or "td")
            {
                var content = ConvertInlineContent(cell, state).Trim();
                var align = GetCellAlignment(cell);
                cells.Add((content, align));
            }
        }

        state.InTableCell = prevInTableCell;
        return cells;
    }

    /// <summary>
    /// Gets the alignment of a table cell by checking, in order:
    /// 1. The CSS text-align property from the style attribute
    /// 2. The HTML align attribute
    /// </summary>
    private static string? GetCellAlignment(IElement cell)
    {
        var style = cell.GetAttribute("style");
        if (style is not null)
        {
            var cssAlign = ExtractCssTextAlign(style);
            if (cssAlign is not null)
                return cssAlign;
        }

        return cell.GetAttribute("align")?.ToLowerInvariant() switch
        {
            "left" or "center" or "right" => cell.GetAttribute("align")!.ToLowerInvariant(),
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the text-align value from an inline CSS style string.
    /// </summary>
    private static string? ExtractCssTextAlign(string style)
    {
        // Parse "text-align: center" (or "text-align:center") from the style string
        foreach (var declaration in style.Split(';'))
        {
            var trimmed = declaration.Trim();
            var colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex < 0)
                continue;

            var property = trimmed[..colonIndex].Trim();
            if (property.Equals("text-align", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[(colonIndex + 1)..].Trim().TrimEnd('!').Trim();
                // Normalize !important
                if (value.EndsWith("important", StringComparison.OrdinalIgnoreCase))
                {
                    var bangIndex = value.LastIndexOf('!');
                    if (bangIndex >= 0)
                        value = value[..bangIndex].Trim();
                }

                return value.ToLowerInvariant() switch
                {
                    "left" => "left",
                    "center" => "center",
                    "right" => "right",
                    _ => null,
                };
            }
        }

        return null;
    }

    private static void AppendTableRow(StringBuilder sb, List<(string Content, string? Align)> row, int colCount)
    {
        sb.Append('|');
        for (var i = 0; i < colCount; i++)
        {
            sb.Append(' ');
            sb.Append(i < row.Count ? row[i].Content : "");
            sb.Append(" |");
        }
    }

    private static void AppendSeparatorRow(StringBuilder sb, List<(string Content, string? Align)> headerRow, int colCount)
    {
        sb.Append('|');
        for (var i = 0; i < colCount; i++)
        {
            var align = i < headerRow.Count ? headerRow[i].Align : null;
            sb.Append(' ');
            sb.Append(align switch
            {
                "left" => ":---",
                "center" => ":---:",
                "right" => "---:",
                _ => "---",
            });
            sb.Append(" |");
        }
    }

    // =========================================================================
    // Code block helpers
    // =========================================================================

    private static string FencedCodeBlock(string content, string? language, char fenceChar)
    {
        content = content.TrimEnd('\n');
        var maxRun = CountMaxConsecutiveChars(content, fenceChar);
        var fenceLength = Math.Max(3, maxRun + 1);
        var fence = new string(fenceChar, fenceLength);

        var sb = new StringBuilder();
        sb.Append(fence);
        if (!string.IsNullOrEmpty(language))
            sb.Append(language);
        sb.Append('\n');
        sb.Append(content);
        sb.Append('\n');
        sb.Append(fence);
        return sb.ToString();
    }

    private static string? ExtractLanguage(IElement? element)
    {
        if (element is null) return null;

        var classes = element.GetAttribute("class");
        if (classes is not null)
        {
            foreach (var cls in classes.Split(' '))
            {
                if (cls.StartsWith("language-", StringComparison.Ordinal))
                    return cls["language-".Length..];
            }
        }

        var dataLang = element.GetAttribute("data-language");
        if (!string.IsNullOrEmpty(dataLang))
            return dataLang;

        return null;
    }

    // =========================================================================
    // Text helpers
    // =========================================================================

    /// <summary>
    /// Escapes a link/image title for use inside double quotes in Markdown.
    /// Characters that would break the title context are backslash-escaped.
    /// </summary>
    private static void AppendLinkTitle(StringBuilder sb, string title)
    {
        sb.Append(" \"");
        foreach (var c in title)
        {
            switch (c)
            {
                case '"' or '\\':
                    sb.Append('\\');
                    sb.Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                // Always escape
                case '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '|':
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                // Escape ~ only in ~~ sequences (strikethrough)
                case '~':
                    if ((i + 1 < text.Length && text[i + 1] == '~') || (i > 0 && text[i - 1] == '~'))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // Escape - at start of text or in sequences of 2+ (thematic break)
                case '-':
                    if (i == 0 || (i + 1 < text.Length && text[i + 1] == '-') || (i > 0 && text[i - 1] == '-'))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // Escape # only at start of text (heading)
                case '#':
                    if (i == 0)
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ApplySimplePunctuation(string text)
    {
        if (text.Length == 0)
            return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '“' or '”':
                    sb.Append('"');
                    break;
                case '‘' or '’':
                    sb.Append('\'');
                    break;
                case '–':
                    sb.Append("--");
                    break;
                case '—':
                    sb.Append("---");
                    break;
                case '…':
                    sb.Append("...");
                    break;
                case '«':
                    sb.Append("<<");
                    break;
                case '»':
                    sb.Append(">>");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string ReplaceEmojiWithShortcodes(string text, EmojiShortcodeMode emojiShortcodeMode)
    {
        if (text.Length == 0)
            return text;

        if (emojiShortcodeMode is EmojiShortcodeMode.None)
            return text;

        var mappings = emojiShortcodeMode switch
        {
            EmojiShortcodeMode.GitHub => EmojiShortcodeMappings.GitHub,
            EmojiShortcodeMode.Unicode => EmojiShortcodeMappings.Unicode,
            _ => EmojiShortcodeMappings.GitHub,
        };

        if (mappings.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var changed = false;
        var textElementEnumerator = StringInfo.GetTextElementEnumerator(text);
        while (textElementEnumerator.MoveNext())
        {
            var textElement = (string)textElementEnumerator.Current;
            if (mappings.TryGetValue(textElement, out var shortcode))
            {
                sb.Append(shortcode);
                changed = true;
                continue;
            }

            if (emojiShortcodeMode == EmojiShortcodeMode.GitHub &&
                TryNormalizeVariationSelectors(textElement, out var normalizedTextElement) &&
                mappings.TryGetValue(normalizedTextElement, out shortcode))
            {
                sb.Append(shortcode);
                changed = true;
                continue;
            }

            sb.Append(textElement);
        }

        return changed ? sb.ToString() : text;
    }

    /// <summary>
    /// Removes variation selectors (U+FE0E/U+FE0F) so emoji text elements can
    /// match mappings that do not include those selectors.
    /// </summary>
    private static bool TryNormalizeVariationSelectors(string text, [NotNullWhen(true)] out string? normalizedText)
    {
        normalizedText = null;
        if (text.IndexOfAny('\uFE0E', '\uFE0F') < 0)
            return false;

        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 0xFE0E or 0xFE0F)
                continue;

            sb.Append(rune);
        }

        normalizedText = sb.ToString();
        return normalizedText.Length > 0;
    }

    /// <summary>
    /// Escapes ordered list markers (e.g., "1.") at the start of text.
    /// </summary>
    private static string PostProcessLineStart(string text)
    {
        if (text.Length > 1)
        {
            var i = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;
            if (i > 0 && i < text.Length && text[i] is '.')
                return string.Concat(text.AsSpan(0, i), "\\.", text.AsSpan(i + 1));
        }
        return text;
    }

    /// <summary>
    /// Wraps content with inline markers (e.g., ** for bold), moving
    /// leading/trailing whitespace outside the markers.
    /// </summary>
    private static string WrapInlineMarker(string content, string marker)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var start = 0;
        while (start < content.Length && content[start] == ' ')
            start++;
        var end = content.Length;
        while (end > start && content[end - 1] == ' ')
            end--;

        if (start == end)
            return content;

        var sb = new StringBuilder();
        if (start > 0) sb.Append(' ', start);
        sb.Append(marker);
        sb.Append(content, start, end - start);
        sb.Append(marker);
        if (end < content.Length) sb.Append(' ', content.Length - end);
        return sb.ToString();
    }

    /// <summary>
    /// Adds prefixes to each line of content.
    /// </summary>
    private static string PrefixLines(string content, string firstPrefix, string otherPrefix, string emptyPrefix)
    {
        var lines = content.Split('\n');
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');

            if (i == 0)
            {
                sb.Append(firstPrefix);
                sb.Append(lines[i]);
            }
            else if (lines[i].Length == 0)
            {
                sb.Append(emptyPrefix);
            }
            else
            {
                sb.Append(otherPrefix);
                sb.Append(lines[i]);
            }
        }
        return sb.ToString();
    }

    // =========================================================================
    // Classification helpers
    // =========================================================================

    private static bool IsBlockElement(INode node)
    {
        return node is IElement el && el.LocalName is
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "p" or "blockquote" or "pre" or "ul" or "ol" or "li"
            or "hr" or "table" or "thead" or "tbody" or "tfoot" or "tr"
            or "div" or "article" or "section" or "nav" or "aside"
            or "header" or "footer" or "main"
            or "dl" or "dt" or "dd" or "figure" or "figcaption"
            or "details" or "summary";
    }

    private static bool IsStrippedElement(INode node)
    {
        return node is IElement el && el.LocalName is "script" or "style" or "noscript";
    }

    /// <summary>
    /// A list is "loose" if any list item contains block elements other than sublists.
    /// </summary>
    private static bool IsLooseList(IElement listElement)
    {
        foreach (var li in listElement.Children)
        {
            if (li.LocalName != "li")
                continue;
            foreach (var child in li.Children)
            {
                if (child.LocalName is not "ul" and not "ol" && IsBlockElement(child))
                    return true;
            }
        }
        return false;
    }

    private static int CountMaxConsecutiveChars(string text, char target)
    {
        var max = 0;
        var current = 0;
        foreach (var c in text)
        {
            if (c == target)
            {
                current++;
            }
            else
            {
                max = Math.Max(max, current);
                current = 0;
            }
        }
        return Math.Max(max, current);
    }

    /// <summary>
    /// Returns the single non-whitespace child node, or null if there are
    /// zero or multiple significant children.
    /// </summary>
    private static INode? GetSingleSignificantChild(IElement element)
    {
        INode? result = null;
        foreach (var child in element.ChildNodes)
        {
            if (child is IText text && string.IsNullOrWhiteSpace(text.Data))
                continue;
            if (result is not null)
                return null;
            result = child;
        }
        return result;
    }

    private static void FlushInlineBuffer(StringBuilder inlineBuf, List<string> blocks)
    {
        var inline = CollapseInlineSpaces(inlineBuf.ToString()).Trim();
        inlineBuf.Clear();
        if (inline.Length > 0)
            blocks.Add(PostProcessLineStart(inline));
    }

    /// <summary>
    /// Collapses runs of multiple spaces into a single space, except when
    /// the spaces precede a newline (trailing spaces for line breaks).
    /// </summary>
    private static string CollapseInlineSpaces(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == ' ')
            {
                var start = i;
                while (i < text.Length && text[i] == ' ')
                    i++;

                if (i - start > 1 && i < text.Length && text[i] == '\n')
                    sb.Append("  "); // preserve trailing spaces before line break
                else
                    sb.Append(' ');
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    // =========================================================================
    // State
    // =========================================================================

    private sealed class ConversionState
    {
        public HtmlToMarkdownOptions Options { get; }
        public bool InTableCell { get; set; }

        public ConversionState(HtmlToMarkdownOptions options)
        {
            Options = options;
        }
    }
}
