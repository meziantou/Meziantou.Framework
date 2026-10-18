using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Resolves the anchor functions of the style of an absolutely positioned box to lengths (https://drafts.csswg.org/css-anchor-position-1/#anchor-resolution,
/// https://drafts.csswg.org/css-anchor-position-1/#anchor-size-resolution): a resolvable function is replaced by the length that aligns
/// the edge of the inset-modified containing block with the edge of the anchor box (<c>anchor()</c>) or by the size of the anchor box
/// (<c>anchor-size()</c>); an unresolvable one by its fallback value, and a declaration whose function has no fallback is invalid at
/// computed-value time (unset).
/// </summary>
/// <param name="query">The anchor queries of the box.</param>
/// <param name="style">The style of the box (with its position option).</param>
/// <param name="containingBlockRect">The containing block the insets refer to (the area of <c>position-area</c>), in the coordinates of the host.</param>
/// <param name="containingBlock">The writing mode and the direction of the containing block.</param>
/// <param name="defaultAnchor">The default anchor element, or null.</param>
/// <param name="getRect">Returns the rectangle of an anchor box with its remembered scroll offset.</param>
internal sealed class AnchorFunctionEvaluator(AnchorQuery query, ComputedStyle style, PhysicalRect containingBlockRect, WritingDirection containingBlock, ContainerBox? defaultAnchor, Func<AnchorBox, PhysicalRect> getRect)
{
    private List<(PhysicalSide Side, AnchorBox Anchor)>? _references;

    /// <summary>Whether a function without anchor name (the default anchor element) was evaluated.</summary>
    public bool ReferencesDefaultAnchor { get; private set; }

    /// <summary>Returns the style whose anchor functions are resolved (the style itself when it has none).</summary>
    /// <param name="baseStyle">The computed style of the element.</param>
    public ComputedStyle Resolve(ComputedStyle baseStyle)
    {
        List<CssLonghandValue>? declarations = null;
        var zoom = style.Layout.EffectiveZoom;
        foreach (var property in AnchorPositioningStyle.AnchorFunctionProperties)
        {
            var value = style.GetComputedValue(property);
            if (!value.Contains("anchor", StringComparison.Ordinal))
                continue;

            var values = CssParser.ParseListOfComponentValues(value);
            var substituted = CssAnchorFunction.Substitute(values, function => Evaluate(function, property) is { } length ? length / zoom : null);
            (declarations ??= []).Add(new CssLonghandValue(property, substituted is null ? "unset" : CssSerializer.SerializeValue(substituted, CssPropertyRegistry.Get(property))));
        }

        return declarations is null ? style : style.WithPositionOption(declarations, baseStyle, resolvesValues: true);
    }

    /// <summary>
    /// Returns whether an <c>anchor()</c> function of an axis refers to an anchor box in the scrolling context of the default anchor box
    /// (https://drafts.csswg.org/css-anchor-position-1/#compensate-for-scroll): the same nearest scroll container.
    /// </summary>
    public bool ReferencesScrollContextOf(AnchorBox defaultAnchor, bool horizontalAxis)
    {
        if (_references is null)
            return false;

        var nearest = defaultAnchor.GetNearestScrollContainer(horizontalAxis);
        foreach (var (side, anchor) in _references)
        {
            if (side is PhysicalSide.Left or PhysicalSide.Right == horizontalAxis && ReferenceEquals(anchor.GetNearestScrollContainer(horizontalAxis), nearest))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the inset properties whose <c>anchor()</c> functions refer to the scrolling context of the default anchor box: the edges
    /// of the inset-modified containing block that follow it when it is scrolled.
    /// </summary>
    public Sides<bool> GetSidesFollowing(AnchorBox defaultAnchor)
    {
        var result = default(Sides<bool>);
        if (_references is null)
            return result;

        foreach (var (side, anchor) in _references)
        {
            var horizontal = side is PhysicalSide.Left or PhysicalSide.Right;
            if (ReferenceEquals(anchor.GetNearestScrollContainer(horizontal), defaultAnchor.GetNearestScrollContainer(horizontal)))
            {
                result = result.With(side, true);
            }
        }

        return result;
    }

    /// <summary>Evaluates an anchor function used in a property, in px (zoomed), or returns null when it is unresolvable.</summary>
    private double? Evaluate(CssAnchorFunction function, string property)
    {
        ContainerBox? target;
        if (function.Name is { } name)
        {
            target = query.FindNamedAnchor(new TreeScopedName(name, GetScope(property)));
        }
        else
        {
            ReferencesDefaultAnchor = true;
            target = defaultAnchor;
        }

        if (target is null || query.GetAnchorBox(target) is not { } anchorBox)
            return null;

        var rect = getRect(anchorBox);
        return function.IsSize ? EvaluateSize(function, property, rect) : EvaluateAnchor(function, property, rect, anchorBox);
    }

    /// <summary>The tree of the declaration of a property (the anchor names of its functions are tree-scoped references).</summary>
    private Node GetScope(string property)
    {
        if (style.TryGetCascadedValue(property, out var cascaded) && cascaded.TreeScope is { } scope)
            return scope;

        var element = style.Element;
        return element.GetRoot() is { } root and (Document or ShadowRoot) ? root : element;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-anchor-position-1/#anchor-pos: "the length that would align the edge of the positioned boxes'
    /// inset-modified containing block corresponding to the property the function appears in with the specified edge of the target
    /// anchor element's anchor box".
    /// </summary>
    private double? EvaluateAnchor(CssAnchorFunction function, string property, PhysicalRect rect, AnchorBox anchorBox)
    {
        // anchor() is only allowed in the inset properties
        PhysicalSide propertySide;
        switch (property)
        {
            case "top":
                propertySide = PhysicalSide.Top;
                break;
            case "right":
                propertySide = PhysicalSide.Right;
                break;
            case "bottom":
                propertySide = PhysicalSide.Bottom;
                break;
            case "left":
                propertySide = PhysicalSide.Left;
                break;
            default:
                return null;
        }

        var horizontal = propertySide is PhysicalSide.Left or PhysicalSide.Right;
        var (low, high) = horizontal ? (rect.Left, rect.Right) : (rect.Top, rect.Bottom);
        var self = WritingDirection.FromStyle(style.Layout);
        double edge;
        switch (function.Side)
        {
            case AnchorSide.Inside:
                edge = GetEdge(propertySide);
                break;
            case AnchorSide.Outside:
                edge = GetEdge(WritingDirection.Opposite(propertySide));
                break;

            // "These are only usable in the inset properties in the matching axis"
            case AnchorSide.Top or AnchorSide.Bottom when horizontal:
            case AnchorSide.Left or AnchorSide.Right when !horizontal:
                return null;
            case AnchorSide.Top:
                edge = rect.Top;
                break;
            case AnchorSide.Bottom:
                edge = rect.Bottom;
                break;
            case AnchorSide.Left:
                edge = rect.Left;
                break;
            case AnchorSide.Right:
                edge = rect.Right;
                break;
            case AnchorSide.Start:
                edge = GetEdge(GetStartSide(containingBlock, horizontal));
                break;
            case AnchorSide.End:
                edge = GetEdge(WritingDirection.Opposite(GetStartSide(containingBlock, horizontal)));
                break;
            case AnchorSide.SelfStart:
                edge = GetEdge(GetStartSide(self, horizontal));
                break;
            case AnchorSide.SelfEnd:
                edge = GetEdge(WritingDirection.Opposite(GetStartSide(self, horizontal)));
                break;
            default:
            {
                // "a position a corresponding percentage between the start and end sides, with 0% being equivalent to start"
                var start = GetStartSide(containingBlock, horizontal);
                var fraction = function.Percentage / 100;
                edge = start is PhysicalSide.Left or PhysicalSide.Top ? low + (fraction * (high - low)) : high - (fraction * (high - low));
                break;
            }
        }

        (_references ??= []).Add((propertySide, anchorBox));
        return propertySide switch
        {
            PhysicalSide.Left => edge - containingBlockRect.Left,
            PhysicalSide.Right => containingBlockRect.Right - edge,
            PhysicalSide.Top => edge - containingBlockRect.Top,
            _ => containingBlockRect.Bottom - edge,
        };

        double GetEdge(PhysicalSide side) => side switch
        {
            PhysicalSide.Left => rect.Left,
            PhysicalSide.Right => rect.Right,
            PhysicalSide.Top => rect.Top,
            _ => rect.Bottom,
        };
    }

    /// <summary>
    /// https://drafts.csswg.org/css-anchor-position-1/#anchor-size-fn: "the length separating the relevant edges (either left and right, or
    /// top and bottom, whichever is in the specified axis) of the target anchor element's anchor box"; an omitted &lt;anchor-size&gt;
    /// "defaults to behaving as whatever keyword matches the axis of the property".
    /// </summary>
    private double EvaluateSize(CssAnchorFunction function, string property, PhysicalRect rect)
    {
        var self = WritingDirection.FromStyle(style.Layout);
        var width = function.Size switch
        {
            AnchorSizeKeyword.Width => true,
            AnchorSizeKeyword.Height => false,
            AnchorSizeKeyword.Inline => containingBlock.IsHorizontal,
            AnchorSizeKeyword.Block => !containingBlock.IsHorizontal,
            AnchorSizeKeyword.SelfInline => self.IsHorizontal,
            AnchorSizeKeyword.SelfBlock => !self.IsHorizontal,
            _ => property is "width" or "min-width" or "max-width" or "left" or "right" or "margin-left" or "margin-right",
        };

        return width ? rect.Width : rect.Height;
    }

    /// <summary>Returns the start side of a physical axis in a writing mode.</summary>
    internal static PhysicalSide GetStartSide(WritingDirection writingDirection, bool horizontal)
        => writingDirection.IsHorizontal == horizontal ? writingDirection.InlineStart : writingDirection.BlockStart;
}
