using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The alignment keywords of the box alignment properties (https://drafts.csswg.org/css-align-3/): align-items,
/// align-self, align-content, justify-content, justify-items, justify-self.
/// </summary>
internal enum AlignmentKeyword : byte
{
    /// <summary><c>auto</c> (the *-self properties: the value of the *-items property of the parent).</summary>
    Auto,
    Normal,
    Stretch,

    /// <summary><c>baseline</c> or <c>first baseline</c>.</summary>
    Baseline,

    /// <summary><c>last baseline</c>.</summary>
    LastBaseline,
    Center,
    Start,
    End,
    SelfStart,
    SelfEnd,
    FlexStart,
    FlexEnd,
    Left,
    Right,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    AnchorCenter,

    /// <summary>The <c>legacy</c> keyword alone (justify-items).</summary>
    Legacy,
}

/// <summary>https://drafts.csswg.org/css-align-3/#overflow-values</summary>
internal enum OverflowAlignment : byte
{
    /// <summary>Neither safe nor unsafe: the default overflow alignment (https://drafts.csswg.org/css-align-3/#auto-safety).</summary>
    Default,
    Safe,
    Unsafe,
}

/// <summary>
/// A computed value of a box alignment property: the alignment keyword, the overflow alignment (<c>safe</c>, <c>unsafe</c>),
/// and whether the <c>legacy</c> keyword is combined with a position (justify-items: <c>legacy left</c>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Alignment(AlignmentKeyword Keyword, OverflowAlignment Overflow = OverflowAlignment.Default, bool IsLegacy = false)
{
    public static Alignment Auto => new(AlignmentKeyword.Auto);

    public static Alignment Normal => new(AlignmentKeyword.Normal);

    /// <summary>Whether the keyword is a &lt;content-distribution&gt; (https://drafts.csswg.org/css-align-3/#typedef-content-distribution).</summary>
    public bool IsDistribution => Keyword is AlignmentKeyword.SpaceBetween or AlignmentKeyword.SpaceAround or AlignmentKeyword.SpaceEvenly or AlignmentKeyword.Stretch;

    /// <summary>Whether the keyword is a baseline alignment (https://drafts.csswg.org/css-align-3/#baseline-values).</summary>
    public bool IsBaseline => Keyword is AlignmentKeyword.Baseline or AlignmentKeyword.LastBaseline;

    /// <summary>Parses a computed value: e.g. <c>normal</c>, <c>safe center</c>, <c>last baseline</c>, <c>legacy left</c>.</summary>
    public static Alignment Parse(string value)
    {
        switch (value)
        {
            case "auto":
                return Auto;
            case "normal":
                return Normal;
            case "stretch":
                return new(AlignmentKeyword.Stretch);
            case "center":
                return new(AlignmentKeyword.Center);
            case "start":
                return new(AlignmentKeyword.Start);
            case "end":
                return new(AlignmentKeyword.End);
            case "flex-start":
                return new(AlignmentKeyword.FlexStart);
            case "flex-end":
                return new(AlignmentKeyword.FlexEnd);
            case "flow-start":
                return new(AlignmentKeyword.FlexStart);
            case "flow-end":
                return new(AlignmentKeyword.FlexEnd);
            case "baseline":
                return new(AlignmentKeyword.Baseline);
        }

        var keyword = AlignmentKeyword.Normal;
        var overflow = OverflowAlignment.Default;
        var legacy = false;
        var last = false;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "safe":
                    overflow = OverflowAlignment.Safe;
                    break;
                case "unsafe":
                    overflow = OverflowAlignment.Unsafe;
                    break;
                case "legacy":
                    legacy = true;
                    break;
                case "first":
                    break;
                case "last":
                    last = true;
                    break;
                default:
                    keyword = ParseKeyword(part);
                    break;
            }
        }

        if (keyword == AlignmentKeyword.Baseline && last)
        {
            keyword = AlignmentKeyword.LastBaseline;
        }

        // legacy alone (justify-items)
        if (legacy && value == "legacy")
            return new(AlignmentKeyword.Legacy);

        return new(keyword, overflow, legacy);
    }

    private static AlignmentKeyword ParseKeyword(string value) => value switch
    {
        "auto" => AlignmentKeyword.Auto,
        "stretch" => AlignmentKeyword.Stretch,
        "baseline" => AlignmentKeyword.Baseline,
        "center" => AlignmentKeyword.Center,
        "start" => AlignmentKeyword.Start,
        "end" => AlignmentKeyword.End,
        "self-start" => AlignmentKeyword.SelfStart,
        "self-end" => AlignmentKeyword.SelfEnd,
        "flex-start" => AlignmentKeyword.FlexStart,
        "flex-end" => AlignmentKeyword.FlexEnd,

        // The flow-relative positions: flex-start and flex-end are their aliases (the start and end of the flow of a flex container
        // or of a grid lanes container, the start and end of the axis in the other layout modes)
        "flow-start" => AlignmentKeyword.FlexStart,
        "flow-end" => AlignmentKeyword.FlexEnd,
        "left" => AlignmentKeyword.Left,
        "right" => AlignmentKeyword.Right,
        "space-between" => AlignmentKeyword.SpaceBetween,
        "space-around" => AlignmentKeyword.SpaceAround,
        "space-evenly" => AlignmentKeyword.SpaceEvenly,
        "anchor-center" => AlignmentKeyword.AnchorCenter,
        _ => AlignmentKeyword.Normal,
    };
}
