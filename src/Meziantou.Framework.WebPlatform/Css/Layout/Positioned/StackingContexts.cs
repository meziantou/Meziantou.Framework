using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The stacking information of the boxes (https://drafts.csswg.org/css2/#elaborate-stacking-contexts): the used <c>z-index</c>
/// and whether a box establishes a stacking context, for the hit testing (PaintOrder, HitTesting) and the painting (<see cref="Painter"/>). It only depends
/// on the styles (and on the parent box for the flex and grid items).
/// </summary>
internal static class StackingContexts
{
    /// <summary>
    /// Returns the used <c>z-index</c> of a box (https://drafts.csswg.org/css2/#z-index): its integer value when it applies (a
    /// positioned box, https://drafts.csswg.org/css-flexbox-1/#painting a flex item, https://drafts.csswg.org/css-grid-2/#z-order
    /// a grid item, a box of the top layer), else null (<c>auto</c>).
    /// </summary>
    public static int? GetZIndex(ContainerBox box)
    {
        if (box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid || box.IsAnonymous)
            return null;

        var layout = box.Style.Layout;
        if (!layout.IsPositioned && !box.IsInTopLayer && !IsFlexOrGridItem(box))
            return null;

        return layout.Insets.ZIndex;
    }

    /// <summary>
    /// Returns whether a box establishes a stacking context (https://drafts.csswg.org/css2/#stacking-context): the root, the
    /// boxes of the top layer (https://drafts.csswg.org/css-position-4/#top-layer), the fixed and sticky positioned boxes, the
    /// boxes whose <c>z-index</c> applies and is not <c>auto</c>, an <c>opacity</c> less than 1, a transform, a filter, a
    /// perspective, <c>transform-style: preserve-3d</c>, layout or paint containment, <c>isolation: isolate</c>, a
    /// <c>mix-blend-mode</c>, a clip path, a mask, a view transition name, and the <c>will-change</c> values that do the same.
    /// </summary>
    public static bool EstablishesStackingContext(ContainerBox box)
    {
        if (box.Kind == ContainerBoxKind.Viewport)
            return true;

        if (box.IsAnonymous || box.Kind == ContainerBoxKind.TableGrid)
            return false;

        var style = box.Style;
        var layout = style.Layout;
        if (style.IsRoot || box.IsInTopLayer || box.IsScopedViewTransitionRoot || layout.Position is PositionKind.Fixed or PositionKind.Sticky || GetZIndex(box) is not null)
            return true;

        // The foreignObject elements are painted in the order of the SVG painting (https://svgwg.org/svg2-draft/render.html#PaintersModel)
        if (SvgForeignObjectLayout.IsForeignObject(box))
            return true;

        // https://drafts.csswg.org/css-transforms-2/#transform-rendering, https://drafts.fxtf.org/filter-effects/#FilterProperty,
        // https://drafts.csswg.org/css-contain-2/#containment-layout: the properties that establish a containing block for the
        // fixed positioned boxes establish a stacking context too
        if (UsedContainment.EstablishesFixedPositionContainingBlock(box))
        {
            // The transforms and the containment do not apply to the non-replaced inline boxes
            // (https://drafts.csswg.org/css-transforms-1/#transformable-element, https://drafts.csswg.org/css-contain-2/#containment-paint):
            // only their filters make them stacking contexts
            if (!box.IsInlineBox && box.Display.Inside != DisplayInside.Ruby)
                return true;

            if (style.GetComputedValue("filter") is not ("none" or "") || style.GetComputedValue("backdrop-filter") is not ("none" or "")
                || Array.Exists(layout.WillChangeProperties, static property => property is "filter" or "backdrop-filter"))
            {
                return true;
            }
        }

        // https://drafts.csswg.org/css-color-4/#transparency
        if (double.TryParse(style.GetComputedValue("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && opacity < 1)
            return true;

        if (style.GetComputedValue("isolation") == "isolate" || style.GetComputedValue("mix-blend-mode") != "normal"
            || style.GetComputedValue("clip-path") != "none" || style.GetComputedValue("mask-image") is not ("none" or "")
            || style.GetComputedValue("mask-border-source") is not ("none" or "") || style.GetComputedValue("view-transition-name") is not ("none" or ""))
        {
            return true;
        }

        // https://drafts.csswg.org/css-will-change/#will-change: "If any non-initial value of a property would create a stacking
        // context on the element, specifying that property in will-change must create a stacking context on the element"
        // (z-index on the boxes it applies to: the positioned boxes, the flex items and the grid items)
        foreach (var property in layout.WillChangeProperties)
        {
            if (property is "opacity" or "isolation" or "mix-blend-mode" or "clip-path" or "mask" or "mask-image" or "mask-border" or "mask-border-source"
                or "view-transition-name" or "position")
            {
                return true;
            }

            if (property == "z-index" && (layout.IsPositioned || IsFlexOrGridItem(box)))
                return true;
        }

        return false;
    }

    // The children of a layout API container too (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-container-painting:
    // "z-index values other than auto create a stacking context even if position is static"); their floats are in flow
    private static bool IsFlexOrGridItem(ContainerBox box)
        => box.Parent is { } parent && (box.IsInFlow && parent.Display.Inside is DisplayInside.Flex or DisplayInside.WebkitBox or DisplayInside.Grid or DisplayInside.GridLanes
            || parent.LayoutApi is not null && !box.Style.Layout.IsAbsolutelyPositioned);
}
