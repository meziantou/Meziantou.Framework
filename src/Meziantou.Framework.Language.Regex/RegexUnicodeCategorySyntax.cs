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

    /// <summary>Returns <see langword="true"/> for the negated form, spelled <c>\P{…}</c> or <c>\p{^…}</c>.</summary>
    public bool IsNegated => CategoryStartToken.Text is [.., 'P'] || NameToken?.Text is ['^', ..];

    /// <summary>The category or block name, without the <c>^</c> that some flavors negate it with.</summary>
    public string Name => NameToken?.Text is { } text ? text.TrimStart('^') : string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitUnicodeCategory(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitUnicodeCategory(this);
}
