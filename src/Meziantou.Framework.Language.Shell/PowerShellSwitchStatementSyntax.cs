namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>switch</c> statement.</summary>
public sealed class PowerShellSwitchStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellSwitchStatementSyntax(
        ShellSyntaxToken switchKeyword,
        IReadOnlyList<ShellSyntaxToken>? parameterTokens,
        ShellSyntaxToken? openParenToken,
        ShellStatementListSyntax condition,
        ShellSyntaxToken? closeParenToken,
        ShellSyntaxToken openBraceToken,
        IReadOnlyList<PowerShellSwitchClauseSyntax>? clauses,
        ShellSyntaxToken closeBraceToken)
        : base(
            ShellSyntaxKind.PowerShellSwitchStatement,
            switchKeyword.ToFullString() + BuildFullText(parameterTokens ?? []) + (openParenToken?.ToFullString() ?? string.Empty) + condition.ToFullString() + (closeParenToken?.ToFullString() ?? string.Empty) + openBraceToken.ToFullString() + BuildFullText(clauses ?? []) + closeBraceToken.ToFullString(),
            switchKeyword.FullSpan.Start,
            BuildTokens(switchKeyword, parameterTokens, openParenToken, closeParenToken, openBraceToken, closeBraceToken))
    {
        SwitchKeyword = switchKeyword;
        ParameterTokens = parameterTokens ?? [];
        OpenParenToken = openParenToken;
        Condition = condition;
        CloseParenToken = closeParenToken;
        OpenBraceToken = openBraceToken;
        Clauses = clauses ?? [];
        CloseBraceToken = closeBraceToken;
        _childNodes = [.. SingleNode(Condition), .. (Clauses as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The <c>switch</c> keyword.</summary>
    public ShellSyntaxToken SwitchKeyword { get; }

    /// <summary>Switch parameters such as <c>-Regex</c> or <c>-File</c>.</summary>
    public IReadOnlyList<ShellSyntaxToken> ParameterTokens { get; }

    /// <summary>The opening parenthesis around the condition.</summary>
    public ShellSyntaxToken? OpenParenToken { get; }

    /// <summary>The value being matched.</summary>
    public ShellStatementListSyntax Condition { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken? CloseParenToken { get; }

    /// <summary>The opening brace of the clause list.</summary>
    public ShellSyntaxToken OpenBraceToken { get; }

    /// <summary>The clauses.</summary>
    public IReadOnlyList<PowerShellSwitchClauseSyntax> Clauses { get; }

    /// <summary>The closing brace.</summary>
    public ShellSyntaxToken CloseBraceToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitSwitchStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitSwitchStatement(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken switchKeyword,
        IReadOnlyList<ShellSyntaxToken>? parameterTokens,
        ShellSyntaxToken? openParenToken,
        ShellSyntaxToken? closeParenToken,
        ShellSyntaxToken openBraceToken,
        ShellSyntaxToken closeBraceToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(switchKeyword);
        tokens.AddRange(parameterTokens ?? []);
        if (openParenToken is not null)
        {
            tokens.Add(openParenToken);
        }

        if (closeParenToken is not null)
        {
            tokens.Add(closeParenToken);
        }

        tokens.Add(openBraceToken);
        tokens.Add(closeBraceToken);

        return tokens;
    }
}
