namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a feature compared to one or two values, such as <c>(width &gt;= 600px)</c> or <c>(400px &lt;= width &lt; 800px)</c>.</summary>
/// <remarks>
/// <see cref="Left"/> and <see cref="Middle"/> are the two sides of the first comparison, whichever of them is the
/// name. With a second comparison, <see cref="Middle"/> is the name and <see cref="Right"/> is the other value.
/// </remarks>
public sealed partial class CssRangeFeatureSyntax
{
    /// <summary>Gets the name of the feature, with its escapes resolved.</summary>
    public string Name
    {
        get
        {
            if (RightComparison is null && Left.Values is [CssTokenValueSyntax { Token: var left }] && left.IsKind(SyntaxKind.IdentToken))
                return left.ValueText;

            return Middle.Values is [CssTokenValueSyntax { Token: var middle }] ? middle.ValueText : "";
        }
    }
}
