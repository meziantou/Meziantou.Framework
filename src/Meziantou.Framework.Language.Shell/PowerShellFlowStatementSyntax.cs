namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents <c>break</c>, <c>continue</c>, <c>return</c>, <c>exit</c>, or <c>throw</c>.</summary>
public sealed class PowerShellFlowStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellFlowStatementSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken keyword,
        ShellSyntaxNode? value)
        : base(
            kind,
            keyword.ToFullString() + (value?.ToFullString() ?? string.Empty),
            keyword.FullSpan.Start,
            [keyword])
    {
        Keyword = keyword;
        Value = value;
        _childNodes = [.. OptionalNode(Value)];
    }

    /// <summary>The flow keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    /// <summary>The returned, thrown, or label value, when present.</summary>
    public ShellSyntaxNode? Value { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitFlowStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitFlowStatement(this);
}
