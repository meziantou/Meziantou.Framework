namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>for</c> loop.</summary>
public sealed class PowerShellForStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellForStatementSyntax(
        ShellSyntaxToken forKeyword,
        ShellSyntaxToken openParenToken,
        ShellSyntaxNode? initializer,
        ShellSyntaxToken? firstSemicolonToken,
        ShellSyntaxNode? condition,
        ShellSyntaxToken? secondSemicolonToken,
        ShellSyntaxNode? iterator,
        ShellSyntaxToken closeParenToken,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellForStatement,
            forKeyword.ToFullString() + openParenToken.ToFullString() + (initializer?.ToFullString() ?? string.Empty) + (firstSemicolonToken?.ToFullString() ?? string.Empty) + (condition?.ToFullString() ?? string.Empty) + (secondSemicolonToken?.ToFullString() ?? string.Empty) + (iterator?.ToFullString() ?? string.Empty) + closeParenToken.ToFullString() + body.ToFullString(),
            forKeyword.FullSpan.Start,
            BuildTokens(forKeyword, openParenToken, firstSemicolonToken, secondSemicolonToken, closeParenToken))
    {
        ForKeyword = forKeyword;
        OpenParenToken = openParenToken;
        Initializer = initializer;
        FirstSemicolonToken = firstSemicolonToken;
        Condition = condition;
        SecondSemicolonToken = secondSemicolonToken;
        Iterator = iterator;
        CloseParenToken = closeParenToken;
        Body = body;
        _childNodes = [.. OptionalNode(Initializer), .. OptionalNode(Condition), .. OptionalNode(Iterator), .. SingleNode(Body)];
    }

    /// <summary>The <c>for</c> keyword.</summary>
    public ShellSyntaxToken ForKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The initializer, when present.</summary>
    public ShellSyntaxNode? Initializer { get; }

    /// <summary>The first <c>;</c>.</summary>
    public ShellSyntaxToken? FirstSemicolonToken { get; }

    /// <summary>The condition, when present.</summary>
    public ShellSyntaxNode? Condition { get; }

    /// <summary>The second <c>;</c>.</summary>
    public ShellSyntaxToken? SecondSemicolonToken { get; }

    /// <summary>The iterator, when present.</summary>
    public ShellSyntaxNode? Iterator { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    /// <summary>The loop body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellForStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellForStatement(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken forKeyword,
        ShellSyntaxToken openParenToken,
        ShellSyntaxToken? firstSemicolonToken,
        ShellSyntaxToken? secondSemicolonToken,
        ShellSyntaxToken closeParenToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(forKeyword);
        tokens.Add(openParenToken);
        if (firstSemicolonToken is not null)
        {
            tokens.Add(firstSemicolonToken);
        }

        if (secondSemicolonToken is not null)
        {
            tokens.Add(secondSemicolonToken);
        }

        tokens.Add(closeParenToken);

        return tokens;
    }
}
