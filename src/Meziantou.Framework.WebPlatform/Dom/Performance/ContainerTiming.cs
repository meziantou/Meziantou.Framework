using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The paints of the subtree of a container root, an element with a <c>containertiming</c> attribute (https://w3c.github.io/container-timing/):
/// reported to the <c>container</c> performance observers of its window by each rendering update that paints new content in the subtree.
/// </summary>
/// <seealso href="https://w3c.github.io/container-timing/#performancecontainertiming"/>
[IdlInterface("PerformanceContainerTiming", "https://w3c.github.io/container-timing/#performancecontainertiming")]
public sealed class PerformanceContainerTiming : PerformanceEntry
{
    /// <summary>The entry type of the container timing entries.</summary>
    internal const string ContainerEntryType = "container";

    /// <summary>The maximum size of the performance entry buffer of the container timings (as the element timings: the registry does not define it).</summary>
    internal const int MaxBufferSize = 150;

    private readonly PaintTimingInfo _paintTimingInfo;
    private readonly HtmlElement? _lastPaintedElement;
    private readonly HtmlElement _rootElement;

    internal PerformanceContainerTiming(GlobalScope global, PaintTimingInfo paintTimingInfo, string identifier, DomRectReadOnly intersectionRect, ulong size, double firstRenderTime, HtmlElement? lastPaintedElement, HtmlElement rootElement)
        : base(global, ContainerEntryType, name: "", paintTimingInfo.DefaultPaintTimestamp, duration: 0)
    {
        _paintTimingInfo = paintTimingInfo;
        Identifier = identifier;
        IntersectionRect = intersectionRect;
        Size = size;
        FirstRenderTime = firstRenderTime;
        _lastPaintedElement = lastPaintedElement;
        _rootElement = rootElement;
    }

    /// <summary>Gets the value of the <c>containertiming</c> attribute of the container root.</summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-performancecontainertiming-identifier"/>
    [IdlMember("identifier", "https://w3c.github.io/container-timing/#dom-performancecontainertiming-identifier")]
    public string Identifier { get; }

    /// <summary>Gets the bounding rectangle of the region painted so far in the container root, in the coordinates of the viewport.</summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-performancecontainertiming-intersectionrect"/>
    [IdlMember("intersectionRect", "https://w3c.github.io/container-timing/#dom-performancecontainertiming-intersectionrect")]
    public DomRectReadOnly IntersectionRect { get; }

    /// <summary>Gets the area of the region painted so far in the container root, in CSS pixels squared (the overlapping paints are counted once).</summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-performancecontainertiming-size"/>
    [IdlMember("size", "https://w3c.github.io/container-timing/#dom-performancecontainertiming-size")]
    public ulong Size { get; }

    /// <summary>Gets the time of the first paint in the container root.</summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-performancecontainertiming-firstrendertime"/>
    [IdlMember("firstRenderTime", "https://w3c.github.io/container-timing/#dom-performancecontainertiming-firstrendertime")]
    public double FirstRenderTime { get; }

    /// <summary>
    /// Gets the largest element (by the area of its own paint) that painted new content in the container root during the rendering
    /// update, or null when it is not an HTML element or it is no longer exposed for paint timing.
    /// </summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-performancecontainertiming-lastpaintedelement"/>
    [IdlMember("lastPaintedElement", "https://w3c.github.io/container-timing/#dom-performancecontainertiming-lastpaintedelement")]
    public HtmlElement? LastPaintedElement => PaintTimingHelpers.IsExposedForPaintTiming(_lastPaintedElement, document: null) ? _lastPaintedElement : null;

    /// <summary>Gets the container root, or null when it is no longer exposed for paint timing (it was removed from its document).</summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-performancecontainertiming-rootelement"/>
    [IdlMember("rootElement", "https://w3c.github.io/container-timing/#dom-performancecontainertiming-rootelement")]
    public HtmlElement? RootElement => PaintTimingHelpers.IsExposedForPaintTiming(_rootElement, document: null) ? _rootElement : null;

    /// <summary>Gets the time of the end of the rendering update that painted the content.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime"/>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime => _paintTimingInfo.RenderingUpdateEndTime;

    /// <summary>Gets the time the frame was presented to the user: null, the presentation time is not supported.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime"/>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => _paintTimingInfo.PresentationTime;
}

// The content attributes of the container timing (https://w3c.github.io/container-timing/)
public partial class HtmlElement
{
    /// <summary>Gets or sets the <c>containertiming</c> attribute: the identifier of the container timing entries of the element, which makes it a container root.</summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-htmlelement-containertiming"/>
    [IdlMember("containerTiming", "https://w3c.github.io/container-timing/#dom-htmlelement-containertiming")]
    public string ContainerTiming
    {
        get => Reflection.GetString(this, "containertiming");
        set => Reflection.SetString(this, "containertiming", value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the element has the <c>containertimingignore</c> attribute: the paints of the element and
    /// of its subtree are not reported to the container roots of its ancestors.
    /// </summary>
    /// <seealso href="https://w3c.github.io/container-timing/#dom-htmlelement-containertimingignore"/>
    [IdlMember("containerTimingIgnore", "https://w3c.github.io/container-timing/#dom-htmlelement-containertimingignore")]
    public bool ContainerTimingIgnore
    {
        get => Reflection.GetBoolean(this, "containertimingignore");
        set => Reflection.SetBoolean(this, "containertimingignore", value);
    }
}
