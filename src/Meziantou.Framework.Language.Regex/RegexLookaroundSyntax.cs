namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a lookahead or lookbehind, <c>(?=…)</c>, <c>(?!…)</c>, <c>(?&lt;=…)</c>, or <c>(?&lt;!…)</c>.</summary>
public sealed class RegexLookaroundSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexLookaroundSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken groupKindToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.Lookaround, [openParenToken, groupKindToken, closeParenToken], Part(openParenToken), Part(groupKindToken), Part(alternation), Part(closeParenToken))
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

    /// <summary>The direction and polarity of the assertion.</summary>
    public RegexLookaroundKind LookaroundKind => GroupKindToken.Text switch
    {
        "?!" => RegexLookaroundKind.NegativeLookahead,
        "?<=" => RegexLookaroundKind.PositiveLookbehind,
        "?<!" => RegexLookaroundKind.NegativeLookbehind,
        _ => RegexLookaroundKind.PositiveLookahead,
    };

    /// <summary>Returns <see langword="true"/> when the assertion looks backwards.</summary>
    public bool IsLookbehind => LookaroundKind is RegexLookaroundKind.PositiveLookbehind or RegexLookaroundKind.NegativeLookbehind;

    /// <summary>Returns <see langword="true"/> when the assertion must fail for the pattern to match.</summary>
    public bool IsNegative => LookaroundKind is RegexLookaroundKind.NegativeLookahead or RegexLookaroundKind.NegativeLookbehind;

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitLookaround(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitLookaround(this);
}
