using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Maps the flow-relative properties to the physical properties of their logical property group
/// (https://drafts.csswg.org/css-logical/#logical-property-group) using the writing mode and the direction
/// (https://drafts.csswg.org/css-writing-modes-4/#logical-to-physical).
/// </summary>
internal static class LogicalProperties
{
    /// <summary>
    /// How a logical property maps to a physical property: the template of the physical name and the logical axis or side, and
    /// the physical names for each writing mode and direction (computed once).
    /// </summary>
    private sealed class Mapping(string prefix, string suffix, LogicalKind kind)
    {
        private readonly string?[] _physicalNames = new string?[WritingModeCount * 2];

        public string Prefix { get; } = prefix;

        public string Suffix { get; } = suffix;

        public LogicalKind Kind { get; } = kind;

        /// <summary>Returns the physical property for a writing mode (its index, see <see cref="GetWritingModeIndex"/>, and its name) and a direction.</summary>
        public string GetPhysicalName(int writingModeIndex, string writingMode, bool rtl)
        {
            // (the mappings are shared by the threads: a name computed twice is the same)
            ref var name = ref _physicalNames[(writingModeIndex * 2) + (rtl ? 1 : 0)];
            return name ??= ComputePhysicalProperty(this, writingMode, rtl);
        }
    }

    /// <summary>The number of writing modes whose physical names are cached by <see cref="Mapping"/>: the values of <c>writing-mode</c>, and the other values.</summary>
    private const int WritingModeCount = 6;

    private static int GetWritingModeIndex(string writingMode) => writingMode switch
    {
        "horizontal-tb" or "" => 0,
        "vertical-rl" => 1,
        "vertical-lr" => 2,
        "sideways-rl" => 3,
        "sideways-lr" => 4,
        _ => 5,
    };

    private enum LogicalKind
    {
        BlockStart,
        BlockEnd,
        InlineStart,
        InlineEnd,
        BlockSize,
        InlineSize,
        BlockAxis,
        InlineAxis,
        StartStart,
        StartEnd,
        EndStart,
        EndEnd,
    }

    private static readonly ConcurrentDictionary<string, Mapping?> Mappings = new(StringComparer.Ordinal);

    /// <summary>Returns whether the property is a flow-relative property of a logical property group (its mapping logic is not physical).</summary>
    public static bool IsFlowRelative(string property) => Mappings.GetOrAdd(property, GetMapping) is not null;

    /// <summary>
    /// Returns whether the used direction of an element is right-to-left: its computed <c>direction</c>, except for the upright
    /// vertical text (https://drafts.csswg.org/css-writing-modes-4/#valdef-text-orientation-upright: "This value causes the used
    /// value of direction to be ltr"; <c>text-orientation</c> only applies to the <c>vertical-*</c> writing modes).
    /// </summary>
    public static bool IsRightToLeft(ComputedStyle style)
        => style.GetComputedValue("direction") == "rtl"
            && !(style.GetComputedValue("writing-mode") is "vertical-rl" or "vertical-lr" && style.GetComputedValue("text-orientation") == "upright");

    /// <summary>Returns the physical property a logical property maps to for the element, or null when the property is not a logical property.</summary>
    public static string? GetPhysicalProperty(string property, ComputedStyle style)
    {
        var mapping = Mappings.GetOrAdd(property, GetMapping);
        if (mapping is null)
            return null;

        var writingMode = style.GetComputedValue("writing-mode");
        var writingModeIndex = GetWritingModeIndex(writingMode);
        var rtl = style.GetComputedValue("direction") == "rtl" && !(writingModeIndex is 1 or 2 && style.GetComputedValue("text-orientation") == "upright");
        return mapping.GetPhysicalName(writingModeIndex, writingMode, rtl);
    }

    private static string ComputePhysicalProperty(Mapping mapping, string writingMode, bool rtl)
    {
        var vertical = writingMode is not ("horizontal-tb" or "");

        // The physical sides of the block and inline axes
        var blockStart = writingMode switch
        {
            "vertical-rl" or "sideways-rl" => "right",
            "vertical-lr" or "sideways-lr" => "left",
            _ => "top",
        };
        var blockEnd = Opposite(blockStart);
        string inlineStart;
        if (!vertical)
        {
            inlineStart = rtl ? "right" : "left";
        }
        else if (writingMode == "sideways-lr")
        {
            inlineStart = rtl ? "top" : "bottom";
        }
        else
        {
            inlineStart = rtl ? "bottom" : "top";
        }

        var inlineEnd = Opposite(inlineStart);
        var physical = mapping.Kind switch
        {
            LogicalKind.BlockStart => blockStart,
            LogicalKind.BlockEnd => blockEnd,
            LogicalKind.InlineStart => inlineStart,
            LogicalKind.InlineEnd => inlineEnd,
            LogicalKind.BlockSize => vertical ? "width" : "height",
            LogicalKind.InlineSize => vertical ? "height" : "width",
            LogicalKind.BlockAxis => vertical ? "x" : "y",
            LogicalKind.InlineAxis => vertical ? "y" : "x",
            LogicalKind.StartStart => Corner(blockStart, inlineStart),
            LogicalKind.StartEnd => Corner(blockStart, inlineEnd),
            LogicalKind.EndStart => Corner(blockEnd, inlineStart),
            _ => Corner(blockEnd, inlineEnd),
        };

        return mapping.Prefix + physical + mapping.Suffix;
    }

    private static string Opposite(string side) => side switch
    {
        "top" => "bottom",
        "bottom" => "top",
        "left" => "right",
        _ => "left",
    };

    /// <summary>The name of a physical corner: the vertical side first (e.g. top-left).</summary>
    private static string Corner(string side1, string side2) => side1 is "top" or "bottom" ? side1 + "-" + side2 : side2 + "-" + side1;

    private static Mapping? GetMapping(string property)
    {
        if (CssPropertyRegistry.Get(property) is not { LogicalGroup: { } group } definition || definition.Name != property)
            return null;

        (string Logical, LogicalKind Kind)[] patterns =
        [
            ("start-start", LogicalKind.StartStart),
            ("start-end", LogicalKind.StartEnd),
            ("end-start", LogicalKind.EndStart),
            ("end-end", LogicalKind.EndEnd),
            ("block-start", LogicalKind.BlockStart),
            ("block-end", LogicalKind.BlockEnd),
            ("inline-start", LogicalKind.InlineStart),
            ("inline-end", LogicalKind.InlineEnd),
            ("block-size", LogicalKind.BlockSize),
            ("inline-size", LogicalKind.InlineSize),
            ("block", LogicalKind.BlockAxis),
            ("inline", LogicalKind.InlineAxis),
        ];

        foreach (var (logical, kind) in patterns)
        {
            var index = FindPart(property, logical);
            if (index < 0)
                continue;

            var prefix = property[..index];
            var suffix = property[(index + logical.Length)..];

            // The physical properties of the group use the same template (e.g. margin-inline-start → margin-left), except
            // the insets (inset-block-start → top) and the sizes (min-inline-size → min-width, overflow-inline → overflow-x)
            if (Exists(prefix + "top" + suffix, group) || Exists(prefix + "top-left" + suffix, group) || Exists(prefix + "width" + suffix, group) || Exists(prefix + "x" + suffix, group))
                return new Mapping(prefix, suffix, kind);

            if (prefix == "inset-" && Exists("top", group))
                return new Mapping("", suffix, kind);

            return null;
        }

        return null;
    }

    /// <summary>Returns the index of a dash-separated part of the name (e.g. "inline-start" in "margin-inline-start").</summary>
    private static int FindPart(string name, string part)
    {
        var index = 0;
        while ((index = name.IndexOf(part, index, StringComparison.Ordinal)) >= 0)
        {
            var end = index + part.Length;
            if ((index == 0 || name[index - 1] == '-') && (end == name.Length || name[end] == '-'))
                return index;

            index++;
        }

        return -1;
    }

    private static bool Exists(string name, string group) => CssPropertyRegistry.Get(name) is { } property && property.Name == name && property.LogicalGroup == group;
}
