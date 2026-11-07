using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>How a resolved value that depends on the layout is obtained (see <see cref="LayoutResolvedValues"/>).</summary>
internal enum LayoutResolution : byte
{
    /// <summary>
    /// There is no layout: the document has no browsing context, or the element is not connected. The resolved value is
    /// computed from the sizes known without layout (see <see cref="BoxSizes"/>).
    /// </summary>
    NoLayout,

    /// <summary>The element does not generate a box, or the property does not apply to its box: the resolved value is the computed value.</summary>
    ComputedValue,

    /// <summary>The resolved value is the used value, from the layout.</summary>
    UsedValue,
}

/// <summary>
/// The resolved values of the properties that depend on the layout (https://drafts.csswg.org/cssom/#resolved-values), read from
/// the fragment tree. Reading one computes the layout when it is out of date (a forced layout); the other properties never do.
/// </summary>
/// <remarks>
/// "If the property applies to the element or pseudo-element and the resolved value of the display property is not none or
/// contents, then the resolved value is the used value. Otherwise the resolved value is the computed value." The used values
/// are in the coordinates of the fragment tree (zoomed): they are divided by the effective zoom of the element
/// (https://drafts.csswg.org/css-viewport/#zoom-property), like the computed values.
/// </remarks>
internal static class LayoutResolvedValues
{
    /// <summary>
    /// Returns the used width or height (https://drafts.csswg.org/cssom/#resolved-values): the size of the content box, or of the
    /// border box when box-sizing is border-box, of the principal box (the table box of a table). They do not apply to the
    /// non-replaced inline boxes (https://drafts.csswg.org/css-sizing-3/#preferred-size-properties).
    /// </summary>
    /// <remarks>
    /// As in the browsers, the scrollbar gutters are part of the content box (css/cssom/getComputedStyle-width-scroll.tentative.html):
    /// the resolved value round-trips.
    /// </remarks>
    public static LayoutResolution GetSize(ComputedStyle style, bool horizontal, out double size)
    {
        size = 0;
        if (style.PseudoElement is null && style.Element is SvgElement { ParentNode: SvgElement } svgElement && svgElement.LocalName is "rect" or "image" or "foreignObject")
            return GetSvgSize(style, svgElement, horizontal, out size);

        var resolution = GetFirstFragment(style, out var fragment);
        if (resolution != LayoutResolution.UsedValue)
            return resolution;

        if (fragment!.Box.IsInlineBox)
            return LayoutResolution.ComputedValue;

        // The content box includes the scrollbar gutters, so that the value round-trips (the gutters are subtracted from the
        // specified size: https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing)
        fragment = GetTableBox(fragment);
        var rect = fragment.BorderBoxRect;
        if (style.Layout.BoxSizing == BoxSizing.ContentBox)
        {
            rect = rect.Contract(fragment.Border.Add(fragment.Padding));
        }

        size = Math.Max(0, horizontal ? rect.Width : rect.Height) / style.Layout.EffectiveZoom;
        return LayoutResolution.UsedValue;
    }

    /// <summary>
    /// Returns the used width or height of an SVG element whose geometry properties include them
    /// (https://svgwg.org/svg2-draft/geometry.html#Sizing): the length in user units, its percentage resolved against its SVG
    /// viewport; auto is 0, or the natural size of an image. As in the browsers, the resolved value of the other SVG elements
    /// (e.g. a nested svg element) is the computed value.
    /// </summary>
    private static LayoutResolution GetSvgSize(ComputedStyle style, SvgElement element, bool horizontal, out double size)
    {
        size = 0;
        if (GetLayoutEngine(style) is null)
            return LayoutResolution.NoLayout;

        if (SvgUserSpace.GetOutermostSvgElement(element) is not { } outermost || ReferenceEquals(outermost, element) || !BoxGeneration.Get(style).IsBeingRendered)
            return LayoutResolution.ComputedValue;

        var (width, height) = SvgUserSpace.GetViewportSize(element);
        var basis = horizontal ? width : height;
        var value = SvgUserSpace.ResolveLength(element, style.GetComputedValue(horizontal ? "width" : "height"), basis);
        if (value is null or < 0)
        {
            if (element is SvgImageElement image)
            {
                var other = SvgUserSpace.ResolveLength(element, style.GetComputedValue(horizontal ? "height" : "width"), horizontal ? height : width) is >= 0 and var length ? length : (double?)null;
                var used = horizontal ? image.GetUsedSize(width: null, other) : image.GetUsedSize(other, height: null);
                value = used is { } usedSize ? (horizontal ? usedSize.Width : usedSize.Height) : null;
            }
        }

        size = Math.Max(0, value ?? 0);
        return LayoutResolution.UsedValue;
    }

    /// <summary>
    /// Returns the used margin of a side (https://drafts.csswg.org/css-box-4/#margins): the percentages are resolved against the
    /// inline size of the containing block, and the <c>auto</c> margins are resolved by the layout. The margins do not apply to
    /// the internal table boxes other than the captions.
    /// </summary>
    /// <remarks>
    /// The <c>auto</c> margins of an in-flow block-level box in the inline axis of its block container are the distances between
    /// its border box and the content box of the container, so they include the space taken by the floats beside it
    /// (https://github.com/w3c/csswg-drafts/issues/9174, as in the browsers). The other margins are not changed when the box is
    /// over-constrained (https://drafts.csswg.org/css2/#blockwidth): as in the browsers, the used value is the specified value.
    /// </remarks>
    public static LayoutResolution GetMargin(ComputedStyle style, PhysicalSide side, out double margin)
    {
        margin = 0;
        var resolution = GetFirstFragment(style, out var fragment);
        if (resolution != LayoutResolution.UsedValue)
            return resolution;

        var box = fragment!.Box;
        if (IsInternalTableBox(box, includeCells: true))
            return LayoutResolution.ComputedValue;

        var specified = style.Layout.Box.Margin.Get(side);
        if (box.IsInlineBox)
        {
            // The margins of an inline box are only applied to its first and last fragments, and the margins in the block axis
            // are not applied at all: they are resolved from the computed values
            margin = specified.Resolve(GetInlineBoxPercentageBasis(fragment));
        }
        else if (!TryGetInFlowInlineMargin(fragment, side, specified, out margin))
        {
            // The margins trimmed by margin-trim are not changed (https://drafts.csswg.org/css-box-4/#margin-trim)
            margin = (fragment.UntrimmedMargin ?? fragment.UnfragmentedFragment?.UntrimmedMargin ?? fragment.Margin).Get(side);
        }

        margin /= style.Layout.EffectiveZoom;
        return LayoutResolution.UsedValue;
    }

    /// <summary>
    /// Returns the used padding of a side (https://drafts.csswg.org/css-box-4/#paddings): the percentages are resolved against the
    /// inline size of the containing block. The paddings do not apply to the internal table boxes other than the cells.
    /// </summary>
    public static LayoutResolution GetPadding(ComputedStyle style, PhysicalSide side, out double padding)
    {
        padding = 0;
        var resolution = GetFirstFragment(style, out var fragment);
        if (resolution != LayoutResolution.UsedValue)
            return resolution;

        var box = fragment!.Box;
        if (IsInternalTableBox(box, includeCells: false))
            return LayoutResolution.ComputedValue;

        padding = box.IsInlineBox
            ? Math.Max(0, style.Layout.Box.Padding.Get(side).Resolve(GetInlineBoxPercentageBasis(fragment)))
            : GetTableBox(fragment).Padding.Get(side);
        padding /= style.Layout.EffectiveZoom;
        return LayoutResolution.UsedValue;
    }

    /// <summary>
    /// Returns the resolved value of an inset of a positioned box (see <see cref="BoxFragment.ResolvedInsets"/>): the used value
    /// in px, or null for an <c>auto</c> inset of a sticky positioned box.
    /// </summary>
    public static LayoutResolution GetInset(ComputedStyle style, PhysicalSide side, out double? inset)
    {
        inset = null;
        if (GetLayoutEngine(style) is not { } engine)
            return LayoutResolution.NoLayout;

        foreach (var fragment in engine.GetFragments(style.Element, style.PseudoElement))
        {
            if (fragment.ResolvedInsets is not { } insets)
                continue;

            inset = insets.Get(side) / style.Layout.EffectiveZoom;
            return LayoutResolution.UsedValue;
        }

        return LayoutResolution.ComputedValue;
    }

    /// <summary>
    /// Returns the size of the reference box of the transforms (https://drafts.csswg.org/css-transforms-1/#transform-box, see
    /// <see cref="LayoutTransforms.GetReferenceBox"/>), which the percentages of <c>transform-origin</c>, <c>perspective-origin</c>
    /// and of the translations refer to. The non-replaced inline boxes are not transformable.
    /// </summary>
    public static LayoutResolution GetTransformReferenceBox(ComputedStyle style, out PhysicalSize size)
    {
        size = default;
        if (style.PseudoElement is null && style.Element is SvgElement { ParentNode: SvgElement } svgElement)
            return GetSvgTransformReferenceBox(style, svgElement, out size);

        var resolution = GetFirstFragment(style, out var fragment);
        if (resolution != LayoutResolution.UsedValue)
            return resolution;

        if (fragment!.Box.IsInlineBox)
            return LayoutResolution.ComputedValue;

        var zoom = style.Layout.EffectiveZoom;
        var rect = LayoutTransforms.GetReferenceBox(GetTableBox(fragment));
        size = new PhysicalSize(rect.Width / zoom, rect.Height / zoom);
        return LayoutResolution.UsedValue;
    }

    /// <summary>
    /// Returns the size of the reference box of the transforms of an SVG element that has no CSS box (see
    /// <see cref="SvgBoundingBox.GetTransformReferenceBox"/>): the viewport of the outermost svg element is its content box.
    /// </summary>
    private static LayoutResolution GetSvgTransformReferenceBox(ComputedStyle style, SvgElement element, out PhysicalSize size)
    {
        size = default;
        if (GetLayoutEngine(style) is not { } engine)
            return LayoutResolution.NoLayout;

        Element outermost = element;
        while (outermost.ParentNode is SvgElement parent)
        {
            outermost = parent;
        }

        if (outermost is not SvgSvgElement || engine.GetFragments(outermost) is not [var fragment, ..] || BoxGeneration.Get(style).IsDisplayNone)
            return LayoutResolution.ComputedValue;

        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        var content = fragment.ContentBoxRect;
        var (width, height) = SvgBoundingBox.GetTransformReferenceBox(element, style.GetComputedValue("transform-box"), new SvgOutermostViewport(content.Width / zoom, content.Height / zoom, zoom));
        size = new PhysicalSize(width, height);
        return LayoutResolution.UsedValue;
    }

    /// <summary>
    /// Returns the used track sizes of a grid container (https://drafts.csswg.org/css-grid-2/#resolved-track-list), serialized:
    /// null when the grid layout did not record them (see <see cref="LayoutContext.OnGridLaidOut"/>). The resolved value of an
    /// element that is not a grid container is the computed value.
    /// </summary>
    public static LayoutResolution GetGridTrackList(ComputedStyle style, bool columns, out string? value)
    {
        value = null;
        var resolution = GetFirstFragment(style, out var fragment);
        if (resolution != LayoutResolution.UsedValue)
            return resolution;

        var box = fragment!.Box;
        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || !box.Display.IsGridContainer)
            return LayoutResolution.ComputedValue;

        if (GetLayoutEngine(style) is { } engine && engine.TryGetGridTrackSizes(box, out var columnTracks, out var rowTracks))
        {
            var tracks = columns ? columnTracks : rowTracks;
            value = ReferenceEquals(tracks, GridTrackSizes.StackingAxis) ? null : tracks.Serialize(style.Layout.EffectiveZoom);
        }

        return LayoutResolution.UsedValue;
    }

    /// <summary>Returns the layout of the document of the element, or null when there is none (no browsing context, or the element is not connected).</summary>
    private static LayoutEngine? GetLayoutEngine(ComputedStyle style)
    {
        var element = style.Element;
        var document = element.NodeDocument;
        return document.BrowsingContext is not null && element.IsConnected ? document.GetLayoutEngine() : null;
    }

    /// <summary>
    /// Returns the style an anchor-positioned box was laid out with (https://drafts.csswg.org/css-anchor-position-1/: its position
    /// option and its anchor functions resolved to lengths, see <see cref="AnchorPositioning"/>), or null when the box was laid out
    /// with its computed style (or when there is no layout). The computed values of the accepted @position-try properties are the
    /// values of this style ("The resulting styles are applied to the element via interleaving, so they affect computed values").
    /// </summary>
    public static ComputedStyle? GetAnchorPositionedStyle(ComputedStyle style)
    {
        if (GetFirstFragment(style, out var fragment) != LayoutResolution.UsedValue)
            return null;

        return fragment!.Box.Style.PositionBaseStyle is not null ? fragment.Box.Style : null;
    }

    /// <summary>Returns the first fragment of the box of the element or pseudo-element of a style (a forced layout), or the box before its fragmentation.</summary>
    private static LayoutResolution GetFirstFragment(ComputedStyle style, out BoxFragment? fragment)
    {
        fragment = null;
        if (GetLayoutEngine(style) is not { } engine)
            return LayoutResolution.NoLayout;

        var fragments = engine.GetFragments(style.Element, style.PseudoElement);
        if (fragments.Count == 0)
            return LayoutResolution.ComputedValue;

        // The size and the edges of a fragmented box are the ones of the whole box
        fragment = fragments[0].UnfragmentedFragment ?? fragments[0];
        return LayoutResolution.UsedValue;
    }

    /// <summary>Returns the fragment of the table box of a table wrapper box (the box the sizes, the borders and the paddings of the table apply to), or the fragment itself.</summary>
    private static BoxFragment GetTableBox(BoxFragment fragment)
    {
        if (fragment.Box.Kind == ContainerBoxKind.TableWrapper)
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is BoxFragment { Box.Kind: ContainerBoxKind.TableGrid } table)
                    return table;
            }
        }

        return fragment;
    }

    /// <summary>
    /// Returns whether a box is an internal table box (https://drafts.csswg.org/css-tables-3/#internal-table-box): the margins
    /// do not apply to them, and the paddings do not apply to them except the cells.
    /// </summary>
    private static bool IsInternalTableBox(ContainerBox box, bool includeCells)
        => box.Kind is not (ContainerBoxKind.TableWrapper or ContainerBoxKind.TableGrid) && box.Display.Internal switch
        {
            DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup or DisplayInternal.TableRow
                or DisplayInternal.TableColumnGroup or DisplayInternal.TableColumn => true,
            DisplayInternal.TableCell => includeCells,
            _ => false,
        };

    /// <summary>
    /// Returns the percentage basis of the margins and the paddings of an inline box: the inline size of the content box of its
    /// containing block, the nearest block container ancestor (the inline formatting context resolves them the same way).
    /// </summary>
    private static double GetInlineBoxPercentageBasis(BoxFragment fragment)
    {
        for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BoxFragment { Box.IsInlineBox: false } container)
            {
                var content = container.ContentBoxRect;
                return container.WritingDirection.IsHorizontal ? content.Width : content.Height;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns the used value of a margin of an in-flow block-level box in the inline axis of its block container (see
    /// <see cref="GetMargin"/>): an <c>auto</c> margin is the distance between the border box (without its relative and sticky
    /// offsets) and the edge of the content box of the parent fragment; the other margins are their computed values, the
    /// percentages resolved against the inline size of that content box (the layout computes the inline-end margin of an
    /// over-constrained box from the other values: https://drafts.csswg.org/css2/#blockwidth). Returns false for the other
    /// boxes and sides.
    /// </summary>
    private static bool TryGetInFlowInlineMargin(BoxFragment fragment, PhysicalSide side, LengthPercentageAuto specified, out double margin)
    {
        margin = 0;
        var box = fragment.Box;
        if (!box.IsInFlow || !box.IsBlockLevel || fragment.Parent is not BoxFragment parent || !parent.Box.IsBlockContainer)
            return false;

        var horizontal = side is PhysicalSide.Left or PhysicalSide.Right;
        if (parent.WritingDirection.IsHorizontal != horizontal)
            return false;

        var content = parent.ContentBoxRect;
        if (!specified.IsAuto)
        {
            margin = specified.Value.Resolve(horizontal ? content.Width : content.Height);
            return true;
        }

        var offset = fragment.Offset - fragment.RelativeOffset - fragment.StickyOffset;
        margin = side switch
        {
            PhysicalSide.Left => offset.X - content.Left,
            PhysicalSide.Right => content.Right - (offset.X + fragment.Size.Width),
            PhysicalSide.Top => offset.Y - content.Top,
            _ => content.Bottom - (offset.Y + fragment.Size.Height),
        };

        return true;
    }
}
