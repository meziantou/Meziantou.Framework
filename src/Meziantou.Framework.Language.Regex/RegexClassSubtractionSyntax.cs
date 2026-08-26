namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents the <c>-[aeiou]</c> tail of a .NET character class subtraction, <c>[a-z-[aeiou]]</c>.</summary>
public sealed class RegexClassSubtractionSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexClassSubtractionSyntax(RegexSyntaxToken hyphenToken, RegexCharacterClassSyntax subtracted)
        : base(RegexSyntaxKind.ClassSubtraction, [hyphenToken], Part(hyphenToken), Part(subtracted))
    {
        HyphenToken = hyphenToken;
        Subtracted = subtracted;
        _childNodes = Children(subtracted);
    }

    public RegexSyntaxToken HyphenToken { get; }

    /// <summary>The class whose characters are removed from the enclosing one.</summary>
    public RegexCharacterClassSyntax Subtracted { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitClassSubtraction(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitClassSubtraction(this);
}
