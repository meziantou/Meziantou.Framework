using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using static Meziantou.Framework.Language.Xml.Syntax.InternalSyntax.XmlDiagnosticDescriptors;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Reads a document into green nodes, reporting what is wrong with it rather than throwing.</summary>
/// <remarks>
/// <para>
/// Whitespace is trivia only inside a tag. Between tags it is character data, which XML says is significant, so it
/// becomes an <see cref="XmlTextSyntax"/> of its own.
/// </para>
/// <para>
/// The checks are those of a non-validating processor: the well-formedness constraints of XML 1.0 and of Namespaces
/// in XML 1.0. The DTD is read only far enough to know where it ends and which general entities it declares.
/// </para>
/// <para>
/// Recovery aims to keep the shape the author meant. A tag missing its <c>&gt;</c> still opens its element, text a
/// tag cannot use becomes skipped-text trivia inside that tag, and an end tag that matches an element further up
/// closes the elements left open in between.
/// </para>
/// </remarks>
internal sealed class LanguageParser : ICharacterDataReporter
{
    private const string XmlNamespaceUri = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespaceUri = "http://www.w3.org/2000/xmlns/";

    private static readonly TagDiagnostics ElementTagDiagnostics = new(UnexpectedTextInStartTag, MissingAttributeValue, UnquotedAttributeValue, UnterminatedAttributeValue, MissingWhitespaceBeforeAttribute);
    private static readonly TagDiagnostics DeclarationDiagnostics = new(UnexpectedTextInDeclaration, DeclarationMissingAttributeValue, DeclarationUnquotedAttributeValue, DeclarationUnterminatedAttributeValue, DeclarationMissingWhitespaceBeforeAttribute);

    private readonly SourceText _source;
    private readonly string _text;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<GreenNode?> _documentNodes = [];
    private readonly Stack<ElementBuilder> _elementStack = new();
    private int _position;

    private bool _hasRootElement;
    private bool _hasDocumentType;
    private bool _hasMisplacedContent;
    private HashSet<string>? _declaredEntities;
    private bool _mayDeclareEntitiesElsewhere;

    public LanguageParser(SourceText source)
    {
        _source = source;
        _text = source.Text;
    }

    /// <summary>Gets everything wrong with the document, in the order it appears in the text.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public XmlDocumentSyntax ParseDocument()
    {
        ReportInvalidCharacters();

        while (!IsAtEnd)
        {
            if (Current != '<')
            {
                ParseText();
            }
            else if (Match("<!--"))
            {
                ParseComment();
            }
            else if (Match("<![CDATA["))
            {
                ParseCData();
            }
            else if (IsAtXmlDeclaration())
            {
                ParseDeclaration();
            }
            else if (Match("<?"))
            {
                ParseProcessingInstruction();
            }
            else if (MatchInsensitive("<!DOCTYPE"))
            {
                ParseDocumentType();
            }
            else if (Match("</"))
            {
                ParseEndTag();
            }
            else
            {
                ParseStartTagOrSkippedText();
            }
        }

        // An element the document never closed keeps everything read since its start tag, and is closed here without
        // an end tag so the text it covers is still accounted for.
        while (_elementStack.Count > 0)
        {
            CloseUnclosedElement();
        }

        // A document whose top level already holds something misplaced has been reported for that; saying the root
        // element is missing as well would only repeat it.
        if (!_hasRootElement && !_hasMisplacedContent)
        {
            AddDiagnostic(_text.Length, 0, MissingRootElement);
        }

        // The checks run in several passes, so the order they found things in is not the order of the text.
        var sorted = _diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToArray();
        _diagnostics.Clear();
        _diagnostics.AddRange(sorted);

        return new XmlDocumentSyntax(SyntaxFactory.List(CollectionsMarshal.AsSpan(_documentNodes)), SyntaxFactory.Token(SyntaxKind.EndOfFileToken));
    }

    private bool IsAtEnd => _position >= _text.Length;
    private char Current => _position < _text.Length ? _text[_position] : '\0';

    bool ICharacterDataReporter.IsEntityDeclared(string name) => _mayDeclareEntitiesElsewhere || (_declaredEntities?.Contains(name) ?? false);

    void ICharacterDataReporter.Report(int start, int length, DiagnosticDescriptor descriptor, params object?[] arguments) => AddDiagnostic(start, length, descriptor, arguments);

    /// <summary>Reports every run of UTF-16 units that is not an XML character, wherever it stands.</summary>
    private void ReportInvalidCharacters()
    {
        var index = 0;
        while (index < _text.Length)
        {
            var offset = CharacterData.IndexOfPossiblyInvalidCharacter(_text.AsSpan(index));
            if (offset < 0)
                return;

            index += offset;
            if (!IsInvalidUnit(index))
            {
                // Half of a well-formed surrogate pair, which is a character outside the basic plane.
                index += 2;
                continue;
            }

            var start = index;
            do
            {
                index++;
            }
            while (index < _text.Length && IsInvalidUnit(index));

            AddDiagnostic(start, index - start, InvalidCharacter, ((int)_text[start]).ToString("X4", CultureInfo.InvariantCulture));
        }
    }

    private bool IsInvalidUnit(int index)
    {
        var value = _text[index];
        if (char.IsHighSurrogate(value))
            return index + 1 >= _text.Length || !char.IsLowSurrogate(_text[index + 1]);

        if (char.IsLowSurrogate(value))
            return index == 0 || !char.IsHighSurrogate(_text[index - 1]);

        return !CharacterData.IsXmlCharacter(value);
    }

    private void ParseText()
    {
        var start = _position;
        var length = _text.AsSpan(_position).IndexOf('<');
        _position = length < 0 ? _text.Length : _position + length;

        // Outside the root element any text but whitespace is an error of its own, so what it holds is not examined.
        var value = CharacterData.Read(_text, start, _position, isAttribute: false, _elementStack.Count > 0 ? this : null);

        AddNode(new XmlTextSyntax(SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.TextToken, _text[start.._position], value)), start);
    }

    private void ParseComment()
    {
        var start = _position;
        var (textStart, textEnd, terminated) = ReadDelimited("<!--", "-->", UnterminatedComment);
        var endToken = terminated ? SyntaxFactory.Token(SyntaxKind.XmlCommentEndToken) : SyntaxFactory.MissingToken(SyntaxKind.XmlCommentEndToken);

        var doubleHyphen = _text.IndexOf("--", textStart, textEnd - textStart, StringComparison.Ordinal);
        if (doubleHyphen >= 0)
        {
            AddDiagnostic(doubleHyphen, 2, DoubleHyphenInComment);
        }
        else if (terminated && textEnd > textStart && _text[textEnd - 1] == '-')
        {
            AddDiagnostic(textEnd - 1, 4, HyphenBeforeCommentEnd);
        }

        AddNode(new XmlCommentSyntax(SyntaxFactory.Token(SyntaxKind.XmlCommentStartToken), SyntaxFactory.Token(leading: null, SyntaxKind.CommentToken, _text[textStart..textEnd]), endToken), start);
    }

    private void ParseCData()
    {
        var start = _position;
        var (textStart, textEnd, terminated) = ReadDelimited("<![CDATA[", "]]>", UnterminatedCData);
        var endToken = terminated ? SyntaxFactory.Token(SyntaxKind.CDataEndToken) : SyntaxFactory.MissingToken(SyntaxKind.CDataEndToken);

        AddNode(new XmlCDataSectionSyntax(SyntaxFactory.Token(SyntaxKind.CDataStartToken), SyntaxFactory.Token(leading: null, SyntaxKind.CDataToken, _text[textStart..textEnd]), endToken), start);
    }

    /// <summary>Reads past a construct written as an opening delimiter, free text, and a closing delimiter.</summary>
    private (int TextStart, int TextEnd, bool Terminated) ReadDelimited(string startText, string endText, DiagnosticDescriptor unterminated)
    {
        var start = _position;
        var textStart = _position + startText.Length;
        var end = _text.IndexOf(endText, textStart, StringComparison.Ordinal);
        if (end < 0)
        {
            _position = _text.Length;
            AddDiagnostic(start, _position - start, unterminated);

            return (textStart, _position, false);
        }

        _position = end + endText.Length;

        return (textStart, end, true);
    }

    /// <summary>Determines whether the reading position is an XML declaration rather than a processing instruction.</summary>
    /// <remarks>
    /// Only a target of exactly <c>xml</c> is one. <c>&lt;?xml-stylesheet</c> is an ordinary processing instruction, and
    /// taking it for a declaration would lose the rest of the document.
    /// </remarks>
    private bool IsAtXmlDeclaration()
    {
        if (!MatchInsensitive("<?xml"))
            return false;

        var next = _position + "<?xml".Length;

        return next >= _text.Length || SyntaxFacts.IsWhitespace(_text[next]) || _text[next] == '?';
    }

    private void ParseDeclaration()
    {
        var start = _position;
        _position += "<?xml".Length;
        var startText = _text[start.._position];
        var startToken = SyntaxFactory.Token(leading: null, SyntaxKind.LessThanQuestionXmlToken, startText);

        if (!string.Equals(startText, "<?xml", StringComparison.Ordinal))
        {
            AddDiagnostic(start, startText.Length, DeclarationCase, startText);
        }

        // A byte order mark decoded into the text is not content, so a declaration right after one is still first.
        if (start != 0 && !(start == 1 && _text[0] == '\uFEFF'))
        {
            AddDiagnostic(start, startText.Length, MisplacedDeclaration);
        }

        var attributes = new List<GreenNode?>();
        var infos = new List<AttributeInfo>();
        GreenToken endToken;
        while (true)
        {
            var tagEnd = _position;
            var trivia = LexTagTrivia(isDeclaration: true, "xml", out var followsSkippedText);
            if (Match("?>"))
            {
                _position += 2;
                endToken = SyntaxFactory.Token(trivia, SyntaxKind.QuestionGreaterThanToken);
                break;
            }

            if (IsAtEnd || Current == '<')
            {
                endToken = WithLeading(SyntaxFactory.MissingToken(SyntaxKind.QuestionGreaterThanToken), trivia);
                AddDiagnostic(start, tagEnd - start, UnterminatedDeclaration);
                break;
            }

            attributes.Add(ParseAttribute(trivia, DeclarationDiagnostics, followsSkippedText, out var info));
            infos.Add(info);
        }

        ValidateDeclaration(start, infos);
        AddNode(new XmlDeclarationSyntax(startToken, SyntaxFactory.List(CollectionsMarshal.AsSpan(attributes)), endToken), start);
    }

    /// <summary>Checks the pseudo-attributes: a version, then optionally an encoding, then optionally standalone.</summary>
    private void ValidateDeclaration(int start, List<AttributeInfo> attributes)
    {
        if (attributes.Count == 0 || !string.Equals(attributes[0].Name, "version", StringComparison.Ordinal))
        {
            AddDiagnostic(start, "<?xml".Length, MissingVersion);
        }

        var stage = 0;
        foreach (var attribute in attributes)
        {
            var order = attribute.Name switch
            {
                "version" => 1,
                "encoding" => 2,
                "standalone" => 3,
                _ => 0,
            };

            if (order <= stage)
            {
                AddDiagnostic(attribute.NameStart, attribute.Name.Length, UnexpectedDeclarationAttribute, attribute.Name);
                continue;
            }

            stage = order;
            if (!attribute.HasValue)
                continue;

            // Pseudo-attribute values are literal: a reference in one is not resolved, so the raw text is what counts.
            var value = attribute.RawValue;
            switch (order)
            {
                case 1 when !IsVersionNumber(value):
                    AddDiagnostic(attribute.ValueStart, value.Length, InvalidVersion, value);
                    break;

                case 2 when !IsEncodingName(value):
                    AddDiagnostic(attribute.ValueStart, value.Length, InvalidEncoding, value);
                    break;

                case 3 when value is not ("yes" or "no"):
                    AddDiagnostic(attribute.ValueStart, value.Length, InvalidStandalone, value);
                    break;
            }
        }

        static bool IsVersionNumber(string value)
            => value.Length > 2 && value.StartsWith("1.", StringComparison.Ordinal) && !value.AsSpan(2).ContainsAnyExceptInRange('0', '9');

        static bool IsEncodingName(string value)
        {
            if (value.Length == 0 || !char.IsAsciiLetter(value[0]))
                return false;

            foreach (var character in value.AsSpan(1))
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
                    return false;
            }

            return true;
        }
    }

    private void ParseProcessingInstruction()
    {
        var start = _position;
        _position += 2;
        var end = _text.IndexOf("?>", _position, StringComparison.Ordinal);
        var terminated = end >= 0;
        var limit = terminated ? end : _text.Length;

        var startToken = SyntaxFactory.Token(SyntaxKind.LessThanQuestionToken);
        var nameTrivia = LexWhitespaceTrivia(limit);
        var nameStart = _position;
        if (IsAtNameStart())
        {
            SkipNameCharacters(limit);
        }

        var target = _text[nameStart.._position];
        var nameToken = target.Length > 0
            ? SyntaxFactory.Token(nameTrivia, SyntaxKind.IdentifierToken, target)
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken), nameTrivia);

        if (target.Length == 0 || nameTrivia is not null)
        {
            AddDiagnostic(start, Math.Max(2, nameStart - start), MissingProcessingInstructionTarget);
        }

        if (string.Equals(target, "xml", StringComparison.OrdinalIgnoreCase))
        {
            AddDiagnostic(nameStart, target.Length, ReservedProcessingInstructionTarget, target);
        }
        else if (target.Contains(':', StringComparison.Ordinal))
        {
            AddDiagnostic(nameStart, target.Length, ColonInProcessingInstructionTarget);
        }

        // The data is everything up to the closing delimiter, except the whitespace separating it from the target.
        var dataTrivia = LexWhitespaceTrivia(limit);
        var dataStart = _position;
        if (target.Length > 0 && dataTrivia is null && dataStart < limit)
        {
            AddDiagnostic(nameStart, target.Length, MissingWhitespaceAfterTarget);
        }

        var dataToken = dataStart < limit
            ? SyntaxFactory.Token(dataTrivia, SyntaxKind.ProcessingInstructionDataToken, _text[dataStart..limit])
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.ProcessingInstructionDataToken), dataTrivia);

        GreenToken endToken;
        if (terminated)
        {
            _position = end + 2;
            endToken = SyntaxFactory.Token(SyntaxKind.QuestionGreaterThanToken);
        }
        else
        {
            _position = _text.Length;
            endToken = SyntaxFactory.MissingToken(SyntaxKind.QuestionGreaterThanToken);
            AddDiagnostic(start, _position - start, UnterminatedProcessingInstruction);
        }

        AddNode(new XmlProcessingInstructionSyntax(startToken, nameToken, dataToken, endToken), start);
    }

    private void ParseDocumentType()
    {
        var start = _position;
        _position += "<!DOCTYPE".Length;
        var startText = _text[start.._position];
        var startToken = SyntaxFactory.Token(leading: null, SyntaxKind.DocumentTypeStartToken, startText);

        if (!string.Equals(startText, "<!DOCTYPE", StringComparison.Ordinal))
        {
            AddDiagnostic(start, startText.Length, DocumentTypeCase, startText);
        }

        if (_elementStack.Count > 0)
        {
            AddDiagnostic(start, startText.Length, DocumentTypeInsideElement);
        }
        else if (_hasRootElement)
        {
            AddDiagnostic(start, startText.Length, DocumentTypeAfterRootElement);
        }
        else if (_hasDocumentType)
        {
            AddDiagnostic(start, startText.Length, DuplicateDocumentType);
        }

        _hasDocumentType = true;
        _declaredEntities ??= new(StringComparer.Ordinal);

        var trivia = LexWhitespaceTrivia();
        var nameStart = _position;
        SkipNameCharacters();

        var name = _text[nameStart.._position];
        var nameToken = name.Length > 0
            ? SyntaxFactory.Token(trivia, SyntaxKind.IdentifierToken, name)
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken), trivia);

        if (name.Length == 0)
        {
            AddDiagnostic(start, startText.Length, MissingDocumentTypeName);
        }
        else if (trivia is null)
        {
            AddDiagnostic(start, startText.Length, MissingWhitespaceInDocumentType);
        }

        // The internal subset may hold quoted text, nested brackets, comments and processing instructions, and a '>'
        // inside any of them does not end the declaration -- nor does a quote inside a comment start a literal.
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

                _position++;
                continue;
            }

            if (depth > 0 && current == '<')
            {
                if (Match("<!--"))
                {
                    SkipPast("-->", "<!--".Length);
                    continue;
                }

                if (Match("<?"))
                {
                    SkipPast("?>", "<?".Length);
                    continue;
                }

                if (Match("<!ENTITY") && _position + "<!ENTITY".Length < _text.Length && SyntaxFacts.IsWhitespace(_text[_position + "<!ENTITY".Length]))
                {
                    _position += "<!ENTITY".Length;
                    _ = LexWhitespaceTrivia();

                    // A parameter entity, written with a '%', is not one a document's content can refer to.
                    var entityNameStart = _position;
                    if (IsAtNameStart())
                    {
                        SkipNameCharacters();
                        _declaredEntities.Add(_text[entityNameStart.._position]);
                    }

                    continue;
                }
            }

            if (depth > 0 && current == '%' && CharacterData.TryReadScalar(_text, _position + 1, _text.Length, out var scalar, out _) && SyntaxFacts.IsNameStartCharacter(scalar))
            {
                // A parameter entity reference can pull in declarations from anywhere.
                _mayDeclareEntitiesElsewhere = true;
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
            else if (depth == 0 && current == '>')
            {
                terminated = true;
                break;
            }
            else if (depth == 0 && current == '<')
            {
                // Outside the internal subset a '<' can only be the next tag, so the declaration was never closed.
                break;
            }

            _position++;
        }

        var content = _text.AsSpan(contentStart, _position - contentStart).TrimStart(" \t\r\n");
        if (content.StartsWith("SYSTEM", StringComparison.Ordinal) || content.StartsWith("PUBLIC", StringComparison.Ordinal))
        {
            // Declarations in the external subset cannot be seen without reading it.
            _mayDeclareEntitiesElsewhere = true;
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
            AddDiagnostic(start, _position - start, UnterminatedDocumentType);
            greaterThanToken = SyntaxFactory.MissingToken(SyntaxKind.GreaterThanToken);
        }

        AddNode(new XmlDocumentTypeSyntax(startToken, nameToken, contentToken, greaterThanToken), start);
    }

    private void SkipPast(string terminator, int openingLength)
    {
        var end = _text.IndexOf(terminator, _position + openingLength, StringComparison.Ordinal);
        _position = end < 0 ? _text.Length : end + terminator.Length;
    }

    private void ParseEndTag()
    {
        var start = _position;
        _position += 2;
        var lessThanSlashToken = SyntaxFactory.Token(SyntaxKind.LessThanSlashToken);

        var trivia = LexWhitespaceTrivia();
        var nameStart = _position;
        SkipNameCharacters();

        var name = _text[nameStart.._position];
        var nameToken = name.Length > 0
            ? SyntaxFactory.Token(trivia, SyntaxKind.IdentifierToken, name)
            : WithLeading(SyntaxFactory.MissingToken(SyntaxKind.IdentifierToken), trivia);

        // What is wrong with the tag itself is reported only when the tag goes on to close an element. One that does
        // not is skipped as a whole, and saying so covers it.
        PendingDiagnostic? problem = trivia is not null && name.Length > 0 ? new(start + 2, nameStart - start - 2, WhitespaceBeforeEndTagName, []) : null;

        // Whatever a malformed tag puts between the name and the '>' is kept rather than dropped, but it stops at a
        // '<', which can only be the next tag.
        var skippedTrivia = LexWhitespaceTrivia();
        var skippedStart = _position;
        while (!IsAtEnd && Current is not ('>' or '<'))
        {
            _position++;
        }

        GreenNode? skippedTokens = null;
        if (_position > skippedStart)
        {
            var skipped = _text[skippedStart.._position];
            skippedTokens = SyntaxFactory.Token(skippedTrivia, SyntaxKind.BadToken, skipped).AsSkippedText();
            problem ??= new(skippedStart, skipped.Length, UnexpectedTextInEndTag, [skipped, name]);
        }

        // Trivia the skipped tokens did not claim belongs to the '>', missing or not, so no character is lost.
        var greaterThanTrivia = skippedTokens is null ? skippedTrivia : null;
        GreenToken greaterThanToken;
        if (IsAtEnd || Current == '<')
        {
            problem ??= new(start, _position - start, UnterminatedEndTag, [name]);
            greaterThanToken = WithLeading(SyntaxFactory.MissingToken(SyntaxKind.GreaterThanToken), greaterThanTrivia);
        }
        else
        {
            _position++;
            greaterThanToken = SyntaxFactory.Token(greaterThanTrivia, SyntaxKind.GreaterThanToken);
        }

        var depth = name.Length > 0 ? FindOpenElement(name) : -1;
        if (depth < 0)
        {
            if (name.Length == 0)
            {
                AddDiagnostic(start, _position - start, MissingEndTagName);
            }
            else if (_elementStack.Count == 0)
            {
                AddDiagnostic(start, _position - start, UnexpectedEndTag, name);
            }
            else
            {
                AddDiagnostic(start, _position - start, MismatchedEndTag, name, _elementStack.Peek().Name);
            }

            AddNode(SkippedText(start, _position), start);

            return;
        }

        if (problem is { } pending)
        {
            AddDiagnostic(pending.Start, pending.Length, pending.Descriptor, pending.Arguments);
        }

        // The end tag belongs to an element further up, so the ones opened since were never closed.
        for (var i = 0; i < depth; i++)
        {
            CloseUnclosedElement();
        }

        var element = _elementStack.Pop();
        var endTag = new XmlElementEndTagSyntax(lessThanSlashToken, nameToken, skippedTokens, greaterThanToken);
        AddNode(new XmlElementSyntax(element.StartTag, SyntaxFactory.List(CollectionsMarshal.AsSpan(element.Content)), endTag), element.Start);
    }

    /// <summary>Returns how many open elements stand above the innermost one called <paramref name="name"/>, or -1.</summary>
    private int FindOpenElement(string name)
    {
        var depth = 0;
        foreach (var element in _elementStack)
        {
            if (string.Equals(element.Name, name, StringComparison.Ordinal))
                return depth;

            depth++;
        }

        return -1;
    }

    private void CloseUnclosedElement()
    {
        var unclosed = _elementStack.Pop();
        AddDiagnostic(unclosed.Start, unclosed.StartTag.FullWidth, MissingEndTag, unclosed.Name);
        AddNode(new XmlElementSyntax(unclosed.StartTag, SyntaxFactory.List(CollectionsMarshal.AsSpan(unclosed.Content)), endTag: null), unclosed.Start);
    }

    private void ParseStartTagOrSkippedText()
    {
        var start = _position;
        _position++;
        if (!IsAtNameStart())
        {
            // Not a tag. The text runs to a '>', but not past a '<', which is the next tag rather than part of this one.
            while (!IsAtEnd && Current is not ('>' or '<'))
            {
                _position++;
            }

            if (!IsAtEnd && Current == '>')
            {
                _position++;
            }

            var isLoneLessThan = start + 1 >= _text.Length || SyntaxFacts.IsWhitespace(_text[start + 1]);
            AddDiagnostic(start, _position - start, isLoneLessThan ? UnescapedLessThan : InvalidStartTag);
            AddNode(SkippedText(start, _position), start);

            return;
        }

        var lessThanToken = SyntaxFactory.Token(SyntaxKind.LessThanToken);
        var nameStart = _position;
        SkipNameCharacters();

        var name = _text[nameStart.._position];
        var nameToken = SyntaxFactory.Token(leading: null, SyntaxKind.IdentifierToken, name);
        var attributes = new List<GreenNode?>();
        var infos = new List<AttributeInfo>();

        while (true)
        {
            var tagEnd = _position;
            var trivia = LexTagTrivia(isDeclaration: false, name, out var followsSkippedText);
            if (Match("/>"))
            {
                _position += 2;
                _ = ValidateElement(nameStart, name, infos);
                AddNode(new XmlEmptyElementSyntax(lessThanToken, nameToken, SyntaxFactory.List(CollectionsMarshal.AsSpan(attributes)), SyntaxFactory.Token(trivia, SyntaxKind.SlashGreaterThanToken)), start);

                return;
            }

            if (IsAtEnd || Current is '>' or '<')
            {
                GreenToken greaterThanToken;
                if (Current == '>' && !IsAtEnd)
                {
                    _position++;
                    greaterThanToken = SyntaxFactory.Token(trivia, SyntaxKind.GreaterThanToken);
                }
                else
                {
                    // The tag was never closed, but it still opens its element: what follows is most likely its content.
                    AddDiagnostic(start, tagEnd - start, UnterminatedStartTag, name);
                    greaterThanToken = WithLeading(SyntaxFactory.MissingToken(SyntaxKind.GreaterThanToken), trivia);
                }

                var scope = ValidateElement(nameStart, name, infos);
                var startTag = new XmlElementStartTagSyntax(lessThanToken, nameToken, SyntaxFactory.List(CollectionsMarshal.AsSpan(attributes)), greaterThanToken);
                _elementStack.Push(new ElementBuilder(start, name, startTag, scope));

                return;
            }

            attributes.Add(ParseAttribute(trivia, ElementTagDiagnostics, followsSkippedText, out var info));
            infos.Add(info);
        }
    }

    private XmlAttributeSyntax ParseAttribute(GreenNode? leadingTrivia, TagDiagnostics diagnostics, bool followsSkippedText, out AttributeInfo info)
    {
        var nameStart = _position;
        SkipNameCharacters();

        var name = _text[nameStart.._position];
        var nameToken = SyntaxFactory.Token(leadingTrivia, SyntaxKind.IdentifierToken, name);

        // Skipped text right in front of the name has been reported already, and is the more useful thing to say.
        if (!followsSkippedText && nameStart > 0 && !SyntaxFacts.IsWhitespace(_text[nameStart - 1]))
        {
            AddDiagnostic(nameStart, name.Length, diagnostics.MissingWhitespace, name);
        }

        var nameEnd = _position;
        var equalsTrivia = LexWhitespaceTrivia();
        if (IsAtEnd || Current != '=')
        {
            // An attribute with no value at all. Rewinding leaves the whitespace for whatever comes next to claim.
            _position = nameEnd;
            AddDiagnostic(nameStart, name.Length, diagnostics.MissingValue, name);
            info = new AttributeInfo(nameStart, name, nameEnd, RawValue: "", Value: "", HasValue: false);

            return new XmlAttributeSyntax(
                nameToken,
                SyntaxFactory.MissingToken(SyntaxKind.EqualsToken),
                SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken),
                SyntaxFactory.MissingToken(SyntaxKind.AttributeValueToken),
                SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken));
        }

        _position++;
        var equalsToken = SyntaxFactory.Token(equalsTrivia, SyntaxKind.EqualsToken);
        var equalsEnd = _position;
        var valueTrivia = LexWhitespaceTrivia();

        if (!IsAtEnd && Current is '"' or '\'')
        {
            var quote = Current;
            var quoteKind = quote == '"' ? SyntaxKind.DoubleQuoteToken : SyntaxKind.SingleQuoteToken;
            _position++;
            var valueStart = _position;

            // A value runs to its closing quote. One that meets what looks like the next tag first was most likely
            // never closed, and stopping there keeps the rest of the document out of it.
            while (!IsAtEnd && Current != quote && !IsAtMarkupStart())
            {
                _position++;
            }

            var raw = _text[valueStart.._position];
            var value = CharacterData.Read(_text, valueStart, _position, isAttribute: true, this);
            var valueToken = SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.AttributeValueToken, raw, value);
            GreenToken endQuoteToken;
            if (!IsAtEnd && Current == quote)
            {
                _position++;
                endQuoteToken = SyntaxFactory.Token(quoteKind);
            }
            else
            {
                AddDiagnostic(valueStart - 1, raw.Length + 1, diagnostics.UnterminatedValue, name);
                endQuoteToken = SyntaxFactory.MissingToken(quoteKind);
            }

            info = new AttributeInfo(nameStart, name, valueStart, raw, value, HasValue: true);

            return new XmlAttributeSyntax(nameToken, equalsToken, SyntaxFactory.Token(valueTrivia, quoteKind), valueToken, endQuoteToken);
        }

        var unquotedStart = _position;
        while (!IsAtEnd && !SyntaxFacts.IsWhitespace(Current) && Current is not ('>' or '<') && !Match("/>") && !Match("?>"))
        {
            _position++;
        }

        if (_position == unquotedStart)
        {
            // Nothing that could be a value. Rewinding leaves the whitespace for whatever comes next to claim.
            _position = equalsEnd;
            AddDiagnostic(nameStart, name.Length, diagnostics.MissingValue, name);
            info = new AttributeInfo(nameStart, name, equalsEnd, RawValue: "", Value: "", HasValue: false);

            return new XmlAttributeSyntax(
                nameToken,
                equalsToken,
                SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken),
                SyntaxFactory.MissingToken(SyntaxKind.AttributeValueToken),
                SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken));
        }

        var unquoted = _text[unquotedStart.._position];
        AddDiagnostic(unquotedStart, unquoted.Length, diagnostics.UnquotedValue, name);
        var unquotedValue = CharacterData.Read(_text, unquotedStart, _position, isAttribute: true, this);
        info = new AttributeInfo(nameStart, name, unquotedStart, unquoted, unquotedValue, HasValue: true);

        return new XmlAttributeSyntax(
            nameToken,
            equalsToken,
            WithLeading(SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken), valueTrivia),
            SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.AttributeValueToken, unquoted, unquotedValue),
            SyntaxFactory.MissingToken(SyntaxKind.DoubleQuoteToken));
    }

    /// <summary>
    /// Checks the constraints a start tag can break once all of it is read: unique attributes, and the Namespaces in
    /// XML rules for its names and declarations.
    /// </summary>
    /// <returns>The namespaces in scope for the element's content.</returns>
    private NamespaceScope? ValidateElement(int nameStart, string name, List<AttributeInfo> attributes)
    {
        var parentScope = _elementStack.Count > 0 ? _elementStack.Peek().Scope : null;
        Dictionary<string, string>? bindings = null;

        // Most tags have a handful of attributes, where comparing each pair costs less than hashing them.
        var seenNames = attributes.Count > 8 ? new HashSet<string>(StringComparer.Ordinal) : null;
        for (var i = 0; i < attributes.Count; i++)
        {
            var attribute = attributes[i];
            if (seenNames is null ? IsNameUsedBefore(attributes, i) : !seenNames.Add(attribute.Name))
            {
                AddDiagnostic(attribute.NameStart, attribute.Name.Length, DuplicateAttribute, attribute.Name);
            }

            if (!ValidateQualifiedNameSyntax(attribute.NameStart, attribute.Name) || !TryGetNamespaceDeclarationPrefix(attribute.Name, out var prefix) || !attribute.HasValue)
                continue;

            var namespaceUri = attribute.Value;
            if (string.Equals(prefix, "xmlns", StringComparison.Ordinal))
            {
                AddDiagnostic(attribute.NameStart, attribute.Name.Length, XmlnsPrefixDeclaration);
                continue;
            }

            if (string.Equals(prefix, "xml", StringComparison.Ordinal))
            {
                if (!string.Equals(namespaceUri, XmlNamespaceUri, StringComparison.Ordinal))
                {
                    AddDiagnostic(attribute.NameStart, attribute.Name.Length, XmlPrefixBinding);
                }

                continue;
            }

            if (namespaceUri is XmlNamespaceUri or XmlnsNamespaceUri)
            {
                AddDiagnostic(attribute.NameStart, attribute.Name.Length, ReservedNamespace, namespaceUri, attribute.Name);
            }
            else if (prefix.Length > 0 && namespaceUri.Length == 0)
            {
                AddDiagnostic(attribute.NameStart, attribute.Name.Length, EmptyPrefixedNamespace, prefix);
            }

            bindings ??= new(StringComparer.Ordinal);
            bindings[prefix] = namespaceUri;
        }

        var scope = bindings is null ? parentScope : new NamespaceScope(parentScope, bindings);

        if (ValidateQualifiedNameSyntax(nameStart, name) && TryGetPrefix(name, out var elementPrefix))
        {
            if (string.Equals(elementPrefix, "xmlns", StringComparison.Ordinal))
            {
                AddDiagnostic(nameStart, name.Length, XmlnsElementPrefix);
            }
            else if (ResolvePrefix(scope, elementPrefix) is null)
            {
                AddDiagnostic(nameStart, elementPrefix.Length, UndeclaredPrefix, elementPrefix);
            }
        }

        // Two attributes can share a namespace and a local name under different prefixes.
        Dictionary<(string NamespaceUri, string LocalName), string>? qualified = null;
        foreach (var attribute in attributes)
        {
            if (TryGetNamespaceDeclarationPrefix(attribute.Name, out _) || !IsQualifiedName(attribute.Name) || !TryGetPrefix(attribute.Name, out var attributePrefix))
                continue;

            var namespaceUri = ResolvePrefix(scope, attributePrefix);
            if (namespaceUri is null)
            {
                AddDiagnostic(attribute.NameStart, attributePrefix.Length, UndeclaredPrefix, attributePrefix);
                continue;
            }

            // An attribute repeated under the same prefix has been reported as a duplicate already.
            var expandedName = (namespaceUri, attribute.Name[(attributePrefix.Length + 1)..]);
            qualified ??= [];
            if (!qualified.TryAdd(expandedName, attribute.Name) && !string.Equals(qualified[expandedName], attribute.Name, StringComparison.Ordinal))
            {
                AddDiagnostic(attribute.NameStart, attribute.Name.Length, DuplicateExpandedAttribute, qualified[expandedName], attribute.Name);
            }
        }

        return scope;

        static bool IsNameUsedBefore(List<AttributeInfo> attributes, int index)
        {
            for (var i = 0; i < index; i++)
            {
                if (string.Equals(attributes[i].Name, attributes[index].Name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Reports a name that is not a <c>QName</c>: at most one colon, with a name on each side of it.</summary>
    private bool ValidateQualifiedNameSyntax(int start, string name)
    {
        if (IsQualifiedName(name))
            return true;

        AddDiagnostic(start, name.Length, InvalidQualifiedName, name);

        return false;
    }

    private static bool IsQualifiedName(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return true;

        if (colon == 0 || colon == name.Length - 1 || name.AsSpan(colon + 1).Contains(':'))
            return false;

        return CharacterData.TryReadScalar(name, colon + 1, name.Length, out var scalar, out _) && SyntaxFacts.IsNameStartCharacter(scalar);
    }

    private static bool TryGetPrefix(string name, out string prefix)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        prefix = colon > 0 ? name[..colon] : "";

        return colon > 0;
    }

    private static bool TryGetNamespaceDeclarationPrefix(string name, out string prefix)
    {
        if (string.Equals(name, "xmlns", StringComparison.Ordinal))
        {
            prefix = "";

            return true;
        }

        if (name.StartsWith("xmlns:", StringComparison.Ordinal))
        {
            prefix = name["xmlns:".Length..];

            return true;
        }

        prefix = "";

        return false;
    }

    private static string? ResolvePrefix(NamespaceScope? scope, string prefix)
        => string.Equals(prefix, "xml", StringComparison.Ordinal) ? XmlNamespaceUri : scope?.Resolve(prefix);

    private XmlSkippedTextSyntax SkippedText(int start, int end)
        => new(SyntaxFactory.BadToken(leading: null, _text[start..end]));

    /// <summary>Determines whether an XML name begins at the reading position.</summary>
    private bool IsAtNameStart() => CharacterData.TryReadScalar(_text, _position, _text.Length, out var scalar, out _) && SyntaxFacts.IsNameStartCharacter(scalar);

    /// <summary>Determines whether the reading position is a '&lt;' that starts a tag, a comment, or the like.</summary>
    private bool IsAtMarkupStart()
    {
        if (Current != '<' || _position + 1 >= _text.Length)
            return false;

        var next = _text[_position + 1];

        return next is '/' or '!' or '?' || (CharacterData.TryReadScalar(_text, _position + 1, _text.Length, out var scalar, out _) && SyntaxFacts.IsNameStartCharacter(scalar));
    }

    /// <summary>Advances past the name characters at the reading position.</summary>
    /// <remarks>
    /// A name character may sit outside the basic plane, so the scan moves in scalar values rather than in UTF-16
    /// units. It does not require the first one to be a name <em>start</em> character: a tag whose name begins with a
    /// digit is malformed, and reading it anyway is what keeps the document round-tripping.
    /// </remarks>
    private void SkipNameCharacters(int limit = int.MaxValue)
    {
        var end = Math.Min(limit, _text.Length);
        while (_position < end && CharacterData.TryReadScalar(_text, _position, end, out var scalar, out var length) && SyntaxFacts.IsNameCharacter(scalar))
        {
            _position += length;
        }
    }

    /// <summary>Claims the trivia at the reading position inside a tag: whitespace, and any text the tag cannot use.</summary>
    /// <remarks>
    /// Text is skipped up to whatever the tag can use again: whitespace, a name, the end of the tag, or a '&lt;',
    /// which starts the next tag. Keeping it as trivia keeps the element and everything else in its tag.
    /// </remarks>
    private GreenNode? LexTagTrivia(bool isDeclaration, string tagName, out bool endsWithSkippedText)
    {
        GreenNode? single = null;
        List<GreenNode?>? pieces = null;
        endsWithSkippedText = false;
        while (true)
        {
            if (LexWhitespace(ref single, ref pieces, _text.Length))
            {
                endsWithSkippedText = false;
            }

            if (IsAtEnd || IsAtTagContinuation(isDeclaration))
                break;

            var skippedStart = _position;
            do
            {
                _position++;
            }
            while (!IsAtEnd && !SyntaxFacts.IsWhitespace(Current) && !IsAtTagContinuation(isDeclaration));

            var skipped = _text[skippedStart.._position];
            if (isDeclaration)
            {
                AddDiagnostic(skippedStart, skipped.Length, UnexpectedTextInDeclaration, skipped);
            }
            else
            {
                AddDiagnostic(skippedStart, skipped.Length, UnexpectedTextInStartTag, skipped, tagName);
            }

            Append(ref single, ref pieces, SyntaxFactory.Trivia(SyntaxKind.SkippedTextTrivia, skipped).AsSkippedText());
            endsWithSkippedText = true;
        }

        return pieces is null ? single : SyntaxFactory.List(CollectionsMarshal.AsSpan(pieces));
    }

    private bool IsAtTagContinuation(bool isDeclaration)
    {
        if (Current == '<' || IsAtNameStart())
            return true;

        return isDeclaration ? Match("?>") : Current == '>' || Match("/>");
    }

    /// <summary>Claims the whitespace at the reading position, which inside a tag is trivia.</summary>
    private GreenNode? LexWhitespaceTrivia(int limit = int.MaxValue)
    {
        GreenNode? single = null;
        List<GreenNode?>? pieces = null;
        _ = LexWhitespace(ref single, ref pieces, Math.Min(limit, _text.Length));

        return pieces is null ? single : SyntaxFactory.List(CollectionsMarshal.AsSpan(pieces));
    }

    private bool LexWhitespace(ref GreenNode? single, ref List<GreenNode?>? pieces, int end)
    {
        var start = _position;
        while (_position < end && SyntaxFacts.IsWhitespace(_text[_position]))
        {
            var pieceStart = _position;
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
                while (_position < end && _text[_position] is ' ' or '\t')
                {
                    _position++;
                }
            }

            Append(ref single, ref pieces, SyntaxFactory.Trivia(kind, _text[pieceStart.._position]));
        }

        return _position > start;
    }

    private static void Append(ref GreenNode? single, ref List<GreenNode?>? pieces, GreenNode item)
    {
        if (single is null && pieces is null)
        {
            single = item;
        }
        else
        {
            pieces ??= [single];
            pieces.Add(item);
        }
    }

    /// <summary>Puts trivia in front of a token that was built without any, such as a missing one.</summary>
    private static GreenToken WithLeading(GreenToken token, GreenNode? leading)
        => leading is null ? token : new GreenToken(token.RawKind, token.Text, leading, trailingTrivia: null, isMissing: token.IsMissing);

    private void AddNode(GreenNode node, int start)
    {
        if (_elementStack.Count > 0)
        {
            _elementStack.Peek().Content.Add(node);

            return;
        }

        _documentNodes.Add(node);
        switch ((SyntaxKind)node.RawKind)
        {
            case SyntaxKind.XmlElement or SyntaxKind.XmlEmptyElement:
                if (_hasRootElement)
                {
                    var isEmpty = node.RawKind == (int)SyntaxKind.XmlEmptyElement;
                    var startTag = isEmpty ? node : node.GetSlot(0)!;
                    var name = ((GreenToken)startTag.GetSlot(1)!).Text;
                    AddDiagnostic(start, startTag.FullWidth, MultipleRootElements, name);
                }

                _hasRootElement = true;
                break;

            case SyntaxKind.XmlText:
                ReportTextOutsideRootElement(start, start + node.FullWidth);
                break;

            case SyntaxKind.XmlCDataSection:
                AddDiagnostic(start, node.FullWidth, CDataOutsideRootElement);
                _hasMisplacedContent = true;
                break;

            case SyntaxKind.XmlSkippedText:
                _hasMisplacedContent = true;
                break;
        }
    }

    /// <summary>Reports what a run of text between top-level nodes holds besides whitespace, which is all it may hold.</summary>
    private void ReportTextOutsideRootElement(int start, int end)
    {
        var first = start;
        while (first < end && (SyntaxFacts.IsWhitespace(_text[first]) || (first == 0 && _text[first] == '\uFEFF')))
        {
            first++;
        }

        if (first == end)
            return;

        var last = end;
        while (SyntaxFacts.IsWhitespace(_text[last - 1]))
        {
            last--;
        }

        AddDiagnostic(first, last - first, TextOutsideRootElement);
        _hasMisplacedContent = true;
    }

    private void AddDiagnostic(int start, int length, DiagnosticDescriptor descriptor, params object?[] arguments)
        => _diagnostics.Add(Diagnostic.Create(descriptor, new Location(new TextSpan(start, length), _source), arguments));

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

    private readonly record struct AttributeInfo(int NameStart, string Name, int ValueStart, string RawValue, string Value, bool HasValue);

    private readonly record struct PendingDiagnostic(int Start, int Length, DiagnosticDescriptor Descriptor, object?[] Arguments);

    private sealed record TagDiagnostics(
        DiagnosticDescriptor UnexpectedText,
        DiagnosticDescriptor MissingValue,
        DiagnosticDescriptor UnquotedValue,
        DiagnosticDescriptor UnterminatedValue,
        DiagnosticDescriptor MissingWhitespace);

    /// <summary>The prefixes an element declares, chained to the ones it inherits.</summary>
    private sealed class NamespaceScope(NamespaceScope? parent, Dictionary<string, string> bindings)
    {
        public NamespaceScope? Parent { get; } = parent;
        public Dictionary<string, string> Bindings { get; } = bindings;

        public string? Resolve(string prefix)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope.Bindings.TryGetValue(prefix, out var namespaceUri))
                    return namespaceUri;
            }

            return null;
        }
    }

    private sealed class ElementBuilder(int start, string name, GreenNode startTag, NamespaceScope? scope)
    {
        public int Start { get; } = start;
        public string Name { get; } = name;
        public GreenNode StartTag { get; } = startTag;
        public NamespaceScope? Scope { get; } = scope;
        public List<GreenNode?> Content { get; } = [];
    }
}
