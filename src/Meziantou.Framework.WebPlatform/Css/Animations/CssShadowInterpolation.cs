using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of the shadow lists (https://drafts.csswg.org/css-backgrounds-3/#box-shadow, https://drafts.csswg.org/web-animations-1/#animating-shadow-lists):
/// the shorter list is padded with transparent shadows of zero length, the shadows are interpolated component by component,
/// and an inset shadow cannot be interpolated with an outer shadow. The addition appends the lists.
/// </summary>
internal static class CssShadowInterpolation
{
    private sealed record Shadow(CssComponentValue Color, List<CssComponentValue> Lengths, bool Inset);

    public static Func<double, string?>? Prepare(CssInterpolationContext context, string property, string from, string to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b)
            return null;

        if (a.Count == 0 && b.Count == 0)
            return _ => "none";

        if (Combine(context, property, a, b, CssCombineOperation.Interpolate, 0.5) is null)
            return null;

        return progress => Combine(context, property, a, b, CssCombineOperation.Interpolate, progress);
    }

    public static string? Combine(CssInterpolationContext context, string property, string underlying, string value, CssCombineOperation operation)
    {
        if (Parse(underlying) is not { } a || Parse(value) is not { } b)
            return null;

        if (a.Count == 0 && b.Count == 0)
            return "none";

        if (operation == CssCombineOperation.Add)
            return string.Join(", ", a.Concat(b).Select(Serialize));

        return Combine(context, property, a, b, CssCombineOperation.Accumulate, 1);
    }

    private static string? Combine(CssInterpolationContext context, string property, List<Shadow> a, List<Shadow> b, CssCombineOperation operation, double progress)
    {
        var count = Math.Max(a.Count, b.Count);
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var shadowA = i < a.Count ? a[i] : GetTransparent(b[i]);
            var shadowB = i < b.Count ? b[i] : GetTransparent(a[i]);
            if (shadowA.Inset != shadowB.Inset || shadowA.Lengths.Count != shadowB.Lengths.Count)
                return null;

            if (CssColorInterpolation.Combine(context, property, shadowA.Color, shadowB.Color, operation == CssCombineOperation.Interpolate ? operation : CssCombineOperation.Add, progress) is not { } color)
                return null;

            var parts = new List<string> { color };
            for (var j = 0; j < shadowA.Lengths.Count; j++)
            {
                if (CssGenericInterpolation.CombineNumeric(shadowA.Lengths[j], shadowB.Lengths[j], CssNumericTraits.Empty, operation == CssCombineOperation.Interpolate ? operation : CssCombineOperation.Add, progress, topLevel: false) is not { } length)
                    return null;

                // The blur radius cannot be negative
                if (j == 2 && length.StartsWith('-', StringComparison.Ordinal))
                {
                    length = "0px";
                }

                parts.Add(length);
            }

            if (shadowA.Inset)
            {
                parts.Add("inset");
            }

            result.Add(string.Join(' ', parts));
        }

        return string.Join(", ", result);
    }

    private static Shadow GetTransparent(Shadow shadow)
    {
        var zero = CssGenericInterpolation.Parse("0px")[0];
        return new Shadow(CssGenericInterpolation.Parse("transparent")[0], [.. shadow.Lengths.Select(_ => zero)], shadow.Inset);
    }

    /// <summary>Parses a computed shadow list: an empty list for none, or null when it is not a shadow list.</summary>
    private static List<Shadow>? Parse(string value)
    {
        var components = CssGenericInterpolation.Parse(value);
        if (components is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ident.Token.IsIdent("none"))
            return [];

        var result = new List<Shadow>();
        foreach (var item in ValueMatcher.SplitAtCommas(components))
        {
            CssComponentValue? color = null;
            var lengths = new List<CssComponentValue>();
            var inset = false;
            foreach (var component in item)
            {
                if (component is CssPreservedToken { Type: CssTokenType.Ident } keyword && keyword.Token.IsIdent("inset"))
                {
                    inset = true;
                }
                else if (CssGenericInterpolation.IsNumeric(component))
                {
                    lengths.Add(component);
                }
                else if (color is null && CssGenericInterpolation.IsColor(component))
                {
                    color = component;
                }
                else
                {
                    return null;
                }
            }

            if (lengths.Count < 2)
                return null;

            result.Add(new Shadow(color ?? CssGenericInterpolation.Parse("currentcolor")[0], lengths, inset));
        }

        return result;
    }

    private static string Serialize(Shadow shadow)
    {
        var text = CssSerializer.SerializeValue([shadow.Color], property: null) + " " + string.Join(' ', shadow.Lengths.Select(length => CssSerializer.SerializeValue([length], property: null)));
        return shadow.Inset ? text + " inset" : text;
    }
}
