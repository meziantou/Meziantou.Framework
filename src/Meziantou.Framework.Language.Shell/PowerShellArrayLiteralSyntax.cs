namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a comma-separated list of values, <c>1, 2, 3</c>.</summary>
public sealed class PowerShellArrayLiteralSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellArrayLiteralSyntax(
        IReadOnlyList<ShellExpressionSyntax>? elements,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens)
        : base(
            ShellSyntaxKind.PowerShellArrayLiteral,
            SeparatedNodes.BuildText(elements, separatorTokens),
            SeparatedNodes.GetFullStart(elements, separatorTokens),
            BuildTokens(separatorTokens))
    {
        Elements = elements ?? [];
        SeparatorTokens = separatorTokens ?? [];
        _childNodes = [.. (Elements as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The array elements.</summary>
    public IReadOnlyList<ShellExpressionSyntax> Elements { get; }

    /// <summary>The separator that follows each entry of <see cref="Elements"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> SeparatorTokens { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitArrayLiteral(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitArrayLiteral(this);

    private static List<ShellSyntaxToken> BuildTokens(
        IReadOnlyList<ShellSyntaxToken>? separatorTokens)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.AddRange(separatorTokens ?? []);

        return tokens;
    }
}
