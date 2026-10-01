namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a feature with a value, such as <c>(min-width: 600px)</c>.</summary>
public sealed partial class CssPlainFeatureSyntax
{
    /// <summary>Gets the name of the feature, with its escapes resolved.</summary>
    public string Name => NameToken.ValueText;
}
