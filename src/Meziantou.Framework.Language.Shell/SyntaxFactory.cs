namespace Meziantou.Framework.Language.Shell;

/// <summary>Creates shell syntax nodes, tokens, and trivia programmatically.</summary>
public static class SyntaxFactory
{
    public static ShellSyntaxTree ParseText(string text, ShellDialect dialect) => ShellSyntaxTree.ParseText(text, dialect);

    public static ShellSyntaxToken Token(
        ShellSyntaxKind kind,
        string text,
        string? valueText = null,
        bool isMissing = false,
        IReadOnlyList<ShellSyntaxTrivia>? leadingTrivia = null,
        IReadOnlyList<ShellSyntaxTrivia>? trailingTrivia = null)
    {
        return new ShellSyntaxToken(kind, text, valueText, isMissing, leadingTrivia, trailingTrivia);
    }

    public static ShellSyntaxTrivia Trivia(ShellSyntaxKind kind, string text) => new(kind, text);
    public static ShellSyntaxTrivia Whitespace(string text = " ") => new(ShellSyntaxKind.WhitespaceTrivia, text);
    public static ShellSyntaxTrivia EndOfLine(string text = "\n") => new(ShellSyntaxKind.EndOfLineTrivia, text);

    /// <summary>Creates comment trivia, adding the dialect's comment marker when <paramref name="text"/> omits it.</summary>
    public static ShellSyntaxTrivia Comment(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialect);

        var marker = dialect.Family == ShellDialectFamily.Cmd ? "::" : "#";
        var content = text.StartsWith(marker, StringComparison.Ordinal) ? text : marker + " " + text;

        return new ShellSyntaxTrivia(ShellSyntaxKind.SingleLineCommentTrivia, content);
    }

    /// <summary>Creates an unquoted literal word part. The text is used verbatim.</summary>
    public static ShellLiteralWordPartSyntax Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new ShellLiteralWordPartSyntax(new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, text, text));
    }

    /// <summary>Creates a word from parts.</summary>
    public static ShellWordSyntax Word(params ShellWordPartSyntax[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return new ShellWordSyntax(parts);
    }

    /// <summary>
    /// Creates a word holding <paramref name="text"/>, quoting it for <paramref name="dialect"/> only when the text
    /// would otherwise be split, expanded, or globbed.
    /// </summary>
    public static ShellWordSyntax Word(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialect);

        if (!RequiresQuoting(text, dialect))
            return new ShellWordSyntax([Literal(text)]);

        return new ShellWordSyntax([QuotedString(text, dialect)]);
    }

    /// <summary>Creates a quoted string that reproduces <paramref name="value"/> literally in <paramref name="dialect"/>.</summary>
    public static ShellQuotedStringSyntax QuotedString(string value, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(dialect);

        return dialect.Family switch
        {
            // A single-quoted POSIX string has no escapes at all, so an embedded quote has to close, escape, reopen.
            ShellDialectFamily.Posix => Quote('\'', ShellSyntaxKind.SingleQuoteToken, value.Replace("'", @"'\''", StringComparison.Ordinal)),
            ShellDialectFamily.PowerShell => Quote('\'', ShellSyntaxKind.SingleQuoteToken, value.Replace("'", "''", StringComparison.Ordinal)),
            _ => Quote('"', ShellSyntaxKind.DoubleQuoteToken, value.Replace("\"", "\"\"", StringComparison.Ordinal)),
        };

        static ShellQuotedStringSyntax Quote(char quote, ShellSyntaxKind kind, string content)
        {
            var text = quote.ToString();

            return new ShellQuotedStringSyntax(
                new ShellSyntaxToken(kind, text, text),
                content.Length == 0 ? [] : [Literal(content)],
                new ShellSyntaxToken(kind, text, text));
        }
    }

    /// <summary>Creates a reference to <paramref name="name"/> using the syntax of <paramref name="dialect"/>.</summary>
    public static ShellVariableReferenceSyntax VariableReference(string name, ShellDialect dialect, bool braced = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.Family == ShellDialectFamily.Cmd)
        {
            return new ShellVariableReferenceSyntax(
                new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, "%", "%"),
                openBraceToken: null,
                new ShellSyntaxToken(ShellSyntaxKind.VariableNameToken, name, name),
                new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, "%", "%"));
        }

        var dollarToken = new ShellSyntaxToken(ShellSyntaxKind.DollarToken, "$", "$");
        var nameToken = new ShellSyntaxToken(ShellSyntaxKind.VariableNameToken, name, name);
        if (!braced)
            return new ShellVariableReferenceSyntax(dollarToken, openBraceToken: null, nameToken, closeBraceToken: null);

        return new ShellVariableReferenceSyntax(
            dollarToken,
            new ShellSyntaxToken(ShellSyntaxKind.OpenBraceToken, "{", "{"),
            nameToken,
            new ShellSyntaxToken(ShellSyntaxKind.CloseBraceToken, "}", "}"));
    }

    /// <summary>Creates a command whose name and arguments are quoted for <paramref name="dialect"/> as needed.</summary>
    public static ShellCommandSyntax Command(ShellDialect dialect, string name, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);

        return Command(Word(name, dialect), [.. arguments.Select(argument => Word(argument, dialect))]);
    }

    /// <summary>Creates a command from existing words, separated by single spaces.</summary>
    public static ShellCommandSyntax Command(ShellWordSyntax name, params ShellWordSyntax[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);

        var elements = new List<ShellSyntaxNode>(arguments.Length + 1) { name };
        foreach (var argument in arguments)
        {
            elements.Add(WithLeadingSpace(argument));
        }

        return new ShellCommandSyntax(elements);
    }

    public static ShellRedirectionSyntax Redirection(ShellSyntaxKind operatorKind, string operatorText, ShellWordSyntax target)
    {
        ArgumentNullException.ThrowIfNull(operatorText);
        ArgumentNullException.ThrowIfNull(target);

        return new ShellRedirectionSyntax(ioNumberToken: null, new ShellSyntaxToken(operatorKind, operatorText, operatorText), target);
    }

    public static ShellAssignmentSyntax Assignment(string name, ShellWordSyntax? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new ShellAssignmentSyntax(
            new ShellSyntaxToken(ShellSyntaxKind.VariableNameToken, name, name),
            new ShellSyntaxToken(ShellSyntaxKind.EqualsToken, "=", "="),
            value);
    }

    /// <summary>Creates a pipeline joining <paramref name="commands"/> with <c>|</c>.</summary>
    public static ShellPipelineSyntax Pipeline(params ShellStatementSyntax[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var operators = new List<ShellSyntaxToken>();
        for (var index = 0; index < commands.Length - 1; index++)
        {
            operators.Add(new ShellSyntaxToken(ShellSyntaxKind.PipeToken, "|", "|", leadingTrivia: [Whitespace()], trailingTrivia: [Whitespace()]));
        }

        return new ShellPipelineSyntax(bangToken: null, commands, operators);
    }

    public static ShellStatementListSyntax StatementList(params ShellStatementSyntax[] statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var separators = new List<ShellSyntaxToken>();
        for (var index = 0; index < statements.Length - 1; index++)
        {
            separators.Add(new ShellSyntaxToken(ShellSyntaxKind.SemicolonToken, ";", ";", trailingTrivia: [Whitespace()]));
        }

        return new ShellStatementListSyntax(statements, separators);
    }

    public static ShellScriptSyntax Script(ShellStatementListSyntax statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        return new ShellScriptSyntax(statements, new ShellSyntaxToken(ShellSyntaxKind.EndOfFileToken, string.Empty));
    }

    public static ShellSkippedTextSyntax SkippedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new ShellSkippedTextSyntax([new ShellSyntaxToken(ShellSyntaxKind.BadToken, text, text)], fullStart: 0);
    }

    public static ShellRawExpressionSyntax RawExpression(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new ShellRawExpressionSyntax(new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, text, text));
    }

    /// <summary>Returns whether <paramref name="text"/> would change meaning if written unquoted.</summary>
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

    private static ShellWordSyntax WithLeadingSpace(ShellWordSyntax word)
    {
        if (word.Parts.Count == 0)
            return word;

        var first = word.Parts[0];
        var updated = first switch
        {
            ShellLiteralWordPartSyntax literal => new ShellLiteralWordPartSyntax(literal.TextToken.WithLeadingTrivia([Whitespace()])),
            ShellQuotedStringSyntax quoted => new ShellQuotedStringSyntax(quoted.OpenQuoteToken.WithLeadingTrivia([Whitespace()]), quoted.Parts, quoted.CloseQuoteToken),
            _ => (ShellWordPartSyntax?)null,
        };

        if (updated is null)
            return word;

        return new ShellWordSyntax([updated, .. word.Parts.Skip(1)]);
    }
}
