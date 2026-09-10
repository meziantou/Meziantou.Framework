namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a named backreference such as <c>\k&lt;name&gt;</c>, <c>\k'name'</c>, or <c>\&lt;name&gt;</c>.</summary>
public sealed partial class RegexNamedBackreferenceSyntax : RegexAtomSyntax
{
    /// <summary>The group name the reference names, or an empty string when the construct is incomplete.</summary>
    public string Name => NameToken.Text;
}
