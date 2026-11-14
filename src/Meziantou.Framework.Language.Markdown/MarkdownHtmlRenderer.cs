using System.Buffers;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>Renders a Markdown syntax tree as HTML, as the CommonMark reference implementation does.</summary>
public static class MarkdownHtmlRenderer
{
    /// <summary>Renders the document as HTML.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToHtml(MarkdownSyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var writer = new Writer(tree.GetRoot(), tree.GetText().Text, tree.Options.Extensions.HasFlag(MarkdownExtensions.TagFilter));
        writer.WriteBlocks(tree.GetRoot());
        return writer.ToString();
    }

    private static readonly SearchValues<char> EscapedCharacters = SearchValues.Create("&<>\"\0");

    // The characters a URL keeps as they are: a % is kept only before two hexadecimal digits, so it is not one of them
    private static readonly SearchValues<char> UriSafeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;/?:@&=+$,-_.!~*'()#");

    private sealed class Writer
    {
        private readonly StringBuilder _output;
        private readonly Dictionary<string, (string Destination, string? Title)> _references = new(StringComparer.Ordinal);
        private readonly string _source;

        // Whether each list is tight, which every paragraph of the list asks
        private readonly Dictionary<SyntaxNode, bool> _tightLists = [];
        private readonly bool _tagFilter;

        // In a cell of a table, a pipe escaped by a backslash is a pipe, even in a code span or a link destination
        private bool _inTableCell;

        // The checkbox of a task list item, which its first paragraph starts with
        private string? _pendingCheckbox;

        public Writer(MarkdownSyntaxNode document, string source, bool tagFilter)
        {
            // The HTML is about as long as the Markdown
            _output = new StringBuilder(source.Length + 16);
            _source = source;
            _tagFilter = tagFilter;

            // The first definition of a label wins
            foreach (var node in document.DescendantNodes().OfType<MarkdownSyntaxNode>())
            {
                if (node.Kind() == SyntaxKind.LinkReferenceDefinition)
                {
                    var label = Scanners.NormalizeLabel(GetTokensText(node, SyntaxKind.LinkLabelToken));
                    _references.TryAdd(label, (GetDestination(node), GetTitle(node)));
                }
            }
        }

        public override string ToString() => _output.ToString();

        private void Cr()
        {
            if (_output.Length > 0 && _output[^1] != '\n')
            {
                _output.Append('\n');
            }
        }

        public void WriteBlocks(MarkdownSyntaxNode container)
        {
            foreach (var child in container.ChildNodes().Cast<MarkdownSyntaxNode>())
            {
                WriteBlock(child);
            }
        }

        private void WriteBlock(MarkdownSyntaxNode block)
        {
            switch (block.Kind())
            {
                case SyntaxKind.Paragraph:
                    if (_pendingCheckbox is not null)
                    {
                        // The paragraph is the first block of a task list item
                        if (IsTight((MarkdownSyntaxNode)block.Parent!.Parent!))
                        {
                            WriteCheckbox();
                            WriteInlines(block);
                            return;
                        }

                        Cr();
                        _output.Append("<p>");
                        WriteCheckbox();
                        WriteInlines(block);
                        _output.Append("</p>");
                        Cr();
                        break;
                    }

                    if (block.Parent?.Parent is { } list && list.Kind() == SyntaxKind.List && IsTight(list))
                    {
                        WriteInlines(block);
                        return;
                    }

                    Cr();
                    _output.Append("<p>");
                    WriteInlines(block);
                    _output.Append("</p>");
                    Cr();
                    break;

                case SyntaxKind.Table:
                    WriteTable(block);
                    break;

                case SyntaxKind.AtxHeading or SyntaxKind.SetextHeading:
                {
                    var level = GetHeadingLevel(block);
                    Cr();
                    _output.Append("<h").Append(level).Append('>');
                    WriteInlines(block);
                    _output.Append("</h").Append(level).Append('>');
                    Cr();
                    break;
                }

                case SyntaxKind.ThematicBreak:
                    Cr();
                    _output.Append("<hr />");
                    Cr();
                    break;

                case SyntaxKind.BlockQuote:
                    Cr();
                    _output.Append("<blockquote>");
                    Cr();
                    WriteBlocks(block);
                    Cr();
                    _output.Append("</blockquote>");
                    Cr();
                    break;

                case SyntaxKind.List:
                {
                    var marker = block.ChildNodes().Cast<MarkdownSyntaxNode>().First().ChildTokens().First(token => token.RawKind == (int)SyntaxKind.ListMarkerToken).Text;
                    var ordered = char.IsAsciiDigit(marker[0]);
                    Cr();
                    if (ordered)
                    {
                        var start = int.Parse(marker.AsSpan(0, marker.Length - 1), CultureInfo.InvariantCulture);
                        _output.Append(start == 1 ? "<ol>" : "<ol start=\"" + start.ToString(CultureInfo.InvariantCulture) + "\">");
                    }
                    else
                    {
                        _output.Append("<ul>");
                    }

                    Cr();
                    WriteBlocks(block);
                    Cr();
                    _output.Append(ordered ? "</ol>" : "</ul>");
                    Cr();
                    break;
                }

                case SyntaxKind.ListItem:
                    _output.Append("<li>");
                    if (block.ChildTokens().FirstOrDefault(token => token.RawKind == (int)SyntaxKind.TaskListItemMarkerToken) is { RawKind: not 0 } task)
                    {
                        _pendingCheckbox = task.Text[1] == ' ' ? "<input disabled=\"\" type=\"checkbox\"> " : "<input checked=\"\" disabled=\"\" type=\"checkbox\"> ";
                        if (block.ChildNodes().FirstOrDefault() is not MarkdownSyntaxNode { } first || first.Kind() != SyntaxKind.Paragraph)
                        {
                            WriteCheckbox();
                        }
                    }

                    WriteBlocks(block);
                    _output.Append("</li>");
                    Cr();
                    break;

                case SyntaxKind.FencedCodeBlock or SyntaxKind.IndentedCodeBlock:
                {
                    Cr();
                    _output.Append("<pre><code");
                    var info = block.ChildTokens().FirstOrDefault(token => token.RawKind == (int)SyntaxKind.InfoStringToken);
                    if (info.RawKind != 0)
                    {
                        var language = Unescape(info.Text);
                        var space = language.AsSpan().IndexOfAny(' ', '\t');
                        language = space < 0 ? language : language[..space];
                        if (language.Length > 0)
                        {
                            _output.Append(" class=\"language-");
                            AppendEscaped(language);
                            _output.Append('"');
                        }
                    }

                    _output.Append('>');
                    foreach (var token in block.ChildTokens())
                    {
                        if (token.RawKind == (int)SyntaxKind.CodeLineToken)
                        {
                            if (token.Value is int virtualSpaces)
                            {
                                _output.Append(' ', virtualSpaces);
                            }

                            AppendEscaped(TrimLineEnding(token.Text));
                            _output.Append('\n');
                        }
                    }

                    _output.Append("</code></pre>");
                    Cr();
                    break;
                }

                case SyntaxKind.HtmlBlock:
                {
                    Cr();
                    // The lines joined by line breaks: a line is written once the next one is known
                    string? previous = null;
                    foreach (var token in block.ChildTokens())
                    {
                        if (token.RawKind == (int)SyntaxKind.HtmlLineToken)
                        {
                            if (previous is not null)
                            {
                                AppendHtml(TrimLineEnding(previous), followedByLineBreak: true);
                                _output.Append('\n');
                            }

                            previous = token.Text;
                        }
                    }

                    if (previous is not null)
                    {
                        AppendHtml(TrimLineEnding(previous), followedByLineBreak: false);
                    }

                    Cr();
                    break;
                }
            }
        }

        private void WriteCheckbox()
        {
            _output.Append(_pendingCheckbox);
            _pendingCheckbox = null;
        }

        // A table: the cells of the header row, then the cells of the other rows, as many as the header row has
        private void WriteTable(MarkdownSyntaxNode table)
        {
            var rows = table.ChildNodes().Cast<MarkdownSyntaxNode>().ToList();
            var alignments = rows[1].ChildTokens()
                .Where(token => token.RawKind == (int)SyntaxKind.TableDelimiterToken)
                .Select(token => (token.Text[0] == ':', token.Text[^1] == ':') switch
                {
                    (true, true) => " align=\"center\"",
                    (true, false) => " align=\"left\"",
                    (false, true) => " align=\"right\"",
                    _ => "",
                })
                .ToList();

            Cr();
            _output.Append("<table>\n<thead>\n");
            WriteTableRow(rows[0], "th", alignments);
            _output.Append("</thead>\n");
            if (rows.Count > 2)
            {
                _output.Append("<tbody>\n");
                for (var i = 2; i < rows.Count; i++)
                {
                    WriteTableRow(rows[i], "td", alignments);
                }

                _output.Append("</tbody>\n");
            }

            _output.Append("</table>\n");
        }

        private void WriteTableRow(MarkdownSyntaxNode row, string tag, List<string> alignments)
        {
            _output.Append("<tr>\n");
            var cells = row.ChildNodes().Cast<MarkdownSyntaxNode>().ToList();
            for (var i = 0; i < alignments.Count; i++)
            {
                _output.Append('<').Append(tag).Append(alignments[i]).Append('>');
                if (i < cells.Count)
                {
                    _inTableCell = true;
                    WriteInlines(cells[i]);
                    _inTableCell = false;
                }

                _output.Append("</").Append(tag).Append(">\n");
            }

            _output.Append("</tr>\n");
        }

        private string GetText(SyntaxToken token) => _inTableCell ? token.Text.Replace("\\|", "|", StringComparison.Ordinal) : token.Text;

        // The line endings of a code span are spaces, and one space is removed on each side unless the code is only spaces
        private string GetCodeSpanText(MarkdownSyntaxNode codeSpan)
        {
            var content = new StringBuilder();
            var tokens = codeSpan.ChildTokens().ToList();
            for (var i = 1; i < tokens.Count - 1; i++)
            {
                content.Append(tokens[i].RawKind == (int)SyntaxKind.LineEndingToken ? " " : GetText(tokens[i]));
            }

            var text = content.ToString();
            if (text.Length >= 2 && text[0] == ' ' && text[^1] == ' ' && text.AsSpan().ContainsAnyExcept(' '))
            {
                text = text[1..^1];
            }

            return text;
        }

        private static ReadOnlySpan<char> TrimLineEnding(string text) => text.AsSpan().TrimEnd("\r\n");

        private static int GetHeadingLevel(MarkdownSyntaxNode heading)
        {
            foreach (var token in heading.ChildTokens())
            {
                if (token.RawKind == (int)SyntaxKind.AtxHeadingMarkerToken)
                {
                    return token.Text.Length;
                }

                if (token.RawKind == (int)SyntaxKind.SetextHeadingUnderlineToken)
                {
                    return token.Text[0] == '=' ? 1 : 2;
                }
            }

            return 1;
        }

        // A list is loose when a blank line separates two of its items, or two blocks of one of its items
        private bool IsTight(MarkdownSyntaxNode list)
        {
            if (!_tightLists.TryGetValue(list, out var tight))
            {
                tight = ComputeIsTight(list);
                _tightLists[list] = tight;
            }

            return tight;
        }

        private bool ComputeIsTight(MarkdownSyntaxNode list)
        {
            var items = list.ChildNodes().ToList();
            if (HasBlankLineBetween(items))
            {
                return false;
            }

            foreach (var item in items)
            {
                if (HasBlankLineBetween(item.ChildNodes().ToList()))
                {
                    return false;
                }
            }

            return true;
        }

        // A line between the nodes that has nothing but the markers of the containers of the list is blank. The marker of a
        // block quote in the list is not: the line is a line of the quote.
        private bool HasBlankLineBetween(List<SyntaxNode> nodes)
        {
            if (nodes.Count < 2)
            {
                return false;
            }

            var quoteDepth = 0;
            for (var ancestor = ((MarkdownSyntaxNode)nodes[0]).Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.Kind() == SyntaxKind.BlockQuote)
                {
                    quoteDepth++;
                }
            }

            for (var i = 1; i < nodes.Count; i++)
            {
                var gap = _source[nodes[i - 1].Span.End..nodes[i].SpanStart].ReplaceLineEndings("\n").Split('\n');

                // The first and the last parts are the ends of the lines of the nodes
                for (var j = 1; j < gap.Length - 1; j++)
                {
                    if (gap[j].Count(c => c == '>') <= quoteDepth)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void WriteInlines(MarkdownSyntaxNode container)
        {
            foreach (var child in container.ChildNodesAndTokens())
            {
                if (child.AsNode() is MarkdownSyntaxNode node)
                {
                    WriteInline(node);
                }
                else
                {
                    WriteToken(child.AsToken());
                }
            }
        }

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
                    AppendEscaped(DecodeEntity(token.Text));
                    break;
                case SyntaxKind.LineEndingToken:
                    _output.Append('\n');
                    break;
                case SyntaxKind.HardLineBreakToken:
                    _output.Append("<br />\n");
                    break;
            }
        }

        private void WriteInline(MarkdownSyntaxNode node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis or SyntaxKind.Strikethrough:
                {
                    var tag = node.Kind() switch
                    {
                        SyntaxKind.Emphasis => "em",
                        SyntaxKind.StrongEmphasis => "strong",
                        _ => "del",
                    };

                    _output.Append('<').Append(tag).Append('>');
                    foreach (var child in node.ChildNodesAndTokens())
                    {
                        if (child.AsNode() is MarkdownSyntaxNode inner)
                        {
                            WriteInline(inner);
                        }
                        else if (child.AsToken().RawKind is not ((int)SyntaxKind.EmphasisDelimiterToken or (int)SyntaxKind.StrikethroughDelimiterToken))
                        {
                            WriteToken(child.AsToken());
                        }
                    }

                    _output.Append("</").Append(tag).Append('>');
                    break;
                }

                case SyntaxKind.CodeSpan:
                    _output.Append("<code>");
                    AppendEscaped(GetCodeSpanText(node));
                    _output.Append("</code>");
                    break;

                case SyntaxKind.ExtendedAutolink:
                {
                    // A www. address gets the http scheme, and an email address the mailto scheme
                    var plain = new Writer(this);
                    plain.WritePlainText(node.ChildNodesAndTokens());
                    var text = plain._output.ToString();
                    var destination = node.ChildTokens().First().RawKind == (int)SyntaxKind.ExtendedAutolinkToken
                        ? (text.StartsWith("www.", StringComparison.Ordinal) ? "http://" + text : text)
                        : "mailto:" + text;
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(destination));
                    _output.Append("\">");
                    AppendEscaped(text);
                    _output.Append("</a>");
                    break;
                }

                case SyntaxKind.Autolink:
                {
                    var text = GetText(node.ChildTokens().First())[1..^1];
                    var destination = text.Contains('@', StringComparison.Ordinal) && !text.Contains(':', StringComparison.Ordinal) ? "mailto:" + text : text;
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(destination));
                    _output.Append("\">");
                    AppendEscaped(text);
                    _output.Append("</a>");
                    break;
                }

                case SyntaxKind.RawHtml:
                {
                    var html = new StringBuilder();
                    foreach (var token in node.ChildTokens())
                    {
                        html.Append(token.RawKind == (int)SyntaxKind.LineEndingToken ? "\n" : GetText(token));
                    }

                    AppendHtml(html.ToString(), followedByLineBreak: false);
                    break;
                }

                case SyntaxKind.Link or SyntaxKind.Image:
                    WriteLink(node);
                    break;
            }
        }

        private void WriteLink(MarkdownSyntaxNode link)
        {
            var (destination, title) = ResolveLink(link);
            var content = link.ChildNodesAndTokens().ToList();

            // The text of the link is between the opening bracket and the first closing bracket at its level
            var close = content.FindIndex(child => child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.CloseBracketToken);
            var text = content.GetRange(1, close - 1);
            if (link.Kind() == SyntaxKind.Image)
            {
                _output.Append("<img src=\"");
                AppendEscaped(NormalizeUri(destination));
                _output.Append("\" alt=\"");
                var alt = new Writer(this);
                alt.WritePlainText(text);
                AppendEscaped(alt._output.ToString());
                _output.Append('"');
                AppendTitle(title);
                _output.Append(" />");
                return;
            }

            _output.Append("<a href=\"");
            AppendEscaped(NormalizeUri(destination));
            _output.Append('"');
            AppendTitle(title);
            _output.Append('>');
            foreach (var child in text)
            {
                if (child.AsNode() is MarkdownSyntaxNode node)
                {
                    WriteInline(node);
                }
                else
                {
                    WriteToken(child.AsToken());
                }
            }

            _output.Append("</a>");
        }

        private Writer(Writer parent)
        {
            _output = new StringBuilder();
            _source = parent._source;
            _references = parent._references;
            _tightLists = parent._tightLists;
            _tagFilter = parent._tagFilter;
            _inTableCell = parent._inTableCell;
        }

        // The text of an image description: its inlines without their markup
        private void WritePlainText(IEnumerable<SyntaxNodeOrToken> children)
        {
            foreach (var child in children)
            {
                if (child.AsNode() is MarkdownSyntaxNode node)
                {
                    switch (node.Kind())
                    {
                        case SyntaxKind.Link or SyntaxKind.Image:
                        {
                            var content = node.ChildNodesAndTokens().ToList();
                            var close = content.FindIndex(item => item.IsToken && item.AsToken().RawKind == (int)SyntaxKind.CloseBracketToken);
                            WritePlainText(content.GetRange(1, close - 1));
                            break;
                        }

                        case SyntaxKind.CodeSpan:
                            _output.Append(GetCodeSpanText(node));
                            break;

                        case SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis or SyntaxKind.Strikethrough:
                            WritePlainText(node.ChildNodesAndTokens().Where(item => !(item.IsToken && item.AsToken().RawKind is (int)SyntaxKind.EmphasisDelimiterToken or (int)SyntaxKind.StrikethroughDelimiterToken)));
                            break;

                        case SyntaxKind.ExtendedAutolink:
                            WritePlainText(node.ChildNodesAndTokens());
                            break;

                        case SyntaxKind.Autolink:
                            _output.Append(GetText(node.ChildTokens().First())[1..^1]);
                            break;

                        case SyntaxKind.RawHtml:
                            break;
                    }
                }
                else
                {
                    var token = child.AsToken();
                    switch ((SyntaxKind)token.RawKind)
                    {
                        case SyntaxKind.TextToken or SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken or SyntaxKind.CloseBracketToken or SyntaxKind.ExtendedAutolinkToken:
                            _output.Append(GetText(token));
                            break;
                        case SyntaxKind.EscapedCharacterToken:
                            _output.Append(GetText(token)[1..]);
                            break;
                        case SyntaxKind.EntityToken:
                            _output.Append(DecodeEntity(token.Text));
                            break;
                        case SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken:
                            _output.Append('\n');
                            break;
                    }
                }
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

        private (string Destination, string? Title) ResolveLink(MarkdownSyntaxNode link)
        {
            var tokens = link.ChildTokens().ToList();
            if (tokens.Any(token => token.RawKind == (int)SyntaxKind.OpenParenthesisToken))
            {
                return (GetDestination(link), GetTitle(link));
            }

            // A reference link: the label is after the text, or the text itself
            string label;
            if (tokens.Any(token => token.RawKind == (int)SyntaxKind.LinkLabelToken))
            {
                label = GetTokensText(link, SyntaxKind.LinkLabelToken);
            }
            else
            {
                var content = link.ChildNodesAndTokens().ToList();
                var close = content.FindIndex(child => child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.CloseBracketToken);
                var builder = new StringBuilder();
                foreach (var child in content.GetRange(1, close - 1))
                {
                    AppendTokensText(child, builder);
                }

                label = builder.ToString();
            }

            return _references.TryGetValue(Scanners.NormalizeLabel(label), out var reference) ? reference : ("", null);
        }

        private void AppendTokensText(SyntaxNodeOrToken element, StringBuilder builder)
        {
            if (element.AsNode() is { } node)
            {
                foreach (var token in node.DescendantTokens())
                {
                    builder.Append(GetText(token));
                }
            }
            else
            {
                builder.Append(GetText(element.AsToken()));
            }
        }

        private string GetTokensText(MarkdownSyntaxNode node, SyntaxKind kind)
        {
            var builder = new StringBuilder();
            var tokens = node.ChildTokens().ToList();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].RawKind == (int)kind)
                {
                    builder.Append(GetText(tokens[i]));
                }
                else if (tokens[i].RawKind == (int)SyntaxKind.LineEndingToken && i > 0 && tokens[i - 1].RawKind == (int)kind)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        private string GetDestination(MarkdownSyntaxNode node)
        {
            var token = node.ChildTokens().FirstOrDefault(token => token.RawKind == (int)SyntaxKind.LinkDestinationToken);
            var destination = token.RawKind == 0 ? "" : GetText(token);
            if (destination.StartsWith('<', StringComparison.Ordinal) && destination.EndsWith('>', StringComparison.Ordinal))
            {
                destination = destination[1..^1];
            }

            return Unescape(destination);
        }

        private string? GetTitle(MarkdownSyntaxNode node)
        {
            var title = GetTokensText(node, SyntaxKind.LinkTitleToken);
            return title.Length < 2 ? null : Unescape(title[1..^1]);
        }

        // Removes the backslash escapes and decodes the entities
        private static string Unescape(string text)
        {
            if (!text.AsSpan().ContainsAny('\\', '&'))
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length && Scanners.IsEscapable(text[i + 1]))
                {
                    builder.Append(text[i + 1]);
                    i++;
                }
                else if (c == '&' && Scanners.EntityRegex().Match(text, i, text.Length - i) is { Success: true } match)
                {
                    builder.Append(DecodeEntity(match.Value));
                    i += match.Length - 1;
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string DecodeEntity(string entity) => HtmlEntities.Decode(entity);

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
