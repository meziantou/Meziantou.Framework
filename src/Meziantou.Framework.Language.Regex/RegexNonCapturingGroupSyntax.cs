namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a non-capturing group, <c>(?:…)</c>.</summary>
public sealed class RegexNonCapturingGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexNonCapturingGroupSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken groupKindToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.NonCapturingGroup, [openParenToken, groupKindToken, closeParenToken], Part(openParenToken), Part(groupKindToken), Part(alternation), Part(closeParenToken))
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

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitNonCapturingGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitNonCapturingGroup(this);
}
