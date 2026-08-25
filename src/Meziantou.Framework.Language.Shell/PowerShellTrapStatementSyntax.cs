namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>trap</c> statement.</summary>
public sealed class PowerShellTrapStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellTrapStatementSyntax(
        ShellSyntaxToken trapKeyword,
        PowerShellTypeLiteralSyntax? typeFilter,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellTrapStatement,
            trapKeyword.ToFullString() + (typeFilter?.ToFullString() ?? string.Empty) + body.ToFullString(),
            trapKeyword.FullSpan.Start,
            [trapKeyword])
    {
        TrapKeyword = trapKeyword;
        TypeFilter = typeFilter;
        Body = body;
        _childNodes = [.. OptionalNode(TypeFilter), .. SingleNode(Body)];
    }

    /// <summary>The <c>trap</c> keyword.</summary>
    public ShellSyntaxToken TrapKeyword { get; }

    /// <summary>The exception type this trap handles, when present.</summary>
    public PowerShellTypeLiteralSyntax? TypeFilter { get; }

    /// <summary>The trap body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitTrapStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitTrapStatement(this);
}
