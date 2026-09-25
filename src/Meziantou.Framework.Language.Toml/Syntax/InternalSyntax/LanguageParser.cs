using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Toml.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Builds the immutable tree for a TOML document, keeping every character of it.</summary>
/// <remarks>
/// <para>
/// Whatever the text says, the tree reproduces it exactly and parsing finishes. TOML is line-oriented, so recovery is
/// too: a key/value pair or a table header that goes wrong never takes the next line with it, and whatever is left on
/// its own line after it is kept as skipped text.
/// </para>
/// <para>
/// Arrays, and inline tables in TOML 1.1, may span lines, so they cannot stop at the end of one. They stop at their
/// closing bracket, and also at a line that could only be the start of the next entry -- a table header, or a key
/// followed by <c>=</c> -- where a comma or a closing bracket was expected. That is what keeps a missing <c>]</c> from
/// turning the rest of the document into one array.
/// </para>
/// </remarks>
internal sealed class LanguageParser
{
    private readonly Lexer _lexer;
    private readonly TomlParseOptions _options;
    private readonly List<PendingDiagnostic> _pending = [];
    private GreenToken _current;
    private LexerMode _currentMode;
    private int _currentFullStart;
    private int _previousTokenTextEnd;
    private bool _previousTokenEndedTheLine = true;
    private int _depth;

    public LanguageParser(SourceText source, TomlParseOptions options)
    {
        _options = options;
        _lexer = new Lexer(source, options.Version);
        _currentMode = LexerMode.Key;
        _current = _lexer.Lex(_currentMode);
        _currentFullStart = _lexer.Position - _current.FullWidth;
    }

    /// <summary>What a value can end with. A value that cannot be read stops at the first of these it meets.</summary>
    [Flags]
    private enum TerminatorState
    {
        None = 0,
        EndOfFile = 1,
        Comma = 2,
        CloseBracket = 4,
        CloseBrace = 8,

        /// <summary>The value is the value of a key/value pair at the top level, which ends with its line.</summary>
        EndOfLine = 16,

        Closers = CloseBracket | CloseBrace,
    }

    private SyntaxKind CurrentKind => (SyntaxKind)_current.RawKind;

    public TomlDocumentSyntax ParseDocument()
    {
        var mark = _pending.Count;
        var entries = new List<GreenNode?>();
        while (true)
        {
            EnsureMode(LexerMode.Key);
            if (CurrentKind == SyntaxKind.EndOfFileToken)
                break;

            var entryStart = _currentFullStart;
            var entry = ParseEntry();
            entries.Add(MoveLineEndOntoTrailingMissingTokens(entry));

            // Every entry reads at least one token; if one ever does not, the line is skipped rather than read forever.
            if (_currentFullStart == entryStart && CurrentKind != SyntaxKind.EndOfFileToken)
            {
                entries.Add(ParseSkippedLine());
                continue;
            }

            // Every entry ends its line; what follows it on the same line is not the start of another one. It is read
            // the way the start of an entry is, not as the value the token after a value was read as.
            if (CurrentKind != SyntaxKind.EndOfFileToken && !_previousTokenEndedTheLine)
            {
                EnsureMode(LexerMode.Key);
                entries.Add(ParseRestOfLine(entry is TomlTableSyntax ? "a table header" : "a key/value pair"));
            }
        }

        var node = new TomlDocumentSyntax(SyntaxFactory.List(entries.ToArray()), EatToken(LexerMode.Key));

        return (TomlDocumentSyntax)Finish(node, nodeFullStart: 0, mark);
    }

    /// <summary>Parses text that holds a single value, keeping whatever follows it as skipped text.</summary>
    public GreenNode ParseStandaloneValue()
    {
        var mark = _pending.Count;
        EnsureMode(LexerMode.Value);
        GreenNode value;
        if (CurrentKind == SyntaxKind.EndOfFileToken)
        {
            AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedValue);
            value = new TomlSkippedValueSyntax(tokens: null);
        }
        else
        {
            value = ParseValue(TerminatorState.EndOfFile);
        }

        if (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            // A value has one node, so what follows it is kept together with it, token by token. The diagnostics the
            // nodes of the value carried move to the node that replaces them, at the same place in the text.
            if (!_current.ContainsDiagnostics)
            {
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);
            }

            var tokens = new List<GreenNode?>();
            var diagnostics = new List<SyntaxDiagnosticInfo>();
            CollectTokens(value, offset: 0, tokens, diagnostics);
            while (CurrentKind != SyntaxKind.EndOfFileToken)
            {
                tokens.Add(EatToken(LexerMode.Value));
            }

            value = new TomlSkippedValueSyntax(SyntaxFactory.ListNode(tokens.ToArray())).WithAdditionalDiagnostics([.. diagnostics]);
        }

        return Finish(value, nodeFullStart: 0, mark);

        static void CollectTokens(GreenNode node, int offset, List<GreenNode?> tokens, List<SyntaxDiagnosticInfo> diagnostics)
        {
            if (node.IsToken)
            {
                tokens.Add(node);
                return;
            }

            foreach (var diagnostic in node.GetDiagnostics())
            {
                diagnostics.Add(diagnostic.WithOffset(offset + diagnostic.Offset));
            }

            for (var i = 0; i < node.SlotCount; i++)
            {
                if (node.GetSlot(i) is { } child)
                {
                    CollectTokens(child, offset + node.GetSlotOffset(i), tokens, diagnostics);
                }
            }
        }
    }

    private GreenNode ParseEntry()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.OpenBracketOpenBracketToken:
                return ParseTableHeader();
            case var kind when IsKeyPart(kind) || kind is SyntaxKind.DotToken or SyntaxKind.EqualsToken:
                return ParseProperty(TerminatorState.EndOfFile | TerminatorState.EndOfLine);
            default:
                return ParseSkippedLine();
        }
    }

    private TomlTableSyntax ParseTableHeader()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(LexerMode.Key);
        var isArrayOfTables = openBracket.RawKind == (int)SyntaxKind.OpenBracketOpenBracketToken;
        var key = ParseKey(firstPartOnSameLine: true);

        var expectedClose = isArrayOfTables ? SyntaxKind.CloseBracketCloseBracketToken : SyntaxKind.CloseBracketToken;
        GreenToken closeBracket;
        if (CurrentKind is SyntaxKind.CloseBracketToken or SyntaxKind.CloseBracketCloseBracketToken && !_previousTokenEndedTheLine)
        {
            if (CurrentKind != expectedClose)
            {
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.ExpectedCharacter, SyntaxFacts.GetText(expectedClose));
            }

            closeBracket = EatToken(LexerMode.Key);
        }
        else
        {
            AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedCharacter, SyntaxFacts.GetText(expectedClose));
            closeBracket = SyntaxFactory.MissingToken(expectedClose);
        }

        var node = new TomlTableSyntax(isArrayOfTables ? SyntaxKind.TomlArrayOfTables : SyntaxKind.TomlTable, openBracket, key, closeBracket);

        return (TomlTableSyntax)Finish(node, start, mark);
    }

    /// <summary>Parses a key, dotted or not, which cannot span lines.</summary>
    /// <param name="firstPartOnSameLine">Whether the first part has to be on the same line as the token before it.</param>
    private TomlKeySyntax ParseKey(bool firstPartOnSameLine)
    {
        var tokens = new List<GreenNode?>();
        while (true)
        {
            EnsureMode(LexerMode.Key);
            var mustBeOnSameLine = tokens.Count > 0 || firstPartOnSameLine;
            if (IsKeyPart(CurrentKind) && !(mustBeOnSameLine && _previousTokenEndedTheLine))
            {
                if (CurrentKind is SyntaxKind.MultiLineBasicStringToken or SyntaxKind.MultiLineLiteralStringToken)
                {
                    AddErrorAtCurrentToken(TomlDiagnosticDescriptors.MultiLineStringKey);
                }

                tokens.Add(EatToken(LexerMode.Key));
            }
            else
            {
                if (mustBeOnSameLine)
                {
                    AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedKey);
                }
                else
                {
                    // Nothing comes before the key in its entry, so the end of the previous line is not where it is missing.
                    _pending.Add(new PendingDiagnostic(_currentFullStart + _current.GetLeadingTriviaWidth(), 0, TomlDiagnosticDescriptors.ExpectedKey, []));
                }

                tokens.Add(SyntaxFactory.MissingToken(SyntaxKind.BareKeyToken));
            }

            // Before anything of the key is read, the line break that matters is the one the first part needed.
            var consumedAny = tokens.Exists(token => token is { IsMissing: false });
            if (CurrentKind != SyntaxKind.DotToken || (_previousTokenEndedTheLine && (consumedAny || firstPartOnSameLine)))
                break;

            tokens.Add(EatToken(LexerMode.Key));
        }

        return new TomlKeySyntax(SyntaxFactory.ListNode(tokens.ToArray()));
    }

    /// <summary>Parses <c>key = value</c>, which cannot span lines, though the value may when it is an array or a string.</summary>
    private TomlPropertySyntax ParseProperty(TerminatorState terminators)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var key = ParseKey(firstPartOnSameLine: false);
        var keyIsEmpty = _currentFullStart == start;

        GreenToken equalsToken;
        GreenNode value;
        if (CurrentKind == SyntaxKind.EqualsToken && (!_previousTokenEndedTheLine || keyIsEmpty))
        {
            equalsToken = EatToken(LexerMode.Value);
            if (_previousTokenEndedTheLine || IsTerminator(CurrentKind, terminators))
            {
                AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedValue);
                value = new TomlSkippedValueSyntax(tokens: null);
            }
            else
            {
                value = ParseValue(terminators);
            }
        }
        else
        {
            // Without '=' there is no telling what follows is a value, so none is read: what is left of the line is
            // skipped as a whole, rather than parsed into a value the text may never have meant.
            AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedCharacter, "=");
            equalsToken = SyntaxFactory.MissingToken(SyntaxKind.EqualsToken);
            value = new TomlSkippedValueSyntax(tokens: null);
        }

        var node = new TomlPropertySyntax(key, equalsToken, value);

        return (TomlPropertySyntax)Finish(node, start, mark);
    }

    private GreenNode ParseValue(TerminatorState terminators)
    {
        EnsureMode(LexerMode.Value);
        var valueKind = SyntaxFacts.GetValueKind(CurrentKind);
        if (valueKind != SyntaxKind.None)
            return new TomlLiteralSyntax(valueKind, EatToken(LexerMode.Value));

        switch (CurrentKind)
        {
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.OpenBraceToken:
                return ParseArrayOrInlineTable(terminators);
            case var kind when IsTerminator(kind, terminators) || ShouldAbandon(terminators):
                AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedValue);
                return new TomlSkippedValueSyntax(tokens: null);
            default:
                return ParseSkippedValue(terminators, reportFirstToken: true);
        }
    }

    /// <summary>Parses an array or an inline table, and refuses to descend past <see cref="TomlParseOptions.MaxDepth"/>.</summary>
    /// <remarks>
    /// Past the limit the rest of the construct is kept as skipped text, so the promise the whole parser is built on
    /// still holds: nothing is thrown, every character is reproduced, and what went wrong is a diagnostic.
    /// </remarks>
    private GreenNode ParseArrayOrInlineTable(TerminatorState terminators)
    {
        if (_depth >= _options.MaxDepth)
            return ParseTooDeepValue();

        _depth++;
        try
        {
            return CurrentKind == SyntaxKind.OpenBracketToken ? ParseArray(terminators) : ParseInlineTable(terminators);
        }
        finally
        {
            _depth--;
        }
    }

    private TomlArraySyntax ParseArray(TerminatorState outerTerminators)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(LexerMode.Value);
        var elements = new List<GreenNode?>();
        var closers = TerminatorState.EndOfFile | TerminatorState.CloseBracket | (outerTerminators & TerminatorState.Closers);

        while (true)
        {
            EnsureMode(LexerMode.Value);
            if (IsTerminator(CurrentKind, closers))
                break;

            if (EndsWithNode(elements))
            {
                if (CurrentKind == SyntaxKind.CommaToken)
                {
                    elements.Add(EatToken(LexerMode.Value));
                    continue;
                }

                if (ShouldAbandon(closers))
                    break;

                AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedCharacter, ",");
                elements.Add(SyntaxFactory.MissingToken(SyntaxKind.CommaToken));
                continue;
            }

            if (CurrentKind == SyntaxKind.CommaToken)
            {
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.ExpectedValue);
                elements.Add(new TomlSkippedValueSyntax(tokens: null));
                continue;
            }

            if (!CanStartValue(CurrentKind) && ShouldAbandon(closers))
                break;

            elements.Add(ParseValue(closers | TerminatorState.Comma));
        }

        var closeBracket = EatToken(SyntaxKind.CloseBracketToken, LexerMode.Value, TomlDiagnosticDescriptors.ExpectedCharacter, "]");
        var node = new TomlArraySyntax(openBracket, SyntaxFactory.ListNode(elements.ToArray()), closeBracket);

        return (TomlArraySyntax)Finish(node, start, mark);
    }

    private TomlInlineTableSyntax ParseInlineTable(TerminatorState outerTerminators)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBrace = EatToken(LexerMode.Key);
        var properties = new List<GreenNode?>();
        var closers = TerminatorState.EndOfFile | TerminatorState.CloseBrace | (outerTerminators & TerminatorState.Closers);
        var reportedLineBreak = false;
        var lastCommaPosition = -1;

        // An inline table written one key/value pair per line, the way TOML 1.1 allows.
        var spansLines = _previousTokenEndedTheLine;

        while (true)
        {
            EnsureMode(LexerMode.Key);

            // Keys are read with the lexer that joins ']]' into the closer of an array-of-tables header, but in a value
            // those are the closers of two arrays.
            if (CurrentKind == SyntaxKind.CloseBracketCloseBracketToken && (closers & TerminatorState.CloseBracket) != TerminatorState.None)
            {
                EnsureMode(LexerMode.Value);
            }

            if (_options.Version < TomlVersion.V1_1 && _previousTokenEndedTheLine && !reportedLineBreak && CurrentKind != SyntaxKind.EndOfFileToken)
            {
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.RequiresNewerVersion, "A line break in an inline table", "1.1");
                reportedLineBreak = true;
            }

            if (IsTerminator(CurrentKind, closers))
                break;

            if (EndsWithNode(properties))
            {
                if (CurrentKind == SyntaxKind.CommaToken)
                {
                    lastCommaPosition = _currentFullStart + _current.GetLeadingTriviaWidth();
                    properties.Add(EatToken(LexerMode.Key));
                    continue;
                }

                // In an inline table that spans lines, an indented 'key =' on the next line is its next key/value pair
                // with the comma forgotten, rather than the next entry of the document.
                if (ShouldAbandon(closers) && !(spansLines && IsIndentedKeyValuePair()))
                    break;

                AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedCharacter, ",");
                properties.Add(SyntaxFactory.MissingToken(SyntaxKind.CommaToken));
                continue;
            }

            if (CurrentKind == SyntaxKind.CommaToken)
            {
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.ExpectedKey);
                properties.Add(MissingProperty(value: null));
                continue;
            }

            if (IsKeyPart(CurrentKind) || CurrentKind is SyntaxKind.DotToken or SyntaxKind.EqualsToken)
            {
                properties.Add(ParseProperty(closers | TerminatorState.Comma));
                continue;
            }

            if (ShouldAbandon(closers))
                break;

            // Something that cannot start a key/value pair: kept as the value of one that has neither key nor '='.
            var skippedMark = _pending.Count;
            var skippedStart = _currentFullStart;
            AddErrorAtCurrentToken(TomlDiagnosticDescriptors.ExpectedKey);
            var skipped = ParseSkippedValue(closers | TerminatorState.Comma, reportFirstToken: false);
            properties.Add(Finish(MissingProperty(skipped), skippedStart, skippedMark));
        }

        if (_options.Version < TomlVersion.V1_1 && properties.Count > 0 && !EndsWithNode(properties) && lastCommaPosition >= 0)
        {
            _pending.Add(new PendingDiagnostic(lastCommaPosition, 1, TomlDiagnosticDescriptors.RequiresNewerVersion, ["A trailing comma in an inline table", "1.1"]));
        }

        var closeBrace = EatToken(SyntaxKind.CloseBraceToken, LexerMode.Value, TomlDiagnosticDescriptors.ExpectedCharacter, "}");
        var node = new TomlInlineTableSyntax(openBrace, SyntaxFactory.ListNode(properties.ToArray()), closeBrace);

        return (TomlInlineTableSyntax)Finish(node, start, mark);
    }

    /// <summary>Keeps an array or an inline table that nests too deep as skipped text, up to the bracket that closes it.</summary>
    /// <remarks>
    /// <para>
    /// The brackets are counted, so the arrays and inline tables around it each still close with their own bracket,
    /// and the construct is reported once. Only the nesting is, and not what the tokens inside it say: they are read
    /// as values whatever they are, so a key in there would otherwise be reported as a value that makes no sense.
    /// </para>
    /// <para>
    /// A construct that is not closed stops, as any other one does, at a line that can only be the start of the
    /// next entry.
    /// </para>
    /// </remarks>
    private TomlSkippedValueSyntax ParseTooDeepValue()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        AddErrorAtCurrentToken(TomlDiagnosticDescriptors.NestingTooDeep, _options.MaxDepth);

        var tokens = new List<GreenNode?>();
        var depth = 0;
        do
        {
            switch (CurrentKind)
            {
                case SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken:
                    depth++;
                    break;
                case SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken:
                    depth--;
                    break;
            }

            tokens.Add(WithoutOwnDiagnostics(EatToken(LexerMode.Value)));
        }
        while (depth > 0 && CurrentKind != SyntaxKind.EndOfFileToken && !(_previousTokenEndedTheLine && LooksLikeStartOfEntry()));

        var node = new TomlSkippedValueSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

        return (TomlSkippedValueSyntax)Finish(node, start, mark);

        static GreenNode WithoutOwnDiagnostics(GreenToken token)
        {
            if (token.GetDiagnostics().Length == 0)
                return token;

            var result = (GreenToken)token.SetDiagnostics(diagnostics: null);

            // A token that holds text that could not be read says so with a flag the copy does not inherit.
            return token.ContainsSkippedText && !result.ContainsSkippedText ? result.AsSkippedText() : result;
        }
    }

    /// <summary>Keeps the tokens of a value that cannot be read, up to where the value has to end.</summary>
    /// <param name="terminators">Where the value has to end.</param>
    /// <param name="reportFirstToken">
    /// Whether to report the first token, when the lexer did not already. The others are part of the same mistake.
    /// </param>
    private TomlSkippedValueSyntax ParseSkippedValue(TerminatorState terminators, bool reportFirstToken)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var tokens = new List<GreenNode?>();
        if (reportFirstToken && !_current.ContainsDiagnostics)
        {
            AddErrorAtCurrentToken(TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);
        }

        do
        {
            tokens.Add(EatToken(LexerMode.Value));
        }
        while (!IsTerminator(CurrentKind, terminators) && !ShouldAbandon(terminators));

        var node = new TomlSkippedValueSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

        return (TomlSkippedValueSyntax)Finish(node, start, mark);
    }

    /// <summary>Keeps a line that does not start an entry, reporting its first token.</summary>
    private TomlSkippedTextSyntax ParseSkippedLine()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        if (!_current.ContainsDiagnostics)
        {
            AddErrorAtCurrentToken(TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);
        }

        var tokens = ReadRestOfLine();
        var node = new TomlSkippedTextSyntax(SyntaxFactory.ListNode(tokens));

        return (TomlSkippedTextSyntax)Finish(node, start, mark);
    }

    /// <summary>Keeps what follows an entry on its line, where TOML allows nothing but a comment.</summary>
    private TomlSkippedTextSyntax ParseRestOfLine(string entryDescription)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        AddErrorAtCurrentToken(TomlDiagnosticDescriptors.ExpectedEndOfLine, entryDescription);

        var tokens = ReadRestOfLine();
        var node = new TomlSkippedTextSyntax(SyntaxFactory.ListNode(tokens));

        return (TomlSkippedTextSyntax)Finish(node, start, mark);
    }

    private GreenNode?[] ReadRestOfLine()
    {
        var tokens = new List<GreenNode?>();
        do
        {
            tokens.Add(EatToken(LexerMode.Key));
        }
        while (CurrentKind != SyntaxKind.EndOfFileToken && !_previousTokenEndedTheLine);

        return tokens.ToArray();
    }

    private GreenToken EatToken(LexerMode nextMode)
    {
        var eaten = _current;
        _previousTokenTextEnd = _currentFullStart + eaten.GetLeadingTriviaWidth() + eaten.Text.Length;
        _previousTokenEndedTheLine = EndsTheLine(eaten.TrailingTrivia);
        _lexer.Position = _currentFullStart + eaten.FullWidth;
        _current = _lexer.Lex(nextMode);
        _currentMode = nextMode;
        _currentFullStart = _lexer.Position - _current.FullWidth;

        return eaten;
    }

    private GreenToken EatToken(SyntaxKind kind, LexerMode nextMode, DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        if (CurrentKind == kind)
            return EatToken(nextMode);

        AddErrorForMissingToken(descriptor, arguments);

        return SyntaxFactory.MissingToken(kind);
    }

    /// <summary>Reads the current token again the way <paramref name="mode"/> reads it, if it was read another way.</summary>
    private void EnsureMode(LexerMode mode)
    {
        if (_currentMode == mode)
            return;

        _lexer.Position = _currentFullStart;
        _current = _lexer.Lex(mode);
        _currentMode = mode;
    }

    /// <summary>
    /// Determines whether a construct that can span lines should stop at the current token rather than read it: the
    /// token begins a line, and that line reads as the start of the next entry.
    /// </summary>
    /// <remarks>Only asked when the token is not what the construct expects, so a well-formed document never pays for it.</remarks>
    private bool ShouldAbandon(TerminatorState terminators)
        => (terminators & TerminatorState.EndOfLine) == TerminatorState.None && _previousTokenEndedTheLine && CurrentKind != SyntaxKind.EndOfFileToken && LooksLikeStartOfEntry();

    /// <summary>Determines whether the current line is a table header, or starts with <c>key =</c>.</summary>
    /// <remarks>
    /// A header is recognized by its whole line, <c>[key]</c> or <c>[[key]]</c> followed by nothing but a comment:
    /// a line such as <c>[3, 4]</c> in an array is an element that lacks the comma before it, not a header.
    /// </remarks>
    private bool LooksLikeStartOfEntry()
    {
        var savedPosition = _lexer.Position;
        try
        {
            _lexer.Position = _currentFullStart;
            var token = _lexer.Lex(LexerMode.Key);
            if (token.RawKind is (int)SyntaxKind.OpenBracketToken or (int)SyntaxKind.OpenBracketOpenBracketToken)
                return IsRestOfHeaderLine();

            while (IsKeyPart((SyntaxKind)token.RawKind) && !EndsTheLine(token.TrailingTrivia))
            {
                token = _lexer.Lex(LexerMode.Key);
                if (token.RawKind == (int)SyntaxKind.EqualsToken)
                    return true;

                if (token.RawKind != (int)SyntaxKind.DotToken || EndsTheLine(token.TrailingTrivia))
                    return false;

                token = _lexer.Lex(LexerMode.Key);
            }

            return false;
        }
        finally
        {
            _lexer.Position = savedPosition;
        }
    }

    /// <summary>Reads the rest of a line that starts with <c>[</c> or <c>[[</c>, and determines whether it is a key, a closing bracket, and nothing more.</summary>
    private bool IsRestOfHeaderLine()
    {
        while (true)
        {
            var token = _lexer.Lex(LexerMode.Key);
            if (!SyntaxFacts.IsKeyToken((SyntaxKind)token.RawKind) || EndsTheLine(token.TrailingTrivia))
                return false;

            token = _lexer.Lex(LexerMode.Key);
            if (token.RawKind is (int)SyntaxKind.CloseBracketToken or (int)SyntaxKind.CloseBracketCloseBracketToken)
                return EndsTheLine(token.TrailingTrivia) || _lexer.Lex(LexerMode.Key).RawKind == (int)SyntaxKind.EndOfFileToken;

            if (token.RawKind != (int)SyntaxKind.DotToken || EndsTheLine(token.TrailingTrivia))
                return false;
        }
    }

    /// <summary>Determines whether the current token starts an indented line that reads as <c>key =</c>.</summary>
    private bool IsIndentedKeyValuePair()
    {
        if (!IsKeyPart(CurrentKind) || _current.LeadingTrivia is not { } leading)
            return false;

        var count = GreenNodeList.Count(leading);
        return count > 0 && GreenNodeList.ElementAt(leading, count - 1)?.RawKind == (int)SyntaxKind.WhitespaceTrivia && LooksLikeStartOfEntry();
    }

    /// <summary>Moves the trivia that ends an entry onto the missing tokens it ends with, where the entry's trailing trivia is read from.</summary>
    /// <remarks>
    /// A missing token is created where the text lacks one, after the trivia of the token before it has been read. Left
    /// there, that trivia -- the line break above all -- would sit before the missing token, and anything asking the
    /// entry whether it ends its line would be told it does not. The text is the same either way.
    /// </remarks>
    private static GreenNode MoveLineEndOntoTrailingMissingTokens(GreenNode entry)
    {
        if (GetLastToken(entry, includeMissing: true) is not { IsMissing: true } || GetLastToken(entry, includeMissing: false) is not { TrailingTrivia: { } trivia } lastRealToken)
            return entry;

        var stripped = ReplaceLastToken(entry, lastRealToken, KeepSkippedText(lastRealToken, lastRealToken.WithTrivia(lastRealToken.LeadingTrivia, trailingTrivia: null)));
        var lastMissingToken = GetLastToken(stripped, includeMissing: true)!;

        return ReplaceLastToken(stripped, lastMissingToken, lastMissingToken.WithTrivia(lastMissingToken.LeadingTrivia, trivia));

        static GreenToken KeepSkippedText(GreenToken original, GreenToken copy)
            => original.ContainsSkippedText && !copy.ContainsSkippedText ? copy.AsSkippedText() : copy;
    }

    private static GreenToken? GetLastToken(GreenNode node, bool includeMissing)
    {
        if (node is GreenToken token)
            return includeMissing || !token.IsMissing ? token : null;

        for (var i = node.SlotCount - 1; i >= 0; i--)
        {
            if (node.GetSlot(i) is { } child && GetLastToken(child, includeMissing) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>Returns <paramref name="node"/> with the last occurrence of <paramref name="token"/> replaced.</summary>
    private static GreenNode ReplaceLastToken(GreenNode node, GreenToken token, GreenToken replacement)
    {
        if (ReferenceEquals(node, token))
            return replacement;

        if (node.IsToken)
            return node;

        for (var i = node.SlotCount - 1; i >= 0; i--)
        {
            if (node.GetSlot(i) is not { } child)
                continue;

            var newChild = ReplaceLastToken(child, token, replacement);
            if (ReferenceEquals(newChild, child))
                continue;

            var slots = new GreenNode?[node.SlotCount];
            for (var j = 0; j < slots.Length; j++)
            {
                slots[j] = node.GetSlot(j);
            }

            slots[i] = newChild;

            // A list of one token has to stay a list, which rebuilding it through its slots would not keep.
            return node.IsList ? SyntaxFactory.ListNode(slots)! : node.WithSlots(slots)!;
        }

        return node;
    }

    private bool IsTerminator(SyntaxKind kind, TerminatorState terminators) => kind switch
    {
        SyntaxKind.EndOfFileToken => true,
        SyntaxKind.CommaToken when (terminators & TerminatorState.Comma) != TerminatorState.None => true,
        SyntaxKind.CloseBracketToken when (terminators & TerminatorState.CloseBracket) != TerminatorState.None => true,
        SyntaxKind.CloseBraceToken when (terminators & TerminatorState.CloseBrace) != TerminatorState.None => true,
        _ => (terminators & TerminatorState.EndOfLine) != TerminatorState.None && _previousTokenEndedTheLine,
    };

    /// <summary>Determines whether a token kind can be part of a key: the kinds that can, a multi-line string (reported), or text that could not be read.</summary>
    private static bool IsKeyPart(SyntaxKind kind)
        => SyntaxFacts.IsKeyToken(kind) || kind is SyntaxKind.MultiLineBasicStringToken or SyntaxKind.MultiLineLiteralStringToken or SyntaxKind.BadToken;

    private static bool CanStartValue(SyntaxKind kind)
        => SyntaxFacts.GetValueKind(kind) != SyntaxKind.None || kind is SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken;

    private static TomlPropertySyntax MissingProperty(GreenNode? value)
        => new(
            new TomlKeySyntax(SyntaxFactory.ListNode([SyntaxFactory.MissingToken(SyntaxKind.BareKeyToken)])),
            SyntaxFactory.MissingToken(SyntaxKind.EqualsToken),
            value ?? new TomlSkippedValueSyntax(tokens: null));

    /// <summary>A separated list alternates, so the next thing after a node has to be a separator.</summary>
    private static bool EndsWithNode(List<GreenNode?> items) => items.Count % 2 == 1;

    private static bool EndsTheLine(GreenNode? trailingTrivia)
    {
        if (trailingTrivia is null)
            return false;

        var count = GreenNodeList.Count(trailingTrivia);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(trailingTrivia, i)?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return false;
    }

    private void AddErrorAtCurrentToken(DiagnosticDescriptor descriptor, params object?[] arguments)
        => _pending.Add(new PendingDiagnostic(_currentFullStart + _current.GetLeadingTriviaWidth(), _current.Text.Length, descriptor, arguments));

    /// <summary>Records an error for something the text does not have.</summary>
    /// <remarks>
    /// When the previous token ended its line, the missing thing belongs at the end of that line rather than in front
    /// of whatever begins the next one -- otherwise a missing value is reported against the key on the line below.
    /// </remarks>
    private void AddErrorForMissingToken(DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        if (_previousTokenEndedTheLine || CurrentKind == SyntaxKind.EndOfFileToken)
        {
            _pending.Add(new PendingDiagnostic(_previousTokenTextEnd, 0, descriptor, arguments));
            return;
        }

        _pending.Add(new PendingDiagnostic(_currentFullStart + _current.GetLeadingTriviaWidth(), 0, descriptor, arguments));
    }

    private GreenNode Finish(GreenNode node, int nodeFullStart, int mark)
    {
        if (_pending.Count == mark)
            return node;

        var infos = new SyntaxDiagnosticInfo[_pending.Count - mark];
        for (var i = 0; i < infos.Length; i++)
        {
            var pending = _pending[mark + i];
            infos[i] = new SyntaxDiagnosticInfo(Math.Max(0, pending.Position - nodeFullStart), pending.Width, pending.Descriptor, pending.Arguments);
        }

        _pending.RemoveRange(mark, infos.Length);

        return node.WithAdditionalDiagnostics(infos);
    }

    private readonly record struct PendingDiagnostic(int Position, int Width, DiagnosticDescriptor Descriptor, object?[] Arguments);
}
