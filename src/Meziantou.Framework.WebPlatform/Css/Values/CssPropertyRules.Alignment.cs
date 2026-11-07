using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Box alignment (https://drafts.csswg.org/css-align-3/)
internal static partial class CssPropertyRules
{
    private static readonly string[] AlignmentLonghands = ["align-content", "justify-content", "align-items", "justify-items", "align-self", "justify-self"];

    /// <summary>
    /// The alignment properties: first baseline is serialized as baseline (https://drafts.csswg.org/css-align-3/#valdef-justify-content-first-baseline),
    /// and legacy precedes its position (legacy left).
    /// </summary>
    private static string? CanonicalizeAlignment(List<CssComponentValue> significant)
    {
        var keywords = significant.ConvertAll(value => Infra.InfraAscii.ToAsciiLowercase(CssSerializer.SerializeValue([value], property: null)));
        if (keywords is ["first", "baseline"])
            return "baseline";

        if (keywords.Count == 2 && keywords.Contains("legacy"))
            return "legacy " + keywords.Find(keyword => keyword != "legacy");

        return null;
    }

    /// <summary>
    /// The shorthands of two longhands whose second value is optional: place-content, place-items, place-self and gap. The
    /// omitted second value is the first one, except for place-content whose first value is a &lt;baseline-position&gt;:
    /// justify-content is then start (https://drafts.csswg.org/css-align-3/#place-content).
    /// </summary>
    private static Dictionary<string, string>? ExpandPlace(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (shorthand.Longhands is not [var first, var second])
            return null;

        for (var split = significant.Count; split > 0; split--)
        {
            if (ParseLonghand(first, significant[..split]) is not { } firstValue)
                continue;

            string? secondValue;
            if (split < significant.Count)
            {
                secondValue = ParseLonghand(second, significant[split..]);
            }
            else if (shorthand.Name == "place-content" && firstValue.EndsWith("baseline", StringComparison.Ordinal))
            {
                secondValue = "start";
            }
            else
            {
                secondValue = ParseLonghand(second, significant);
            }

            if (secondValue is not null)
                return new(StringComparer.Ordinal) { [first] = firstValue, [second] = secondValue };
        }

        return null;

        static string? ParseLonghand(string name, List<CssComponentValue> values)
            => CssShorthands.ParseValue(name, CssShorthands.InsertSpaces(values), originalText: null) is [{ ShorthandName: null } longhand] ? longhand.Value : null;
    }

    /// <summary>The two values are serialized, or one when they are equal.</summary>
    private static string? SerializePair(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (shorthand.Longhands is not [var first, var second] || !lookup.TryGetValue(first, out var firstValue) || !lookup.TryGetValue(second, out var secondValue))
            return null;

        return firstValue == secondValue ? firstValue : firstValue + " " + secondValue;
    }
}
