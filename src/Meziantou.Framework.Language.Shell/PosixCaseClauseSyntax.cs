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
            openParenToken?.FullSpan.Start ?? SeparatedNodes.GetFullStart(patterns, patternSeparatorTokens),
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
