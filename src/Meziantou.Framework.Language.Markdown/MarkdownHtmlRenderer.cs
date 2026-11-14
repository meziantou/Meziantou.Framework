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

        var writer = new Writer(tree.GetRoot());
        writer.WriteBlocks(tree.GetRoot());
        return writer.ToString();
    }

    private sealed class Writer
    {
        private readonly StringBuilder _output = new();
        private readonly Dictionary<string, (string Destination, string? Title)> _references = new(StringComparer.Ordinal);
        private readonly string _source;

        // Whether each list is tight, which every paragraph of the list asks
        private readonly Dictionary<SyntaxNode, bool> _tightLists = [];

        public Writer(MarkdownSyntaxNode document)
        {
            _source = document.ToFullString();

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
                    var lines = block.ChildTokens().Where(token => token.RawKind == (int)SyntaxKind.HtmlLineToken).Select(token => TrimLineEnding(token.Text)).ToList();
                    _output.Append(string.Join('\n', lines));
                    Cr();
                    break;
                }
            }
        }

        // The line endings of a code span are spaces, and one space is removed on each side unless the code is only spaces
        private static string GetCodeSpanText(MarkdownSyntaxNode codeSpan)
        {
            var content = new StringBuilder();
            var tokens = codeSpan.ChildTokens().ToList();
            for (var i = 1; i < tokens.Count - 1; i++)
            {
                content.Append(tokens[i].RawKind == (int)SyntaxKind.LineEndingToken ? " " : tokens[i].Text);
            }

            var text = content.ToString();
            if (text.Length >= 2 && text[0] == ' ' && text[^1] == ' ' && text.AsSpan().ContainsAnyExcept(' '))
            {
                text = text[1..^1];
            }

            return text;
        }

        private static string TrimLineEnding(string text) => text.TrimEnd('\r', '\n').Replace('\0', '�');

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
                case SyntaxKind.TextToken or SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken or SyntaxKind.CloseBracketToken:
                    AppendEscaped(token.Text);
                    break;
                case SyntaxKind.EscapedCharacterToken:
                    AppendEscaped(token.Text[1..]);
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
                case SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis:
                {
                    var tag = node.Kind() == SyntaxKind.Emphasis ? "em" : "strong";
                    _output.Append('<').Append(tag).Append('>');
                    foreach (var child in node.ChildNodesAndTokens())
                    {
                        if (child.AsNode() is MarkdownSyntaxNode inner)
                        {
                            WriteInline(inner);
                        }
                        else if (child.AsToken().RawKind != (int)SyntaxKind.EmphasisDelimiterToken)
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

                case SyntaxKind.Autolink:
                {
                    var text = node.ChildTokens().First().Text[1..^1];
                    var destination = text.Contains('@', StringComparison.Ordinal) && !text.Contains(':', StringComparison.Ordinal) ? "mailto:" + text : text;
                    _output.Append("<a href=\"");
                    AppendEscaped(NormalizeUri(destination));
                    _output.Append("\">");
                    AppendEscaped(text);
                    _output.Append("</a>");
                    break;
                }

                case SyntaxKind.RawHtml:
                    foreach (var token in node.ChildTokens())
                    {
                        _output.Append(token.RawKind == (int)SyntaxKind.LineEndingToken ? "\n" : token.Text);
                    }

                    break;

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
            _source = parent._source;
            _references = parent._references;
            _tightLists = parent._tightLists;
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

                        case SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis:
                            WritePlainText(node.ChildNodesAndTokens().Where(item => !(item.IsToken && item.AsToken().RawKind == (int)SyntaxKind.EmphasisDelimiterToken)));
                            break;

                        case SyntaxKind.Autolink:
                            _output.Append(node.ChildTokens().First().Text[1..^1]);
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
                        case SyntaxKind.TextToken or SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken or SyntaxKind.CloseBracketToken:
                            _output.Append(token.Text);
                            break;
                        case SyntaxKind.EscapedCharacterToken:
                            _output.Append(token.Text[1..]);
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

        private static void AppendTokensText(SyntaxNodeOrToken element, StringBuilder builder)
        {
            if (element.AsNode() is { } node)
            {
                foreach (var token in node.DescendantTokens())
                {
                    builder.Append(token.Text);
                }
            }
            else
            {
                builder.Append(element.AsToken().Text);
            }
        }

        private static string GetTokensText(MarkdownSyntaxNode node, SyntaxKind kind)
        {
            var builder = new StringBuilder();
            var tokens = node.ChildTokens().ToList();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].RawKind == (int)kind)
                {
                    builder.Append(tokens[i].Text);
                }
                else if (tokens[i].RawKind == (int)SyntaxKind.LineEndingToken && i > 0 && tokens[i - 1].RawKind == (int)kind)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        private static string GetDestination(MarkdownSyntaxNode node)
        {
            var destination = node.ChildTokens().FirstOrDefault(token => token.RawKind == (int)SyntaxKind.LinkDestinationToken).Text ?? "";
            if (destination.StartsWith('<', StringComparison.Ordinal) && destination.EndsWith('>', StringComparison.Ordinal))
            {
                destination = destination[1..^1];
            }

            return Unescape(destination);
        }

        private static string? GetTitle(MarkdownSyntaxNode node)
        {
            var title = GetTokensText(node, SyntaxKind.LinkTitleToken);
            return title.Length < 2 ? null : Unescape(title[1..^1]);
        }

        // Removes the backslash escapes and decodes the entities
        private static string Unescape(string text)
        {
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

        private static string DecodeEntity(string entity)
        {
            if (entity.StartsWith("&#", StringComparison.Ordinal))
            {
                var isHex = entity[2] is 'x' or 'X';
                var digits = entity.AsSpan(isHex ? 3 : 2, entity.Length - (isHex ? 4 : 3));
                if (!int.TryParse(digits, isHex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out var codePoint) || codePoint == 0 || !Rune.IsValid(codePoint))
                {
                    return "�";
                }

                return char.ConvertFromUtf32(codePoint);
            }

            // An entity that HTML does not define is text
            return HtmlEntities.Entities.TryGetValue(entity[1..^1], out var value) ? value : entity;
        }

        // Percent-encodes the characters that cannot be in a URL, as the reference implementation does
        private static string NormalizeUri(string uri)
        {
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

        private void AppendEscaped(string text)
        {
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&':
                        _output.Append("&amp;");
                        break;
                    case '<':
                        _output.Append("&lt;");
                        break;
                    case '>':
                        _output.Append("&gt;");
                        break;
                    case '"':
                        _output.Append("&quot;");
                        break;
                    case '\0':
                        _output.Append('�');
                        break;
                    default:
                        _output.Append(c);
                        break;
                }
            }
        }
    }
}
