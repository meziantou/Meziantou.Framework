using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The try tactics of <c>position-try-fallbacks</c> (https://drafts.csswg.org/css-anchor-position-1/#execute-a-try-tactic): the values of
/// the accepted @position-try properties are swapped between the properties of the flipped sides, and modified to keep the same relation
/// to their new direction (the sides of the anchor functions, the self-alignment keywords, <c>position-area</c>).
/// </summary>
internal static class TryTactics
{
    /// <summary>Returns the declarations of the accepted @position-try properties of a style transformed by try tactics, composed in order.</summary>
    /// <param name="style">The style (the base styles with the declarations of the @position-try rule of the option).</param>
    /// <param name="tactics">The try tactics.</param>
    /// <param name="containingBlock">The writing mode of the containing block ("Logical directions are resolved against the writing mode of the containing block").</param>
    public static List<CssLonghandValue> Apply(ComputedStyle style, IReadOnlyList<TryTactic> tactics, WritingDirection containingBlock)
    {
        var map = new SideMap(containingBlock, tactics);
        var self = WritingDirection.FromStyle(style.Layout);
        var result = new List<CssLonghandValue>(AnchorPositioning.AcceptedProperties.Length);
        Span<PhysicalSide> sides = [PhysicalSide.Top, PhysicalSide.Right, PhysicalSide.Bottom, PhysicalSide.Left];
        foreach (var side in sides)
        {
            var target = map.Map(side);
            result.Add(new CssLonghandValue(GetName("", target), TransformValue(style.GetComputedValue(GetName("", side)), side, target, map, containingBlock, self, inset: true)));
            result.Add(new CssLonghandValue(GetName("margin-", target), TransformValue(style.GetComputedValue(GetName("margin-", side)), side, target, map, containingBlock, self, inset: false)));
        }

        // The sizes are swapped when the axes are swapped
        var swapsAxes = map.SwapsAxes;
        foreach (var (width, height) in (ReadOnlySpan<(string, string)>)[("width", "height"), ("min-width", "min-height"), ("max-width", "max-height")])
        {
            var widthValue = style.GetComputedValue(width);
            var heightValue = style.GetComputedValue(height);
            result.Add(new CssLonghandValue(swapsAxes ? height : width, TransformValue(widthValue, PhysicalSide.Left, map.Map(PhysicalSide.Left), map, containingBlock, self, inset: false)));
            result.Add(new CssLonghandValue(swapsAxes ? width : height, TransformValue(heightValue, PhysicalSide.Top, map.Map(PhysicalSide.Top), map, containingBlock, self, inset: false)));
        }

        // The self-alignment properties (justify-self in the inline axis of the containing block, align-self in its block axis)
        var horizontalProperty = containingBlock.IsHorizontal ? "justify-self" : "align-self";
        var verticalProperty = containingBlock.IsHorizontal ? "align-self" : "justify-self";
        var horizontalValue = style.GetComputedValue(horizontalProperty);
        var verticalValue = style.GetComputedValue(verticalProperty);
        var newHorizontal = map.Map(PhysicalSide.Left) is PhysicalSide.Left or PhysicalSide.Right;
        result.Add(new CssLonghandValue(newHorizontal ? horizontalProperty : verticalProperty, TransformAlignment(horizontalValue, horizontal: true, map, containingBlock, self, newHorizontal ? horizontalProperty : verticalProperty)));
        result.Add(new CssLonghandValue(newHorizontal ? verticalProperty : horizontalProperty, TransformAlignment(verticalValue, horizontal: false, map, containingBlock, self, newHorizontal ? verticalProperty : horizontalProperty)));

        result.Add(new CssLonghandValue("position-anchor", style.GetComputedValue("position-anchor")));
        result.Add(new CssLonghandValue("position-area", TransformPositionArea(style.GetComputedValue("position-area"), map, containingBlock, self)));
        return result;
    }

    private static string GetName(string prefix, PhysicalSide side) => prefix + side switch
    {
        PhysicalSide.Top => "top",
        PhysicalSide.Right => "right",
        PhysicalSide.Bottom => "bottom",
        _ => "left",
    };

    /// <summary>
    /// Transforms the anchor functions of a value moved from the property of a side to the property of another side: the sides of
    /// <c>anchor()</c> keep the same relation to the new direction (a percentage becomes 100% minus the percentage when the directions
    /// are opposing), the axes of <c>anchor-size()</c> are swapped with the axes.
    /// </summary>
    private static string TransformValue(string value, PhysicalSide from, PhysicalSide to, in SideMap map, WritingDirection containingBlock, WritingDirection self, bool inset)
    {
        if (!value.Contains("anchor", StringComparison.Ordinal))
            return value;

        var values = CssParser.ParseListOfComponentValues(value);
        var mapCopy = map;
        return CssSerializer.SerializeValue(Transform(values, function => TransformFunction(function, from, to, mapCopy, containingBlock, self, inset)), property: null);
    }

    private static List<CssComponentValue> Transform(List<CssComponentValue> values, Func<CssFunction, string?> transform)
    {
        var result = new List<CssComponentValue>(values.Count);
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when CssCalc.GetAnchorFunction(function.Name) is not null && transform(function) is { } text:
                    result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Delim, Value = text }));
                    break;
                case CssFunction function:
                {
                    var copy = new CssFunction(function.Name);
                    copy.Value.AddRange(Transform(function.Value, transform));
                    result.Add(copy);
                    break;
                }

                case CssSimpleBlock block:
                {
                    var copy = new CssSimpleBlock(block.AssociatedToken);
                    copy.Value.AddRange(Transform(block.Value, transform));
                    result.Add(copy);
                    break;
                }

                default:
                    result.Add(value);
                    break;
            }
        }

        return result;
    }

    private static string? TransformFunction(CssFunction function, PhysicalSide from, PhysicalSide to, SideMap map, WritingDirection containingBlock, WritingDirection self, bool inset)
    {
        if (CssAnchorFunction.Parse(function, CssAnchorFunctions.Anchor | CssAnchorFunctions.AnchorSize) is not { } parsed)
            return null;

        var builder = new StringBuilder(parsed.IsSize ? "anchor-size(" : "anchor(");
        var hasArgument = false;
        if (parsed.Name is { } name)
        {
            builder.Append(CssSerializer.SerializeIdentifier(name));
            hasArgument = true;
        }

        string? argument;
        if (parsed.IsSize)
        {
            argument = parsed.Size switch
            {
                AnchorSizeKeyword.None => null,
                AnchorSizeKeyword.Width => map.SwapsAxes ? "height" : "width",
                AnchorSizeKeyword.Height => map.SwapsAxes ? "width" : "height",
                AnchorSizeKeyword.Block => map.SwapsAxes ? "inline" : "block",
                AnchorSizeKeyword.Inline => map.SwapsAxes ? "block" : "inline",
                AnchorSizeKeyword.SelfBlock => map.SwapsAxes ? "self-inline" : "self-block",
                _ => map.SwapsAxes ? "self-block" : "self-inline",
            };
        }
        else
        {
            argument = inset ? TransformSide(parsed, from, to, map, containingBlock, self) : null;
        }

        if (argument is not null)
        {
            if (hasArgument)
            {
                builder.Append(' ');
            }

            builder.Append(argument);
            hasArgument = true;
        }

        if (parsed.Fallback is { } fallback)
        {
            if (hasArgument)
            {
                builder.Append(", ");
            }

            builder.Append(CssSerializer.SerializeValue(Transform([fallback], nested => TransformFunction(nested, from, to, map, containingBlock, self, inset)), property: null));
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>Returns the &lt;anchor-side&gt; that has the same relation to the side of the new property as the side had to the old one.</summary>
    private static string TransformSide(CssAnchorFunction function, PhysicalSide from, PhysicalSide to, in SideMap map, WritingDirection containingBlock, WritingDirection self)
    {
        var fromHorizontal = from is PhysicalSide.Left or PhysicalSide.Right;
        var toHorizontal = to is PhysicalSide.Left or PhysicalSide.Right;
        switch (function.Side)
        {
            case AnchorSide.Inside:
                return "inside";
            case AnchorSide.Outside:
                return "outside";
            case AnchorSide.Top or AnchorSide.Bottom or AnchorSide.Left or AnchorSide.Right:
                return GetName("", map.Map(function.Side switch
                {
                    AnchorSide.Top => PhysicalSide.Top,
                    AnchorSide.Bottom => PhysicalSide.Bottom,
                    AnchorSide.Left => PhysicalSide.Left,
                    _ => PhysicalSide.Right,
                }));
            case AnchorSide.Start or AnchorSide.End:
            {
                var start = AnchorFunctionEvaluator.GetStartSide(containingBlock, fromHorizontal);
                var side = function.Side == AnchorSide.Start ? start : WritingDirection.Opposite(start);
                return map.Map(side) == AnchorFunctionEvaluator.GetStartSide(containingBlock, toHorizontal) ? "start" : "end";
            }

            case AnchorSide.SelfStart or AnchorSide.SelfEnd:
            {
                var start = AnchorFunctionEvaluator.GetStartSide(self, fromHorizontal);
                var side = function.Side == AnchorSide.SelfStart ? start : WritingDirection.Opposite(start);
                return map.Map(side) == AnchorFunctionEvaluator.GetStartSide(self, toHorizontal) ? "self-start" : "self-end";
            }

            default:
            {
                // A percentage from the start side: 100% minus the percentage when the start side maps to the end side
                var start = AnchorFunctionEvaluator.GetStartSide(containingBlock, fromHorizontal);
                var percentage = map.Map(start) == AnchorFunctionEvaluator.GetStartSide(containingBlock, toHorizontal) ? function.Percentage : 100 - function.Percentage;
                return percentage.ToString(CultureInfo.InvariantCulture) + "%";
            }
        }
    }

    /// <summary>
    /// Transforms a self-alignment value moved to the axis of its mapped sides: "if directions are opposing, change the specified
    /// &lt;self-position&gt; (or left/right keywords), if any, to maintain the same relative relationship to the new direction".
    /// </summary>
    private static string TransformAlignment(string value, bool horizontal, in SideMap map, WritingDirection containingBlock, WritingDirection self, string property)
    {
        var alignment = Alignment.Parse(value);
        var fromAxis = horizontal == containingBlock.IsHorizontal ? AlignmentAxis.Inline(containingBlock) : AlignmentAxis.Block(containingBlock);
        if (fromAxis.GetSide(alignment.Keyword, self) is not { } side)
            return value;

        var target = map.Map(side);
        var toAxis = property == "justify-self" ? AlignmentAxis.Inline(containingBlock) : AlignmentAxis.Block(containingBlock);
        var keyword = alignment.Keyword switch
        {
            AlignmentKeyword.SelfStart or AlignmentKeyword.SelfEnd => target == (toAxis.GetSide(AlignmentKeyword.SelfStart, self) ?? toAxis.WritingModeStart) ? "self-start" : "self-end",
            AlignmentKeyword.Left or AlignmentKeyword.Right when toAxis.IsHorizontal => target == PhysicalSide.Left ? "left" : "right",
            _ => target == toAxis.WritingModeStart ? "start" : "end",
        };

        return alignment.Overflow switch
        {
            OverflowAlignment.Safe => "safe " + keyword,
            OverflowAlignment.Unsafe => "unsafe " + keyword,
            _ => keyword,
        };
    }

    /// <summary>Transforms a <c>position-area</c> so that "the selected rows/columns of the position-area grid maintain the same relative relationship to the new direction".</summary>
    private static string TransformPositionArea(string value, in SideMap map, WritingDirection containingBlock, WritingDirection self)
    {
        if (value == "none")
            return value;

        var (horizontal, vertical) = PositionAreaValue.Parse(value).Resolve(containingBlock, self);
        var newHorizontal = map.SwapsAxes ? vertical : horizontal;
        var newVertical = map.SwapsAxes ? horizontal : vertical;

        // The tracks are reversed when the low side (left or top) maps to the high side of the new axis
        if (map.Map(map.SwapsAxes ? PhysicalSide.Top : PhysicalSide.Left) == PhysicalSide.Right)
        {
            newHorizontal = new PositionAreaTracks(2 - newHorizontal.Last, 2 - newHorizontal.First);
        }

        if (map.Map(map.SwapsAxes ? PhysicalSide.Left : PhysicalSide.Top) == PhysicalSide.Bottom)
        {
            newVertical = new PositionAreaTracks(2 - newVertical.Last, 2 - newVertical.First);
        }

        return Serialize(newHorizontal, "left", "right") + " " + Serialize(newVertical, "top", "bottom");

        static string Serialize(PositionAreaTracks tracks, string low, string high) => (tracks.First, tracks.Last) switch
        {
            (0, 0) => low,
            (2, 2) => high,
            (1, 1) => "center",
            (0, 1) => "span-" + low,
            (1, 2) => "span-" + high,
            _ => "span-all",
        };
    }

    /// <summary>The mapping of the physical sides of a composition of try tactics.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct SideMap
    {
        private readonly PhysicalSide _top;
        private readonly PhysicalSide _right;
        private readonly PhysicalSide _bottom;
        private readonly PhysicalSide _left;

        public SideMap(WritingDirection containingBlock, IReadOnlyList<TryTactic> tactics)
        {
            (_top, _right, _bottom, _left) = (PhysicalSide.Top, PhysicalSide.Right, PhysicalSide.Bottom, PhysicalSide.Left);
            foreach (var tactic in tactics)
            {
                _top = Apply(tactic, _top, containingBlock);
                _right = Apply(tactic, _right, containingBlock);
                _bottom = Apply(tactic, _bottom, containingBlock);
                _left = Apply(tactic, _left, containingBlock);
            }
        }

        /// <summary>Whether the horizontal and vertical axes are swapped (flip-start).</summary>
        public bool SwapsAxes => _left is PhysicalSide.Top or PhysicalSide.Bottom;

        public PhysicalSide Map(PhysicalSide side) => side switch
        {
            PhysicalSide.Top => _top,
            PhysicalSide.Right => _right,
            PhysicalSide.Bottom => _bottom,
            _ => _left,
        };

        private static PhysicalSide Apply(TryTactic tactic, PhysicalSide side, WritingDirection containingBlock)
        {
            switch (tactic)
            {
                case TryTactic.FlipX:
                    return side switch { PhysicalSide.Left => PhysicalSide.Right, PhysicalSide.Right => PhysicalSide.Left, _ => side };
                case TryTactic.FlipY:
                    return side switch { PhysicalSide.Top => PhysicalSide.Bottom, PhysicalSide.Bottom => PhysicalSide.Top, _ => side };
                case TryTactic.FlipBlock:
                    return side == containingBlock.BlockStart ? containingBlock.BlockEnd : side == containingBlock.BlockEnd ? containingBlock.BlockStart : side;
                case TryTactic.FlipInline:
                    return side == containingBlock.InlineStart ? containingBlock.InlineEnd : side == containingBlock.InlineEnd ? containingBlock.InlineStart : side;
                default:
                    // flip-start: "swaps the values of the start properties with each other, and the end properties with each other"
                    if (side == containingBlock.BlockStart)
                        return containingBlock.InlineStart;

                    if (side == containingBlock.InlineStart)
                        return containingBlock.BlockStart;

                    if (side == containingBlock.BlockEnd)
                        return containingBlock.InlineEnd;

                    return containingBlock.BlockEnd;
            }
        }
    }
}
