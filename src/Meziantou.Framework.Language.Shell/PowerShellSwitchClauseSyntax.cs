namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents one clause of a <c>switch</c> statement.</summary>
public sealed class PowerShellSwitchClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellSwitchClauseSyntax(
        ShellSyntaxNode pattern,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellSwitchClause,
            pattern.ToFullString() + body.ToFullString(),
            pattern.FullSpan.Start,
            null)
    {
        Pattern = pattern;
        Body = body;
        _childNodes = [.. SingleNode(Pattern), .. SingleNode(Body)];
    }

    /// <summary>The pattern, or the <c>default</c> keyword expression.</summary>
    public ShellSyntaxNode Pattern { get; }

    /// <summary>The clause body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitSwitchClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitSwitchClause(this);
}
