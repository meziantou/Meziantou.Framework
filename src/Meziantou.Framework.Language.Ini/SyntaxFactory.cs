using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>Builds INI nodes, tokens, and trivia.</summary>
/// <remarks>
/// <para>
/// A node built here is not part of any document, so its span starts at zero. Putting it into a tree with
/// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/> gives it a real position,
/// without re-reading any text.
/// </para>
/// <para>
/// The methods that take a key, a section name, or a value check that the text reads back as that same key, name, or
/// value, whatever <see cref="IniParseOptions.InlineComments"/> is, so an edit cannot change the rest of the document.
/// <see cref="Value(string)"/> quotes a value that would not read back otherwise.
/// </para>
/// </remarks>
public static class SyntaxFactory
{
    /// <summary>Options under which <c>;</c> and <c>#</c> start a comment anywhere, the strictest way to read a line.</summary>
    private static readonly IniParseOptions StrictOptions = new() { InlineComments = IniInlineCommentMode.Anywhere };

    /// <summary>A single space.</summary>
    public static SyntaxTrivia Space => Whitespace(" ");

    /// <summary>A single tab.</summary>
    public static SyntaxTrivia Tab => Whitespace("\t");

    /// <summary>A Unix line break.</summary>
    public static SyntaxTrivia LineFeed => Trivia(SyntaxKind.EndOfLineTrivia, "\n");

    /// <summary>A Windows line break.</summary>
    public static SyntaxTrivia CarriageReturnLineFeed => Trivia(SyntaxKind.EndOfLineTrivia, "\r\n");

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or holds something other than whitespace.</exception>
    public static SyntaxTrivia Whitespace(string text) => Trivia(SyntaxKind.WhitespaceTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not <c>\n</c>, <c>\r\n</c>, or <c>\r</c>.</exception>
    public static SyntaxTrivia EndOfLine(string text) => Trivia(SyntaxKind.EndOfLineTrivia, text);

    /// <summary>Creates comment trivia.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not an INI comment.</exception>
    public static SyntaxTrivia Comment(string text) => Trivia(SyntaxKind.CommentTrivia, text);

    /// <summary>Creates trivia of the given kind.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not trivia of that kind, or <paramref name="kind"/> is not a kind of trivia.</exception>
    public static SyntaxTrivia Trivia(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var isValid = kind switch
        {
            SyntaxKind.WhitespaceTrivia => text.Length > 0 && text.All(Green.Lexer.IsWhitespace),
            SyntaxKind.EndOfLineTrivia => text is "\n" or "\r\n" or "\r",
            SyntaxKind.CommentTrivia => text is [';' or '#', ..] && text.AsSpan().IndexOfAny('\r', '\n') < 0,
            _ => throw new ArgumentException($"{kind} is not a kind of trivia.", nameof(kind)),
        };

        if (!isValid)
            throw new ArgumentException(kind == SyntaxKind.CommentTrivia ? "An INI comment starts with ';' or '#' and does not span lines." : $"The text is not {kind}.", nameof(text));

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
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> would not read back as this key: it is empty, starts with <c>[</c>, <c>;</c>, or <c>#</c>,
    /// has whitespace around it, or holds <c>=</c>, <c>:</c>, <c>;</c>, <c>#</c>, or a line break.
    /// </exception>
    public static SyntaxToken Key(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Lex(text, Green.LexerMode.LineStart, SyntaxKind.KeyToken) ?? throw new ArgumentException($"'{text}' cannot be written as an INI key.", nameof(text));
    }

    /// <summary>Creates a section name token written exactly as <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> would not read back as this name: it is empty, has whitespace around it, or holds <c>]</c>,
    /// <c>;</c>, <c>#</c>, or a line break.
    /// </exception>
    public static SyntaxToken SectionName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Lex(text, Green.LexerMode.SectionName, SyntaxKind.KeyToken) ?? throw new ArgumentException($"'{text}' cannot be written as an INI section name.", nameof(text));
    }

    /// <summary>Creates a value token that reads back as <paramref name="value"/>, in quotes when it has to be.</summary>
    /// <remarks>
    /// A value is quoted when it would otherwise lose part of itself: whitespace around it, a <c>;</c> or <c>#</c> that
    /// could start a comment, or quotes around it. It is put in double quotes, or in single quotes when it holds a
    /// double quote.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> holds a line break, or needs quotes and holds both kinds of quote.
    /// </exception>
    public static SyntaxToken Value(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            if (LexValue(value) is { } token)
                return token;

            var quote = value.Contains('"', StringComparison.Ordinal) ? '\'' : '"';
            if (LexValue(quote + value + quote) is { } quoted)
                return quoted;
        }

        throw new ArgumentException("The value cannot be written as an INI value: it holds a line break, or needs quotes and holds both kinds of quote.", nameof(value));

        SyntaxToken? LexValue(string text) => Lex(text, Green.LexerMode.Value, SyntaxKind.ValueToken) is { } token && token.ValueText == value ? token : null;
    }

    /// <summary>Creates a value token written exactly as <paramref name="text"/>, quotes included.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> would not read back as one value: it has whitespace around it, a line break, or a
    /// <c>;</c> or <c>#</c> outside quotes.
    /// </exception>
    public static SyntaxToken RawValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Lex(text, Green.LexerMode.Value, SyntaxKind.ValueToken) ?? throw new ArgumentException($"'{text}' cannot be written as an INI value.", nameof(text));
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

    /// <summary>Creates a document from <paramref name="entries"/>, ending the line of each that does not end its own.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    public static IniDocumentSyntax IniDocument(params IniEntrySyntax[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return IniDocument(List(entries.Select(entry => EndLine(entry, LineFeed))), Token(SyntaxKind.EndOfFileToken));
    }

    public static IniDocumentSyntax IniDocument(SyntaxList<IniEntrySyntax> entries, SyntaxToken endOfFileToken)
        => (IniDocumentSyntax)new Green.IniDocumentSyntax(entries.Green, endOfFileToken.Node ?? Green.SyntaxFactory.Token(SyntaxKind.EndOfFileToken)).CreateRed();

    /// <summary>Creates a section header such as <c>[database]</c>, ending with a line feed like every line of a document.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> cannot be written as a section name; see <see cref="SectionName(string)"/>.</exception>
    public static IniSectionSyntax IniSection(string name)
        => IniSection(Token(SyntaxKind.OpenBracketToken), SectionName(name), Token(TriviaList(), SyntaxKind.CloseBracketToken, TriviaList(LineFeed)));

    public static IniSectionSyntax IniSection(SyntaxToken openBracketToken, SyntaxToken nameToken, SyntaxToken closeBracketToken)
        => (IniSectionSyntax)new Green.IniSectionSyntax(Required(openBracketToken, SyntaxKind.OpenBracketToken), Required(nameToken, SyntaxKind.KeyToken), Required(closeBracketToken, SyntaxKind.CloseBracketToken)).CreateRed();

    /// <summary>Creates <c>key=value</c>, quoting <paramref name="value"/> when it has to be, and ending with a line feed like every line of a document.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> cannot be written as a key, or <paramref name="value"/> as a value; see <see cref="Key(string)"/> and <see cref="Value(string)"/>.
    /// </exception>
    public static IniPropertySyntax IniProperty(string key, string value) => IniProperty(Key(key), Token(SyntaxKind.EqualsToken), Value(value).WithTrailingTrivia(LineFeed));

    /// <exception cref="ArgumentException"><paramref name="separatorToken"/> is neither <c>=</c> nor <c>:</c>.</exception>
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

    /// <summary>Parses <paramref name="text"/> into a tree.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseSyntaxTree(string text, IniParseOptions? options, string? path = null) => IniSyntaxTree.ParseText(text, options, path);

    /// <summary>Parses <paramref name="text"/> and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniDocumentSyntax ParseDocument(string text) => IniSyntaxTree.ParseText(text).GetRoot();

    /// <summary>Parses <paramref name="text"/> and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniDocumentSyntax ParseDocument(string text, IniParseOptions? options) => IniSyntaxTree.ParseText(text, options).GetRoot();

    /// <summary>Determines whether the two nodes have the same structure and text.</summary>
    public static bool AreEquivalent(IniSyntaxNode? oldNode, IniSyntaxNode? newNode)
        => oldNode is null ? newNode is null : oldNode.IsEquivalentTo(newNode);

    /// <summary>Returns <paramref name="entry"/> ending with a line break, which every entry of a document but the last needs.</summary>
    internal static IniEntrySyntax EndLine(IniEntrySyntax entry, SyntaxTrivia endOfLine)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var trailing = entry.GetTrailingTrivia();
        if (trailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            return entry;

        return entry.WithTrailingTrivia([.. trailing, endOfLine]);
    }

    /// <summary>Reads <paramref name="text"/> as <paramref name="mode"/> would, and returns the token if it is the whole text and nothing else.</summary>
    private static SyntaxToken? Lex(string text, Green.LexerMode mode, SyntaxKind kind)
    {
        var lexer = new Green.Lexer(SourceText.From(text), StrictOptions);
        var token = lexer.Lex(mode);
        if (token.RawKind != (int)kind || token.LeadingTrivia is not null || token.TrailingTrivia is not null || token.Width != text.Length)
            return null;

        // A value is read after whitespace, so a comment character it starts with would start a comment there.
        if (text is [';' or '#', ..])
            return null;

        return new SyntaxToken(parent: null, token, position: 0, index: 0);
    }

    private static Meziantou.Framework.Language.InternalSyntax.GreenNode Required(SyntaxToken token, SyntaxKind kind)
        => token.Node ?? Green.SyntaxFactory.MissingToken(kind);
}
