namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents an inline option setter such as <c>(?i)</c> or <c>(?-x)</c>, which has no body.</summary>
public sealed partial class RegexInlineOptionsSyntax : RegexAtomSyntax
{
    /// <summary>The option letters, such as <c>imnsx-imnsx</c>.</summary>
    public string OptionsText => OptionsToken.Text;
}
