using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of the <c>foreignObject</c> elements of an outermost svg element (https://svgwg.org/svg2-draft/embedded.html#ForeignObjectElement):
/// each one is a block formatting context root (a child box of the replaced box of the svg element, see
/// <see cref="BoxTreeBuilder"/>) laid out with the size of its <c>width</c> and <c>height</c> in its user space, and placed by a
/// transform (<see cref="BoxFragment.SvgTransform"/>) at its <c>x</c> and <c>y</c> in its user space, mapped to the content box
/// of the svg element by the transforms of the element and of its SVG ancestors, the viewports of the nested svg elements and
/// the viewBox of the svg element. The element establishes a stacking context and the containing block of its absolutely
/// positioned and fixed positioned descendants.
/// </summary>
internal static class SvgForeignObjectLayout
{
    /// <summary>Whether a box is the box of a foreignObject element (a child of the replaced box of an svg element).</summary>
    public static bool IsForeignObject(ContainerBox box) => box.Element is SvgForeignObjectElement && box.Parent is { Kind: ContainerBoxKind.Replaced };

    /// <summary>
    /// Returns the nearest SVG viewport of the fragment of a foreignObject element in the coordinates of the fragment (the origin of
    /// the user space of the element is at -<c>x</c>, -<c>y</c> from its border box; a user unit is a CSS px of the zoom of the
    /// element), or null when the fragment is not the one of a foreignObject element. It is the reference box of <c>view-box</c>
    /// (https://drafts.csswg.org/css-box-4/#valdef-box-view-box), and its origin is the one of the user space of the SVG
    /// resources applied to the element.
    /// </summary>
    public static PhysicalRect? GetViewportRect(BoxFragment fragment)
    {
        if (!IsForeignObject(fragment.Box) || fragment.Box.Element is not { } element || fragment.Parent is not BoxFragment svgFragment)
            return null;

        var (width, height) = SvgUserSpace.GetViewportSize(element, SvgUserSpace.GetOutermostViewport(svgFragment));
        var x = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "x"), width) ?? 0;
        var y = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "y"), height) ?? 0;
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        return new PhysicalRect(-x * zoom, -y * zoom, width * zoom, height * zoom);
    }

    /// <summary>Lays out the foreignObject boxes of the replaced box of an outermost svg element and appends their fragments to its fragment.</summary>
    public static void Layout(LayoutContext context, ContainerBox svgBox, BoxFragment svgFragment)
    {
        List<BoxFragment>? children = null;
        var viewport = SvgUserSpace.GetOutermostViewport(svgFragment);
        var contentOffset = svgFragment.ContentBoxRect.Offset;
        for (var child = svgBox.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is not ContainerBox { Element: SvgForeignObjectElement element } box)
                continue;

            var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
            var w = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "width"), width) ?? 0;
            var h = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "height"), height) ?? 0;

            // https://svgwg.org/svg2-draft/geometry.html#Sizing: a negative size is an error (auto, 0); an element of zero size is not rendered
            if (w <= 0 || h <= 0 || SvgUserSpace.GetTransformToOutermostContentBox(element, out _, viewport) is not { } transform)
                continue;

            var x = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "x"), width) ?? 0;
            var y = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "y"), height) ?? 0;

            // The CSS contents are laid out in CSS px (zoomed): a user unit is a CSS px of the zoom of the element
            var zoom = box.Style.Layout.EffectiveZoom;
            var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
            var size = writingDirection.ToLogical(new PhysicalSize(w * zoom, h * zoom));
            var space = ConstraintSpace.ForContentBox(writingDirection, size.Inline, size.Block) with
            {
                FixedInlineSize = size.Inline,
                FixedBlockSize = size.Block,
            };

            var fragment = context.LayoutBox(box, space);
            fragment.Offset = contentOffset;
            fragment.SvgTransform = SvgAffine.Scale(1 / zoom, 1 / zoom).Then(SvgAffine.Translation(x, y)).Then(transform).ToMatrix();
            (children ??= []).Add(fragment);
        }

        if (children is not null)
        {
            svgFragment.AppendOutOfFlowChildren(children);
        }
    }
}
