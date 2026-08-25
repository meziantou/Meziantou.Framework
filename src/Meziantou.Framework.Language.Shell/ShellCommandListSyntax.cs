namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents two or more pipelines joined by the <c>&amp;&amp;</c> and <c>||</c> operators.</summary>
public sealed class ShellCommandListSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellCommandListSyntax(IReadOnlyList<ShellStatementSyntax>? pipelines, IReadOnlyList<ShellSyntaxToken>? operatorTokens)
        : base(
            ShellSyntaxKind.CommandList,
            SeparatedNodes.BuildText(pipelines, operatorTokens),
            SeparatedNodes.GetFullStart(pipelines, operatorTokens),
            operatorTokens ?? [])
    {
        Pipelines = pipelines ?? [];
        OperatorTokens = operatorTokens ?? [];
        _childNodes = [.. Pipelines];
    }

    public IReadOnlyList<ShellStatementSyntax> Pipelines { get; }

    /// <summary>The operator that follows each pipeline. <c>OperatorTokens[i]</c> follows <c>Pipelines[i]</c>.</summary>
    public IReadOnlyList<ShellSyntaxToken> OperatorTokens { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public ShellCommandListSyntax WithPipelines(IEnumerable<ShellStatementSyntax>? pipelines)
    {
        var updated = pipelines?.ToArray() ?? [];
        if (updated.SequenceEqual(Pipelines))
            return this;

        return new ShellCommandListSyntax(updated, OperatorTokens);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCommandList(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCommandList(this);
}
