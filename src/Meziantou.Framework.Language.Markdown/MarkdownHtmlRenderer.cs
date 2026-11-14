using System.Buffers;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>Renders a Markdown syntax tree as HTML, as the CommonMark reference implementation does.</summary>
public static class MarkdownHtmlRenderer
{
    private static readonly SearchValues<char> EscapedCharacters = SearchValues.Create("&<>\"\0");

    // The characters a URL keeps as they are: a % is kept only before two hexadecimal digits, so it is not one of them
    private static readonly SearchValues<char> UriSafeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;/?:@&=+$,-_.!~*'()#");

    /// <summary>Renders the document as HTML.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToHtml(MarkdownSyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var writer = new Writer(tree.GetText().Length, tree.Options.Extensions.HasFlag(MarkdownExtensions.TagFilter));
        writer.WriteBlocks(tree.GetRoot().Blocks);
        return writer.ToString();
    }

    private sealed class Writer(int sourceLength, bool tagFilter)
    {
        // The HTML is about as long as the Markdown
        private readonly StringBuilder _output = new(sourceLength + 16);
        private readonly bool _tagFilter = tagFilter;

        // In a cell of a table, a pipe escaped by a backslash is a pipe, even in a code span or a link destination
        private bool _inTableCell;

        // The checkbox of a task list item, which its first paragraph starts with
        private string? _pendingCheckbox;

        public override string ToString() => _output.ToString();

        private void Cr()
        {
            if (_output.Length > 0 && _output[^1] != '\n')
            {
                _output.Append('\n');
            }
        }

        public void WriteBlocks(IReadOnlyList<MarkdownBlockSyntax> blocks)
        {
            foreach (var block in blocks)
            {
                WriteBlock(block);
            }
        }

        private void WriteBlock(MarkdownBlockSyntax block)
        {
            switch (block)
            {
                case MarkdownParagraphSyntax paragraph:
                {
                    // The paragraphs of a tight list have no <p>
                    var tight = paragraph.Parent is MarkdownListItemSyntax { Parent: MarkdownListSyntax { IsTight: true } };
                    if (!tight)
                    {
                        Cr();
                        _output.Append("<p>");
                    }

                    if (_pendingCheckbox is not null)
                    {
                        _output.Append(_pendingCheckbox);
                        _pendingCheckbox = null;
                    }

                    WriteInlines(paragraph.Inlines);
                    if (!tight)
                    {
                        _output.Append("</p>");
                        Cr();
                    }

                    break;
                }

                case MarkdownHeadingSyntax heading:
                    Cr();
                    _output.Append("<h").Append(heading.Level).Append('>');
                    WriteInlines(heading.Inlines);
                    _output.Append("</h").Append(heading.Level).Append('>');
                    Cr();
                    break;

                case MarkdownThematicBreakSyntax:
                    Cr();
                    _output.Append("<hr />");
                    Cr();
                    break;

                case MarkdownBlockQuoteSyntax quote:
                    Cr();
                    _output.Append("<blockquote>");
                    Cr();
                    WriteBlocks(quote.Blocks);
                    Cr();
                    _output.Append("</blockquote>");
                    Cr();
                    break;

                case MarkdownListSyntax list:
                    Cr();
                    if (list.StartNumber is { } start)
                    {
                        _output.Append(start == 1 ? "<ol>" : "<ol start=\"" + start.ToString(CultureInfo.InvariantCulture) + "\">");
                    }
                    else
                    {
                        _output.Append("<ul>");
                    }

                    Cr();
                    foreach (var item in list.Items)
                    {
                        WriteListItem(item);
                    }

                    Cr();
                    _output.Append(list.IsOrdered ? "</ol>" : "</ul>");
                    Cr();
                    break;

                case MarkdownCodeBlockSyntax code:
                    Cr();
                    _output.Append("<pre><code");
                    if (code.Language is { } language)
                    {
                        _output.Append(" class=\"language-");
                        AppendEscaped(language);
                        _output.Append('"');
                    }

                    _output.Append('>');
                    AppendEscaped(code.Code);
                    _output.Append("</code></pre>");
                    Cr();
                    break;

                case MarkdownHtmlBlockSyntax html:
                    Cr();
                    AppendHtml(html.Html, followedByLineBreak: false);
                    Cr();
                    break;

                case MarkdownTableSyntax table:
                    WriteTable(table);
                    break;
            }
        }

        private void WriteListItem(MarkdownListItemSyntax item)
        {
            _output.Append("<li>");
            if (item.IsTask)
            {
                // The checkbox starts the first paragraph, or the item when it starts with another block
                var checkbox = item.IsChecked ? "<input checked=\"\" disabled=\"\" type=\"checkbox\"> " : "<input disabled=\"\" type=\"checkbox\"> ";
                if (item.Blocks.Count > 0 && item.Blocks[0] is MarkdownParagraphSyntax)
                {
                    _pendingCheckbox = checkbox;
                }
                else
                {
                    _output.Append(checkbox);
                }
            }

            WriteBlocks(item.Blocks);
            _output.Append("</li>");
            Cr();
        }

        // A table: the cells of the header row, then the cells of the other rows, as many as the header row has
        private void WriteTable(MarkdownTableSyntax table)
        {
            var alignments = table.Alignments.Select(alignment => alignment switch
            {
                MarkdownTableAlignment.Center => " align=\"center\"",
                MarkdownTableAlignment.Left => " align=\"left\"",
                MarkdownTableAlignment.Right => " align=\"right\"",
                _ => "",
            }).ToList();

            Cr();
            _output.Append("<table>\n<thead>\n");
            WriteTableRow(table.HeaderRow, "th", alignments);
            _output.Append("</thead>\n");
            var rows = table.Rows;
            if (rows.Count > 0)
            {
                _output.Append("<tbody>\n");
                foreach (var row in rows)
                {
                    WriteTableRow(row, "td", alignments);
                }

                _output.Append("</tbody>\n");
            }

            _output.Append("</table>\n");
        }

        private void WriteTableRow(MarkdownTableRowSyntax row, string tag, List<string> alignments)
        {
            _output.Append("<tr>\n");
            var cells = row.Cells;
            for (var i = 0; i < alignments.Count; i++)
            {
                _output.Append('<').Append(tag).Append(alignments[i]).Append('>');
                if (i < cells.Count)
                {
                    _inTableCell = true;
                    WriteInlines(cells[i].Inlines);
                    _inTableCell = false;
                }

                _output.Append("</").Append(tag).Append(">\n");
            }

            _output.Append("</tr>\n");
        }

        private void WriteInlines(IEnumerable<SyntaxNodeOrToken> inlines)
        {
            foreach (var child in inlines)
            {
                if (child.AsNode() is MarkdownInlineSyntax inline)
                {
                    WriteInline(inline);
                }
                else
                {
                    WriteToken(child.AsToken());
                }
            }
        }

        private string GetText(SyntaxToken token) => _inTableCell ? token.Text.Replace("\\|", "|", StringComparison.Ordinal) : token.Text;

        private void WriteToken(SyntaxToken token)
        {
            switch ((SyntaxKind)token.RawKind)
            {
                case SyntaxKind.TextToken or SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken or SyntaxKind.CloseBracketToken or SyntaxKind.ExtendedAutolinkToken:
                    AppendEscaped(GetText(token));
                    break;
                case SyntaxKind.EscapedCharacterToken:
                    AppendEscaped(GetText(token).AsSpan(1));
                    break;
                case SyntaxKind.EntityToken:
                    AppendEscaped(HtmlEntities.Decode(token.Text));
                    break;
                case SyntaxKind.LineEndingToken:
                    _output.Append('\n');
                    break;
                case SyntaxKind.HardLineBreakToken:
                    _output.Append("<br />\n");
                    break;
            }
        }

        private void WriteInline(MarkdownInlineSyntax inline)
        {
            switch (inline)
            {
                case MarkdownEmphasisSyntax emphasis:
                {
                    var tag = emphasis.IsStrong ? "strong" : "em";
                    _output.Append('<').Append(tag).Append('>');
                    WriteInlines(emphasis.Inlines);
                    _output.Append("</").Append(tag).Append('>');
                    break;
                }

                case MarkdownStrikethroughSyntax strikethrough:
                    _output.Append("<del>");
                    WriteInlines(strikethrough.Inlines);
                    _output.Append("</del>");
                    break;

                case MarkdownCodeSpanSyntax code:
                    _output.Append("<code>");
                    AppendEscaped(code.Code);
                    _output.Append("</code>");
                    break;

                case MarkdownAutolinkSyntax autolink:
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(autolink.Destination));
                    _output.Append("\">");
                    AppendEscaped(autolink.Text);
                    _output.Append("</a>");
                    break;

                case MarkdownRawHtmlSyntax html:
                    AppendHtml(html.Html, followedByLineBreak: false);
                    break;

                case MarkdownLinkSyntax { IsImage: true } image:
                    _output.Append("<img src=\"");
                    AppendEscaped(NormalizeUri(image.Destination));
                    _output.Append("\" alt=\"");
                    AppendEscaped(image.GetPlainText());
                    _output.Append('"');
                    AppendTitle(image.Title);
                    _output.Append(" />");
                    break;

                case MarkdownLinkSyntax link:
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(link.Destination));
                    _output.Append('"');
                    AppendTitle(link.Title);
                    _output.Append('>');
                    WriteInlines(link.Text);
                    _output.Append("</a>");
                    break;
            }
        }

        private void AppendTitle(string? title)
        {
            if (!string.IsNullOrEmpty(title))
            {
                _output.Append(" title=\"");
                AppendEscaped(title);
                _output.Append('"');
            }
        }

        // Percent-encodes the characters that cannot be in a URL, as the reference implementation does
        private static string NormalizeUri(string uri)
        {
            if (!uri.AsSpan().ContainsAnyExcept(UriSafeCharacters))
            {
                return uri;
            }

            var builder = new StringBuilder(uri.Length);
            Span<byte> bytes = stackalloc byte[4];
            for (var i = 0; i < uri.Length; i++)
            {
                var c = uri[i];
                if (c == '%' && i + 2 < uri.Length && char.IsAsciiHexDigit(uri[i + 1]) && char.IsAsciiHexDigit(uri[i + 2]))
                {
                    builder.Append(c);
                }
                else if (char.IsAsciiLetterOrDigit(c) || ";/?:@&=+$,-_.!~*'()#".Contains(c, StringComparison.Ordinal))
                {
                    builder.Append(c);
                }
                else
                {
                    // A lone surrogate is encoded as the replacement character
                    if (Rune.DecodeFromUtf16(uri.AsSpan(i), out var rune, out var consumed) != System.Buffers.OperationStatus.Done)
                    {
                        rune = Rune.ReplacementChar;
                        consumed = 1;
                    }

                    var length = rune.EncodeToUtf8(bytes);
                    for (var j = 0; j < length; j++)
                    {
                        builder.Append('%').Append(bytes[j].ToString("X2", CultureInfo.InvariantCulture));
                    }

                    i += consumed - 1;
                }
            }

            return builder.ToString();
        }

        // Raw HTML, where the tag filter of GFM escapes the tags that change how the rest of the page is read
        private void AppendHtml(ReadOnlySpan<char> html, bool followedByLineBreak)
        {
            while (!html.IsEmpty)
            {
                var index = html.IndexOfAny(_tagFilter ? "<\0" : "\0");
                if (index < 0)
                {
                    _output.Append(html);
                    return;
                }

                _output.Append(html[..index]);
                if (html[index] == '\0')
                {
                    _output.Append('\uFFFD');
                }
                else
                {
                    _output.Append(IsDisallowedTag(html[index..], followedByLineBreak) ? "&lt;" : "<");
                }

                html = html[(index + 1)..];
            }
        }

        // A line break after the text of a line of an HTML block ends the tag name as whitespace does
        private static bool IsDisallowedTag(ReadOnlySpan<char> tag, bool followedByLineBreak)
        {
            var name = tag[1..];
            if (name.StartsWith('/'))
            {
                name = name[1..];
            }

            foreach (var disallowed in (ReadOnlySpan<string>)["title", "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "plaintext"])
            {
                if (name.Length == disallowed.Length && followedByLineBreak && name.Equals(disallowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (name.Length > disallowed.Length && name.StartsWith(disallowed, StringComparison.OrdinalIgnoreCase))
                {
                    var next = name[disallowed.Length..];
                    if (next[0] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' or '>' || next.StartsWith("/>", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void AppendEscaped(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                var index = text.IndexOfAny(EscapedCharacters);
                if (index < 0)
                {
                    _output.Append(text);
                    return;
                }

                _output.Append(text[..index]);
                _output.Append(text[index] switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => "\uFFFD",
                });
                text = text[(index + 1)..];
            }
        }
    }
}
