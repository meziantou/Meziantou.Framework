using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Animations and transitions (https://drafts.csswg.org/css-animations/, https://drafts.csswg.org/css-transitions/,
// https://drafts.csswg.org/scroll-animations-1/#animation-range)
internal static partial class CssPropertyRules
{
    /// <summary>The longhands set by the components of a &lt;single-animation&gt;, in their canonical order.</summary>
    private static readonly string[] AnimationLonghands = ["animation-duration", "animation-timing-function", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state", "animation-name"];

    /// <summary>The longhands set by the components of a &lt;single-transition&gt;, in their canonical order.</summary>
    private static readonly string[] TransitionLonghands = ["transition-property", "transition-duration", "transition-timing-function", "transition-delay", "transition-behavior"];

    /// <summary>The grammar of the components of a &lt;single-animation&gt; (<see cref="AnimationLonghands"/>).</summary>
    private static readonly GrammarNode[] AnimationComponents =
    [
        ValueGrammarParser.Parse("auto | <time [0s,\u221E]>"),
        ValueGrammarParser.Parse("<easing-function>"),
        ValueGrammarParser.Parse("<time>"),
        ValueGrammarParser.Parse("<single-animation-iteration-count>"),
        ValueGrammarParser.Parse("<single-animation-direction>"),
        ValueGrammarParser.Parse("<single-animation-fill-mode>"),
        ValueGrammarParser.Parse("<single-animation-play-state>"),
        ValueGrammarParser.Parse("none | <keyframes-name>"),
    ];

    /// <summary>The grammar of the components of a &lt;single-transition&gt; (<see cref="TransitionLonghands"/>).</summary>
    private static readonly GrammarNode[] TransitionComponents =
    [
        ValueGrammarParser.Parse("none | <single-transition-property>"),
        ValueGrammarParser.Parse("<time [0s,\u221E]>"),
        ValueGrammarParser.Parse("<easing-function>"),
        ValueGrammarParser.Parse("<time>"),
        ValueGrammarParser.Parse("<transition-behavior-value>"),
    ];

    /// <summary>
    /// The order in which the components of a &lt;single-transition&gt; are tried: the keywords of the easing functions and
    /// of transition-behavior are not property names (e.g. normal opacity is transition-behavior: normal), and the first
    /// time is the duration.
    /// </summary>
    private static readonly int[] TransitionComponentOrder = [1, 2, 3, 4, 0];

    /// <summary>The keywords of the animation shorthand: an animation name that is one of them is serialized as a string.</summary>
    private static readonly HashSet<string> AnimationKeywords = new(StringComparer.Ordinal)
    {
        "auto", "linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end", "infinite", "normal", "reverse", "alternate", "alternate-reverse",
        "none", "forwards", "backwards", "both", "running", "paused",
    };

    /// <summary>The keywords of the transition shorthand that are not a property name.</summary>
    private static readonly HashSet<string> TransitionKeywords = new(StringComparer.Ordinal)
    {
        "linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end", "normal", "allow-discrete",
    };

    private static bool IsTimelineRangeName(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && IsTimelineRangeName(InfraAscii.ToAsciiLowercase(ident.Token.Value));

    private static bool IsTimelineRangeName(string value) => value is "cover" or "contain" or "entry" or "exit" or "entry-crossing" or "exit-crossing" or "scroll";

    private static string SerializeComponent(CssComponentValue value, CssPropertyDefinition? property) => CssSerializer.SerializeValue([value], property);

    // ----------------------------------------------------------------------------------------------------------------
    // Validation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-easing-2/#step-easing-functions: steps() with jump-none needs at least 2 steps, and a
    /// number of steps given by a math function cannot be NaN (as in the browsers); the &lt;keyframes-name&gt; of an
    /// animation cannot be the empty string.
    /// </summary>
    private static bool IsValidAnimationValue(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        foreach (var value in significant)
        {
            switch (value)
            {
                case CssFunction function when function.Name.Equals("steps", StringComparison.OrdinalIgnoreCase):
                    var arguments = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
                    if (arguments is [[CssPreservedToken { Type: CssTokenType.Number } count], [var position]]
                        && IsIdent(position, "jump-none") && count.Token.NumericValue < 2)
                        return false;

                    if (arguments is [[CssFunction math], ..] && CssCalc.Evaluate(math, CssCalcContext.Default) is { IsResolved: true } result && double.IsNaN(result.Value))
                        return false;

                    break;
                case CssPreservedToken { Type: CssTokenType.String } name when name.Token.Value.Length == 0 && property.Name is "animation" or "animation-name":
                    return false;
            }
        }

        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Longhands
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The specified value of animation-timing-function and transition-timing-function (https://drafts.csswg.org/css-easing-2/#step-easing-functions):
    /// step-start is steps(1, start), step-end is steps(1), and the default position end (jump-end) of steps() is omitted.
    /// </summary>
    private static string CanonicalizeEasing(List<CssComponentValue> significant) => SerializeEasing(significant, computed: false);

    /// <summary>
    /// The computed value of animation-timing-function and transition-timing-function: like the specified value, and the
    /// number of steps is an integer of at least 1 (2 with jump-none). The value is the generic computed value.
    /// </summary>
    /// <summary>
    /// Serializes a single &lt;easing-function&gt; (e.g. the easing of a Web Animations effect,
    /// https://drafts.csswg.org/web-animations-1/#dom-effecttiming-easing) like a specified value; returns null when it is not a
    /// single component.
    /// </summary>
    public static string? SerializeEasingFunction(string value)
    {
        var significant = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        return significant.Count == 1 ? SerializeEasing(significant, computed: false) : null;
    }

    public static string ComputeEasing(string value) => SerializeEasing(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)), computed: true);

    private static string SerializeEasing(List<CssComponentValue> significant, bool computed)
    {
        var items = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            if (item is not [var value])
            {
                items.Add(string.Join(' ', item.Select(component => SerializeComponent(component, property: null))));
                continue;
            }

            if (IsIdent(value, "step-start"))
            {
                items.Add("steps(1, start)");
            }
            else if (IsIdent(value, "step-end"))
            {
                items.Add("steps(1)");
            }
            else if (value is CssFunction function && function.Name.Equals("steps", StringComparison.OrdinalIgnoreCase))
            {
                var arguments = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
                var position = arguments.Count > 1 && arguments[1] is [CssPreservedToken { Type: CssTokenType.Ident } ident] ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;
                var count = string.Join(' ', arguments[0].Select(component => SerializeComponent(component, property: null)));
                if (computed && Cascade.CssValueComputer.TryParseNumber(count, out var number))
                {
                    count = CssSerializer.SerializeNumber(Math.Max(Math.Floor(number + 0.5), position == "jump-none" ? 2 : 1));
                }

                items.Add(position is null or "end" or "jump-end" ? "steps(" + count + ")" : "steps(" + count + ", " + position + ")");
            }
            else if (computed && value is CssFunction cubicBezier && cubicBezier.Name.Equals("cubic-bezier", StringComparison.OrdinalIgnoreCase) && ComputeCubicBezier(cubicBezier) is { } computedCubicBezier)
            {
                items.Add(computedCubicBezier);
            }
            else if (computed && value is CssFunction linear && linear.Name.Equals("linear", StringComparison.OrdinalIgnoreCase) && ComputeLinearEasing(linear) is { } computedLinear)
            {
                items.Add(computedLinear);
            }
            else
            {
                items.Add(value is CssPreservedToken { Type: CssTokenType.Ident } keyword ? InfraAscii.ToAsciiLowercase(keyword.Token.Value) : SerializeComponent(value, property: null));
            }
        }

        return string.Join(", ", items);
    }

    /// <summary>
    /// The computed value of cubic-bezier() (https://drafts.csswg.org/css-easing-2/#cubic-bezier-easing-functions): the x
    /// values, which must be in the range [0, 1], are clamped when they are given by math functions. Returns null when an
    /// argument is not a number.
    /// </summary>
    private static string? ComputeCubicBezier(CssFunction function)
    {
        var arguments = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        if (arguments.Count != 4)
            return null;

        var values = new string[4];
        for (var i = 0; i < values.Length; i++)
        {
            if (arguments[i] is not [CssPreservedToken { Type: CssTokenType.Number } number])
                return null;

            values[i] = CssSerializer.SerializeNumber(i % 2 == 0 ? Math.Clamp(number.Token.NumericValue, 0, 1) : number.Token.NumericValue);
        }

        return "cubic-bezier(" + string.Join(", ", values) + ")";
    }

    /// <summary>
    /// The computed value of linear() (https://drafts.csswg.org/css-easing-2/#linear-easing-function-parsing): a stop with
    /// two input progress values is split into two stops, the missing input progress values of the first and last stops are
    /// 0% and 100%, an input progress value smaller than a previous one is set to the largest previous one, and the missing
    /// input progress values of the other stops are spread evenly between their neighbors. Returns null when an argument is
    /// not a number or a percentage.
    /// </summary>
    private static string? ComputeLinearEasing(CssFunction function)
    {
        var outputs = new List<double>();
        var inputs = new List<double?>();
        foreach (var argument in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value)))
        {
            double? output = null;
            var percentages = new List<double>(2);
            foreach (var component in argument)
            {
                switch (component)
                {
                    case CssPreservedToken { Type: CssTokenType.Number } number when output is null:
                        output = number.Token.NumericValue;
                        break;
                    case CssPreservedToken { Type: CssTokenType.Percentage } percentage when percentages.Count < 2:
                        percentages.Add(percentage.Token.NumericValue);
                        break;
                    default:
                        return null;
                }
            }

            if (output is null)
                return null;

            if (percentages.Count == 0)
            {
                outputs.Add(output.Value);
                inputs.Add(null);
            }

            foreach (var percentage in percentages)
            {
                outputs.Add(output.Value);
                inputs.Add(percentage);
            }
        }

        if (outputs.Count < 2)
            return null;

        inputs[0] ??= 0;
        inputs[^1] ??= 100;

        var largest = double.NegativeInfinity;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] is { } input)
            {
                largest = Math.Max(largest, input);
                inputs[i] = largest;
            }
        }

        for (var i = 1; i < inputs.Count - 1; i++)
        {
            if (inputs[i] is not null)
                continue;

            var end = i;
            while (inputs[end] is null)
            {
                end++;
            }

            var start = inputs[i - 1]!.Value;
            var step = (inputs[end]!.Value - start) / (end - i + 1);
            for (var j = i; j < end; j++)
            {
                inputs[j] = start + (step * (j - i + 1));
            }
        }

        var stops = new string[outputs.Count];
        for (var i = 0; i < stops.Length; i++)
        {
            stops[i] = CssSerializer.SerializeNumber(outputs[i]) + " " + CssSerializer.SerializeNumber(inputs[i]!.Value) + "%";
        }

        return "linear(" + string.Join(", ", stops) + ")";
    }

    /// <summary>
    /// The specified value of animation-name (https://drafts.csswg.org/css-animations/#animation-name): a string is
    /// serialized as an identifier, unless it is none, default or a CSS-wide keyword.
    /// </summary>
    private static string? CanonicalizeAnimationName(List<CssComponentValue> significant)
    {
        var names = new List<string>();
        foreach (var value in significant)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Comma }:
                    continue;
                case CssPreservedToken { Type: CssTokenType.String } name:
                    var lower = InfraAscii.ToAsciiLowercase(name.Token.Value);
                    names.Add(lower is "none" or "default" || ValueMatcher.CssWideKeywords.Contains(lower) ? CssSerializer.SerializeString(name.Token.Value) : CssSerializer.SerializeIdentifier(name.Token.Value));
                    break;
                case CssPreservedToken { Type: CssTokenType.Ident } ident:
                    names.Add(ident.Token.IsIdent("none") ? "none" : CssSerializer.SerializeIdentifier(ident.Token.Value));
                    break;
                default:
                    return null;
            }
        }

        return string.Join(", ", names);
    }

    /// <summary>
    /// The specified value of animation-range-start and animation-range-end (https://drafts.csswg.org/scroll-animations-1/#animation-range-start):
    /// the default offset of a timeline range name (0% for the start, 100% for the end) is omitted.
    /// </summary>
    private static string CanonicalizeAnimationRange(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        var defaultOffset = property.Name.EndsWith("-end", StringComparison.Ordinal) ? 100 : 0;
        var items = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            var parts = new List<string>(2);
            foreach (var value in item)
            {
                if (value is CssPreservedToken { Type: CssTokenType.Ident } ident)
                {
                    parts.Add(InfraAscii.ToAsciiLowercase(ident.Token.Value));
                }
                else if (!(parts.Count == 1 && value is CssPreservedToken { Type: CssTokenType.Percentage } percentage && percentage.Token.NumericValue == defaultOffset))
                {
                    parts.Add(SerializeComponent(value, property));
                }
            }

            items.Add(string.Join(' ', parts));
        }

        return string.Join(", ", items);
    }

    /// <summary>The computed value of animation-range-start and animation-range-end: like the specified value. The value is the generic computed value.</summary>
    public static string ComputeAnimationRange(CssPropertyDefinition property, string value) => CanonicalizeAnimationRange(property, ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)));

    /// <summary>Returns the canonical value of a longhand set to the comma-separated items, or null when it is invalid.</summary>
    private static string? ParseListLonghand(string name, List<string> items)
        => CssShorthands.ParseValue(name, CssParser.ParseListOfComponentValues(string.Join(", ", items)), originalText: null) is [{ ShorthandName: null } longhand] ? longhand.Value : null;

    /// <summary>Splits the serialized value of a longhand into its comma-separated items.</summary>
    private static List<string> SplitItems(string value)
        => ValueMatcher.SplitAtCommas(CssParser.ParseListOfComponentValues(value)).ConvertAll(item => CssSerializer.SerializeValue(item, property: null));

    // ----------------------------------------------------------------------------------------------------------------
    // Shorthands
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Assigns the components of an item of a list shorthand to the first unset component whose grammar matches, in the
    /// order of <paramref name="order"/> (https://drafts.csswg.org/css-animations/#animation-shorthand: the first time is the
    /// duration). The omitted components are set to the initial value of their longhand.
    /// </summary>
    private static List<string>[]? ExpandItems(List<CssComponentValue> significant, string[] longhands, GrammarNode[] components, int[]? order)
    {
        var columns = new List<string>[longhands.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i] = [];
        }

        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            var slots = new CssComponentValue?[longhands.Length];
            foreach (var value in item)
            {
                var assigned = false;
                for (var j = 0; j < slots.Length && !assigned; j++)
                {
                    var index = order is null ? j : order[j];
                    if (slots[index] is null && ValueMatcher.Matches(components[index], [value]))
                    {
                        slots[index] = value;
                        assigned = true;
                    }
                }

                if (!assigned)
                    return null;
            }

            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] is { } slot)
                {
                    columns[i].Add(SerializeComponent(slot, property: null));
                }
                else if (CssShorthands.GetInitialValue(longhands[i]) is { } initial)
                {
                    columns[i].Add(initial);
                }
                else
                {
                    return null;
                }
            }
        }

        return columns;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-animations/#animation-shorthand: each animation sets the longhands of its components,
    /// and the shorthand resets animation-timeline and animation-range to their initial value.
    /// </summary>
    private static Dictionary<string, string>? ExpandAnimation(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (ExpandItems(significant, AnimationLonghands, AnimationComponents, order: null) is not { } columns)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["animation-timeline"] = "auto",
            ["animation-range-start"] = "normal",
            ["animation-range-end"] = "normal",
        };

        for (var i = 0; i < AnimationLonghands.Length; i++)
        {
            if (ParseListLonghand(AnimationLonghands[i], columns[i]) is not { } value)
                return null;

            result[AnimationLonghands[i]] = value;
        }

        return result;
    }

    private static string? SerializeAnimation(CssPropertyDefinition shorthand, Dictionary<string, string> lookup) => SerializeAnimation(lookup, resolved: false);

    /// <summary>
    /// Serializes the resolved value of the animation shorthand from the resolved values of its longhands: the resolved
    /// value of animation-duration: auto is 0s (https://drafts.csswg.org/css-animations-2/#animation-duration), which is
    /// also omitted.
    /// </summary>
    public static string SerializeResolvedAnimation(Func<string, string> getResolvedValue)
    {
        var lookup = CssPropertyRegistry.GetLonghandsRecursive(CssPropertyRegistry.Get("animation")!).ToDictionary(longhand => longhand, getResolvedValue, StringComparer.Ordinal);
        return SerializeAnimation(lookup, resolved: true) ?? "";
    }

    /// <summary>
    /// Serializes the animations in the canonical order of their components (duration, easing function, delay, iteration
    /// count, direction, fill mode, play state, name), without the components that have their initial value; the duration is
    /// kept when there is a delay, and the name when all the components are omitted. The longhands must have the same number
    /// of items, and the reset-only longhands their initial value.
    /// </summary>
    private static string? SerializeAnimation(Dictionary<string, string> lookup, bool resolved)
    {
        if (lookup.GetValueOrDefault("animation-timeline") != "auto" || lookup.GetValueOrDefault("animation-range-start") != "normal" || lookup.GetValueOrDefault("animation-range-end") != "normal")
            return null;

        if (GetColumns(AnimationLonghands, lookup) is not { } columns)
            return null;

        var items = new List<string>();
        for (var i = 0; i < columns[0].Count; i++)
        {
            var duration = columns[0][i];
            var delay = columns[2][i];
            var parts = new List<string>();
            if (!(duration == "auto" || (resolved && duration == "0s")) || delay != "0s")
            {
                parts.Add(duration);
            }

            AddIfNotDefault(parts, columns[1][i], "ease");
            AddIfNotDefault(parts, delay, "0s");
            AddIfNotDefault(parts, columns[3][i], "1");
            AddIfNotDefault(parts, columns[4][i], "normal");
            AddIfNotDefault(parts, columns[5][i], "none");
            AddIfNotDefault(parts, columns[6][i], "running");

            // A name that is a keyword of the shorthand is a string, so that it is not parsed as the keyword
            var name = columns[7][i];
            if (name != "none" || parts.Count == 0)
            {
                parts.Add(name != "none" && AnimationKeywords.Contains(InfraAscii.ToAsciiLowercase(name)) ? CssSerializer.SerializeString(name) : name);
            }

            items.Add(string.Join(' ', parts));
        }

        return string.Join(", ", items);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transitions/#transition-shorthand-property: each transition sets the longhands of its
    /// components (the first time is the duration, the second the delay).
    /// </summary>
    private static Dictionary<string, string>? ExpandTransition(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (ExpandItems(significant, TransitionLonghands, TransitionComponents, TransitionComponentOrder) is not { } columns)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < TransitionLonghands.Length; i++)
        {
            if (ParseListLonghand(TransitionLonghands[i], columns[i]) is not { } value)
                return null;

            result[TransitionLonghands[i]] = value;
        }

        return result;
    }

    /// <summary>
    /// Serializes the transitions in the canonical order of their components (property, duration, easing function, delay,
    /// behavior), without the components that have their initial value; the duration is kept when there is a delay, and
    /// the property (all) when all the components are omitted.
    /// </summary>
    private static string? SerializeTransition(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (GetColumns(TransitionLonghands, lookup) is not { } columns)
            return null;

        var items = new List<string>();
        for (var i = 0; i < columns[0].Count; i++)
        {
            var property = columns[0][i];
            var delay = columns[3][i];
            if (TransitionKeywords.Contains(InfraAscii.ToAsciiLowercase(property)))
                return null;

            var parts = new List<string>();
            AddIfNotDefault(parts, property, "all");
            if (columns[1][i] != "0s" || delay != "0s")
            {
                parts.Add(columns[1][i]);
            }

            AddIfNotDefault(parts, columns[2][i], "ease");
            AddIfNotDefault(parts, delay, "0s");
            AddIfNotDefault(parts, columns[4][i], "normal");
            items.Add(parts.Count == 0 ? "all" : string.Join(' ', parts));
        }

        return string.Join(", ", items);
    }

    private static void AddIfNotDefault(List<string> parts, string value, string defaultValue)
    {
        if (value != defaultValue)
        {
            parts.Add(value);
        }
    }

    /// <summary>Returns the items of the longhands, or null when the longhands do not have the same number of items.</summary>
    private static List<string>[]? GetColumns(string[] longhands, Dictionary<string, string> lookup)
    {
        var columns = new List<string>[longhands.Length];
        for (var i = 0; i < longhands.Length; i++)
        {
            if (!lookup.TryGetValue(longhands[i], out var value))
                return null;

            columns[i] = SplitItems(value);
            if (columns[i].Count != columns[0].Count)
                return null;
        }

        return columns;
    }

    /// <summary>
    /// https://drafts.csswg.org/scroll-animations-1/#animation-range: the omitted end is the timeline range name of the start
    /// (with 100%), or normal. Like the browsers, an end offset of 100% without a timeline range name is normal.
    /// </summary>
    private static Dictionary<string, string>? ExpandAnimationRange(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var starts = new List<string>();
        var ends = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            var startLength = GetRangeLength(item, 0);
            var start = item[..startLength];
            var end = item[startLength..];
            if (end.Count > 0 && GetRangeLength(end, 0) != end.Count)
                return null;

            starts.Add(string.Join(' ', start.Select(value => SerializeComponent(value, property: null))));
            if (end.Count == 0)
            {
                ends.Add(IsTimelineRangeName(start[0]) ? SerializeComponent(start[0], property: null) : "normal");
            }
            else if (end is [CssPreservedToken { Type: CssTokenType.Percentage } percentage] && percentage.Token.NumericValue == 100)
            {
                ends.Add("normal");
            }
            else
            {
                ends.Add(string.Join(' ', end.Select(value => SerializeComponent(value, property: null))));
            }
        }

        if (ParseListLonghand("animation-range-start", starts) is not { } startValue || ParseListLonghand("animation-range-end", ends) is not { } endValue)
            return null;

        return new(StringComparer.Ordinal)
        {
            ["animation-range-start"] = startValue,
            ["animation-range-end"] = endValue,
        };

        // normal, <length-percentage>, or <timeline-range-name> <length-percentage>?
        static int GetRangeLength(List<CssComponentValue> values, int start)
        {
            if (start >= values.Count)
                return 0;

            if (IsTimelineRangeName(values[start]))
                return start + 1 < values.Count && values[start + 1] is not CssPreservedToken { Type: CssTokenType.Ident } ? 2 : 1;

            return 1;
        }
    }

    /// <summary>
    /// The end of each item is omitted when it is the end set by the start alone (its timeline range name, or normal); the
    /// offset of a timeline range name is kept when the end is a single offset.
    /// </summary>
    private static string? SerializeAnimationRange(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("animation-range-start", out var startValue) || !lookup.TryGetValue("animation-range-end", out var endValue))
            return null;

        var starts = SplitItems(startValue);
        var ends = SplitItems(endValue);
        if (starts.Count != ends.Count)
            return null;

        var items = new List<string>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var startComponents = Cascade.CssValueComputer.SplitComponents(start);
            var rangeName = startComponents is [var name, ..] && IsTimelineRangeName(name) ? name : null;
            if (ends[i] == (rangeName ?? "normal"))
            {
                items.Add(start);
                continue;
            }

            // entry 0% 50%: the end offset would be the offset of the start
            if (rangeName is not null && startComponents.Length == 1 && Cascade.CssValueComputer.SplitComponents(ends[i]) is [var offset, ..] && offset != "normal" && !IsTimelineRangeName(offset))
            {
                start += " 0%";
            }

            items.Add(start + " " + ends[i]);
        }

        return string.Join(", ", items);
    }
}
