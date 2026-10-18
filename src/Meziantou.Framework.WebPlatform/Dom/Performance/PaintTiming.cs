namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The paint timing info of a rendering update (https://w3c.github.io/paint-timing/#paint-timing-info): the end time of the
/// rendering update that painted, and the implementation-defined presentation time, which is always null (the frames are not
/// presented: the paint timings are flushed synchronously by "mark paint timing").
/// </summary>
/// <param name="RenderingUpdateEndTime">The time of the end of the rendering update, relative to the time origin of the window.</param>
internal sealed record PaintTimingInfo(double RenderingUpdateEndTime)
{
    /// <summary>The implementation-defined presentation time: null, there is no presentation time.</summary>
    public double? PresentationTime => null;

    /// <summary>https://w3c.github.io/paint-timing/#default-paint-timestamp</summary>
    public double DefaultPaintTimestamp => PresentationTime ?? RenderingUpdateEndTime;
}

/// <summary>
/// The first paint or the first contentful paint of a document (https://w3c.github.io/paint-timing/#sec-PerformancePaintTiming),
/// reported to the <c>paint</c> performance observers of its window and available from its performance timeline.
/// </summary>
[IdlInterface("PerformancePaintTiming", "https://w3c.github.io/paint-timing/#performancepainttiming")]
public sealed class PerformancePaintTiming : PerformanceEntry
{
    /// <summary>The entry type of the paint timing entries.</summary>
    internal const string PaintEntryType = "paint";

    /// <summary>The maximum size of the performance entry buffer of the paint timings (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 2;

    private readonly PaintTimingInfo _paintTimingInfo;

    internal PerformancePaintTiming(GlobalScope global, string name, PaintTimingInfo paintTimingInfo)
        : base(global, PaintEntryType, name, paintTimingInfo.DefaultPaintTimestamp, duration: 0)
    {
        _paintTimingInfo = paintTimingInfo;
    }

    /// <summary>Gets the time of the end of the rendering update that painted (https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime).</summary>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime => _paintTimingInfo.RenderingUpdateEndTime;

    /// <summary>Gets the time the frame was presented to the user: null, the presentation time is not supported.</summary>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => _paintTimingInfo.PresentationTime;
}

/// <summary>
/// A largest contentful paint candidate of a document (https://w3c.github.io/largest-contentful-paint/#sec-largest-contentful-paint-interface):
/// the largest image or text painted so far, reported to the <c>largest-contentful-paint</c> performance observers of its window.
/// </summary>
[IdlInterface("LargestContentfulPaint", "https://w3c.github.io/largest-contentful-paint/#largestcontentfulpaint")]
public sealed class LargestContentfulPaint : PerformanceEntry
{
    /// <summary>The entry type of the largest contentful paint entries.</summary>
    internal const string LargestContentfulPaintEntryType = "largest-contentful-paint";

    /// <summary>The maximum size of the performance entry buffer of the largest contentful paints (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 150;

    private readonly PaintTimingInfo _paintTimingInfo;
    private readonly Element? _element;

    internal LargestContentfulPaint(GlobalScope global, PaintTimingInfo paintTimingInfo, uint size, double loadTime, string id, string url, Element? element)
        : base(global, LargestContentfulPaintEntryType, name: "", paintTimingInfo.DefaultPaintTimestamp, duration: 0)
    {
        _paintTimingInfo = paintTimingInfo;
        Size = size;
        LoadTime = loadTime;
        Id = id;
        Url = url;
        _element = element;
    }

    /// <summary>Gets the time the image finished loading, or 0 for a text.</summary>
    [IdlMember("loadTime", "https://w3c.github.io/largest-contentful-paint/#dom-largestcontentfulpaint-loadtime")]
    public double LoadTime { get; }

    /// <summary>Gets the time the content was painted (the default paint timestamp of the rendering update).</summary>
    [IdlMember("renderTime", "https://w3c.github.io/largest-contentful-paint/#dom-largestcontentfulpaint-rendertime")]
    public double RenderTime => _paintTimingInfo.DefaultPaintTimestamp;

    /// <summary>Gets the effective visual size of the content, in CSS pixels squared.</summary>
    [IdlMember("size", "https://w3c.github.io/largest-contentful-paint/#dom-largestcontentfulpaint-size")]
    public uint Size { get; }

    /// <summary>Gets the id of the element when the entry was created.</summary>
    [IdlMember("id", "https://w3c.github.io/largest-contentful-paint/#dom-largestcontentfulpaint-id")]
    public new string Id { get; }

    /// <summary>Gets the URL of the image, or the empty string for a text.</summary>
    [IdlMember("url", "https://w3c.github.io/largest-contentful-paint/#dom-largestcontentfulpaint-url")]
    public string Url { get; }

    /// <summary>Gets the element, or null when it is no longer exposed for paint timing (it was removed from its document).</summary>
    [IdlMember("element", "https://w3c.github.io/largest-contentful-paint/#dom-largestcontentfulpaint-element")]
    public Element? Element => PaintTimingHelpers.IsExposedForPaintTiming(_element, document: null) ? _element : null;

    /// <summary>Gets the time of the end of the rendering update that painted the content.</summary>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime => _paintTimingInfo.RenderingUpdateEndTime;

    /// <summary>Gets the time the frame was presented to the user: null, the presentation time is not supported.</summary>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => _paintTimingInfo.PresentationTime;
}

/// <summary>
/// The paint of an image or of a text of an element with an <c>elementtiming</c> attribute (https://w3c.github.io/element-timing/#sec-performance-element-timing),
/// reported to the <c>element</c> performance observers of its window.
/// </summary>
[IdlInterface("PerformanceElementTiming", "https://w3c.github.io/element-timing/#performanceelementtiming")]
public sealed class PerformanceElementTiming : PerformanceEntry
{
    /// <summary>The entry type of the element timing entries.</summary>
    internal const string ElementEntryType = "element";

    /// <summary>The maximum size of the performance entry buffer of the element timings (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 150;

    /// <summary>The maximum length of the URL of a data: image (https://w3c.github.io/element-timing/#dom-performanceelementtiming-url).</summary>
    private const int MaxDataUrlLength = 100;

    private readonly PaintTimingInfo _paintTimingInfo;
    private readonly Element _element;
    private readonly string? _requestUrl;

    internal PerformanceElementTiming(GlobalScope global, string name, PaintTimingInfo paintTimingInfo, double loadTime, DomRectReadOnly intersectionRect, string identifier, uint naturalWidth, uint naturalHeight, string id, Element element, string? requestUrl)
        : base(global, ElementEntryType, name, paintTimingInfo.DefaultPaintTimestamp is not 0 and var renderTime ? renderTime : loadTime, duration: 0)
    {
        _paintTimingInfo = paintTimingInfo;
        LoadTime = loadTime;
        IntersectionRect = intersectionRect;
        Identifier = identifier;
        NaturalWidth = naturalWidth;
        NaturalHeight = naturalHeight;
        Id = id;
        _element = element;
        _requestUrl = requestUrl;
    }

    /// <summary>Gets the time the content was painted (the default paint timestamp of the rendering update).</summary>
    [IdlMember("renderTime", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-rendertime")]
    public double RenderTime => _paintTimingInfo.DefaultPaintTimestamp;

    /// <summary>Gets the time the image finished loading, or 0 for a text.</summary>
    [IdlMember("loadTime", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-loadtime")]
    public double LoadTime { get; }

    /// <summary>Gets the visible part of the content in the viewport, in CSS pixels.</summary>
    [IdlMember("intersectionRect", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-intersectionrect")]
    public DomRectReadOnly IntersectionRect { get; }

    /// <summary>Gets the value of the <c>elementtiming</c> attribute of the element.</summary>
    [IdlMember("identifier", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-identifier")]
    public string Identifier { get; }

    /// <summary>Gets the natural width of the image, or 0 for a text.</summary>
    [IdlMember("naturalWidth", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-naturalwidth")]
    public uint NaturalWidth { get; }

    /// <summary>Gets the natural height of the image, or 0 for a text.</summary>
    [IdlMember("naturalHeight", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-naturalheight")]
    public uint NaturalHeight { get; }

    /// <summary>Gets the id of the element when the entry was created.</summary>
    [IdlMember("id", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-id")]
    public new string Id { get; }

    /// <summary>Gets the element, or null when it is no longer exposed for paint timing (it was removed from its document).</summary>
    [IdlMember("element", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-element")]
    public Element? Element => PaintTimingHelpers.IsExposedForPaintTiming(_element, document: null) ? _element : null;

    /// <summary>Gets the URL of the image (the first 100 characters of a data: URL), or the empty string for a text.</summary>
    [IdlMember("url", "https://w3c.github.io/element-timing/#dom-performanceelementtiming-url")]
    public string Url => _requestUrl is not { } url ? "" : url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && url.Length > MaxDataUrlLength ? url[..MaxDataUrlLength] : url;

    /// <summary>Gets the time of the end of the rendering update that painted the content.</summary>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime => _paintTimingInfo.RenderingUpdateEndTime;

    /// <summary>Gets the time the frame was presented to the user: null, the presentation time is not supported.</summary>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => _paintTimingInfo.PresentationTime;
}

/// <summary>The algorithms shared by the paint timing entries.</summary>
internal static class PaintTimingHelpers
{
    /// <summary>
    /// https://w3c.github.io/paint-timing/#exposed-for-paint-timing: whether an element is connected, in a fully active document
    /// (the associated document of its relevant global object when <paramref name="document"/> is null) and not in a shadow tree.
    /// The target of an event timing entry can be a node that is not an element (e.g. the document).
    /// </summary>
    public static bool IsExposedForPaintTiming(Node? node, Document? document)
    {
        if (node is not { IsConnected: true })
            return false;

        document ??= node.NodeDocument.RelevantWindow?.Document ?? node.NodeDocument;
        return document.IsFullyActive && ReferenceEquals(node.GetRoot(), document);
    }
}
