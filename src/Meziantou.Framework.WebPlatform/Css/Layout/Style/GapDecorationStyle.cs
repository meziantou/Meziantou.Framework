using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>https://drafts.csswg.org/css-gaps-1/#break: column-rule-break and row-rule-break.</summary>
internal enum RuleBreak : byte
{
    Normal,
    None,
    Intersection,
}

/// <summary>https://drafts.csswg.org/css-gaps-1/#visibility: column-rule-visibility-items and row-rule-visibility-items.</summary>
internal enum RuleVisibilityItems : byte
{
    Normal,
    All,
    Around,
    Between,
}

/// <summary>An &lt;inset-value&gt; (https://drafts.csswg.org/css-gaps-1/#typedef-inset-value): a length-percentage or <c>overlap-join</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RuleInset(LengthPercentage Length, bool IsOverlapJoin)
{
    public static RuleInset Parse(string value, double zoom)
    {
        if (value == "overlap-join")
            return new RuleInset(default, IsOverlapJoin: true);

        return new RuleInset(LengthPercentage.TryParse(value, zoom, nonNegative: false, out var length) ? length : default, IsOverlapJoin: false);
    }
}

/// <summary>
/// A list of gap decoration values with <c>repeat()</c> (https://drafts.csswg.org/css-gaps-1/#lists-repeat), with its integer
/// repeaters expanded: the values before the auto repeater, the values of the auto repeater (null when there is none), and the
/// values after it.
/// </summary>
internal sealed class GapValueList<T>
{
    private readonly T[] _leading;
    private readonly T[]? _auto;
    private readonly T[] _trailing;

    private GapValueList(T[] leading, T[]? auto, T[] trailing)
    {
        _leading = leading;
        _auto = auto;
        _trailing = trailing;
    }

    /// <summary>Returns whether a value of the list matches a predicate.</summary>
    public bool Any(Func<T, bool> predicate) => Array.Exists(_leading, v => predicate(v)) || Array.Exists(_trailing, v => predicate(v)) || (_auto is not null && Array.Exists(_auto, v => predicate(v)));

    /// <summary>
    /// https://drafts.csswg.org/css-gaps-1/#assign-gap-decoration-values: returns the value assigned to the gap at
    /// <paramref name="index"/> of a sequence of <paramref name="count"/> gaps. Without auto repeater, the values repeat from
    /// the first one; with an auto repeater, the leading values are assigned to the first gaps, the first trailing values to the
    /// last gaps (that are not leading gaps), and the values of the auto repeater to the other gaps.
    /// </summary>
    public T Get(int index, int count)
    {
        if (_auto is null)
            return _leading[index % _leading.Length];

        if (index < _leading.Length)
            return _leading[index];

        var trailingStart = Math.Max(_leading.Length, count - _trailing.Length);
        if (index >= trailingStart)
            return _trailing[index - trailingStart];

        return _auto[(index - _leading.Length) % _auto.Length];
    }

    /// <summary>Parses the computed value of a list of gap decoration values: the items are parsed by <paramref name="parse"/>.</summary>
    public static GapValueList<T> Parse(string value, Func<string, T> parse, T initial)
    {
        var leading = new List<T>();
        var trailing = new List<T>();
        List<T>? auto = null;
        foreach (var item in Split(value))
        {
            var target = auto is null ? leading : trailing;
            if (item.StartsWith("repeat(", StringComparison.Ordinal) && item[^1] == ')')
            {
                var arguments = Split(item[7..^1]);
                if (arguments.Count < 2)
                    continue;

                if (arguments[0] == "auto")
                {
                    auto = [];
                    for (var i = 1; i < arguments.Count; i++)
                    {
                        auto.Add(parse(arguments[i]));
                    }
                }
                else if (int.TryParse(arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                {
                    // The lists are bounded so that a huge repetition count cannot exhaust the memory: the values repeat anyway
                    for (var repetition = 0; repetition < count && target.Count < MaxExpandedValues; repetition++)
                    {
                        for (var i = 1; i < arguments.Count; i++)
                        {
                            target.Add(parse(arguments[i]));
                        }
                    }
                }
            }
            else
            {
                target.Add(parse(item));
            }
        }

        if (leading.Count == 0 && auto is null)
        {
            leading.Add(initial);
        }

        return new GapValueList<T>([.. leading], auto is { Count: > 0 } ? [.. auto] : null, [.. trailing]);
    }

    private const int MaxExpandedValues = 100_000;

    /// <summary>Splits a comma-separated list, ignoring the commas inside functions (e.g. <c>rgb(0, 0, 0), red</c>).</summary>
    private static List<string> Split(string value)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(':
                    depth++;
                    break;

                case ')':
                    depth--;
                    break;

                case ',' when depth == 0:
                    result.Add(value[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        var last = value[start..].Trim();
        if (last.Length > 0)
        {
            result.Add(last);
        }

        return result;
    }
}

/// <summary>The gap decoration properties of one direction (the column-rule-* or the row-rule-* properties).</summary>
internal sealed class GapRuleStyle
{
    internal GapRuleStyle(ComputedStyle style, string prefix, double zoom)
    {
        Widths = GapValueList<double>.Parse(style.GetComputedValue(prefix + "-width"), value => BoxModelStyle.ParseFixedLength(value, zoom), 3 * zoom);
        Styles = GapValueList<BorderStyle>.Parse(style.GetComputedValue(prefix + "-style"), LayoutKeywords.ParseBorderStyle, BorderStyle.None);
        Break = style.GetComputedValue(prefix + "-break") switch
        {
            "none" => RuleBreak.None,
            "intersection" => RuleBreak.Intersection,
            _ => RuleBreak.Normal,
        };
        VisibilityItems = style.GetComputedValue(prefix + "-visibility-items") switch
        {
            "all" => RuleVisibilityItems.All,
            "around" => RuleVisibilityItems.Around,
            "between" => RuleVisibilityItems.Between,
            _ => RuleVisibilityItems.Normal,
        };
        InsetCapStart = RuleInset.Parse(style.GetComputedValue(prefix + "-inset-cap-start"), zoom);
        InsetCapEnd = RuleInset.Parse(style.GetComputedValue(prefix + "-inset-cap-end"), zoom);
        InsetJunctionStart = RuleInset.Parse(style.GetComputedValue(prefix + "-inset-junction-start"), zoom);
        InsetJunctionEnd = RuleInset.Parse(style.GetComputedValue(prefix + "-inset-junction-end"), zoom);
        IsVisible = Styles.Any(s => s is not (BorderStyle.None or BorderStyle.Hidden)) && Widths.Any(w => w > 0);
        HasOverlapJoin = InsetCapStart.IsOverlapJoin || InsetCapEnd.IsOverlapJoin || InsetJunctionStart.IsOverlapJoin || InsetJunctionEnd.IsOverlapJoin;
    }

    /// <summary>The used widths, in px.</summary>
    public GapValueList<double> Widths { get; }

    public GapValueList<BorderStyle> Styles { get; }

    public RuleBreak Break { get; }

    public RuleVisibilityItems VisibilityItems { get; }

    public RuleInset InsetCapStart { get; }

    public RuleInset InsetCapEnd { get; }

    public RuleInset InsetJunctionStart { get; }

    public RuleInset InsetJunctionEnd { get; }

    /// <summary>Whether a decoration may be painted: a style of the list is not none or hidden, and a width is not zero.</summary>
    public bool IsVisible { get; }

    public bool HasOverlapJoin { get; }
}

/// <summary>The gap decoration properties (https://drafts.csswg.org/css-gaps-1/#gap-decorations), except the colors (resolved by the painting).</summary>
internal sealed class GapDecorationStyle
{
    internal GapDecorationStyle(ComputedStyle style, double zoom)
    {
        Column = new GapRuleStyle(style, "column-rule", zoom);
        Row = new GapRuleStyle(style, "row-rule", zoom);
        ColumnOverRow = style.GetComputedValue("rule-overlap") == "column-over-row";
    }

    public GapRuleStyle Column { get; }

    public GapRuleStyle Row { get; }

    /// <summary>rule-overlap (https://drafts.csswg.org/css-gaps-1/#overlap): whether the column decorations are painted above the row decorations.</summary>
    public bool ColumnOverRow { get; }

    /// <summary>Whether a gap decoration may be painted: the layout computes the geometry of the gaps.</summary>
    public bool HasRules => Column.IsVisible || Row.IsVisible;
}
