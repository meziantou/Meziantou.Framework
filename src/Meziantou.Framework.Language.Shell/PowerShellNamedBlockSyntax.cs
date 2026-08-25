namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>begin</c>, <c>process</c>, <c>end</c>, <c>clean</c>, or <c>dynamicparam</c> block.</summary>
public sealed class PowerShellNamedBlockSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellNamedBlockSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken keyword,
        PowerShellScriptBlockSyntax body)
        : base(
            kind,
            keyword.ToFullString() + body.ToFullString(),
            keyword.FullSpan.Start,
            [keyword])
    {
        Keyword = keyword;
        Body = body;
        _childNodes = [.. SingleNode(Body)];
    }

    /// <summary>The block keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    /// <summary>The block body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitNamedBlock(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitNamedBlock(this);
}
