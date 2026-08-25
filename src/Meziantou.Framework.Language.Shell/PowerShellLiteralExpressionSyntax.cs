namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a number, a verbatim string, or a bare word used as a value.</summary>
public sealed class PowerShellLiteralExpressionSyntax : ShellExpressionSyntax
{
    public PowerShellLiteralExpressionSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken token)
        : base(
            kind,
            token.ToFullString(),
            token.FullSpan.Start,
            [token])
    {
        Token = token;
    }

    /// <summary>The literal token.</summary>
    public ShellSyntaxToken Token { get; }

    /// <summary>The literal value with quoting resolved.</summary>
    public string Value => Token.ValueText;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellLiteral(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellLiteral(this);
}
