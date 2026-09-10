using Green = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Builds shell nodes, tokens, and trivia.</summary>
/// <remarks>
/// A node built here is not part of any script, so its span starts at zero. Putting it into a tree with
/// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/> gives it a real position,
/// without re-reading any text.
/// </remarks>
/// <example>
/// <code>
/// var command = SyntaxFactory.Command(ShellDialect.Bash, "echo", "hello world");
/// </code>
/// </example>
public static partial class SyntaxFactory
{
    /// <summary>A single space.</summary>
    public static SyntaxTrivia Space => Whitespace(" ");

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Whitespace(string text = " ") => Trivia(SyntaxKind.WhitespaceTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia EndOfLine(string text = "\n") => Trivia(SyntaxKind.EndOfLineTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Trivia(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxTrivia(token: default, Green.SyntaxFactory.Trivia(kind, text), position: 0, index: 0);
    }

    /// <summary>Creates comment trivia, adding the dialect's comment marker when <paramref name="text"/> omits it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Comment(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialect);

        var marker = dialect.Family == ShellDialectFamily.Cmd ? "::" : "#";
        var content = text.StartsWith(marker, StringComparison.Ordinal) ? text : marker + " " + text;

        return Trivia(SyntaxKind.SingleLineCommentTrivia, content);
    }

    /// <summary>Creates a token of <paramref name="kind"/> spelled <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Token(SyntaxKind kind, string text, string? valueText = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.TokenWithValue(leading: null, kind, text, valueText ?? text, trailing: null), position: 0, index: 0);
    }

    /// <summary>Creates a token of <paramref name="kind"/>, spelled the only way that kind can be.</summary>
    public static SyntaxToken Token(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.Token(kind), position: 0, index: 0);

    /// <summary>Creates a zero-width token standing in for one the source does not have.</summary>
    public static SyntaxToken MissingToken(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.MissingToken(kind), position: 0, index: 0);

    /// <summary>Creates an unquoted literal word part. The text is used verbatim.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static ShellLiteralWordPartSyntax Literal(string text) => ShellLiteralWordPart(Token(SyntaxKind.BareTextToken, text));

    /// <summary>Creates a word from parts.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="parts"/> is <see langword="null"/>.</exception>
    public static ShellWordSyntax Word(params ShellWordPartSyntax[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return ShellWord(new SyntaxList<ShellWordPartSyntax>(parts));
    }

    /// <summary>
    /// Creates a word holding <paramref name="text"/>, quoting it for <paramref name="dialect"/> only when the text
    /// would otherwise be split, expanded, or globbed.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static ShellWordSyntax Word(string text, ShellDialect dialect)
        => RequiresQuoting(text, dialect) ? Word(QuotedString(text, dialect)) : Word(Literal(text));

    /// <summary>Creates a quoted string that reproduces <paramref name="value"/> literally in <paramref name="dialect"/>.</summary>
    /// <remarks>
    /// The returned node's <c>Value</c> is always <paramref name="value"/>. For the POSIX and PowerShell families the
    /// text also reads back as <paramref name="value"/> when parsed again. Cmd has no escape for a quote inside a
    /// quoted string and no way to carry a line break in a word, so a value holding either cannot be written as cmd
    /// text that parses back to it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static ShellQuotedStringSyntax QuotedString(string value, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(dialect);

        return dialect.Family switch
        {
            // A single-quoted POSIX string has no escapes at all, so a value holding a quote is double-quoted
            // instead, with the characters the shell would still act on written as escape sequences.
            ShellDialectFamily.Posix => value.Contains('\'', StringComparison.Ordinal)
                ? Quote(SyntaxKind.DoubleQuoteToken, EscapeParts(value, PosixDoubleQuoteSpecials))
                : Quote(SyntaxKind.SingleQuoteToken, VerbatimParts(value)),

            // PowerShell and cmd double the quote character inside the string rather than escaping it.
            ShellDialectFamily.PowerShell => Quote(SyntaxKind.SingleQuoteToken, DoubledParts(value, '\'')),
            _ => Quote(SyntaxKind.DoubleQuoteToken, DoubledParts(value, '"')),
        };

        static ShellQuotedStringSyntax Quote(SyntaxKind kind, ShellWordPartSyntax[] parts)
            => ShellQuotedString(Token(kind), new SyntaxList<ShellWordPartSyntax>(parts), Token(kind));
    }

    /// <summary>Creates a reference to <paramref name="name"/> using the syntax of <paramref name="dialect"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static ShellVariableReferenceSyntax VariableReference(string name, ShellDialect dialect, bool braced = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dialect);

        var nameToken = Token(SyntaxKind.VariableNameToken, name);
        if (dialect.Family == ShellDialectFamily.Cmd)
        {
            return ShellVariableReference(Token(SyntaxKind.BareTextToken, "%"), default, nameToken, Token(SyntaxKind.BareTextToken, "%"));
        }

        var dollarToken = Token(SyntaxKind.DollarToken);

        return braced
            ? ShellVariableReference(dollarToken, Token(SyntaxKind.OpenBraceToken), nameToken, Token(SyntaxKind.CloseBraceToken))
            : ShellVariableReference(dollarToken, default, nameToken, default);
    }

    /// <summary>Creates a command whose name and arguments are quoted for <paramref name="dialect"/> as needed.</summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static ShellCommandSyntax Command(ShellDialect dialect, string name, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);

        return Command(Word(name, dialect), [.. arguments.Select(argument => Word(argument, dialect))]);
    }

    /// <summary>Creates a command from existing words, separated by single spaces.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="arguments"/> is <see langword="null"/>.</exception>
    public static ShellCommandSyntax Command(ShellWordSyntax name, params ShellWordSyntax[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);

        var elements = new List<ShellSyntaxNode>(arguments.Length + 1) { name };
        elements.AddRange(arguments.Select(WithLeadingSpace));

        return ShellCommand(new SyntaxList<ShellSyntaxNode>(elements));
    }

    /// <exception cref="ArgumentNullException"><paramref name="operatorText"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    public static ShellRedirectionSyntax Redirection(SyntaxKind operatorKind, string operatorText, ShellWordSyntax target)
    {
        ArgumentNullException.ThrowIfNull(operatorText);
        ArgumentNullException.ThrowIfNull(target);

        return ShellRedirection(default, Token(operatorKind, operatorText), target);
    }

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static ShellAssignmentSyntax Assignment(string name, ShellWordSyntax? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ShellAssignment(Token(SyntaxKind.VariableNameToken, name), Token(SyntaxKind.EqualsToken), value);
    }

    /// <summary>Creates a pipeline joining <paramref name="commands"/> with <c>|</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is <see langword="null"/>.</exception>
    public static ShellPipelineSyntax Pipeline(params ShellStatementSyntax[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var pipe = Token(SyntaxKind.PipeToken).WithLeadingTrivia(Space).WithTrailingTrivia(Space);

        return ShellPipeline(default, Separated(commands, pipe));
    }

    /// <exception cref="ArgumentNullException"><paramref name="statements"/> is <see langword="null"/>.</exception>
    public static ShellStatementListSyntax StatementList(params ShellStatementSyntax[] statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var semicolon = Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(Space);

        return ShellStatementList(Separated(statements, semicolon));
    }

    /// <exception cref="ArgumentNullException"><paramref name="statements"/> is <see langword="null"/>.</exception>
    public static ShellScriptSyntax Script(ShellStatementListSyntax statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        return ShellScript(statements, Token(SyntaxKind.EndOfFileToken));
    }

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static ShellSkippedTextSyntax SkippedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return (ShellSkippedTextSyntax)new Green.ShellSkippedTextSyntax(
            Green.ParserHelpers.SkippedTokens([Green.SyntaxFactory.Token(leading: null, SyntaxKind.BadToken, text, trailing: null)])).CreateRed();
    }

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static ShellRawExpressionSyntax RawExpression(string text) => ShellRawExpression(Token(SyntaxKind.BareTextToken, text));

    /// <summary>Returns whether <paramref name="text"/> would change meaning if written unquoted.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static bool RequiresQuoting(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialect);

        if (text.Length == 0)
            return true;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
                return true;

            var isSpecial = dialect.Family switch
            {
                ShellDialectFamily.Cmd => character is '%' or '!' or '^' or '&' or '|' or '<' or '>' or '(' or ')' or '"',
                _ => character is '$' or '`' or '"' or '\'' or '\\' or '*' or '?' or '[' or ']' or '{' or '}' or '(' or ')'
                    or '|' or '&' or ';' or '<' or '>' or '#' or '~' or '!',
            };

            if (isSpecial)
                return true;
        }

        return false;
    }

    /// <summary>The characters a POSIX shell still acts on inside a double-quoted string.</summary>
    private static ReadOnlySpan<char> PosixDoubleQuoteSpecials => ['$', '`', '"', '\\'];

    /// <summary>Weaves nodes and one separator between each pair into the sequence a separated list holds.</summary>
    private static SeparatedSyntaxList<TNode> Separated<TNode>(TNode[] nodes, SyntaxToken separator)
        where TNode : ShellSyntaxNode
    {
        var items = new List<SyntaxNodeOrToken>(Math.Max(0, (nodes.Length * 2) - 1));
        for (var index = 0; index < nodes.Length; index++)
        {
            if (index > 0)
            {
                items.Add(separator);
            }

            items.Add(nodes[index]);
        }

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(items));
    }

    private static ShellWordPartSyntax[] VerbatimParts(string value) => value.Length == 0 ? [] : [Literal(value)];

    /// <summary>
    /// Splits <paramref name="value"/> into literal runs and backslash escapes, the way the parser reads a
    /// double-quoted string back, so the parts resolve to the original value.
    /// </summary>
    private static ShellWordPartSyntax[] EscapeParts(string value, ReadOnlySpan<char> specials)
    {
        var parts = new List<ShellWordPartSyntax>();
        var run = new StringBuilder();
        foreach (var character in value)
        {
            if (specials.IndexOf(character) < 0)
            {
                run.Append(character);
                continue;
            }

            if (run.Length > 0)
            {
                parts.Add(Literal(run.ToString()));
                run.Clear();
            }

            parts.Add(ShellEscapeSequence(Token(SyntaxKind.EscapeToken, "\\" + character, character.ToString())));
        }

        if (run.Length > 0)
        {
            parts.Add(Literal(run.ToString()));
        }

        return [.. parts];
    }

    /// <summary>
    /// Writes <paramref name="value"/> with every <paramref name="quote"/> doubled, keeping the original text as the
    /// token's value so the part resolves to what was asked for.
    /// </summary>
    private static ShellWordPartSyntax[] DoubledParts(string value, char quote)
    {
        if (value.Length == 0)
            return [];

        var text = value.Replace(quote.ToString(), new string(quote, 2), StringComparison.Ordinal);

        return [ShellLiteralWordPart(Token(SyntaxKind.BareTextToken, text, value))];
    }

    /// <summary>
    /// Puts a space in front of <paramref name="word"/> so it stays a word of its own instead of being glued to what
    /// precedes it. A word that already starts with trivia is returned unchanged.
    /// </summary>
    internal static ShellWordSyntax WithLeadingSpace(ShellWordSyntax word)
        => word.GetLeadingTrivia().Count > 0 ? word : word.WithLeadingTrivia(Space);

    /// <summary>Unwraps a token that a node requires, rejecting the default one no factory should produce.</summary>
    private static Meziantou.Framework.Language.InternalSyntax.GreenNode Required(SyntaxToken token)
        => token.Node ?? throw new ArgumentException("A required token was not given.", nameof(token));
}
