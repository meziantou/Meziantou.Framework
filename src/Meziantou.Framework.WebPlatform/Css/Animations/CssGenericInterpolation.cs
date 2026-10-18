using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// Combines two computed values component by component (https://drafts.csswg.org/web-animations-1/#by-computed-value): the
/// values must have the same structure (the same keywords, functions and separators); the numbers, percentages, dimensions
/// and colors are combined by their type (https://drafts.csswg.org/css-values-4/#combining-values).
/// </summary>
internal static class CssGenericInterpolation
{
    /// <summary>Parses a computed value into its significant component values.</summary>
    public static List<CssComponentValue> Parse(string value) => ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));

    /// <summary>
    /// Combines two repeatable lists (https://drafts.csswg.org/web-animations-1/#repeatable-list): comma-separated lists
    /// of different lengths are repeated to the least common multiple of their lengths, then combined item by item.
    /// </summary>
    public static string? CombineRepeatableLists(CssInterpolationContext context, string property, CssNumericTraits traits, List<CssComponentValue> a, List<CssComponentValue> b, CssCombineOperation operation, double progress)
    {
        // https://drafts.csswg.org/css-gaps-1/#interpolation-behavior: "Expansion: Expand any integer repeater into its equivalent
        // list of values", then the lists are interpolated as repeatable lists (the lists with an auto repeater are combined
        // when their structures match)
        if (property is "column-rule-width" or "row-rule-width" or "column-rule-color" or "row-rule-color")
        {
            if (HasAutoRepeater(a) || HasAutoRepeater(b))
                return CombineAutoRepeaterLists(context, property, traits, a, b, operation, progress);

            a = ExpandIntegerRepeaters(a);
            b = ExpandIntegerRepeaters(b);
        }

        var itemsA = ValueMatcher.SplitAtCommas(a);
        var itemsB = ValueMatcher.SplitAtCommas(b);
        if (itemsA.Count == itemsB.Count)
            return CombineLists(context, property, traits, a, b, operation, progress);

        return CombineRepeatedItems(context, property, traits, itemsA, itemsB, operation, progress);
    }

    /// <summary>Combines the items of two lists repeated to the least common multiple of their lengths.</summary>
    private static string? CombineRepeatedItems(CssInterpolationContext context, string property, CssNumericTraits traits, List<List<CssComponentValue>> itemsA, List<List<CssComponentValue>> itemsB, CssCombineOperation operation, double progress)
    {
        // A single keyword (e.g. none) is not a list
        if (itemsA.Count == 0 || itemsB.Count == 0)
            return null;

        var count = LeastCommonMultiple(itemsA.Count, itemsB.Count);
        if (count > 1024)
            return null;

        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (CombineLists(context, property, traits, itemsA[i % itemsA.Count], itemsB[i % itemsB.Count], operation, progress) is not { } item)
                return null;

            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(item);
        }

        return builder.ToString();
    }

    /// <summary>
    /// https://drafts.csswg.org/css-gaps-1/#interpolate-with-auto-repeaters: the lists with an auto repeater are combined when both
    /// have one and the same numbers of values before and after it (the integer repeaters expanded): the leading and trailing
    /// values are combined one by one, the values of the auto repeaters as repeatable lists. Returns null otherwise (the
    /// interpolation is discrete).
    /// </summary>
    private static string? CombineAutoRepeaterLists(CssInterpolationContext context, string property, CssNumericTraits traits, List<CssComponentValue> a, List<CssComponentValue> b, CssCombineOperation operation, double progress)
    {
        if (SplitAtAutoRepeater(a) is not var (leadingA, autoA, trailingA) || SplitAtAutoRepeater(b) is not var (leadingB, autoB, trailingB))
            return null;

        if (leadingA.Count != leadingB.Count || trailingA.Count != trailingB.Count)
            return null;

        var builder = new StringBuilder();
        if (!Append(leadingA, leadingB))
            return null;

        if (CombineRepeatedItems(context, property, traits, autoA, autoB, operation, progress) is not { } auto)
            return null;

        if (builder.Length > 0)
        {
            builder.Append(", ");
        }

        builder.Append("repeat(auto, ").Append(auto).Append(')');
        return Append(trailingA, trailingB) ? builder.ToString() : null;

        bool Append(List<List<CssComponentValue>> itemsA, List<List<CssComponentValue>> itemsB)
        {
            for (var i = 0; i < itemsA.Count; i++)
            {
                if (CombineLists(context, property, traits, itemsA[i], itemsB[i], operation, progress) is not { } item)
                    return false;

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(item);
            }

            return true;
        }
    }

    /// <summary>Splits a list of gap decoration values into the values before its auto repeater (the integer repeaters expanded), the values of the auto repeater and the values after it.</summary>
    private static (List<List<CssComponentValue>> Leading, List<List<CssComponentValue>> Auto, List<List<CssComponentValue>> Trailing)? SplitAtAutoRepeater(List<CssComponentValue> values)
    {
        var leading = new List<List<CssComponentValue>>();
        var trailing = new List<List<CssComponentValue>>();
        List<List<CssComponentValue>>? auto = null;
        foreach (var item in ValueMatcher.SplitAtCommas(ExpandIntegerRepeaters(values)))
        {
            if (item is [var single] && IsRepeat(single, out var function))
            {
                var arguments = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
                if (auto is not null || arguments.Count < 2)
                    return null;

                auto = arguments.GetRange(1, arguments.Count - 1);
            }
            else
            {
                (auto is null ? leading : trailing).Add(item);
            }
        }

        return auto is null ? null : (leading, auto, trailing);
    }

    private static bool IsRepeat(CssComponentValue value, out CssFunction function)
    {
        if (value is CssFunction candidate && InfraAscii.EqualsIgnoreAsciiCase(candidate.Name, "repeat"))
        {
            function = candidate;
            return true;
        }

        function = null!;
        return false;
    }

    /// <summary>Returns whether a list of gap decoration values has an auto repeater (https://drafts.csswg.org/css-gaps-1/#auto-repeater).</summary>
    private static bool HasAutoRepeater(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            if (IsRepeat(value, out var function) && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.Ident } count, ..] && InfraAscii.EqualsIgnoreAsciiCase(count.Token.Value, "auto"))
                return true;
        }

        return false;
    }

    /// <summary>Replaces the integer repeaters of a list of gap decoration values (https://drafts.csswg.org/css-gaps-1/#integer-repeater) with their values, repeated.</summary>
    private static List<CssComponentValue> ExpandIntegerRepeaters(List<CssComponentValue> values)
    {
        if (!values.Exists(static value => IsRepeat(value, out _)))
            return values;

        const int MaxValues = 1024;
        var result = new List<CssComponentValue>();
        foreach (var item in ValueMatcher.SplitAtCommas(values))
        {
            if (item is [var single] && IsRepeat(single, out var function)
                && ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value)) is { Count: >= 2 } arguments
                && arguments[0] is [CssPreservedToken { Type: CssTokenType.Number } count])
            {
                for (var repetition = 0; repetition < (int)count.Token.NumericValue && result.Count < MaxValues; repetition++)
                {
                    for (var i = 1; i < arguments.Count; i++)
                    {
                        AddItem(arguments[i]);
                    }
                }
            }
            else
            {
                AddItem(item);
            }
        }

        return result;

        void AddItem(List<CssComponentValue> item)
        {
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma, Value = "," }));
            }

            result.AddRange(item);
        }
    }

    /// <summary>
    /// Combines two track lists (https://drafts.csswg.org/css-grid-2/#track-sizing): when they have the same number of tracks,
    /// the tracks are combined one by one (a track that cannot be combined is interpolated discretely, or is replaced by the
    /// value for the addition) and the line names are discrete. Returns null when the numbers of tracks are different, or
    /// for a keyword (e.g. none or subgrid).
    /// </summary>
    public static string? CombineItems(CssInterpolationContext context, string property, CssNumericTraits traits, List<CssComponentValue> a, List<CssComponentValue> b, CssCombineOperation operation, double progress)
    {
        if (IsTrackListKeyword(a) || IsTrackListKeyword(b))
            return null;

        var tracksA = GetTracks(a, out var namesA);
        var tracksB = GetTracks(b, out var namesB);
        if (tracksA.Count != tracksB.Count)
            return null;

        var useFirst = operation == CssCombineOperation.Interpolate && progress < 0.5;
        var names = useFirst ? namesA : namesB;
        var builder = new StringBuilder();
        for (var i = 0; i <= tracksA.Count; i++)
        {
            if (names[i] is { } lineNames)
            {
                AppendSeparated(builder, lineNames);
            }

            if (i == tracksA.Count)
                break;

            // https://drafts.csswg.org/css-grid-2/#repeat-interpolation: "If the two repeat() notations have the same first
            // argument (repetition count) and the same number of tracks in their second argument (the track listing), they
            // are combined by combining each component of their computed track lists by computed value [...] They otherwise
            // combine discretely" (the whole track list; as in the browsers, the automatic repetitions are discrete)
            var repeatA = tracksA[i] is CssFunction functionA && InfraAscii.EqualsIgnoreAsciiCase(functionA.Name, "repeat") ? functionA : null;
            var repeatB = tracksB[i] is CssFunction functionB && InfraAscii.EqualsIgnoreAsciiCase(functionB.Name, "repeat") ? functionB : null;
            string? track;
            if (repeatA is not null || repeatB is not null)
            {
                if (repeatA is null || repeatB is null || !TrySplitAtComma(repeatA.Value, out var countA, out var listA) || !TrySplitAtComma(repeatB.Value, out var countB, out var listB)
                    || countA is not [CssPreservedToken { Type: CssTokenType.Number }]
                    || !string.Equals(CssSerializer.SerializeValue(countA, property: null), CssSerializer.SerializeValue(countB, property: null), StringComparison.OrdinalIgnoreCase)
                    || CombineItems(context, property, traits, listA, listB, operation, progress) is not { } repeated)
                {
                    return null;
                }

                track = "repeat(" + CssSerializer.SerializeValue(countA, property: null) + ", " + repeated + ")";
            }
            else if (tracksA[i] is CssFunction minmaxA && tracksB[i] is CssFunction minmaxB && InfraAscii.EqualsIgnoreAsciiCase(minmaxA.Name, "minmax") && InfraAscii.EqualsIgnoreAsciiCase(minmaxB.Name, "minmax")
                && TrySplitAtComma(minmaxA.Value, out var minA, out var maxA) && TrySplitAtComma(minmaxB.Value, out var minB, out var maxB))
            {
                // The arguments of minmax() are combined one by one (https://drafts.csswg.org/css-grid-2/#track-sizes: by
                // computed value type per item)
                track = "minmax(" + CombineArgument(minA, minB) + ", " + CombineArgument(maxA, maxB) + ")";
            }
            else
            {
                track = CombineLists(context, property, traits, [tracksA[i]], [tracksB[i]], operation, progress)
                    ?? CssSerializer.SerializeValue([useFirst ? tracksA[i] : tracksB[i]], property: null);
            }

            AppendSeparated(builder, track);
        }

        return builder.ToString();

        string CombineArgument(List<CssComponentValue> x, List<CssComponentValue> y)
            => CombineLists(context, property, traits, x, y, operation, progress) ?? CssSerializer.SerializeValue(useFirst ? x : y, property: null);

        // Splits the arguments of a function at its first comma (the white space is not significant)
        static bool TrySplitAtComma(List<CssComponentValue> value, out List<CssComponentValue> first, out List<CssComponentValue> rest)
        {
            first = [];
            rest = [];
            var found = false;
            foreach (var component in value)
            {
                if (component is CssPreservedToken { Type: CssTokenType.Whitespace })
                    continue;

                if (!found && component is CssPreservedToken { Type: CssTokenType.Comma })
                {
                    found = true;
                    continue;
                }

                (found ? rest : first).Add(component);
            }

            return found && first.Count > 0 && rest.Count > 0;
        }

        static void AppendSeparated(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(value);
        }
    }

    private static bool IsTrackListKeyword(List<CssComponentValue> value) => value is [CssPreservedToken { Type: CssTokenType.Ident }]
        || (value is [CssPreservedToken { Type: CssTokenType.Ident } ident, ..] && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "subgrid"));

    /// <summary>Returns the tracks of a track list, and the line names before each track and after the last one (null when there is none).</summary>
    private static List<CssComponentValue> GetTracks(List<CssComponentValue> value, out List<string?> names)
    {
        var tracks = new List<CssComponentValue>();
        names = [null];
        foreach (var component in value)
        {
            if (component is CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket })
            {
                var text = CssSerializer.SerializeValue([component], property: null);
                names[^1] = names[^1] is { } previous ? previous + " " + text : text;
                continue;
            }

            tracks.Add(component);
            names.Add(null);
        }

        return tracks;
    }

    /// <summary>Combines two lists of significant component values; returns null when their structure is different.</summary>
    public static string? CombineLists(CssInterpolationContext context, string property, CssNumericTraits traits, List<CssComponentValue> a, List<CssComponentValue> b, CssCombineOperation operation, double progress)
    {
        var builder = new StringBuilder();
        return AppendList(builder, context, property, traits, a, b, operation, progress, topLevel: true) ? builder.ToString() : null;
    }

    private static bool AppendList(StringBuilder builder, CssInterpolationContext context, string property, CssNumericTraits traits, List<CssComponentValue> a, List<CssComponentValue> b, CssCombineOperation operation, double progress, bool topLevel)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] is CssPreservedToken { Type: CssTokenType.Comma })
            {
                if (b[i] is not CssPreservedToken { Type: CssTokenType.Comma })
                    return false;

                builder.Append(',');
                continue;
            }

            if (i > 0)
            {
                builder.Append(' ');
            }

            if (!AppendComponent(builder, context, property, traits, a[i], b[i], operation, progress, topLevel))
                return false;
        }

        return true;
    }

    private static bool AppendComponent(StringBuilder builder, CssInterpolationContext context, string property, CssNumericTraits traits, CssComponentValue a, CssComponentValue b, CssCombineOperation operation, double progress, bool topLevel)
    {
        // Numbers, percentages, dimensions and math functions
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (CombineNumeric(a, b, traits, operation, progress, topLevel) is not { } numeric)
                return false;

            builder.Append(numeric);
            return true;
        }

        // Colors
        if (IsColor(a) && IsColor(b))
        {
            if (CssColorInterpolation.Combine(context, property, a, b, operation, progress) is not { } color)
                return false;

            builder.Append(color);
            return true;
        }

        switch (a)
        {
            case CssPreservedToken { Type: CssTokenType.Ident } identA:
                if (b is not CssPreservedToken { Type: CssTokenType.Ident } identB || !InfraAscii.EqualsIgnoreAsciiCase(identA.Token.Value, identB.Token.Value))
                    return false;

                builder.Append(CssSerializer.SerializeIdentifier(identA.Token.Value));
                return true;

            case CssFunction functionA:
                if (b is not CssFunction functionB || !InfraAscii.EqualsIgnoreAsciiCase(functionA.Name, functionB.Name))
                    return false;

                // The number of repetitions of the tracks of repeat() must be the same, and only the tracks are combined
                // (https://drafts.csswg.org/css-grid-2/#repeat-interpolation)
                if (InfraAscii.EqualsIgnoreAsciiCase(functionA.Name, "repeat"))
                {
                    var argumentsA = ValueMatcher.RemoveWhitespace(functionA.Value);
                    var argumentsB = ValueMatcher.RemoveWhitespace(functionB.Value);
                    var commaA = argumentsA.FindIndex(c => c is CssPreservedToken { Type: CssTokenType.Comma });
                    var commaB = argumentsB.FindIndex(c => c is CssPreservedToken { Type: CssTokenType.Comma });
                    if (commaA < 0 || commaB < 0)
                        return false;

                    var countText = CssSerializer.SerializeValue(argumentsA[..commaA], property: null);
                    if (countText != CssSerializer.SerializeValue(argumentsB[..commaB], property: null))
                        return false;

                    builder.Append("repeat(").Append(countText).Append(", ");
                    if (!AppendList(builder, context, property, CssNumericTraits.LengthPercentage, argumentsA[(commaA + 1)..], argumentsB[(commaB + 1)..], operation, progress, topLevel: false))
                        return false;

                    builder.Append(')');
                    return true;
                }

                builder.Append(CssSerializer.SerializeIdentifier(InfraAscii.ToAsciiLowercase(functionA.Name))).Append('(');
                // The arguments of the functions may combine lengths and percentages (e.g. circle(50px at 10% 20px))
                if (!AppendList(builder, context, property, CssNumericTraits.LengthPercentage, ValueMatcher.RemoveWhitespace(functionA.Value), ValueMatcher.RemoveWhitespace(functionB.Value), operation, progress, topLevel: false))
                    return false;

                builder.Append(')');
                return true;

            case CssSimpleBlock blockA:
                if (b is not CssSimpleBlock blockB || blockA.AssociatedToken != blockB.AssociatedToken)
                    return false;

                var (open, close) = blockA.AssociatedToken switch
                {
                    CssTokenType.LeftSquareBracket => ('[', ']'),
                    CssTokenType.LeftCurlyBracket => ('{', '}'),
                    _ => ('(', ')'),
                };

                builder.Append(open);
                if (!AppendList(builder, context, property, CssNumericTraits.Empty, ValueMatcher.RemoveWhitespace(blockA.Value), ValueMatcher.RemoveWhitespace(blockB.Value), operation, progress, topLevel: false))
                    return false;

                builder.Append(close);
                return true;

            default:
                var textA = CssSerializer.SerializeValue([a], property: null);
                var textB = CssSerializer.SerializeValue([b], property: null);
                if (!string.Equals(textA, textB, StringComparison.Ordinal))
                    return false;

                builder.Append(textA);
                return true;
        }
    }

    /// <summary>Returns whether a component is a number, a percentage, a dimension, a math function or an anchor function.</summary>
    public static bool IsNumeric(CssComponentValue value) => value switch
    {
        CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } => true,
        CssFunction function => CssCalc.IsMathFunction(function.Name) || IsAnchorFunction(function),
        _ => false,
    };

    /// <summary>
    /// Whether a component is an anchor function (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos, a &lt;length&gt;): the
    /// computed values keep them (they are resolved by the layout), so a value with an anchor function is combined with another
    /// length in a calc(), which the layout resolves.
    /// </summary>
    private static bool IsAnchorFunction(CssFunction function) => InfraAscii.EqualsIgnoreAsciiCase(function.Name, "anchor") || InfraAscii.EqualsIgnoreAsciiCase(function.Name, "anchor-size");

    /// <summary>Returns whether a component is a color (including currentcolor).</summary>
    public static bool IsColor(CssComponentValue value) => value switch
    {
        CssPreservedToken { Type: CssTokenType.Ident } ident => InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "currentcolor") || CssColor.Parse(value) is not null,
        CssPreservedToken { Type: CssTokenType.Hash } => CssColor.Parse(value) is not null,
        CssFunction function => CssColor.Parse(value) is not null || IsColorDependingOnCurrentColor(function),
        _ => false,
    };

    /// <summary>
    /// Whether a function is a color that depends on currentcolor (color-mix() or a relative color whose computed value keeps
    /// currentcolor, https://drafts.csswg.org/css-color-5/#resolving-mix): it is resolved when it is combined (see CssColorInterpolation).
    /// </summary>
    private static bool IsColorDependingOnCurrentColor(CssFunction function)
        => InfraAscii.ToAsciiLowercase(function.Name) is "color-mix" or "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color"
            && CssSerializer.SerializeValue([function], property: null).Contains("currentcolor", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Combines two numeric components (https://drafts.csswg.org/css-values-4/#combine-numbers). Values of the same unit are
    /// combined directly; values of different units of the same type (e.g. a length and a percentage) are combined in a calc().
    /// </summary>
    public static string? CombineNumeric(CssComponentValue a, CssComponentValue b, CssNumericTraits traits, CssCombineOperation operation, double progress, bool topLevel)
    {
        if (TryGetNumber(a, out var valueA, out var unitA) && TryGetNumber(b, out var valueB, out var unitB) && unitA == unitB)
        {
            var value = operation == CssCombineOperation.Interpolate ? Lerp(valueA, valueB, progress) : valueA + valueB;
            if (topLevel)
            {
                if (traits.IsInteger(unitA) && unitA.Length == 0)
                {
                    value = Math.Floor(value + 0.5);
                }

                value = traits.Clamp(unitA, value);
            }

            return FormatNumber(value) + unitA;
        }

        // A dimension and a percentage (or a calc() of both): the percentage term is kept when one of the values has a
        // percentage, and the dimension term too, even when it is zero (e.g. 0% to 100px at 0.25 is calc(0% + 25px), 100% to 0px
        // at 0.3 is calc(70% + 0px), as in the browsers)
        if (traits.PercentageUnit is { } percentageUnit && TryGetDimensionPercentage(a, percentageUnit, out var pxA, out var pctA, out var hasPctA)
            && TryGetDimensionPercentage(b, percentageUnit, out var pxB, out var pctB, out var hasPctB))
        {
            var px = operation == CssCombineOperation.Interpolate ? Lerp(pxA, pxB, progress) : pxA + pxB;
            var pct = operation == CssCombineOperation.Interpolate ? Lerp(pctA, pctB, progress) : pctA + pctB;
            if (!hasPctA && !hasPctB)
                return FormatNumber(topLevel ? traits.Clamp(percentageUnit, px) : px) + percentageUnit;

            return "calc(" + FormatNumber(pct) + "% " + (px < 0 ? "- " : "+ ") + FormatNumber(Math.Abs(px)) + percentageUnit + ")";
        }

        // Values of different units: they must have the same type (a percentage has the type of the percentages of the property)
        var typeA = GetType(a, traits.PercentageUnit);
        var typeB = GetType(b, traits.PercentageUnit);
        if (typeA is null || typeA != typeB)
            return null;

        var textA = Serialize(a);
        var textB = Serialize(b);
        return operation == CssCombineOperation.Interpolate
            ? "calc(" + textA + " * " + FormatNumber(1 - progress) + " + " + textB + " * " + FormatNumber(progress) + ")"
            : "calc(" + textA + " + " + textB + ")";
    }

    /// <summary>
    /// Returns the dimension (in the canonical unit of the percentages of the property) and the percentage of a value that is a
    /// dimension, a percentage, or a calc() of a percentage and a dimension (the computed form, e.g. calc(10% - 5px)).
    /// </summary>
    private static bool TryGetDimensionPercentage(CssComponentValue component, string unit, out double dimension, out double percentage, out bool hasPercentage)
    {
        dimension = 0;
        percentage = 0;
        hasPercentage = false;
        if (TryGetNumber(component, out var value, out var tokenUnit))
        {
            if (tokenUnit == "%")
            {
                percentage = value;
                hasPercentage = true;
                return true;
            }

            dimension = value;
            return tokenUnit == unit;
        }

        if (component is not CssFunction { Name: var name } function || !InfraAscii.EqualsIgnoreAsciiCase(name, "calc"))
            return false;

        var terms = ValueMatcher.RemoveWhitespace(function.Value);
        if (terms is not [var first, CssPreservedToken { Type: CssTokenType.Delim } sign, var second] || sign.Token.Value is not ("+" or "-"))
            return false;

        if (!TryGetNumber(first, out var firstValue, out var firstUnit) || !TryGetNumber(second, out var secondValue, out var secondUnit))
            return false;

        if (sign.Token.Value == "-")
        {
            secondValue = -secondValue;
        }

        if (firstUnit == "%" && secondUnit == unit)
        {
            percentage = firstValue;
            dimension = secondValue;
        }
        else if (firstUnit == unit && secondUnit == "%")
        {
            dimension = firstValue;
            percentage = secondValue;
        }
        else
        {
            return false;
        }

        hasPercentage = true;
        return true;
    }

    /// <summary>Returns the value and the canonical unit (lowercase, "" for a number, "%" for a percentage) of a numeric token.</summary>
    public static bool TryGetNumber(CssComponentValue component, out double value, out string unit)
    {
        switch (component)
        {
            case CssPreservedToken { Type: CssTokenType.Number } number:
                value = number.Token.NumericValue;
                unit = "";
                return true;
            case CssPreservedToken { Type: CssTokenType.Percentage } percentage:
                value = percentage.Token.NumericValue;
                unit = "%";
                return true;
            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                value = dimension.Token.NumericValue;
                unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
                return true;
            default:
                value = 0;
                unit = "";
                return false;
        }
    }

    /// <summary>Returns the type of a numeric component: its canonical unit, the unit of the percentages, or the type of a math function.</summary>
    private static string? GetType(CssComponentValue component, string? percentageUnit)
    {
        if (TryGetNumber(component, out _, out var unit))
            return unit == "%" ? percentageUnit ?? "%" : unit;

        if (component is CssFunction anchorFunction && IsAnchorFunction(anchorFunction))
            return "px";

        if (component is CssFunction function && CssCalc.Evaluate(function, new CssCalcContext { PercentageUnit = percentageUnit }) is { } result)
            return result.IsNumeric && result.Unit == "%" ? percentageUnit ?? "%" : result.TypeUnit;

        return null;
    }

    private static string Serialize(CssComponentValue component)
    {
        if (TryGetNumber(component, out var value, out var unit))
            return FormatNumber(value) + unit;

        return CssSerializer.SerializeValue([component], property: null);
    }

    public static double Lerp(double a, double b, double progress) => (a * (1 - progress)) + (b * progress);

    /// <summary>Formats a number with enough precision to be computed again (the computed value rounds it).</summary>
    public static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            return double.IsNaN(value) ? "0" : value > 0 ? "3.40282e38" : "-3.40282e38";

        var rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "0";

        return rounded.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int LeastCommonMultiple(int a, int b)
    {
        static int GreatestCommonDivisor(int x, int y)
        {
            while (y != 0)
            {
                (x, y) = (y, x % y);
            }

            return x;
        }

        return a / GreatestCommonDivisor(a, b) * b;
    }
}
