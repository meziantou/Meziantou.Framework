using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

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
        if (IsTransformList(property, context.Style, from) && IsTransformList(property, context.Style, to))
            return CssTransformInterpolation.Prepare(context, from, to);

        var traits = GetTraits(property, context.Style, from);
        var a = CssGenericInterpolation.Parse(from);
        var b = CssGenericInterpolation.Parse(to);
        if (CssGenericInterpolation.CombineLists(context, property, traits, a, b, CssCombineOperation.Interpolate, 0.5) is null)
            return null;

        return progress => CssGenericInterpolation.CombineLists(context, property, traits, a, b, CssCombineOperation.Interpolate, progress);
    }

    public static string? Combine(CssInterpolationContext context, string property, string underlying, string value, CssCombineOperation operation)
    {
        // A value that parsed as a <transform-list> is added and accumulated like the transform property (a syntax may have
        // other components, e.g. <transform-list> | none: the values of different components are not combined)
        if (IsTransformList(property, context.Style, underlying) && IsTransformList(property, context.Style, value))
            return operation == CssCombineOperation.Add ? CssTransformInterpolation.Add(underlying, value) : CssTransformInterpolation.Accumulate(context, underlying, value);

        var a = CssGenericInterpolation.Parse(underlying);
        var b = CssGenericInterpolation.Parse(value);
        if (CombineTransformFunctions(context, property, value, a, b) is { } transformFunctions)
            return transformFunctions;

        var traits = GetTraits(property, context.Style, value);
        return CssGenericInterpolation.CombineLists(context, property, traits, a, b, operation, 1);
    }

    /// <summary>
    /// Adds or accumulates the items of a <c>&lt;transform-function&gt;</c> list whose paired items are the same functions: as in the
    /// browsers, a function cannot be appended to another one, so the functions are accumulated
    /// (https://drafts.csswg.org/css-transforms-2/#combining-transform-lists, e.g. scale(2) plus scale(4) is scale(5)).
    /// </summary>
    private static string? CombineTransformFunctions(CssInterpolationContext context, string property, string value, List<CssComponentValue> a, List<CssComponentValue> b)
    {
        if (a.Count != b.Count || a.Count == 0 || context.Style?.RegisteredProperties.GetValueOrDefault(property) is not { } registration
            || !registration.Syntax.TryMatch(CssParser.ParseListOfComponentValues(value), out var component) || component is not { IsDataType: true, Name: "transform-function" })
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] is CssPreservedToken { Type: CssTokenType.Comma } && b[i] is CssPreservedToken { Type: CssTokenType.Comma })
            {
                builder.Append(", ");
                continue;
            }

            if (a[i] is not CssFunction functionA || b[i] is not CssFunction functionB || !InfraAscii.EqualsIgnoreAsciiCase(functionA.Name, functionB.Name)
                || CssTransformInterpolation.Accumulate(context, CssSerializer.SerializeValue([functionA], property: null), CssSerializer.SerializeValue([functionB], property: null)) is not { } combined)
            {
                return null;
            }

            builder.Append(combined);
        }

        return builder.ToString();
    }

    /// <summary>Returns whether a value of a registered custom property matches the <c>&lt;transform-list&gt;</c> component of its syntax.</summary>
    private static bool IsTransformList(string property, ComputedStyle? style, string value)
        => style?.RegisteredProperties.GetValueOrDefault(property) is { } registration
            && registration.Syntax.TryMatch(CssParser.ParseListOfComponentValues(value), out var component)
            && component is { IsDataType: true, Name: "transform-list" };

    /// <summary>
    /// The numeric traits of the values of a registered custom property: the ones of the component of its syntax that
    /// <paramref name="value"/> matches (a syntax may have several components, e.g. <c>&lt;length-percentage&gt; | none</c>).
    /// </summary>
    private static CssNumericTraits GetTraits(string property, ComputedStyle? style, string value)
    {
        if (style?.RegisteredProperties.GetValueOrDefault(property) is not { } registration)
            return CssNumericTraits.Empty;

        var component = registration.Syntax.Components is [var single] ? single
            : registration.Syntax.TryMatch(CssParser.ParseListOfComponentValues(value), out var matched) ? matched : null;
        if (component is { Grammar: { } grammar })
            return CssNumericTraits.FromGrammar(grammar, component.Name == "length-percentage" ? "px" : null);

        return CssNumericTraits.Empty;
    }
}
