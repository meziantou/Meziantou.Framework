namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A layout shift of a document (https://wicg.github.io/layout-instability/#sec-layout-shift): the score of the movement of
/// the visible boxes between two rendering updates, reported to the <c>layout-shift</c> performance observers of its window
/// (see <see cref="Document.ReportTheLayoutShift"/>).
/// </summary>
/// <seealso href="https://wicg.github.io/layout-instability/#layoutshift"/>
[IdlInterface("LayoutShift", "https://wicg.github.io/layout-instability/#layoutshift")]
public sealed class LayoutShift : PerformanceEntry
{
    /// <summary>The entry type of the layout shift entries.</summary>
    internal const string LayoutShiftEntryType = "layout-shift";

    /// <summary>The maximum size of the performance entry buffer of the layout shifts (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 150;

    internal LayoutShift(GlobalScope global, double startTime, double value, bool hadRecentInput, double lastInputTime, LayoutShiftAttribution[] sources)
        : base(global, LayoutShiftEntryType, name: "", startTime, duration: 0)
    {
        Value = value;
        HadRecentInput = hadRecentInput;
        LastInputTime = lastInputTime;
        Sources = sources;
    }

    /// <summary>Gets the layout shift value: the impact fraction multiplied by the distance fraction.</summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshift-value"/>
    [IdlMember("value", "https://wicg.github.io/layout-instability/#dom-layoutshift-value")]
    public double Value { get; }

    /// <summary>Gets whether an excluding input (e.g. a press of a mouse button or of a key) occurred less than 500 milliseconds before the shift.</summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshift-hadrecentinput"/>
    [IdlMember("hadRecentInput", "https://wicg.github.io/layout-instability/#dom-layoutshift-hadrecentinput")]
    public bool HadRecentInput { get; }

    /// <summary>Gets the time of the most recent excluding input of the document, or 0 when there was none.</summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshift-lastinputtime"/>
    [IdlMember("lastInputTime", "https://wicg.github.io/layout-instability/#dom-layoutshift-lastinputtime")]
    public double LastInputTime { get; }

    /// <summary>Gets the nodes that contributed the most to the shift (at most 5), sorted in descending order by the area of their impact region.</summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshift-sources"/>
    [IdlMember("sources", "https://wicg.github.io/layout-instability/#dom-layoutshift-sources")]
    public IReadOnlyList<LayoutShiftAttribution> Sources { get; }
}

/// <summary>A node that shifted in a <see cref="LayoutShift"/> (https://wicg.github.io/layout-instability/#sec-layout-shift-attribution).</summary>
/// <seealso href="https://wicg.github.io/layout-instability/#layoutshiftattribution"/>
[IdlInterface("LayoutShiftAttribution", "https://wicg.github.io/layout-instability/#layoutshiftattribution")]
public sealed class LayoutShiftAttribution
{
    private readonly Node? _node;

    internal LayoutShiftAttribution(Node? node, DomRectReadOnly previousRect, DomRectReadOnly currentRect)
    {
        _node = node;
        PreviousRect = previousRect;
        CurrentRect = currentRect;
    }

    /// <summary>
    /// Gets the node that shifted, or null when it is not connected, when it is in a shadow tree ("get an element") or for a
    /// pseudo-element (which is not a node).
    /// </summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshiftattribution-node"/>
    [IdlMember("node", "https://wicg.github.io/layout-instability/#dom-layoutshiftattribution-node")]
    public Node? Node => _node is { IsConnected: true } node && ReferenceEquals(node.GetRoot(), node.NodeDocument) ? node : null;

    /// <summary>Gets the smallest rectangle that contains the visible part of the node in the previous frame, in the coordinates of the viewport (CSS pixels).</summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshiftattribution-previousrect"/>
    [IdlMember("previousRect", "https://wicg.github.io/layout-instability/#dom-layoutshiftattribution-previousrect")]
    public DomRectReadOnly PreviousRect { get; }

    /// <summary>Gets the smallest rectangle that contains the visible part of the node in the current frame, in the coordinates of the viewport (CSS pixels).</summary>
    /// <seealso href="https://wicg.github.io/layout-instability/#dom-layoutshiftattribution-currentrect"/>
    [IdlMember("currentRect", "https://wicg.github.io/layout-instability/#dom-layoutshiftattribution-currentrect")]
    public DomRectReadOnly CurrentRect { get; }
}
