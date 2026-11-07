using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The sizes observed by the resize observers (https://drafts.csswg.org/resize-observer-1/#calculate-box-size-h), read from the
/// fragments of the last layout (the layout of the document of the target is updated when it is out of date).
/// </summary>
/// <remarks>
/// <para>
/// The sizes are in CSS pixels of the element (the fragments are divided by its effective zoom), in the inline and block axes
/// of its writing mode, one per box fragment of its principal box (e.g. one per column in a multi-column container,
/// https://github.com/w3c/csswg-drafts/issues/3673). The content box excludes the scrollbars. An element without a box
/// (<c>display: none</c>, disconnected, in a document without a browsing context) and a non-replaced inline box have a single
/// 0×0 size, except the SVG graphics elements without a CSS box, whose size is their bounding box
/// (<see cref="SvgBoundingBox"/>, without the stroke).
/// </para>
/// <para>
/// The device pixel content box is the content box in the device pixels of <see cref="ViewportOptions.DevicePixelRatio"/>,
/// snapped as the browsers do: its edges are rounded to device pixels from their position in the document (before the
/// transforms), so its size is an integer. It is not divided by the zoom.
/// </para>
/// </remarks>
internal static class ResizeObservations
{
    /// <summary>
    /// https://drafts.csswg.org/resize-observer-1/#calculate-depth-for-node-h: the number of nodes of the parent-traversal path
    /// from the node to the root of its flat tree.
    /// </summary>
    public static int CalculateDepth(Node node)
    {
        var depth = 0;
        for (Node? current = node; current is not null; current = HtmlElement.GetFlatTreeParent(current))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Returns whether an element is in the skipped contents of an element (e.g. <c>content-visibility: hidden</c>): it is not
    /// rendered, so its observations are not active until it is rendered again.
    /// </summary>
    public static bool IsInSkippedContents(Element element)
        => element.IsConnected && element.NodeDocument.BrowsingContext is not null && element.NodeDocument.GetStyleEngine().MayHaveContentVisibility() && ContentRelevancy.IsInSkippedContents(element);

    /// <summary>https://drafts.csswg.org/resize-observer-1/#calculate-box-size-h: the sizes of the observed box of an element, one per fragment.</summary>
    public static IReadOnlyList<ResizeObserverSize> CalculateBoxSize(Element target, ResizeObserverBoxOptions observedBox)
    {
        var measurement = Measure(target);
        return measurement.Fragments is { } fragments ? GetSizes(measurement, fragments, observedBox) : GetSvgSizes(measurement, observedBox);
    }

    /// <summary>https://drafts.csswg.org/resize-observer-1/#create-and-populate-resizeobserverentry-h</summary>
    public static ResizeObserverEntry CreateEntry(Element target)
    {
        var measurement = Measure(target);
        if (measurement.Fragments is not { } fragments)
        {
            // The SVG elements without a CSS box: their bounding box, at (0, 0)
            var contentBoxSize = GetSvgSizes(measurement, ResizeObserverBoxOptions.ContentBox);
            var rect = measurement.SvgBoundingBox;
            return new ResizeObserverEntry(target, new DomRectReadOnly(0, 0, rect.Width, rect.Height), contentBoxSize, contentBoxSize, GetSvgSizes(measurement, ResizeObserverBoxOptions.DevicePixelContentBox));
        }

        // "logical this.contentBoxSize": the content box of the first fragment, at the position of its padding top and left
        var contentRect = new DomRectReadOnly();
        if (fragments.Count > 0)
        {
            var first = fragments[0];
            var zoom = measurement.Zoom;
            var content = first.ContentBoxRect.Size;
            contentRect = new DomRectReadOnly(first.Padding.Left / zoom, first.Padding.Top / zoom, content.Width / zoom, content.Height / zoom);
        }

        return new ResizeObserverEntry(target, contentRect, GetSizes(measurement, fragments, ResizeObserverBoxOptions.BorderBox), GetSizes(measurement, fragments, ResizeObserverBoxOptions.ContentBox), GetSizes(measurement, fragments, ResizeObserverBoxOptions.DevicePixelContentBox));
    }

    private static Measurement Measure(Element target)
    {
        var document = target.NodeDocument;
        if (!target.IsConnected || document.BrowsingContext is null)
            return new Measurement([], Zoom: 1, Horizontal: true, DevicePixelRatio: 1, default);

        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        var devicePixelRatio = document.GetStyleEngine().Environment.DevicePixelRatio;
        var box = engine.BoxTree?.GetPrincipalBox(target);

        // "non-replaced inline Elements will always have an empty content rect"
        if (box is { IsInlineBox: true })
            return new Measurement([], Zoom: 1, Horizontal: true, devicePixelRatio, default);

        IReadOnlyList<BoxFragment> fragments = box is null ? [] : engine.GetFragments(target);
        if (fragments.Count == 0)
        {
            // https://drafts.csswg.org/resize-observer-1/#content-rect-h: the SVG elements that have no CSS layout box use their
            // bounding box (the foreignObject boxes are not laid out by the CSS layout yet: they use their bounding box too)
            if (target is SvgGraphicsElement svg && document.GetStyleEngine().GetStyle(target) is { } svgStyle)
            {
                var rect = SvgBoundingBox.Compute(svg, fill: true, stroke: false);
                return new Measurement(Fragments: null, svgStyle.Layout.EffectiveZoom, svgStyle.Layout.IsHorizontalWritingMode, devicePixelRatio, (rect.Width, rect.Height));
            }

            return new Measurement([], Zoom: 1, Horizontal: true, devicePixelRatio, default);
        }

        var layout = box!.Style.Layout;
        return new Measurement(fragments, layout.EffectiveZoom, layout.IsHorizontalWritingMode, devicePixelRatio, default);
    }

    private static ResizeObserverSize[] GetSizes(Measurement measurement, IReadOnlyList<BoxFragment> fragments, ResizeObserverBoxOptions observedBox)
    {
        // Each entry has its own arrays (the frozen arrays of the entries are distinct objects)
        if (fragments.Count == 0)
            return [new ResizeObserverSize(0, 0)];

        var result = new ResizeObserverSize[fragments.Count];
        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            double width;
            double height;
            switch (observedBox)
            {
                case ResizeObserverBoxOptions.BorderBox:
                    width = fragment.Size.Width / measurement.Zoom;
                    height = fragment.Size.Height / measurement.Zoom;
                    break;

                case ResizeObserverBoxOptions.DevicePixelContentBox:
                    // The edges of the content box are snapped to the device pixels (the fragments are in zoomed pixels)
                    var contentBox = fragment.ContentBoxRect;
                    var origin = fragment.GetOffsetInRoot();
                    var ratio = measurement.DevicePixelRatio;
                    var left = origin.X + contentBox.X;
                    var top = origin.Y + contentBox.Y;
                    width = Snap((left + contentBox.Width) * ratio) - Snap(left * ratio);
                    height = Snap((top + contentBox.Height) * ratio) - Snap(top * ratio);
                    break;

                default:
                    var size = fragment.ContentBoxRect.Size;
                    width = size.Width / measurement.Zoom;
                    height = size.Height / measurement.Zoom;
                    break;
            }

            result[i] = measurement.Horizontal ? new ResizeObserverSize(width, height) : new ResizeObserverSize(height, width);
        }

        return result;
    }

    private static ResizeObserverSize[] GetSvgSizes(Measurement measurement, ResizeObserverBoxOptions observedBox)
    {
        var width = measurement.SvgBoundingBox.Width;
        var height = measurement.SvgBoundingBox.Height;
        if (observedBox == ResizeObserverBoxOptions.DevicePixelContentBox)
        {
            // The bounding box is in the user units of the element, which are zoomed in the device pixels
            var ratio = measurement.Zoom * measurement.DevicePixelRatio;
            width = Snap(width * ratio);
            height = Snap(height * ratio);
        }

        return [measurement.Horizontal ? new ResizeObserverSize(width, height) : new ResizeObserverSize(height, width)];
    }

    /// <summary>Rounds a coordinate in device pixels to the nearest pixel edge (the halves are rounded up, as the pixel snapping of the browsers).</summary>
    private static double Snap(double value) => Math.Floor(value + 0.5);

    /// <summary>The box fragments of an element (null for an SVG element measured by its bounding box), with the values that convert their sizes (the effective zoom of the element).</summary>
    private readonly record struct Measurement(IReadOnlyList<BoxFragment>? Fragments, double Zoom, bool Horizontal, double DevicePixelRatio, (double Width, double Height) SvgBoundingBox);
}
