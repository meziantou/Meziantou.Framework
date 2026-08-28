using System.Globalization;
using System.Text;

namespace Meziantou.Framework.AtlassianDataFormat;

internal sealed class MarkdownConverter(AdfToMarkdownOptions options)
{
    public string Convert(AdfDocument document) => ConvertBlocks(document.Content).Trim('\n');

    // =========================================================================
    // Blocks
    // =========================================================================

    private string ConvertBlocks(IReadOnlyList<AdfNode> nodes)
    {
        var blocks = new List<string>(nodes.Count);
        foreach (var node in nodes)
        {
            blocks.Add(ConvertBlock(node));
        }

        return MarkdownHelper.JoinBlocks(blocks);
    }

    private string ConvertBlock(AdfNode node)
    {
        return node switch
        {
            AdfParagraph paragraph => ConvertInlines(paragraph.Content),
            AdfHeading heading => ConvertHeading(heading),
            AdfBlockquote blockquote => Quote(ConvertBlocks(blockquote.Content)),
            AdfCodeBlock codeBlock => ConvertCodeBlock(codeBlock),
            AdfRule => options.ThematicBreak,
            AdfBulletList bulletList => ConvertBulletList(bulletList),
            AdfOrderedList orderedList => ConvertOrderedList(orderedList),
            AdfListItem listItem => ConvertBlocks(listItem.Content),
            AdfPanel panel => ConvertPanel(panel),
            AdfTable table => ConvertTable(table),
            AdfExpand expand => ConvertExpand(expand.Title, expand.Content),
            AdfNestedExpand expand => ConvertExpand(expand.Title, expand.Content),
            AdfTaskList taskList => ConvertTaskList(taskList),
            AdfDecisionList decisionList => ConvertDecisionList(decisionList),
            AdfMediaGroup mediaGroup => MarkdownHelper.JoinBlocks(mediaGroup.Content.Select(ConvertBlock)),
            AdfMediaSingle mediaSingle => ConvertMediaSingle(mediaSingle),
            AdfMedia media => ConvertMedia(media, media.Marks),
            AdfCaption caption => ConvertInlines(caption.Content),
            AdfLayoutSection layout => ConvertBlocks(layout.Content),
            AdfLayoutColumn column => ConvertBlocks(column.Content),
            AdfBlockCard card => ConvertCard(card.Url, card.Data),
            AdfEmbedCard card => ConvertCard(card.Url, data: null),
            AdfExtension extension => extension.Text is { Length: > 0 } text ? MarkdownHelper.Escape(text) : "",
            AdfBodiedExtension extension => ConvertBlocks(extension.Content),
            AdfUnknownNode unknown => ConvertUnknown(unknown),
            _ => ConvertInlines([node]),
        };
    }

    private string ConvertHeading(AdfHeading heading)
    {
        var text = ConvertInlines(heading.Content);
        var level = Math.Clamp(heading.Level, 1, 6);

        // Setext headings can only represent the first two levels.
        if (options.HeadingStyle is AdfHeadingStyle.Setext && level <= 2 && text.Length > 0 && !text.Contains('\n', StringComparison.Ordinal))
        {
            var underline = new string(level is 1 ? '=' : '-', Math.Max(3, text.Length));
            return text + "\n" + underline;
        }

        return text.Length == 0 ? new string('#', level) : new string('#', level) + " " + text;
    }

    private string ConvertCodeBlock(AdfCodeBlock codeBlock)
    {
        var sb = new StringBuilder();
        foreach (var child in codeBlock.Content)
        {
            if (child is AdfText text)
            {
                sb.Append(text.Text);
            }
        }

        var code = sb.ToString();

        // The closing fence goes on its own line, so a trailing newline in the content would emit a
        // blank line before it.
        if (code.Length > 0 && code[^1] is '\n')
        {
            code = code[..^1];
        }

        if (options.CodeBlockStyle is AdfCodeBlockStyle.Indented)
            return MarkdownHelper.PrefixLines(code, "    ", "    ", "");

        var fence = MarkdownHelper.CreateFence(code, options.CodeBlockFenceCharacter, minimumLength: 3);
        return fence + codeBlock.Language + "\n" + code + "\n" + fence;
    }

    private string ConvertBulletList(AdfBulletList list)
    {
        var marker = options.UnorderedListMarker + " ";
        var indent = new string(' ', marker.Length);
        var items = new List<string>();
        foreach (var item in list.Content)
        {
            var content = ConvertBlock(item);
            if (content.Length == 0)
            {
                content = "";
            }

            items.Add(MarkdownHelper.PrefixLines(content, marker, indent, ""));
        }

        return string.Join('\n', items);
    }

    private string ConvertOrderedList(AdfOrderedList list)
    {
        var number = list.Order ?? 1;
        var items = new List<string>();
        foreach (var item in list.Content)
        {
            var marker = number.ToString(CultureInfo.InvariantCulture) + ". ";
            var indent = new string(' ', marker.Length);
            items.Add(MarkdownHelper.PrefixLines(ConvertBlock(item), marker, indent, ""));
            number++;
        }

        return string.Join('\n', items);
    }

    private string ConvertPanel(AdfPanel panel)
    {
        var content = ConvertBlocks(panel.Content);
        switch (options.PanelStyle)
        {
            case AdfPanelStyle.PlainText:
                return Quote(content);

            case AdfPanelStyle.Html:
                return $"<div data-panel-type=\"{GetPanelTypeName(panel.PanelType)}\">\n\n{content}\n\n</div>";

            case AdfPanelStyle.GitHubAlert when GetGitHubAlert(panel.PanelType) is { } alert:
                // The content must follow the marker directly: a blank quote line in between stops
                // GitHub from rendering the alert.
                return Quote($"[!{alert}]\n{content}");

            default:
                var marker = GetPanelEmoji(panel.PanelType);
                if (marker is null)
                    return Quote(content);

                // Merging the marker into the first line only works when that line is a paragraph;
                // prefixing a list item or a heading with it would change how it parses.
                return panel.Content is [AdfParagraph, ..]
                    ? Quote(marker + " " + content)
                    : Quote(marker + "\n\n" + content);
        }
    }

    private string ConvertExpand(string? title, IReadOnlyList<AdfNode> children)
    {
        var content = ConvertBlocks(children);
        var escapedTitle = title is { Length: > 0 } ? MarkdownHelper.Escape(title) : null;

        switch (options.ExpandStyle)
        {
            case AdfExpandStyle.HtmlDetails:
                var summary = escapedTitle is null ? "<summary></summary>" : $"<summary>{title}</summary>";

                // The blank line after <summary> is required for the body to be parsed as Markdown.
                return $"<details>\n{summary}\n\n{content}\n\n</details>";

            case AdfExpandStyle.Heading:
                return escapedTitle is null ? content : MarkdownHelper.JoinBlocks(["### " + escapedTitle, content]);

            default:
                var body = escapedTitle is null
                    ? content
                    : MarkdownHelper.JoinBlocks([Bold(escapedTitle), content]);
                return Quote(body);
        }
    }

    private string ConvertTaskList(AdfTaskList list)
    {
        var lines = new List<string>();
        foreach (var child in list.Content)
        {
            switch (child)
            {
                case AdfTaskItem item:
                    var marker = options.TaskListStyle is AdfTaskListStyle.Checkbox
                        ? (item.State is AdfTaskState.Done ? "- [x] " : "- [ ] ")
                        : "- ";
                    lines.Add(MarkdownHelper.PrefixLines(ConvertItemContent(item.Content), marker, new string(' ', marker.Length), ""));
                    break;

                case AdfTaskList nested:
                    lines.Add(MarkdownHelper.PrefixLines(ConvertTaskList(nested), "  ", "  ", ""));
                    break;

                default:
                    lines.Add(ConvertBlock(child));
                    break;
            }
        }

        return string.Join('\n', lines.Where(l => l.Length > 0));
    }

    private string ConvertDecisionList(AdfDecisionList list)
    {
        var items = new List<string>();
        foreach (var child in list.Content)
        {
            var content = child is AdfDecisionItem item ? ConvertItemContent(item.Content) : ConvertBlock(child);
            if (content.Length == 0)
                continue;

            items.Add(options.DecisionListStyle is AdfDecisionListStyle.BulletList
                ? MarkdownHelper.PrefixLines(content, "- ", "  ", "")
                : content);
        }

        return options.DecisionListStyle is AdfDecisionListStyle.BulletList
            ? string.Join('\n', items)
            : MarkdownHelper.JoinBlocks(items);
    }

    /// <summary>
    /// Converts the content of a task or decision item. The schema says it holds inline nodes, but
    /// documents returned by the APIs wrap it in paragraphs.
    /// </summary>
    private string ConvertItemContent(IReadOnlyList<AdfNode> content)
    {
        foreach (var child in content)
        {
            if (!IsInline(child))
                return ConvertBlocks(content);
        }

        return ConvertInlines(content);
    }

    private static bool IsInline(AdfNode node) => node.Kind is AdfNodeKind.Text or AdfNodeKind.HardBreak
        or AdfNodeKind.Emoji or AdfNodeKind.Mention or AdfNodeKind.Date or AdfNodeKind.Status
        or AdfNodeKind.InlineCard or AdfNodeKind.InlineExtension or AdfNodeKind.Placeholder
        or AdfNodeKind.MediaInline;

    private string ConvertMediaSingle(AdfMediaSingle mediaSingle)
    {
        var blocks = new List<string>();
        foreach (var child in mediaSingle.Content)
        {
            switch (child)
            {
                case AdfMedia media:
                    // A link on the mediaSingle applies to the media it wraps.
                    var marks = media.Marks.Count > 0 ? media.Marks : mediaSingle.Marks;
                    blocks.Add(ConvertMedia(media, marks));
                    break;

                case AdfCaption caption:
                    var text = ConvertInlines(caption.Content);
                    if (text.Length > 0)
                    {
                        blocks.Add(Italic(text));
                    }

                    break;

                default:
                    blocks.Add(ConvertBlock(child));
                    break;
            }
        }

        return MarkdownHelper.JoinBlocks(blocks);
    }

    private string ConvertMedia(AdfMedia media, IReadOnlyList<AdfMark> marks)
    {
        if (options.MediaRendering is AdfMediaRendering.Skip)
            return "";

        var url = options.MediaUrlResolver?.Invoke(media) ?? media.Url;
        var alt = MarkdownHelper.Escape(media.Alt ?? "");

        string result;
        if (options.MediaRendering is AdfMediaRendering.AltText || url is null or { Length: 0 })
        {
            result = alt;
        }
        else if (options.MediaRendering is AdfMediaRendering.Link || media.Type is AdfMediaType.Link)
        {
            result = MarkdownHelper.Link(alt.Length > 0 ? alt : MarkdownHelper.Escape(url), url, title: null);
        }
        else
        {
            result = "!" + MarkdownHelper.Link(alt, url, title: null);
        }

        if (result.Length > 0 && FindLink(marks) is { } link)
        {
            result = MarkdownHelper.Link(result, link.Href, link.Title);
        }

        return result;
    }

    private static string ConvertCard(string? url, System.Text.Json.JsonElement? data)
    {
        var name = TryGetString(data, "name");
        url ??= TryGetString(data, "url");

        if (url is null or { Length: 0 })
            return name is null ? "" : MarkdownHelper.Escape(name);

        return name is { Length: > 0 }
            ? MarkdownHelper.Link(MarkdownHelper.Escape(name), url, title: null)
            : "<" + url + ">";
    }

    private string ConvertUnknown(AdfUnknownNode node)
    {
        return options.UnknownNodeHandling switch
        {
            AdfUnknownNodeHandling.KeepContent => ConvertBlocks(node.Content),
            AdfUnknownNodeHandling.Throw => throw new AdfException($"The node type '{node.TypeName}' is not supported"),
            _ => "",
        };
    }

    // =========================================================================
    // Tables
    // =========================================================================

    private string ConvertTable(AdfTable table)
    {
        var rows = table.Content.OfType<AdfTableRow>().ToList();
        if (rows.Count == 0)
            return "";

        var style = options.TableStyle;
        if (style is AdfTableStyle.Auto)
        {
            style = CanUsePipeTable(rows) ? AdfTableStyle.PipeTable : AdfTableStyle.Html;
        }

        return style is AdfTableStyle.Html ? ConvertHtmlTable(rows) : ConvertPipeTable(rows);
    }

    private static bool CanUsePipeTable(List<AdfTableRow> rows)
    {
        foreach (var row in rows)
        {
            foreach (var cell in row.Content)
            {
                if (GetSpan(cell, "col") > 1 || GetSpan(cell, "row") > 1)
                    return false;

                // More than one block, or anything that is not a plain paragraph, cannot be
                // represented in a pipe table cell without flattening it.
                if (cell.Content.Count > 1 || cell.Content.Any(c => c is not AdfParagraph))
                    return false;
            }
        }

        return true;
    }

    private static int GetSpan(AdfNode cell, string kind) => cell switch
    {
        AdfTableCell c => (kind is "col" ? c.ColSpan : c.RowSpan) ?? 1,
        AdfTableHeader h => (kind is "col" ? h.ColSpan : h.RowSpan) ?? 1,
        _ => 1,
    };

    private string ConvertPipeTable(List<AdfTableRow> rows)
    {
        var cells = rows.Select(r => r.Content.Select(c => MarkdownHelper.Flatten(ConvertBlocks(c.Content))).ToList()).ToList();
        var columnCount = cells.Max(r => r.Count);

        var sb = new StringBuilder();

        // GitHub pipe tables require a header row. When the document does not mark the first row as
        // a header, an empty one is emitted so no data is lost.
        var hasHeader = rows[0].Content.Count > 0 && rows[0].Content.All(c => c is AdfTableHeader);
        var firstBodyRow = 0;
        if (hasHeader)
        {
            AppendRow(sb, cells[0], columnCount);
            firstBodyRow = 1;
        }
        else
        {
            AppendRow(sb, [], columnCount);
        }

        sb.Append('\n');
        sb.Append('|');
        for (var i = 0; i < columnCount; i++)
        {
            sb.Append(" --- |");
        }

        for (var i = firstBodyRow; i < cells.Count; i++)
        {
            sb.Append('\n');
            AppendRow(sb, cells[i], columnCount);
        }

        return sb.ToString();

        static void AppendRow(StringBuilder sb, List<string> row, int columnCount)
        {
            sb.Append('|');
            for (var i = 0; i < columnCount; i++)
            {
                sb.Append(' ');
                sb.Append(i < row.Count ? row[i] : "");
                sb.Append(" |");
            }
        }
    }

    private string ConvertHtmlTable(List<AdfTableRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table>");
        foreach (var row in rows)
        {
            sb.Append("\n<tr>");
            foreach (var cell in row.Content)
            {
                var tag = cell is AdfTableHeader ? "th" : "td";
                sb.Append("\n<").Append(tag);

                var colspan = GetSpan(cell, "col");
                if (colspan > 1)
                {
                    sb.Append(" colspan=\"").Append(colspan.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                var rowspan = GetSpan(cell, "row");
                if (rowspan > 1)
                {
                    sb.Append(" rowspan=\"").Append(rowspan.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                sb.Append(">\n\n").Append(ConvertBlocks(cell.Content)).Append("\n\n</").Append(tag).Append('>');
            }

            sb.Append("\n</tr>");
        }

        sb.Append("\n</table>");
        return sb.ToString();
    }

    // =========================================================================
    // Inlines
    // =========================================================================

    private string ConvertInlines(IReadOnlyList<AdfNode> nodes)
    {
        // Consecutive nodes carrying the same marks are merged so a single run of bold text does not
        // become several adjacent emphasis spans.
        // A hard break at the very end of a block has nothing to break to.
        var count = nodes.Count;
        while (count > 0 && nodes[count - 1] is AdfHardBreak)
        {
            count--;
        }

        var runs = new List<(StringBuilder Text, IReadOnlyList<AdfMark> Marks)>();
        for (var i = 0; i < count; i++)
        {
            var node = nodes[i];
            var content = ConvertInlineContent(node);
            if (content.Length == 0)
                continue;

            if (runs.Count > 0 && MarksEqual(runs[^1].Marks, node.Marks))
            {
                runs[^1].Text.Append(content);
            }
            else
            {
                runs.Add((new StringBuilder(content), node.Marks));
            }
        }

        var sb = new StringBuilder();
        foreach (var (text, marks) in runs)
        {
            sb.Append(ApplyMarks(text.ToString(), marks));
        }

        // Trailing whitespace is meaningless at the end of a block, and two trailing spaces would
        // be read as a hard break.
        return sb.ToString().TrimEnd();
    }

    private string ConvertInlineContent(AdfNode node)
    {
        return node switch
        {
            // Text carrying a code mark is emitted verbatim: escaping does not apply inside a code span.
            AdfText text => HasCodeMark(text.Marks) ? text.Text : MarkdownHelper.Escape(text.Text),
            AdfHardBreak => options.LineBreakStyle is AdfLineBreakStyle.Backslash ? "\\\n" : "  \n",
            AdfEmoji emoji => ConvertEmoji(emoji),
            AdfMention mention => ConvertMention(mention),
            AdfDate date => ConvertDate(date),
            AdfStatus status => ConvertStatus(status),
            AdfInlineCard card => ConvertCard(card.Url, card.Data),
            AdfInlineExtension extension => extension.Text is { Length: > 0 } text ? MarkdownHelper.Escape(text) : "",
            AdfPlaceholder => "",
            AdfMediaInline media => ConvertMediaInline(media),
            AdfUnknownNode unknown => options.UnknownNodeHandling switch
            {
                AdfUnknownNodeHandling.KeepContent => ConvertInlines(unknown.Content),
                AdfUnknownNodeHandling.Throw => throw new AdfException($"The node type '{unknown.TypeName}' is not supported"),
                _ => "",
            },
            _ => MarkdownHelper.Flatten(ConvertBlock(node)),
        };
    }

    private string ConvertEmoji(AdfEmoji emoji)
    {
        var literal = emoji.Text is { Length: > 0 } text ? text : emoji.Fallback;
        if (options.EmojiRendering is AdfEmojiRendering.ShortName)
            return emoji.ShortName is { Length: > 0 } ? emoji.ShortName : literal ?? "";

        return literal is { Length: > 0 } ? literal : emoji.ShortName;
    }

    private string ConvertMention(AdfMention mention)
    {
        var name = options.MentionResolver?.Invoke(mention) ?? mention.Text ?? mention.Id;

        // The text stored in the document usually already carries the '@' prefix.
        name = name.TrimStart('@');

        return options.MentionFormat
            .Replace("{text}", MarkdownHelper.Escape(name), StringComparison.Ordinal)
            .Replace("{id}", MarkdownHelper.Escape(mention.Id), StringComparison.Ordinal);
    }

    private string ConvertDate(AdfDate date)
    {
        var value = date.GetDateTimeOffset();
        return value is null
            ? MarkdownHelper.Escape(date.Timestamp)
            : value.Value.ToString(options.DateFormat, CultureInfo.InvariantCulture);
    }

    private string ConvertStatus(AdfStatus status)
    {
        // The text is inserted verbatim: the default format wraps it in a code span, where escaping
        // would show the backslashes.
        return options.StatusFormat
            .Replace("{text}", status.Text, StringComparison.Ordinal)
            .Replace("{color}", status.Color ?? "", StringComparison.Ordinal);
    }

    private string ConvertMediaInline(AdfMediaInline media)
    {
        var equivalent = new AdfMedia
        {
            Type = media.Type,
            Id = media.Id,
            Collection = media.Collection,
            Alt = media.Alt,
            Width = media.Width,
            Height = media.Height,
            OccurrenceKey = media.OccurrenceKey,
            LocalId = media.LocalId,
        };

        return ConvertMedia(equivalent, marks: []);
    }

    // =========================================================================
    // Marks
    // =========================================================================

    private string ApplyMarks(string text, IReadOnlyList<AdfMark> marks)
    {
        if (marks.Count == 0)
            return text;

        // ADF stores marks as an unordered set; Markdown needs nested delimiters, so they are
        // applied in the order the schema ranks them, the first one ending up outermost.
        var ordered = marks.OrderBy(GetMarkRank).ToList();
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            text = ApplyMark(text, ordered[i]);
        }

        return text;
    }

    private string ApplyMark(string text, AdfMark mark)
    {
        return mark switch
        {
            AdfCodeMark => MarkdownHelper.CodeSpan(text),
            AdfStrongMark => Bold(text),
            AdfEmphasisMark => Italic(text),
            AdfStrikeMark => MarkdownHelper.Delimit(text, "~~"),
            AdfSubSupMark subsup => subsup.Type is AdfSubSupType.Subscript
                ? MarkdownHelper.Delimit(text, "<sub>", "</sub>")
                : MarkdownHelper.Delimit(text, "<sup>", "</sup>"),
            AdfLinkMark link => MarkdownHelper.Link(text, link.Href, link.Title),

            // Underline, colors, annotations, borders, alignment, indentation and breakout have no
            // Markdown equivalent and are dropped.
            _ => text,
        };
    }

    private static int GetMarkRank(AdfMark mark) => mark.Kind switch
    {
        AdfMarkKind.Link => 0,
        AdfMarkKind.Emphasis => 1,
        AdfMarkKind.Strong => 2,
        AdfMarkKind.Strike => 3,
        AdfMarkKind.SubSup => 4,
        AdfMarkKind.Underline => 5,
        AdfMarkKind.TextColor => 6,
        AdfMarkKind.Annotation => 7,
        AdfMarkKind.BackgroundColor => 8,
        AdfMarkKind.Code => 9,
        _ => 10,
    };

    private static bool HasCodeMark(IReadOnlyList<AdfMark> marks)
    {
        foreach (var mark in marks)
        {
            if (mark is AdfCodeMark)
                return true;
        }

        return false;
    }

    private static AdfLinkMark? FindLink(IReadOnlyList<AdfMark> marks)
    {
        foreach (var mark in marks)
        {
            if (mark is AdfLinkMark link)
                return link;
        }

        return null;
    }

    private static bool MarksEqual(IReadOnlyList<AdfMark> left, IReadOnlyList<AdfMark> right)
    {
        if (left.Count != right.Count)
            return false;

        if (left.Count == 0)
            return true;

        var leftKeys = left.Select(GetMarkKey).Order(StringComparer.Ordinal);
        var rightKeys = right.Select(GetMarkKey).Order(StringComparer.Ordinal);
        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }

    private static string GetMarkKey(AdfMark mark) => mark switch
    {
        AdfLinkMark link => $"link|{link.Href}|{link.Title}",
        AdfSubSupMark subsup => $"subsup|{subsup.Type}",
        AdfTextColorMark color => $"textColor|{color.Color}",
        AdfBackgroundColorMark color => $"backgroundColor|{color.Color}",
        AdfAnnotationMark annotation => $"annotation|{annotation.Id}",
        AdfUnknownMark unknown => $"unknown|{unknown.TypeName}",
        _ => mark.Kind.ToString(),
    };

    // =========================================================================
    // Helpers
    // =========================================================================

    private string Bold(string text) => MarkdownHelper.Delimit(text, options.EmphasisMarker is AdfEmphasisMarker.Underscore ? "__" : "**");

    private string Italic(string text) => MarkdownHelper.Delimit(text, options.EmphasisMarker is AdfEmphasisMarker.Underscore ? "_" : "*");

    private static string Quote(string content)
        => content.Length == 0 ? "" : MarkdownHelper.PrefixLines(content, "> ", "> ", ">");

    private static string? TryGetString(System.Text.Json.JsonElement? element, string name)
    {
        if (element is { } value && value.ValueKind is System.Text.Json.JsonValueKind.Object
            && value.TryGetProperty(name, out var property)
            && property.ValueKind is System.Text.Json.JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static string GetPanelTypeName(AdfPanelType type) => type switch
    {
        AdfPanelType.Info => "info",
        AdfPanelType.Note => "note",
        AdfPanelType.Tip => "tip",
        AdfPanelType.Warning => "warning",
        AdfPanelType.Error => "error",
        AdfPanelType.Success => "success",
        AdfPanelType.Custom => "custom",
        _ => "info",
    };

    private static string? GetPanelEmoji(AdfPanelType type) => type switch
    {
        AdfPanelType.Info => "ℹ️",
        AdfPanelType.Note => "📝",
        AdfPanelType.Tip => "💡",
        AdfPanelType.Warning => "⚠️",
        AdfPanelType.Error => "❌",
        AdfPanelType.Success => "✅",
        _ => null,
    };

    private static string? GetGitHubAlert(AdfPanelType type) => type switch
    {
        AdfPanelType.Info or AdfPanelType.Note => "NOTE",
        AdfPanelType.Tip or AdfPanelType.Success => "TIP",
        AdfPanelType.Warning => "WARNING",
        AdfPanelType.Error => "CAUTION",
        _ => null,
    };
}
