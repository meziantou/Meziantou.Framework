namespace Meziantou.Framework.Language.Json;

/// <summary>Represents an immutable JSON syntax tree with source text and diagnostics.</summary>
public sealed class JsonSyntaxTree
{
    private JsonSyntaxTree(string text, JsonDocumentSyntax root, IReadOnlyList<JsonDiagnostic> diagnostics)
    {
        Text = text;
        SourceText = SourceText.From(text);
        Root = root;
        Diagnostics = diagnostics;
        Root.SetParentAndTree(parent: null, this);
    }

    public string Text { get; }
    public SourceText SourceText { get; }
    public JsonDocumentSyntax Root { get; }
    public IReadOnlyList<JsonDiagnostic> Diagnostics { get; }

    public JsonDocumentSyntax GetRoot() => Root;
    public IReadOnlyList<JsonDiagnostic> GetDiagnostics() => Diagnostics;

    public static JsonSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Json)] string text)
    {
        var parser = new JsonParser(text ?? string.Empty);

        return parser.Parse();
    }

    public JsonSyntaxTree WithChanges(params JsonTextChange[] changes) => WithChanges((IEnumerable<JsonTextChange>)changes);

    public JsonSyntaxTree WithChanges(IEnumerable<JsonTextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ParseText(SourceText.WithChanges(changes).Text);
    }

    public IReadOnlyList<JsonTextChange> GetChanges(JsonSyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);
        if (string.Equals(Text, oldTree.Text, StringComparison.Ordinal))
            return [];

        return [new JsonTextChange(new TextSpan(0, oldTree.Text.Length), Text)];
    }

    public bool IsEquivalentTo(JsonSyntaxTree? other)
    {
        if (other is null)
            return false;

        return string.Equals(Text, other.Text, StringComparison.Ordinal);
    }

    private sealed class JsonParser
    {
        private readonly string _text;
        private readonly List<JsonDiagnostic> _diagnostics = [];
        private readonly List<JsonSyntaxToken> _tokens;
        private int _position;

        public JsonParser(string text)
        {
            _text = text ?? string.Empty;
            var lexer = new JsonLexer(_text);
            _tokens = lexer.Lex();
            _diagnostics.AddRange(lexer.Diagnostics);
        }

        public JsonSyntaxTree Parse()
        {
            var childNodes = new List<JsonSyntaxNode>();
            var hasRootValue = false;
            while (Current.Kind != JsonSyntaxKind.EndOfFileToken)
            {
                if (hasRootValue)
                {
                    AddDiagnostic(Current.FullSpan, "JSON0010", "Unexpected data after the root JSON value.");
                }

                if (Current.Kind == JsonSyntaxKind.BadToken)
                {
                    childNodes.Add(ParseSkippedTextUntil(JsonSyntaxKind.EndOfFileToken));
                    continue;
                }

                var value = ParseValue();
                childNodes.Add(value);
                hasRootValue = true;

                if (value.FullSpan.Length == 0 && Current.Kind != JsonSyntaxKind.EndOfFileToken)
                {
                    childNodes.Add(ParseSkippedTextUntil(JsonSyntaxKind.EndOfFileToken));
                }
            }

            var endOfFileToken = ConsumeToken();
            var root = new JsonDocumentSyntax(childNodes, endOfFileToken, _text);

            return new JsonSyntaxTree(_text, root, _diagnostics);
        }

        private JsonSyntaxToken Current => Peek(0);

        private JsonSyntaxToken Peek(int offset)
        {
            var index = _position + offset;
            if (index >= _tokens.Count)
                return _tokens[^1];

            return _tokens[index];
        }

        private JsonSyntaxToken ConsumeToken()
        {
            var current = Current;
            _position++;

            return current;
        }

        private JsonSyntaxToken ConsumeToken(JsonSyntaxKind kind, string diagnosticId, string message)
        {
            if (Current.Kind == kind)
                return ConsumeToken();

            AddDiagnostic(Current.FullSpan, diagnosticId, message);

            return MissingToken(kind, Current.FullSpan.Start);
        }

        private JsonValueSyntax ParseValue()
        {
            switch (Current.Kind)
            {
                case JsonSyntaxKind.OpenBraceToken:
                    return ParseObject();
                case JsonSyntaxKind.OpenBracketToken:
                    return ParseArray();
                case JsonSyntaxKind.StringToken:
                    return new JsonStringSyntax(ConsumeToken());
                case JsonSyntaxKind.NumberToken:
                    return new JsonNumberSyntax(ConsumeToken());
                case JsonSyntaxKind.TrueKeyword:
                case JsonSyntaxKind.FalseKeyword:
                case JsonSyntaxKind.NullKeyword:
                    return new JsonLiteralSyntax(ConsumeToken());
                case JsonSyntaxKind.BadToken:
                    return ParseSkippedTextUntil(JsonSyntaxKind.CommaToken, JsonSyntaxKind.CloseBraceToken, JsonSyntaxKind.CloseBracketToken, JsonSyntaxKind.EndOfFileToken);
                default:
                    AddDiagnostic(Current.FullSpan, "JSON0007", "Expected a JSON value.");

                    return new JsonSkippedTextSyntax([], Current.FullSpan.Start);
            }
        }

        private JsonObjectSyntax ParseObject()
        {
            var openBraceToken = ConsumeToken(JsonSyntaxKind.OpenBraceToken, "JSON0006", "Expected '{'.");
            var childNodes = new List<JsonSyntaxNode>();

            while (Current.Kind is not JsonSyntaxKind.EndOfFileToken and not JsonSyntaxKind.CloseBraceToken)
            {
                if (Current.Kind == JsonSyntaxKind.CommaToken)
                {
                    AddDiagnostic(Current.FullSpan, "JSON0005", "Unexpected comma.");
                    var comma = ConsumeToken();
                    childNodes.Add(new JsonSkippedTextSyntax([comma], comma.FullSpan.Start));
                    continue;
                }

                if (Current.Kind == JsonSyntaxKind.BadToken)
                {
                    childNodes.Add(ParseSkippedTextUntil(JsonSyntaxKind.CommaToken, JsonSyntaxKind.CloseBraceToken, JsonSyntaxKind.EndOfFileToken));
                    continue;
                }

                var member = ParseMember();
                if (Current.Kind == JsonSyntaxKind.CommaToken)
                {
                    member = member.WithCommaToken(ConsumeToken());
                    childNodes.Add(member);
                    continue;
                }

                childNodes.Add(member);

                if (Current.Kind is JsonSyntaxKind.CloseBraceToken or JsonSyntaxKind.EndOfFileToken)
                    continue;

                AddDiagnostic(Current.FullSpan, "JSON0009", "Expected a comma or the end of the object.");
            }

            var closeBraceToken = Current.Kind == JsonSyntaxKind.CloseBraceToken
                ? ConsumeToken()
                : ConsumeToken(JsonSyntaxKind.CloseBraceToken, "JSON0006", "Expected '}'.");

            return new JsonObjectSyntax(openBraceToken, childNodes, closeBraceToken);
        }

        private JsonMemberSyntax ParseMember()
        {
            JsonSyntaxToken nameToken;
            if (Current.Kind == JsonSyntaxKind.StringToken)
            {
                nameToken = ConsumeToken();
            }
            else
            {
                AddDiagnostic(Current.FullSpan, "JSON0008", "Expected a JSON property name.");
                nameToken = MissingToken(JsonSyntaxKind.StringToken, Current.FullSpan.Start);
            }

            var colonToken = ConsumeToken(JsonSyntaxKind.ColonToken, "JSON0006", "Expected ':'.");
            var value = ParseValue();

            return new JsonMemberSyntax(nameToken, colonToken, value);
        }

        private JsonArraySyntax ParseArray()
        {
            var openBracketToken = ConsumeToken(JsonSyntaxKind.OpenBracketToken, "JSON0006", "Expected '['.");
            var childNodes = new List<JsonSyntaxNode>();

            while (Current.Kind is not JsonSyntaxKind.EndOfFileToken and not JsonSyntaxKind.CloseBracketToken)
            {
                if (Current.Kind == JsonSyntaxKind.CommaToken)
                {
                    AddDiagnostic(Current.FullSpan, "JSON0007", "Expected a JSON value.");
                    var missingValue = new JsonSkippedTextSyntax([], Current.FullSpan.Start);
                    childNodes.Add(new JsonArrayElementSyntax(missingValue, ConsumeToken()));
                    continue;
                }

                if (Current.Kind == JsonSyntaxKind.BadToken)
                {
                    childNodes.Add(ParseSkippedTextUntil(JsonSyntaxKind.CommaToken, JsonSyntaxKind.CloseBracketToken, JsonSyntaxKind.EndOfFileToken));
                    continue;
                }

                var value = ParseValue();
                var element = new JsonArrayElementSyntax(value);
                if (Current.Kind == JsonSyntaxKind.CommaToken)
                {
                    element = element.WithCommaToken(ConsumeToken());
                    childNodes.Add(element);
                    continue;
                }

                childNodes.Add(element);

                if (Current.Kind is JsonSyntaxKind.CloseBracketToken or JsonSyntaxKind.EndOfFileToken)
                    continue;

                AddDiagnostic(Current.FullSpan, "JSON0009", "Expected a comma or the end of the array.");
            }

            var closeBracketToken = Current.Kind == JsonSyntaxKind.CloseBracketToken
                ? ConsumeToken()
                : ConsumeToken(JsonSyntaxKind.CloseBracketToken, "JSON0006", "Expected ']'.");

            return new JsonArraySyntax(openBracketToken, childNodes, closeBracketToken);
        }

        private JsonSkippedTextSyntax ParseSkippedTextUntil(params JsonSyntaxKind[] stopKinds)
        {
            var tokens = new List<JsonSyntaxToken>();
            var fullStart = Current.FullSpan.Start;
            while (Current.Kind != JsonSyntaxKind.EndOfFileToken && !stopKinds.Contains(Current.Kind))
            {
                AddDiagnostic(Current.FullSpan, "JSON0005", $"Unexpected token '{Current.Text}'.");
                tokens.Add(ConsumeToken());
            }

            if (tokens.Count == 0 && Current.Kind == JsonSyntaxKind.BadToken)
            {
                AddDiagnostic(Current.FullSpan, "JSON0005", $"Unexpected token '{Current.Text}'.");
                tokens.Add(ConsumeToken());
            }

            return new JsonSkippedTextSyntax(tokens, fullStart);
        }

        private void AddDiagnostic(TextSpan span, string id, string message)
        {
            _diagnostics.Add(new JsonDiagnostic(id, message, JsonDiagnosticSeverity.Error, span));
        }

        private static JsonSyntaxToken MissingToken(JsonSyntaxKind kind, int position)
        {
            return new JsonSyntaxToken(kind, string.Empty, valueText: string.Empty, isMissing: true, fullStart: position);
        }
    }

    private sealed class JsonLexer
    {
        private readonly string _text;
        private readonly List<JsonDiagnostic> _diagnostics = [];
        private int _position;

        public JsonLexer(string text)
        {
            _text = text ?? string.Empty;
        }

        public IReadOnlyList<JsonDiagnostic> Diagnostics => _diagnostics;

        public List<JsonSyntaxToken> Lex()
        {
            var tokens = new List<JsonSyntaxToken>();
            while (true)
            {
                var token = ReadToken();
                tokens.Add(token);
                if (token.Kind == JsonSyntaxKind.EndOfFileToken)
                    break;
            }

            return tokens;
        }

        private bool IsAtEnd => _position >= _text.Length;
        private char Current => _position < _text.Length ? _text[_position] : '\0';
        private char LookAhead => _position + 1 < _text.Length ? _text[_position + 1] : '\0';

        private JsonSyntaxToken ReadToken()
        {
            var leadingTrivia = ReadLeadingTrivia();
            var fullStart = leadingTrivia.Count > 0 ? leadingTrivia[0].Span.Start : _position;

            if (IsAtEnd)
                return new JsonSyntaxToken(JsonSyntaxKind.EndOfFileToken, string.Empty, leadingTrivia: leadingTrivia, fullStart: fullStart);

            var tokenStart = _position;
            switch (Current)
            {
                case '{':
                    _position++;
                    return CreateToken(JsonSyntaxKind.OpenBraceToken, tokenStart, leadingTrivia, fullStart);
                case '}':
                    _position++;
                    return CreateToken(JsonSyntaxKind.CloseBraceToken, tokenStart, leadingTrivia, fullStart);
                case '[':
                    _position++;
                    return CreateToken(JsonSyntaxKind.OpenBracketToken, tokenStart, leadingTrivia, fullStart);
                case ']':
                    _position++;
                    return CreateToken(JsonSyntaxKind.CloseBracketToken, tokenStart, leadingTrivia, fullStart);
                case ':':
                    _position++;
                    return CreateToken(JsonSyntaxKind.ColonToken, tokenStart, leadingTrivia, fullStart);
                case ',':
                    _position++;
                    return CreateToken(JsonSyntaxKind.CommaToken, tokenStart, leadingTrivia, fullStart);
                case '"':
                    return ReadStringToken(leadingTrivia, fullStart);
                case '-' or >= '0' and <= '9':
                    return ReadNumberToken(leadingTrivia, fullStart);
                case >= 'a' and <= 'z':
                case >= 'A' and <= 'Z':
                case '_':
                    return ReadIdentifierOrBadToken(leadingTrivia, fullStart);
                default:
                    return ReadBadToken(leadingTrivia, fullStart);
            }
        }

        private List<JsonSyntaxTrivia> ReadLeadingTrivia()
        {
            List<JsonSyntaxTrivia>? trivia = null;
            while (!IsAtEnd)
            {
                var start = _position;
                if (Current is ' ' or '\t' or '\f' or '\v')
                {
                    while (!IsAtEnd && (Current is ' ' or '\t' or '\f' or '\v'))
                    {
                        _position++;
                    }

                    AddTrivia(ref trivia, JsonSyntaxKind.WhitespaceTrivia, start);
                    continue;
                }

                if (Current is '\r' or '\n')
                {
                    _position += GetLineBreakLength(_text, _position);
                    AddTrivia(ref trivia, JsonSyntaxKind.EndOfLineTrivia, start);
                    continue;
                }

                if (Current == '/' && LookAhead == '/')
                {
                    _position += 2;
                    while (!IsAtEnd && Current is not '\r' and not '\n')
                    {
                        _position++;
                    }

                    AddTrivia(ref trivia, JsonSyntaxKind.SingleLineCommentTrivia, start);
                    continue;
                }

                if (Current == '/' && LookAhead == '*')
                {
                    _position += 2;
                    while (!IsAtEnd && !(Current == '*' && LookAhead == '/'))
                    {
                        _position++;
                    }

                    if (IsAtEnd)
                    {
                        AddDiagnostic(start, _text.Length - start, "JSON0001", "Unterminated block comment.");
                        AddTrivia(ref trivia, JsonSyntaxKind.MultiLineCommentTrivia, start);
                        continue;
                    }

                    _position += 2;
                    AddTrivia(ref trivia, JsonSyntaxKind.MultiLineCommentTrivia, start);
                    continue;
                }

                break;
            }

            return trivia ?? [];
        }

        private JsonSyntaxToken ReadStringToken(IReadOnlyList<JsonSyntaxTrivia> leadingTrivia, int fullStart)
        {
            var start = _position;
            var valueBuilder = new StringBuilder();
            _position++;
            var isTerminated = false;

            while (!IsAtEnd)
            {
                var current = Current;
                if (current == '"')
                {
                    _position++;
                    isTerminated = true;
                    break;
                }

                if (current == '\\')
                {
                    ReadEscapeSequence(valueBuilder, start);
                    continue;
                }

                if (current is '\r' or '\n')
                {
                    AddDiagnostic(_position, GetLineBreakLength(_text, _position), "JSON0011", "Line breaks are not allowed in JSON strings.");
                }

                valueBuilder.Append(current);
                _position++;
            }

            if (!isTerminated)
            {
                AddDiagnostic(start, _text.Length - start, "JSON0002", "Unterminated string literal.");
            }

            var text = _text[start.._position];

            return new JsonSyntaxToken(JsonSyntaxKind.StringToken, text, valueBuilder.ToString(), leadingTrivia: leadingTrivia, fullStart: fullStart);
        }

        private void ReadEscapeSequence(StringBuilder valueBuilder, int stringStart)
        {
            var escapeStart = _position;
            _position++;
            if (IsAtEnd)
            {
                AddDiagnostic(stringStart, _text.Length - stringStart, "JSON0002", "Unterminated string literal.");
                return;
            }

            switch (Current)
            {
                case '"':
                    valueBuilder.Append('"');
                    _position++;
                    break;
                case '\\':
                    valueBuilder.Append('\\');
                    _position++;
                    break;
                case '/':
                    valueBuilder.Append('/');
                    _position++;
                    break;
                case 'b':
                    valueBuilder.Append('\b');
                    _position++;
                    break;
                case 'f':
                    valueBuilder.Append('\f');
                    _position++;
                    break;
                case 'n':
                    valueBuilder.Append('\n');
                    _position++;
                    break;
                case 'r':
                    valueBuilder.Append('\r');
                    _position++;
                    break;
                case 't':
                    valueBuilder.Append('\t');
                    _position++;
                    break;
                case 'u':
                    ReadUnicodeEscape(valueBuilder, escapeStart);
                    break;
                default:
                    AddDiagnostic(escapeStart, Math.Min(2, _text.Length - escapeStart), "JSON0003", "Invalid escape sequence.");
                    valueBuilder.Append(Current);
                    _position++;
                    break;
            }
        }

        private void ReadUnicodeEscape(StringBuilder valueBuilder, int escapeStart)
        {
            if (_position + 4 >= _text.Length)
            {
                AddDiagnostic(escapeStart, _text.Length - escapeStart, "JSON0003", "Invalid unicode escape sequence.");
                _position++;
                return;
            }

            var value = 0;
            for (var index = 1; index <= 4; index++)
            {
                var digit = _text[_position + index];
                var digitValue = GetHexValue(digit);
                if (digitValue < 0)
                {
                    AddDiagnostic(escapeStart, 6, "JSON0003", "Invalid unicode escape sequence.");
                    _position++;
                    return;
                }

                value = (value * 16) + digitValue;
            }

            valueBuilder.Append((char)value);
            _position += 5;
        }

        private JsonSyntaxToken ReadNumberToken(IReadOnlyList<JsonSyntaxTrivia> leadingTrivia, int fullStart)
        {
            var start = _position;
            var hasDigits = false;
            if (Current == '-')
            {
                _position++;
            }

            if (Current == '0')
            {
                hasDigits = true;
                _position++;
                if (Current is >= '0' and <= '9')
                {
                    AddDiagnostic(start, _position - start + 1, "JSON0004", "JSON numbers cannot contain leading zeroes.");
                    while (Current is >= '0' and <= '9')
                    {
                        _position++;
                    }
                }
            }
            else
            {
                while (Current is >= '0' and <= '9')
                {
                    hasDigits = true;
                    _position++;
                }
            }

            if (!hasDigits)
            {
                AddDiagnostic(start, _position - start, "JSON0004", "Invalid number literal.");
            }

            if (Current == '.')
            {
                _position++;
                var fractionStart = _position;
                while (Current is >= '0' and <= '9')
                {
                    _position++;
                }

                if (_position == fractionStart)
                {
                    AddDiagnostic(fractionStart, 0, "JSON0004", "Expected at least one digit after the decimal point.");
                }
            }

            if (Current is 'e' or 'E')
            {
                _position++;
                if (Current is '+' or '-')
                {
                    _position++;
                }

                var exponentStart = _position;
                while (Current is >= '0' and <= '9')
                {
                    _position++;
                }

                if (_position == exponentStart)
                {
                    AddDiagnostic(exponentStart, 0, "JSON0004", "Expected at least one digit in the exponent.");
                }
            }

            var text = _text[start.._position];

            return new JsonSyntaxToken(JsonSyntaxKind.NumberToken, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart);
        }

        private JsonSyntaxToken ReadIdentifierOrBadToken(IReadOnlyList<JsonSyntaxTrivia> leadingTrivia, int fullStart)
        {
            var start = _position;
            while (!IsAtEnd && !IsTokenBoundary(Current))
            {
                _position++;
            }

            var text = _text[start.._position];

            return text switch
            {
                "true" => new JsonSyntaxToken(JsonSyntaxKind.TrueKeyword, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart),
                "false" => new JsonSyntaxToken(JsonSyntaxKind.FalseKeyword, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart),
                "null" => new JsonSyntaxToken(JsonSyntaxKind.NullKeyword, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart),
                _ => new JsonSyntaxToken(JsonSyntaxKind.BadToken, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart),
            };
        }

        private JsonSyntaxToken ReadBadToken(IReadOnlyList<JsonSyntaxTrivia> leadingTrivia, int fullStart)
        {
            var start = _position;
            while (!IsAtEnd && !IsTokenBoundary(Current))
            {
                _position++;
            }

            if (_position == start)
            {
                _position++;
            }

            var text = _text[start.._position];

            return new JsonSyntaxToken(JsonSyntaxKind.BadToken, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart);
        }

        private JsonSyntaxToken CreateToken(JsonSyntaxKind kind, int tokenStart, IReadOnlyList<JsonSyntaxTrivia> leadingTrivia, int fullStart)
        {
            var text = _text[tokenStart.._position];

            return new JsonSyntaxToken(kind, text, text, leadingTrivia: leadingTrivia, fullStart: fullStart);
        }

        private void AddTrivia(ref List<JsonSyntaxTrivia>? trivia, JsonSyntaxKind kind, int start)
        {
            trivia ??= [];
            trivia.Add(new JsonSyntaxTrivia(kind, _text[start.._position], start));
        }

        private void AddDiagnostic(int start, int length, string id, string message)
        {
            _diagnostics.Add(new JsonDiagnostic(id, message, JsonDiagnosticSeverity.Error, new TextSpan(start, Math.Max(0, length))));
        }

        private static bool IsTokenBoundary(char value)
        {
            return value is '\0' or ' ' or '\t' or '\f' or '\v' or '\r' or '\n' or '{' or '}' or '[' or ']' or ':' or ',' or '"';
        }

        private static int GetLineBreakLength(string text, int index)
        {
            if (text[index] == '\r')
                return index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;

            return text[index] == '\n' ? 1 : 0;
        }

        private static int GetHexValue(char value)
        {
            return value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'a' and <= 'f' => value - 'a' + 10,
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1,
            };
        }
    }
}
