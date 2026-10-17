using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The viewports of the document (https://drafts.csswg.org/cssom-view/#viewport): the size of the viewports of the nested
// browsing contexts, laid out in this document, and the visual viewport of this document.
internal sealed partial class LayoutEngine
{
    /// <summary>The last size returned by <see cref="GetNestedViewportSize"/> for each container, used while this document is laid out.</summary>
    private Dictionary<Element, (double Width, double Height)>? _nestedViewportSizes;

    /// <summary>
    /// The size of the initial containing block computed by the last layout: the viewport without its scrollbars
    /// (https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing), the <c>clientWidth</c> and <c>clientHeight</c> of the root
    /// element and the size of the visual viewport. It does not compute the layout.
    /// </summary>
    public PhysicalSize InitialContainingBlockSize => _initialContainingBlockSize;

    /// <summary>
    /// Returns the size of the viewport of a nested browsing context whose container (an <c>iframe</c>, <c>frame</c>,
    /// <c>object</c> or <c>embed</c> element) is laid out in this document: the content box of its principal box
    /// (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-iframe-element: "the nested navigable's viewport
    /// [...] is the content box of the element"), or the concrete object size given by <c>object-fit</c> when the document has
    /// natural dimensions (the SVG document of an <c>object</c> or <c>embed</c> element, https://drafts.csswg.org/css-images-3/#the-object-fit),
    /// in the CSS pixels of the container (the effective zoom of the container scales
    /// the device pixels of the nested document instead, see <c>StyleEngine.CreateEnvironment</c>). The layout of this document
    /// is computed when it is out of date (as in the browsers, the layout of the parent document is updated before the styles
    /// of a nested document), without registering it for the rendering steps.
    /// </summary>
    /// <returns>The size, or null when the container does not generate a laid out box (the caller falls back to its computed size).</returns>
    public (double Width, double Height)? GetNestedViewportSize(Element container)
    {
        // A nested document read by the layout of this document (e.g. the natural size of an SVG document of an object element)
        // sees the size of the last layout
        if (_inLayout)
            return _nestedViewportSizes is not null && _nestedViewportSizes.TryGetValue(container, out var last) ? last : null;

        UpdateLayout();
        if (_boxTree?.GetPrincipalBox(container) is not { } box || GetFirstFragment(box) is not BoxFragment fragment)
        {
            _nestedViewportSizes?.Remove(container);
            return null;
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets: the content navigable of a frame element
        // that does not have a frameset parent "is expected to have a viewport with zero width and zero height"
        var content = fragment.ReplacedContentRect ?? fragment.ContentBoxRect;
        var zoom = box.Style.Layout.EffectiveZoom;
        var size = container is HtmlFrameElement && !FramesetFormattingContext.IsFramesetChild(container) ? (0, 0) : (Math.Max(0, content.Width / zoom), Math.Max(0, content.Height / zoom));
        (_nestedViewportSizes ??= new(ReferenceEqualityComparer.Instance))[container] = size;
        return size;
    }
}
