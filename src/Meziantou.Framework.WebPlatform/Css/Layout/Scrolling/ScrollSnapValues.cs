using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The axis value of scroll-snap-type (https://drafts.csswg.org/css-scroll-snap-1/#snap-axis).</summary>
internal enum ScrollSnapAxis : byte
{
    /// <summary><c>none</c>: the scroll container does not snap.</summary>
    None,
    X,
    Y,
    Block,
    Inline,
    Both,

    /// <summary>Both axes together, as a single 2D point of a snap area aligned in both axes.</summary>
    Pair,
}

/// <summary>The strictness value of scroll-snap-type (https://drafts.csswg.org/css-scroll-snap-1/#snap-strictness).</summary>
internal enum ScrollSnapStrictness : byte
{
    Proximity,
    Mandatory,
}

/// <summary>The computed scroll-snap-type (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-type).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ScrollSnapType(ScrollSnapAxis Axis, ScrollSnapStrictness Strictness)
{
    public bool IsNone => Axis == ScrollSnapAxis.None;

    /// <summary>Parses the computed value (<c>none</c>, or an axis followed by an optional strictness, <c>proximity</c> by default).</summary>
    public static ScrollSnapType Parse(string value)
    {
        if (value is "none" or "")
            return default;

        var separator = value.IndexOf(' ', StringComparison.Ordinal);
        var axisName = separator < 0 ? value : value[..separator];
        var axis = axisName switch
        {
            "x" => ScrollSnapAxis.X,
            "y" => ScrollSnapAxis.Y,
            "block" => ScrollSnapAxis.Block,
            "inline" => ScrollSnapAxis.Inline,
            "both" => ScrollSnapAxis.Both,
            "pair" => ScrollSnapAxis.Pair,
            _ => ScrollSnapAxis.None,
        };
        var strictness = separator >= 0 && value.AsSpan(separator + 1).Trim() is "mandatory" ? ScrollSnapStrictness.Mandatory : ScrollSnapStrictness.Proximity;
        return new ScrollSnapType(axis, strictness);
    }
}

/// <summary>A value of scroll-snap-align (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-align), or a physical alignment once resolved.</summary>
internal enum SnapAlignment : byte
{
    None,
    Start,
    End,
    Center,
}

/// <summary>The computed scroll-snap-align: the alignments in the block axis and in the inline axis.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ScrollSnapAlign(SnapAlignment Block, SnapAlignment Inline)
{
    public bool IsNone => Block == SnapAlignment.None && Inline == SnapAlignment.None;

    /// <summary>Parses the computed value: one or two keywords (the second one defaults to the first one).</summary>
    public static ScrollSnapAlign Parse(string value)
    {
        if (value is "none" or "")
            return default;

        var separator = value.IndexOf(' ', StringComparison.Ordinal);
        var block = ParseKeyword(separator < 0 ? value : value[..separator]);
        var inline = separator < 0 ? block : ParseKeyword(value[(separator + 1)..].Trim());
        return new ScrollSnapAlign(block, inline);

        static SnapAlignment ParseKeyword(string keyword) => keyword switch
        {
            "start" => SnapAlignment.Start,
            "end" => SnapAlignment.End,
            "center" => SnapAlignment.Center,
            _ => SnapAlignment.None,
        };
    }
}
