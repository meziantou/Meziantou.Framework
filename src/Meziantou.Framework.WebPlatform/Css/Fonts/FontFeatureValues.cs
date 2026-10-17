using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The features of <c>font-variant-alternates</c> (https://drafts.csswg.org/css-fonts-4/#font-variant-alternates-prop): the
/// names of its functions are the feature values of the <c>@font-feature-values</c> rules of the style sheets of the document
/// (https://drafts.csswg.org/css-fonts-4/#font-feature-values) for a family of the <c>font-family</c> of the element (the
/// first one whose rules define the name).
/// </summary>
internal static class FontFeatureValues
{
    /// <summary>Returns the features of a computed value of <c>font-variant-alternates</c>, or null for <c>normal</c>.</summary>
    public static IReadOnlyList<FontFeature>? ResolveAlternates(ComputedStyle style, string value)
    {
        if (value is "normal" or "")
            return null;

        var features = new List<FontFeature>();
        IReadOnlyList<CssFontFeatureValuesRule>? rules = null;
        string[]? families = null;
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } keyword when keyword.Token.Value.Equals("historical-forms", StringComparison.OrdinalIgnoreCase):
                    features.Add(new FontFeature(ShapePlanBuilder.Tag("hist"), 1));
                    break;

                case CssFunction function:
                {
                    var type = function.Name.ToLowerInvariant() switch
                    {
                        "stylistic" => CssFontFeatureType.Stylistic,
                        "styleset" => CssFontFeatureType.Styleset,
                        "character-variant" => CssFontFeatureType.CharacterVariant,
                        "swash" => CssFontFeatureType.Swash,
                        "ornaments" => CssFontFeatureType.Ornaments,
                        "annotation" => CssFontFeatureType.Annotation,
                        _ => (CssFontFeatureType?)null,
                    };

                    if (type is null)
                        continue;

                    rules ??= CollectRules(style);
                    families ??= GetFamilies(style);
                    foreach (var argument in CssValueComputer.SplitArguments(function.Value))
                    {
                        if (ValueMatcher.RemoveWhitespace(argument) is [CssPreservedToken { Type: CssTokenType.Ident } name] && FindValues(rules, families, type.Value, name.Token.Value) is { } values)
                        {
                            AddFeatures(type.Value, values, features);
                        }
                    }

                    break;
                }
            }
        }

        return features.Count == 0 ? null : features;
    }

    /// <summary>The features of the values of a name (https://drafts.csswg.org/css-fonts-4/#multi-value-features).</summary>
    private static void AddFeatures(CssFontFeatureType type, IReadOnlyList<uint> values, List<FontFeature> features)
    {
        if (values.Count == 0)
            return;

        switch (type)
        {
            case CssFontFeatureType.Stylistic:
                features.Add(new FontFeature(ShapePlanBuilder.Tag("salt"), values[0]));
                break;
            case CssFontFeatureType.Styleset:
                // Each value is a style set (1 to 20)
                foreach (var set in values)
                {
                    if (set is >= 1 and <= 99)
                    {
                        features.Add(new FontFeature(ShapePlanBuilder.Tag(string.Create(CultureInfo.InvariantCulture, $"ss{set:00}")), 1));
                    }
                }

                break;
            case CssFontFeatureType.CharacterVariant:
                // The character variant, then its value (1 by default)
                if (values[0] is >= 1 and <= 99)
                {
                    features.Add(new FontFeature(ShapePlanBuilder.Tag(string.Create(CultureInfo.InvariantCulture, $"cv{values[0]:00}")), values.Count > 1 ? values[1] : 1));
                }

                break;
            case CssFontFeatureType.Swash:
                features.Add(new FontFeature(ShapePlanBuilder.Tag("swsh"), values[0]));
                features.Add(new FontFeature(ShapePlanBuilder.Tag("cswh"), values[0]));
                break;
            case CssFontFeatureType.Ornaments:
                features.Add(new FontFeature(ShapePlanBuilder.Tag("ornm"), values[0]));
                break;
            case CssFontFeatureType.Annotation:
                features.Add(new FontFeature(ShapePlanBuilder.Tag("nalt"), values[0]));
                break;
        }
    }

    /// <summary>
    /// The values of a name for the first family of the element whose rules define it: the definition of the strongest cascade
    /// layer wins, then the last one (the rules are sorted by layer).
    /// </summary>
    private static IReadOnlyList<uint>? FindValues(IReadOnlyList<CssFontFeatureValuesRule> rules, string[] families, CssFontFeatureType type, string name)
    {
        foreach (var family in families)
        {
            IReadOnlyList<uint>? result = null;
            foreach (var rule in rules)
            {
                foreach (var ruleFamily in rule.Families)
                {
                    if (string.Equals(ruleFamily, family, StringComparison.OrdinalIgnoreCase) && rule.GetMap(type).Get(name) is { } values)
                    {
                        result = values;
                    }
                }
            }

            if (result is not null)
                return result;
        }

        return null;
    }

    private static string[] GetFamilies(ComputedStyle style) => [.. FontFaceSet.ParseFamilies(style.GetComputedValue("font-family"))];

    /// <summary>
    /// The <c>@font-feature-values</c> rules of the style sheets of the document that apply, from the weakest cascade layer to
    /// the strongest one (https://drafts.csswg.org/css-cascade-5/#layering), then in order.
    /// </summary>
    private static IReadOnlyList<CssFontFeatureValuesRule> CollectRules(ComputedStyle style) => style.Engine.GetFontFeatureValuesRules();
}
