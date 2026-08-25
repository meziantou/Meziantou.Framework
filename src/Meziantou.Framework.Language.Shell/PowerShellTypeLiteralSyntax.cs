namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a type literal, <c>[System.String]</c>.</summary>
public sealed class PowerShellTypeLiteralSyntax : ShellExpressionSyntax
{
    public PowerShellTypeLiteralSyntax(
        ShellSyntaxToken openBracketToken,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken closeBracketToken)
        : base(
            ShellSyntaxKind.PowerShellTypeLiteral,
            openBracketToken.ToFullString() + nameToken.ToFullString() + closeBracketToken.ToFullString(),
            openBracketToken.FullSpan.Start,
            [openBracketToken, nameToken, closeBracketToken])
    {
        OpenBracketToken = openBracketToken;
        NameToken = nameToken;
        CloseBracketToken = closeBracketToken;
    }

    /// <summary>The opening bracket.</summary>
    public ShellSyntaxToken OpenBracketToken { get; }

    /// <summary>The type name, kept verbatim.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The closing bracket.</summary>
    public ShellSyntaxToken CloseBracketToken { get; }

    /// <summary>The type name between the brackets.</summary>
    public string Name => NameToken.Text;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitTypeLiteral(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitTypeLiteral(this);
}
