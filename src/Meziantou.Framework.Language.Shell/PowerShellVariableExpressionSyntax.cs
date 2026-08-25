namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a variable reference such as <c>$name</c>, <c>${name}</c>, <c>$env:PATH</c>, or a splat <c>@name</c>.</summary>
public sealed class PowerShellVariableExpressionSyntax : ShellExpressionSyntax
{
    public PowerShellVariableExpressionSyntax(
        ShellSyntaxToken sigilToken,
        ShellSyntaxToken nameToken)
        : base(
            ShellSyntaxKind.PowerShellVariableExpression,
            sigilToken.ToFullString() + nameToken.ToFullString(),
            sigilToken.FullSpan.Start,
            [sigilToken, nameToken])
    {
        SigilToken = sigilToken;
        NameToken = nameToken;
    }

    /// <summary>The <c>$</c> or <c>@</c> that introduces the variable.</summary>
    public ShellSyntaxToken SigilToken { get; }

    /// <summary>The variable name, including any scope or drive prefix.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The variable name without the sigil or braces.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for a splatted variable, <c>@name</c>.</summary>
    public bool IsSplatted => SigilToken.Text is ['@', ..];

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellVariable(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellVariable(this);
}
