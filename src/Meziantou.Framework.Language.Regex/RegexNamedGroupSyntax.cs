namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a named capturing group, <c>(?&lt;name&gt;…)</c>, <c>(?'name'…)</c>, or <c>(?P&lt;name&gt;…)</c>.</summary>
public sealed partial class RegexNamedGroupSyntax : RegexGroupSyntax
{
    /// <summary>The group name.</summary>
    public string Name => NameToken.Text;
}
