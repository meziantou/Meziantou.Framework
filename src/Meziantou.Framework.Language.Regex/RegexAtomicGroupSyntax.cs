namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents an atomic group, <c>(?&gt;…)</c>, which never gives back what it matched.</summary>
public sealed class RegexAtomicGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexAtomicGroupSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken groupKindToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.AtomicGroup, [openParenToken, groupKindToken, closeParenToken], Part(openParenToken), Part(groupKindToken), Part(alternation), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        GroupKindToken = groupKindToken;
        Alternation = alternation;
        CloseParenToken = closeParenToken;
        _childNodes = Children(alternation);
    }

    public RegexSyntaxToken GroupKindToken { get; }

    public RegexAlternationSyntax Alternation { get; }

    public override RegexSyntaxToken OpenParenToken { get; }

    public override RegexSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitAtomicGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitAtomicGroup(this);
}
