using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;

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

        var document = HtmlDocumentParser.ParseDocument("<body>" + html + "</body>");
        var body = document.Body!;
        var state = new ConversionState(options, body);
        ConvertDescendants(body, state);
        var result = ConvertChildNodes(body, state);
        return result.Trim('\n', '\r');
    }

    // =========================================================================
    // Core traversal
    // =========================================================================

    /// <summary>
    /// Converts every descendant of <paramref name="root"/> bottom-up using an explicit
    /// stack, so that deeply nested documents cannot overflow the call stack. Every node
    /// is converted before any of its ancestors, and the result is recorded in
    /// <paramref name="state"/> for the ancestor to read back.
    /// </summary>
    private static void ConvertDescendants(Node root, ConversionState state)
    {
        var stack = new Stack<(Node Node, bool ChildrenConverted)>();
        PushChildren(stack, root);

        while (stack.Count > 0)
        {
            var (node, childrenConverted) = stack.Pop();
            if (childrenConverted)
            {
                state.SetConverted(node, ConvertNode(node, state));
                continue;
            }

            stack.Push((node, ChildrenConverted: true));
            if (!IsOpaqueElement(node))
                PushChildren(stack, node);
        }

        static void PushChildren(Stack<(Node Node, bool ChildrenConverted)> stack, Node parent)
        {
            foreach (var child in parent.ChildNodes)
                stack.Push((child, ChildrenConverted: false));
        }
    }

    /// <summary>
    /// Gets a value indicating whether an element is converted from its raw text or outer
    /// HTML. The descendants of such an element are never read back, so the traversal does
    /// not need to convert them.
    /// </summary>
    private static bool IsOpaqueElement(Node node)
    {
        return node is Element el && el.LocalName is "pre" or "code" or "script" or "style" or "noscript";
    }

    private static string ConvertNode(Node node, ConversionState state)
    {
        return node switch
        {
            Text text => ConvertText(text, state),
            Element element => ConvertElement(element, state),
            _ => "",
        };
    }

    private static string ConvertText(Text text, ConversionState state)
    {
        var whiteSpace = state.Options.UseCssWhiteSpace ? state.GetWhiteSpace(text.ParentElement) : WhiteSpaceMode.Collapse;
        if (whiteSpace is WhiteSpaceMode.Collapse)
            return ConvertTextSegment(CollapseWhitespace(text.Data), state);

        return ConvertPreservedWhiteSpaceText(text, whiteSpace, state);
    }

    private static string ConvertTextSegment(string content, ConversionState state)
    {
        if (state.Options.UseSimplePunctuation)
            content = ApplySimplePunctuation(content);

        content = EscapeMarkdown(content, state.Options);
        if (state.Options.EmojiShortcodeMode is not EmojiShortcodeMode.None)
        {
            content = ReplaceEmojiWithShortcodes(content, state.Options.EmojiShortcodeMode);
        }

        return content;
    }

    /// <summary>
    /// Converts a text whose style preserves its line breaks, its spaces, or both. A preserved line break becomes a hard
    /// line break. A run of preserved spaces keeps one space, and writes the other ones as <c>&amp;nbsp;</c> because a
    /// renderer collapses consecutive spaces: all of them at the start of a line, where Markdown removes the indentation.
    /// </summary>
    private static string ConvertPreservedWhiteSpaceText(Text text, WhiteSpaceMode whiteSpace, ConversionState state)
    {
        var data = text.Data.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (whiteSpace is WhiteSpaceMode.PreserveSpaces)
            data = data.Replace('\n', ' ');

        var lineBreak = text.ParentElement is { } parent && (parent.LocalName is "td" or "th" || IsInTableCell(parent))
            ? "<br>"
            : GetHardLineBreak(state);

        var sb = new StringBuilder(data.Length);
        var lines = data.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                sb.Append(lineBreak);

            var line = lines[i];
            if (whiteSpace is WhiteSpaceMode.PreserveBreaks)
            {
                // Collapsible spaces around a preserved line break are removed, as a browser does
                line = CollapseWhitespace(line);
                if (i > 0)
                    line = line.TrimStart(' ');

                if (i < lines.Length - 1)
                    line = line.TrimEnd(' ');
            }

            // A line between two preserved line breaks has to hold something: a line made of nothing but spaces
            // would be a blank line, which ends the paragraph
            if (line.Length == 0)
            {
                if (i > 0 && i < lines.Length - 1)
                    sb.Append("&nbsp;");
            }
            else if (whiteSpace is WhiteSpaceMode.PreserveBreaks)
            {
                sb.Append(ConvertTextSegment(line, state));
            }
            else
            {
                // A tab advances to a multiple of 8 columns from the start of the line, which can be in a previous text
                var startColumn = i == 0 && line.Contains('\t', StringComparison.Ordinal) ? state.GetStartColumn(text) : 0;
                AppendPreservedSpaces(sb, ConvertTextSegment(ExpandTabs(line, startColumn), state));
            }
        }

        return sb.ToString();

        static void AppendPreservedSpaces(StringBuilder sb, string line)
        {
            var index = 0;
            while (index < line.Length)
            {
                if (line[index] is not ' ')
                {
                    sb.Append(line[index]);
                    index++;
                    continue;
                }

                var start = index;
                while (index < line.Length && line[index] is ' ')
                    index++;

                for (var i = start; i < index; i++)
                {
                    if (i == start && start > 0)
                    {
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append("&nbsp;");
                    }
                }
            }
        }

        // A tab advances to the next multiple of 8 columns, the default tab-size of CSS
        static string ExpandTabs(string line, int startColumn)
        {
            if (!line.Contains('\t', StringComparison.Ordinal))
                return line;

            var sb = new StringBuilder(line.Length + 8);
            foreach (var c in line)
            {
                if (c is '\t')
                {
                    sb.Append(' ', TabSize - ((startColumn + sb.Length) % TabSize));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }

    private const int TabSize = 8;

    private static string GetHardLineBreak(ConversionState state)
    {
        return state.Options.LineBreakStyle == LineBreakStyle.Backslash ? "\\\n" : "  \n";
    }

    /// <summary>Reads the <c>white-space</c> and <c>white-space-collapse</c> declarations of the style attribute of an element.</summary>
    private static WhiteSpaceMode? GetDeclaredWhiteSpace(Element element)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
            return null;

        // An important declaration wins over the normal ones; otherwise the last valid declaration wins
        WhiteSpaceMode? result = null;
        var resultIsImportant = false;
        foreach (var (property, value, isImportant) in GetCssDeclarations(style))
        {
            if (resultIsImportant && !isImportant)
                continue;

            WhiteSpaceMode? mode;
            if (property.Equals("white-space", StringComparison.OrdinalIgnoreCase))
            {
                mode = ParseWhiteSpaceShorthand(value, out var isInherited);
                if (isInherited)
                {
                    result = null;
                    resultIsImportant = isImportant;
                    continue;
                }
            }
            else if (property.Equals("white-space-collapse", StringComparison.OrdinalIgnoreCase))
            {
                mode = ParseWhiteSpaceCollapse(value, out var isInherited);
                if (isInherited)
                {
                    result = null;
                    resultIsImportant = isImportant;
                    continue;
                }
            }
            else
            {
                continue;
            }

            // An invalid declaration is ignored: the previous one still applies
            if (mode is not null)
            {
                result = mode;
                resultIsImportant = isImportant;
            }
        }

        return result;

        static WhiteSpaceMode? ParseWhiteSpaceShorthand(string value, out bool isInherited)
        {
            isInherited = false;
            switch (value.ToLowerInvariant())
            {
                case "normal" or "nowrap" or "initial":
                    return WhiteSpaceMode.Collapse;
                case "pre" or "pre-wrap" or "break-spaces":
                    return WhiteSpaceMode.Preserve;
                case "pre-line":
                    return WhiteSpaceMode.PreserveBreaks;
                case "inherit" or "unset" or "revert" or "revert-layer":
                    isInherited = true;
                    return null;
            }

            // The longhand values, such as "preserve nowrap"
            WhiteSpaceMode? result = null;
            foreach (var part in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Equals("wrap", StringComparison.OrdinalIgnoreCase) || part.Equals("nowrap", StringComparison.OrdinalIgnoreCase))
                    continue;

                var collapse = ParseWhiteSpaceCollapse(part, out var partIsInherited);
                if (collapse is null || partIsInherited || result is not null)
                    return null;

                result = collapse;
            }

            return result;
        }

        static WhiteSpaceMode? ParseWhiteSpaceCollapse(string value, out bool isInherited)
        {
            isInherited = false;
            switch (value.ToLowerInvariant())
            {
                case "collapse" or "initial":
                    return WhiteSpaceMode.Collapse;
                case "preserve" or "break-spaces":
                    return WhiteSpaceMode.Preserve;
                case "preserve-breaks":
                    return WhiteSpaceMode.PreserveBreaks;
                case "preserve-spaces":
                    return WhiteSpaceMode.PreserveSpaces;
                case "inherit" or "unset" or "revert" or "revert-layer":
                    isInherited = true;
                    return null;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Splits an inline style into its declarations, with the property and the value trimmed and the
    /// <c>!important</c> flag removed from the value.
    /// </summary>
    private static IEnumerable<(string Property, string Value, bool IsImportant)> GetCssDeclarations(string style)
    {
        foreach (var declaration in style.Split(';'))
        {
            var colonIndex = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex < 0)
                continue;

            var property = declaration[..colonIndex].Trim();
            var value = declaration[(colonIndex + 1)..].Trim();
            var isImportant = false;
            var bangIndex = value.LastIndexOf('!', StringComparison.Ordinal);
            if (bangIndex >= 0 && value[(bangIndex + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..bangIndex].Trim();
                isImportant = true;
            }

            yield return (property, value, isImportant);
        }
    }

    private static string ConvertElement(Element element, ConversionState state)
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
            "br" => ConvertBreak(element, state),
            "input" => ConvertInput(element),
            "sub" => ConvertSubscript(element, state),
            "sup" => ConvertSuperscript(element, state),
            "u" => ConvertUnderline(element, state),
            "kbd" => ConvertKeyboard(element, state),
            "video" or "audio" or "iframe" => ConvertEmbeddedContent(element, state),

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
    private static string ConvertChildNodes(Node parent, ConversionState state)
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
                var block = state.GetConverted(child);
                if (!string.IsNullOrEmpty(block))
                    blocks.Add(block);
            }
            else
            {
                inlineBuf.Append(state.GetConverted(child));
            }
        }

        FlushInlineBuffer(inlineBuf, blocks);
        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Concatenates all children as inline content (no block grouping).
    /// Used inside inline elements like strong, em, a, etc.
    /// </summary>
    private static string ConvertInlineContent(Node parent, ConversionState state)
    {
        var sb = new StringBuilder();
        foreach (var child in parent.ChildNodes)
        {
            sb.Append(state.GetConverted(child));
        }
        return sb.ToString();
    }

    // =========================================================================
    // Block converters
    // =========================================================================

    private static string ConvertHeading(Element element, ConversionState state)
    {
        // A heading is a single line: a line break in its content would end the heading and
        // turn the remainder into a separate paragraph.
        var content = CollapseHardLineBreaks(ConvertInlineContent(element, state)).Trim();
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

    /// <summary>
    /// Replaces hard line breaks with a single space so inline content fits on one line.
    /// A hard line break is a newline preceded either by trailing spaces or by a backslash,
    /// depending on <see cref="LineBreakStyle"/>; the backslash has to go with it.
    /// </summary>
    private static string CollapseHardLineBreaks(string content)
    {
        return CollapseWhitespace(content.Replace("\\\n", "\n", StringComparison.Ordinal));
    }

    private static string ConvertParagraph(Element element, ConversionState state)
    {
        var content = TrimInline(ConvertInlineContent(element, state));
        return PostProcessLineStart(content);
    }

    private static string ConvertBlockquote(Element element, ConversionState state)
    {
        var content = ConvertChildNodes(element, state);
        if (string.IsNullOrEmpty(content))
            return "";
        return PrefixLines(content, "> ", "> ", ">");
    }

    private static string ConvertPre(Element element, ConversionState state)
    {
        var codeElement = element.QuerySelector("code");

        // Only take the content from <code> when it is the sole significant child of the
        // <pre>. QuerySelector matches any descendant, so using it unconditionally would
        // silently drop everything around the match. The language may still come from a
        // nested <code>, because a wrong language annotation costs less than lost content.
        var contentElement = GetSingleSignificantChild(element) is Element { LocalName: "code" } soleCodeChild
            ? soleCodeChild
            : element;
        var content = contentElement.TextContent ?? "";
        var language = ExtractLanguage(codeElement) ?? ExtractLanguage(element);

        if (state.Options.CodeBlockStyle == CodeBlockStyle.Indented)
        {
            return PrefixLines(content.TrimEnd('\n'), "    ", "    ", "");
        }

        return FencedCodeBlock(content, language, state.Options.CodeBlockFenceCharacter);
    }

    private static string ConvertUnorderedList(Element element, ConversionState state)
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

    private static string ConvertOrderedList(Element element, ConversionState state)
    {
        var start = ParseListStart(element.GetAttribute("start"));
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
            if (index < MaximumListStart)
                index++;
        }

        return string.Join(isLoose ? "\n\n" : "\n", items);
    }

    // CommonMark allows an ordered list marker of at most nine digits.
    private const int MaximumListStart = 999_999_999;

    /// <summary>
    /// Parses the start attribute of an ordered list. Values that cannot produce a valid
    /// Markdown list marker fall back to 1, because emitting them would turn the list into
    /// a plain paragraph.
    /// </summary>
    private static int ParseListStart(string? value)
    {
        if (!int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var start))
            return 1;

        return start is < 0 or > MaximumListStart ? 1 : start;
    }

    /// <summary>
    /// Converts the content of a list item, using single newline before
    /// sublists (tight) and double newline before other block elements.
    /// </summary>
    private static string ConvertListItemContent(Element li, ConversionState state)
    {
        var parts = new List<(string Content, bool IsList)>();
        var inlineBuf = new StringBuilder();

        foreach (var child in li.ChildNodes)
        {
            if (IsStrippedElement(child))
                continue;

            if (IsBlockElement(child))
            {
                var inline = TrimInline(CollapseInlineSpaces(inlineBuf.ToString()));
                inlineBuf.Clear();
                if (inline.Length > 0)
                    parts.Add((PostProcessLineStart(inline), false));

                var block = state.GetConverted(child);
                if (!string.IsNullOrEmpty(block))
                {
                    var isList = child is Element el && el.LocalName is "ul" or "ol";
                    parts.Add((block, isList));
                }
            }
            else
            {
                inlineBuf.Append(state.GetConverted(child));
            }
        }

        var remaining = TrimInline(CollapseInlineSpaces(inlineBuf.ToString()));
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

    private static string ConvertTable(Element table, ConversionState state)
    {
        // A group holds the rows of a thead, a tbody or a tfoot: a cell cannot span the rows of another group
        var headerGroups = new List<List<Element>>();
        var bodyGroups = new List<List<Element>>();
        var footerGroups = new List<List<Element>>();
        List<Element>? directRows = null;

        foreach (var child in table.Children)
        {
            switch (child.LocalName)
            {
                case "thead":
                    headerGroups.Add(GetRows(child));
                    directRows = null;
                    break;

                case "tbody":
                    bodyGroups.Add(GetRows(child));
                    directRows = null;
                    break;

                case "tfoot":
                    footerGroups.Add(GetRows(child));
                    directRows = null;
                    break;

                case "tr":
                    if (directRows is null)
                    {
                        directRows = [];
                        bodyGroups.Add(directRows);
                    }

                    directRows.Add(child);
                    break;
            }
        }

        var groups = new List<List<Element>>(headerGroups.Count + bodyGroups.Count + footerGroups.Count);
        groups.AddRange(headerGroups);
        groups.AddRange(bodyGroups);
        groups.AddRange(footerGroups);

        var rows = BuildTableGrid(groups, state, out var hasSpans, out var hasBlocks);
        if (rows.Count == 0)
            return "";

        if (state.Options.ComplexTableHandling is ComplexTableHandling.Html)
        {
            var headerRowCount = headerGroups.Sum(group => group.Count);
            if (hasBlocks || headerRowCount > 1 || (hasSpans && state.Options.TableSpanHandling is TableSpanHandling.Ignore))
                return GetRawHtml(table);
        }

        var duplicateSpans = state.Options.TableSpanHandling is TableSpanHandling.Duplicate;
        var colCount = rows.Max(r => r.Count);
        var sb = new StringBuilder();

        // Header row (first row)
        var headerRow = rows[0];
        AppendTableRow(sb, headerRow, colCount, duplicateSpans);
        sb.Append('\n');

        // Separator row with alignment
        AppendSeparatorRow(sb, headerRow, colCount);

        // Body rows
        for (var i = 1; i < rows.Count; i++)
        {
            sb.Append('\n');
            AppendTableRow(sb, rows[i], colCount, duplicateSpans);
        }

        return sb.ToString();

        static List<Element> GetRows(Element group) => [.. group.Children.Where(c => c.LocalName == "tr")];
    }

    private static string ConvertDefinitionList(Element element, ConversionState state)
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

                // Convert as blocks so multiple paragraphs keep their separation, and
                // indent continuation lines so they stay part of the definition.
                var definition = ConvertChildNodes(child, state).Trim();
                sb.Append(PrefixLines(definition, ":   ", "    ", ""));
                lastWasDt = false;
                isFirst = false;
            }
        }

        return sb.ToString();
    }

    // =========================================================================
    // Inline converters
    // =========================================================================

    private static string ConvertStrong(Element element, ConversionState state)
    {
        // Collapse redundant nesting: <strong><strong>x</strong></strong>
        var onlyChild = GetSingleSignificantChild(element);
        if (onlyChild is Element childEl && childEl.LocalName is "strong" or "b")
            return state.GetConverted(childEl);

        var content = ConvertInlineContent(element, state);
        var marker = state.Options.EmphasisMarker == EmphasisMarker.Asterisk ? "**" : "__";
        return WrapInlineMarker(content, marker);
    }

    private static string ConvertEmphasis(Element element, ConversionState state)
    {
        var onlyChild = GetSingleSignificantChild(element);
        if (onlyChild is Element childEl && childEl.LocalName is "em" or "i")
            return state.GetConverted(childEl);

        var content = ConvertInlineContent(element, state);
        var marker = state.Options.EmphasisMarker == EmphasisMarker.Asterisk ? "*" : "_";
        return WrapInlineMarker(content, marker);
    }

    private static string ConvertStrikethrough(Element element, ConversionState state)
    {
        var content = ConvertInlineContent(element, state);
        return WrapInlineMarker(content, "~~");
    }

    private static string ConvertInlineCode(Element element)
    {
        var content = element.TextContent;
        if (string.IsNullOrEmpty(content))
            return "";

        // A code span whose content both begins and ends with a space has one space removed
        // from each end when it is parsed, so it has to be padded to survive the round trip.
        // Content made entirely of spaces is left alone: that rule does not apply to it.
        var needSpace = content.StartsWith('`', StringComparison.Ordinal)
            || content.EndsWith('`', StringComparison.Ordinal)
            || (content.StartsWith(' ', StringComparison.Ordinal)
                && content.EndsWith(' ', StringComparison.Ordinal)
                && content.AsSpan().Trim(' ').Length > 0);
        var openCount = CountMaxConsecutiveChars(content, '`') + 1;

        var sb = new StringBuilder();
        sb.Append('`', openCount);
        if (needSpace) sb.Append(' ');
        sb.Append(content);
        if (needSpace) sb.Append(' ');
        sb.Append('`', openCount);
        return sb.ToString();
    }

    private static string ConvertLink(Element element, ConversionState state)
    {
        // GetAttribute returns null only when the attribute is absent: <a href> yields "".
        var href = element.GetAttribute("href");
        if (href is null)
            return ConvertInlineContent(element, state);

        var title = element.GetAttribute("title");
        var content = ConvertInlineContent(element, state);

        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(content);
        sb.Append("](");
        AppendLinkDestination(sb, href);
        if (!string.IsNullOrEmpty(title))
        {
            AppendLinkTitle(sb, title);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string ConvertImage(Element element)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src))
            return "";

        var alt = element.GetAttribute("alt") ?? "";
        var title = element.GetAttribute("title");

        var sb = new StringBuilder();
        sb.Append("![");
        AppendLinkText(sb, alt);
        sb.Append("](");
        AppendLinkDestination(sb, src);
        if (!string.IsNullOrEmpty(title))
        {
            AppendLinkTitle(sb, title);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string ConvertBreak(Element element, ConversionState state)
    {
        if (IsInTableCell(element))
            return "<br>";
        return GetHardLineBreak(state);
    }

    private static string ConvertSubscript(Element element, ConversionState state)
    {
        return state.Options.SubscriptStyle switch
        {
            SubscriptStyle.Html => WrapInlineHtml(element, state),
            SubscriptStyle.Tilde => WrapCompactMarker(element, state, '~'),
            SubscriptStyle.Text => ConvertInlineContent(element, state),
            _ => ConvertUnknownElement(element, state),
        };
    }

    private static string ConvertSuperscript(Element element, ConversionState state)
    {
        return state.Options.SuperscriptStyle switch
        {
            SuperscriptStyle.Html => WrapInlineHtml(element, state),
            SuperscriptStyle.Caret => WrapCompactMarker(element, state, '^'),
            SuperscriptStyle.Text => ConvertInlineContent(element, state),
            _ => ConvertUnknownElement(element, state),
        };
    }

    private static string ConvertUnderline(Element element, ConversionState state)
    {
        return state.Options.UnderlineStyle switch
        {
            UnderlineStyle.Html => WrapInlineHtml(element, state),
            UnderlineStyle.PlusSigns => WrapInlineMarker(ConvertInlineContent(element, state), "++"),
            UnderlineStyle.Text => ConvertInlineContent(element, state),
            _ => ConvertUnknownElement(element, state),
        };
    }

    private static string ConvertKeyboard(Element element, ConversionState state)
    {
        return state.Options.KeyboardStyle switch
        {
            KeyboardStyle.Html => WrapInlineHtml(element, state),
            KeyboardStyle.CodeSpan => ConvertInlineCode(element),
            KeyboardStyle.Text => ConvertInlineContent(element, state),
            _ => ConvertUnknownElement(element, state),
        };
    }

    private static string ConvertEmbeddedContent(Element element, ConversionState state)
    {
        return state.Options.EmbeddedContentStyle switch
        {
            EmbeddedContentStyle.Html => GetRawHtml(element),
            EmbeddedContentStyle.Link => ConvertEmbeddedContentToLink(element, state),
            EmbeddedContentStyle.Remove => "",
            _ => ConvertUnknownElement(element, state),
        };

        static string ConvertEmbeddedContentToLink(Element element, ConversionState state)
        {
            var src = element.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src) && element.LocalName is "video" or "audio")
            {
                foreach (var child in element.Children)
                {
                    if (child.LocalName is "source" && child.GetAttribute("src") is { } sourceSrc && !string.IsNullOrWhiteSpace(sourceSrc))
                    {
                        src = sourceSrc;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(src))
                return "";

            src = src.Trim();
            var label = CollapseWhitespace(element.GetAttribute("title") ?? "").Trim();
            if (label.Length == 0)
                label = src;

            var sb = new StringBuilder();
            sb.Append('[');
            sb.Append(EscapeMarkdown(label, state.Options));
            sb.Append("](");
            AppendLinkDestination(sb, src);
            sb.Append(')');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Writes the converted content of an element between its start and end tags, without its attributes. Markdown
    /// renders inline HTML tags, and still reads the Markdown between them.
    /// </summary>
    private static string WrapInlineHtml(Element element, ConversionState state)
    {
        var content = ConvertInlineContent(element, state);
        if (string.IsNullOrWhiteSpace(content))
            return content;

        return "<" + element.LocalName + ">" + content + "</" + element.LocalName + ">";
    }

    /// <summary>
    /// Writes the content between two markers that do not allow spaces, as the subscript (<c>~</c>) and superscript
    /// (<c>^</c>) extensions of Pandoc and markdown-it: the spaces of the content are escaped. Content that the markers
    /// cannot hold, such as a code span or a nested marker, is written as inline HTML.
    /// </summary>
    private static string WrapCompactMarker(Element element, ConversionState state, char marker)
    {
        var content = CollapseHardLineBreaks(ConvertInlineContent(element, state));
        if (string.IsNullOrWhiteSpace(content))
            return content;

        var start = 0;
        while (start < content.Length && content[start] == ' ')
            start++;

        var end = content.Length;
        while (end > start && content[end - 1] == ' ')
            end--;

        var inner = content[start..end];
        if (inner.Contains('`', StringComparison.Ordinal) || inner.Contains('<', StringComparison.Ordinal) || inner.Contains("](", StringComparison.Ordinal) || ContainsUnescaped(inner, marker) || inner.EndsWith('\\', StringComparison.Ordinal))
            return content[..start] + "<" + element.LocalName + ">" + inner + "</" + element.LocalName + ">" + content[end..];

        return content[..start] + marker + inner.Replace(" ", "\\ ", StringComparison.Ordinal) + marker + content[end..];

        static bool ContainsUnescaped(string text, char c)
        {
            var escaped = false;
            foreach (var current in text)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current is '\\')
                {
                    escaped = true;
                }
                else if (current == c)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Determines whether an element is inside a table cell by walking its ancestors.
    /// </summary>
    private static bool IsInTableCell(Element element)
    {
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName is "td" or "th")
                return true;
        }

        return false;
    }

    private static string ConvertInput(Element element)
    {
        if (string.Equals(element.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
            return element.HasAttribute("checked") ? "[x]" : "[ ]";
        return "";
    }

    private static string ConvertUnknownElement(Element element, ConversionState state)
    {
        return state.Options.UnknownElementHandling switch
        {
            UnknownElementHandling.Strip => "",
            UnknownElementHandling.StripKeepContent => ConvertChildNodes(element, state),
            _ => GetRawHtml(element),
        };
    }

    /// <summary>
    /// Gets the outer HTML of an element with its script, style and noscript descendants
    /// removed. Those elements are stripped everywhere else, so passing an unknown element
    /// through verbatim must not reintroduce them.
    /// </summary>
    private static string GetOuterHtmlWithoutStrippedElements(Element element)
    {
        if (element.QuerySelector(StrippedElementsSelector) is null)
            return element.OuterHtml;

        return CopyWithoutStrippedElements(element).OuterHtml;
    }

    /// <summary>
    /// Copies an element and its descendants, except the script, style and noscript elements. The copy uses an
    /// explicit stack: <see cref="Node.CloneNode(bool)"/> with a subtree recurses once per nesting level, so a deeply
    /// nested document would overflow the call stack.
    /// </summary>
    private static Element CopyWithoutStrippedElements(Element element)
    {
        var copy = (Element)element.CloneNode(subtree: false);
        var stack = new Stack<(Node Source, Node Copy)>();
        Push(stack, element, copy);
        while (stack.Count > 0)
        {
            var (source, target) = stack.Pop();
            for (var child = source.FirstChild; child is not null; child = child.NextSibling)
            {
                if (IsStrippedElement(child))
                    continue;

                var childCopy = child.CloneNode(subtree: false);
                target.AppendChild(childCopy);
                Push(stack, child, childCopy);
            }
        }

        // Removing an element can leave two text nodes side by side
        copy.Normalize();
        return copy;

        static void Push(Stack<(Node Source, Node Copy)> stack, Node source, Node copy)
        {
            if (source.FirstChild is not null)
                stack.Push((source, copy));

            // The content of a template is not one of its children
            if (source is HtmlTemplateElement template && copy is HtmlTemplateElement templateCopy && template.Content.FirstChild is not null)
                stack.Push((template.Content, templateCopy.Content));
        }
    }

    /// <summary>
    /// Gets the HTML written for an element passed through as raw HTML. A blank line ends an HTML block, and a raw HTML
    /// tag cannot span one, so the blank lines are removed from the copy that is written. In text, where a line can be
    /// significant (in a <c>pre</c> element), an empty comment is inserted on the blank line instead: it renders nothing.
    /// The blank lines of comments, attribute values and raw text elements (<c>textarea</c>, <c>title</c>...) are
    /// removed, because markup there would be read as text.
    /// </summary>
    private static string GetRawHtml(Element element)
    {
        var html = GetOuterHtmlWithoutStrippedElements(element);
        if (!ContainsBlankLine(html))
            return html;

        var clone = CopyWithoutStrippedElements(element);
        var document = element.OwnerDocument!;
        foreach (var node in clone.DescendantsAndSelf().ToList())
        {
            switch (node)
            {
                case Element el:
                    foreach (var attribute in el.Attributes)
                    {
                        if (ContainsBlankLine(attribute.Value))
                            attribute.Value = RemoveBlankLines(attribute.Value);
                    }

                    break;

                case Comment comment when ContainsBlankLine(comment.Data):
                    comment.Data = RemoveBlankLines(comment.Data);
                    break;

                case Text text when ContainsBlankLine(text.Data):
                    if (text.ParentElement?.LocalName is "textarea" or "title" or "xmp" or "iframe" or "noembed" or "noframes" or "plaintext")
                    {
                        text.Data = RemoveBlankLines(text.Data);
                    }
                    else
                    {
                        InsertCommentsOnBlankLines(text, document);
                    }

                    break;
            }
        }

        return clone.OuterHtml;

        static void InsertCommentsOnBlankLines(Text text, Document document)
        {
            var parent = text.ParentNode!;
            var next = text.NextSibling;
            var lines = text.Data.Split('\n');
            var segment = new StringBuilder(lines[0]);
            var isFirstSegment = true;
            for (var i = 1; i < lines.Length; i++)
            {
                segment.Append('\n').Append(lines[i]);
                if (i < lines.Length - 1 && string.IsNullOrWhiteSpace(lines[i]))
                {
                    AddSegment(segment.ToString());
                    parent.InsertBefore(document.CreateComment(""), next);
                    segment.Clear();
                }
            }

            AddSegment(segment.ToString());

            void AddSegment(string value)
            {
                if (isFirstSegment)
                {
                    text.Data = value;
                    isFirstSegment = false;
                }
                else if (value.Length > 0)
                {
                    parent.InsertBefore(document.CreateTextNode(value), next);
                }
            }
        }
    }

    private static bool ContainsBlankLine(string value)
    {
        var index = value.IndexOf('\n', StringComparison.Ordinal);
        while (index >= 0)
        {
            var nextIndex = value.IndexOf('\n', index + 1, StringComparison.Ordinal);
            if (nextIndex < 0)
                return false;

            if (value.AsSpan(index + 1, nextIndex - index - 1).IsWhiteSpace())
                return true;

            index = nextIndex;
        }

        return false;
    }

    // Removes the lines made only of whitespace between two line breaks
    private static string RemoveBlankLines(string value)
    {
        var lines = value.Split('\n');
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0 && i < lines.Length - 1 && string.IsNullOrWhiteSpace(lines[i]))
                continue;

            if (i > 0)
                sb.Append('\n');

            sb.Append(lines[i]);
        }

        return sb.ToString();
    }

    // =========================================================================
    // Table helpers
    // =========================================================================

    /// <summary>
    /// Places the cells of the rows in a grid. When <see cref="HtmlToMarkdownOptions.TableSpanHandling"/> honors the spans,
    /// a cell fills every position it spans, and the following cells of a row skip the positions that a cell of a
    /// previous row spans, as the table model of HTML does. A position that no cell fills is <see langword="null"/>.
    /// A span that would make the grid larger than <see cref="MaximumTableGridSize"/> positions is ignored, so that a
    /// small document cannot allocate a huge grid.
    /// </summary>
    private static List<List<TableCell?>> BuildTableGrid(List<List<Element>> groups, ConversionState state, out bool hasSpans, out bool hasBlocks)
    {
        hasSpans = false;
        hasBlocks = false;
        var honorSpans = state.Options.TableSpanHandling is not TableSpanHandling.Ignore;
        var rowCount = groups.Sum(group => group.Count);
        var width = 0;
        long spannedPositions = 0;
        var rows = new List<List<TableCell?>>(rowCount);
        foreach (var group in groups)
        {
            var groupStart = rows.Count;
            var groupEnd = groupStart + group.Count;
            for (var i = 0; i < group.Count; i++)
            {
                rows.Add([]);
            }

            for (var i = 0; i < group.Count; i++)
            {
                var rowIndex = groupStart + i;
                var row = rows[rowIndex];
                var column = 0;
                foreach (var cell in group[i].Children)
                {
                    if (cell.LocalName is not ("th" or "td"))
                        continue;

                    var content = ConvertTableCellContent(cell, state, out var cellHasBlocks);
                    hasBlocks |= cellHasBlocks;
                    var align = GetCellAlignment(cell);

                    // The limits and the meaning of 0 come from the table model of HTML
                    var colSpan = ParseSpan(cell.GetAttribute("colspan"), defaultValue: 1, maximum: 1000);
                    if (colSpan == 0)
                        colSpan = 1;

                    var rowSpan = ParseSpan(cell.GetAttribute("rowspan"), defaultValue: 1, maximum: 65534);
                    if (rowSpan == 0 || rowSpan > groupEnd - rowIndex)
                        rowSpan = groupEnd - rowIndex;

                    hasSpans |= colSpan > 1 || rowSpan > 1;
                    while (column < row.Count && row[column] is not null)
                        column++;

                    // The grid is written with as many columns as its widest row, in every row
                    if (!honorSpans
                        || (long)rowCount * Math.Max(width, column + colSpan) > MaximumTableGridSize
                        || spannedPositions + ((long)colSpan * rowSpan) > MaximumTableGridSize)
                    {
                        colSpan = 1;
                        rowSpan = 1;
                    }

                    width = Math.Max(width, column + colSpan);
                    if (colSpan > 1 || rowSpan > 1)
                        spannedPositions += (long)colSpan * rowSpan;

                    for (var r = 0; r < rowSpan; r++)
                    {
                        var target = rows[rowIndex + r];
                        for (var c = 0; c < colSpan; c++)
                        {
                            var targetColumn = column + c;
                            while (target.Count <= targetColumn)
                                target.Add(null);

                            target[targetColumn] = new TableCell(content, align, IsSpanned: r > 0 || c > 0);
                        }
                    }

                    column += colSpan;
                }
            }
        }

        return rows;
    }

    private const int MaximumTableGridSize = 1_000_000;

    /// <summary>
    /// Parses a colspan or rowspan attribute with the rules for parsing non-negative integers of HTML: leading
    /// whitespace, an optional plus sign, then digits; the characters after the digits are ignored.
    /// </summary>
    private static int ParseSpan(string? value, int defaultValue, int maximum)
    {
        if (value is null)
            return defaultValue;

        var index = 0;
        while (index < value.Length && value[index] is ' ' or '\t' or '\n' or '\f' or '\r')
            index++;

        if (index < value.Length && value[index] is '+')
            index++;

        if (index >= value.Length || !char.IsAsciiDigit(value[index]))
            return defaultValue;

        var result = 0;
        while (index < value.Length && char.IsAsciiDigit(value[index]))
        {
            result = Math.Min(maximum, (result * 10) + (value[index] - '0'));
            index++;
        }

        return result;
    }

    // The blocks that a cell of a pipe table cannot hold
    private const string TableCellBlockSelector = "h1, h2, h3, h4, h5, h6, blockquote, pre, ul, ol, table, hr, dl";

    /// <summary>
    /// Converts the content of a table cell. Block children are converted as blocks rather
    /// than concatenated, then the result is collapsed onto a single line: a row of a pipe
    /// table cannot contain a line break.
    /// </summary>
    private static string ConvertTableCellContent(Element cell, ConversionState state, out bool hasBlocks)
    {
        var content = ConvertChildNodes(cell, state).Trim();
        hasBlocks = content.Contains('\n', StringComparison.Ordinal) || cell.QuerySelector(TableCellBlockSelector) is not null;
        if (!content.Contains('\n', StringComparison.Ordinal))
            return content;

        var sb = new StringBuilder();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (sb.Length > 0)
                sb.Append("<br>");

            sb.Append(trimmed);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the alignment of a table cell by checking, in order:
    /// 1. The CSS text-align property from the style attribute
    /// 2. The HTML align attribute
    /// </summary>
    private static string? GetCellAlignment(Element cell)
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
        foreach (var (property, value, _) in GetCssDeclarations(style))
        {
            if (property.Equals("text-align", StringComparison.OrdinalIgnoreCase))
            {
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

    private static void AppendTableRow(StringBuilder sb, List<TableCell?> row, int colCount, bool duplicateSpans)
    {
        sb.Append('|');
        for (var i = 0; i < colCount; i++)
        {
            sb.Append(' ');
            if (i < row.Count && row[i] is { } cell && (!cell.IsSpanned || duplicateSpans))
                sb.Append(cell.Content);

            sb.Append(" |");
        }
    }

    private static void AppendSeparatorRow(StringBuilder sb, List<TableCell?> headerRow, int colCount)
    {
        sb.Append('|');
        for (var i = 0; i < colCount; i++)
        {
            var align = i < headerRow.Count ? headerRow[i]?.Align : null;
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

    /// <summary>A position of the grid of a table.</summary>
    /// <param name="Content">The converted content of the cell.</param>
    /// <param name="Align">The alignment of the cell.</param>
    /// <param name="IsSpanned">Whether the position is covered by the span of a cell that starts at another position.</param>
    private readonly record struct TableCell(string Content, string? Align, bool IsSpanned);

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

    private static string? ExtractLanguage(Element? element)
    {
        if (element is null) return null;

        var classes = element.GetAttribute("class");
        if (classes is not null)
        {
            // The class attribute is separated by any whitespace, not only spaces.
            foreach (var cls in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (cls.StartsWith("language-", StringComparison.Ordinal))
                    return SanitizeLanguage(cls["language-".Length..]);
            }
        }

        var dataLang = element.GetAttribute("data-language");
        if (!string.IsNullOrEmpty(dataLang))
            return SanitizeLanguage(dataLang);

        return null;
    }

    /// <summary>
    /// Validates the info string of a fenced code block. The value comes from the document
    /// and is copied onto the opening fence, so one containing a line break or a fence
    /// character would end the code block early and let the rest of the attribute be
    /// interpreted as Markdown. Letters and digits are accepted, including non-ASCII ones,
    /// and anything outside the accepted set drops the info string.
    /// </summary>
    private static string? SanitizeLanguage(string language)
    {
        if (language.Length == 0)
            return null;

        foreach (var c in language)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '+' or '_' or '.' or '#'))
                return null;
        }

        return language;
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

    /// <summary>
    /// Appends a link or image destination, escaping the characters that would otherwise
    /// end it. A destination containing a space must use the angle bracket form, because
    /// a space cannot be backslash-escaped there.
    /// </summary>
    private static void AppendLinkDestination(StringBuilder sb, string url)
    {
        if (url.Contains(' ', StringComparison.Ordinal))
        {
            sb.Append('<');
            foreach (var c in url)
            {
                switch (c)
                {
                    case '<' or '>' or '\\':
                        sb.Append('\\');
                        sb.Append(c);
                        break;

                    default:
                        AppendUrlCharacter(sb, c);
                        break;
                }
            }
            sb.Append('>');
            return;
        }

        var escapeParentheses = !HasBalancedParentheses(url);
        foreach (var c in url)
        {
            switch (c)
            {
                case '(' or ')' when escapeParentheses:
                case '<' or '>' or '\\':
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                default:
                    AppendUrlCharacter(sb, c);
                    break;
            }
        }
    }

    /// <summary>
    /// Determines whether the parentheses in a link destination are balanced. A bare
    /// destination may contain balanced parentheses, so only unbalanced ones need escaping.
    /// </summary>
    private static bool HasBalancedParentheses(string url)
    {
        var depth = 0;
        foreach (var c in url)
        {
            if (c is '(')
            {
                depth++;
            }
            else if (c is ')')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    /// <summary>
    /// Appends a character of a URL, percent-encoding the control characters that cannot
    /// appear in a link destination in any form.
    /// </summary>
    private static void AppendUrlCharacter(StringBuilder sb, char c)
    {
        if (char.IsControl(c))
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"%{(int)c:X2}");
        }
        else
        {
            sb.Append(c);
        }
    }

    /// <summary>
    /// Appends literal text used as a link label or image description, escaping the
    /// brackets that would otherwise end it.
    /// </summary>
    private static void AppendLinkText(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '[' or ']' or '\\':
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }
    }

    private static string EscapeMarkdown(string text, HtmlToMarkdownOptions options)
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

                // Escape & only when it could start an entity reference. Markdown has no
                // backslash escape for &, so the entity form is the only way to keep a
                // literal & from being decoded into a different character by the renderer.
                case '&':
                    if (i + 1 < text.Length && (char.IsAsciiLetter(text[i + 1]) || text[i + 1] is '#'))
                    {
                        sb.Append("&amp;");
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;

                // Escape ~ only in ~~ sequences (strikethrough), or everywhere when it marks subscripts
                case '~':
                    if (options.SubscriptStyle is SubscriptStyle.Tilde || (i + 1 < text.Length && text[i + 1] == '~') || (i > 0 && text[i - 1] == '~'))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // Escape ^ when it marks superscripts
                case '^':
                    if (options.SuperscriptStyle is SuperscriptStyle.Caret)
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // Escape - at start of text or in sequences of 2+ (thematic break)
                case '-':
                    if (i == 0 || (i + 1 < text.Length && text[i + 1] == '-') || (i > 0 && text[i - 1] == '-'))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // Escape # (heading) and + (bullet list) only at start of text, and + in sequences of 2+ when it marks underlines
                case '#' or '+':
                    if (i == 0 || (c is '+' && options.UnderlineStyle is UnderlineStyle.PlusSigns && ((i + 1 < text.Length && text[i + 1] == '+') || (i > 0 && text[i - 1] == '+'))))
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
        var index = 0;
        while (index < text.Length)
        {
            var remaining = text.AsSpan(index);
            var length = StringInfo.GetNextTextElementLength(remaining);
            var element = remaining[..length];
            index += length;

            // No mapping key is a single ASCII character, so ordinary text can be copied
            // without materializing a string for every grapheme cluster.
            if (length == 1 && char.IsAscii(element[0]))
            {
                sb.Append(element[0]);
                continue;
            }

            var textElement = element.ToString();
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
    /// Escapes constructs that would start a new block when they appear at the beginning of
    /// a line: ordered list markers such as "1." and "4)", and setext heading underlines.
    /// This runs per line because a line break inside a paragraph puts text at the start of
    /// a line without it being the start of the block.
    /// </summary>
    private static string PostProcessLineStart(string text)
    {
        if (!text.Contains('\n', StringComparison.Ordinal))
            return EscapeLineStart(text);

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = EscapeLineStart(lines[i]);
        }

        return string.Join('\n', lines);
    }

    private static string EscapeLineStart(string line)
    {
        if (line.Length == 0)
            return line;

        // Ordered list marker: a run of digits followed by '.' or ')'
        var i = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]))
            i++;

        if (i > 0 && i < line.Length && line[i] is '.' or ')')
            return string.Concat(line.AsSpan(0, i), "\\", line.AsSpan(i, 1), line.AsSpan(i + 1));

        // Setext heading underline: a line containing nothing but '='
        if (line[0] is '=' && IsSetextUnderline(line))
            return "\\" + line;

        return line;

        static bool IsSetextUnderline(string line)
        {
            foreach (var c in line)
            {
                if (c is not ('=' or ' '))
                    return false;
            }

            return true;
        }
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

    private static bool IsBlockElement(Node node)
    {
        return node is Element el && el.LocalName is
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "p" or "blockquote" or "pre" or "ul" or "ol" or "li"
            or "hr" or "table" or "thead" or "tbody" or "tfoot" or "tr"
            or "div" or "article" or "section" or "nav" or "aside"
            or "header" or "footer" or "main"
            or "dl" or "dt" or "dd" or "figure" or "figcaption"
            or "details" or "summary";
    }

    private const string StrippedElementsSelector = "script, style, noscript";

    private static bool IsStrippedElement(Node node)
    {
        return node is Element el && el.LocalName is "script" or "style" or "noscript";
    }

    /// <summary>
    /// A list is "loose" if any list item contains block elements other than sublists.
    /// </summary>
    private static bool IsLooseList(Element listElement)
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
    private static Node? GetSingleSignificantChild(Element element)
    {
        Node? result = null;
        foreach (var child in element.ChildNodes)
        {
            if (child is Text text && string.IsNullOrWhiteSpace(text.Data))
                continue;
            if (result is not null)
                return null;
            result = child;
        }
        return result;
    }

    private static void FlushInlineBuffer(StringBuilder inlineBuf, List<string> blocks)
    {
        var inline = TrimInline(CollapseInlineSpaces(inlineBuf.ToString()));
        inlineBuf.Clear();
        if (inline.Length > 0)
            blocks.Add(PostProcessLineStart(inline));
    }

    /// <summary>
    /// Trims the inline content of a block: the whitespace, the hard line breaks at its start and at its end, which
    /// render nothing there, and a content made of nothing else than spaces preserved by CSS.
    /// </summary>
    private static string TrimInline(string content)
    {
        var start = 0;
        var end = content.Length;
        while (true)
        {
            while (start < end && char.IsWhiteSpace(content[start]))
                start++;

            if (start + 1 < end && content[start] is '\\' && content[start + 1] is '\n')
            {
                start += 2;
                continue;
            }

            break;
        }

        while (true)
        {
            while (end > start && char.IsWhiteSpace(content[end - 1]))
                end--;

            // A backslash that was followed by a line break is a hard line break
            if (end > start && end < content.Length && content[end - 1] is '\\' && content[end] is '\n')
            {
                end--;
                continue;
            }

            break;
        }

        var result = content[start..end];
        if (result.Contains("&nbsp;", StringComparison.Ordinal) && IsBlank(result))
            return "";

        return result;

        static bool IsBlank(string text)
        {
            var withoutSpaces = text.Replace("&nbsp;", "", StringComparison.Ordinal).Replace("\\\n", "", StringComparison.Ordinal);
            return string.IsNullOrWhiteSpace(withoutSpaces);
        }
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
        private readonly Dictionary<Node, string> _converted = [];
        private readonly Dictionary<Element, WhiteSpaceMode> _whiteSpace = [];

        private readonly Node _root;
        private Dictionary<Text, int>? _startColumns;

        public HtmlToMarkdownOptions Options { get; }

        public ConversionState(HtmlToMarkdownOptions options, Node root)
        {
            Options = options;
            _root = root;
        }

        /// <summary>
        /// Gets the column of the rendered line at which a text starts, so that a tab advances to the same position as
        /// in a browser when elements split the line. A line starts at a block element, a <c>br</c> or a preserved line
        /// break. The columns of all the texts are computed in one pass, the first time one is needed.
        /// </summary>
        public int GetStartColumn(Text text)
        {
            if (_startColumns is null)
            {
                _startColumns = [];
                ComputeStartColumns();
            }

            return _startColumns.TryGetValue(text, out var column) ? column : 0;
        }

        private void ComputeStartColumns()
        {
            var column = 0;
            var stack = new Stack<(Node Node, bool Exiting)>();
            PushChildren(stack, _root);
            while (stack.Count > 0)
            {
                var (node, exiting) = stack.Pop();
                if (node is Text text)
                {
                    _startColumns![text] = column;
                    column = AdvanceColumn(column, text);
                    continue;
                }

                if (node is not Element element || IsStrippedElement(element))
                    continue;

                if (element.LocalName is "br" || IsBlockElement(element))
                    column = 0;

                if (!exiting)
                {
                    stack.Push((element, Exiting: true));
                    PushChildren(stack, element);
                }
            }

            static void PushChildren(Stack<(Node Node, bool Exiting)> stack, Node parent)
            {
                var children = parent.ChildNodes;
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push((children[i], Exiting: false));
                }
            }
        }

        private int AdvanceColumn(int column, Text text)
        {
            var whiteSpace = Options.UseCssWhiteSpace ? GetWhiteSpace(text.ParentElement) : WhiteSpaceMode.Collapse;
            var data = whiteSpace switch
            {
                WhiteSpaceMode.Collapse or WhiteSpaceMode.PreserveBreaks => CollapseWhitespaceExceptLineBreaks(text.Data, whiteSpace),
                WhiteSpaceMode.PreserveSpaces => text.Data.Replace('\n', ' '),
                _ => text.Data,
            };
            foreach (var c in data)
            {
                column = c switch
                {
                    '\n' => 0,
                    '\t' => column + TabSize - (column % TabSize),
                    _ => column + 1,
                };
            }

            return column;

            static string CollapseWhitespaceExceptLineBreaks(string data, WhiteSpaceMode whiteSpace)
            {
                if (whiteSpace is WhiteSpaceMode.Collapse)
                    return CollapseWhitespace(data);

                return string.Join('\n', data.Split('\n').Select(CollapseWhitespace));
            }
        }

        public void SetConverted(Node node, string markdown) => _converted[node] = markdown;

        public string GetConverted(Node node) => _converted.TryGetValue(node, out var markdown) ? markdown : "";

        /// <summary>
        /// Gets the white-space mode that applies to the content of an element: the one its style declares, else the one
        /// of its parent, as the property is inherited. Each element is resolved once.
        /// </summary>
        public WhiteSpaceMode GetWhiteSpace(Element? element)
        {
            var mode = WhiteSpaceMode.Collapse;
            List<Element>? unresolved = null;
            for (var current = element; current is not null; current = current.ParentElement)
            {
                if (_whiteSpace.TryGetValue(current, out var cached))
                {
                    mode = cached;
                    break;
                }

                if (GetDeclaredWhiteSpace(current) is { } declared)
                {
                    mode = declared;
                    _whiteSpace[current] = declared;
                    break;
                }

                unresolved ??= [];
                unresolved.Add(current);
            }

            if (unresolved is not null)
            {
                foreach (var item in unresolved)
                {
                    _whiteSpace[item] = mode;
                }
            }

            return mode;
        }
    }

    /// <summary>The handling of the spaces and line breaks of a text, from the CSS white-space properties.</summary>
    private enum WhiteSpaceMode
    {
        Collapse,
        Preserve,
        PreserveBreaks,
        PreserveSpaces,
    }
}
