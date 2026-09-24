using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>Builds INI nodes, tokens, and trivia.</summary>
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

    /// <summary>Creates comment trivia.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not an INI comment.</exception>
    public static SyntaxTrivia Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text is [';' or '#', ..])
            return Trivia(SyntaxKind.CommentTrivia, text);

        throw new ArgumentException("An INI comment starts with ';' or '#'.", nameof(text));
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

    /// <summary>Creates a token whose text is fixed by its kind, such as a bracket or separator.</summary>
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

    /// <summary>Creates a key token written exactly as <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Key(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.Token(leading: null, SyntaxKind.KeyToken, text, trailing: null), position: 0, index: 0);
    }

    /// <summary>Creates a value token written exactly as <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Value(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.Token(leading: null, SyntaxKind.ValueToken, text, trailing: null), position: 0, index: 0);
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
        where TNode : IniSyntaxNode
        => default;

    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static SyntaxList<TNode> List<TNode>(IEnumerable<TNode> nodes)
        where TNode : IniSyntaxNode
        => new(nodes);

    public static SyntaxList<TNode> SingletonList<TNode>(TNode node)
        where TNode : IniSyntaxNode
        => new(node);

    public static IniDocumentSyntax IniDocument(params IniEntrySyntax[] entries) => IniDocument(List(entries), Token(SyntaxKind.EndOfFileToken));

    public static IniDocumentSyntax IniDocument(SyntaxList<IniEntrySyntax> entries, SyntaxToken endOfFileToken)
        => (IniDocumentSyntax)new Green.IniDocumentSyntax(entries.Green, endOfFileToken.Node ?? Green.SyntaxFactory.Token(SyntaxKind.EndOfFileToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static IniSectionSyntax IniSection(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return IniSection(Token(SyntaxKind.OpenBracketToken), Key(name), Token(SyntaxKind.CloseBracketToken));
    }

    public static IniSectionSyntax IniSection(SyntaxToken openBracketToken, SyntaxToken nameToken, SyntaxToken closeBracketToken)
        => (IniSectionSyntax)new Green.IniSectionSyntax(Required(openBracketToken, SyntaxKind.OpenBracketToken), Required(nameToken, SyntaxKind.KeyToken), Required(closeBracketToken, SyntaxKind.CloseBracketToken)).CreateRed();

    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public static IniPropertySyntax IniProperty(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        return IniProperty(Key(key), Token(SyntaxKind.EqualsToken), Value(value));
    }

    public static IniPropertySyntax IniProperty(SyntaxToken keyToken, SyntaxToken separatorToken, SyntaxToken valueToken)
    {
        var separatorKind = separatorToken.Kind();
        if (separatorKind is not (SyntaxKind.EqualsToken or SyntaxKind.ColonToken))
            throw new ArgumentException("An INI property separator is '=' or ':'.", nameof(separatorToken));

        return (IniPropertySyntax)new Green.IniPropertySyntax(Required(keyToken, SyntaxKind.KeyToken), separatorToken.Node!, Required(valueToken, SyntaxKind.ValueToken)).CreateRed();
    }

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSkippedTextSyntax IniSkippedText(string text) => IniSkippedText(TokenList(BadToken(text)));

    public static IniSkippedTextSyntax IniSkippedText(SyntaxTokenList tokens)
        => (IniSkippedTextSyntax)new Green.IniSkippedTextSyntax(tokens.Node).CreateRed();

    /// <summary>Parses <paramref name="text"/> into a tree.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseSyntaxTree(string text, string? path = null) => IniSyntaxTree.ParseText(text, path);

    /// <summary>Parses <paramref name="text"/> and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniDocumentSyntax ParseDocument(string text) => IniSyntaxTree.ParseText(text).GetRoot();

    /// <summary>Determines whether the two nodes have the same structure and text.</summary>
    public static bool AreEquivalent(IniSyntaxNode? oldNode, IniSyntaxNode? newNode)
        => oldNode is null ? newNode is null : oldNode.IsEquivalentTo(newNode);

    private static Meziantou.Framework.Language.InternalSyntax.GreenNode Required(SyntaxToken token, SyntaxKind kind)
        => token.Node ?? Green.SyntaxFactory.MissingToken(kind);
}
