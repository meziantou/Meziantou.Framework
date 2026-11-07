namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a feature without a value, such as <c>(hover)</c>.</summary>
public sealed partial class CssBooleanFeatureSyntax
{
    /// <summary>Gets the name of the feature, with its escapes resolved.</summary>
    public string Name => NameToken.ValueText;
}
