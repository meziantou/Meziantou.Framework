using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

// The layout shifts of a document (https://wicg.github.io/layout-instability/): the tracking of the layout from frame to frame,
// the excluding inputs, and "report the layout shift" of the rendering update
public partial class Document
{
    /// <summary>The time below which an excluding input makes a layout shift a shift with recent input (https://wicg.github.io/layout-instability/#dom-layoutshift-hadrecentinput).</summary>
    private const double RecentInputDuration = 500;

    private LayoutShiftTracker? _layoutShiftTracker;

    /// <summary>The timestamp (<see cref="DomEventLoop.TimeProvider"/>) of the most recent excluding input, or null when there was none.</summary>
    private long? _lastExcludingInputTimestamp;

    /// <summary>Whether the layout shifts of the document are computed by the rendering updates.</summary>
    internal bool IsTrackingLayoutShifts => _layoutShiftTracker is not null;

    /// <summary>
    /// Starts to compute the layout shifts of the document (a performance observer of its window observes the <c>layout-shift</c>
    /// entries): the document is laid out by every rendering update from then on, which compares its layout with the one of the
    /// previous rendering update. The layout is computed when the tracking starts, as the previous frame of the next rendering
    /// update: the changes made after the call of <c>observe()</c> are layout shifts (the ones made before it in the same task
    /// are not).
    /// </summary>
    internal void StartLayoutShiftTracking()
    {
        if (_layoutShiftTracker is not null || BrowsingContext is not { Options.EffectiveFeatures.LayoutInstabilityEnabled: true } || EventLoop is not { } eventLoop)
            return;

        // The current layout is the previous frame of the next rendering update (the document may not have been laid out by
        // the previous rendering updates, when nothing read its geometry)
        var layoutEngine = GetLayoutEngine();
        _layoutShiftTracker = new LayoutShiftTracker(layoutEngine);
        eventLoop.AddDocumentWithLayoutShiftTracking(this);
        layoutEngine.EnsureLayout();
        _layoutShiftTracker.Update();
        eventLoop.RequestRenderingUpdate();
    }

    /// <summary>
    /// Records the time of a trusted event when it is an excluding input (https://wicg.github.io/layout-instability/#excluding-input):
    /// a press of a mouse button or of a key, and a move of the mouse with a pressed button (a drag). The moves without a pressed
    /// button are not excluding inputs.
    /// </summary>
    internal void NotifyLayoutShiftInput(Event @event)
    {
        var isExcludingInput = @event switch
        {
            KeyboardEvent { Type: "keydown" } => true,
            MouseEvent { Type: "mousedown" or "pointerdown" } => true,
            MouseEvent { Type: "mousemove" or "pointermove", Buttons: not 0 } => true,
            _ => false,
        };

        if (isExcludingInput && EventLoop is { } eventLoop)
        {
            _lastExcludingInputTimestamp = eventLoop.TimeProvider.GetTimestamp();
        }
    }

    /// <summary>
    /// https://wicg.github.io/layout-instability/#report-the-layout-shift: computes the layout shift of the current frame and,
    /// when its value is not 0, queues a <see cref="LayoutShift"/> entry to the performance timeline of the window.
    /// </summary>
    internal void ReportTheLayoutShift()
    {
        if (_layoutShiftTracker is null || DefaultView is not { } window)
            return;

        GetLayoutEngine().UpdateLayout();
        if (_layoutShiftTracker.Update() is not { } shift)
            return;

        var startTime = window.GetCurrentHighResolutionTime();
        var lastInputTime = _lastExcludingInputTimestamp is { } timestamp ? window.ToRelativeHighResolutionTime(timestamp) : 0;
        var hadRecentInput = _lastExcludingInputTimestamp is not null && startTime - lastInputTime < RecentInputDuration;

        // https://wicg.github.io/layout-instability/#create-the-attribution: the rectangles in CSS pixels
        var sources = new LayoutShiftAttribution[shift.Sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            var source = shift.Sources[i];
            sources[i] = new LayoutShiftAttribution(source.Node, ToDomRect(source.PreviousRect), ToDomRect(source.CurrentRect));
        }

        window.Performance.QueueLayoutShiftEntry(new LayoutShift(window, startTime, shift.Value, hadRecentInput, lastInputTime, sources));

        static DomRectReadOnly ToDomRect(PhysicalRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
