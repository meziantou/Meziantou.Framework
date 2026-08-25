namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>catch</c> clause, optionally filtered by exception type.</summary>
public sealed class PowerShellCatchClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellCatchClauseSyntax(
        ShellSyntaxToken catchKeyword,
        IReadOnlyList<PowerShellTypeLiteralSyntax>? typeFilters,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellCatchClause,
            catchKeyword.ToFullString() + SeparatedNodes.BuildText(typeFilters, separatorTokens) + body.ToFullString(),
            catchKeyword.FullSpan.Start,
            BuildTokens(catchKeyword, separatorTokens))
    {
        CatchKeyword = catchKeyword;
        TypeFilters = typeFilters ?? [];
        SeparatorTokens = separatorTokens ?? [];
        Body = body;
        _childNodes = [.. (TypeFilters as IEnumerable<ShellSyntaxNode>), .. SingleNode(Body)];
    }

    /// <summary>The <c>catch</c> keyword.</summary>
    public ShellSyntaxToken CatchKeyword { get; }

    /// <summary>The exception types this clause handles.</summary>
    public IReadOnlyList<PowerShellTypeLiteralSyntax> TypeFilters { get; }

    /// <summary>The separator that follows each entry of <see cref="TypeFilters"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> SeparatorTokens { get; }

    /// <summary>The clause body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCatchClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCatchClause(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken catchKeyword,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(catchKeyword);
        tokens.AddRange(separatorTokens ?? []);

        return tokens;
    }
}
