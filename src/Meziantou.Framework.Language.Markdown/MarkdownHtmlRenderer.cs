using System.Buffers;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>Renders a Markdown syntax tree as HTML, as the CommonMark reference implementation does.</summary>
public static class MarkdownHtmlRenderer
{
    // The titles of the alerts of GitHub, with their icons (Octicons, MIT license), as Meziantou.Framework.Markdown 1.x writes them
    private static readonly Dictionary<string, string> AlertTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z\"></path></svg>Note</p>",
        ["TIP"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M8 1.5c-2.363 0-4 1.69-4 3.75 0 .984.424 1.625.984 2.304l.214.253c.223.264.47.556.673.848.284.411.537.896.621 1.49a.75.75 0 0 1-1.484.211c-.04-.282-.163-.547-.37-.847a8.456 8.456 0 0 0-.542-.68c-.084-.1-.173-.205-.268-.32C3.201 7.75 2.5 6.766 2.5 5.25 2.5 2.31 4.863 0 8 0s5.5 2.31 5.5 5.25c0 1.516-.701 2.5-1.328 3.259-.095.115-.184.22-.268.319-.207.245-.383.453-.541.681-.208.3-.33.565-.37.847a.751.751 0 0 1-1.485-.212c.084-.593.337-1.078.621-1.489.203-.292.45-.584.673-.848.075-.088.147-.173.213-.253.561-.679.985-1.32.985-2.304 0-2.06-1.637-3.75-4-3.75ZM5.75 12h4.5a.75.75 0 0 1 0 1.5h-4.5a.75.75 0 0 1 0-1.5ZM6 15.25a.75.75 0 0 1 .75-.75h2.5a.75.75 0 0 1 0 1.5h-2.5a.75.75 0 0 1-.75-.75Z\"></path></svg>Tip</p>",
        ["IMPORTANT"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M0 1.75C0 .784.784 0 1.75 0h12.5C15.216 0 16 .784 16 1.75v9.5A1.75 1.75 0 0 1 14.25 13H8.06l-2.573 2.573A1.458 1.458 0 0 1 3 14.543V13H1.75A1.75 1.75 0 0 1 0 11.25Zm1.75-.25a.25.25 0 0 0-.25.25v9.5c0 .138.112.25.25.25h2a.75.75 0 0 1 .75.75v2.19l2.72-2.72a.749.749 0 0 1 .53-.22h6.5a.25.25 0 0 0 .25-.25v-9.5a.25.25 0 0 0-.25-.25Zm7 2.25v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 9a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z\"></path></svg>Important</p>",
        ["WARNING"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M6.457 1.047c.659-1.234 2.427-1.234 3.086 0l6.082 11.378A1.75 1.75 0 0 1 14.082 15H1.918a1.75 1.75 0 0 1-1.543-2.575Zm1.763.707a.25.25 0 0 0-.44 0L1.698 13.132a.25.25 0 0 0 .22.368h12.164a.25.25 0 0 0 .22-.368Zm.53 3.996v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 11a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z\"></path></svg>Warning</p>",
        ["CAUTION"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M4.47.22A.749.749 0 0 1 5 0h6c.199 0 .389.079.53.22l4.25 4.25c.141.14.22.331.22.53v6a.749.749 0 0 1-.22.53l-4.25 4.25A.749.749 0 0 1 11 16H5a.749.749 0 0 1-.53-.22L.22 11.53A.749.749 0 0 1 0 11V5c0-.199.079-.389.22-.53Zm.84 1.28L1.5 5.31v5.38l3.81 3.81h5.38l3.81-3.81V5.31L10.69 1.5ZM8 4a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 8 4Zm0 8a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z\"></path></svg>Caution</p>",
    };

    private static readonly SearchValues<char> EscapedCharacters = SearchValues.Create("&<>\"\0");

    // The characters a URL keeps as they are: a % is kept only before two hexadecimal digits, so it is not one of them
    private static readonly SearchValues<char> UriSafeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;/?:@&=+$,-_.!~*'()#");

    /// <summary>Renders the document as HTML.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToHtml(MarkdownSyntaxTree tree) => ToHtml(tree, options: null);

    /// <summary>Renders the document as HTML.</summary>
    /// <param name="tree">The document.</param>
    /// <param name="options">How to write the HTML, or <see langword="null"/> for <see cref="MarkdownHtmlRendererOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToHtml(MarkdownSyntaxTree tree, MarkdownHtmlRendererOptions? options)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var writer = new Writer(tree.GetText().Length, tree.Options, options ?? MarkdownHtmlRendererOptions.Default);
        if (tree.Options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes))
        {
            writer.CollectAttributes(tree.GetRoot());
        }

        writer.WriteBlocks(tree.GetRoot().Blocks);
        if (tree.Options.Extensions.HasFlag(MarkdownExtensions.Footnotes))
        {
            writer.WriteFootnotes(tree.GetRoot());
        }

        return writer.ToString();
    }

    private sealed class Writer(int sourceLength, MarkdownParseOptions options, MarkdownHtmlRendererOptions rendererOptions)
    {
        // The generic attributes of the nodes, merged as Markdig merges them, and the classes of the attributes of the first line of a
        // block, which come before the classes of the block
        private Dictionary<SyntaxNode, ParsedAttributes>? _attributes;
        private Dictionary<SyntaxNode, List<string>>? _blockLineClasses;
        private Dictionary<MarkdownParagraphSyntax, MarkdownBlockSyntax>? _nextBlocks;

        // The HTML is about as long as the Markdown
        private readonly StringBuilder _output = new(sourceLength + 16);
        private readonly bool _tagFilter = options.Extensions.HasFlag(MarkdownExtensions.TagFilter);
        private readonly bool _hardLineBreaks = options.Extensions.HasFlag(MarkdownExtensions.HardLineBreaks);
        private readonly bool _diagrams = options.Extensions.HasFlag(MarkdownExtensions.Diagrams);

        // In a cell of a table, a pipe escaped by a backslash is a pipe, even in a code span or a link destination
        private bool _inTableCell;

        // The checkbox of a task list item, which its first paragraph starts with
        private string? _pendingCheckbox;

        // The footnotes and their numbers, and the links back to the references of a footnote, which its last paragraph ends with
        private MarkdownFootnotes? _footnotes;
        private MarkdownParagraphSyntax? _backLinksParagraph;
        private string? _backLinks;

        public override string ToString() => _output.ToString();

        // The id of the last attributes wins, their classes are added, and a property is only added when there is none with its name
        public void CollectAttributes(MarkdownDocumentSyntax document)
        {
            _attributes = [];
            _nextBlocks = [];

            // The block after each paragraph of attributes only, which its last attributes are for
            foreach (var container in document.DescendantNodesAndSelf(MarkdownDocumentSyntax.IsBlockContainer))
            {
                MarkdownParagraphSyntax? attributesParagraph = null;
                foreach (var child in container.ChildNodes())
                {
                    if (child is not MarkdownBlockSyntax block)
                    {
                        continue;
                    }

                    if (attributesParagraph is not null)
                    {
                        _nextBlocks[attributesParagraph] = block;
                    }

                    attributesParagraph = block is MarkdownParagraphSyntax paragraph && IsAttributesOnly(paragraph) ? paragraph : null;
                }
            }

            // The targets of the attributes of each node, found in one pass over its children
            foreach (var node in document.DescendantNodesAndSelf())
            {
                SyntaxNodeOrToken previous = default;
                var attributesParagraph = node is MarkdownParagraphSyntax paragraph && _nextBlocks.ContainsKey(paragraph);
                var children = node.ChildNodesAndTokens();
                var count = children.Count;
                var index = 0;
                foreach (var child in children)
                {
                    index++;
                    if (child.AsNode() is not MarkdownGenericAttributesSyntax attributes)
                    {
                        previous = child;
                        continue;
                    }

                    if (attributes.GetTarget(previous, attributesParagraph && index == count, _nextBlocks.GetValueOrDefault) is { } target)
                    {
                        Merge(attributes, target);
                    }
                }
            }
        }

        private static bool IsAttributesOnly(MarkdownParagraphSyntax paragraph)
        {
            foreach (var child in paragraph.ChildNodesAndTokens())
            {
                if (child.AsNode() is not MarkdownGenericAttributesSyntax)
                {
                    return false;
                }
            }

            return true;
        }

        private void Merge(MarkdownGenericAttributesSyntax attributes, SyntaxNode target)
        {
            {
                if (!_attributes!.TryGetValue(target, out var merged))
                {
                    merged = new ParsedAttributes(null, [], []);
                }

                // A property is added when the element has none with its name from other attributes: the ones of the same attributes are all kept
                var properties = merged.Properties;
                var existingCount = properties.Count;
                foreach (var property in attributes.Properties)
                {
                    if (rendererOptions.AttributeFilter(property.Key) && properties.FindIndex(0, existingCount, existing => string.Equals(existing.Key, property.Key, StringComparison.Ordinal)) < 0)
                    {
                        properties.Add(property);
                    }
                }

                if (attributes.IsBlockLine)
                {
                    _blockLineClasses ??= [];
                    _blockLineClasses[target] = [.. attributes.Classes];
                }
                else
                {
                    merged.Classes.AddRange(attributes.Classes);
                }

                _attributes![target] = merged with { Id = attributes.Id ?? merged.Id };
            }
        }

        // The generic attributes of the element of the node: its id, the classes of the element and the ones of the attributes, and
        // the other attributes
        private void AppendAttributes(SyntaxNode node, string? leadingClasses = null, string? trailingClasses = null)
        {
            ParsedAttributes? attributes = null;
            _ = _attributes?.TryGetValue(node, out attributes);
            if (attributes?.Id is { } id)
            {
                _output.Append(" id=\"");
                AppendEscaped(id);
                _output.Append('"');
            }

            List<string>? blockLineClasses = null;
            _ = _blockLineClasses?.TryGetValue(node, out blockLineClasses);
            var hasClasses = attributes is { Classes.Count: > 0 } || blockLineClasses is { Count: > 0 };
            if (hasClasses || leadingClasses is not null || trailingClasses is not null)
            {
                _output.Append(" class=\"");
                var first = true;
                foreach (var cssClass in (string?[])[leadingClasses, .. blockLineClasses ?? [], trailingClasses, .. attributes?.Classes ?? []])
                {
                    if (cssClass is null)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        _output.Append(' ');
                    }

                    AppendEscaped(cssClass);
                    first = false;
                }

                _output.Append('"');
            }

            foreach (var property in attributes?.Properties ?? [])
            {
                _output.Append(' ').Append(property.Key).Append("=\"");
                AppendEscaped(property.Value ?? "");
                _output.Append('"');
            }
        }

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
                case MarkdownParagraphSyntax paragraph when _attributes is not null && IsAttributesParagraph(paragraph):
                    // Its attributes are for the next block
                    break;

                case MarkdownParagraphSyntax paragraph:
                {
                    // The paragraphs of a tight list have no <p>
                    var tight = paragraph.Parent is MarkdownListItemSyntax { Parent: MarkdownListSyntax { IsTight: true } };
                    if (!tight)
                    {
                        Cr();
                        _output.Append("<p");
                        AppendAttributes(paragraph);
                        _output.Append('>');
                    }

                    if (_pendingCheckbox is not null)
                    {
                        _output.Append(_pendingCheckbox);
                        _pendingCheckbox = null;
                    }

                    WriteInlines(paragraph.Inlines);
                    if (paragraph == _backLinksParagraph)
                    {
                        _output.Append(_backLinks);
                    }

                    if (!tight)
                    {
                        _output.Append("</p>");
                        Cr();
                    }

                    break;
                }

                case MarkdownHeadingSyntax heading:
                    Cr();
                    _output.Append("<h").Append(heading.Level);
                    AppendAttributes(heading);
                    _output.Append('>');
                    WriteInlines(heading.Inlines);
                    _output.Append("</h").Append(heading.Level).Append('>');
                    Cr();
                    break;

                case MarkdownThematicBreakSyntax:
                    Cr();
                    _output.Append("<hr");
                    AppendAttributes(block);
                    _output.Append(" />");
                    Cr();
                    break;

                case MarkdownAlertSyntax alert:
                    Cr();
                    _output.Append("<div class=\"markdown-alert markdown-alert-");
                    AppendEscaped(alert.AlertKind.ToLowerInvariant());
                    _output.Append("\">\n");
                    if (AlertTitles.TryGetValue(alert.AlertKind, out var title))
                    {
                        _output.Append(title).Append('\n');
                    }

                    WriteBlocks(alert.Blocks);
                    Cr();
                    _output.Append("</div>");
                    Cr();
                    break;

                case MarkdownBlockQuoteSyntax quote:
                    Cr();
                    _output.Append("<blockquote");
                    AppendAttributes(quote);
                    _output.Append('>');
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
                        _output.Append(start == 1 ? "<ol" : "<ol start=\"" + start.ToString(CultureInfo.InvariantCulture) + "\"");
                        AppendAttributes(list);
                        _output.Append('>');
                    }
                    else
                    {
                        _output.Append("<ul");
                        AppendAttributes(list);
                        _output.Append('>');
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

                case MarkdownCodeBlockSyntax { IsFenced: true, Language: "mermaid" or "nomnoml" } diagram when _diagrams:
                {
                    // A diagram is its code, where only < and & are escaped, in the element that its script reads
                    var tag = diagram.Language == "mermaid" ? "pre" : "div";
                    Cr();
                    _output.Append('<').Append(tag).Append(" class=\"").Append(diagram.Language).Append("\">");
                    AppendSoftEscaped(diagram.Code);
                    _output.Append("</").Append(tag).Append('>');
                    Cr();
                    break;
                }

                case MarkdownCodeBlockSyntax code:
                    Cr();
                    _output.Append("<pre><code");
                    AppendAttributes(code, trailingClasses: code.Language is { } language ? "language-" + language : null);
                    _output.Append('>');
                    AppendEscaped(code.Code);
                    _output.Append("</code></pre>");
                    Cr();
                    break;

                case MarkdownMathBlockSyntax math:
                    Cr();
                    _output.Append("<div");
                    AppendAttributes(math, leadingClasses: "math");
                    _output.Append(">\n\\[\n");
                    AppendEscaped(math.Math);
                    _output.Append("\\]</div>");
                    Cr();
                    break;

                case MarkdownHtmlBlockSyntax html:
                    Cr();
                    AppendHtml(html.Html);
                    Cr();
                    break;

                case MarkdownTableSyntax table:
                    WriteTable(table);
                    break;
            }
        }

        // The footnotes that are referred to, at the end of the document, each one with the links back to its references
        public void WriteFootnotes(MarkdownDocumentSyntax document)
        {
            // A document with footnotes has the list of the footnotes, even when none is referred to, as 1.x writes it
            _footnotes = new MarkdownFootnotes(document);
            if (!document.Blocks.OfType<MarkdownFootnoteDefinitionSyntax>().Any())
            {
                return;
            }

            Cr();
            _output.Append("<div class=\"footnotes\">\n<hr />\n<ol>\n");
            foreach (var footnote in _footnotes.Footnotes)
            {
                _output.Append("<li id=\"fn:").Append(_footnotes.GetNumber(footnote)).Append("\">\n");
                var backLinks = new StringBuilder();
                foreach (var reference in _footnotes.GetReferences(footnote))
                {
                    backLinks.Append("<a href=\"#fnref:").Append(_footnotes.GetReferenceNumber(reference)).Append("\" class=\"footnote-back-ref\">&#8617;</a>");
                }

                // The links end the last paragraph, or a paragraph of their own
                _backLinks = backLinks.ToString();
                _backLinksParagraph = footnote.Blocks.Count > 0 ? footnote.Blocks[^1] as MarkdownParagraphSyntax : null;
                WriteBlocks(footnote.Blocks);
                if (_backLinksParagraph is null)
                {
                    Cr();
                    _output.Append("<p>").Append(_backLinks).Append("</p>");
                }

                Cr();
                _output.Append("</li>\n");
            }

            _output.Append("</ol>\n</div>\n");
        }

        // A paragraph of attributes only, whose last attributes are for the next block, is not written
        private bool IsAttributesParagraph(MarkdownParagraphSyntax paragraph) => _nextBlocks!.ContainsKey(paragraph);

        private void WriteListItem(MarkdownListItemSyntax item)
        {
            _output.Append("<li");
            AppendAttributes(item);
            _output.Append('>');
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
                case SyntaxKind.LineEndingToken when !_hardLineBreaks:
                    _output.Append('\n');
                    break;
                case SyntaxKind.HardLineBreakToken or SyntaxKind.LineEndingToken:
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
                    _output.Append('<').Append(tag);
                    AppendAttributes(emphasis);
                    _output.Append('>');
                    WriteInlines(emphasis.Inlines);
                    _output.Append("</").Append(tag).Append('>');
                    break;
                }

                case MarkdownStrikethroughSyntax strikethrough:
                    _output.Append("<del");
                    AppendAttributes(strikethrough);
                    _output.Append('>');
                    WriteInlines(strikethrough.Inlines);
                    _output.Append("</del>");
                    break;

                case MarkdownEmphasisExtraSyntax extra:
                {
                    var tag = extra.Style switch
                    {
                        MarkdownEmphasisExtraStyle.Subscript => "sub",
                        MarkdownEmphasisExtraStyle.Superscript => "sup",
                        MarkdownEmphasisExtraStyle.Inserted => "ins",
                        _ => "mark",
                    };

                    _output.Append('<').Append(tag);
                    AppendAttributes(extra);
                    _output.Append('>');
                    WriteInlines(extra.Inlines);
                    _output.Append("</").Append(tag).Append('>');
                    break;
                }

                case MarkdownCodeSpanSyntax code:
                    _output.Append("<code");
                    AppendAttributes(code);
                    _output.Append('>');
                    AppendEscaped(code.Code);
                    _output.Append("</code>");
                    break;

                case MarkdownAutolinkSyntax autolink:
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(autolink.Destination));
                    _output.Append('"');
                    AppendAttributes(autolink);
                    _output.Append('>');
                    AppendEscaped(autolink.Text);
                    _output.Append("</a>");
                    break;

                case MarkdownRawHtmlSyntax html:
                    AppendHtml(html.Html);
                    break;

                case MarkdownAbbreviationSyntax abbreviation:
                    if (abbreviation.Definition is { } definition)
                    {
                        _output.Append("<abbr title=\"");
                        AppendEscaped(definition.Title);
                        _output.Append("\">");
                        AppendEscaped(abbreviation.Label);
                        _output.Append("</abbr>");
                    }
                    else
                    {
                        AppendEscaped(abbreviation.Label);
                    }

                    break;

                case MarkdownFootnoteReferenceSyntax reference:
                {
                    _footnotes ??= new MarkdownFootnotes(reference.FirstAncestorOrSelf<MarkdownDocumentSyntax>()!);
                    if (reference.Definition is { } footnote && _footnotes.Footnotes.Contains(footnote))
                    {
                        var number = _footnotes.GetNumber(footnote);
                        _output.Append("<a id=\"fnref:").Append(_footnotes.GetReferenceNumber(reference)).Append("\" href=\"#fn:").Append(number).Append("\" class=\"footnote-ref\"><sup>").Append(number).Append("</sup></a>");
                    }

                    break;
                }

                case MarkdownMathInlineSyntax math:
                    _output.Append("<span");
                    AppendAttributes(math, leadingClasses: "math");
                    _output.Append(">\\(");
                    AppendEscaped(math.Math);
                    _output.Append("\\)</span>");
                    break;

                case MarkdownLinkSyntax { IsImage: true } image:
                    _output.Append("<img src=\"");
                    AppendEscaped(NormalizeUri(image.Destination));
                    _output.Append('"');
                    AppendAttributes(image);
                    _output.Append(" alt=\"");
                    AppendEscaped(image.GetPlainText());
                    _output.Append('"');
                    AppendTitle(image.Title);
                    _output.Append(" />");
                    break;

                case MarkdownLinkSyntax link:
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(link.Destination));
                    _output.Append('"');
                    AppendAttributes(link);
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
        private void AppendHtml(ReadOnlySpan<char> html)
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
                    _output.Append(IsDisallowedTag(html[index..]) ? "&lt;" : "<");
                }

                html = html[(index + 1)..];
            }
        }

        private static bool IsDisallowedTag(ReadOnlySpan<char> tag)
        {
            var name = tag[1..];
            if (name.StartsWith('/'))
            {
                name = name[1..];
            }

            foreach (var disallowed in (ReadOnlySpan<string>)["title", "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "plaintext"])
            {
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

        private void AppendSoftEscaped(string text)
        {
            foreach (var c in text)
            {
                _ = c switch
                {
                    '<' => _output.Append("&lt;"),
                    '&' => _output.Append("&amp;"),
                    '\0' => _output.Append('\uFFFD'),
                    _ => _output.Append(c),
                };
            }
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
