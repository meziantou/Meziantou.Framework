using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Parsing;

/// <summary>
/// The parser.
/// </summary>
internal partial class Parser
{
    private readonly Lexer _lexer;
    private readonly int _effectiveMaxDepth;
    private SyntaxTokenValue _previousToken;
    private SyntaxTokenValue _token;
    private bool _hideNewLine;
    private readonly List<SyntaxTrivia> _currentTrivias;
    private TableSyntaxBase? _currentTable;
    private DiagnosticsBag? _diagnostics;
    private int _currentContainerDepth;
    private TableArrayPathNode _tableArrayPaths;
    private bool _skippedToEndOfFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="Parser"/> class.
    /// </summary>
    /// <param name="lexer">The lexer.</param>
    /// <param name="options">The serializer options supplying the effective max depth.</param>
    /// <exception cref="System.ArgumentNullException"></exception>
    public Parser(Lexer lexer, TomlSerializerOptions options)
    {
        _lexer = lexer;
        _currentTrivias = new List<SyntaxTrivia>();
        _effectiveMaxDepth = TomlDepthHelper.GetEffectiveMaxDepth((options ?? throw new ArgumentNullException(nameof(options))).MaxDepth);
        _currentContainerDepth = 1;
        _tableArrayPaths = new TableArrayPathNode();
    }

    // private Stack<ScriptNode> Blocks { get; }

    public DocumentSyntax Run()
    {
        var doc = new DocumentSyntax { HasByteOrderMark = _lexer.HasByteOrderMark };
        _diagnostics = doc.Diagnostics;

        _currentTable = null;
        _hideNewLine = true;
        _currentContainerDepth = 1;
        _tableArrayPaths = new TableArrayPathNode();
        _skippedToEndOfFile = false;
        NextToken();
        while (TryParseTableEntry(out var itemEntry))
        {
            if (itemEntry == null) continue;

            if (itemEntry is TableSyntaxBase table)
            {
                _currentTable = table;
                AddToListAndUpdateSpan(doc.Tables, table);
            }
            else if (_currentTable == null)
            {
                AddToListAndUpdateSpan(doc.KeyValues, (KeyValueSyntax)itemEntry);
            }
            else
            {
                // Otherwise, we know that we can only have a key-value
                AddToListAndUpdateSpan(_currentTable.Items, (KeyValueSyntax)itemEntry);
            }
        }

        if (_currentTable != null)
        {
            Close(_currentTable);
            _currentTable = null;
        }
        Close(doc);

        if (_lexer.HasErrors)
        {
            foreach (var lexerError in _lexer.Errors)
            {
                Log(lexerError);
            }
        }

        return doc;
    }

    private static void AddToListAndUpdateSpan<TSyntaxNode>(SyntaxList<TSyntaxNode> list, TSyntaxNode node) where TSyntaxNode : SyntaxNode
    {
        if (list.ChildrenCount == 0)
        {
            list.Span.FileName = node.Span.FileName;
            list.Span.Start = node.Span.Start;
        }
        else
        {
            list.Span.End = node.Span.End;
        }

        list.Add(node);
    }

    private bool TryParseTableEntry(out SyntaxNode? nextEntry)
    {
        nextEntry = null;
        while (true)
        {
            switch (_token.Kind)
            {
                case TokenKind.Eof:
                    return false;
                case TokenKind.BasicKey:
                case TokenKind.String:
                case TokenKind.StringLiteral:
                    nextEntry = ParseKeyValue(true);
                    return true;
                case TokenKind.OpenBracket:
                case TokenKind.OpenBracketDouble:
                    nextEntry = ParseTableOrTableArray();
                    return true;
                default:
                    LogError($"Unexpected token [{ToPrintable(_token)}] found");
                    SkipToken();
                    break;
            }
        }
    }

    private KeyValueSyntax ParseKeyValue(bool expectEndOfLine)
    {
        // When parsing a key = value, we don't expect NewLines, so we don't hide them as trivia
        var previousState = _hideNewLine;
        _hideNewLine = false;
        try
        {
            var keyValueSyntax = Open<KeyValueSyntax>();
            keyValueSyntax.Key = ParseKey(isTableHeader: false, out var keyDepth);

            if (_token.Kind != TokenKind.Equal)
            {
                LogError($"Expecting `=` after a key instead of {ToPrintable(_token)}");
                Close(keyValueSyntax);
                // We recover the parsing on the next line
                SkipAfterEndOfLine();
            }
            else
            {
                // Switch the lexer to value parser
                _lexer.State = LexerState.Value;
                var containerDepth = _currentContainerDepth;
                try
                {
                    // The tables of a dotted key contain the value
                    _currentContainerDepth = keyDepth;
                    keyValueSyntax.EqualToken = EatToken();
                    keyValueSyntax.Value = ParseValue();
                }
                finally
                {
                    _currentContainerDepth = containerDepth;
                    _lexer.State = LexerState.Key;
                }

                if (expectEndOfLine && _token.Kind != TokenKind.Eof)
                {
                    keyValueSyntax.EndOfLineToken = EatToken(TokenKind.NewLine);
                }

                Close(keyValueSyntax);
            }
            return keyValueSyntax;
        }
        finally
        {
            _hideNewLine = previousState;
        }
    }

    private ValueSyntax? ParseValue()
    {
        switch (_token.Kind)
        {
            case TokenKind.Integer:
            case TokenKind.IntegerHexa:
            case TokenKind.IntegerOctal:
            case TokenKind.IntegerBinary:
                return ParseInteger();

            case TokenKind.Nan:
            case TokenKind.PositiveNan:
            case TokenKind.NegativeNan:
            case TokenKind.Infinite:
            case TokenKind.PositiveInfinite:
            case TokenKind.NegativeInfinite:
            case TokenKind.Float:
                return ParseFloat();

            case TokenKind.String:
            case TokenKind.StringMulti:
            case TokenKind.StringLiteral:
            case TokenKind.StringLiteralMulti:
                return ParseString();

            case TokenKind.OpenBracket:
                return ParseArray();

            case TokenKind.OpenBrace:
                return ParseInlineTable();

            case TokenKind.OffsetDateTimeByZ:
            case TokenKind.OffsetDateTimeByNumber:
            case TokenKind.LocalDateTime:
            case TokenKind.LocalDate:
            case TokenKind.LocalTime:
                return ParseDateTime();

            case TokenKind.True:
            case TokenKind.False:
                return ParseBoolean();

            case TokenKind.NewLine:
                // Provide a dedicated error for end-of-line
                // We don't eat the token as it is supposed to be taken by the caller
                LogError($"Unexpected token end-of-line found while expecting a value");
                break;

            default:
                LogError($"Unexpected token `{ToPrintable(_token)}` for a value");
                // Skip the token as we don't want to loop forever
                SkipToken();
                break;
        }
        return null;
    }

    private bool IsCurrentValue()
    {
        switch (_token.Kind)
        {
            case TokenKind.Integer:
            case TokenKind.IntegerHexa:
            case TokenKind.IntegerOctal:
            case TokenKind.IntegerBinary:
            case TokenKind.Infinite:
            case TokenKind.PositiveInfinite:
            case TokenKind.NegativeInfinite:
            case TokenKind.Float:
            case TokenKind.String:
            case TokenKind.StringMulti:
            case TokenKind.StringLiteral:
            case TokenKind.StringLiteralMulti:
            case TokenKind.OpenBracket:
            case TokenKind.OpenBrace:
            case TokenKind.OffsetDateTimeByZ:
            case TokenKind.OffsetDateTimeByNumber:
            case TokenKind.LocalDateTime:
            case TokenKind.LocalDate:
            case TokenKind.LocalTime:
            case TokenKind.True:
            case TokenKind.False:
                return true;
        }
        return false;
    }

    private BooleanValueSyntax ParseBoolean()
    {
        var boolean = Open<BooleanValueSyntax>();
        boolean.Value = _token.Kind == TokenKind.True;
        boolean.Token = EatToken();
        return Close(boolean);
    }

    private DateTimeValueSyntax ParseDateTime()
    {
        DateTimeValueSyntax datetime;

        switch (_token.Kind)
        {
            case TokenKind.OffsetDateTimeByZ:
                datetime = Open(new DateTimeValueSyntax(SyntaxKind.OffsetDateTimeByZ));
                break;
            case TokenKind.OffsetDateTimeByNumber:
                datetime = Open(new DateTimeValueSyntax(SyntaxKind.OffsetDateTimeByNumber));
                break;
            case TokenKind.LocalDateTime:
                datetime = Open(new DateTimeValueSyntax(SyntaxKind.LocalDateTime));
                break;
            case TokenKind.LocalDate:
                datetime = Open(new DateTimeValueSyntax(SyntaxKind.LocalDate));
                break;
            case TokenKind.LocalTime:
                datetime = Open(new DateTimeValueSyntax(SyntaxKind.LocalTime));
                break;
            default:
                LogError($"Unsupported datetime token kind `{_token.Kind}`. Treating it as a local-date-time.");
                datetime = Open(new DateTimeValueSyntax(SyntaxKind.LocalDateTime));
                break;
        }

        var literal = _token.StringValue ?? _token.GetText(_lexer.Text.Span) ?? string.Empty;
        TomlDateTime parsed;
        bool parsedOk = _token.Kind switch
        {
            TokenKind.OffsetDateTimeByZ => DateTimeRFC3339.TryParseOffsetDateTime(literal, out parsed),
            TokenKind.OffsetDateTimeByNumber => DateTimeRFC3339.TryParseOffsetDateTime(literal, out parsed),
            TokenKind.LocalDateTime => DateTimeRFC3339.TryParseLocalDateTime(literal, out parsed),
            TokenKind.LocalDate => DateTimeRFC3339.TryParseLocalDate(literal, out parsed),
            TokenKind.LocalTime => DateTimeRFC3339.TryParseLocalTime(literal, out parsed),
            _ => DateTimeRFC3339.TryParseLocalDateTime(literal, out parsed),
        };

        datetime.Value = parsedOk ? parsed : default;
        datetime.Token = EatToken();
        return Close(datetime);
    }

    private IntegerValueSyntax ParseInteger()
    {
        var i64 = Open<IntegerValueSyntax>();
        i64.Value = unchecked((long)_token.Data);
        i64.Token = EatToken();
        return Close(i64);
    }

    private FloatValueSyntax ParseFloat()
    {
        var f64 = Open<FloatValueSyntax>();
        f64.Value = BitConverter.Int64BitsToDouble(unchecked((long)_token.Data));
        f64.Token = EatToken();
        return Close(f64);
    }

    private ArraySyntax? ParseArray()
    {
        if (!EnterContainer())
        {
            return null;
        }

        var array = Open<ArraySyntax>();
        var saveHideNewLine = _hideNewLine;
        _hideNewLine = true;
        array.OpenBracket = EatToken(TokenKind.OpenBracket);
        try
        {
            bool expectingEndOfArray = false;
            while (true)
            {
                if (_token.Kind == TokenKind.CloseBracket)
                {
                    // Before parsing the next token we need to restore the parsing of new line
                    _hideNewLine = saveHideNewLine;
                    array.CloseBracket = EatToken();
                    break;
                }

                if (!expectingEndOfArray)
                {
                    var item = Open<ArrayItemSyntax>();
                    item.Value = ParseValue();

                    if (_token.Kind == TokenKind.Comma)
                    {
                        item.Comma = EatToken();
                    }
                    else if (IsCurrentValue())
                    {
                        LogError($"Missing a `,` (token: comma) to separate items in an array");
                    }
                    else
                    {
                        expectingEndOfArray = true;
                    }
                    Close(item);

                    AddToListAndUpdateSpan(array.Items, item);
                }
                else
                {
                    if (!_skippedToEndOfFile)
                    {
                        LogError($"Unexpected token `{ToPrintable(_token)}` (token: `{_token.Kind}`). Expecting a closing `]` for an array");
                    }

                    break;
                }
            }
        }
        finally
        {
            _hideNewLine = saveHideNewLine;
            ExitContainer();
        }
        return Close(array);
    }

    private InlineTableSyntax? ParseInlineTable()
    {
        if (!EnterContainer())
        {
            return null;
        }

        var inlineTable = Open<InlineTableSyntax>();

        var previousState = _lexer.State;
        var previousHideNewLine = _hideNewLine;
        _lexer.State = LexerState.Key;
        inlineTable.OpenBrace = EatToken(TokenKind.OpenBrace);
        try
        {
            // TOML 1.1: inline tables allow newlines and comments between entries.
            _hideNewLine = true;

            bool? expectingEndOfInitializer = null;

            while (true)
            {
                // Newlines are allowed between inline-table entries in TOML 1.1. They can leak as
                // non-hidden tokens because ParseKeyValue temporarily disables newline hiding.
                if (_token.Kind == TokenKind.NewLine)
                {
                    _currentTrivias.Add(new SyntaxTrivia
                    {
                        Span = GetSpanForToken(_token),
                        Kind = _token.Kind,
                        Text = GetTokenText(_token),
                    });
                    NextToken();
                    continue;
                }

                if (_token.Kind == TokenKind.CloseBrace)
                {
                    // Restore newline visibility before consuming the close brace so the outer parser
                    // can see the end-of-line token for `key = { ... }` assignments.
                    _hideNewLine = previousHideNewLine;
                    _lexer.State = previousState;
                    inlineTable.CloseBrace = EatToken();
                    break;
                }

                if ((expectingEndOfInitializer == null || !expectingEndOfInitializer.Value) && (_token.Kind == TokenKind.BasicKey || _token.Kind == TokenKind.String || _token.Kind == TokenKind.StringLiteral))
                {
                    var item = Open<InlineTableItemSyntax>();
                    item.KeyValue = ParseKeyValue(false);

                    while (_token.Kind == TokenKind.NewLine)
                    {
                        _currentTrivias.Add(new SyntaxTrivia
                        {
                            Span = GetSpanForToken(_token),
                            Kind = _token.Kind,
                            Text = GetTokenText(_token),
                        });
                        NextToken();
                    }

                    if (_token.Kind == TokenKind.Comma)
                    {
                        item.Comma = EatToken();
                        expectingEndOfInitializer = false;
                    }
                    else
                    {
                        expectingEndOfInitializer = true;
                    }

                    Close(item);

                    AddToListAndUpdateSpan(inlineTable.Items, item);
                }
                else
                {
                    if (!_skippedToEndOfFile)
                    {
                        LogError($"Unexpected token `{_token.Kind}` while parsing inline table. Expecting a bare key or string instead of `{ToPrintable(_token)}`");
                    }

                    break;
                }
            }
        }
        finally
        {
            _lexer.State = previousState;
            _hideNewLine = previousHideNewLine;
            ExitContainer();
        }

        return Close(inlineTable);
    }

    private TableSyntaxBase ParseTableOrTableArray()
    {
        // If we have a pending table, close it
        if (_currentTable != null)
        {
            Close(_currentTable);
        }
        bool isTableArray = _token.Kind == TokenKind.OpenBracketDouble;

        var previousState = _hideNewLine;
        _hideNewLine = false;
        var table = isTableArray ? (TableSyntaxBase)Open<TableArraySyntax>() : Open<TableSyntax>();
        try
        {
            table.OpenBracket = EatToken();
            table.Name = ParseKey(isTableHeader: true, out var tableDepth);
            if (isTableArray)
            {
                // The element of the array of tables is a table too
                tableDepth = IncrementDepth(tableDepth, 1);
                AddTableArrayPath(table.Name);
            }

            // The key/value pairs of the table are read at its depth
            _currentContainerDepth = tableDepth;
            table.CloseBracket = EatToken(isTableArray ? TokenKind.CloseBracketDouble : TokenKind.CloseBracket);

            if (_token.Kind != TokenKind.Eof)
            {
                table.EndOfLineToken = EatToken(TokenKind.NewLine);
            }
            // We don't close the table as it is going to be the new table
        }
        finally
        {
            _hideNewLine = previousState;
        }

        return table;
    }

    private KeySyntax ParseKey(bool isTableHeader, out int depth)
    {
        var key = Open<KeySyntax>();
        key.Key = ParseBaseKey();

        // Count the depth the way TomlParser does. Each segment of a table header, and each segment but the last of a
        // dotted key, is a table. A table header starts from the root, and goes through the last element of each array
        // of tables it names.
        // An error is reported on the segment that is too deep, like TomlParser does.
        depth = _currentContainerDepth;
        TableArrayPathNode? tableArrayPath = null;
        if (isTableHeader)
        {
            depth = IncrementDepth(1, 1, key.Key);
            tableArrayPath = _tableArrayPaths.GetChild(key.Key);
        }

        var previousSegment = key.Key;
        while (_token.Kind == TokenKind.Dot)
        {
            // In a header, the next segment is the next table. In a dotted key, the previous segment is.
            var levels = tableArrayPath is { IsTableArray: true } ? 2 : 1;
            if (!isTableHeader)
            {
                depth = IncrementDepth(depth, levels, previousSegment);
            }

            var dotKey = ParseDotKey();
            AddToListAndUpdateSpan(key.DotKeys, dotKey);
            if (isTableHeader)
            {
                depth = IncrementDepth(depth, levels, dotKey.Key);
            }

            tableArrayPath = tableArrayPath?.GetChild(dotKey.Key);
            previousSegment = dotKey.Key;
        }
        return Close(key);
    }

    // Reports an error when the depth goes past the maximum. The segments of a key are read in a loop, so the key can
    // still be read.
    private int IncrementDepth(int depth, int levels, SyntaxNode? segment = null)
    {
        var newDepth = depth + levels;
        if (depth <= _effectiveMaxDepth && newDepth > _effectiveMaxDepth)
        {
            var message = TomlDepthHelper.GetMaxDepthExceededMessage(_effectiveMaxDepth);
            if (segment is not null)
            {
                LogError(segment.Span, message);
            }
            else
            {
                LogError(message);
            }
        }

        return newDepth;
    }

    private void AddTableArrayPath(KeySyntax key)
    {
        var node = _tableArrayPaths.GetOrAddChild(key.Key);
        foreach (var dotKey in key.DotKeys)
        {
            node = node?.GetOrAddChild(dotKey.Key);
        }

        node?.IsTableArray = true;
    }

    private BareKeyOrStringValueSyntax? ParseBaseKey()
    {
        if (_token.Kind == TokenKind.BasicKey)
        {
            return ParseBasicKey();
        }

        if (_token.Kind == TokenKind.String || _token.Kind == TokenKind.StringLiteral)
        {
            return ParseString();
        }

        LogError($"Unexpected token `{ToPrintable(_token)}` for a base key");
        SkipToken();
        return null;
    }

    private bool EnterContainer()
    {
        _currentContainerDepth++;
        var isTooDeep = _currentContainerDepth > _effectiveMaxDepth;
        if (isTooDeep || !TomlDepthHelper.HasSufficientExecutionStack())
        {
            // Containers are read recursively, so the rest of the document is kept as trivia instead of being read. When
            // the key or the table of the container already went past the maximum depth, the error is already reported.
            if (!isTooDeep)
            {
                LogError(TomlDepthHelper.InsufficientExecutionStackMessage);
            }
            else if (_currentContainerDepth - 1 <= _effectiveMaxDepth)
            {
                LogError(TomlDepthHelper.GetMaxDepthExceededMessage(_effectiveMaxDepth));
            }

            _currentContainerDepth--;
            while (_token.Kind != TokenKind.Eof)
            {
                SkipToken();
            }

            // The enclosing containers are not closed, which is already reported
            _skippedToEndOfFile = true;

            return false;
        }

        return true;
    }

    private void ExitContainer()
    {
        _currentContainerDepth = Math.Max(1, _currentContainerDepth - 1);
    }

    // A lexer created without DecodeScalars (TomlLexer.Create) does not decode strings
    private string DecodeCurrentString()
    {
        var raw = _token.GetText(_lexer.Text.Span);
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        try
        {
            return TomlStringDecoder.Decode(raw, _token.Kind);
        }
        catch (FormatException)
        {
            // The lexer already reported the invalid escape sequence
            return string.Empty;
        }
    }

    private StringValueSyntax ParseString()
    {
        var str = Open<StringValueSyntax>();
        str.Value = _token.StringValue ?? DecodeCurrentString();
        str.Token = EatToken();
        return Close(str);
    }
    private DottedKeyItemSyntax ParseDotKey()
    {
        var dotKey = Open<DottedKeyItemSyntax>();
        dotKey.Dot = EatToken();
        dotKey.Key = ParseBaseKey();
        return Close(dotKey);
    }

    private BareKeySyntax ParseBasicKey()
    {
        var basicKey = Open<BareKeySyntax>();
        basicKey.Key = EatToken(TokenKind.BasicKey);
        return Close(basicKey);
    }

    private SyntaxToken EatToken(TokenKind tokenKind)
    {
        SyntaxToken syntax;
        if (_token.Kind == tokenKind)
        {
            syntax = Open<SyntaxToken>();
        }
        else
        {
            // Create an invalid token in case we don't match it
            var invalid = Open<InvalidSyntaxToken>();
            invalid.InvalidKind = _token.Kind;
            syntax = invalid;
            var tokenText = tokenKind.ToText();
            var expectingTokenText = tokenText != null ? $"while expecting `{tokenText}` (token: `{tokenKind.ToString().ToLowerInvariant()}`)" : $"while expecting token `{tokenKind.ToString().ToLowerInvariant()}`";
            if (_token.Kind == TokenKind.Invalid)
            {
                LogError($"Unexpected token found `{ToPrintable(_token)}` {expectingTokenText}");
            }
            else
            {
                LogError($"Unexpected token found `{ToPrintable(_token)}` (token: `{_token.Kind.ToString().ToLowerInvariant()}`) {expectingTokenText}");
            }
        }
        syntax.TokenKind = tokenKind;
        // An invalid token keeps the text that was found, not the text of the expected token
        syntax.Text = _token.Kind == TokenKind.Eof ? string.Empty : _token.Kind.ToText() ?? GetTokenText(_token);
        if (tokenKind == TokenKind.NewLine)
        {
            // Once we have found a new line, we let all the other NewLines as trivias
            _hideNewLine = true;
        }
        NextToken();
        return Close(syntax);
    }

    private SyntaxToken EatToken()
    {
        var syntax = Open<SyntaxToken>();
        syntax.TokenKind = _token.Kind;
        syntax.Text = _token.Kind.ToText() ?? GetTokenText(_token);
        NextToken();
        return Close(syntax);
    }

    private void SkipAfterEndOfLine()
    {
        while (!IsEolOrEof())
        {
            SkipToken();
        }
        if (_token.Kind != TokenKind.Eof)
        {
            SkipToken();
        }
    }

    // A skipped token is kept as trivia, so that the tree still has every character of the document
    private void SkipToken()
    {
        if (_token.Kind != TokenKind.Eof)
        {
            _currentTrivias.Add(new SyntaxTrivia
            {
                Span = GetSpanForToken(_token),
                Kind = _token.Kind,
                Text = GetTokenText(_token),
            });
        }

        NextToken();
    }

    private bool IsEolOrEof()
    {
        return _token.Kind == TokenKind.NewLine || _token.Kind == TokenKind.Eof;
    }

    private T Open<T>() where T : SyntaxNode, new()
    {
        return Open<T>(_token);
    }

    private T Open<T>(T syntax) where T : SyntaxNode
    {
        return Open(syntax, _token);
    }

    private T Open<T>(T syntax, SyntaxTokenValue startToken) where T : SyntaxNode
    {
        syntax.Span = new SourceSpan(_lexer.SourcePath, startToken.Start, new TextPosition());

        if (_currentTrivias.Count > 0)
        {
            syntax.LeadingTrivia = new List<SyntaxTrivia>(_currentTrivias);
            _currentTrivias.Clear();
        }
        return syntax;
    }

    private T Open<T>(SyntaxTokenValue startToken) where T : SyntaxNode, new()
    {
        return Open(new T(), startToken);
    }

    private T Close<T>(T syntax) where T : SyntaxNode
    {
        syntax.Span.End = _previousToken.End;

        if (_currentTrivias.Count > 0)
        {
            syntax.TrailingTrivia = new List<SyntaxTrivia>(_currentTrivias);
            _currentTrivias.Clear();
        }
        return syntax;
    }

    private string? ToPrintable(SyntaxTokenValue localToken)
    {
        return ToText(localToken).ToPrintableString();
    }

    private string? ToText(SyntaxTokenValue localToken)
    {
        return localToken.GetText(_lexer.Text.Span);
    }

    private void NextToken()
    {
        _previousToken = _token;
        bool result;

        // Skip trivias
        while (true)
        {
            result = _lexer.MoveNext();
            if (!result)
            {
                _token = new SyntaxTokenValue(TokenKind.Eof, new TextPosition(), new TextPosition());
                return;
            }

            ref readonly var token = ref _lexer.Token;
            if (!token.Kind.IsHidden(_hideNewLine))
            {
                _token = token;
                return;
            }

            _currentTrivias.Add(new SyntaxTrivia
            {
                Span = new SourceSpan(_lexer.SourcePath, token.Start, token.End),
                Kind = token.Kind,
                Text = GetTokenText(token),
            });
        }
    }

    // Whitespace and newlines are most of the tokens of a document, so their texts are shared
    private static readonly string[] SharedSpaces = CreateSpaces();

    private static string[] CreateSpaces()
    {
        var spaces = new string[17];
        for (var i = 0; i < spaces.Length; i++)
        {
            spaces[i] = new string(' ', i);
        }

        return spaces;
    }

    private string? GetTokenText(in SyntaxTokenValue token)
    {
        var length = token.End.Offset - token.Start.Offset + 1;
        if (length > 0 && length < SharedSpaces.Length && token.Start.Offset + length <= _lexer.Text.Length)
        {
            var text = _lexer.Text.Span.Slice(token.Start.Offset, length);
            if (text is "\n")
            {
                return "\n";
            }

            if (text is "\r\n")
            {
                return "\r\n";
            }

            if (!text.ContainsAnyExcept(' '))
            {
                return SharedSpaces[length];
            }
        }

        return token.GetText(_lexer.Text.Span);
    }

    private void LogError(string text)
    {
        LogError(_token, text);
    }

    private void LogError(SyntaxTokenValue tokenArg, string text)
    {
        LogError(GetSpanForToken(tokenArg), text);
    }

    //private void LogError<T>(SyntaxValueNode<T> tokenArg, string text)
    //{
    //    LogError(tokenArg.Token, text);
    //}

    private SourceSpan GetSpanForToken(SyntaxTokenValue tokenArg)
    {
        return new SourceSpan(_lexer.SourcePath, tokenArg.Start, tokenArg.End);
    }

    private void LogError(SourceSpan span, string text)
    {
        Log(new DiagnosticMessage(DiagnosticMessageKind.Error, span, text));
    }

    private void Log(DiagnosticMessage diagnosticMessage)
    {
        ArgumentNullException.ThrowIfNull(diagnosticMessage);
        _diagnostics!.Add(diagnosticMessage);
    }

    // The paths of the arrays of tables, so that a table header counts the elements it goes through
    private sealed class TableArrayPathNode
    {
        private Dictionary<string, TableArrayPathNode>? _children;

        public bool IsTableArray { get; set; }

        public TableArrayPathNode? GetChild(BareKeyOrStringValueSyntax? key)
        {
            return _children is not null && GetKey(key) is { } name && _children.TryGetValue(name, out var child) ? child : null;
        }

        public TableArrayPathNode? GetOrAddChild(BareKeyOrStringValueSyntax? key)
        {
            if (GetKey(key) is not { } name)
            {
                return null;
            }

            _children ??= new Dictionary<string, TableArrayPathNode>(StringComparer.Ordinal);
            if (!_children.TryGetValue(name, out var child))
            {
                child = new TableArrayPathNode();
                _children.Add(name, child);
            }

            return child;
        }

        private static string? GetKey(BareKeyOrStringValueSyntax? key) => key switch
        {
            BareKeySyntax bareKey => bareKey.Key?.Text,
            StringValueSyntax stringKey => stringKey.Value,
            _ => null,
        };
    }
}
