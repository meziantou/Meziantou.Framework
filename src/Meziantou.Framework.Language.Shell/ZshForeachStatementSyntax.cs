namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents the zsh loop forms that take their word list in parentheses: <c>foreach x (a b) ... end</c> and the
/// short <c>for x (a b) command</c>.
/// </summary>
public sealed class ZshForeachStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ZshForeachStatementSyntax(
        ShellSyntaxToken keyword,
        ShellSyntaxToken variableToken,
        ShellSyntaxToken openParenToken,
        IReadOnlyList<ShellWordSyntax>? items,
        ShellSyntaxToken closeParenToken,
        ShellStatementListSyntax body,
        ShellSyntaxToken? endKeyword)
        : base(
            ShellSyntaxKind.ZshForeachStatement,
            keyword.ToFullString() + variableToken.ToFullString() + openParenToken.ToFullString()
                + BuildFullText(items ?? []) + closeParenToken.ToFullString() + body.ToFullString()
                + (endKeyword?.ToFullString() ?? string.Empty),
            keyword.FullSpan.Start,
            BuildTokens(keyword, variableToken, openParenToken, closeParenToken, endKeyword))
    {
        Keyword = keyword;
        VariableToken = variableToken;
        OpenParenToken = openParenToken;
        Items = items ?? [];
        CloseParenToken = closeParenToken;
        Body = body;
        EndKeyword = endKeyword;
        _childNodes = [.. Items, body];
    }

    /// <summary>The <c>foreach</c> or <c>for</c> keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    public ShellSyntaxToken VariableToken { get; }
    public string VariableName => VariableToken.ValueText;
    public ShellSyntaxToken OpenParenToken { get; }
    public IReadOnlyList<ShellWordSyntax> Items { get; }
    public ShellSyntaxToken CloseParenToken { get; }
    public ShellStatementListSyntax Body { get; }

    /// <summary>The closing <c>end</c> keyword, absent in the short <c>for x (a b) command</c> form.</summary>
    public ShellSyntaxToken? EndKeyword { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitZshForeachStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitZshForeachStatement(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken keyword,
        ShellSyntaxToken variableToken,
        ShellSyntaxToken openParenToken,
        ShellSyntaxToken closeParenToken,
        ShellSyntaxToken? endKeyword)
    {
        var tokens = new List<ShellSyntaxToken>(5) { keyword, variableToken, openParenToken, closeParenToken };
        if (endKeyword is not null)
        {
            tokens.Add(endKeyword);
        }

        return tokens;
    }
}
