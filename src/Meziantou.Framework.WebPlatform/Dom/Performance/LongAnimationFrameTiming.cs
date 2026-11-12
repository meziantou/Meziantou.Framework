namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A long animation frame (https://w3c.github.io/long-animation-frames/#sec-PerformanceLongAnimationFrameTiming): a task
/// after which the rendering was not updated, or the tasks that ran until the rendering was updated, that lasted 50
/// milliseconds or more. It is reported to the <c>long-animation-frame</c> performance observers of the window whose tasks
/// ran, and is available from its performance timeline. <see cref="Scripts"/> lists the scripts that ran 5 milliseconds or more.
/// </summary>
/// <remarks>The Long Animation Frames API is a draft that the browsers based on Chromium implement.</remarks>
/// <seealso href="https://w3c.github.io/long-animation-frames/#performancelonganimationframetiming"/>
[IdlInterface("PerformanceLongAnimationFrameTiming", "https://w3c.github.io/long-animation-frames/#performancelonganimationframetiming")]
public sealed class PerformanceLongAnimationFrameTiming : PerformanceEntry
{
    /// <summary>The entry type and the name of the long animation frame entries.</summary>
    internal const string LongAnimationFrameEntryType = "long-animation-frame";

    /// <summary>The maximum size of the performance entry buffer of the long animation frames (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 200;

    /// <summary>The minimum duration of a long animation frame, in milliseconds (https://w3c.github.io/long-animation-frames/#long-animation-frame).</summary>
    internal const double Threshold = 50;

    /// <summary>The minimum duration of a script reported by <see cref="Scripts"/>, in milliseconds (https://w3c.github.io/long-animation-frames/#finish-the-pending-script).</summary>
    internal const double ScriptThreshold = 5;

    private readonly double _endTime;
    private readonly double[] _taskDurations;

    private PerformanceLongAnimationFrameTiming(Window window, double startTime, double endTime, double renderStart, double styleAndLayoutStart, double firstUIEventTimestamp, double paintTime, double[] taskDurations)
        : base(window, LongAnimationFrameEntryType, LongAnimationFrameEntryType, startTime, (paintTime != 0 ? paintTime : endTime) - startTime)
    {
        _endTime = endTime;
        _taskDurations = taskDurations;
        RenderStart = renderStart;
        StyleAndLayoutStart = styleAndLayoutStart;
        FirstUIEventTimestamp = firstUIEventTimestamp;
        PaintTime = paintTime;
    }

    /// <summary>Gets <c>long-animation-frame</c>.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-name"/>
    [IdlMember("name", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-name")]
    public new string Name => base.Name;

    /// <summary>Gets <c>long-animation-frame</c>.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-entrytype"/>
    [IdlMember("entryType", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-entrytype")]
    public new string EntryType => base.EntryType;

    /// <summary>Gets the time of the start of the first task of the frame, in milliseconds relative to the time origin of the window of the entry.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-starttime"/>
    [IdlMember("startTime", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-starttime")]
    public new double StartTime => base.StartTime;

    /// <summary>Gets the duration of the frame, from the start of its first task to the end of its rendering update (<see cref="PaintTime"/>), or to the end of its last task when the rendering was not updated.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-duration"/>
    [IdlMember("duration", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-duration")]
    public new double Duration => base.Duration;

    /// <summary>Gets the time of the start of the rendering update of the frame, or 0 when the frame did not update the rendering.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-renderstart"/>
    [IdlMember("renderStart", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-renderstart")]
    public double RenderStart { get; }

    /// <summary>
    /// Gets the time of the start of the style and layout step of the rendering update of the frame (after the animation
    /// frame callbacks), or 0 when the frame did not update the rendering.
    /// </summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-styleandlayoutstart"/>
    [IdlMember("styleAndLayoutStart", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-styleandlayoutstart")]
    public double StyleAndLayoutStart { get; }

    /// <summary>Gets the time spent recalculating the styles in the rendering update of the frame: always 0, the library does not measure it.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-styleduration"/>
    [IdlMember("styleDuration", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-styleduration")]
    public double StyleDuration => 0;

    /// <summary>Gets the time spent updating the layout in the rendering update of the frame: always 0, the library does not measure it.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-layoutduration"/>
    [IdlMember("layoutDuration", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-layoutduration")]
    public double LayoutDuration => 0;

    /// <summary>
    /// Gets the time the frame blocked the main thread: the sum of the durations of its tasks beyond 50 milliseconds, the
    /// duration of the rendering update being added to the longest task.
    /// </summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-blockingduration"/>
    [IdlMember("blockingDuration", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-blockingduration")]
    public double BlockingDuration
    {
        get
        {
            var sortedTaskDurations = _taskDurations.OrderDescending().ToList();
            if (RenderStart != 0)
            {
                var renderEndTime = PaintTime != 0 ? PaintTime : _endTime;
                var renderDuration = renderEndTime - RenderStart;
                if (sortedTaskDurations.Count == 0)
                {
                    sortedTaskDurations.Add(renderDuration);
                }
                else
                {
                    sortedTaskDurations[0] += renderDuration;
                }
            }

            var totalBlockingDuration = 0d;
            foreach (var duration in sortedTaskDurations)
            {
                if (duration > Threshold)
                {
                    totalBlockingDuration += duration - Threshold;
                }
            }

            return totalBlockingDuration;
        }
    }

    /// <summary>Gets the time stamp of the first UI event (e.g. <c>click</c>, <c>keydown</c>) whose listeners ran in the frame, or 0.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-firstuieventtimestamp"/>
    [IdlMember("firstUIEventTimestamp", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-firstuieventtimestamp")]
    public double FirstUIEventTimestamp { get; }

    /// <summary>Gets the time of the end of the rendering update of the frame, or 0 when the frame did not update the rendering.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime"/>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime { get; }

    /// <summary>Gets the time the frame was presented: null, the library has no presentation time.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime"/>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => null;

    /// <summary>Gets the scripts that ran 5 milliseconds or more in the frame, in the order they started.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-scripts"/>
    [IdlMember("scripts", "https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-scripts")]
    public IReadOnlyList<PerformanceScriptTiming> Scripts { get; private set; } = [];

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#queue-a-long-animation-frame-entry: creates the entry of a frame and
    /// queues it to the performance timeline of its window.
    /// </summary>
    internal static void Queue(FrameTimingInfo timingInfo)
    {
        var window = timingInfo.Window;
        if (!window.Options.EffectiveFeatures.LongAnimationFramesEnabled)
            return;

        var startTime = window.ToRelativeHighResolutionTime(timingInfo.StartTime);
        var endTime = window.ToRelativeHighResolutionTime(timingInfo.EndTime);
        var renderStart = timingInfo.UpdateTheRenderingStartTime == 0 ? 0 : window.ToRelativeHighResolutionTime(timingInfo.UpdateTheRenderingStartTime);
        var styleAndLayoutStart = timingInfo.StyleAndLayoutStartTime == 0 ? 0 : window.ToRelativeHighResolutionTime(timingInfo.StyleAndLayoutStartTime);
        var firstUIEventTimestamp = timingInfo.FirstUIEventTimestamp ?? 0;
        var paintTime = renderStart == 0 ? 0 : endTime;
        var entry = new PerformanceLongAnimationFrameTiming(window, startTime, endTime, renderStart, styleAndLayoutStart, firstUIEventTimestamp, paintTime, [.. timingInfo.TaskDurations]);
        if (timingInfo.Scripts is { Count: > 0 } scripts)
        {
            var scriptEntries = new PerformanceScriptTiming[scripts.Count];
            for (var i = 0; i < scripts.Count; i++)
            {
                scriptEntries[i] = new PerformanceScriptTiming(window, scripts[i], GetWindowAttribution(window, scripts[i].Window));
            }

            entry.Scripts = scriptEntries;
        }

        window.Performance.QueueLongAnimationFrameEntry(entry);
    }

    /// <summary>The window attribution of a script, relative to the window of the entry (https://w3c.github.io/long-animation-frames/#dom-performancelonganimationframetiming-scripts).</summary>
    private static ScriptWindowAttribution GetWindowAttribution(Window entryWindow, Window? scriptWindow)
    {
        if (scriptWindow is null || !scriptWindow.Document.IsFullyActive)
            return ScriptWindowAttribution.Other;

        if (ReferenceEquals(scriptWindow, entryWindow))
            return ScriptWindowAttribution.Self;

        var entryNavigable = entryWindow.Document.BrowsingContext;
        var scriptNavigable = scriptWindow.Document.BrowsingContext;
        if (entryNavigable is null || scriptNavigable is null)
            return ScriptWindowAttribution.Other;

        if (IsAncestor(scriptNavigable, entryNavigable))
            return ScriptWindowAttribution.Ancestor;

        if (IsAncestor(entryNavigable, scriptNavigable))
            return ScriptWindowAttribution.Descendant;

        if (ReferenceEquals(entryNavigable.Top, scriptNavigable.Top))
            return ScriptWindowAttribution.SamePage;

        return ScriptWindowAttribution.Other;
    }

    private static bool IsAncestor(BrowsingContext ancestor, BrowsingContext context)
    {
        for (var current = context.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }
}

/// <summary>A script that ran 5 milliseconds or more in a long animation frame (https://w3c.github.io/long-animation-frames/#sec-PerformanceScriptTiming).</summary>
/// <seealso href="https://w3c.github.io/long-animation-frames/#performancescripttiming"/>
[IdlInterface("PerformanceScriptTiming", "https://w3c.github.io/long-animation-frames/#performancescripttiming")]
public sealed class PerformanceScriptTiming : PerformanceEntry
{
    /// <summary>The entry type and the name of the script entries.</summary>
    internal const string ScriptEntryType = "script";

    private readonly ScriptTimingInfo _timingInfo;

    internal PerformanceScriptTiming(Window entryWindow, ScriptTimingInfo timingInfo, ScriptWindowAttribution windowAttribution)
        : base(entryWindow, ScriptEntryType, ScriptEntryType, entryWindow.ToRelativeHighResolutionTime(timingInfo.StartTime), entryWindow.ToRelativeHighResolutionTime(timingInfo.EndTime) - entryWindow.ToRelativeHighResolutionTime(timingInfo.StartTime))
    {
        _timingInfo = timingInfo;
        WindowAttribution = windowAttribution;
        ExecutionStart = timingInfo.ExecutionStartTime == 0 ? 0 : entryWindow.ToRelativeHighResolutionTime(timingInfo.ExecutionStartTime);
        Invoker = GetInvoker(timingInfo);
    }

    /// <summary>Gets <c>script</c>.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-name"/>
    [IdlMember("name", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-name")]
    public new string Name => base.Name;

    /// <summary>Gets <c>script</c>.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-entrytype"/>
    [IdlMember("entryType", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-entrytype")]
    public new string EntryType => base.EntryType;

    /// <summary>Gets the time the script started, in milliseconds relative to the time origin of the window of the long animation frame.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-starttime"/>
    [IdlMember("startTime", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-starttime")]
    public new double StartTime => base.StartTime;

    /// <summary>Gets the duration of the script, including the microtasks that ran after it.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-duration"/>
    [IdlMember("duration", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-duration")]
    public new double Duration => base.Duration;

    /// <summary>Gets how the script was invoked: a script block, an event listener, a callback or a promise reaction.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-invokertype"/>
    [IdlMember("invokerType", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-invokertype")]
    public ScriptInvokerType InvokerType => _timingInfo.InvokerType;

    /// <summary>
    /// Gets what invoked the script: the URL of a script block, the target and the type of an event (e.g.
    /// <c>IMG#id.onload</c>), the name of a callback (e.g. <c>FrameRequestCallback</c>, <c>TimerHandler:setTimeout</c>),
    /// or the promise of a reaction (e.g. <c>Promise.then</c>).
    /// </summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-invoker"/>
    [IdlMember("invoker", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-invoker")]
    public string Invoker { get; }

    /// <summary>Gets the time the script started to execute (after its compilation), or 0.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-executionstart"/>
    [IdlMember("executionStart", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-executionstart")]
    public double ExecutionStart { get; }

    /// <summary>Gets the URL of the script that defines the code, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourceurl"/>
    [IdlMember("sourceURL", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourceurl")]
    public string SourceUrl => _timingInfo.SourceUrl;

    /// <summary>Gets the name of the function of a callback or an event listener, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourcefunctionname"/>
    [IdlMember("sourceFunctionName", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourcefunctionname")]
    public string SourceFunctionName => _timingInfo.SourceFunctionName;

    /// <summary>Gets the position (0-based) of the code in its script, or -1.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourcecharposition"/>
    [IdlMember("sourceCharPosition", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourcecharposition")]
    public long SourceCharPosition => _timingInfo.SourceCharPosition;

    /// <summary>Gets the line (1-based) of the code in its script, or -1.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourceline"/>
    [IdlMember("sourceLine", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourceline")]
    public long SourceLine => _timingInfo.SourceLine;

    /// <summary>Gets the column (1-based) of the code in its script, or -1.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourcecolumn"/>
    [IdlMember("sourceColumn", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-sourcecolumn")]
    public long SourceColumn => _timingInfo.SourceColumn;

    /// <summary>Gets the time the script was paused by a synchronous operation (e.g. a synchronous <c>XMLHttpRequest</c>).</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-pauseduration"/>
    [IdlMember("pauseDuration", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-pauseduration")]
    public double PauseDuration => _timingInfo.PauseDuration;

    /// <summary>Gets the time spent recalculating the styles and updating the layout synchronously while the script ran: always 0, the library does not measure it.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-forcedstyleandlayoutduration"/>
    [IdlMember("forcedStyleAndLayoutDuration", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-forcedstyleandlayoutduration")]
    public double ForcedStyleAndLayoutDuration => ForcedStyleDuration + ForcedLayoutDuration;

    /// <summary>Gets the time spent recalculating the styles synchronously while the script ran: always 0, the library does not measure it.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-forcedstyleduration"/>
    [IdlMember("forcedStyleDuration", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-forcedstyleduration")]
    public double ForcedStyleDuration => 0;

    /// <summary>Gets the time spent updating the layout synchronously while the script ran: always 0, the library does not measure it.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-forcedlayoutduration"/>
    [IdlMember("forcedLayoutDuration", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-forcedlayoutduration")]
    public double ForcedLayoutDuration => 0;

    /// <summary>Gets the window of the realm of the script, or null when its document is no longer fully active.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-window"/>
    [IdlMember("window", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-window")]
    public Window? Window => _timingInfo.Window is { Document.IsFullyActive: true } window ? window : null;

    /// <summary>Gets the relation between the window of the script and the window of the long animation frame.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-windowattribution"/>
    [IdlMember("windowAttribution", "https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-windowattribution")]
    public ScriptWindowAttribution WindowAttribution { get; }

    /// <summary>https://w3c.github.io/long-animation-frames/#dom-performancescripttiming-invoker</summary>
    private static string GetInvoker(ScriptTimingInfo timingInfo)
    {
        switch (timingInfo.InvokerType)
        {
            case ScriptInvokerType.ClassicScript or ScriptInvokerType.ModuleScript:
                return timingInfo.SourceUrl;

            case ScriptInvokerType.EventListener:
                var targetName = timingInfo.InvokerName;
                if (timingInfo.EventTargetElementId.Length > 0)
                {
                    targetName += "#" + timingInfo.EventTargetElementId;
                }
                else if (timingInfo.EventTargetElementSrc.Length > 0)
                {
                    targetName += "[src=\"" + timingInfo.EventTargetElementSrc + "\"]";
                }

                return targetName + ".on" + timingInfo.EventType;

            case ScriptInvokerType.ResolvePromise:
                return timingInfo.InvokerName + ".then";

            case ScriptInvokerType.RejectPromise:
                return timingInfo.InvokerName + ".catch";

            default:
                return timingInfo.InvokerName;
        }
    }
}

/// <summary>How a script of a long animation frame was invoked (https://w3c.github.io/long-animation-frames/#enumdef-scriptinvokertype).</summary>
/// <seealso href="https://w3c.github.io/long-animation-frames/#enumdef-scriptinvokertype"/>
[IdlInterface("ScriptInvokerType", "https://w3c.github.io/long-animation-frames/#enumdef-scriptinvokertype")]
public enum ScriptInvokerType
{
    /// <summary>A classic script block.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-classic-script"/>
    [IdlMember("classic-script", "https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-classic-script")]
    ClassicScript,

    /// <summary>A module script block.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-module-script"/>
    [IdlMember("module-script", "https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-module-script")]
    ModuleScript,

    /// <summary>An event listener.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-event-listener"/>
    [IdlMember("event-listener", "https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-event-listener")]
    EventListener,

    /// <summary>A callback passed to the platform (e.g. a timer, an animation frame callback, an observer).</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-user-callback"/>
    [IdlMember("user-callback", "https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-user-callback")]
    UserCallback,

    /// <summary>The reactions of a promise of the platform that was fulfilled.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-resolve-promise"/>
    [IdlMember("resolve-promise", "https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-resolve-promise")]
    ResolvePromise,

    /// <summary>The reactions of a promise of the platform that was rejected.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-reject-promise"/>
    [IdlMember("reject-promise", "https://w3c.github.io/long-animation-frames/#dom-scriptinvokertype-reject-promise")]
    RejectPromise,
}

/// <summary>The relation between the window of a script and the window of its long animation frame (https://w3c.github.io/long-animation-frames/#enumdef-scriptwindowattribution).</summary>
/// <seealso href="https://w3c.github.io/long-animation-frames/#enumdef-scriptwindowattribution"/>
[IdlInterface("ScriptWindowAttribution", "https://w3c.github.io/long-animation-frames/#enumdef-scriptwindowattribution")]
public enum ScriptWindowAttribution
{
    /// <summary>The script ran in the window of the entry.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-self"/>
    [IdlMember("self", "https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-self")]
    Self,

    /// <summary>The script ran in a window of a descendant navigable.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-descendant"/>
    [IdlMember("descendant", "https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-descendant")]
    Descendant,

    /// <summary>The script ran in a window of an ancestor navigable.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-ancestor"/>
    [IdlMember("ancestor", "https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-ancestor")]
    Ancestor,

    /// <summary>The script ran in another window of the same page.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-same-page"/>
    [IdlMember("same-page", "https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-same-page")]
    SamePage,

    /// <summary>The script ran in another window, or in a window whose document is no longer fully active.</summary>
    /// <seealso href="https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-other"/>
    [IdlMember("other", "https://w3c.github.io/long-animation-frames/#dom-scriptwindowattribution-other")]
    Other,
}
