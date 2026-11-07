namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a Unicode category or block escape, <c>\p{L}</c> or <c>\P{IsGreek}</c>.</summary>
public sealed partial class RegexUnicodeCategorySyntax : RegexAtomSyntax
{
    /// <summary>Returns <see langword="true"/> for the negated form, spelled <c>\P{…}</c> or <c>\p{^…}</c>.</summary>
    public bool IsNegated => CategoryStartToken.Text is [.., 'P'] || NameToken.Text is ['^', ..];

    /// <summary>The category or block name, without the <c>^</c> that some dialects negate it with.</summary>
    public string Name => NameToken.Text.TrimStart('^');
}
