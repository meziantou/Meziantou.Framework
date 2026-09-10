namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a <c>*</c>, <c>+</c>, or <c>?</c> quantifier, with an optional lazy or possessive modifier.</summary>
public sealed partial class RegexSimpleQuantifierSyntax : RegexQuantifierSyntax
{
    public override int MinCount => OperatorToken.Text == "+" ? 1 : 0;

    public override int? MaxCount => OperatorToken.Text == "?" ? 1 : null;
}
