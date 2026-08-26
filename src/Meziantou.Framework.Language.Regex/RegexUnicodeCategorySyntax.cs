namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a Unicode category or block escape, <c>\p{L}</c> or <c>\P{IsGreek}</c>.</summary>
public sealed class RegexUnicodeCategorySyntax : RegexAtomSyntax
{
    public RegexUnicodeCategorySyntax(RegexSyntaxToken categoryStartToken, RegexSyntaxToken? openBraceToken, RegexSyntaxToken? nameToken, RegexSyntaxToken? closeBraceToken)
        : base(RegexSyntaxKind.UnicodeCategory, [categoryStartToken, openBraceToken, nameToken, closeBraceToken], Part(categoryStartToken), Part(openBraceToken), Part(nameToken), Part(closeBraceToken))
    {
        CategoryStartToken = categoryStartToken;
        OpenBraceToken = openBraceToken;
        NameToken = nameToken;
        CloseBraceToken = closeBraceToken;
    }

    /// <summary>The <c>\p</c> or <c>\P</c> that introduces the escape.</summary>
    public RegexSyntaxToken CategoryStartToken { get; }

    public RegexSyntaxToken? OpenBraceToken { get; }

    public RegexSyntaxToken? NameToken { get; }

    public RegexSyntaxToken? CloseBraceToken { get; }

    /// <summary>Returns <see langword="true"/> for <c>\P</c>, the negated form.</summary>
    public bool IsNegated => CategoryStartToken.Text is [.., 'P'];

    /// <summary>The category or block name, or an empty string when the construct is incomplete.</summary>
    public string Name => NameToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitUnicodeCategory(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitUnicodeCategory(this);
}
