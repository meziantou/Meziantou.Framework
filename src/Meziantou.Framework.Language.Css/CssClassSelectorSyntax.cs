namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a class selector, such as <c>.card</c>.</summary>
public sealed partial class CssClassSelectorSyntax
{
    /// <summary>Gets the class name, with its escapes resolved.</summary>
    public string Name => NameToken.ValueText;
}
