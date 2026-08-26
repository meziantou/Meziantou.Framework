namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a list of branches separated by <c>|</c>.</summary>
/// <remarks>
/// Every pattern body is an alternation, even one with a single branch and no <c>|</c> at all. Keeping the shape
/// uniform means a consumer never has to handle two spellings of the same thing, and it is what makes
/// <see cref="RegexSyntaxNode.IsEquivalentTo"/> well defined.
/// </remarks>
public sealed class RegexAlternationSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexAlternationSyntax(IReadOnlyList<RegexSequenceSyntax>? branches, IReadOnlyList<RegexSyntaxToken>? barTokens = null, int fullStart = 0)
        : base(
            RegexSyntaxKind.Alternation,
            SeparatedNodes.BuildText(branches, barTokens),
            branches is { Count: > 0 } || barTokens is { Count: > 0 } ? SeparatedNodes.GetFullStart(branches, barTokens) : fullStart,
            barTokens ?? [])
    {
        Branches = branches ?? [];
        BarTokens = barTokens ?? [];
        _childNodes = [.. Branches];
    }

    public IReadOnlyList<RegexSequenceSyntax> Branches { get; }

    /// <summary>The <c>|</c> that follows each branch. <c>BarTokens[i]</c> follows <c>Branches[i]</c>.</summary>
    public IReadOnlyList<RegexSyntaxToken> BarTokens { get; }

    /// <summary>Returns <see langword="true"/> when the alternation has more than one branch.</summary>
    public bool HasAlternatives => Branches.Count > 1 || BarTokens.Count > 0;

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitAlternation(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitAlternation(this);
}
