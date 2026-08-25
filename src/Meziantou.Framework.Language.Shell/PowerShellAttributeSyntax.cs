namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an attribute or type constraint, <c>[Parameter(Mandatory)]</c> or <c>[string]</c>.</summary>
public sealed class PowerShellAttributeSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellAttributeSyntax(
        ShellSyntaxToken openBracketToken,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<ShellExpressionSyntax>? arguments,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        ShellSyntaxToken? closeParenToken,
        ShellSyntaxToken closeBracketToken)
        : base(
            ShellSyntaxKind.PowerShellAttribute,
            openBracketToken.ToFullString() + nameToken.ToFullString() + (openParenToken?.ToFullString() ?? string.Empty) + SeparatedNodes.BuildText(arguments, separatorTokens) + (closeParenToken?.ToFullString() ?? string.Empty) + closeBracketToken.ToFullString(),
            openBracketToken.FullSpan.Start,
            BuildTokens(openBracketToken, nameToken, openParenToken, separatorTokens, closeParenToken, closeBracketToken))
    {
        OpenBracketToken = openBracketToken;
        NameToken = nameToken;
        OpenParenToken = openParenToken;
        Arguments = arguments ?? [];
        SeparatorTokens = separatorTokens ?? [];
        CloseParenToken = closeParenToken;
        CloseBracketToken = closeBracketToken;
        _childNodes = [.. (Arguments as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The opening bracket.</summary>
    public ShellSyntaxToken OpenBracketToken { get; }

    /// <summary>The attribute or type name.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The opening parenthesis of the argument list.</summary>
    public ShellSyntaxToken? OpenParenToken { get; }

    /// <summary>The attribute arguments.</summary>
    public IReadOnlyList<ShellExpressionSyntax> Arguments { get; }

    /// <summary>The separator that follows each entry of <see cref="Arguments"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> SeparatorTokens { get; }

    /// <summary>The closing parenthesis of the argument list.</summary>
    public ShellSyntaxToken? CloseParenToken { get; }

    /// <summary>The closing bracket.</summary>
    public ShellSyntaxToken CloseBracketToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The attribute or type name.</summary>
    public string Name => NameToken.Text;

    /// <summary>Returns <see langword="true"/> when the attribute has no argument list, making it a type constraint.</summary>
    public bool IsTypeConstraint => OpenParenToken is null;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitAttribute(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitAttribute(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken openBracketToken,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        ShellSyntaxToken? closeParenToken,
        ShellSyntaxToken closeBracketToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(openBracketToken);
        tokens.Add(nameToken);
        if (openParenToken is not null)
        {
            tokens.Add(openParenToken);
        }

        tokens.AddRange(separatorTokens ?? []);
        if (closeParenToken is not null)
        {
            tokens.Add(closeParenToken);
        }

        tokens.Add(closeBracketToken);

        return tokens;
    }
}
