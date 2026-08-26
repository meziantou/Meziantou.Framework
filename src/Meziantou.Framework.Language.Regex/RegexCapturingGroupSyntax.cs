namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a plain capturing group, <c>(…)</c>.</summary>
public sealed class RegexCapturingGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexCapturingGroupSyntax(RegexSyntaxToken openParenToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken, int number)
        : base(RegexSyntaxKind.CapturingGroup, [openParenToken, closeParenToken], Part(openParenToken), Part(alternation), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        Alternation = alternation;
        CloseParenToken = closeParenToken;
        Number = number;
        _childNodes = Children(alternation);
    }

    public RegexAlternationSyntax Alternation { get; }

    public override RegexSyntaxToken OpenParenToken { get; }

    public override RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The group number the engine assigns, or 0 when the group does not capture.</summary>
    public int Number { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCapturingGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCapturingGroup(this);
}
