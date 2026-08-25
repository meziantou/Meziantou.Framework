namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents one pattern clause of a <see cref="PosixCaseStatementSyntax"/>.</summary>
public sealed class PosixCaseClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixCaseClauseSyntax(
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<ShellWordSyntax>? patterns,
        IReadOnlyList<ShellSyntaxToken>? patternSeparatorTokens,
        ShellSyntaxToken closeParenToken,
        ShellStatementListSyntax body,
        ShellSyntaxToken? terminatorToken)
        : base(
            ShellSyntaxKind.PosixCaseClause,
            (openParenToken?.ToFullString() ?? string.Empty)
                + SeparatedNodes.BuildText(patterns, patternSeparatorTokens)
                + closeParenToken?.ToFullString() + body?.ToFullString() + (terminatorToken?.ToFullString() ?? string.Empty),
            GetFullStart(openParenToken, patterns, patternSeparatorTokens, closeParenToken, body, terminatorToken),
            BuildTokens(openParenToken, patternSeparatorTokens, closeParenToken!, terminatorToken))
    {
        OpenParenToken = openParenToken;
        Patterns = patterns ?? [];
        PatternSeparatorTokens = patternSeparatorTokens ?? [];
        CloseParenToken = closeParenToken!;
        Body = body!;
        TerminatorToken = terminatorToken;
        _childNodes = [.. Patterns, body!];
    }

    /// <summary>The optional <c>(</c> that may precede the first pattern.</summary>
    public ShellSyntaxToken? OpenParenToken { get; }

    public IReadOnlyList<ShellWordSyntax> Patterns { get; }

    /// <summary>The <c>|</c> tokens separating alternative patterns.</summary>
    public IReadOnlyList<ShellSyntaxToken> PatternSeparatorTokens { get; }

    public ShellSyntaxToken CloseParenToken { get; }
    public ShellStatementListSyntax Body { get; }

    /// <summary>The <c>;;</c>, <c>;&amp;</c>, or <c>;;&amp;</c> token, absent on the last clause when the source omits it.</summary>
    public ShellSyntaxToken? TerminatorToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCaseClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCaseClause(this);

    /// <summary>
    /// The clause starts at whichever part comes first. A clause with neither an opening parenthesis nor a pattern,
    /// which only malformed input produces, still has to report the position of the text it does cover.
    /// </summary>
    private static int GetFullStart(
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<ShellWordSyntax>? patterns,
        IReadOnlyList<ShellSyntaxToken>? patternSeparatorTokens,
        ShellSyntaxToken? closeParenToken,
        ShellStatementListSyntax? body,
        ShellSyntaxToken? terminatorToken)
    {
        // The parts appear in this order, and a missing one contributes no text, so the clause starts at the first
        // part that actually carries some.
        if (openParenToken is { IsMissing: false })
            return openParenToken.FullSpan.Start;

        if (patterns is { Count: > 0 } || patternSeparatorTokens is { Count: > 0 })
            return SeparatedNodes.GetFullStart(patterns, patternSeparatorTokens);

        if (closeParenToken is { IsMissing: false })
            return closeParenToken.FullSpan.Start;

        if (body is { Statements.Count: > 0 })
            return body.FullSpan.Start;

        return terminatorToken?.FullSpan.Start ?? closeParenToken?.FullSpan.Start ?? 0;
    }

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<ShellSyntaxToken>? patternSeparatorTokens,
        ShellSyntaxToken closeParenToken,
        ShellSyntaxToken? terminatorToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        if (openParenToken is not null)
        {
            tokens.Add(openParenToken);
        }

        tokens.AddRange(patternSeparatorTokens ?? []);
        tokens.Add(closeParenToken);
        if (terminatorToken is not null)
        {
            tokens.Add(terminatorToken);
        }

        return tokens;
    }
}
