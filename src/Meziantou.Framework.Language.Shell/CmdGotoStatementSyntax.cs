namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>goto</c> statement.</summary>
public sealed class CmdGotoStatementSyntax : ShellStatementSyntax
{
    public CmdGotoStatementSyntax(
        ShellSyntaxToken gotoKeyword,
        ShellSyntaxToken labelToken)
        : base(
            ShellSyntaxKind.CmdGotoStatement,
            gotoKeyword.ToFullString() + labelToken.ToFullString(),
            gotoKeyword.FullSpan.Start,
            [gotoKeyword, labelToken])
    {
        GotoKeyword = gotoKeyword;
        LabelToken = labelToken;
    }

    /// <summary>The <c>goto</c> keyword.</summary>
    public ShellSyntaxToken GotoKeyword { get; }

    /// <summary>The target label, which may be <c>:eof</c>.</summary>
    public ShellSyntaxToken LabelToken { get; }

    /// <summary>The target label without any leading colon.</summary>
    public string Label => LabelToken.ValueText.TrimStart(':');

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdGoto(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdGoto(this);
}
