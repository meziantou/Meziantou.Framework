namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents one branch of an alternation: the terms that must match one after another.</summary>
/// <remarks>A branch can be empty, as both branches of <c>(|)</c> are.</remarks>
public sealed class RegexSequenceSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexSequenceSyntax(IReadOnlyList<RegexTermSyntax>? terms, int fullStart = 0)
        : base(
            RegexSyntaxKind.Sequence,
            BuildFullText(terms ?? []),
            terms is { Count: > 0 } ? terms[0].FullSpan.Start : fullStart,
            tokens: null)
    {
        Terms = terms ?? [];
        _childNodes = [.. Terms];
    }

    public IReadOnlyList<RegexTermSyntax> Terms { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public RegexSequenceSyntax WithTerms(IEnumerable<RegexTermSyntax>? terms)
    {
        var updated = terms?.ToArray() ?? [];
        if (updated.SequenceEqual(Terms))
            return this;

        return new RegexSequenceSyntax(updated, FullSpan.Start);
    }

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitSequence(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitSequence(this);
}
