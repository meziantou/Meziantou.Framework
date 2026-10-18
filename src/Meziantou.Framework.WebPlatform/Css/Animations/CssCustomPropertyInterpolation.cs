using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The animation of the custom properties (https://drafts.css-houdini.org/css-properties-values-api/#animation-behavior-of-custom-properties):
/// the unregistered properties, and the registered properties with the universal syntax or an identifier, are discrete; the
/// registered properties are interpolated by the type of their syntax (the lists must have the same length).
/// </summary>
internal static class CssCustomPropertyInterpolation
{
    public static CssInterpolationKind GetKind(string property, ComputedStyle? style)
    {
        if (style?.RegisteredProperties.GetValueOrDefault(property) is not { Syntax.IsUniversal: false } registration)
            return CssInterpolationKind.Discrete;

        // A <transform-list> is interpolated, added and accumulated like the transform property
        if (registration.Syntax.Components is [{ IsDataType: true, Name: "transform-list" }])
            return CssInterpolationKind.TransformList;

        foreach (var component in registration.Syntax.Components)
        {
            if (component.IsDataType && component.Name is "length" or "number" or "percentage" or "length-percentage" or "color" or "integer" or "angle" or "time" or "resolution" or "transform-function" or "transform-list")
                return CssInterpolationKind.ByComputedValue;
        }

        return CssInterpolationKind.Discrete;
    }

    public static Func<double, string?>? Prepare(CssInterpolationContext context, string property, string from, string to)
    {
        var traits = GetTraits(property, context.Style, from);
        var a = CssGenericInterpolation.Parse(from);
        var b = CssGenericInterpolation.Parse(to);
        if (CssGenericInterpolation.CombineLists(context, property, traits, a, b, CssCombineOperation.Interpolate, 0.5) is null)
            return null;

        return progress => CssGenericInterpolation.CombineLists(context, property, traits, a, b, CssCombineOperation.Interpolate, progress);
    }

    public static string? Combine(CssInterpolationContext context, string property, string underlying, string value, CssCombineOperation operation)
    {
        var traits = GetTraits(property, context.Style, value);
        return CssGenericInterpolation.CombineLists(context, property, traits, CssGenericInterpolation.Parse(underlying), CssGenericInterpolation.Parse(value), operation, 1);
    }

    /// <summary>
    /// The numeric traits of the values of a registered custom property: the ones of the component of its syntax that
    /// <paramref name="value"/> matches (a syntax may have several components, e.g. <c>&lt;length-percentage&gt; | none</c>).
    /// </summary>
    private static CssNumericTraits GetTraits(string property, ComputedStyle? style, string value)
    {
        if (style?.RegisteredProperties.GetValueOrDefault(property) is not { } registration)
            return CssNumericTraits.Empty;

        var component = registration.Syntax.Components is [var single] ? single
            : registration.Syntax.TryMatch(Syntax.CssParser.ParseListOfComponentValues(value), out var matched) ? matched : null;
        if (component is { Grammar: { } grammar })
            return CssNumericTraits.FromGrammar(grammar, component.Name == "length-percentage" ? "px" : null);

        return CssNumericTraits.Empty;
    }
}
