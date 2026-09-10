namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a group that scopes inline options to its body, <c>(?i:…)</c>.</summary>
public sealed partial class RegexOptionsGroupSyntax : RegexGroupSyntax
{
    /// <summary>The option letters, such as <c>ix-ms</c>.</summary>
    public string OptionsText => OptionsToken.Text;
}
