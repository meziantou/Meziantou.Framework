namespace Meziantou.Framework.Xml;

/// <summary>Represents an immutable XML syntax tree with source text and diagnostics.</summary>
/// <example>
/// <code>
/// var tree = XmlSyntaxTree.ParseText(xml);
/// var updated = tree.WithChanges(new XmlTextChange(new TextSpan(0, 0), "&lt;!--generated--&gt;"));
/// </code>
/// </example>
public sealed class XmlSyntaxTree
{
    private XmlSyntaxTree(string text, XmlDocumentSyntax root, IReadOnlyList<XmlDiagnostic> diagnostics)
    {
        Text = text;
        SourceText = SourceText.From(text);
        Root = root;
        Diagnostics = diagnostics;
        Root.SetParentAndTree(parent: null, this);
    }

    public string Text { get; }
    public SourceText SourceText { get; }
    public XmlDocumentSyntax Root { get; }
    public IReadOnlyList<XmlDiagnostic> Diagnostics { get; }

    public XmlDocumentSyntax GetRoot() => Root;
    public IReadOnlyList<XmlDiagnostic> GetDiagnostics() => Diagnostics;

    public static XmlSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Xml)] string text)
    {
        var parser = new XmlParser(text ?? string.Empty);
        return parser.Parse();
    }

    public XmlSyntaxTree WithChanges(params XmlTextChange[] changes) => WithChanges((IEnumerable<XmlTextChange>)changes);

    public XmlSyntaxTree WithChanges(IEnumerable<XmlTextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return ParseText(SourceText.WithChanges(changes).Text);
    }

    public IReadOnlyList<XmlTextChange> GetChanges(XmlSyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);
        if (string.Equals(Text, oldTree.Text, StringComparison.Ordinal))
            return [];

        return [new XmlTextChange(new TextSpan(0, oldTree.Text.Length), Text)];
    }

    public bool IsEquivalentTo(XmlSyntaxTree? other)
    {
        if (other is null)
            return false;

        if (string.Equals(Text, other.Text, StringComparison.Ordinal))
            return true;

        return string.Equals(Canonicalize(Text), Canonicalize(other.Text), StringComparison.Ordinal);
    }

    private static string Canonicalize(string text)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(text, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        }
        catch (System.Xml.XmlException)
        {
            return CollapseWhitespace(text);
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var seenWhitespace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                seenWhitespace = true;
                continue;
            }

            if (seenWhitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(character);
            seenWhitespace = false;
        }

        return builder.ToString();
    }

    private sealed class XmlParser
    {
        private const string XmlDeclarationPrefix = "<?xml";
        private readonly string _text;
        private readonly List<XmlDiagnostic> _diagnostics = [];
        private readonly List<XmlSyntaxNode> _documentNodes = [];
        private readonly Stack<ElementBuilder> _elementStack = new();
        private int _position;

        public XmlParser(string text)
        {
            _text = text ?? string.Empty;
        }

        public XmlSyntaxTree Parse()
        {
            while (!IsAtEnd)
            {
                if (Current != '<')
                {
                    ParseTextNode();
                    continue;
                }

                if (Match("<!--"))
                {
                    ParseComment();
                    continue;
                }

                if (Match("<![CDATA["))
                {
                    ParseCData();
                    continue;
                }

                if (MatchInsensitive(XmlDeclarationPrefix))
                {
                    ParseDeclaration();
                    continue;
                }

                if (Match("<?"))
                {
                    ParseProcessingInstruction();
                    continue;
                }

                if (MatchInsensitive("<!DOCTYPE"))
                {
                    ParseDocumentType();
                    continue;
                }

                if (Match("</"))
                {
                    ParseEndTag();
                    continue;
                }

                if (Match("<"))
                {
                    ParseElementOrSkippedText();
                    continue;
                }
            }

            while (_elementStack.Count > 0)
            {
                var unclosedElement = _elementStack.Pop();
                _diagnostics.Add(new XmlDiagnostic(
                    Id: "XML0001",
                    Message: $"Missing end tag for '{unclosedElement.Name}'.",
                    Severity: XmlDiagnosticSeverity.Error,
                    Span: new TextSpan(unclosedElement.Start, _text.Length - unclosedElement.Start)));

                var fullText = _text.Substring(unclosedElement.Start);
                var element = new XmlElementSyntax(
                    unclosedElement.Name,
                    unclosedElement.Attributes,
                    unclosedElement.Content,
                    endTag: null,
                    isSelfClosing: false,
                    fullText,
                    unclosedElement.StartTagText);

                AddNode(element);
            }

            var root = new XmlDocumentSyntax(_documentNodes, _text);
            return new XmlSyntaxTree(_text, root, _diagnostics);
        }

        private bool IsAtEnd => _position >= _text.Length;
        private char Current => _position < _text.Length ? _text[_position] : '\0';

        private void ParseTextNode()
        {
            var start = _position;
            while (!IsAtEnd && Current != '<')
            {
                _position++;
            }

            var text = _text[start.._position];
            if (text.Length > 0)
            {
                AddNode(new XmlTextSyntax(text));
            }
        }

        private void ParseComment()
        {
            var start = _position;
            _position += 4;
            var end = _text.IndexOf("-->", _position, StringComparison.Ordinal);
            if (end < 0)
            {
                AddDiagnostic(start, _text.Length - start, "XML0008", "Unterminated XML comment.");
                var raw = _text[start..];
                AddNode(new XmlCommentSyntax(raw.Length >= 4 ? raw[4..] : string.Empty, raw));
                _position = _text.Length;
                return;
            }

            var rawComment = _text[start..(end + 3)];
            var innerText = rawComment[4..^3];
            AddNode(new XmlCommentSyntax(innerText, rawComment));
            _position = end + 3;
        }

        private void ParseCData()
        {
            var start = _position;
            _position += 9;
            var end = _text.IndexOf("]]>", _position, StringComparison.Ordinal);
            if (end < 0)
            {
                AddDiagnostic(start, _text.Length - start, "XML0009", "Unterminated CDATA section.");
                var raw = _text[start..];
                AddNode(new XmlCDataSectionSyntax(raw.Length >= 9 ? raw[9..] : string.Empty, raw));
                _position = _text.Length;
                return;
            }

            var rawCData = _text[start..(end + 3)];
            var innerText = rawCData[9..^3];
            AddNode(new XmlCDataSectionSyntax(innerText, rawCData));
            _position = end + 3;
        }

        private void ParseDeclaration()
        {
            var start = _position;
            _position += 5;
            var end = _text.IndexOf("?>", _position, StringComparison.Ordinal);
            if (end < 0)
            {
                AddDiagnostic(start, _text.Length - start, "XML0007", "Unterminated XML declaration.");
                var raw = _text[start..];
                AddNode(new XmlSkippedTextSyntax(raw));
                _position = _text.Length;
                return;
            }

            var rawDeclaration = _text[start..(end + 2)];
            var inner = rawDeclaration[5..^2];
            var attributes = ParsePseudoAttributes(inner);
            attributes.TryGetValue("version", out var version);
            attributes.TryGetValue("encoding", out var encoding);
            attributes.TryGetValue("standalone", out var standalone);
            AddNode(new XmlDeclarationSyntax(version ?? "1.0", encoding, standalone, rawDeclaration));
            _position = end + 2;
        }

        private void ParseProcessingInstruction()
        {
            var start = _position;
            _position += 2;
            var end = _text.IndexOf("?>", _position, StringComparison.Ordinal);
            if (end < 0)
            {
                AddDiagnostic(start, _text.Length - start, "XML0012", "Unterminated processing instruction.");
                var raw = _text[start..];
                AddNode(new XmlSkippedTextSyntax(raw));
                _position = _text.Length;
                return;
            }

            var rawInstruction = _text[start..(end + 2)];
            var inner = rawInstruction[2..^2].Trim();
            var firstSpaceIndex = inner.IndexOf(' ', StringComparison.Ordinal);
            string target;
            string? data;
            if (firstSpaceIndex < 0)
            {
                target = inner;
                data = null;
            }
            else
            {
                target = inner[..firstSpaceIndex];
                data = inner[(firstSpaceIndex + 1)..].Trim();
            }

            AddNode(new XmlProcessingInstructionSyntax(target, data, rawInstruction));
            _position = end + 2;
        }

        private void ParseDocumentType()
        {
            var start = _position;
            _position += 9;
            var depth = 0;
            var quote = '\0';
            while (!IsAtEnd)
            {
                var current = Current;
                if (quote != '\0')
                {
                    if (current == quote)
                        quote = '\0';
                }
                else
                {
                    if (current is '\'' or '"')
                    {
                        quote = current;
                    }
                    else if (current == '[')
                    {
                        depth++;
                    }
                    else if (current == ']')
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                    else if (current == '>' && depth == 0)
                    {
                        _position++;
                        break;
                    }
                }

                _position++;
            }

            if (_position > _text.Length)
                _position = _text.Length;

            var raw = _text[start.._position];
            if (!raw.EndsWith(">", StringComparison.Ordinal))
            {
                AddDiagnostic(start, raw.Length, "XML0011", "Unterminated document type declaration.");
            }

            var value = raw.Length > 9 ? raw[9..^1].Trim() : string.Empty;
            var name = value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            AddNode(new XmlDocumentTypeSyntax(name, value, raw));
        }

        private void ParseEndTag()
        {
            var start = _position;
            _position += 2;
            SkipTagWhitespace();
            var nameStart = _position;
            while (!IsAtEnd && IsNameCharacter(Current))
            {
                _position++;
            }

            var name = _text[nameStart.._position];
            while (!IsAtEnd && Current != '>')
            {
                _position++;
            }

            if (!IsAtEnd)
                _position++;

            var rawEndTag = _text[start.._position];
            if (_elementStack.Count == 0)
            {
                AddDiagnostic(start, rawEndTag.Length, "XML0002", $"Unexpected end tag '{name}'.");
                AddNode(new XmlSkippedTextSyntax(rawEndTag));
                return;
            }

            var top = _elementStack.Peek();
            if (string.Equals(top.Name, name, StringComparison.Ordinal))
            {
                _ = _elementStack.Pop();
                var endTag = new XmlEndTagSyntax(name, rawEndTag);
                var fullText = _text[top.Start.._position];
                var element = new XmlElementSyntax(
                    top.Name,
                    top.Attributes,
                    top.Content,
                    endTag,
                    isSelfClosing: false,
                    fullText,
                    top.StartTagText);

                AddNode(element);
                return;
            }

            AddDiagnostic(start, rawEndTag.Length, "XML0002", $"Mismatched end tag '{name}'.");
            AddNode(new XmlSkippedTextSyntax(rawEndTag));
        }

        private void ParseElementOrSkippedText()
        {
            var start = _position;
            _position++;
            if (IsAtEnd || !IsNameStartCharacter(Current))
            {
                ParseSkippedText(start, "Invalid start tag.");
                return;
            }

            var nameStart = _position;
            while (!IsAtEnd && IsNameCharacter(Current))
            {
                _position++;
            }

            var name = _text[nameStart.._position];
            var attributes = new List<XmlAttributeSyntax>();
            var isSelfClosing = false;

            while (!IsAtEnd)
            {
                SkipTagWhitespace();
                if (Match("/>"))
                {
                    _position += 2;
                    isSelfClosing = true;
                    break;
                }

                if (Match(">"))
                {
                    _position++;
                    break;
                }

                var attributeStart = _position;
                if (!IsNameStartCharacter(Current))
                {
                    ParseSkippedText(start, "Invalid attribute in start tag.");
                    return;
                }

                while (!IsAtEnd && IsNameCharacter(Current))
                {
                    _position++;
                }

                var attributeName = _text[attributeStart.._position];
                SkipTagWhitespace();
                string attributeValue = string.Empty;
                if (Match("="))
                {
                    _position++;
                    SkipTagWhitespace();
                    if (Current is '"' or '\'')
                    {
                        var quote = Current;
                        _position++;
                        var valueStart = _position;
                        while (!IsAtEnd && Current != quote)
                        {
                            _position++;
                        }

                        attributeValue = _text[valueStart.._position];
                        if (Current == quote)
                            _position++;
                    }
                    else
                    {
                        var valueStart = _position;
                        while (!IsAtEnd && !char.IsWhiteSpace(Current) && !Match("/>") && Current != '>')
                        {
                            _position++;
                        }

                        attributeValue = _text[valueStart.._position];
                    }
                }

                var rawAttribute = _text[attributeStart.._position];
                attributes.Add(new XmlAttributeSyntax(attributeName, attributeValue, rawAttribute));
            }

            var startTagText = _text[start.._position];
            if (isSelfClosing)
            {
                AddNode(new XmlElementSyntax(name, attributes, [], endTag: null, isSelfClosing: true, startTagText, startTagText));
                return;
            }

            _elementStack.Push(new ElementBuilder(start, name, attributes, startTagText));
        }

        private void ParseSkippedText(int start, string message)
        {
            while (!IsAtEnd && Current != '>')
            {
                _position++;
            }

            if (!IsAtEnd)
                _position++;

            var raw = _text[start.._position];
            AddDiagnostic(start, raw.Length, "XML0010", message);
            AddNode(new XmlSkippedTextSyntax(raw));
        }

        private void AddNode(XmlSyntaxNode node)
        {
            if (_elementStack.Count == 0)
            {
                _documentNodes.Add(node);
            }
            else
            {
                _elementStack.Peek().Content.Add(node);
            }
        }

        private void AddDiagnostic(int start, int length, string id, string message)
        {
            _diagnostics.Add(new XmlDiagnostic(id, message, XmlDiagnosticSeverity.Error, new TextSpan(start, length)));
        }

        private bool Match(string token)
        {
            if (_position + token.Length > _text.Length)
                return false;

            return string.Compare(_text, _position, token, 0, token.Length, StringComparison.Ordinal) == 0;
        }

        private bool MatchInsensitive(string token)
        {
            if (_position + token.Length > _text.Length)
                return false;

            return string.Compare(_text, _position, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private void SkipTagWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(Current))
            {
                _position++;
            }
        }

        private static bool IsNameStartCharacter(char value)
        {
            return char.IsLetter(value) || value is '_' or ':';
        }

        private static bool IsNameCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value is '_' or ':' or '-' or '.';
        }

        private static Dictionary<string, string> ParsePseudoAttributes(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (value.Length == 0)
                return result;

            var index = 0;
            while (index < value.Length)
            {
                while (index < value.Length && char.IsWhiteSpace(value[index]))
                {
                    index++;
                }

                var nameStart = index;
                while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] != '=')
                {
                    index++;
                }

                if (index == nameStart)
                    break;

                var name = value[nameStart..index];
                while (index < value.Length && char.IsWhiteSpace(value[index]))
                {
                    index++;
                }

                if (index >= value.Length || value[index] != '=')
                {
                    result[name] = string.Empty;
                    continue;
                }

                index++;
                while (index < value.Length && char.IsWhiteSpace(value[index]))
                {
                    index++;
                }

                if (index >= value.Length)
                {
                    result[name] = string.Empty;
                    break;
                }

                if (value[index] is '"' or '\'')
                {
                    var quote = value[index];
                    index++;
                    var valueStart = index;
                    while (index < value.Length && value[index] != quote)
                    {
                        index++;
                    }

                    result[name] = value[valueStart..Math.Min(index, value.Length)];
                    if (index < value.Length && value[index] == quote)
                        index++;
                }
                else
                {
                    var valueStart = index;
                    while (index < value.Length && !char.IsWhiteSpace(value[index]))
                    {
                        index++;
                    }

                    result[name] = value[valueStart..index];
                }
            }

            return result;
        }

        private sealed class ElementBuilder
        {
            public ElementBuilder(int start, string name, IReadOnlyList<XmlAttributeSyntax> attributes, string startTagText)
            {
                Start = start;
                Name = name;
                Attributes = attributes;
                StartTagText = startTagText;
            }

            public int Start { get; }
            public string Name { get; }
            public IReadOnlyList<XmlAttributeSyntax> Attributes { get; }
            public string StartTagText { get; }
            public List<XmlSyntaxNode> Content { get; } = [];
        }
    }
}
