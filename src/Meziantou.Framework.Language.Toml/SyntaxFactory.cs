using Meziantou.Framework.Language.Toml.Internals;
using Green = Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>Builds TOML nodes, tokens, and trivia.</summary>
/// <remarks>
/// <para>
/// A node built here is not part of any document, so its span starts at zero. Putting it into a tree with
/// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/> gives it a real position,
/// without re-reading any text.
/// </para>
/// <para>
/// The methods that take .NET values write them the way TOML requires: keys are quoted when they cannot be bare,
/// strings are escaped, and floats always read back as floats. The ones that take tokens use them as they are.
/// </para>
/// </remarks>
public static class SyntaxFactory
{
    /// <summary>A single space.</summary>
    public static SyntaxTrivia Space => Whitespace(" ");

    /// <summary>A single tab.</summary>
    public static SyntaxTrivia Tab => Whitespace("\t");

    /// <summary>A Unix line break.</summary>
    public static SyntaxTrivia LineFeed => Trivia(SyntaxKind.EndOfLineTrivia, "\n");

    /// <summary>A Windows line break.</summary>
    public static SyntaxTrivia CarriageReturnLineFeed => Trivia(SyntaxKind.EndOfLineTrivia, "\r\n");

    /// <summary>Creates whitespace trivia: one or more spaces and tabs, the only whitespace TOML has.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, or holds something other than spaces and tabs.</exception>
    public static SyntaxTrivia Whitespace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || text.AsSpan().ContainsAnyExcept(' ', '\t'))
            throw new ArgumentException("TOML whitespace is made of spaces and tabs.", nameof(text));

        return Trivia(SyntaxKind.WhitespaceTrivia, text);
    }

    /// <summary>Creates a line break: <c>\n</c> or <c>\r\n</c>, the only two TOML has.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is neither <c>\n</c> nor <c>\r\n</c>.</exception>
    public static SyntaxTrivia EndOfLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text is not ("\n" or "\r\n"))
            throw new ArgumentException("A TOML line break is '\\n' or '\\r\\n'.", nameof(text));

        return Trivia(SyntaxKind.EndOfLineTrivia, text);
    }

    /// <summary>Creates comment trivia.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is not a TOML comment: it does not start with <c>#</c>, or it holds a line break, a
    /// control character other than tab, or a lone surrogate.
    /// </exception>
    public static SyntaxTrivia Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text is ['#', ..] && !text.AsSpan().ContainsAnyInRange('\0', '\b') && !text.AsSpan().ContainsAnyInRange('\n', '\u001F') && !text.Contains('\u007F', StringComparison.Ordinal) && TomlFormatting.IsWellFormedUtf16(text))
            return Trivia(SyntaxKind.CommentTrivia, text);

        throw new ArgumentException("A TOML comment starts with '#', does not span lines, and holds no control character other than tab.", nameof(text));
    }

    /// <summary>Creates trivia of any kind, with <paramref name="text"/> used as it is.</summary>
    /// <remarks>
    /// Nothing checks that <paramref name="text"/> is what <paramref name="kind"/> says, which is what lets a tree hold
    /// exactly what a document has, mistakes included. Prefer <see cref="Whitespace"/>, <see cref="EndOfLine"/>, and
    /// <see cref="Comment"/>, which refuse text that would change what the tokens around it mean.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Trivia(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxTrivia(default, Green.SyntaxFactory.Trivia(kind, text), position: 0, index: 0);
    }

    public static SyntaxTriviaList TriviaList() => default;
    public static SyntaxTriviaList TriviaList(SyntaxTrivia trivia) => TriviaList([trivia]);
    public static SyntaxTriviaList TriviaList(params SyntaxTrivia[] trivia) => TriviaList((IEnumerable<SyntaxTrivia>)trivia);

    /// <exception cref="ArgumentNullException"><paramref name="trivia"/> is <see langword="null"/>.</exception>
    public static SyntaxTriviaList TriviaList(IEnumerable<SyntaxTrivia> trivia)
    {
        ArgumentNullException.ThrowIfNull(trivia);

        return new SyntaxTriviaList(default, SyntaxTriviaList.ToGreen(trivia), position: 0, index: 0);
    }

    /// <summary>Creates a token whose text is fixed by its kind, such as a bracket or <c>true</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not a token whose text is fixed, such as a key or a value.</exception>
    public static SyntaxToken Token(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.Token(FixedTextKind(kind)), position: 0, index: 0);

    /// <summary>Creates a token whose text is fixed by its kind, with trivia on either side.</summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not a token whose text is fixed, such as a key or a value.</exception>
    public static SyntaxToken Token(SyntaxTriviaList leading, SyntaxKind kind, SyntaxTriviaList trailing)
        => new(parent: null, Green.SyntaxFactory.Token(leading.Node, FixedTextKind(kind), trailing.Node), position: 0, index: 0);

    /// <summary>Creates a zero-width token standing in for one the text does not have.</summary>
    public static SyntaxToken MissingToken(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.MissingToken(kind), position: 0, index: 0);

    /// <summary>Creates a token holding text the parser could make no sense of.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken BadToken(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.BadToken(leading: null, text, trailing: null), position: 0, index: 0);
    }

    /// <summary>Creates one part of a key: a bare key when <paramref name="name"/> can be one, a basic string otherwise.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> holds a lone surrogate, which TOML cannot represent.</exception>
    public static SyntaxToken KeyPart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return SyntaxFacts.IsBareKey(name) ? ValueToken(SyntaxKind.BareKeyToken, name, name, name) : Literal(name);
    }

    /// <summary>Creates a basic string token holding <paramref name="value"/>, escaped as TOML requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> holds a lone surrogate, a character TOML cannot hold, escaped or not.
    /// </exception>
    public static SyntaxToken Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TomlFormatting.IsWellFormedUtf16(value))
            throw new ArgumentException("The string holds a lone surrogate, which TOML cannot represent.", nameof(value));

        return ValueToken(SyntaxKind.BasicStringToken, TomlFormatting.QuoteBasicString(value), value, value);
    }

    /// <summary>Creates an integer token, written in decimal.</summary>
    public static SyntaxToken Literal(long value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);

        return ValueToken(SyntaxKind.IntegerToken, text, value, text);
    }

    /// <summary>Creates a float token, written so that it reads back as the same float.</summary>
    public static SyntaxToken Literal(double value)
        => ValueToken(SyntaxKind.FloatToken, TomlFormatting.FormatFloat(value), value, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates <c>true</c> or <c>false</c>.</summary>
    public static SyntaxToken Literal(bool value) => Token(value ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword);

    /// <summary>Creates an offset date-time token, such as <c>1979-05-27T07:32:00Z</c>.</summary>
    public static SyntaxToken Literal(DateTimeOffset value)
    {
        var text = TomlFormatting.FormatDateTime(value);

        return ValueToken(SyntaxKind.OffsetDateTimeToken, text, value, text);
    }

    /// <summary>Creates a local date-time token, such as <c>1979-05-27T07:32:00</c>. The kind of <paramref name="value"/> is ignored.</summary>
    public static SyntaxToken Literal(DateTime value)
    {
        var text = TomlFormatting.FormatDateTime(value);

        return ValueToken(SyntaxKind.LocalDateTimeToken, text, DateTime.SpecifyKind(value, DateTimeKind.Unspecified), text);
    }

    /// <summary>Creates a local date token, such as <c>1979-05-27</c>.</summary>
    public static SyntaxToken Literal(DateOnly value)
    {
        var text = TomlFormatting.FormatDate(value);

        return ValueToken(SyntaxKind.LocalDateToken, text, value, text);
    }

    /// <summary>Creates a local time token, such as <c>07:32:00</c>.</summary>
    public static SyntaxToken Literal(TimeOnly value)
    {
        var text = TomlFormatting.FormatTime(value);

        return ValueToken(SyntaxKind.LocalTimeToken, text, value, text);
    }

    public static SyntaxTokenList TokenList() => default;
    public static SyntaxTokenList TokenList(SyntaxToken token) => TokenList([token]);
    public static SyntaxTokenList TokenList(params SyntaxToken[] tokens) => TokenList((IEnumerable<SyntaxToken>)tokens);

    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    public static SyntaxTokenList TokenList(IEnumerable<SyntaxToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return new SyntaxTokenList(tokens);
    }

    public static SyntaxList<TNode> List<TNode>()
        where TNode : TomlSyntaxNode
        => default;

    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static SyntaxList<TNode> List<TNode>(IEnumerable<TNode> nodes)
        where TNode : TomlSyntaxNode
        => new(nodes);

    public static SyntaxList<TNode> SingletonList<TNode>(TNode node)
        where TNode : TomlSyntaxNode
        => new(node);

    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>()
        where TNode : TomlSyntaxNode
        => default;

    /// <summary>Creates a list of <paramref name="nodes"/> with a comma and a space between each pair.</summary>
    /// <remarks>
    /// A node that ends with a comment, such as one from <see cref="ParseValue"/>, gets a line break after it, or the
    /// comment would hide the comma or the bracket that follows it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> or one of its items is <see langword="null"/>.</exception>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<TNode> nodes)
        where TNode : TomlSyntaxNode
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var items = new List<SyntaxNodeOrToken>();
        foreach (var node in nodes)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(nodes), "The list cannot hold null.");

            if (items.Count > 0)
            {
                items.Add(Token(TriviaList(), SyntaxKind.CommaToken, TriviaList(Space)));
            }

            items.Add(EndCommentLine(node));
        }

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(items));
    }

    /// <summary>Creates a list from nodes and the separators to put between them.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodesAndSeparators"/> is <see langword="null"/>.</exception>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<SyntaxNodeOrToken> nodesAndSeparators)
        where TNode : TomlSyntaxNode
        => new(nodesAndSeparators);

    public static SeparatedSyntaxList<TNode> SingletonSeparatedList<TNode>(TNode node)
        where TNode : TomlSyntaxNode
        => new(new SyntaxNodeOrTokenList([node]));

    /// <summary>Creates a document from <paramref name="entries"/>, ending the line of each that does not end its own.</summary>
    public static TomlDocumentSyntax TomlDocument(params TomlEntrySyntax[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return TomlDocument(List(entries.Select(EndLine)), Token(SyntaxKind.EndOfFileToken));
    }

    /// <summary>Creates a document from <paramref name="entries"/>.</summary>
    /// <remarks>
    /// An entry followed by another one on the same line gets a line break after it, and so does a comment in front of
    /// an entry, or the text would read as something else. The last entry is left as it is.
    /// </remarks>
    public static TomlDocumentSyntax TomlDocument(SyntaxList<TomlEntrySyntax> entries, SyntaxToken endOfFileToken)
        => (TomlDocumentSyntax)Green.TomlDocumentSyntax.Create(entries.Green, endOfFileToken.Node ?? Green.SyntaxFactory.Token(SyntaxKind.EndOfFileToken)).CreateRed();

    /// <summary>Creates a key from the names of its parts, quoting each that cannot be bare.</summary>
    /// <example><c>Key("site", "google.com")</c> is <c>site."google.com"</c>.</example>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> or one of its items is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    public static TomlKeySyntax Key(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0)
            throw new ArgumentException("A key has at least one part.", nameof(names));

        var tokens = new List<SyntaxToken>(names.Length * 2);
        foreach (var name in names)
        {
            if (tokens.Count > 0)
            {
                tokens.Add(Token(SyntaxKind.DotToken));
            }

            tokens.Add(KeyPart(name));
        }

        return Key(TokenList(tokens));
    }

    /// <summary>Creates a key from its parts and the dots between them.</summary>
    public static TomlKeySyntax Key(SyntaxTokenList tokens)
        => (TomlKeySyntax)new Green.TomlKeySyntax(TokenListNode(tokens)).CreateRed();

    /// <summary>Creates a table header such as <c>[server]</c>, or <c>[server.http]</c> from several names.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    public static TomlTableSyntax TomlTable(params string[] names)
        => TomlTable(Token(SyntaxKind.OpenBracketToken), Key(names), Token(SyntaxKind.CloseBracketToken));

    /// <summary>Creates an array-of-tables header such as <c>[[products]]</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    public static TomlTableSyntax TomlArrayOfTables(params string[] names)
        => TomlTable(Token(SyntaxKind.OpenBracketOpenBracketToken), Key(names), Token(SyntaxKind.CloseBracketCloseBracketToken));

    /// <summary>Creates a table header, or an array-of-tables header when <paramref name="openBracketToken"/> is <c>[[</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static TomlTableSyntax TomlTable(SyntaxToken openBracketToken, TomlKeySyntax key, SyntaxToken closeBracketToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var isArrayOfTables = openBracketToken.IsKind(SyntaxKind.OpenBracketOpenBracketToken);
        var kind = isArrayOfTables ? SyntaxKind.TomlArrayOfTables : SyntaxKind.TomlTable;
        var closeKind = isArrayOfTables ? SyntaxKind.CloseBracketCloseBracketToken : SyntaxKind.CloseBracketToken;

        return (TomlTableSyntax)new Green.TomlTableSyntax(kind, Required(openBracketToken, SyntaxKind.OpenBracketToken), key.Green, Required(closeBracketToken, closeKind)).CreateRed();
    }

    /// <summary>Creates <c>name = value</c>, quoting <paramref name="name"/> when it cannot be a bare key.</summary>
    /// <remarks><paramref name="name"/> is one name, even if it has dots in it. Use <see cref="Key(string[])"/> for a dotted key.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public static TomlPropertySyntax TomlProperty(string name, TomlValueSyntax value) => TomlProperty(Key(name), value);

    /// <summary>Creates <c>key = value</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> starts with a line break or a comment, which would put it on a line of its own.
    /// </exception>
    public static TomlPropertySyntax TomlProperty(TomlKeySyntax key, TomlValueSyntax value)
        => TomlProperty(key.WithTrailingTrivia(Space), Token(TriviaList(), SyntaxKind.EqualsToken, TriviaList(Space)), value);

    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> starts with a line break or a comment, which would put it on a line of its own.
    /// </exception>
    public static TomlPropertySyntax TomlProperty(TomlKeySyntax key, SyntaxToken equalsToken, TomlValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        // The value of a key/value pair has to start on the line of its '='. One that comes from a multi-line array, or
        // from ParseValue, can have a line break or a comment in front of it, which the pair cannot hold.
        if (value.GetLeadingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia) || trivia.IsKind(SyntaxKind.CommentTrivia)))
            throw new ArgumentException("The value of a key/value pair has to start on the same line as its '=', so it cannot start with a line break or a comment.", nameof(value));

        return (TomlPropertySyntax)new Green.TomlPropertySyntax(key.Green, Required(equalsToken, SyntaxKind.EqualsToken), value.Green).CreateRed();
    }

    /// <summary>Creates an array such as <c>[1, 2, 3]</c>.</summary>
    public static TomlArraySyntax TomlArray(params TomlValueSyntax[] values) => TomlArray(SeparatedList(values));

    public static TomlArraySyntax TomlArray(SeparatedSyntaxList<TomlValueSyntax> elements)
        => TomlArray(Token(SyntaxKind.OpenBracketToken), elements, Token(SyntaxKind.CloseBracketToken));

    public static TomlArraySyntax TomlArray(SyntaxToken openBracketToken, SeparatedSyntaxList<TomlValueSyntax> elements, SyntaxToken closeBracketToken)
        => (TomlArraySyntax)new Green.TomlArraySyntax(Required(openBracketToken, SyntaxKind.OpenBracketToken), elements.Green, Required(closeBracketToken, SyntaxKind.CloseBracketToken)).CreateRed();

    /// <summary>Creates an inline table such as <c>{ x = 1, y = 2 }</c>.</summary>
    public static TomlInlineTableSyntax TomlInlineTable(params TomlPropertySyntax[] properties) => TomlInlineTable(SeparatedList(properties));

    /// <summary>Creates an inline table with a space inside each brace, or <c>{}</c> when it is empty.</summary>
    public static TomlInlineTableSyntax TomlInlineTable(SeparatedSyntaxList<TomlPropertySyntax> properties)
        => properties.Count == 0
            ? TomlInlineTable(Token(SyntaxKind.OpenBraceToken), properties, Token(SyntaxKind.CloseBraceToken))
            : TomlInlineTable(Token(TriviaList(), SyntaxKind.OpenBraceToken, TriviaList(Space)), properties, Token(TriviaList(Space), SyntaxKind.CloseBraceToken, TriviaList()));

    public static TomlInlineTableSyntax TomlInlineTable(SyntaxToken openBraceToken, SeparatedSyntaxList<TomlPropertySyntax> properties, SyntaxToken closeBraceToken)
        => (TomlInlineTableSyntax)new Green.TomlInlineTableSyntax(Required(openBraceToken, SyntaxKind.OpenBraceToken), properties.Green, Required(closeBraceToken, SyntaxKind.CloseBraceToken)).CreateRed();

    /// <summary>Creates a basic string holding <paramref name="value"/>, escaped as TOML requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static TomlStringSyntax TomlString(string value) => TomlString(Literal(value));

    /// <summary>Creates a string from a token of any of the four string kinds.</summary>
    /// <exception cref="ArgumentException"><paramref name="stringToken"/> is not a string.</exception>
    public static TomlStringSyntax TomlString(SyntaxToken stringToken)
        => (TomlStringSyntax)Scalar(stringToken, SyntaxKind.TomlString, nameof(stringToken));

    public static TomlIntegerSyntax TomlInteger(long value) => TomlInteger(Literal(value));

    /// <exception cref="ArgumentException"><paramref name="integerToken"/> is not an integer.</exception>
    public static TomlIntegerSyntax TomlInteger(SyntaxToken integerToken)
        => (TomlIntegerSyntax)Scalar(integerToken, SyntaxKind.TomlInteger, nameof(integerToken));

    public static TomlFloatSyntax TomlFloat(double value) => TomlFloat(Literal(value));

    /// <exception cref="ArgumentException"><paramref name="floatToken"/> is not a float.</exception>
    public static TomlFloatSyntax TomlFloat(SyntaxToken floatToken)
        => (TomlFloatSyntax)Scalar(floatToken, SyntaxKind.TomlFloat, nameof(floatToken));

    public static TomlBooleanSyntax TomlBoolean(bool value) => TomlBoolean(Literal(value));

    /// <exception cref="ArgumentException"><paramref name="booleanToken"/> is neither <c>true</c> nor <c>false</c>.</exception>
    public static TomlBooleanSyntax TomlBoolean(SyntaxToken booleanToken)
        => (TomlBooleanSyntax)Scalar(booleanToken, SyntaxKind.TomlBoolean, nameof(booleanToken));

    public static TomlDateTimeSyntax TomlDateTime(DateTimeOffset value) => TomlDateTime(Literal(value));
    public static TomlDateTimeSyntax TomlDateTime(DateTime value) => TomlDateTime(Literal(value));
    public static TomlDateTimeSyntax TomlDateTime(DateOnly value) => TomlDateTime(Literal(value));
    public static TomlDateTimeSyntax TomlDateTime(TimeOnly value) => TomlDateTime(Literal(value));

    /// <summary>Creates a date, a time, or both, taking the kind of the node from the token.</summary>
    /// <exception cref="ArgumentException"><paramref name="dateTimeToken"/> is not a date or a time.</exception>
    public static TomlDateTimeSyntax TomlDateTime(SyntaxToken dateTimeToken)
    {
        if (!SyntaxFacts.IsDateTimeToken(dateTimeToken.Kind()))
            throw new ArgumentException("The token is not a date or a time.", nameof(dateTimeToken));

        return (TomlDateTimeSyntax)new Green.TomlLiteralSyntax(SyntaxFacts.GetValueKind(dateTimeToken.Kind()), dateTimeToken.Node!).CreateRed();
    }

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSkippedTextSyntax TomlSkippedText(string text) => TomlSkippedText(TokenList(BadToken(text)));

    public static TomlSkippedTextSyntax TomlSkippedText(SyntaxTokenList tokens)
        => (TomlSkippedTextSyntax)new Green.TomlSkippedTextSyntax(TokenListNode(tokens)).CreateRed();

    /// <summary>Creates a value that holds <paramref name="tokens"/> as they are, or a missing value when there are none.</summary>
    public static TomlSkippedValueSyntax TomlSkippedValue(SyntaxTokenList tokens)
        => (TomlSkippedValueSyntax)new Green.TomlSkippedValueSyntax(TokenListNode(tokens)).CreateRed();

    /// <summary>Parses <paramref name="text"/> into a tree.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseSyntaxTree(string text, TomlParseOptions? options = null, string? path = null) => TomlSyntaxTree.ParseText(text, options, path);

    /// <summary>Parses <paramref name="text"/> and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlDocumentSyntax ParseDocument(string text, TomlParseOptions? options = null) => TomlSyntaxTree.ParseText(text, options).GetRoot();

    /// <summary>Parses <paramref name="text"/> as a single value, such as <c>[1, 2]</c> or <c>'''literal'''</c>.</summary>
    /// <remarks>
    /// Text after the value makes the whole of it a <see cref="TomlSkippedValueSyntax"/>. Whether
    /// <paramref name="text"/> held one value the grammar accepts and nothing else is what
    /// <see cref="SyntaxNode.ContainsDiagnostics"/> says. A key defined twice in an inline table is not a grammar
    /// mistake: <see cref="TomlSyntaxTree.GetDiagnostics()"/> reports it once the value is part of a document.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlValueSyntax ParseValue(string text, TomlParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return (TomlValueSyntax)new Green.LanguageParser(SourceText.From(text), options ?? TomlParseOptions.Default).ParseStandaloneValue().CreateRed();
    }

    /// <summary>Determines whether the two nodes have the same structure and text.</summary>
    public static bool AreEquivalent(TomlSyntaxNode? oldNode, TomlSyntaxNode? newNode)
        => oldNode is null ? newNode is null : oldNode.IsEquivalentTo(newNode);

    /// <summary>Returns <paramref name="list"/> with <paramref name="items"/> added at its end, laid out the way the list already is.</summary>
    /// <remarks>
    /// <para>
    /// A list written on one line gets a comma and a space before each new item, and what came after its last item,
    /// such as the space before a closing brace, goes after the new last one. A list whose last item ends its line gets
    /// each new item on a line of its own, indented as the last item is, and the comment after the last item stays on
    /// its line. A list that ends with a comma still does.
    /// </para>
    /// <para>
    /// The commas are new ones rather than copies of those the list has, which could carry a comment of their own.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or one of its items is <see langword="null"/>.</exception>
    internal static SeparatedSyntaxList<TNode> AddToList<TNode>(SeparatedSyntaxList<TNode> list, TNode[] items)
        where TNode : TomlSyntaxNode
    {
        ArgumentNullException.ThrowIfNull(items);
        if (Array.Exists(items, item => item is null))
            throw new ArgumentNullException(nameof(items), "The list cannot hold null.");

        if (items.Length == 0)
            return list;

        if (list.Count == 0)
            return SeparatedList(items);

        var result = new List<SyntaxNodeOrToken>(list.GetWithSeparators());
        var last = result[^1];
        var tail = last.GetTrailingTrivia();
        var endsTrailingComma = list.HasTrailingSeparator;
        if (!tail.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            result[^1] = WithTrailingTrivia(last, endsTrailingComma ? [Space] : []);
            for (var i = 0; i < items.Length; i++)
            {
                if (i > 0 || !endsTrailingComma)
                {
                    result.Add(Comma(Space));
                }

                var item = EndCommentLine(items[i]);
                result.Add(i == items.Length - 1 && !endsTrailingComma ? item.WithTrailingTrivia([.. item.GetTrailingTrivia(), .. tail]) : item);
            }

            if (endsTrailingComma)
            {
                result.Add(Comma([.. tail]));
            }
        }
        else
        {
            var endOfLine = tail.First(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var lastLeading = list[^1].GetLeadingTrivia();
            var lastLineBreak = lastLeading.LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            SyntaxTrivia[] indentation = [.. lastLeading.Skip(lastLineBreak.RawKind == 0 ? 0 : lastLeading.IndexOf(lastLineBreak) + 1).Where(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))];
            if (!endsTrailingComma)
            {
                // The comma goes right after the last item, and the comment after it stays on that line.
                result[^1] = WithTrailingTrivia(last, []);
                result.Add(Comma([.. tail]));
            }

            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i].GetLeadingTrivia().Count == 0 ? items[i].WithLeadingTrivia(indentation) : items[i];
                item = EndCommentLine(item);
                if (i < items.Length - 1 || endsTrailingComma)
                {
                    result.Add(item);
                    result.Add(Comma(endOfLine));
                }
                else
                {
                    result.Add(item.GetTrailingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)) ? item : item.WithTrailingTrivia([.. item.GetTrailingTrivia(), endOfLine]));
                }
            }
        }

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(result));

        static SyntaxToken Comma(params SyntaxTrivia[] trailing) => Token(TriviaList(), SyntaxKind.CommaToken, TriviaList(trailing));

        static SyntaxNodeOrToken WithTrailingTrivia(SyntaxNodeOrToken item, SyntaxTrivia[] trivia)
            => item.AsNode(out var node) ? node.WithTrailingTrivia(trivia) : item.AsToken().WithTrailingTrivia(trivia);
    }

    /// <summary>Returns <paramref name="node"/> with a line break after its trailing comment, when it ends with one that has none.</summary>
    private static TNode EndCommentLine<TNode>(TNode node)
        where TNode : TomlSyntaxNode
    {
        var trailing = node.GetTrailingTrivia();
        var last = trailing.LastOrDefault(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia));
        if (!last.IsKind(SyntaxKind.CommentTrivia))
            return node;

        return node.WithTrailingTrivia([.. trailing, LineFeed]);
    }

    /// <summary>Returns <paramref name="kind"/>, refusing a kind whose text is not fixed and so cannot be made from the kind alone.</summary>
    private static SyntaxKind FixedTextKind(SyntaxKind kind)
    {
        if (kind != SyntaxKind.EndOfFileToken && SyntaxFacts.GetText(kind).Length == 0)
            throw new ArgumentException($"A {kind} has no fixed text. Use {nameof(Literal)}, {nameof(KeyPart)}, {nameof(ParseValue)}, or {nameof(BadToken)}.", nameof(kind));

        return kind;
    }

    /// <summary>Returns <paramref name="entry"/> ending with a line break, which every entry of a document needs.</summary>
    internal static TomlEntrySyntax EndLine(TomlEntrySyntax entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var trailing = entry.GetTrailingTrivia();
        if (trailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            return entry;

        return entry.WithTrailingTrivia([.. trailing, LineFeed]);
    }

    /// <summary>Gets the node a list of tokens is held in, which stays a list even for a single token, as the parser builds it.</summary>
    private static Meziantou.Framework.Language.InternalSyntax.GreenNode? TokenListNode(SyntaxTokenList tokens)
        => Green.SyntaxFactory.ListNode(Meziantou.Framework.Language.InternalSyntax.GreenNodeList.ToArray(tokens.Node));

    private static SyntaxToken ValueToken<TValue>(SyntaxKind kind, string text, TValue value, string valueText)
        => new(parent: null, Green.SyntaxFactory.TokenWithValue(leading: null, kind, text, value, valueText, trailing: null), position: 0, index: 0);

    private static TomlValueSyntax Scalar(SyntaxToken token, SyntaxKind nodeKind, string parameterName)
    {
        if (SyntaxFacts.GetValueKind(token.Kind()) != nodeKind)
            throw new ArgumentException($"A {nodeKind} cannot hold a {token.Kind()}.", parameterName);

        return (TomlValueSyntax)new Green.TomlLiteralSyntax(nodeKind, token.Node!).CreateRed();
    }

    private static Meziantou.Framework.Language.InternalSyntax.GreenNode Required(SyntaxToken token, SyntaxKind kind)
        => token.Node ?? Green.SyntaxFactory.MissingToken(kind);
}
