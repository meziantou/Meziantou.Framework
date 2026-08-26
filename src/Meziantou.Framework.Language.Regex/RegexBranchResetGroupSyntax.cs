namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a PCRE branch reset group, <c>(?|…)</c>, in which every branch numbers its groups from the same base.</summary>
public sealed class RegexBranchResetGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexBranchResetGroupSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken groupKindToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.BranchResetGroup, [openParenToken, groupKindToken, closeParenToken], Part(openParenToken), Part(groupKindToken), Part(alternation), Part(closeParenToken))
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

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitBranchResetGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitBranchResetGroup(this);
}
