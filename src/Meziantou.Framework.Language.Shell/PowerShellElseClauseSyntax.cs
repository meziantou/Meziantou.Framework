namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>else</c> clause.</summary>
public sealed class PowerShellElseClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellElseClauseSyntax(
        ShellSyntaxToken elseKeyword,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellElseClause,
            elseKeyword.ToFullString() + body.ToFullString(),
            elseKeyword.FullSpan.Start,
            [elseKeyword])
    {
        ElseKeyword = elseKeyword;
        Body = body;
        _childNodes = [.. SingleNode(Body)];
    }

    /// <summary>The <c>else</c> keyword.</summary>
    public ShellSyntaxToken ElseKeyword { get; }

    /// <summary>The body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellElseClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellElseClause(this);
}
