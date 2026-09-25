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

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Whitespace(string text) => Trivia(SyntaxKind.WhitespaceTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia EndOfLine(string text) => Trivia(SyntaxKind.EndOfLineTrivia, text);

    /// <summary>Creates comment trivia.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not a TOML comment.</exception>
    public static SyntaxTrivia Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text is ['#', ..] && !text.Contains('\n', StringComparison.Ordinal) && !text.Contains('\r', StringComparison.Ordinal))
            return Trivia(SyntaxKind.CommentTrivia, text);

        throw new ArgumentException("A TOML comment starts with '#' and does not span lines.", nameof(text));
    }

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
    public static SyntaxToken Token(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.Token(kind), position: 0, index: 0);

    /// <summary>Creates a token with trivia on either side.</summary>
    public static SyntaxToken Token(SyntaxTriviaList leading, SyntaxKind kind, SyntaxTriviaList trailing)
        => new(parent: null, Green.SyntaxFactory.Token(leading.Node, kind, trailing.Node), position: 0, index: 0);

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
    public static SyntaxToken KeyPart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return SyntaxFacts.IsBareKey(name) ? ValueToken(SyntaxKind.BareKeyToken, name, name, name) : Literal(name);
    }

    /// <summary>Creates a basic string token holding <paramref name="value"/>, escaped as TOML requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

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
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<TNode> nodes)
        where TNode : TomlSyntaxNode
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var items = new List<SyntaxNodeOrToken>();
        foreach (var node in nodes)
        {
            if (items.Count > 0)
            {
                items.Add(Token(TriviaList(), SyntaxKind.CommaToken, TriviaList(Space)));
            }

            items.Add(node);
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

    public static TomlDocumentSyntax TomlDocument(SyntaxList<TomlEntrySyntax> entries, SyntaxToken endOfFileToken)
        => (TomlDocumentSyntax)new Green.TomlDocumentSyntax(entries.Green, endOfFileToken.Node ?? Green.SyntaxFactory.Token(SyntaxKind.EndOfFileToken)).CreateRed();

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
        => (TomlKeySyntax)new Green.TomlKeySyntax(tokens.Node).CreateRed();

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
    public static TomlPropertySyntax TomlProperty(TomlKeySyntax key, TomlValueSyntax value)
        => TomlProperty(key.WithTrailingTrivia(Space), Token(TriviaList(), SyntaxKind.EqualsToken, TriviaList(Space)), value);

    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public static TomlPropertySyntax TomlProperty(TomlKeySyntax key, SyntaxToken equalsToken, TomlValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

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
        => (TomlSkippedTextSyntax)new Green.TomlSkippedTextSyntax(tokens.Node).CreateRed();

    /// <summary>Creates a value that holds <paramref name="tokens"/> as they are, or a missing value when there are none.</summary>
    public static TomlSkippedValueSyntax TomlSkippedValue(SyntaxTokenList tokens)
        => (TomlSkippedValueSyntax)new Green.TomlSkippedValueSyntax(tokens.Node).CreateRed();

    /// <summary>Parses <paramref name="text"/> into a tree.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseSyntaxTree(string text, TomlParseOptions? options = null, string? path = null) => TomlSyntaxTree.ParseText(text, options, path);

    /// <summary>Parses <paramref name="text"/> and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlDocumentSyntax ParseDocument(string text, TomlParseOptions? options = null) => TomlSyntaxTree.ParseText(text, options).GetRoot();

    /// <summary>Parses <paramref name="text"/> as a single value, such as <c>[1, 2]</c> or <c>'''literal'''</c>.</summary>
    /// <remarks>
    /// Text after the value makes the whole of it a <see cref="TomlSkippedValueSyntax"/>. Whether
    /// <paramref name="text"/> held one valid value and nothing else is what <see cref="SyntaxNode.ContainsDiagnostics"/> says.
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

    /// <summary>Returns <paramref name="entry"/> ending with a line break, which every entry of a document needs.</summary>
    internal static TomlEntrySyntax EndLine(TomlEntrySyntax entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var trailing = entry.GetTrailingTrivia();
        if (trailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            return entry;

        return entry.WithTrailingTrivia([.. trailing, LineFeed]);
    }

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
