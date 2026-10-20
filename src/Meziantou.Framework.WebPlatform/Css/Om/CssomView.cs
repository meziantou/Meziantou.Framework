using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

// CSSOM View (https://drafts.csswg.org/cssom-view/): the geometry of the boxes (getClientRects(), getBoundingClientRect()...)
// and the hit testing (elementFromPoint(), caretPositionFromPoint()...) read the fragment tree of the layout (see
// LayoutEngine and docs/layout.md). Reading them computes the layout when it is out of date (a forced layout).

/// <summary>Helpers of the geometry members of CSSOM View, from the fragment tree of the layout.</summary>
internal static class CssomViewGeometry
{
    /// <summary>
    /// Returns the layout of the document of an element that is being rendered (https://drafts.csswg.org/cssom-view/#css-layout-box),
    /// or null when it is not (not connected, display: none, no browsing context).
    /// </summary>
    public static LayoutEngine? GetLayoutEngine(Element element)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is null || !element.IsConnected || !ComputedStyleDeclarations.IsBeingRendered(element))
            return null;

        return document.GetLayoutEngine();
    }

    /// <summary>
    /// Returns the box fragments of an element for <c>getClientRects()</c> (https://drafts.csswg.org/cssom-view/#dom-element-getclientrects):
    /// the fragments of its principal box, in content order. The specification gives the table box and the caption boxes of a
    /// table; as the browsers, the table wrapper box is used (its border box contains the captions and their margins, WPT
    /// css/css-tables/caption-orthogonal-margin-in-table-block-axis.html).
    /// </summary>
    public static List<BoxFragment> GetClientFragments(LayoutEngine engine, Element element) => [.. engine.GetFragmentsWithSplitBlocks(element)];

    /// <summary>
    /// Returns the quads of the boxes of an element in the coordinates of the viewport
    /// (https://drafts.csswg.org/cssom-view/#dom-element-getclientrects): the border boxes of its fragments, or the bounding box
    /// of an element of an SVG document fragment, with the transforms that apply to the element and its ancestors. The list
    /// is empty when the element has no box.
    /// </summary>
    public static List<PhysicalQuad> GetClientQuads(Element element)
    {
        var result = new List<PhysicalQuad>();
        if (GetLayoutEngine(element) is not { } engine)
            return result;

        if (element is SvgElement svg && GetOutermostSvgElement(svg) is { } outermost)
        {
            // "If the element has an associated SVG layout box return [...] the bounding box of the element as defined by the SVG
            // specification, applying the transforms that apply to the element and its ancestors"
            var fragments = engine.GetFragments(outermost);
            if (fragments.Count > 0)
            {
                var content = fragments[0].ContentBoxRect;
                if (SvgBoundingBox.GetBoundingQuad(svg, SvgUserSpace.GetOutermostViewport(fragments[0])) is { } quad)
                {
                    result.Add(engine.ToViewport(MapToRoot(fragments[0], quad.Translate(content.Offset))));
                }
            }

            return result;
        }

        foreach (var fragment in GetClientFragments(engine, element))
        {
            result.Add(engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.BorderBoxRect)));
        }

        return result;
    }

    /// <summary>
    /// Adds the quads of the characters of a text of an SVG text element (https://drafts.csswg.org/cssom-view/#dom-range-getclientrects):
    /// the glyph cells of its addressable characters in the range (see <see cref="SvgTextLayout"/>), with the transforms.
    /// </summary>
    private static void AddSvgTextQuads(LayoutEngine engine, Text text, SvgElement parent, int start, int end, List<PhysicalQuad> result)
    {
        if (SvgUserSpace.GetOutermostSvgElement(parent) is not { } outermost || engine.GetFragments(outermost) is not [var fragment, ..] || SvgTextLayout.Get(parent) is not { } layout)
            return;

        var viewport = SvgUserSpace.GetOutermostViewport(fragment);
        if (SvgUserSpace.GetTransformToOutermostContentBox(layout.Root, out _, viewport) is not { } toContent)
            return;

        var toViewport = toContent.Then(SvgUserSpace.GetContentBoxToViewportTransform(engine, fragment));
        foreach (var character in layout.Characters)
        {
            if (!ReferenceEquals(character.TextNode, text) || !character.IsClusterStart || character.IsHidden || character.TextOffset < start || character.TextOffset >= Math.Max(end, start + 1))
                continue;

            var (p1, p2, p3, p4) = character.GetCell();
            if (start == end)
            {
                // A collapsed range: the start edge of the character
                p2 = p1;
                p3 = p4;
            }

            result.Add(new PhysicalQuad(ToOffset(toViewport.Apply(p1)), ToOffset(toViewport.Apply(p2)), ToOffset(toViewport.Apply(p3)), ToOffset(toViewport.Apply(p4))));
            if (start == end)
                return;
        }

        static PhysicalOffset ToOffset(SvgPathData.SvgPoint point) => new(point.X, point.Y);
    }

    /// <summary>Maps a quad in the coordinates of a fragment to the coordinates of the root of the fragment tree (see <see cref="LayoutEngine.MapToRoot(Fragment, PhysicalOffset)"/>).</summary>
    public static PhysicalQuad MapToRoot(Fragment fragment, PhysicalQuad quad)
        => new(LayoutEngine.MapToRoot(fragment, quad.P1), LayoutEngine.MapToRoot(fragment, quad.P2), LayoutEngine.MapToRoot(fragment, quad.P3), LayoutEngine.MapToRoot(fragment, quad.P4));

    /// <summary>
    /// Returns the outermost svg element of an element of an SVG document fragment (https://svgwg.org/svg2-draft/struct.html#TermOutermostSVGElement),
    /// or null for an outermost svg element (it has a CSS box) and an element that is not in an svg element.
    /// </summary>
    public static SvgSvgElement? GetOutermostSvgElement(SvgElement element)
    {
        SvgSvgElement? outermost = null;
        for (var ancestor = element.ParentNode; ancestor is SvgElement svgAncestor; ancestor = ancestor.ParentNode)
        {
            if (svgAncestor is SvgSvgElement svg)
            {
                outermost = svg;
            }

            // An svg element in a foreignObject is an outermost svg element
            if (svgAncestor.LocalName == "foreignObject")
                break;
        }

        return outermost;
    }

    /// <summary>
    /// Returns the bounding rectangle of rectangles (https://drafts.csswg.org/cssom-view/#dom-element-getboundingclientrect): a
    /// zero rectangle for an empty list, the first rectangle when all of them have a zero width or height, else the smallest
    /// rectangle that contains the ones whose width and height are not zero.
    /// </summary>
    public static DomRect GetBoundingRect(List<PhysicalRect> rects)
    {
        if (rects.Count == 0)
            return new DomRect();

        var result = rects[0];
        for (var i = 1; i < rects.Count; i++)
        {
            result = result.Union(rects[i]);
        }

        return ToDomRect(result);
    }

    public static DomRect ToDomRect(PhysicalRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>Returns the first box of an element (https://drafts.csswg.org/cssom-view/#first-box): its first fragment, the table box of a table, or null.</summary>
    public static BoxFragment? GetFirstBox(LayoutEngine engine, Element element)
    {
        var fragments = engine.GetFragments(element);
        if (fragments.Count == 0)
            return null;

        var fragment = fragments[0];
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
    /// Returns the position of a fragment relative to the origin of the initial containing block, ignoring the transforms and
    /// the scroll positions (the offsets of <c>offsetTop</c>/<c>offsetLeft</c>): the relative and sticky offsets are included,
    /// the fixed positioned boxes of the viewport are relative to the viewport.
    /// </summary>
    public static PhysicalOffset GetPositionInInitialContainingBlock(Fragment fragment)
    {
        var position = PhysicalOffset.Zero;
        for (var current = fragment; current.Parent is not null; current = current.Parent)
        {
            position += current.Offset;
        }

        return position;
    }

    /// <summary>Rounds a position to an integer as the browsers do (to the nearest integer, the halves upwards).</summary>
    public static int RoundPosition(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>
    /// Appends the quads of the characters of a text node from <paramref name="start"/> to <paramref name="end"/> in the
    /// coordinates of the viewport (https://drafts.csswg.org/cssom-view/#dom-range-getclientrects): one per text fragment, its
    /// advance along the inline axis and the ascent and descent of its font along the block axis. A collapsed range gives a
    /// quad without width (in a horizontal writing mode) at the caret position.
    /// </summary>
    public static void AddTextQuads(LayoutEngine engine, Text text, int start, int end, List<PhysicalQuad> result)
    {
        if (text.ParentNode is SvgElement svgParent)
        {
            AddSvgTextQuads(engine, text, svgParent, start, end, result);
            return;
        }

        foreach (var fragment in engine.GetTextFragments(text))
        {
            if (fragment.IsHyphen)
            {
                // The hyphen of a line broken after a soft hyphen is part of the soft hyphen
                if (start < fragment.Start && end >= fragment.Start)
                {
                    result.Add(engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.LocalRect)));
                }

                continue;
            }

            if (start == end)
            {
                if (start < fragment.Start || start > fragment.End)
                    continue;

                result.Add(engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.GetRangeRect(start, start))));
                return;
            }

            if (end <= fragment.Start || start >= fragment.End)
            {
                // An empty fragment (e.g. a line of collapsed white space) in the range
                if (fragment.Start != fragment.End || fragment.Start < start || fragment.Start >= end)
                    continue;
            }

            result.Add(engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.GetRangeRect(Math.Max(start, fragment.Start), Math.Min(end, fragment.End)))));
        }
    }

    /// <summary>Returns the topmost text fragment or box fragment at a point of the viewport, or null (see <see cref="HitTesting"/>).</summary>
    public static HitTestResult? HitTest(Document document, double x, double y)
    {
        if (!IsInViewport(document, x, y))
            return null;

        return HitTesting.HitTest(document.GetLayoutEngine(), new PhysicalOffset(x, y));
    }

    /// <summary>
    /// Whether a point is in the viewport of a document that has one: "If either argument is negative, x is greater than the
    /// viewport width excluding the size of a rendered scroll bar (if any), or y is greater than the viewport height excluding
    /// the size of a rendered scroll bar (if any), or there is no viewport associated with the document".
    /// </summary>
    public static bool IsInViewport(Document document, double x, double y)
    {
        if (document.BrowsingContext is null || !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0)
            return false;

        var engine = document.GetLayoutEngine();
        var size = engine.EnsureLayout() is { } root ? root.Size : new PhysicalSize(StyleEngine.CreateEnvironment(document).Width, StyleEngine.CreateEnvironment(document).Height);
        return x <= size.Width && y <= size.Height;
    }
}

/// <summary>The coordinates of the mouse events relative to the boxes (https://drafts.csswg.org/cssom-view/#extensions-to-the-mouseevent-interface).</summary>
internal static class MouseEventGeometry
{
    /// <summary>
    /// Returns the coordinates of the pointer relative to the padding edge of the first box of the target of an event
    /// (https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsetx), divided by its effective zoom, or null when the target has
    /// no box. The point is mapped through the transforms, as in the browsers.
    /// The target of an element of an SVG document fragment is its outermost svg element.
    /// </summary>
    public static (double X, double Y)? GetOffsetPosition(MouseEvent @event, Element target)
    {
        var element = target is SvgElement svg && CssomViewGeometry.GetOutermostSvgElement(svg) is { } outermost ? outermost : target;
        if (CssomViewGeometry.GetLayoutEngine(element) is not { } engine || CssomViewGeometry.GetFirstBox(engine, element) is not { } fragment)
            return null;

        if (!LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(new PhysicalOffset(@event.ClientX, @event.ClientY)), out var point))
            return null;

        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        return ((point.X - fragment.Border.Left - fragment.Scrollbars.Left) / zoom, (point.Y - fragment.Border.Top - fragment.Scrollbars.Top) / zoom);
    }

    /// <summary>
    /// Returns the coordinate of the pointer relative to the border box of the nearest positioned inclusive ancestor of the
    /// target of an event (https://w3c.github.io/uievents/#dom-mouseevent-layerx), in the coordinates of the page, or null when
    /// there is no such ancestor.
    /// </summary>
    public static (double X, double Y)? GetLayerPosition(MouseEvent @event, Node target)
    {
        for (var node = target; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (node is not Element element || CssomViewGeometry.GetLayoutEngine(element) is not { } engine)
                continue;

            if (!CssomViewHelpers.GetStyle(element).Layout.IsPositioned || CssomViewGeometry.GetFirstBox(engine, element) is not { } fragment)
                continue;

            var position = fragment.GetOffsetInRoot();
            return (@event.PageX - position.X, @event.PageY - position.Y);
        }

        return null;
    }
}

public partial class Element
{
    /// <summary>
    /// Returns a <see cref="ContainerQueryList"/> that evaluates a comma-separated list of container conditions for the
    /// element (https://github.com/w3c/csswg-drafts/pull/13551, a tentative extension of CSS Conditional 5).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-element-matchcontainer"/>
    [IdlMember("matchContainer", "https://drafts.csswg.org/css-conditional-5/#dom-element-matchcontainer")]
    [Experimental(ExperimentalApis.MatchContainer, UrlFormat = ExperimentalApis.UrlFormat)]
    public ContainerQueryList MatchContainer(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ContainerQueryList(this, query);
    }

    /// <summary>
    /// Returns the boxes of the element (https://drafts.csswg.org/cssom-view/#dom-element-getclientrects), in the coordinates of
    /// the viewport: the border box of each fragment of its principal box (e.g. one per line for an inline box split over
    /// several lines; the table box and the caption boxes of a table), or the bounding box of an element of an SVG document
    /// fragment, with the transforms of the element and of its ancestors applied (the bounding box of the transformed box). The
    /// list is empty when the element has no box (it is not connected, it or one of its ancestors has <c>display: none</c>,
    /// <c>display: contents</c>, or its document has no browsing context).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-getclientrects"/>
    [IdlMember("getClientRects", "https://drafts.csswg.org/cssom-view/#dom-element-getclientrects")]
    public DomRectList GetClientRects()
    {
        var quads = CssomViewGeometry.GetClientQuads(this);
        var rects = new DomRect[quads.Count];
        for (var i = 0; i < rects.Length; i++)
        {
            rects[i] = CssomViewGeometry.ToDomRect(quads[i].Bounds);
        }

        return new DomRectList(rects);
    }

    /// <summary>
    /// Returns the bounding box of the boxes of the element (https://drafts.csswg.org/cssom-view/#dom-element-getboundingclientrect),
    /// in the coordinates of the viewport: the union of the rectangles of <see cref="GetClientRects"/> (ignoring the empty ones,
    /// unless they all are), a zero rectangle when the element has no box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-getboundingclientrect"/>
    [IdlMember("getBoundingClientRect", "https://drafts.csswg.org/cssom-view/#dom-element-getboundingclientrect")]
    public DomRect GetBoundingClientRect()
    {
        var quads = CssomViewGeometry.GetClientQuads(this);
        var rects = new List<PhysicalRect>(quads.Count);
        foreach (var quad in quads)
        {
            rects.Add(quad.Bounds);
        }

        return CssomViewGeometry.GetBoundingRect(rects);
    }
}

public sealed partial class ShadowRoot
{
    /// <summary>
    /// Returns the topmost element at a point of the viewport, retargeted against the shadow root: as <see cref="Document.ElementFromPoint(double, double)"/>,
    /// but the elements of the shadow tree (and of the shadow trees that contain it) are not replaced by their shadow hosts, so
    /// the result can be an element of the shadow tree, or an element outside of it (e.g. the shadow host, or an element of
    /// the document). The member was defined by the obsolete Shadow DOM specification and is implemented by the browsers, but
    /// CSSOM View only defines it for the documents (https://github.com/w3c/csswg-drafts/issues/556).
    /// </summary>
    /// <seealso href="https://www.w3.org/TR/2018/NOTE-shadow-dom-20180301/#extensions-to-the-documentorshadowroot-mixin"/>
    [IdlMember("elementFromPoint", "https://www.w3.org/TR/2018/NOTE-shadow-dom-20180301/#extensions-to-the-documentorshadowroot-mixin")]
    [Experimental(ExperimentalApis.ShadowRootExtensions, UrlFormat = ExperimentalApis.UrlFormat)]
    public Element? ElementFromPoint(double x, double y) => NodeDocument.ElementFromPoint(x, y, this);

    /// <summary>Returns the elements at a point of the viewport, topmost first, retargeted against the shadow root: see <see cref="ElementFromPoint"/> and <see cref="Document.ElementsFromPoint(double, double)"/>.</summary>
    /// <seealso href="https://www.w3.org/TR/2018/NOTE-shadow-dom-20180301/#extensions-to-the-documentorshadowroot-mixin"/>
    [IdlMember("elementsFromPoint", "https://www.w3.org/TR/2018/NOTE-shadow-dom-20180301/#extensions-to-the-documentorshadowroot-mixin")]
    [Experimental(ExperimentalApis.ShadowRootExtensions, UrlFormat = ExperimentalApis.UrlFormat)]
    public IReadOnlyList<Element> ElementsFromPoint(double x, double y) => NodeDocument.ElementsFromPoint(x, y, this);
}

public partial class Document
{
    /// <summary>
    /// Returns the topmost element at a point of the viewport (https://drafts.csswg.org/cssom-view/#dom-document-elementfrompoint):
    /// the element of the topmost box at the point (see <see cref="ElementsFromPoint(double, double)"/>), else the document element. Returns null
    /// when the point is outside of the viewport or the document has no browsing context.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-document-elementfrompoint"/>
    [IdlMember("elementFromPoint", "https://drafts.csswg.org/cssom-view/#dom-document-elementfrompoint")]
    public Element? ElementFromPoint(double x, double y) => ElementFromPoint(x, y, this);

    /// <summary>The topmost element at a point of the viewport, retargeted against the document or one of its shadow roots (see <see cref="ElementFromPoint(double, double)"/>).</summary>
    internal Element? ElementFromPoint(double x, double y, Node documentOrShadowRoot)
    {
        if (!CssomViewGeometry.IsInViewport(this, x, y))
            return null;

        var hit = HitTesting.HitTest(GetLayoutEngine(), new PhysicalOffset(x, y));
        if (hit is { } result && EventDispatcher.Retarget(GetParentOfHitSlottedText(result) ?? result.Element, documentOrShadowRoot) is Element retargeted)
            return retargeted;

        return DocumentElement;
    }

    /// <summary>
    /// The parent element of a hit text node that is assigned to a slot (the shadow host of the slot): as in the browsers, the
    /// element of a text is its parent in the tree, not its slot, which is in a shadow tree (a shadow root gets the shadow
    /// host for a text slotted in its tree, not the slot). Null for the other fragments.
    /// </summary>
    private static Element? GetParentOfHitSlottedText(HitTestResult hit) => hit is { Fragment: TextFragment { Run.Node.ParentNode: Element parent }, Element: HtmlSlotElement slot } && !ReferenceEquals(parent, slot)
        ? parent
        : null;

    /// <summary>
    /// Returns the elements at a point of the viewport, topmost first (https://drafts.csswg.org/cssom-view/#dom-document-elementsfrompoint):
    /// the elements of the boxes at the point in the reverse of the painting order (retargeted out of the shadow trees; the
    /// texts are not listed, as in the browsers), then the document element. A box is not hit when its <c>visibility</c> is not <c>visible</c>, its <c>pointer-events</c> is
    /// <c>none</c> or its element is inert, outside of the overflow clip edge of its ancestors, or outside of its border box (the
    /// hit testing, see <see cref="HitTesting"/>). The list is empty when the point is outside of the viewport or the document
    /// has no browsing context.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-document-elementsfrompoint"/>
    [IdlMember("elementsFromPoint", "https://drafts.csswg.org/cssom-view/#dom-document-elementsfrompoint")]
    public IReadOnlyList<Element> ElementsFromPoint(double x, double y) => ElementsFromPoint(x, y, this);

    /// <summary>The elements at a point of the viewport, retargeted against the document or one of its shadow roots (see <see cref="ElementsFromPoint(double, double)"/>).</summary>
    internal IReadOnlyList<Element> ElementsFromPoint(double x, double y, Node documentOrShadowRoot)
    {
        var result = new List<Element>();
        if (!CssomViewGeometry.IsInViewport(this, x, y))
            return result;

        var hits = new List<HitTestResult>();
        HitTesting.HitTest(GetLayoutEngine(), new PhysicalOffset(x, y), hits, all: true);
        foreach (var hit in hits)
        {
            // As in the browsers, the texts are not listed (only the boxes of the elements, unlike elementFromPoint(), which
            // returns the parent element of the topmost text), and the consecutive boxes of the same element are listed once;
            // an element whose boxes are not consecutive (e.g. its ::before box above another element) is listed again. The
            // parent of a text that generates no box (display: contents, e.g. a slot and its fallback content) is listed for
            // the text.
            var element = GetParentOfHitSlottedText(hit) ?? hit.Element;
            if (hit.Fragment is TextFragment && !CssomViewHelpers.GetStyle(element).Layout.Display.IsContents)
                continue;

            if (EventDispatcher.Retarget(element, documentOrShadowRoot) is Element retargeted && (result.Count == 0 || !ReferenceEquals(result[^1], retargeted)))
            {
                result.Add(retargeted);
            }
        }

        if (DocumentElement is { } root && (result.Count == 0 || !ReferenceEquals(result[^1], root)))
        {
            result.Add(root);
        }

        return result;
    }

    /// <summary>
    /// Returns the caret position at a point of the viewport (https://drafts.csswg.org/cssom-view/#dom-document-caretpositionfrompoint):
    /// the position in the text under the point (the closest boundary of the grapheme clusters), else in the closest text of the
    /// line of the element under the point, else before or after the replaced element under the point, else at the start of
    /// the element under the point. The positions in the shadow trees that are not given in the options are the positions of
    /// their shadow hosts. Returns null when the point is outside of the viewport or the document has no browsing context.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-document-caretpositionfrompoint"/>
    [IdlMember("caretPositionFromPoint", "https://drafts.csswg.org/cssom-view/#dom-document-caretpositionfrompoint")]
    public CaretPosition? CaretPositionFromPoint(double x, double y, CaretPositionFromPointOptions? options = null)
    {
        if (!CssomViewGeometry.IsInViewport(this, x, y))
            return null;

        if (CaretPositions.FromPoint(this, x, y) is not var (node, offset, textControlCaret))
            return null;

        var caretNode = node;

        // "If caretPositionFromPointOptions["shadowRoots"] does not contain the shadow root of the caret node [...], set the caret
        // node to the shadow host and the caret offset to the index of the shadow host in its parent"
        var shadowRoots = options?.ShadowRoots;
        while (node.GetRootNode() is ShadowRoot shadowRoot && (shadowRoots is null || !shadowRoots.Contains(shadowRoot)))
        {
            var host = shadowRoot.Host;
            if (host.ParentNode is not { } parent)
                return null;

            node = parent;
            offset = (uint)host.GetIndex();
        }

        // The caret rectangle of a text entry widget, when the position was not moved to a shadow host
        return new CaretPosition(node, offset, ReferenceEquals(node, caretNode) ? textControlCaret : null);
    }

    // The media query lists of the document, in creation order (https://drafts.csswg.org/cssom-view/#evaluate-media-queries-and-report-changes).
    // They are weak references: a media query list with event listeners is also kept alive by _mediaQueryListsWithListeners.
    private List<WeakReference<MediaQueryList>>? _mediaQueryLists;
    private List<MediaQueryList>? _mediaQueryListsWithListeners;

    internal void AddMediaQueryList(MediaQueryList mediaQueryList)
    {
        if (_mediaQueryLists is null)
        {
            _mediaQueryLists = [];
            EventLoop?.AddDocumentWithMediaQueryLists(this);
        }

        _mediaQueryLists.Add(new WeakReference<MediaQueryList>(mediaQueryList));
    }

    internal void KeepMediaQueryListAlive(MediaQueryList mediaQueryList) => (_mediaQueryListsWithListeners ??= []).Add(mediaQueryList);

    /// <summary>https://drafts.csswg.org/cssom-view/#evaluate-media-queries-and-report-changes</summary>
    internal void EvaluateMediaQueriesAndReportChanges()
    {
        if (_mediaQueryLists is null)
            return;

        // The matches states are updated before the events are fired, so that the state of every list is up to date in the
        // event listeners
        var changed = new List<(MediaQueryList List, bool Matches)>();
        _mediaQueryLists.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _mediaQueryLists)
        {
            if (!reference.TryGetTarget(out var mediaQueryList))
                continue;

            var matches = mediaQueryList.Matches;
            if (matches != mediaQueryList.LastMatches)
            {
                mediaQueryList.LastMatches = matches;
                changed.Add((mediaQueryList, matches));
            }
        }

        foreach (var (mediaQueryList, matches) in changed)
        {
            mediaQueryList.FireEvent(new MediaQueryListEvent("change", new MediaQueryListEventInit { Media = mediaQueryList.Media, Matches = matches })
            {
                RealmWindow = mediaQueryList.RelevantGlobal,
            });
        }
    }
}

public partial class Window
{
    private Screen? _screen;

    /// <summary>
    /// Returns a <see cref="MediaQueryList"/> that evaluates a media query list against the environment of the document
    /// (https://drafts.csswg.org/cssom-view/#dom-window-matchmedia).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-matchmedia"/>
    [IdlMember("matchMedia", "https://drafts.csswg.org/cssom-view/#dom-window-matchmedia")]
    public MediaQueryList MatchMedia(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new MediaQueryList(Document, query);
    }

    // The size and the device pixel ratio of the viewport when the resize steps last ran. It is only tracked once the window
    // has a resize event listener (the resize events of a window without listeners are not observable).
    private (double Width, double Height, double DevicePixelRatio)? _lastViewportSize;
    private bool _registeredForResizeSteps;

    /// <summary>
    /// Whether the client window of the top-level browsing context was just created by <c>window.open()</c>: a client
    /// window is created, then sized (https://drafts.csswg.org/cssom-view/#set-up-browsing-context-features), so the next
    /// resize steps fire a resize event, as in the browsers. It is reset by the task queued by <c>window.open()</c>.
    /// </summary>
    internal bool IsClientWindowBeingSized { get; set; }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        base.OnEventListenerAdded(listener);

        // https://html.spec.whatwg.org/multipage/dom.html#reveal: the pagereveal event is fired by the first rendering update of
        // the document; a rendering opportunity is only scheduled for it when the event is observed
        if (listener.Type == "pagereveal" && !Document.HasBeenRevealed && Document.IsFullyActive)
        {
            EventLoop.RequestReveal(Document);
        }

        if (listener.Type == "resize" && _lastViewportSize is null)
        {
            var viewport = StyleEngine.CreateEnvironment(Document);
            if (IsClientWindowBeingSized)
            {
                // The viewport of the new client window had no size
                _lastViewportSize = (0, 0, viewport.DevicePixelRatio);
                EventLoop.RequestRenderingUpdate();
            }
            else
            {
                _lastViewportSize = (viewport.Width, viewport.Height, viewport.DevicePixelRatio);
            }

            RegisterForResizeSteps();
        }
    }

    /// <summary>Registers the window for the resize steps of the rendering updates (see <see cref="RunResizeSteps"/>): the window or its visual viewport has resize event listeners.</summary>
    internal void RegisterForResizeSteps()
    {
        if (!_registeredForResizeSteps)
        {
            _registeredForResizeSteps = true;
            EventLoop.AddWindowWithResizeListeners(this);
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom-view/#run-the-resize-steps: fires a resize event at the window when the size of the
    /// viewport changed since the last time the steps ran (e.g. the size of the iframe of a nested browsing context), or its
    /// device pixel ratio (the zoom of the iframe, as the page zoom of the browsers), then at the visual viewport when its size
    /// changed (see <see cref="VisualViewport.RunResizeSteps"/>).
    /// </summary>
    internal void RunResizeSteps()
    {
        if (!Document.IsFullyActive)
            return;

        if (_lastViewportSize is { } last)
        {
            var viewport = StyleEngine.CreateEnvironment(Document);
            if (viewport.Width != last.Width || viewport.Height != last.Height || viewport.DevicePixelRatio != last.DevicePixelRatio)
            {
                _lastViewportSize = (viewport.Width, viewport.Height, viewport.DevicePixelRatio);
                FireEvent(new Event("resize"));
            }
        }

        Document.VisualViewportOrNull?.RunResizeSteps();
    }

    /// <summary>Gets the screen of the window (https://drafts.csswg.org/cssom-view/#dom-window-screen).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-screen"/>
    [IdlMember("screen", "https://drafts.csswg.org/cssom-view/#dom-window-screen")]
    public Screen Screen => _screen ??= new Screen(this);

    /// <summary>Gets the number of device pixels per CSS pixel (https://drafts.csswg.org/cssom-view/#dom-window-devicepixelratio): <see cref="ViewportOptions.DevicePixelRatio"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-devicepixelratio"/>
    [IdlMember("devicePixelRatio", "https://drafts.csswg.org/cssom-view/#dom-window-devicepixelratio")]
    public double DevicePixelRatio => StyleEngine.CreateEnvironment(Document).DevicePixelRatio;

    /// <summary>Gets the width of the viewport (https://drafts.csswg.org/cssom-view/#dom-window-innerwidth): <see cref="ViewportOptions.Width"/>, or the width of the iframe.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-innerwidth"/>
    [IdlMember("innerWidth", "https://drafts.csswg.org/cssom-view/#dom-window-innerwidth")]
    public int InnerWidth => (int)StyleEngine.CreateEnvironment(Document).Width;

    /// <summary>Gets the height of the viewport (https://drafts.csswg.org/cssom-view/#dom-window-innerheight): <see cref="ViewportOptions.Height"/>, or the height of the iframe.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-innerheight"/>
    [IdlMember("innerHeight", "https://drafts.csswg.org/cssom-view/#dom-window-innerheight")]
    public int InnerHeight => (int)StyleEngine.CreateEnvironment(Document).Height;
}

public sealed partial class Range
{
    /// <summary>
    /// Returns the boxes of the range (https://drafts.csswg.org/cssom-view/#dom-range-getclientrects), in the coordinates of the
    /// viewport: the boxes of the elements contained in the range whose parent is not (see <see cref="Element.GetClientRects"/>),
    /// and for each text node selected or partially selected, the rectangle of the selected characters of each of its text
    /// fragments (their advance along the inline axis, the ascent and descent of the font along the block axis; a collapsed
    /// range in a text gives a rectangle without width), with the transforms applied.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-range-getclientrects"/>
    [IdlMember("getClientRects", "https://drafts.csswg.org/cssom-view/#dom-range-getclientrects")]
    public DomRectList GetClientRects()
    {
        var quads = GetClientQuads();
        var rects = new DomRect[quads.Count];
        for (var i = 0; i < rects.Length; i++)
        {
            rects[i] = CssomViewGeometry.ToDomRect(quads[i].Bounds);
        }

        return new DomRectList(rects);
    }

    /// <summary>Returns the bounding box of the boxes of the range (https://drafts.csswg.org/cssom-view/#dom-range-getboundingclientrect): see <see cref="GetClientRects"/> and <see cref="Element.GetBoundingClientRect"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-range-getboundingclientrect"/>
    [IdlMember("getBoundingClientRect", "https://drafts.csswg.org/cssom-view/#dom-range-getboundingclientrect")]
    public DomRect GetBoundingClientRect()
    {
        var quads = GetClientQuads();
        var rects = new List<PhysicalRect>(quads.Count);
        foreach (var quad in quads)
        {
            rects.Add(quad.Bounds);
        }

        return CssomViewGeometry.GetBoundingRect(rects);
    }

    internal List<PhysicalQuad> GetClientQuads()
    {
        var result = new List<PhysicalQuad>();
        if (StartContainerInternal.GetShadowIncludingRoot() is not Document { BrowsingContext: not null } document)
            return result;

        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is null)
            return result;

        var startContainer = StartContainerInternal;
        var endContainer = EndContainerInternal;
        if (ReferenceEquals(startContainer, endContainer) && startContainer is Text text)
        {
            CssomViewGeometry.AddTextQuads(engine, text, (int)StartOffsetInternal, (int)EndOffsetInternal, result);
            return result;
        }

        var root = CommonAncestorContainer;
        for (Node? node = root; node is not null; node = node.GetNextInTreeOrder(root))
        {
            switch (node)
            {
                case Text textNode:
                    // A text node is selected when the range does not end before it nor start after it
                    if (ComparePoints(textNode, GetNodeLength(textNode), startContainer, StartOffsetInternal) == BoundaryPointPosition.Before
                        || ComparePoints(textNode, 0, endContainer, EndOffsetInternal) == BoundaryPointPosition.After)
                    {
                        break;
                    }

                    var start = ReferenceEquals(textNode, startContainer) ? (int)StartOffsetInternal : 0;
                    var end = ReferenceEquals(textNode, endContainer) ? (int)EndOffsetInternal : (int)textNode.Length;
                    CssomViewGeometry.AddTextQuads(engine, textNode, start, end, result);
                    break;

                case Element element when IsContained(element) && (element.ParentNode is not { } parent || !IsContained(parent)):
                    result.AddRange(CssomViewGeometry.GetClientQuads(element));
                    break;
            }
        }

        return result;
    }
}

/// <summary>
/// The screen of the output device (https://drafts.csswg.org/cssom-view/#the-screen-interface): the size of the viewport of
/// the top-level browsing context (see <see cref="ViewportOptions"/>), or 0 when the window has no browsing context.
/// </summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#screen"/>
[IdlInterface("Screen", "https://drafts.csswg.org/cssom-view/#screen")]
public sealed class Screen : IRealmBoundPlatformObject
{
    private readonly Window _window;

    internal Screen(Window window)
    {
        _window = window;
    }

    Window? IRealmBoundPlatformObject.RealmWindow => _window;

    /// <summary>Gets the available width of the screen (https://drafts.csswg.org/cssom-view/#dom-screen-availwidth): the width of the screen.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-screen-availwidth"/>
    [IdlMember("availWidth", "https://drafts.csswg.org/cssom-view/#dom-screen-availwidth")]
    public int AvailWidth => Width;

    /// <summary>Gets the available height of the screen (https://drafts.csswg.org/cssom-view/#dom-screen-availheight): the height of the screen.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-screen-availheight"/>
    [IdlMember("availHeight", "https://drafts.csswg.org/cssom-view/#dom-screen-availheight")]
    public int AvailHeight => Height;

    /// <summary>Gets the width of the screen in CSS pixels (https://drafts.csswg.org/cssom-view/#dom-screen-width).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-screen-width"/>
    [IdlMember("width", "https://drafts.csswg.org/cssom-view/#dom-screen-width")]
    public int Width => _window.BrowsingContext is null ? 0 : (int)StyleEngine.CreateEnvironment(_window.Document).DeviceWidth;

    /// <summary>Gets the height of the screen in CSS pixels (https://drafts.csswg.org/cssom-view/#dom-screen-height).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-screen-height"/>
    [IdlMember("height", "https://drafts.csswg.org/cssom-view/#dom-screen-height")]
    public int Height => _window.BrowsingContext is null ? 0 : (int)StyleEngine.CreateEnvironment(_window.Document).DeviceHeight;

    /// <summary>Gets the number of bits used to represent the colors (https://drafts.csswg.org/cssom-view/#dom-screen-colordepth): 24, as recommended when it is unknown.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-screen-colordepth"/>
    [IdlMember("colorDepth", "https://drafts.csswg.org/cssom-view/#dom-screen-colordepth")]
    public uint ColorDepth => 24;

    /// <summary>Gets the number of bits used to represent the colors (https://drafts.csswg.org/cssom-view/#dom-screen-pixeldepth): <see cref="ColorDepth"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-screen-pixeldepth"/>
    [IdlMember("pixelDepth", "https://drafts.csswg.org/cssom-view/#dom-screen-pixeldepth")]
    public uint PixelDepth => ColorDepth;
}
