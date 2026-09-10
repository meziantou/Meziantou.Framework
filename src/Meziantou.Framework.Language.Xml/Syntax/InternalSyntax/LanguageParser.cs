using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Reads a document into green nodes, reporting what is wrong with it rather than throwing.</summary>
/// <remarks>
/// Whitespace is trivia only inside a tag. Between tags it is character data, which XML says is significant, so it
/// becomes an <see cref="XmlTextSyntax"/> of its own.
/// </remarks>
internal sealed class LanguageParser
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<GreenNode?> _documentNodes = [];
    private readonly Stack<ElementBuilder> _elementStack = new();
    private int _position;

    public LanguageParser(SourceText source)
    {
        _source = source;
        _text = source.Text;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public XmlDocumentSyntax ParseDocument()
    {
        while (!IsAtEnd)
        {
            if (Current != '<')
            {
                AddNode(ParseText());
            }
            else if (Match("<!--"))
            {
                AddNode(ParseComment());
            }
            else if (Match("<![CDATA["))
            {
                AddNode(ParseCData());
            }
            else if (MatchInsensitive("<?xml"))
            {
                AddNode(ParseDeclaration());
            }
            else if (Match("<?"))
            {
                AddNode(ParseProcessingInstruction());
            }
            else if (MatchInsensitive("<!DOCTYPE"))
            {
                AddNode(ParseDocumentType());
            }
            else if (Match("</"))
            {
                ParseEndTag();
            }
            else
            {
                ParseElementOrSkippedText();
            }
        }

        // An element the document never closed keeps everything read since its start tag, and is closed here without
        // an end tag so the text it covers is still accounted for.
        while (_elementStack.Count > 0)
        {
            var unclosed = _elementStack.Pop();
            AddDiagnostic(unclosed.Start, _text.Length - unclosed.Start, "XML0001", $"Missing end tag for '{unclosed.Name}'.");
            AddNode(new XmlElementSyntax(unclosed.StartTag, SyntaxFactory.List(CollectionsMarshal.AsSpan(unclosed.Content)), endTag: null));
        }

        return new XmlDocumentSyntax(SyntaxFactory.List(CollectionsMarshal.AsSpan(_documentNodes)), SyntaxFactory.Token(SyntaxKind.EndOfFileToken));
    }

    private bool IsAtEnd => _position >= _text.Length;
    private char Current => _position < _text.Length ? _text[_position] : '\0';

    private XmlTextSyntax ParseText()
    {
        var start = _position;
        while (!IsAtEnd && Current != '<')
        {
            _position++;
        }

        return new XmlTextSyntax(SyntaxFactory.Token(leading: null, SyntaxKind.TextToken, _text[start.._position]));
    }

    private XmlCommentSyntax ParseComment() => ParseDelimited(
        SyntaxKind.XmlCommentStartToken, SyntaxKind.CommentToken, SyntaxKind.XmlCommentEndToken,
        "XML0008", "Unterminated XML comment.",
        static (start, text, end) => new XmlCommentSyntax(start, text, end));

    private XmlCDataSectionSyntax ParseCData() => ParseDelimited(
        SyntaxKind.CDataStartToken, SyntaxKind.CDataToken, SyntaxKind.CDataEndToken,
        "XML0009", "Unterminated CDATA section.",
        static (start, text, end) => new XmlCDataSectionSyntax(start, text, end));

    /// <summary>Reads a construct written as an opening delimiter, free text, and a closing delimiter.</summary>
    private TNode ParseDelimited<TNode>(SyntaxKind startKind, SyntaxKind textKind, SyntaxKind endKind, string id, string message, Func<GreenNode, GreenNode, GreenNode, TNode> create)
    {
        var start = _position;
        var startText = SyntaxFacts.GetText(startKind);
        var endText = SyntaxFacts.GetText(endKind);
        _position += startText.Length;

        var textStart = _position;
        var end = _text.IndexOf(endText, _position, StringComparison.Ordinal);
        if (end < 0)
        {
            _position = _text.Length;
            AddDiagnostic(start, _position - start, id, message);

            return create(SyntaxFactory.Token(startKind), SyntaxFactory.Token(leading: null, textKind, _text[textStart.._position]), SyntaxFactory.MissingToken(endKind));
        }

        _position = end + endText.Length;

        return create(SyntaxFactory.Token(startKind), SyntaxFactory.Token(leading: null, textKind, _text[textStart..end]), SyntaxFactory.Token(endKind));
    }

    private XmlNodeSyntax ParseDeclaration()
    {
        var start = _position;
        _position += "<?xml".Length;
        var startToken = SyntaxFactory.Token(leading: null, SyntaxKind.LessThanQuestionXmlToken, _text[start.._position]);

        var attributes = new List<GreenNode?>();
        while (true)
        {
            var trivia = LexTagTrivia();
            if (Match("?>"))
            {
                _position += 2;

                return new XmlDeclarationSyntax(startToken, SyntaxFactory.List(CollectionsMarshal.AsSpan(attributes)), SyntaxFactory.Token(trivia, SyntaxKind.QuestionGreaterThanToken));
            }

            if (!IsAtNameStart())
            {
                // Nothing here can be read as a pseudo-attribute, so the whole declaration is kept as skipped text.
                _position = _text.Length;
                AddDiagnostic(start, _position - start, "XML0007", "Unterminated XML declaration.");

                return SkippedText(start, _position);
            }

            attributes.Add(ParseAttribute(trivia));
        }
    }

    private XmlNodeSyntax ParseProcessingInstruction()
    {
        var start = _position;
        _position += 2;
        var end = _text.IndexOf("?>", _position, StringComparison.Ordinal);
        if (end < 0)
        {
            _position = _text.Length;
            AddDiagnostic(start, _position - start, "XML0012", "Unterminated processing instruction.");

            return SkippedText(start, _position);
        }

        var startToken = SyntaxFactory.Token(SyntaxKind.LessThanQuestionToken);
        var trivia = LexTagTrivia(limit: end);
        var nameStart = _position;
        SkipNameCharacters(end);

        var nameToken = _position > nameStart
            ? SyntaxFactory.Token(trivia, SyntaxKind.IdentifierToken, _text[nameStart.._position])
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken), trivia);

        // Everything up to the closing delimiter is the data, kept exactly as written so the text comes back whole.
        var dataToken = SyntaxFactory.Token(leading: null, SyntaxKind.ProcessingInstructionDataToken, _text[_position..end]);
        _position = end + 2;

        return new XmlProcessingInstructionSyntax(startToken, nameToken, dataToken, SyntaxFactory.Token(SyntaxKind.QuestionGreaterThanToken));
    }

    private XmlDocumentTypeSyntax ParseDocumentType()
    {
        var start = _position;
        _position += "<!DOCTYPE".Length;
        var startToken = SyntaxFactory.Token(leading: null, SyntaxKind.DocumentTypeStartToken, _text[start.._position]);

        var trivia = LexTagTrivia();
        var nameStart = _position;
        SkipNameCharacters();

        var nameToken = _position > nameStart
            ? SyntaxFactory.Token(trivia, SyntaxKind.IdentifierToken, _text[nameStart.._position])
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken), trivia);

        // The internal subset may hold both quoted text and nested brackets, and a '>' inside either does not end
        // the declaration.
        var contentStart = _position;
        var depth = 0;
        var quote = '\0';
        var terminated = false;
        while (!IsAtEnd)
        {
            var current = Current;
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }
            }
            else if (current is '\'' or '"')
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
                terminated = true;
                break;
            }

            _position++;
        }

        var contentToken = SyntaxFactory.Token(leading: null, SyntaxKind.DocumentTypeContentToken, _text[contentStart.._position]);
        GreenToken greaterThanToken;
        if (terminated)
        {
            _position++;
            greaterThanToken = SyntaxFactory.Token(SyntaxKind.GreaterThanToken);
        }
        else
        {
            AddDiagnostic(start, _position - start, "XML0011", "Unterminated document type declaration.");
            greaterThanToken = SyntaxFactory.MissingToken(SyntaxKind.GreaterThanToken);
        }

        return new XmlDocumentTypeSyntax(startToken, nameToken, contentToken, greaterThanToken);
    }

    private void ParseEndTag()
    {
        var start = _position;
        _position += 2;
        var lessThanSlashToken = SyntaxFactory.Token(SyntaxKind.LessThanSlashToken);

        var trivia = LexTagTrivia();
        var nameStart = _position;
        SkipNameCharacters();

        var name = _text[nameStart.._position];
        var nameToken = name.Length > 0
            ? SyntaxFactory.Token(trivia, SyntaxKind.IdentifierToken, name)
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken), trivia);

        // Whatever a malformed tag puts between the name and the '>' is kept rather than dropped.
        var skippedTrivia = LexTagTrivia();
        var skippedStart = _position;
        while (!IsAtEnd && Current != '>')
        {
            _position++;
        }

        GreenNode? skippedTokens = _position > skippedStart
            ? SyntaxFactory.Token(skippedTrivia, SyntaxKind.BadToken, _text[skippedStart.._position]).AsSkippedText()
            : null;

        // Trivia the skipped tokens did not claim belongs to the '>', missing or not, so no character is lost.
        var greaterThanTrivia = skippedTokens is null ? skippedTrivia : null;
        GreenToken greaterThanToken;
        if (IsAtEnd)
        {
            greaterThanToken = WithLeading(SyntaxFactory.MissingToken(SyntaxKind.GreaterThanToken), greaterThanTrivia);
        }
        else
        {
            _position++;
            greaterThanToken = SyntaxFactory.Token(greaterThanTrivia, SyntaxKind.GreaterThanToken);
        }

        if (_elementStack.Count == 0)
        {
            AddDiagnostic(start, _position - start, "XML0002", $"Unexpected end tag '{name}'.");
            AddNode(SkippedText(start, _position));

            return;
        }

        var top = _elementStack.Peek();
        if (!string.Equals(top.Name, name, StringComparison.Ordinal))
        {
            AddDiagnostic(start, _position - start, "XML0002", $"Mismatched end tag '{name}'.");
            AddNode(SkippedText(start, _position));

            return;
        }

        _ = _elementStack.Pop();
        var endTag = new XmlElementEndTagSyntax(lessThanSlashToken, nameToken, skippedTokens, greaterThanToken);
        AddNode(new XmlElementSyntax(top.StartTag, SyntaxFactory.List(CollectionsMarshal.AsSpan(top.Content)), endTag));
    }

    private void ParseElementOrSkippedText()
    {
        var start = _position;
        _position++;
        if (!IsAtNameStart())
        {
            AddNode(ParseSkippedText(start, "Invalid start tag."));

            return;
        }

        var lessThanToken = SyntaxFactory.Token(SyntaxKind.LessThanToken);
        var nameStart = _position;
        SkipNameCharacters();

        var name = _text[nameStart.._position];
        var nameToken = SyntaxFactory.Token(leading: null, SyntaxKind.IdentifierToken, name);
        var attributes = new List<GreenNode?>();

        while (true)
        {
            var trivia = LexTagTrivia();
            if (Match("/>"))
            {
                _position += 2;
                AddNode(new XmlEmptyElementSyntax(lessThanToken, nameToken, SyntaxFactory.List(CollectionsMarshal.AsSpan(attributes)), SyntaxFactory.Token(trivia, SyntaxKind.SlashGreaterThanToken)));

                return;
            }

            if (Match(">"))
            {
                _position++;
                var startTag = new XmlElementStartTagSyntax(lessThanToken, nameToken, SyntaxFactory.List(CollectionsMarshal.AsSpan(attributes)), SyntaxFactory.Token(trivia, SyntaxKind.GreaterThanToken));
                _elementStack.Push(new ElementBuilder(start, name, startTag));

                return;
            }

            if (!IsAtNameStart())
            {
                // The tag cannot be read any further, so the whole of it is kept as skipped text -- which is what the
                // attributes read so far become part of again.
                _position = start;
                AddNode(ParseSkippedText(start, "Invalid attribute in start tag."));

                return;
            }

            attributes.Add(ParseAttribute(trivia));
        }
    }

    private XmlAttributeSyntax ParseAttribute(GreenNode? leadingTrivia)
    {
        var nameStart = _position;
        SkipNameCharacters();

        var nameToken = SyntaxFactory.Token(leadingTrivia, SyntaxKind.IdentifierToken, _text[nameStart.._position]);

        var equalsTrivia = LexTagTrivia();
        if (Current != '=')
        {
            // An attribute with no value at all. Rewinding leaves the whitespace for whatever comes next to claim.
            _position = nameStart + nameToken.Text.Length;

            return new XmlAttributeSyntax(
                nameToken,
                SyntaxFactory.MissingToken(SyntaxKind.EqualsToken),
                SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken),
                SyntaxFactory.MissingToken(SyntaxKind.AttributeValueToken),
                SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken));
        }

        _position++;
        var equalsToken = SyntaxFactory.Token(equalsTrivia, SyntaxKind.EqualsToken);
        var valueTrivia = LexTagTrivia();

        if (Current is '"' or '\'')
        {
            var quote = Current;
            var quoteKind = quote == '"' ? SyntaxKind.DoubleQuoteToken : SyntaxKind.SingleQuoteToken;
            _position++;
            var valueStart = _position;
            while (!IsAtEnd && Current != quote)
            {
                _position++;
            }

            var valueToken = SyntaxFactory.Token(leading: null, SyntaxKind.AttributeValueToken, _text[valueStart.._position]);
            GreenToken endQuoteToken;
            if (Current == quote)
            {
                _position++;
                endQuoteToken = SyntaxFactory.Token(quoteKind);
            }
            else
            {
                endQuoteToken = SyntaxFactory.MissingToken(quoteKind);
            }

            return new XmlAttributeSyntax(nameToken, equalsToken, SyntaxFactory.Token(valueTrivia, quoteKind), valueToken, endQuoteToken);
        }

        var unquotedStart = _position;
        while (!IsAtEnd && !char.IsWhiteSpace(Current) && !Match("/>") && Current != '>')
        {
            _position++;
        }

        return new XmlAttributeSyntax(
            nameToken,
            equalsToken,
            WithLeading(SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken), valueTrivia),
            SyntaxFactory.Token(leading: null, SyntaxKind.AttributeValueToken, _text[unquotedStart.._position]),
            SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken));
    }

    private XmlSkippedTextSyntax ParseSkippedText(int start, string message)
    {
        _position = start;
        while (!IsAtEnd && Current != '>')
        {
            _position++;
        }

        if (!IsAtEnd)
        {
            _position++;
        }

        AddDiagnostic(start, _position - start, "XML0010", message);

        return SkippedText(start, _position);
    }

    private XmlSkippedTextSyntax SkippedText(int start, int end)
        => new(SyntaxFactory.BadToken(leading: null, _text[start..end]));

    /// <summary>Determines whether an XML name begins at the reading position.</summary>
    private bool IsAtNameStart() => TryReadScalar(_position, out var scalar, out _) && SyntaxFacts.IsNameStartCharacter(scalar);

    /// <summary>Advances past the name characters at the reading position.</summary>
    /// <remarks>
    /// A name character may sit outside the basic plane, so the scan moves in scalar values rather than in UTF-16
    /// units. It does not require the first one to be a name <em>start</em> character: a tag whose name begins with a
    /// digit is malformed, and reading it anyway is what keeps the document round-tripping.
    /// </remarks>
    private void SkipNameCharacters(int limit = int.MaxValue)
    {
        var end = Math.Min(limit, _text.Length);
        while (_position < end && TryReadScalar(_position, out var scalar, out var length) && _position + length <= end && SyntaxFacts.IsNameCharacter(scalar))
        {
            _position += length;
        }
    }

    /// <summary>Reads the scalar value at <paramref name="position"/>, and how many UTF-16 units it took.</summary>
    /// <remarks>A lone surrogate is not a scalar value at all, so it can be no part of a name.</remarks>
    private bool TryReadScalar(int position, out Rune scalar, out int length)
    {
        if (position >= _text.Length)
        {
            scalar = default;
            length = 0;

            return false;
        }

        var first = _text[position];
        if (!char.IsSurrogate(first))
        {
            scalar = new Rune(first);
            length = 1;

            return true;
        }

        if (position + 1 < _text.Length && Rune.TryCreate(first, _text[position + 1], out scalar))
        {
            length = 2;

            return true;
        }

        scalar = default;
        length = 0;

        return false;
    }

    /// <summary>Claims the whitespace at the reading position, which inside a tag is trivia.</summary>
    private GreenNode? LexTagTrivia(int limit = int.MaxValue)
    {
        var end = Math.Min(limit, _text.Length);
        if (_position >= end || !char.IsWhiteSpace(Current))
            return null;

        List<GreenNode?>? trivia = null;
        GreenNode? single = null;
        while (_position < end && char.IsWhiteSpace(_text[_position]))
        {
            var start = _position;
            SyntaxKind kind;
            if (_text[_position] is '\r' or '\n')
            {
                kind = SyntaxKind.EndOfLineTrivia;
                if (_text[_position] == '\r' && _position + 1 < end && _text[_position + 1] == '\n')
                {
                    _position++;
                }

                _position++;
            }
            else
            {
                kind = SyntaxKind.WhitespaceTrivia;
                while (_position < end && char.IsWhiteSpace(_text[_position]) && _text[_position] is not ('\r' or '\n'))
                {
                    _position++;
                }
            }

            var item = SyntaxFactory.Trivia(kind, _text[start.._position]);
            if (single is null && trivia is null)
            {
                single = item;
            }
            else
            {
                trivia ??= [single];
                trivia.Add(item);
            }
        }

        return trivia is null ? single : SyntaxFactory.List(CollectionsMarshal.AsSpan(trivia));
    }

    /// <summary>Puts trivia in front of a token that was built without any, such as a missing one.</summary>
    private static GreenToken WithLeading(GreenToken token, GreenNode? leading)
        => leading is null ? token : new GreenToken(token.RawKind, token.Text, leading, trailingTrivia: null, isMissing: token.IsMissing);

    private void AddNode(GreenNode node)
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
        => _diagnostics.Add(new Diagnostic(id, message, DiagnosticSeverity.Error, new Location(new TextSpan(start, length), _source)));

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

    private sealed class ElementBuilder(int start, string name, GreenNode startTag)
    {
        public int Start { get; } = start;
        public string Name { get; } = name;
        public GreenNode StartTag { get; } = startTag;
        public List<GreenNode?> Content { get; } = [];
    }
}
