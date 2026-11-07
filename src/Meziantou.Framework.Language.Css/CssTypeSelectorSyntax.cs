namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a type selector, such as <c>div</c> or <c>svg|a</c>.</summary>
public sealed partial class CssTypeSelectorSyntax
{
    /// <summary>Gets the element name, with its escapes resolved.</summary>
    public string Name => NameToken.ValueText;
}
