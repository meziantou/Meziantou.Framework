using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of the filter lists (https://drafts.fxtf.org/filter-effects/#interpolation-of-filters): the lists must
/// have the same functions in the same order (the shorter list is padded with the initial values of the functions of the
/// other list, none is an empty list), and a url() is interpolated discretely. The addition appends the lists.
/// </summary>
internal static class CssFilterInterpolation
{
    private sealed record Filter(string Name, List<CssComponentValue> Arguments);

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
            return string.Join(' ', a.Concat(b).Select(Serialize));

        return Combine(context, property, a, b, CssCombineOperation.Accumulate, 1);
    }

    private static string? Combine(CssInterpolationContext context, string property, List<Filter> a, List<Filter> b, CssCombineOperation operation, double progress)
    {
        var count = Math.Max(a.Count, b.Count);
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var filterA = i < a.Count ? a[i] : GetInitial(b[i]);
            var filterB = i < b.Count ? b[i] : GetInitial(a[i]);
            if (filterA is null || filterB is null || filterA.Name != filterB.Name || filterA.Name == "url")
                return null;

            if (filterA.Name == "drop-shadow")
            {
                if (CssShadowInterpolation.Prepare(context, property, SerializeArguments(filterA), SerializeArguments(filterB)) is not { } shadow
                    || (operation == CssCombineOperation.Interpolate ? shadow(progress) : CssShadowInterpolation.Combine(context, property, SerializeArguments(filterA), SerializeArguments(filterB), CssCombineOperation.Accumulate)) is not { } combinedShadow)
                    return null;

                result.Add("drop-shadow(" + combinedShadow + ")");
                continue;
            }

            if (filterA.Arguments.Count != 1 || filterB.Arguments.Count != 1)
                return null;

            string? combined;
            if (operation == CssCombineOperation.Accumulate && GetInitialValue(filterA.Name) is 1
                && CssGenericInterpolation.TryGetNumber(filterA.Arguments[0], out var valueA, out _) && CssGenericInterpolation.TryGetNumber(filterB.Arguments[0], out var valueB, out _))
            {
                // The functions whose initial value is 1 (e.g. brightness) accumulate as a + b - 1
                combined = CssGenericInterpolation.FormatNumber(valueA + valueB - 1);
            }
            else
            {
                combined = CssGenericInterpolation.CombineNumeric(filterA.Arguments[0], filterB.Arguments[0], CssNumericTraits.Empty, operation == CssCombineOperation.Interpolate ? operation : CssCombineOperation.Add, progress, topLevel: false);
            }

            if (combined is null)
                return null;

            // The values of the functions cannot be negative (the accumulated values are clamped after the interpolation)
            if (operation == CssCombineOperation.Interpolate && combined.StartsWith('-', StringComparison.Ordinal) && filterA.Name != "hue-rotate")
            {
                combined = filterA.Name == "blur" ? "0px" : "0";
            }

            result.Add(filterA.Name + "(" + combined + ")");
        }

        return string.Join(' ', result);
    }

    /// <summary>Returns the filter with the initial value of a function (https://drafts.fxtf.org/filter-effects/#interpolation-of-filters), or null for url().</summary>
    private static Filter? GetInitial(Filter filter)
    {
        var text = filter.Name switch
        {
            "blur" => "0px",
            "brightness" or "contrast" or "opacity" or "saturate" => "1",
            "grayscale" or "invert" or "sepia" => "0",
            "hue-rotate" => "0deg",
            "drop-shadow" => "transparent 0px 0px 0px",
            _ => null,
        };

        return text is null ? null : new Filter(filter.Name, filter.Name == "drop-shadow" ? CssGenericInterpolation.Parse(text) : [CssGenericInterpolation.Parse(text)[0]]);
    }

    private static int? GetInitialValue(string name) => name switch
    {
        "brightness" or "contrast" or "opacity" or "saturate" => 1,
        "grayscale" or "invert" or "sepia" => 0,
        _ => null,
    };

    /// <summary>Parses a computed filter list: an empty list for none, or null when it is not a list of filter functions.</summary>
    private static List<Filter>? Parse(string value)
    {
        var components = CssGenericInterpolation.Parse(value);
        if (components is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ident.Token.IsIdent("none"))
            return [];

        var result = new List<Filter>(components.Count);
        foreach (var component in components)
        {
            // A url() is kept for the addition, but cannot be interpolated
            if (component is CssPreservedToken { Type: CssTokenType.Url } or CssFunction { Name: "url" })
            {
                result.Add(new Filter("url", [component]));
                continue;
            }

            if (component is not CssFunction function || InfraAscii.ToAsciiLowercase(function.Name) is not ("blur" or "brightness" or "contrast" or "drop-shadow" or "grayscale" or "hue-rotate" or "invert" or "opacity" or "saturate" or "sepia"))
                return null;

            var arguments = ValueMatcher.RemoveWhitespace(function.Value);
            result.Add(new Filter(InfraAscii.ToAsciiLowercase(function.Name), arguments));
        }

        return result;
    }

    private static string SerializeArguments(Filter filter) => string.Join(' ', filter.Arguments.Select(argument => CssSerializer.SerializeValue([argument], property: null)));

    private static string Serialize(Filter filter) => filter.Name == "url" ? SerializeArguments(filter) : filter.Name + "(" + SerializeArguments(filter) + ")";
}
