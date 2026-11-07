using Green = Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>Builds JSON nodes, tokens, and trivia.</summary>
/// <remarks>
/// A node built here is not part of any document, so its span starts at zero. Putting it into a tree with
/// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/> gives it a real position,
/// without re-reading any text.
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

    /// <summary>Creates comment trivia, choosing the kind from how <paramref name="text"/> starts.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not a comment.</exception>
    public static SyntaxTrivia Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.StartsWith("//", StringComparison.Ordinal))
            return Trivia(SyntaxKind.SingleLineCommentTrivia, text);

        if (text.StartsWith("/*", StringComparison.Ordinal))
            return Trivia(SyntaxKind.MultiLineCommentTrivia, text);

        throw new ArgumentException("A comment starts with '//' or '/*'.", nameof(text));
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

    /// <summary>Creates a token whose text is fixed by its kind, such as a brace or a keyword.</summary>
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

    /// <summary>Creates a string token holding <paramref name="value"/>, escaped as JSON requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.StringToken, Escape(value), value, trailing: null), position: 0, index: 0);
    }

    /// <summary>Creates a string token spelled <paramref name="text"/> and meaning <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Literal(string text, string value)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(value);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.StringToken, text, value, trailing: null), position: 0, index: 0);
    }

    /// <summary>Creates a number token written exactly as <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken NumberToken(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.Token(leading: null, SyntaxKind.NumberToken, text, trailing: null), position: 0, index: 0);
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
        where TNode : JsonSyntaxNode
        => default;

    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static SyntaxList<TNode> List<TNode>(IEnumerable<TNode> nodes)
        where TNode : JsonSyntaxNode
        => new(nodes);

    public static SyntaxList<TNode> SingletonList<TNode>(TNode node)
        where TNode : JsonSyntaxNode
        => new(node);

    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>()
        where TNode : JsonSyntaxNode
        => default;

    /// <summary>Creates a list of <paramref name="nodes"/> with a comma between each pair.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<TNode> nodes)
        where TNode : JsonSyntaxNode
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var items = new List<SyntaxNodeOrToken>();
        foreach (var node in nodes)
        {
            if (items.Count > 0)
            {
                items.Add(Token(SyntaxKind.CommaToken));
            }

            items.Add(node);
        }

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(items));
    }

    /// <summary>Creates a list from nodes and the separators to put between them.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodesAndSeparators"/> is <see langword="null"/>.</exception>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<SyntaxNodeOrToken> nodesAndSeparators)
        where TNode : JsonSyntaxNode
        => new(nodesAndSeparators);

    public static SeparatedSyntaxList<TNode> SingletonSeparatedList<TNode>(TNode node)
        where TNode : JsonSyntaxNode
        => new(new SyntaxNodeOrTokenList([node]));

    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static JsonDocumentSyntax JsonDocument(JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return JsonDocument(SingletonList(value), Token(SyntaxKind.EndOfFileToken));
    }

    public static JsonDocumentSyntax JsonDocument(SyntaxList<JsonValueSyntax> values, SyntaxToken endOfFileToken)
        => (JsonDocumentSyntax)new Green.JsonDocumentSyntax(values.Green, endOfFileToken.Node ?? Green.SyntaxFactory.Token(SyntaxKind.EndOfFileToken)).CreateRed();

    public static JsonObjectSyntax JsonObject(params JsonMemberSyntax[] members) => JsonObject(SeparatedList(members));

    public static JsonObjectSyntax JsonObject(SeparatedSyntaxList<JsonMemberSyntax> members)
        => JsonObject(Token(SyntaxKind.OpenBraceToken), members, Token(SyntaxKind.CloseBraceToken));

    public static JsonObjectSyntax JsonObject(SyntaxToken openBraceToken, SeparatedSyntaxList<JsonMemberSyntax> members, SyntaxToken closeBraceToken)
        => (JsonObjectSyntax)new Green.JsonObjectSyntax(Required(openBraceToken, SyntaxKind.OpenBraceToken), members.Green, Required(closeBraceToken, SyntaxKind.CloseBraceToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public static JsonMemberSyntax JsonMember(string name, JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(name);

        return JsonMember(Literal(name), Token(SyntaxKind.ColonToken), value);
    }

    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static JsonMemberSyntax JsonMember(SyntaxToken nameToken, SyntaxToken colonToken, JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return (JsonMemberSyntax)new Green.JsonMemberSyntax(Required(nameToken, SyntaxKind.StringToken), Required(colonToken, SyntaxKind.ColonToken), value.Green).CreateRed();
    }

    public static JsonArraySyntax JsonArray(params JsonValueSyntax[] values) => JsonArray(SeparatedList(values));

    public static JsonArraySyntax JsonArray(SeparatedSyntaxList<JsonValueSyntax> elements)
        => JsonArray(Token(SyntaxKind.OpenBracketToken), elements, Token(SyntaxKind.CloseBracketToken));

    public static JsonArraySyntax JsonArray(SyntaxToken openBracketToken, SeparatedSyntaxList<JsonValueSyntax> elements, SyntaxToken closeBracketToken)
        => (JsonArraySyntax)new Green.JsonArraySyntax(Required(openBracketToken, SyntaxKind.OpenBracketToken), elements.Green, Required(closeBracketToken, SyntaxKind.CloseBracketToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static JsonStringSyntax JsonString(string value) => JsonString(Literal(value));

    public static JsonStringSyntax JsonString(SyntaxToken stringToken)
        => (JsonStringSyntax)new Green.JsonStringSyntax(Required(stringToken, SyntaxKind.StringToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonNumberSyntax JsonNumber(string text) => JsonNumber(NumberToken(text));

    public static JsonNumberSyntax JsonNumber(SyntaxToken numberToken)
        => (JsonNumberSyntax)new Green.JsonNumberSyntax(Required(numberToken, SyntaxKind.NumberToken)).CreateRed();

    /// <summary>Creates <c>true</c>, <c>false</c>, or <c>null</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not one of the three literal kinds.</exception>
    public static JsonLiteralSyntax JsonLiteral(SyntaxKind kind) => JsonLiteral(Token(KeywordFor(kind)));

    /// <summary>Creates a literal from a keyword token, taking the kind of the node from the keyword.</summary>
    /// <exception cref="ArgumentException"><paramref name="literalToken"/> is not a literal keyword.</exception>
    public static JsonLiteralSyntax JsonLiteral(SyntaxToken literalToken)
    {
        var kind = SyntaxFacts.GetLiteralExpression(literalToken.Kind());
        if (kind == SyntaxKind.None)
            throw new ArgumentException("A JSON literal is one of 'true', 'false', or 'null'.", nameof(literalToken));

        return (JsonLiteralSyntax)new Green.JsonLiteralSyntax(kind, literalToken.Node!).CreateRed();
    }

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonSkippedTextSyntax JsonSkippedText(string text) => JsonSkippedText(TokenList(BadToken(text)));

    public static JsonSkippedTextSyntax JsonSkippedText(SyntaxTokenList tokens)
        => (JsonSkippedTextSyntax)new Green.JsonSkippedTextSyntax(tokens.Node).CreateRed();

    /// <summary>Parses <paramref name="text"/> into a tree.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxTree ParseSyntaxTree([StringSyntax(StringSyntaxAttribute.Json)] string text, string? path = null) => JsonSyntaxTree.ParseText(text, path);

    /// <summary>Parses <paramref name="text"/> and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonDocumentSyntax ParseDocument([StringSyntax(StringSyntaxAttribute.Json)] string text) => JsonSyntaxTree.ParseText(text).GetRoot();

    /// <summary>Parses <paramref name="text"/> and returns its root value, or <see langword="null"/> when it has none.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonValueSyntax? ParseValue([StringSyntax(StringSyntaxAttribute.Json)] string text) => ParseDocument(text).Value;

    /// <summary>Determines whether the two nodes have the same structure and text.</summary>
    public static bool AreEquivalent(JsonSyntaxNode? oldNode, JsonSyntaxNode? newNode)
        => oldNode is null ? newNode is null : oldNode.IsEquivalentTo(newNode);

    private static SyntaxKind KeywordFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.JsonTrueLiteral => SyntaxKind.TrueKeyword,
        SyntaxKind.JsonFalseLiteral => SyntaxKind.FalseKeyword,
        SyntaxKind.JsonNullLiteral => SyntaxKind.NullKeyword,
        _ => throw new ArgumentException("A JSON literal is one of 'true', 'false', or 'null'.", nameof(kind)),
    };

    private static Meziantou.Framework.Language.InternalSyntax.GreenNode Required(SyntaxToken token, SyntaxKind kind)
        => token.Node ?? Green.SyntaxFactory.MissingToken(kind);

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');

        return builder.ToString();
    }
}
