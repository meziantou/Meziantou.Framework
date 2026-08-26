namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a range inside a character class, such as <c>a-z</c>.</summary>
public sealed class RegexCharacterRangeSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexCharacterRangeSyntax(RegexSyntaxNode start, RegexSyntaxToken hyphenToken, RegexSyntaxNode? end)
        : base(RegexSyntaxKind.CharacterRange, [hyphenToken], Part(start), Part(hyphenToken), Part(end))
    {
        Start = start;
        HyphenToken = hyphenToken;
        End = end;
        _childNodes = Children(start, end);
    }

    public RegexSyntaxNode Start { get; }

    public RegexSyntaxToken HyphenToken { get; }

    /// <summary>The upper endpoint, absent when the class ends before it, as in <c>[a-</c>.</summary>
    public RegexSyntaxNode? End { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCharacterRange(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCharacterRange(this);
}
