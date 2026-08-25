namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a label, <c>:name</c>, which is a jump target for <c>goto</c> and <c>call</c>.</summary>
public sealed class CmdLabelStatementSyntax : ShellStatementSyntax
{
    public CmdLabelStatementSyntax(
        ShellSyntaxToken colonToken,
        ShellSyntaxToken nameToken)
        : base(
            ShellSyntaxKind.CmdLabelStatement,
            colonToken.ToFullString() + nameToken.ToFullString(),
            colonToken.FullSpan.Start,
            [colonToken, nameToken])
    {
        ColonToken = colonToken;
        NameToken = nameToken;
    }

    /// <summary>The leading colon.</summary>
    public ShellSyntaxToken ColonToken { get; }

    /// <summary>The label name.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The label name without its leading colon.</summary>
    public string Name => NameToken.ValueText;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdLabel(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdLabel(this);
}
