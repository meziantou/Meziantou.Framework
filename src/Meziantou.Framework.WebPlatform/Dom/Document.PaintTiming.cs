using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

// The paint timing of a document (https://w3c.github.io/paint-timing/): the pending image records, the owned text nodes, "mark
// paint timing" at the end of the rendering update, the first paint and the first contentful paint, the largest contentful paint
// (https://w3c.github.io/largest-contentful-paint/) and the element timing (https://w3c.github.io/element-timing/)
public partial class Document
{
    /// <summary>The maximum number of pending image records (the oldest ones are dropped).</summary>
    private const int MaxPendingImages = 512;

    private PaintTimingState? _paintTiming;

    /// <summary>Whether the paints of the document are computed by the rendering updates (see <see cref="StartPaintTiming"/>).</summary>
    internal bool IsTrackingPaintTiming => _paintTiming is { IsTracking: true };

    /// <summary>
    /// Whether one of the paint timing APIs is exposed to the window of the document (see <see cref="WebPlatformFeatures.PaintTimingEnabled"/>,
    /// <see cref="WebPlatformFeatures.LargestContentfulPaintEnabled"/>, <see cref="WebPlatformFeatures.ElementTimingEnabled"/>) and
    /// its browsing context is paint-timing eligible (https://w3c.github.io/paint-timing/#paint-timing-eligible: all of them are).
    /// </summary>
    private bool IsPaintTimingSupported => BrowsingContext?.Options.EffectiveFeatures is { } features
        && (features.PaintTimingEnabled || features.LargestContentfulPaintEnabled || features.ElementTimingEnabled)
        && DefaultView is not null;

    /// <summary>
    /// Starts to compute the paints of the document (a script of its window observes or reads the paint timing entries): from then
    /// on, every rendering update paints the document into its display list, which is "the paint" of the paint timing whether or
    /// not a rendering backend rasterizes it, and runs "mark paint timing". A document whose paint timing is never read is never
    /// laid out nor painted for it: its content painted before the tracking starts is considered painted by the next rendering
    /// update (the images loaded before are kept as pending image records).
    /// </summary>
    internal void StartPaintTiming()
    {
        if (_paintTiming is { IsTracking: true } || !IsPaintTimingSupported || EventLoop is not { } eventLoop)
            return;

        _paintTiming ??= new PaintTimingState();
        _paintTiming.IsTracking = true;
        eventLoop.AddDocumentWithPaintTiming(this);
        GetLayoutEngine().RequestRenderingUpdates();
        eventLoop.RequestRenderingUpdate();
    }

    /// <summary>
    /// https://w3c.github.io/paint-timing/#process-image-that-finished-loading for the image request of an element (an img
    /// element, an SVG image element, the poster frame of a video element) that became completely available.
    /// </summary>
    internal void ProcessImageThatFinishedLoading(Element element, string url, ImageNaturalSize naturalSize, long contentLength)
    {
        if (!IsPaintTimingSupported || DefaultView is not { } window)
            return;

        var state = _paintTiming ??= new PaintTimingState();

        // A new image request of the element replaces the one that was not painted yet
        state.ImagesPendingRendering.RemoveAll(record => ReferenceEquals(record.Element, element) && !record.IsCssImage);
        state.PaintedImageRecords.RemoveAll(record => ReferenceEquals(record.Element, element));
        if (state.ImagesPendingRendering.Count >= MaxPendingImages)
        {
            state.ImagesPendingRendering.RemoveAt(0);
        }

        // As in the browsers, the image of an element that is not in the document is recorded too: it is considered loaded when the
        // element is first painted (the specification ignores it)
        var loadTime = ReferenceEquals(element.GetRoot(), this) ? window.GetCurrentHighResolutionTime() : double.NaN;
        state.ImagesPendingRendering.Add(new PendingImageRecord(element, url, naturalSize.Width ?? 0, naturalSize.Height ?? 0, contentLength, loadTime, IsCssImage: false));
        if (state.IsTracking)
        {
            EventLoop?.RequestRenderingUpdate();
        }
    }

    /// <summary>
    /// https://w3c.github.io/paint-timing/#sec-modifications-CSS: a CSS image of the document finished loading. The image requests of
    /// the CSS images are shared by the elements: the pending image record of an element is created when it first paints the image,
    /// with the time the image finished loading (or the previous rendering update, for an image loaded before the element used it).
    /// </summary>
    internal void ProcessCssImageThatFinishedLoading(string url, ImageNaturalSize naturalSize, long contentLength)
    {
        if (!IsPaintTimingSupported || DefaultView is not { } window)
            return;

        var state = _paintTiming ??= new PaintTimingState();
        state.CssImages[url] = new CssImageLoad(naturalSize.Width ?? 0, naturalSize.Height ?? 0, contentLength, window.GetCurrentHighResolutionTime());
        if (state.IsTracking)
        {
            EventLoop?.RequestRenderingUpdate();
        }
    }

    /// <summary>
    /// https://w3c.github.io/paint-timing/#mark-paint-timing, at the end of the rendering update: the document is painted into its
    /// display list, whose contentful paints (<see cref="ContentfulPaintScanner"/>) give the painted images (the pending image records
    /// of the painted elements) and the painted text nodes, then the first paint, the first contentful paint, the largest contentful
    /// paint and the element timing are reported (the user agent does not support the presentation times: they are flushed at once).
    /// </summary>
    internal void MarkPaintTiming()
    {
        if (_paintTiming is not { IsTracking: true } state || !IsFullyActive || DefaultView is not { } window)
            return;

        var engine = GetLayoutEngine();
        var displayList = engine.GetDisplayList();
        if (displayList is null || (ReferenceEquals(displayList, state.LastDisplayList) && state.ImagesPendingRendering.Count == state.LastPendingImageCount && state.CssImages.Count == state.LastCssImageCount))
            return;

        state.LastDisplayList = displayList;
        var paintTimingInfo = new PaintTimingInfo(window.GetCurrentHighResolutionTime());
        var scrollState = engine.ViewportScrollState;
        var scrollingArea = scrollState.ScrollingArea.Translate(new PhysicalOffset(-scrollState.Offset.X, -scrollState.Offset.Y));
        var scrollport = engine.EnsureLayout() is { } root ? new PhysicalRect(root.Offset, root.Size) : new PhysicalRect(PhysicalOffset.Zero, displayList.Size);
        var scan = ContentfulPaintScanner.Scan(displayList, this, scrollingArea, scrollport);

        // The images: the pending image records of the elements that painted their image, and the CSS images painted for the first
        // time by an element
        var paintedImages = new List<(PendingImageRecord Record, PhysicalRect PaintedRect)>();

        // As in the browsers, the image of an element that stopped being rendered (e.g. display: none) is painted again when it is
        // rendered again (its box is created again)
        for (var i = 0; i < state.PaintedImageRecords.Count; i++)
        {
            var record = state.PaintedImageRecords[i];
            if (!record.Element.IsConnected)
            {
                state.PaintedImageRecords.RemoveAt(i--);
            }
            else if (!scan.Images.ContainsKey(record.Element) && engine.GetFragments(record.Element).Count == 0)
            {
                state.PaintedImageRecords.RemoveAt(i--);
                state.ImagesPendingRendering.Add(record);
            }
        }

        foreach (var (element, images) in scan.Images)
        {
            AddCssImageRecords(state, element, images, paintTimingInfo);
        }

        for (var i = 0; i < state.ImagesPendingRendering.Count; i++)
        {
            var record = state.ImagesPendingRendering[i];
            if (!scan.Images.TryGetValue(record.Element, out var images) || GetPaintedRect(images, record.Url) is not { } paintedRect)
                continue;

            // https://w3c.github.io/paint-timing/#process-image-that-finished-loading: the images of the shadow trees are not reported
            if (!ReferenceEquals(record.Element.GetRoot(), this))
            {
                state.ImagesPendingRendering.RemoveAt(i--);
                continue;
            }

            if (double.IsNaN(record.LoadTime))
            {
                record = record with { LoadTime = paintTimingInfo.RenderingUpdateEndTime };
            }

            paintedImages.Add((record, paintedRect));
            state.ImagesPendingRendering.RemoveAt(i--);
            if (!record.IsCssImage)
            {
                state.PaintedImageRecords.Add(record);
            }
        }

        state.LastPendingImageCount = state.ImagesPendingRendering.Count;
        state.LastCssImageCount = state.CssImages.Count;
        state.LastMarkTime = paintTimingInfo.RenderingUpdateEndTime;

        // The text nodes painted for the first time are owned by the element that determines their containing block (the
        // originating element for a generated text)
        HashSet<Element>? newOwners = null;
        foreach (var (node, _) in scan.Texts)
        {
            if (!state.PaintedTexts.Add(node) || GetTextOwner(engine, node) is not { } owner)
                continue;

            if (!state.OwnedTextNodes.TryGetValue(owner, out var owned))
            {
                owned = [];
                state.OwnedTextNodes[owner] = owned;
            }

            owned.Add(node);
            if (!state.ElementsWithRenderedText.Contains(owner))
            {
                (newOwners ??= []).Add(owner);
            }
        }

        // The elements with owned text nodes that were not reported, in tree order
        var paintedTextElements = new List<Element>();
        if (newOwners is not null)
        {
            foreach (var node in Descendants())
            {
                if (node is Element element && newOwners.Contains(element) && state.ElementsWithRenderedText.Add(element))
                {
                    paintedTextElements.Add(element);
                }
            }
        }


        // As in the browsers, the texts painted again after a web font is loaded (e.g. at the end of the swap period of their font) are
        // considered again by the largest contentful paint (the specification reports each element once)
        var lcpTextElements = paintedTextElements;
        var fontVersion = FontSelectorOrNull?.Version ?? 0;
        if (fontVersion != state.LastFontVersion)
        {
            if (state.LastFontVersion >= 0)
            {
                lcpTextElements = [.. paintedTextElements];
                foreach (var element in state.ElementsWithRenderedText)
                {
                    if (!paintedTextElements.Contains(element) && element.IsConnected)
                    {
                        lcpTextElements.Add(element);
                    }
                }
            }

            state.LastFontVersion = fontVersion;
        }

        var features = BrowsingContext!.Options.EffectiveFeatures;
        if (features.PaintTimingEnabled)
        {
            // The first paint excludes the default background paint; the first contentful paint needs an element that is both
            // paintable and contentful (a first contentful paint is a first paint too)
            if (!state.ReportedPaints.Contains("first-paint") && (scan.HasFirstPaint || scan.HasContentfulPaint))
            {
                ReportPaintTiming(state, window, "first-paint", paintTimingInfo);
            }

            if (!state.ReportedPaints.Contains("first-contentful-paint") && scan.HasContentfulPaint)
            {
                ReportPaintTiming(state, window, "first-contentful-paint", paintTimingInfo);
            }
        }

        if (features.LargestContentfulPaintEnabled)
        {
            ReportLargestContentfulPaint(state, engine, window, paintTimingInfo, paintedImages, lcpTextElements, scan);
        }

        if (features.ElementTimingEnabled)
        {
            ReportElementTiming(state, engine, window, paintTimingInfo, paintedImages, paintedTextElements);
        }
    }

    /// <summary>https://w3c.github.io/paint-timing/#report-paint-timing</summary>
    private static void ReportPaintTiming(PaintTimingState state, Window window, string paintType, PaintTimingInfo paintTimingInfo)
    {
        window.Performance.QueuePaintTimingEntry(new PerformancePaintTiming(window, paintType, paintTimingInfo));
        state.ReportedPaints.Add(paintType);
    }

    /// <summary>
    /// Creates the pending image records of the CSS images an element painted for the first time: the load time is the time the
    /// image finished loading, or the time of the previous rendering update when the image was loaded before (the element started
    /// to use an image that was available, e.g. after a change of its styles).
    /// </summary>
    private static void AddCssImageRecords(PaintTimingState state, Element element, Dictionary<string, PhysicalRect> images, PaintTimingInfo paintTimingInfo)
    {
        foreach (var (url, _) in images)
        {
            if (url.Length == 0 || !state.CssImages.TryGetValue(url, out var load) || !state.ReportedCssImages.Add((element, url)))
                continue;

            var loadTime = Math.Min(Math.Max(load.LoadTime, state.LastMarkTime), paintTimingInfo.RenderingUpdateEndTime);
            state.ImagesPendingRendering.Add(new PendingImageRecord(element, url, load.NaturalWidth, load.NaturalHeight, load.ContentLength, loadTime, IsCssImage: true));
        }

        // An SVG image is painted from its document: the URLs of the CSS images of the element that finished loading are painted
        if (images.ContainsKey(""))
        {
            foreach (var (url, load) in state.CssImages)
            {
                if (images.ContainsKey(url) || !Css.Om.CssImageFetching.UsesImage(element.NodeDocument, element, url) || !state.ReportedCssImages.Add((element, url)))
                    continue;

                var loadTime = Math.Min(Math.Max(load.LoadTime, state.LastMarkTime), paintTimingInfo.RenderingUpdateEndTime);
                state.ImagesPendingRendering.Add(new PendingImageRecord(element, url, load.NaturalWidth, load.NaturalHeight, load.ContentLength, loadTime, IsCssImage: true));
            }
        }
    }

    /// <summary>Returns the bounds of the paints of the image of a URL by an element (an image whose URL is not known, an SVG image, matches any URL).</summary>
    private static PhysicalRect? GetPaintedRect(Dictionary<string, PhysicalRect> images, string url)
        => images.TryGetValue(url, out var rect) ? rect : images.TryGetValue("", out var svgRect) ? svgRect : null;

    /// <summary>
    /// Returns the element that owns a painted text (https://w3c.github.io/paint-timing/#set-of-owned-text-nodes): the element of the
    /// nearest block container (or other box that is not an inline box) of a text node, or the element whose generated content
    /// or form control is painted.
    /// </summary>
    private static Element? GetTextOwner(LayoutEngine engine, Node node)
    {
        var fragments = GetPaintedTextFragments(engine, node);
        if (fragments.Count == 0)
            return node as Element ?? node.ParentElement;

        for (var fragment = fragments[0].Parent; fragment is not null; fragment = fragment.Parent)
        {
            if (fragment is BoxFragment { Box: ContainerBox { Element: { } owner, IsInlineBox: false } })
                return owner;
        }

        return node as Element ?? node.ParentElement;
    }

    /// <summary>Returns the text fragments of a painted text: the fragments of a text node, or of the generated text of an element (its pseudo-elements, its content).</summary>
    private static List<TextFragment> GetPaintedTextFragments(LayoutEngine engine, Node node)
    {
        if (node is Text text)
            return [.. engine.GetTextFragments(text)];

        var result = new List<TextFragment>();
        if (node is not Element element)
            return result;

        foreach (var pseudoElement in (ReadOnlySpan<string?>)[null, "before", "after", "marker"])
        {
            foreach (var fragment in engine.GetFragments(element, pseudoElement))
            {
                Collect(fragment, element, result);
            }
        }

        return result;

        static void Collect(Fragment fragment, Element element, List<TextFragment> result)
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is TextFragment { Run: { Node: null } run } textFragment && ReferenceEquals(run.Element, element))
                {
                    if (!result.Contains(textFragment))
                    {
                        result.Add(textFragment);
                    }
                }
                else if (child is not BoxFragment { Box.Element: { } childElement } || ReferenceEquals(childElement, element))
                {
                    Collect(child, element, result);
                }
            }
        }
    }

    /// <summary>
    /// The union of the border boxes of the owned text nodes of an element (the text fragments of its text nodes, and of the
    /// generated texts), in the coordinates of the viewport.
    /// </summary>
    private static PhysicalRect? GetOwnedTextRect(PaintTimingState state, LayoutEngine engine, Element element)
    {
        if (!state.OwnedTextNodes.TryGetValue(element, out var owned))
            return null;

        PhysicalRect? result = null;
        foreach (var node in owned)
        {
            foreach (var fragment in GetPaintedTextFragments(engine, node))
            {
                var rect = engine.ToViewport(LayoutEngine.MapToRoot(fragment, new PhysicalRect(PhysicalOffset.Zero, fragment.Size))).Bounds;
                result = result is { } previous ? previous.Union(rect) : rect;
            }
        }

        return result;
    }

    /// <summary>Returns the visual viewport of the document (without the scrollbars), in the coordinates of the viewport.</summary>
    private static PhysicalRect GetVisualViewportRect(LayoutEngine engine)
    {
        var size = engine.InitialContainingBlockSize;
        return new PhysicalRect(0, 0, size.Width, size.Height);
    }

    /// <summary>
    /// https://w3c.github.io/largest-contentful-paint/#report-largest-contentful-paint: the largest image or text painted by the
    /// rendering update becomes the new candidate when it is larger than the current one, until a scroll or an input.
    /// </summary>
    private void ReportLargestContentfulPaint(PaintTimingState state, LayoutEngine engine, Window window, PaintTimingInfo paintTimingInfo, List<(PendingImageRecord Record, PhysicalRect PaintedRect)> paintedImages, List<Element> paintedTextElements, ContentfulPaintScan scan)
    {
        if (window.HasDispatchedInputEvent)
            return;

        // https://w3c.github.io/largest-contentful-paint/#compute-new-lcp-candidate
        var currentCandidate = state.LargestContentfulPaintCandidate;
        var currentSize = currentCandidate?.Size ?? 0;
        var largestSize = currentSize;
        LargestContentfulPaintCandidate? newCandidate = null;
        foreach (var (record, paintedRect) in paintedImages)
        {
            var imageElement = record.Element;
            if (!PaintTimingHelpers.IsExposedForPaintTiming(imageElement, this))
                continue;

            var intersectionRect = GetImageIntersectionRect(engine, record, paintedRect);

            if (GetEffectiveVisualSize(intersectionRect, record, paintedRect) is not { } result || result.Size <= largestSize)
                continue;

            largestSize = result.Size;
            newCandidate = new LargestContentfulPaintCandidate(imageElement, result.Size, result.Width, result.Height, record.Url, record.LoadTime);
        }

        foreach (var element in paintedTextElements)
        {
            if (!PaintTimingHelpers.IsExposedForPaintTiming(element, this))
                continue;

            // A transparent text (or a text in a group whose opacity is 0, which is not painted) is not a candidate, unless its shadow or
            // its stroke is visible
            if (!HasVisibleText(state, scan, element))
                continue;

            if (GetOwnedTextRect(state, engine, element) is not { } textRect || !IntersectionGeometry.TryIntersect(textRect, GetVisualViewportRect(engine), out var intersectionRect))
                continue;

            if (GetEffectiveVisualSize(intersectionRect, record: null, paintedRect: default) is not { } result || result.Size <= largestSize)
                continue;

            largestSize = result.Size;
            newCandidate = new LargestContentfulPaintCandidate(element, result.Size, result.Width, result.Height, Url: null, LoadTime: 0);
        }

        if (newCandidate is null)
            return;

        // A candidate whose size changed by 3 px or less in both axes is the same content (e.g. a text whose web font is loaded). As in
        // the browsers, this only applies to the content of the same element (WPT largest-contentful-paint/image-TAO.sub.html)
        if (currentCandidate is not null && currentSize > 0 && ReferenceEquals(newCandidate.Element, currentCandidate.Element)
            && newCandidate.Width - currentCandidate.Width <= 3 && newCandidate.Height - currentCandidate.Height <= 3)
            return;

        state.LargestContentfulPaintCandidate = newCandidate;

        // https://w3c.github.io/largest-contentful-paint/#create-a-largestcontentfulpaint-entry
        var entry = new LargestContentfulPaint(window, paintTimingInfo, (uint)Math.Min(uint.MaxValue, Math.Round(newCandidate.Size, MidpointRounding.AwayFromZero)), newCandidate.LoadTime, newCandidate.Element.GetAttributeValue("id") ?? "", newCandidate.Url ?? "", newCandidate.Element);
        window.Performance.QueueLargestContentfulPaintEntry(entry);

        static bool HasVisibleText(PaintTimingState state, ContentfulPaintScan scan, Element element)
        {
            if (!state.OwnedTextNodes.TryGetValue(element, out var owned))
                return false;

            foreach (var node in owned)
            {
                if (scan.Texts.TryGetValue(node, out var visible) && visible)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// https://w3c.github.io/largest-contentful-paint/#sec-effective-visual-size: the area of the visible part of the content in the
    /// viewport (for an image: of the painted image, reduced by its upscaling), or null when it is not a candidate.
    /// </summary>
    private EffectiveVisualSize? GetEffectiveVisualSize(PhysicalRect intersectionRect, PendingImageRecord? record, PhysicalRect paintedRect)
    {
        // As in the browsers, the size is the area of the rectangle (the width and the height rounded up are used to compare the
        // candidates: the specification rounds them up before computing the size)
        var width = Math.Ceiling(intersectionRect.Width);
        var height = Math.Ceiling(intersectionRect.Height);
        var size = intersectionRect.Width * intersectionRect.Height;

        // The content that covers the whole viewport of the top-level document is a background
        var root = BrowsingContext?.Top.Document ?? this;
        var rootSize = root.GetLayoutEngine().InitialContainingBlockSize;
        if (size == rootSize.Width * rootSize.Height)
            return null;

        if (record is not null)
        {
            // The visible part of the painted image (the rectangle of the image in the element, with the transforms)
            var clientContentRect = paintedRect;
            if (!IntersectionGeometry.TryIntersect(clientContentRect, intersectionRect, out var intersectingClientContentRect))
                return null;

            width = Math.Ceiling(intersectingClientContentRect.Width);
            height = Math.Ceiling(intersectingClientContentRect.Height);
            size = intersectingClientContentRect.Width * intersectingClientContentRect.Height;
            var naturalArea = record.NaturalWidth * record.NaturalHeight;
            if (naturalArea == 0)
                return null;

            var boundingClientArea = clientContentRect.Width * clientContentRect.Height;
            var scaleFactor = boundingClientArea / naturalArea;
            if (scaleFactor > 1)
            {
                size /= scaleFactor;
            }

            // As in the browsers, the size is a whole number of pixels: an image upscaled so much that its effective size is less
            // than 1 px (e.g. a 1x1 image) is not a candidate
            size = Math.Floor(size + 1e-6);
            if (size == 0)
                return null;

            // An image whose encoded data is small for its area is a low-content background. As in the browsers, the area is the
            // effective size (after the upscaling adjustment; the specification checks the size of the intersection rect)
            if (record.ContentLength < size * 0.004)
                return null;
        }

        return new EffectiveVisualSize(Math.Floor(size), width, height);
    }

    /// <summary>
    /// The intersection rect of an image (https://w3c.github.io/element-timing/#report-image-element-timing): the intersection rect
    /// algorithm of its element, or, as in the browsers, the visible part of the painted image of a CSS image (the image of a
    /// pseudo-element, of a list marker, a background image) in the viewport.
    /// </summary>
    private PhysicalRect GetImageIntersectionRect(LayoutEngine engine, PendingImageRecord record, PhysicalRect paintedRect)
    {
        if (!record.IsCssImage)
            return GetIntersectionRect(record.Element);

        return IntersectionGeometry.TryIntersect(paintedRect, GetVisualViewportRect(engine), out var visible) ? visible : default;
    }

    /// <summary>The intersection rect algorithm (https://w3c.github.io/IntersectionObserver/#calculate-intersection-rect-algo) of an element with the viewport of the document as the root.</summary>
    private PhysicalRect GetIntersectionRect(Element element)
    {
        var observation = IntersectionGeometry.Compute(this, this, element, IntersectionMargin.Zero, IntersectionMargin.Zero, applyMargins: false, trackVisibility: false);
        return observation.IsIntersecting ? observation.IntersectionRect : default;
    }

    /// <summary>https://w3c.github.io/element-timing/#report-element-timing</summary>
    private void ReportElementTiming(PaintTimingState state, LayoutEngine engine, Window window, PaintTimingInfo paintTimingInfo, List<(PendingImageRecord Record, PhysicalRect PaintedRect)> paintedImages, List<Element> paintedTextElements)
    {
        // https://w3c.github.io/element-timing/#report-image-element-timing
        foreach (var (record, paintedRect) in paintedImages)
        {
            var element = record.Element;
            if (element.GetAttributeValue("elementtiming") is not { } identifier)
                continue;

            var intersectionRect = GetImageIntersectionRect(engine, record, paintedRect);
            var entry = new PerformanceElementTiming(window, "image-paint", paintTimingInfo, record.LoadTime, ToDomRect(intersectionRect), identifier,
                (uint)Math.Max(0, Math.Round(record.NaturalWidth, MidpointRounding.AwayFromZero)), (uint)Math.Max(0, Math.Round(record.NaturalHeight, MidpointRounding.AwayFromZero)), element.GetAttributeValue("id") ?? "", element, record.Url);
            window.Performance.QueueElementTimingEntry(entry);
        }

        // https://w3c.github.io/element-timing/#report-text-element-timing
        foreach (var element in paintedTextElements)
        {
            if (element.GetAttributeValue("elementtiming") is not { } identifier)
                continue;

            var intersectionRect = GetOwnedTextRect(state, engine, element) is { } textRect && IntersectionGeometry.TryIntersect(textRect, GetVisualViewportRect(engine), out var visible) ? visible : default;
            var entry = new PerformanceElementTiming(window, "text-paint", paintTimingInfo, loadTime: 0, ToDomRect(intersectionRect), identifier, 0, 0, element.GetAttributeValue("id") ?? "", element, requestUrl: null);
            window.Performance.QueueElementTimingEntry(entry);
        }

        static DomRectReadOnly ToDomRect(PhysicalRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>A pending image record (https://w3c.github.io/paint-timing/#pending-image-record), with the data of its image request used by the paint timing entries.</summary>
    private sealed record PendingImageRecord(Element Element, string Url, double NaturalWidth, double NaturalHeight, long ContentLength, double LoadTime, bool IsCssImage);

    /// <summary>A CSS image that finished loading (its image request), with the time it finished loading.</summary>
    private sealed record CssImageLoad(double NaturalWidth, double NaturalHeight, long ContentLength, double LoadTime);

    /// <summary>A largest contentful paint candidate (https://w3c.github.io/largest-contentful-paint/#largest-contentful-paint-candidate).</summary>
    private sealed record LargestContentfulPaintCandidate(Element Element, double Size, double Width, double Height, string? Url, double LoadTime);

    /// <summary>An effective visual size (https://w3c.github.io/largest-contentful-paint/#effective-visual-size).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct EffectiveVisualSize(double Size, double Width, double Height);

    /// <summary>The state of the paint timing of a document.</summary>
    private sealed class PaintTimingState
    {
        /// <summary>Whether the rendering updates mark the paint timing (see <see cref="StartPaintTiming"/>).</summary>
        public bool IsTracking { get; set; }

        /// <summary>https://w3c.github.io/paint-timing/#set-of-previously-reported-paints</summary>
        public HashSet<string> ReportedPaints { get; } = new(StringComparer.Ordinal);

        /// <summary>https://w3c.github.io/paint-timing/#images-pending-rendering</summary>
        public List<PendingImageRecord> ImagesPendingRendering { get; } = [];

        /// <summary>The image records of the elements (not the CSS images) that were painted.</summary>
        public List<PendingImageRecord> PaintedImageRecords { get; } = [];

        /// <summary>The CSS images that finished loading, by URL.</summary>
        public Dictionary<string, CssImageLoad> CssImages { get; } = new(StringComparer.Ordinal);

        /// <summary>The CSS images whose pending image record was created for an element.</summary>
        public HashSet<(Element Element, string Url)> ReportedCssImages { get; } = [];

        /// <summary>https://w3c.github.io/paint-timing/#set-of-elements-with-rendered-text</summary>
        public HashSet<Element> ElementsWithRenderedText { get; } = [];

        /// <summary>https://w3c.github.io/paint-timing/#set-of-owned-text-nodes, by element (a text node, or the element for its generated text).</summary>
        public Dictionary<Element, List<Node>> OwnedTextNodes { get; } = [];

        /// <summary>The text nodes (and the elements whose generated text) that were painted.</summary>
        public HashSet<Node> PaintedTexts { get; } = [];

        /// <summary>https://w3c.github.io/largest-contentful-paint/#current-largest-contentful-paint-candidate</summary>
        public LargestContentfulPaintCandidate? LargestContentfulPaintCandidate { get; set; }

        /// <summary>The display list scanned by the last "mark paint timing" (a rendering update that does not paint again has no new paint).</summary>
        public Rendering.DisplayList? LastDisplayList { get; set; }

        public int LastPendingImageCount { get; set; } = -1;

        public int LastCssImageCount { get; set; } = -1;

        /// <summary>The version of the font selector of the document at the last rendering update that marked the paint timing (the web fonts loaded since then).</summary>
        public int LastFontVersion { get; set; } = -1;

        /// <summary>The time of the last rendering update that marked the paint timing.</summary>
        public double LastMarkTime { get; set; }
    }
}

// The flag of a window that stops the reporting of the largest contentful paint (https://w3c.github.io/largest-contentful-paint/#sec-modifications-DOM).
// As in the browsers, only the user scrolls stop it, not the scroll events of the scripted scrolls (the trusted scroll events of
// the specification): the user scrolling is not implemented, so the scrolls never stop it.
public partial class Window
{
    /// <summary>
    /// https://w3c.github.io/event-timing/#has-dispatched-input-event: a trusted input event that is not a hover nor a scroll was
    /// dispatched (a press of a key or of a pointer button, a click).
    /// </summary>
    internal bool HasDispatchedInputEvent { get; private set; }

    /// <summary>Records the dispatch of a trusted event whose relevant global object is the window.</summary>
    internal void NotifyEventDispatched(Event @event)
    {
        if (@event.IsTrusted && @event.Type is "keydown" or "mousedown" or "pointerdown" or "click")
        {
            HasDispatchedInputEvent = true;
        }
    }
}
