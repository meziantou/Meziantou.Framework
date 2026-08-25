namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents two or more commands joined by <c>|</c> or <c>|&amp;</c>.</summary>
public sealed class ShellPipelineSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellPipelineSyntax(ShellSyntaxToken? bangToken, IReadOnlyList<ShellStatementSyntax>? commands, IReadOnlyList<ShellSyntaxToken>? operatorTokens)
        : base(
            ShellSyntaxKind.Pipeline,
            (bangToken?.ToFullString() ?? string.Empty) + SeparatedNodes.BuildText(commands, operatorTokens),
            bangToken?.FullSpan.Start ?? SeparatedNodes.GetFullStart(commands, operatorTokens),
            BuildTokens(bangToken, operatorTokens))
    {
        BangToken = bangToken;
        Commands = commands ?? [];
        OperatorTokens = operatorTokens ?? [];
        _childNodes = [.. Commands];
    }

    /// <summary>The leading <c>!</c> that negates the pipeline exit status, when present.</summary>
    public ShellSyntaxToken? BangToken { get; }

    public IReadOnlyList<ShellStatementSyntax> Commands { get; }

    /// <summary>The pipe operator that follows each command. <c>OperatorTokens[i]</c> follows <c>Commands[i]</c>.</summary>
    public IReadOnlyList<ShellSyntaxToken> OperatorTokens { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public ShellPipelineSyntax WithCommands(IEnumerable<ShellStatementSyntax>? commands)
    {
        var updated = commands?.ToArray() ?? [];
        if (updated.SequenceEqual(Commands))
            return this;

        return new ShellPipelineSyntax(BangToken, updated, OperatorTokens);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPipeline(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPipeline(this);

    private static IReadOnlyList<ShellSyntaxToken> BuildTokens(ShellSyntaxToken? bangToken, IReadOnlyList<ShellSyntaxToken>? operatorTokens)
    {
        if (bangToken is null)
            return operatorTokens ?? [];

        return [bangToken, .. operatorTokens ?? []];
    }
}
