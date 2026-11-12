using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

// The long animation frames (https://w3c.github.io/long-animation-frames/): the tasks that run scripts, until the rendering
// is updated, and the scripts that run in them
public sealed partial class DomEventLoop
{
    // The last timestamp read from the clock by the event loop: the current time of a fake clock, which only advances when it
    // is read (see PeekTimestamp)
    private long _lastTimestamp;

    // The start of the current task (a fake clock), or 0 until it is needed (the system clock)
    private long _frameTaskStart;

    // The frames whose windows ran scripts in the current task
    private List<FrameTimingInfo>? _taskFrames;

    // The frames whose task ended while a rendering update was scheduled: they end with the rendering update
    private List<FrameTimingInfo>? _framesAwaitingRendering;

    // A frame that ended without being reported, reused by the next frame
    private FrameTimingInfo? _spareFrame;

    // A script timing info that was not reported, reused by the next script entry point
    private ScriptTimingInfo? _spareScript;

    // The script entry point that runs (https://w3c.github.io/long-animation-frames/#frame-timing-info-pending-script), with its frames
    private ScriptTimingInfo? _pendingScript;
    private FrameTimingInfo? _pendingScriptFrame;
    private FrameTimingInfo? _pendingScriptRootFrame;

    // Whether the pending script runs outside of a task: it is timed as a task (https://w3c.github.io/long-animation-frames/#start-recording-task-time-if-needed)
    private bool _pendingScriptIsTask;

    private bool _inRenderingUpdate;
    private bool _renderingStyleAndLayoutStarted;
    private long _renderingStart;
    private long _renderingStyleAndLayoutStart;

    /// <summary>
    /// The window whose callbacks the event loop runs (the animation frame callbacks and the idle callbacks of a window): the
    /// long animation frame of this window times the callbacks, whatever the realm of their functions.
    /// </summary>
    internal Window? CallbackOwnerWindow { get; set; }

    /// <summary>Gets whether a script entry point runs: the scripts that it runs are timed with it.</summary>
    internal bool HasPendingScriptEntryPoint => _pendingScript is not null;

    private bool IsSystemClock => ReferenceEquals(TimeProvider, TimeProvider.System);

    /// <summary>Records a timestamp read from the clock by the event loop (see <see cref="PeekTimestamp"/>).</summary>
    private long ObserveClock(long timestamp)
    {
        if (timestamp > _lastTimestamp)
        {
            _lastTimestamp = timestamp;
        }

        return timestamp;
    }

    /// <summary>
    /// The current time for the long animation frames. The system clock is read. Another clock (e.g. a fake clock, which may
    /// advance each time it is read) is not read: the time is the last time the event loop read it (e.g. by
    /// <c>performance.now()</c>, or to run the timers), so that timing the frames does not change the timers of the pages.
    /// </summary>
    private long PeekTimestamp() => IsSystemClock ? ObserveClock(TimeProvider.GetTimestamp()) : _lastTimestamp;

    private double ToMilliseconds(long duration) => duration * 1000d / TimeProvider.TimestampFrequency;

    /// <summary>The start of the current task (https://w3c.github.io/long-animation-frames/#record-task-start-time).</summary>
    private long GetFrameTaskStart()
    {
        if (_frameTaskStart == 0)
        {
            _frameTaskStart = _taskTimed ? _taskStartTimestamp : PeekTimestamp();
        }

        return _frameTaskStart;
    }

    /// <summary>Starts timing a task for the long animation frames (the system clock is read only when a script runs).</summary>
    private void StartFrameTask() => _frameTaskStart = IsSystemClock ? 0 : _lastTimestamp;

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#record-task-start-time: the frame of a window whose scripts run in the
    /// current task, started when the window has none. Returns null when the frame is not timed (the document is hidden or
    /// not fully active).
    /// </summary>
    private FrameTimingInfo? RecordFrameTask(Window window)
    {
        if (!window.Options.EffectiveFeatures.LongAnimationFramesEnabled)
            return null;

        var document = window.Document;
        if (!document.IsFullyActive || !document.IsVisible)
        {
            DiscardFrame(window);
            return null;
        }

        var frame = window.CurrentFrameTimingInfo;
        if (frame is null)
        {
            // A frame that starts in a rendering update starts with it
            var start = _inRenderingUpdate ? _renderingStart : GetFrameTaskStart();
            frame = _spareFrame ?? new FrameTimingInfo();
            _spareFrame = null;
            frame.Window = window;
            frame.StartTime = start;
            frame.CurrentTaskStartTime = start;
            window.CurrentFrameTimingInfo = frame;

            // The scripts that run after the styles and the layout were updated (e.g. the resize observer callbacks) are in the
            // rendering update of the frame
            if (_renderingStyleAndLayoutStarted)
            {
                frame.UpdateTheRenderingStartTime = _renderingStart;
                frame.StyleAndLayoutStartTime = _renderingStyleAndLayoutStart;
            }
        }
        else if (!frame.InCurrentTask)
        {
            frame.CurrentTaskStartTime = _inRenderingUpdate ? _renderingStart : GetFrameTaskStart();
        }

        if (!frame.InCurrentTask)
        {
            frame.InCurrentTask = true;
            (_taskFrames ??= []).Add(frame);
            _framesAwaitingRendering?.Remove(frame);
        }

        return frame;
    }

    /// <summary>Discards the frame of a window whose document became hidden or is no longer fully active.</summary>
    private void DiscardFrame(Window window)
    {
        if (window.CurrentFrameTimingInfo is not { } frame)
            return;

        window.CurrentFrameTimingInfo = null;
        frame.Discarded = true;
        _framesAwaitingRendering?.Remove(frame);
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#record-task-end-time: ends the current task of the frames whose windows
    /// ran scripts in it. A frame ends with the task unless a rendering update is scheduled, and is reported when it lasted
    /// 50 milliseconds or more.
    /// </summary>
    private void EndFrameTask(long end)
    {
        if (_taskFrames is not { Count: > 0 } frames)
            return;

        // Ending a frame runs no script: the list does not change while it is enumerated
        foreach (var frame in frames)
        {
            frame.InCurrentTask = false;
            if (frame.Discarded)
                continue;

            if (frame.UpdateTheRenderingStartTime != 0)
            {
                // The frame ends with its rendering update
                EndFrame(frame, end);
                continue;
            }

            frame.TaskDurations.Add(ToMilliseconds(end - frame.CurrentTaskStartTime));
            if (_renderingScheduled && !_inRenderingUpdate)
            {
                (_framesAwaitingRendering ??= []).Add(frame);
            }
            else
            {
                EndFrame(frame, end);
            }
        }

        frames.Clear();
    }

    private void EndFrame(FrameTimingInfo frame, long end)
    {
        if (ReferenceEquals(frame.Window.CurrentFrameTimingInfo, frame))
        {
            frame.Window.CurrentFrameTimingInfo = null;
        }

        frame.EndTime = end;
        if (ToMilliseconds(end - frame.StartTime) >= PerformanceLongAnimationFrameTiming.Threshold && frame.Window.Document is { IsFullyActive: true, IsVisible: true })
        {
            PerformanceLongAnimationFrameTiming.Queue(frame);
        }
        else
        {
            // A short frame is reused for the next one (most tasks are short)
            frame.Reset();
            _spareFrame = frame;
        }
    }

    /// <summary>Starts the rendering update for the long animation frames: the frames that wait for it continue in its task.</summary>
    private void StartRenderingUpdateFrames(long renderingStart)
    {
        ObserveClock(renderingStart);
        _inRenderingUpdate = true;
        _renderingStyleAndLayoutStarted = false;
        _renderingStart = renderingStart;
        if (_framesAwaitingRendering is not { Count: > 0 } frames)
            return;

        foreach (var frame in frames)
        {
            if (frame.Discarded || frame.InCurrentTask)
                continue;

            frame.InCurrentTask = true;
            frame.CurrentTaskStartTime = renderingStart;
            (_taskFrames ??= []).Add(frame);
        }

        frames.Clear();
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#record-rendering-time: the styles and the layout are about to be updated.
    /// The frames end with the rendering update. As in Chromium, a frame is not discarded when it lasted less than 50
    /// milliseconds so far: the scripts that run after the styles and the layout (e.g. the resize observer callbacks) can make
    /// it a long animation frame.
    /// </summary>
    private void RecordRenderingTime()
    {
        if (!_inRenderingUpdate)
            return;

        _renderingStyleAndLayoutStarted = true;
        _renderingStyleAndLayoutStart = PeekTimestamp();
        if (_taskFrames is not { Count: > 0 } frames)
            return;

        foreach (var frame in frames)
        {
            if (frame.Discarded || frame.UpdateTheRenderingStartTime != 0)
                continue;

            frame.UpdateTheRenderingStartTime = frame.CurrentTaskStartTime;
            frame.StyleAndLayoutStartTime = _renderingStyleAndLayoutStart;
        }
    }

    /// <summary>Ends the rendering update for the long animation frames: the scripts that run afterwards are not in it.</summary>
    private void EndRenderingUpdateFrames()
    {
        _inRenderingUpdate = false;
        _renderingStyleAndLayoutStarted = false;
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#create-script-entry-point: a script starts to run for a window (whose
    /// long animation frame times it). Nothing is recorded while another script entry point runs (it is timed with it).
    /// </summary>
    /// <param name="owner">The window whose task runs the script.</param>
    /// <param name="scriptGlobal">The global object of the realm of the script (the window of the script).</param>
    /// <param name="invokerType">How the script is invoked.</param>
    /// <param name="invokerName">The name of the invoker (e.g. <c>FrameRequestCallback</c>, the target of an event listener).</param>
    /// <param name="sourceCallback">The callback whose source location is reported (a function, a delegate or a script callback), or null.</param>
    /// <returns>The timing info of the script, or null when it is not recorded.</returns>
    internal ScriptTimingInfo? RecordScriptEntryPoint(Window owner, GlobalScope? scriptGlobal, ScriptInvokerType invokerType, string invokerName, object? sourceCallback)
    {
        if (_pendingScript is not null || IsDisposed)
            return null;

        var isTask = _taskDepth == 0;
        if (isTask)
        {
            // A script that runs outside of a task (e.g. an event dispatched by the host) is timed as a task
            StartFrameTask();
        }

        var frame = RecordFrameTask(owner);
        if (frame is null)
            return null;

        // The scripts of a document that run in a rendering update are also in the frame of its nearest same-origin root,
        // whose rendering update includes the rendering of the document
        FrameTimingInfo? rootFrame = null;
        if (_inRenderingUpdate && GetNearestSameOriginRoot(owner) is { } root && !ReferenceEquals(root, owner))
        {
            rootFrame = RecordFrameTask(root);
        }

        var now = PeekTimestamp();
        var script = _spareScript ?? new ScriptTimingInfo();
        _spareScript = null;
        script.InvokerType = invokerType;
        script.InvokerName = invokerName;
        script.StartTime = now;
        script.ExecutionStartTime = now;
        script.ScriptWindow = scriptGlobal as Window;
        script.SourceCallback = sourceCallback;
        script.ScriptEngine = scriptGlobal?.ScriptEngine;

        _pendingScript = script;
        _pendingScriptFrame = frame;
        _pendingScriptRootFrame = rootFrame;
        _pendingScriptIsTask = isTask;
        return script;
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#record-timing-info-for-user-callback: a callback function runs in the
    /// long animation frame of the window whose callbacks run (<see cref="CallbackOwnerWindow"/>), or of its own window.
    /// </summary>
    internal void RecordUserCallbackScript(GlobalScope callbackGlobal, string callbackName, object function)
    {
        if (_pendingScript is not null || (CallbackOwnerWindow ?? callbackGlobal as Window) is not { } owner)
            return;

        RecordScriptEntryPoint(owner, callbackGlobal, ScriptInvokerType.UserCallback, callbackName, function);
    }

    /// <summary>https://w3c.github.io/long-animation-frames/#record-timing-info-for-event-listener</summary>
    internal void RecordEventListenerScript(Window owner, GlobalScope? scriptGlobal, Event @event, object listenerCallback)
    {
        if (_pendingScript is not null)
            return;

        var target = @event.CurrentTarget;
        if (RecordScriptEntryPoint(owner, scriptGlobal, ScriptInvokerType.EventListener, invokerName: "", listenerCallback) is not { } script)
            return;

        script.EventType = @event.Type;
        script.EventTarget = target;
        if (@event is UIEvent && _pendingScriptFrame is { FirstUIEventTimestamp: null } frame)
        {
            frame.FirstUIEventTimestamp = @event.TimeStamp;
        }
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#record-timing-info-for-promise-resolver: a promise of the platform is
    /// settled; its reactions run in the next microtask checkpoint, which ends the script.
    /// </summary>
    internal void RecordPromiseResolverScript(GlobalScope global, bool rejected, string invokerName = "Promise")
    {
        if (_pendingScript is not null || global is not Window window)
            return;

        RecordScriptEntryPoint(window, window, rejected ? ScriptInvokerType.RejectPromise : ScriptInvokerType.ResolvePromise, invokerName, sourceCallback: null);
    }

    /// <summary>https://w3c.github.io/long-animation-frames/#record-classic-script-creation-time, for a classic or a module script block.</summary>
    internal void RecordScriptBlock(Window window, ScriptInvokerType invokerType, string? url, bool mutedErrors)
    {
        if (_pendingScript is not null)
            return;

        if (RecordScriptEntryPoint(window, window, invokerType, invokerName: "", sourceCallback: null) is not { } script)
            return;

        // https://w3c.github.io/long-animation-frames/#set-source-url-for-script-block
        script.SourceCharPosition = 0;
        if (url is null)
            return;

        script.SourceUrl = GetExposedSourceUrl(url);
        if (!mutedErrors)
        {
            script.SourceLine = 1;
            script.SourceColumn = 1;
        }
    }

    /// <summary>The source URL exposed by the script timings: an HTTP(S) or a blob URL, <c>data:</c> for a data URL, or the empty string.</summary>
    internal static string GetExposedSourceUrl(string url)
    {
        if (url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return "data:";

        return "";
    }

    /// <summary>The start of a pause of the running script (see <see cref="RecordPauseDuration"/>), or 0 when no script entry point runs.</summary>
    internal long GetPauseStartTimestamp() => _pendingScript is null ? 0 : PeekTimestamp();

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#record-pause-duration: the running script was paused since
    /// <paramref name="start"/> (e.g. by a synchronous request). A fake clock is not read: a pause that does not read it
    /// (e.g. a host that does not advance it while a synchronous request of a window blocks the event loop) lasts 0 ms.
    /// </summary>
    internal void RecordPauseDuration(long start)
    {
        if (start == 0 || _pendingScript is not { } script)
            return;

        script.PauseDuration += ToMilliseconds(PeekTimestamp() - start);
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#finish-the-pending-script: ends the script entry point once the
    /// microtasks that it queued ran (the microtask checkpoint that follows the script), or at the end of the task.
    /// </summary>
    private void FinishPendingScript()
    {
        if (_pendingScript is not { } script)
            return;

        var frame = _pendingScriptFrame!;
        var rootFrame = _pendingScriptRootFrame;
        var isTask = _pendingScriptIsTask;
        _pendingScript = null;
        _pendingScriptFrame = null;
        _pendingScriptRootFrame = null;
        _pendingScriptIsTask = false;

        var end = PeekTimestamp();
        script.EndTime = end;
        if (ToMilliseconds(end - script.StartTime) > PerformanceLongAnimationFrameTiming.ScriptThreshold)
        {
            script.Complete();
            if (!frame.Discarded)
            {
                frame.Scripts.Add(script);
            }

            if (rootFrame is { Discarded: false })
            {
                rootFrame.Scripts.Add(script);
            }
        }
        else
        {
            script.Reset();
            _spareScript = script;
        }

        if (isTask)
        {
            EndFrameTask(end);
        }
    }

    /// <summary>Ends the script entry point that runs, before the microtask checkpoint that would end it (e.g. the evaluation of a module imported by <c>import()</c>, before the reactions of its promise).</summary>
    internal void FinishScriptEntryPoint() => FinishPendingScript();

    /// <summary>Called at the end of a microtask checkpoint: ends the pending script when no script runs.</summary>
    private void OnMicrotaskCheckpointCompleted()
    {
        if (_pendingScript is not null && !IsScriptRunning)
        {
            FinishPendingScript();
        }
    }

    /// <summary>Called at the end of a task: ends the pending script and the current task of the frames.</summary>
    private void OnFrameTaskEnded()
    {
        if (_pendingScript is null && _taskFrames is not { Count: > 0 })
        {
            if (_inRenderingUpdate)
            {
                EndRenderingUpdateFrames();
            }

            return;
        }

        FinishPendingScript();
        EndFrameTask(IsSystemClock ? (_taskTimed ? _taskEndTimestamp : PeekTimestamp()) : _lastTimestamp);
        EndRenderingUpdateFrames();
    }

    /// <summary>
    /// https://w3c.github.io/long-animation-frames/#nearest-same-origin-root: the topmost ancestor whose document is same
    /// origin with the document of the window, or the window.
    /// </summary>
    private static Window? GetNearestSameOriginRoot(Window window)
    {
        var context = window.Document.BrowsingContext;
        if (context?.Parent is null)
            return window;

        Window? result = null;
        for (var ancestor = context.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Document is { RelevantWindow: { } ancestorWindow } ancestorDocument && OriginRecord.IsSameOrigin(ancestorDocument.Origin, window.Document.Origin))
            {
                result = ancestorWindow;
            }
        }

        return result ?? window;
    }
}

/// <summary>The bookkeeping of a long animation frame of a window (https://w3c.github.io/long-animation-frames/#frame-timing-info).</summary>
internal sealed class FrameTimingInfo
{
    public Window Window { get; set; } = null!;

    public long StartTime { get; set; }

    public long CurrentTaskStartTime { get; set; }

    public long UpdateTheRenderingStartTime { get; set; }

    public long StyleAndLayoutStartTime { get; set; }

    public long EndTime { get; set; }

    /// <summary>The time stamp of the first UI event, relative to the time origin of the window, or null.</summary>
    public double? FirstUIEventTimestamp { get; set; }

    public List<double> TaskDurations { get; } = [];

    public List<ScriptTimingInfo> Scripts { get; } = [];

    /// <summary>Whether the window ran scripts in the current task.</summary>
    public bool InCurrentTask { get; set; }

    /// <summary>Whether the frame was discarded (its document became hidden, or it lasted less than 50 milliseconds when the rendering was updated).</summary>
    public bool Discarded { get; set; }

    /// <summary>Clears the frame, to reuse it.</summary>
    public void Reset()
    {
        Window = null!;
        StartTime = 0;
        CurrentTaskStartTime = 0;
        UpdateTheRenderingStartTime = 0;
        StyleAndLayoutStartTime = 0;
        EndTime = 0;
        FirstUIEventTimestamp = null;
        TaskDurations.Clear();
        Scripts.Clear();
        InCurrentTask = false;
        Discarded = false;
    }
}

/// <summary>The bookkeeping of a script entry point of a long animation frame (https://w3c.github.io/long-animation-frames/#script-timing-info).</summary>
internal sealed class ScriptTimingInfo
{
    public ScriptInvokerType InvokerType { get; set; }

    public string InvokerName { get; set; } = "";

    public long StartTime { get; set; }

    public long EndTime { get; set; }

    public long ExecutionStartTime { get; set; }

    public double PauseDuration { get; set; }

    public string SourceUrl { get; set; } = "";

    public string SourceFunctionName { get; set; } = "";

    public string EventType { get; set; } = "";

    public string EventTargetElementId { get; set; } = "";

    public string EventTargetElementSrc { get; set; } = "";

    public long SourceCharPosition { get; set; } = -1;

    public long SourceLine { get; set; } = -1;

    public long SourceColumn { get; set; } = -1;

    /// <summary>The window of the script while it runs; a weak reference once it is reported (<see cref="WindowReference"/>).</summary>
    public Window? ScriptWindow { get; set; }

    public WeakReference<Window>? WindowReference { get; private set; }

    public Window? Window => WindowReference is not null && WindowReference.TryGetTarget(out var window) ? window : null;

    /// <summary>The callback whose source location is applied when the script is kept (it lasted more than 5 milliseconds).</summary>
    public object? SourceCallback { get; set; }

    /// <summary>The target of an event listener, whose name is computed when the script is kept.</summary>
    public EventTarget? EventTarget { get; set; }

    public ScriptEngine? ScriptEngine { get; set; }

    /// <summary>Clears the script timing info, to reuse it.</summary>
    public void Reset()
    {
        InvokerName = "";
        EndTime = 0;
        PauseDuration = 0;
        SourceUrl = "";
        SourceFunctionName = "";
        EventType = "";
        EventTargetElementId = "";
        EventTargetElementSrc = "";
        SourceCharPosition = -1;
        SourceLine = -1;
        SourceColumn = -1;
        ScriptWindow = null;
        WindowReference = null;
        SourceCallback = null;
        EventTarget = null;
        ScriptEngine = null;
    }

    /// <summary>
    /// Computes the information of a script that is reported (it lasted more than 5 milliseconds): the target of an event
    /// listener (https://w3c.github.io/long-animation-frames/#record-timing-info-for-event-listener) and the source location
    /// of the callback (https://w3c.github.io/long-animation-frames/#apply-source-location).
    /// </summary>
    public void Complete()
    {
        if (ScriptWindow is { } window)
        {
            WindowReference = new WeakReference<Window>(window);
            ScriptWindow = null;
        }

        if (EventTarget is { } target)
        {
            EventTarget = null;
            if (target is Node node)
            {
                InvokerName = node.NodeName;
                if (node is Element element)
                {
                    EventTargetElementId = element.Id;
                    EventTargetElementSrc = element.GetAttributeValue("src") ?? "";
                }
            }
            else
            {
                InvokerName = ScriptEngine?.GetInterfaceName(target) ?? target.GetType().Name;
            }
        }

        if (SourceCallback is { } callback)
        {
            SourceCallback = null;
            if (ScriptEngine?.GetCallbackSourceLocation(callback) is { } location)
            {
                SourceUrl = location.Url is null ? "" : DomEventLoop.GetExposedSourceUrl(location.Url);
                if (!location.MutedErrors)
                {
                    SourceFunctionName = location.FunctionName;
                    SourceCharPosition = location.CharPosition;
                    SourceLine = location.Line;
                    SourceColumn = location.Column;
                }
            }
        }

        ScriptEngine = null;
    }
}
