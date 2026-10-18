using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// The properties of the scroll-driven animations (https://drafts.csswg.org/scroll-animations-1/): the scroll-timeline and
// view-timeline shorthands, and the canonical values of animation-timeline and view-timeline-inset
internal static partial class CssPropertyRules
{
    private static bool IsAxisKeyword(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.ToAsciiLowercase(ident.Token.Value) is "block" or "inline" or "x" or "y";

    private static bool IsAutoKeyword(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("auto");

    /// <summary>
    /// The canonical value of a &lt;'view-timeline-inset'&gt; item: the second value is omitted when it is the first one
    /// (https://drafts.csswg.org/scroll-animations-1/#view-timeline-inset), so auto auto is auto.
    /// </summary>
    private static string CanonicalizeInset(List<CssComponentValue> values)
    {
        var start = SerializeComponent(values[0], property: null);
        if (values.Count == 1)
            return IsAutoKeyword(values[0]) ? "auto" : start;

        var end = SerializeComponent(values[1], property: null);
        if (IsAutoKeyword(values[0]))
            start = "auto";

        if (IsAutoKeyword(values[1]))
            end = "auto";

        return start == end ? start : start + " " + end;
    }

    /// <summary>The canonical value of view-timeline-inset: each item is canonicalized (see <see cref="CanonicalizeInset"/>).</summary>
    private static string? CanonicalizeViewTimelineInset(List<CssComponentValue> significant)
    {
        var items = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            if (item.Count is 0 or > 2)
                return null;

            items.Add(CanonicalizeInset(item));
        }

        return string.Join(", ", items);
    }

    /// <summary>
    /// The canonical value of animation-timeline (https://drafts.csswg.org/css-animations-2/#animation-timeline): the default
    /// arguments of scroll() (nearest, block) and view() (block, auto) are omitted, the scroller precedes the axis in scroll()
    /// and the axis precedes the inset in view().
    /// </summary>
    private static string? CanonicalizeAnimationTimeline(List<CssComponentValue> significant)
    {
        var items = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            if (item is not [var value])
                return null;

            if (value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "scroll"))
            {
                string? scroller = null;
                string? axis = null;
                foreach (var argument in ValueMatcher.RemoveWhitespace(function.Value))
                {
                    if (argument is not CssPreservedToken { Type: CssTokenType.Ident } keyword)
                        return null;

                    var lower = InfraAscii.ToAsciiLowercase(keyword.Token.Value);
                    if (IsAxisKeyword(argument))
                    {
                        axis = lower;
                    }
                    else
                    {
                        scroller = lower;
                    }
                }

                var parts = new List<string>(2);
                if (scroller is not null and not "nearest")
                {
                    parts.Add(scroller);
                }

                if (axis is not null and not "block")
                {
                    parts.Add(axis);
                }

                items.Add("scroll(" + string.Join(' ', parts) + ")");
            }
            else if (value is CssFunction viewFunction && InfraAscii.EqualsIgnoreAsciiCase(viewFunction.Name, "view"))
            {
                string? axis = null;
                var insets = new List<CssComponentValue>(2);
                foreach (var argument in ValueMatcher.RemoveWhitespace(viewFunction.Value))
                {
                    if (IsAxisKeyword(argument))
                    {
                        axis = InfraAscii.ToAsciiLowercase(((CssPreservedToken)argument).Token.Value);
                    }
                    else
                    {
                        insets.Add(argument);
                    }
                }

                var parts = new List<string>(2);
                if (axis is not null and not "block")
                {
                    parts.Add(axis);
                }

                if (insets.Count > 0 && CanonicalizeInset(insets) is var inset && inset != "auto")
                {
                    parts.Add(inset);
                }

                items.Add("view(" + string.Join(' ', parts) + ")");
            }
            else
            {
                items.Add(value is CssPreservedToken { Type: CssTokenType.Ident } ident && !ident.Token.Value.StartsWith("--", StringComparison.Ordinal) ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : SerializeComponent(value, property: null));
            }
        }

        return string.Join(", ", items);
    }

    /// <summary>
    /// https://drafts.csswg.org/scroll-animations-1/#scroll-timeline-shorthand: each item is a name followed by an optional axis.
    /// </summary>
    private static Dictionary<string, string>? ExpandScrollTimeline(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var names = new List<string>();
        var axes = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            if (item.Count is 0 or > 2 || (item.Count == 2 && !IsAxisKeyword(item[1])))
                return null;

            names.Add(SerializeComponent(item[0], property: null));
            axes.Add(item.Count == 2 ? InfraAscii.ToAsciiLowercase(SerializeComponent(item[1], property: null)) : "block");
        }

        if (ParseListLonghand("scroll-timeline-name", names) is not { } nameValue || ParseListLonghand("scroll-timeline-axis", axes) is not { } axisValue)
            return null;

        return new(StringComparer.Ordinal)
        {
            ["scroll-timeline-name"] = nameValue,
            ["scroll-timeline-axis"] = axisValue,
        };
    }

    /// <summary>The axis of each item is omitted when it is block; the longhands must have the same number of items.</summary>
    private static string? SerializeScrollTimeline(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("scroll-timeline-name", out var nameValue) || !lookup.TryGetValue("scroll-timeline-axis", out var axisValue))
            return null;

        var names = SplitItems(nameValue);
        var axes = SplitItems(axisValue);
        if (names.Count != axes.Count)
            return "";

        var items = new List<string>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            items.Add(axes[i] == "block" ? names[i] : names[i] + " " + axes[i]);
        }

        return string.Join(", ", items);
    }

    /// <summary>
    /// https://drafts.csswg.org/scroll-animations-1/#view-timeline-shorthand: each item is a name followed by an optional axis
    /// and an optional inset, in any order.
    /// </summary>
    private static Dictionary<string, string>? ExpandViewTimeline(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var names = new List<string>();
        var axes = new List<string>();
        var insets = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            if (item.Count == 0)
                return null;

            string? axis = null;
            var inset = new List<CssComponentValue>(2);
            var insetDone = false;
            for (var i = 1; i < item.Count; i++)
            {
                if (IsAxisKeyword(item[i]))
                {
                    if (axis is not null)
                        return null;

                    axis = InfraAscii.ToAsciiLowercase(SerializeComponent(item[i], property: null));
                    insetDone = inset.Count > 0;
                }
                else
                {
                    if (insetDone || inset.Count == 2)
                        return null;

                    inset.Add(item[i]);
                }
            }

            names.Add(SerializeComponent(item[0], property: null));
            axes.Add(axis ?? "block");
            insets.Add(inset.Count == 0 ? "auto" : string.Join(' ', inset.Select(value => SerializeComponent(value, property: null))));
        }

        if (ParseListLonghand("view-timeline-name", names) is not { } nameValue
            || ParseListLonghand("view-timeline-axis", axes) is not { } axisValue
            || ParseListLonghand("view-timeline-inset", insets) is not { } insetValue)
        {
            return null;
        }

        return new(StringComparer.Ordinal)
        {
            ["view-timeline-name"] = nameValue,
            ["view-timeline-axis"] = axisValue,
            ["view-timeline-inset"] = insetValue,
        };
    }

    /// <summary>The axis (block) and the inset (auto) of each item are omitted when they are the initial ones; the longhands must have the same number of items.</summary>
    private static string? SerializeViewTimeline(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("view-timeline-name", out var nameValue) || !lookup.TryGetValue("view-timeline-axis", out var axisValue) || !lookup.TryGetValue("view-timeline-inset", out var insetValue))
            return null;

        var names = SplitItems(nameValue);
        var axes = SplitItems(axisValue);
        var insets = SplitItems(insetValue);
        if (names.Count != axes.Count || names.Count != insets.Count)
            return "";

        var items = new List<string>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var item = names[i];
            if (axes[i] != "block")
            {
                item += " " + axes[i];
            }

            if (insets[i] != "auto")
            {
                item += " " + insets[i];
            }

            items.Add(item);
        }

        return string.Join(", ", items);
    }
}
