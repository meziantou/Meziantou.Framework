namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a term followed by a quantifier, such as <c>a*</c> or <c>(ab){2,3}?</c>.</summary>
public sealed class RegexQuantifiedSyntax : RegexTermSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexQuantifiedSyntax(RegexTermSyntax term, RegexQuantifierSyntax quantifier)
        : base(RegexSyntaxKind.Quantified, [], Part(term), Part(quantifier))
    {
        Term = term;
        Quantifier = quantifier;
        _childNodes = Children(term, quantifier);
    }

    public RegexTermSyntax Term { get; }

    public RegexQuantifierSyntax Quantifier { get; }

    /// <summary>How the quantifier backtracks.</summary>
    public RegexQuantifierMode Mode => Quantifier.Mode;

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitQuantified(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitQuantified(this);
}
