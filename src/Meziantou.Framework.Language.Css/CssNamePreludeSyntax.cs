namespace Meziantou.Framework.Language.Css;

/// <summary>Represents the name in the prelude of an at-rule such as <c>@keyframes fade</c> or <c>@property --size</c>.</summary>
public sealed partial class CssNamePreludeSyntax
{
    /// <summary>Gets the name, unquoted and with its escapes resolved.</summary>
    public string Name => NameToken.ValueText;
}
