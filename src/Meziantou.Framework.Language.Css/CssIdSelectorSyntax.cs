namespace Meziantou.Framework.Language.Css;

/// <summary>Represents an ID selector, such as <c>#main</c>.</summary>
public sealed partial class CssIdSelectorSyntax
{
    /// <summary>Gets the ID, without the <c>#</c> and with its escapes resolved.</summary>
    public string Name => HashToken.ValueText;
}
