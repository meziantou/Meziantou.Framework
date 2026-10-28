namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The window event loop (https://html.spec.whatwg.org/multipage/webappapis.html#window-event-loop) of a browsing context
/// group: each top-level <see cref="BrowsingContext"/> has its own event loop, shared by its nested browsing contexts
/// (<see cref="BrowsingContext.EventLoop"/>). It runs the tasks and the microtasks of their documents (load events,
/// timers, mutation observers, slotchange events, scripts...). The documents that are not associated with a browsing
/// context (e.g. a document parsed by <see cref="HtmlDocumentParser"/>) have no event
/// loop: the tasks and the microtasks of their nodes are not queued.
/// The host drives the loop: call <see cref="PerformMicrotaskCheckpoint"/>, <see cref="RunUntilIdle"/>
/// or <see cref="RunUntilIdleAsync"/>.
/// Tasks can be queued from any thread (e.g. when an asynchronous operation completes); they run on the thread that runs the loop.
/// When a browsing context has a <see cref="Scripting.ScriptEngine"/>, the microtasks are queued in the job queue of the
/// engine, so that they run in order with the promise jobs.
/// </summary>
/// <remarks>
/// In the specification, the agents of a browsing context group are keyed by site, and each agent has its own event loop.
/// The library runs all the documents of a browsing context group on a single thread, so a single event loop runs the
/// tasks of all its agents in one of the orders allowed by the specification.
/// </remarks>
#pragma warning disable CA1001 // The signal of a worker is set from other threads after its loop ends: it is not disposed (it allocates no handle)
public sealed partial class DomEventLoop
#pragma warning restore CA1001
{
    private readonly Queue<Action> _microtasks = new();
    private readonly Queue<Action> _tasks = new();
    private readonly Lock _tasksLock = new();
    private TaskCompletionSource? _taskQueuedSignal;
    private int _pendingOperations;
    private bool _performingMicrotaskCheckpoint;
    private readonly List<WeakReference<Scripting.ScriptEngine>> _scriptEngines = [];
    private List<GlobalScope>? _globalsWithRejectedPromises;
    private bool _isDisposed;

    /// <summary>Creates the event loop of a browsing context group (<see cref="BrowsingContext"/>).</summary>
    internal DomEventLoop()
    {
    }

    /// <summary>
    /// Gets or sets a function that queues microtasks in another queue (for instance the job queue of a JavaScript engine),
    /// so that microtasks queued by the DOM and by scripts are interleaved in order.
    /// </summary>
    [Obsolete("Use BrowsingContextOptions.ScriptEngine: the microtasks are queued in the job queue of the script engine.")]
    public Action<Action>? MicrotaskQueueOverride { get; set; }

    /// <summary>Gets or sets whether queued microtasks run synchronously at the end of each DOM operation that queued them.</summary>
    public bool AutoPerformMicrotaskCheckpoint { get; set; }

    // Mutation observers state of the "surrounding agent"
    internal bool MutationObserverMicrotaskQueued { get; set; }

    internal List<MutationObserver> PendingMutationObservers { get; } = [];

    internal List<HtmlSlotElement> SignalSlots { get; } = [];

    // The backup element queue of the agent (https://html.spec.whatwg.org/multipage/custom-elements.html#backup-element-queue)
    private readonly Queue<Element> _backupElementQueue = new();
    private bool _processingBackupElementQueue;

    /// <summary>
    /// Gets whether the event loop was disposed with its browsing context (<see cref="BrowsingContext.Dispose"/>): the
    /// tasks, the microtasks and the timers queued afterwards are ignored.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _isDisposed);

    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// A task completed when the event loop is disposed (e.g. at the end of the cleanup of a terminated worker): its pending
    /// tasks are discarded, so an algorithm that waits for one of its tasks to run (from another thread) also waits for this task.
    /// </summary>
    internal Task WhenDisposed => _disposed.Task;

    // The timers can be queued from any thread (e.g. by a loader), and the event loop of the owner of a worker reads the
    // next timer of the worker
    private readonly PriorityQueue<Action, (long Due, long Sequence)> _timers = new();
    private readonly Lock _timersLock = new();
    private long _nextTimerSequence;

    /// <summary>
    /// Gets or sets the clock of the timers (<c>setTimeout()</c>, <c>requestAnimationFrame()</c>, <c>AbortSignal.timeout()</c>...)
    /// and of <c>performance.now()</c>. The default is <see cref="TimeProvider.System"/>. A browsing context created with
    /// <see cref="BrowsingContextOptions.TimeProvider"/> sets it. Use a fake clock (e.g. <c>FakeTimeProvider</c>) to run
    /// the timers without waiting.
    /// </summary>
    public TimeProvider TimeProvider
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = TimeProvider.System;

    /// <summary>
    /// Queues a task after a delay (e.g. <c>setTimeout()</c> or <c>AbortSignal.timeout()</c>), measured with <see cref="TimeProvider"/>.
    /// The timers are moved to the task queue once they are due (<see cref="PromoteDueTimers"/>, <see cref="RunUntilIdle"/>).
    /// The timers that are due at the same time run in the order they were queued.
    /// </summary>
    public void QueueTimer(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
#pragma warning disable CS0618 // The obsolete override is still supported
        if (TimerQueueOverride is { } queue)
        {
            queue(delay, callback);
            return;
        }
#pragma warning restore CS0618

        if (IsDisposed)
            return;

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        var due = TimeProvider.GetTimestamp() + (long)(delay.TotalSeconds * TimeProvider.TimestampFrequency);
        lock (_timersLock)
        {
            _timers.Enqueue(callback, (due, _nextTimerSequence++));
        }

        // The thread of a worker waits until its next timer
        if (Owner is not null)
        {
            SignalWork();
        }

        WorkSignal?.Invoke();
    }

    /// <summary>
    /// Gets or sets the callback invoked when work arrives, from any thread: a task is queued (e.g. by an asynchronous
    /// operation that completes on another thread, such as a document loaded by <see cref="BrowsingContextOptions.DocumentLoader"/>),
    /// a timer is queued, or an asynchronous operation ends. A host that runs the event loop on its own thread uses it to wake
    /// up and call <see cref="RunTask"/> and <see cref="PromoteDueTimers"/>. The callback must not run the event loop itself.
    /// </summary>
    public Action? WorkSignal { get; set; }

    /// <summary>
    /// Gets or sets a function that schedules the timers of the DOM (e.g. <c>AbortSignal.timeout()</c>) in the host's own
    /// timer queue, so they are ordered with the other timers of the host.
    /// </summary>
    [Obsolete("Use BrowsingContextOptions.TimeProvider or DomEventLoop.TimeProvider: the timers of the windows are implemented by the library.")]
    public Action<TimeSpan, Action>? TimerQueueOverride { get; set; }

    /// <summary>
    /// Moves the due timers to the task queue. Returns the time until the next timer, or null when there is none. The timers
    /// of the workers created by the documents of the event loop are included in the result (they run on the threads of the
    /// workers), so that a host that advances a fake clock to the next timer also runs the timers of the workers.
    /// </summary>
    public TimeSpan? PromoteDueTimers()
    {
        var next = PromoteOwnDueTimers();
        return HasChildLoops ? Min(next, GetTimeUntilNextChildTimer()) : next;
    }

    /// <summary>Moves the due timers of this event loop to the task queue. Returns the time until its next timer, or null when there is none.</summary>
    private TimeSpan? PromoteOwnDueTimers()
    {
        lock (_timersLock)
        {
            if (_timers.Count == 0)
                return null;

            // The due timers are queued while the timers are locked: another thread (e.g. a host that advances a fake clock
            // while a worker promotes its timers) sees either the timer or its task
            var now = TimeProvider.GetTimestamp();
            while (_timers.TryPeek(out var callback, out var priority) && priority.Due <= now)
            {
                _timers.Dequeue();
                QueueTask(callback);
            }

            return _timers.TryPeek(out _, out var next) ? TimeSpan.FromSeconds((double)(next.Due - now) / TimeProvider.TimestampFrequency) : null;
        }
    }

    /// <summary>Returns the time until the next timer of this event loop (zero when it is due), without promoting it.</summary>
    private TimeSpan? GetTimeUntilNextOwnTimer()
    {
        lock (_timersLock)
        {
            if (_timers.Count == 0)
                return null;

            _timers.TryPeek(out _, out var first);
            var remaining = TimeSpan.FromSeconds((double)(first.Due - TimeProvider.GetTimestamp()) / TimeProvider.TimestampFrequency);
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    private static TimeSpan? Min(TimeSpan? a, TimeSpan? b) => a is null ? b : b is null ? a : a.Value < b.Value ? a : b;

    /// <summary>
    /// Waits until the next timer is due, using <see cref="TimeProvider"/> (e.g. until a fake clock is advanced), and moves it
    /// to the task queue. Returns false when there is no timer and no task. For instance, to run the tasks and the timers
    /// until there is nothing left:
    /// <code>
    /// do { await eventLoop.RunUntilIdleAsync(); } while (await eventLoop.WaitForNextTimerAsync());
    /// </code>
    /// </summary>
    public async Task<bool> WaitForNextTimerAsync(CancellationToken cancellationToken = default)
    {
        var delay = PromoteDueTimers();
        if (HasPendingWork)
            return true;

        if (delay is null)
            return false;

        await Task.Delay(delay.Value < TimeSpan.Zero ? TimeSpan.Zero : delay.Value, TimeProvider, cancellationToken).ConfigureAwait(false);
        PromoteDueTimers();
        return true;
    }

    /// <summary>The timer nesting level of the currently running task (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timer-nesting-level).</summary>
    internal int CurrentTimerNestingLevel { get; set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#termination-nesting-level: the number of documents whose
    /// beforeunload, pagehide, visibilitychange or unload events are being fired. window.open() opens nothing meanwhile.
    /// </summary>
    internal int TerminationNestingLevel { get; set; }

    // ----------------------------------------------------------------------------------------------------------------
    // Animation frames: the "update the rendering" steps that do not need a layout
    // (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering) run at a fixed rate
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The duration of a frame (60 frames per second).</summary>
    internal static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds(1d / 60);

    private readonly List<Window> _windowsWithAnimationFrameCallbacks = [];
    private bool _renderingScheduled;
    private long? _frameEpoch;

    /// <summary>The timestamp of the last rendering opportunity, or null before the first one.</summary>
    internal long? LastFrameTimestamp { get; private set; }

    private readonly List<Document> _documentsWithRenderingSteps = [];

    private readonly List<Document> _documentsWithCssResources = [];

    private readonly List<Document> _documentsWithAnimations = [];

    private readonly List<Document> _documentsWithAnimationStyles = [];

    private readonly List<WeakReference<Document>> _documentsWithMediaQueryLists = [];

    /// <summary>Registers a document that has media query lists, whose changes are reported when the rendering is updated.</summary>
    internal void AddDocumentWithMediaQueryLists(Document document) => _documentsWithMediaQueryLists.Add(new WeakReference<Document>(document));

    private readonly List<WeakReference<Document>> _documentsWithImagesReactingToEnvironment = [];

    /// <summary>
    /// Registers a document whose img elements use srcset or picture: its environment is checked when the rendering is updated
    /// (see <see cref="Document.MayHaveImagesReactingToEnvironment"/>).
    /// </summary>
    internal void AddDocumentWithImagesReactingToEnvironment(Document document) => _documentsWithImagesReactingToEnvironment.Add(new WeakReference<Document>(document));

    private readonly List<WeakReference<Document>> _documentsWithLayout = [];

    /// <summary>
    /// Registers a document whose layout was requested (see <see cref="Css.Layout.LayoutEngine.EnsureLayout"/>): its layout is
    /// updated by the rendering steps. The documents that never read the geometry are never laid out.
    /// </summary>
    internal void AddDocumentWithLayout(Document document) => _documentsWithLayout.Add(new WeakReference<Document>(document));

    private readonly List<WeakReference<Window>> _windowsWithResizeListeners = [];

    /// <summary>Registers a window that has resize event listeners, whose viewport changes are reported when the rendering is updated.</summary>
    internal void AddWindowWithResizeListeners(Window window)
    {
        _windowsWithResizeListeners.Add(new WeakReference<Window>(window));
        if (window.Document.BrowsingContext is { Parent: not null })
        {
            HasNestedWindowsWithResizeListeners = true;
        }
    }

    /// <summary>
    /// Whether the window of a nested browsing context registered resize event listeners (see <see cref="AddWindowWithResizeListeners"/>):
    /// the changes of the documents that contain nested browsing contexts then schedule a rendering update, which fires the resize
    /// events when the size of a viewport changed (e.g. its iframe element is no longer in a <c>display: none</c> subtree).
    /// </summary>
    internal bool HasNestedWindowsWithResizeListeners { get; private set; }

    private readonly List<WeakReference<Css.Om.ContainerQueryList>> _containerQueryLists = [];

    /// <summary>Registers a container query list (<see cref="Element.MatchContainer"/>), whose changes are reported when the rendering is updated.</summary>
    internal void AddContainerQueryList(Css.Om.ContainerQueryList list) => _containerQueryLists.Add(new WeakReference<Css.Om.ContainerQueryList>(list));

    /// <summary>Schedules a rendering opportunity to run the animation frame callbacks of a window.</summary>
    internal void RequestAnimationFrame(Window window)
    {
        if (!_windowsWithAnimationFrameCallbacks.Contains(window))
        {
            _windowsWithAnimationFrameCallbacks.Add(window);
        }

        ScheduleRendering();
    }

    /// <summary>
    /// Schedules a rendering opportunity to run the steps of "update the rendering" that apply to a document: the flush of
    /// its autofocus candidates and the focus fixup rule.
    /// </summary>
    internal void RequestRenderingOpportunity(Document document)
    {
        if (!_documentsWithRenderingSteps.Contains(document))
        {
            _documentsWithRenderingSteps.Add(document);
        }

        ScheduleRendering();
    }

    /// <summary>
    /// Schedules a rendering opportunity to update the animations of a document and send their events
    /// (https://drafts.csswg.org/web-animations-1/#update-animations-and-send-events).
    /// </summary>
    internal void RequestAnimationsUpdate(Document document)
    {
        if (!_documentsWithAnimations.Contains(document))
        {
            _documentsWithAnimations.Add(document);
        }

        ScheduleRendering();
    }

    private readonly List<Document> _documentsWithFullscreenSteps = [];

    /// <summary>
    /// Requests a rendering opportunity that runs the fullscreen steps of the document
    /// (https://fullscreen.spec.whatwg.org/#run-the-fullscreen-steps): the pending fullscreen events are fired.
    /// </summary>
    /// <remarks>
    /// The rendering opportunity is a task queued now, not the next frame of the frame rate: the user agent renders the
    /// document when its window enters or exits fullscreen. The scripts that wait for the events with tasks (e.g. messages
    /// between frames) then see them, as in a browser, where frames elapse while the tasks run.
    /// </remarks>
    internal void RequestFullscreenSteps(Document document)
    {
        if (!_documentsWithFullscreenSteps.Contains(document))
        {
            _documentsWithFullscreenSteps.Add(document);
        }

        if (!_fullscreenRenderingQueued)
        {
            _fullscreenRenderingQueued = true;
            QueueTask(() =>
            {
                _fullscreenRenderingQueued = false;
                if (_documentsWithFullscreenSteps.Count > 0)
                {
                    UpdateTheRendering();
                }
            });
        }
    }

    private bool _fullscreenRenderingQueued;

    private readonly List<Document> _documentsWithScrollSteps = [];

    /// <summary>
    /// Schedules a rendering opportunity to run the scroll steps of a document (https://drafts.csswg.org/cssom-view/#document-run-the-scroll-steps):
    /// its pending scroll events and its smooth scrolls.
    /// </summary>
    internal void RequestScrollSteps(Document document)
    {
        if (!_documentsWithScrollSteps.Contains(document))
        {
            _documentsWithScrollSteps.Add(document);
        }

        ScheduleRendering();
    }

    private readonly List<Document> _documentsToReveal = [];

    /// <summary>
    /// Schedules a rendering opportunity that reveals a document that became the active document of its navigable
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering: "if doc's has been revealed is false, then
    /// reveal doc"): the <c>pagereveal</c> event.
    /// </summary>
    internal void RequestReveal(Document document)
    {
        if (document.HasBeenRevealed || _documentsToReveal.Contains(document))
            return;

        _documentsToReveal.Add(document);
        ScheduleRendering();
    }

    /// <summary>Schedules a rendering opportunity when a document that waits to be revealed is no longer render-blocked (its parser stopped).</summary>
    internal void OnDocumentParsed(Document document)
    {
        if (_documentsToReveal.Contains(document))
        {
            ScheduleRendering();
        }
    }

    private readonly List<Document> _documentsWithViewTransitions = [];

    /// <summary>
    /// Schedules a rendering opportunity to perform the pending operations of the active view transition of a document
    /// (https://drafts.csswg.org/css-view-transitions-1/#perform-pending-transition-operations).
    /// </summary>
    internal void RequestViewTransitionUpdate(Document document)
    {
        if (!_documentsWithViewTransitions.Contains(document))
        {
            _documentsWithViewTransitions.Add(document);
        }

        ScheduleRendering();
    }

    /// <summary>
    /// Schedules a rendering opportunity for a document whose rendering is no longer suppressed for a view transition: its
    /// animation frame callbacks and its animations, which waited, run again.
    /// </summary>
    internal void ResumeRendering(Document document)
    {
        if (document.BrowsingContext is not null)
        {
            ScheduleRendering();
        }
    }

    /// <summary>Schedules a rendering opportunity to update the CSS animations and transitions of a document (see <see cref="Document.FlushAnimationStyles"/>).</summary>
    internal void RequestAnimationStylesUpdate(Document document)
    {
        _documentsWithAnimationStyles.Add(document);
        ScheduleRendering();
    }

    /// <summary>Schedules a rendering opportunity to fetch the resources used by the rendering of a document (see <see cref="Document.UpdateCssResources"/>).</summary>
    internal void RequestCssResourcesUpdate(Document document)
    {
        _documentsWithCssResources.Add(document);
        ScheduleRendering();
    }

    /// <summary>
    /// The number of resize observers that observe an element in the documents of the event loop: a change of the layout that
    /// does not change the styles (see <see cref="Css.Layout.LayoutEngine.SetNeedsLayout"/>) then schedules a rendering update,
    /// which gathers their observations.
    /// </summary>
    internal int ResizeObserverCount { get; set; }

    /// <summary>
    /// The number of intersection observers that observe an element in the documents of the event loop: a change of the layout
    /// that does not change the styles then schedules a rendering update, which computes their observations.
    /// </summary>
    internal int IntersectionObserverCount { get; set; }

    private readonly List<WeakReference<Document>> _documentsWithIntersectionObservers = [];

    /// <summary>Registers a document that has intersection observers, whose observations are computed when the rendering is updated.</summary>
    internal void AddDocumentWithIntersectionObservers(Document document)
    {
        foreach (var reference in _documentsWithIntersectionObservers)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, document))
                return;
        }

        _documentsWithIntersectionObservers.Add(new WeakReference<Document>(document));
    }

    private readonly List<WeakReference<Document>> _documentsWithScrollDrivenTimelines = [];

    /// <summary>Registers a document that has scroll-driven timelines: the rendering steps update them after the layout (https://drafts.csswg.org/scroll-animations-1/#event-loop).</summary>
    internal void AddDocumentWithScrollDrivenTimelines(Document document)
    {
        foreach (var reference in _documentsWithScrollDrivenTimelines)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, document))
                return;
        }

        _documentsWithScrollDrivenTimelines.Add(new WeakReference<Document>(document));
    }

    private readonly List<WeakReference<Document>> _documentsWithLayoutShiftTracking = [];

    /// <summary>Registers a document whose layout shifts are observed (see <see cref="Document.StartLayoutShiftTracking"/>), reported when the rendering is updated.</summary>
    internal void AddDocumentWithLayoutShiftTracking(Document document) => _documentsWithLayoutShiftTracking.Add(new WeakReference<Document>(document));

    private readonly List<WeakReference<Document>> _documentsWithPaintTiming = [];

    /// <summary>Registers a document whose paint timing is tracked (see <see cref="Document.StartPaintTiming"/>), marked when the rendering is updated.</summary>
    internal void AddDocumentWithPaintTiming(Document document) => _documentsWithPaintTiming.Add(new WeakReference<Document>(document));

    private readonly List<Window> _windowsWithEventTimingEntries = [];

    /// <summary>
    /// Schedules a rendering opportunity that sets the duration of the event timing entries of a window and queues them
    /// (https://w3c.github.io/event-timing/#dispatch-pending-event-timing-entries).
    /// </summary>
    internal void RequestEventTimingDispatch(Window window)
    {
        if (!_windowsWithEventTimingEntries.Contains(window))
        {
            _windowsWithEventTimingEntries.Add(window);
        }

        ScheduleRendering();
    }

    /// <summary>Schedules a rendering opportunity, whose steps update the layout of the documents whose layout was requested (e.g. to deliver the resize observations).</summary>
    internal void RequestRenderingUpdate() => ScheduleRendering();

    private void ScheduleRendering()
    {
        if (_renderingScheduled)
            return;

        // The frames are aligned on a fixed rate from the first frame
        _renderingScheduled = true;
        var now = TimeProvider.GetTimestamp();
        _frameEpoch ??= now;
        var frameTicks = (long)(FrameDuration.TotalSeconds * TimeProvider.TimestampFrequency);
        var elapsed = now - _frameEpoch.Value;
        var next = ((elapsed / frameTicks) + 1) * frameTicks;
        QueueTimer(TimeSpan.FromSeconds((double)(next - elapsed) / TimeProvider.TimestampFrequency), UpdateTheRendering);
    }

    /// <summary>The number of rendering updates: the documents remember whether a rendering update skips them (see <see cref="Document.IsSkippedByRenderingUpdate"/>).</summary>
    private long _renderingUpdateCount;

    /// <summary>
    /// "Filter non-renderable documents" of https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering, for the
    /// documents that wait for a step of the rendering update: returns the documents that are not render-blocked. The
    /// render-blocked ones stay in <paramref name="pending"/>: the step runs in the rendering update that is requested when they
    /// are no longer render-blocked (see <see cref="Document.RequestSkippedRenderingUpdate"/>).
    /// </summary>
    private static Document[] TakeRenderableDocuments(List<Document> pending, long renderingUpdate)
    {
        if (pending.Count == 0)
            return [];

        var documents = pending.ToArray();
        pending.Clear();
        foreach (var document in documents)
        {
            if (document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                pending.Add(document);
            }
        }

        return pending.Count == 0 ? documents : Array.FindAll(documents, document => !document.IsSkippedByRenderingUpdate(renderingUpdate));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering. There is no list of the documents of the
    /// event loop: each step runs for the documents that registered for it. The render-blocked documents
    /// (https://html.spec.whatwg.org/multipage/dom.html#render-blocked) are skipped by every step.
    /// </summary>
    private void UpdateTheRendering()
    {
        _renderingScheduled = false;
        var renderingUpdate = ++_renderingUpdateCount;

        // The time of the frame, shared by the animations and the animation frame callbacks
        var frameTimestamp = TimeProvider.GetTimestamp();
        LastFrameTimestamp = frameTimestamp;
        // https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering: "Reveal": the documents that were not
        // revealed yet fire pagereveal. A render-blocked document (e.g. a document that is still parsed without body element, or
        // that waits for a render-blocking script) is revealed by a later rendering update
        if (_documentsToReveal.Count > 0)
        {
            foreach (var document in _documentsToReveal.ToArray())
            {
                if (!document.IsFullyActive)
                {
                    _documentsToReveal.Remove(document);
                }
                else if (document.Prerendering)
                {
                    // A prerendering traversable has no rendering opportunity: its documents are revealed once activated
                }
                else if (!document.IsSkippedByRenderingUpdate(renderingUpdate))
                {
                    _documentsToReveal.Remove(document);
                    document.Reveal();
                }
            }
        }

        var documents = TakeRenderableDocuments(_documentsWithRenderingSteps, renderingUpdate);
        foreach (var document in documents)
        {
            document.FocusFixupRequested = false;
            if (document.IsFullyActive && document.BrowsingContext is { Parent: null })
            {
                FocusModel.FlushAutofocusCandidates(document);
            }
        }

        // https://drafts.csswg.org/cssom-view/#run-the-resize-steps
        _windowsWithResizeListeners.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _windowsWithResizeListeners.ToArray())
        {
            if (reference.TryGetTarget(out var window) && !window.Document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                window.RunResizeSteps();
            }
        }

        // https://drafts.csswg.org/cssom-view/#document-run-the-scroll-steps (the smooth scrolls are animated there)
        var scrolledDocuments = TakeRenderableDocuments(_documentsWithScrollSteps, renderingUpdate);
        foreach (var document in scrolledDocuments)
        {
            if (document.IsFullyActive && document.LayoutEngineOrNull is { } layoutEngine && layoutEngine.RunScrollSteps(frameTimestamp, TimeProvider.TimestampFrequency))
            {
                RequestScrollSteps(document);
            }
        }

        // https://drafts.csswg.org/css-anchor-position-1/#scroll: the anchor-positioned boxes that their default scroll shift makes
        // overflow determine their position fallback again (the snapshot of the scroll offsets is taken after the scroll, before the
        // animation frame callbacks, as in the browsers)
        foreach (var reference in _documentsWithLayout.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                // The pickers of the select elements whose appearance changed are closed
                document.CheckOpenSelectPickers();
                document.LayoutEngineOrNull?.UpdateAnchorPositionedBoxesForRendering();
            }
        }

        // https://drafts.csswg.org/cssom-view/#evaluate-media-queries-and-report-changes
        _documentsWithMediaQueryLists.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _documentsWithMediaQueryLists.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                document.EvaluateMediaQueriesAndReportChanges();
            }
        }

        // The container query lists (https://github.com/w3c/csswg-drafts/pull/13551) are evaluated like the media query lists
        Css.Om.ContainerQueryList.EvaluateAndReportChanges(_containerQueryLists);

        // https://html.spec.whatwg.org/multipage/images.html#img-environment-changes: "The user agent may at any time run the
        // following algorithm to update an img element's image in order to react to changes in the environment": the environment
        // of the documents with such images is checked at each rendering opportunity, although nothing reads their styles (e.g.
        // the viewport of a nested document whose container was resized by its document)
        _documentsWithImagesReactingToEnvironment.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _documentsWithImagesReactingToEnvironment.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                document.GetStyleEngine().CheckEnvironment();
                document.CheckAutoSizesImages();
            }
        }

        // https://drafts.csswg.org/web-animations-1/#update-animations-and-send-events
        // https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering: the documents whose "rendering is suppressed
        // for view transitions" are not rendered: their animations and their animation frame callbacks wait
        var animatedDocuments = TakeRenderableDocuments(_documentsWithAnimations, renderingUpdate);
        var suppressedDocuments = 0;
        foreach (var document in animatedDocuments)
        {
            if (document.ViewTransitionRenderingSuppressed)
            {
                _documentsWithAnimations.Add(document);
                suppressedDocuments++;
            }
            else if (document.IsFullyActive && document.RelevantWindow is { } window)
            {
                document.Animations.UpdateAnimationsAndSendEvents(window.ToRelativeHighResolutionTime(frameTimestamp));
            }
        }

        if (suppressedDocuments > 0)
        {
            animatedDocuments = Array.FindAll(animatedDocuments, static document => !document.ViewTransitionRenderingSuppressed);
        }

        // https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering: "For each doc of docs, run the fullscreen
        // steps for doc", after the animations and before the animation frame callbacks. The documents are in tree order: the
        // document of a container before the documents of its navigable.
        if (_documentsWithFullscreenSteps.Count > 0)
        {
            var fullscreenDocuments = TakeRenderableDocuments(_documentsWithFullscreenSteps, renderingUpdate);
            foreach (var document in fullscreenDocuments.OrderBy(GetNestingDepth))
            {
                if (document.IsFullyActive)
                {
                    document.RunFullscreenSteps();
                }
            }

            static int GetNestingDepth(Document document)
            {
                var depth = 0;
                for (var navigable = document.BrowsingContext?.Parent; navigable is not null; navigable = navigable.Parent)
                {
                    depth++;
                }

                return depth;
            }
        }

        var windows = _windowsWithAnimationFrameCallbacks.ToArray();
        _windowsWithAnimationFrameCallbacks.Clear();
        // The documents of a prerendering traversable have no rendering opportunity either: their callbacks run once the page
        // is activated (https://wicg.github.io/nav-speculation/prerendering.html). The callbacks of a render-blocked document
        // run once it is no longer render-blocked.
        bool WaitsForRendering(Window window) => window.Document.ViewTransitionRenderingSuppressed || window.Document.Prerendering || window.Document.IsSkippedByRenderingUpdate(renderingUpdate);
        var someWindowsWait = false;
        foreach (var window in windows)
        {
            someWindowsWait |= WaitsForRendering(window);
        }

        if (someWindowsWait)
        {
            _windowsWithAnimationFrameCallbacks.AddRange(Array.FindAll(windows, WaitsForRendering));
            windows = Array.FindAll(windows, window => !WaitsForRendering(window));
        }

        // "Reveal": a document that is rendered (it runs its animation frame callbacks) is revealed before its callbacks run, also
        // when no pagereveal listener requested it, so that a listener added later only observes the next reveal (e.g. the
        // restoration of the document from the back/forward cache)
        foreach (var window in windows)
        {
            if (window.Document is { HasBeenRevealed: false, IsFullyActive: true } revealedDocument)
            {
                _documentsToReveal.Remove(revealedDocument);
                revealedDocument.Reveal();
            }
        }

        // The timelines of the other documents are updated too, so that the animation frame callbacks see the time of the frame
        foreach (var window in windows)
        {
            var document = window.Document;
            if (document.IsFullyActive && document.AnimationsOrNull is { } animations && !animatedDocuments.Contains(document))
            {
                animations.UpdateAnimationsAndSendEvents(window.ToRelativeHighResolutionTime(frameTimestamp));
            }
        }

        foreach (var window in windows)
        {
            window.RunAnimationFrameCallbacks(frameTimestamp);
        }

        // The styles are updated after the animation frame callbacks: the CSS animations and transitions are created, updated
        // and canceled, then the resources used by the styles are fetched
        var cssDocuments = TakeRenderableDocuments(_documentsWithCssResources, renderingUpdate);
        var styleDocuments = TakeRenderableDocuments(_documentsWithAnimationStyles, renderingUpdate);
        foreach (var document in animatedDocuments.Concat(styleDocuments).Distinct())
        {
            document.FlushAnimationStyles();
        }

        foreach (var document in cssDocuments)
        {
            document.UpdateCssResources();
        }

        // The documents whose styles may use content-visibility are laid out even when nothing read their geometry: the rendering
        // steps determine the relevance to the user of the elements with content-visibility: auto (and fire their events). So are
        // the documents whose styles may use container scroll-state queries: the rendering steps snapshot the scroll states, and the
        // documents whose styles may use position options: the rendering steps record the last successful position options, and
        // the documents whose styles may generate scroll navigation controls: the rendering steps determine the active scroll markers
        foreach (var document in cssDocuments.Concat(styleDocuments))
        {
            if (document.IsFullyActive && document.LayoutEngineOrNull is not { IsLayoutRequested: true }
                && (document.GetStyleEngine().MayHaveContentVisibility() || document.GetStyleEngine().MayHaveScrollStateQueries() || document.GetStyleEngine().MayHavePositionTryFallbacks()
                    || document.GetStyleEngine().MayHaveScrollNavigationControls()))
            {
                document.GetLayoutEngine().EnsureLayout();
            }
        }

        // Recalculate styles and update layout (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering), for the
        // documents whose layout was requested, with the steps that depend on it: content-visibility: auto, the last remembered
        // sizes, and the gathering and the broadcast of the resize observations, depth after depth (the documents with resize
        // observers are registered, so they are laid out by every rendering update)
        _documentsWithLayout.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _documentsWithLayout.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && document.LayoutEngineOrNull is { } layoutEngine && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                layoutEngine.UpdateRendering();
            }
        }

        // https://drafts.csswg.org/css-position-4/#process-top-layer-removals: "intended to be called during the Update the
        // Rendering step", after the styles are recalculated (the overlay of the elements pending removal is known)
        foreach (var document in documents)
        {
            if (document.IsFullyActive)
            {
                document.ProcessTopLayerRemovals();
            }
        }

        // https://drafts.csswg.org/scroll-animations-1/#event-loop: the scroll-driven timelines created or changed by the style and
        // layout update (stale timelines) are updated after it, then the styles and the layout are updated once more
        _documentsWithScrollDrivenTimelines.RemoveAll(reference => !reference.TryGetTarget(out var document) || document.AnimationsOrNull is not { HasScrollDrivenTimelines: true });
        foreach (var reference in _documentsWithScrollDrivenTimelines.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate) && document.Animations.UpdateScrollDrivenTimelines(staleOnly: true))
            {
                document.FlushAnimationStyles();
                document.LayoutEngineOrNull?.UpdateLayout();
            }
        }

        foreach (var document in documents)
        {
            if (document.IsFullyActive)
            {
                FocusModel.RunFocusFixupRule(document);
            }
        }

        // https://html.spec.whatwg.org/multipage/speculative-loading.html#consider-speculative-loads: the style recalculation
        // may change the links that the document rules of the speculation rules select
        foreach (var reference in _documentsWithLayout.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.SpeculationRuleSets is not null)
            {
                document.ConsiderSpeculativeLoadsForRendering();
            }
        }

        // The element under a pointer of the user that hovers may have changed with the layout or the scroll positions: its
        // boundary events are fired, as the browsers do after a layout
        foreach (var reference in _documentsWithLayout.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && document.BrowsingContext is { Parent: null, InputOrNull: { } input })
            {
                input.UpdateHoverAfterLayout();
            }
        }

        // https://drafts.csswg.org/css-view-transitions-1/#perform-pending-transition-operations: the old state of a pending view
        // transition is captured, and the frame of an animating one is handled
        if (_documentsWithViewTransitions.Count > 0)
        {
            var transitionDocuments = TakeRenderableDocuments(_documentsWithViewTransitions, renderingUpdate);
            foreach (var document in transitionDocuments)
            {
                if (document.IsFullyActive)
                {
                    document.PerformPendingTransitionOperations();
                }
            }
        }

        // https://w3c.github.io/IntersectionObserver/#run-the-update-intersection-observations-steps, after the layout and the
        // resize observers. The documents without observers are forgotten.
        _documentsWithIntersectionObservers.RemoveAll(reference => !reference.TryGetTarget(out var document) || !document.HasIntersectionObservers);
        foreach (var reference in _documentsWithIntersectionObservers.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                document.RunUpdateIntersectionObservationsSteps(frameTimestamp);
            }
        }

        // https://w3c.github.io/paint-timing/#mark-paint-timing: the documents whose paint timing is tracked are painted (into their
        // display list) and their paint timing is marked, at the end of the rendering update
        _documentsWithPaintTiming.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _documentsWithPaintTiming.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                document.MarkPaintTiming();
            }
        }

        // https://w3c.github.io/event-timing/#sec-modifications-HTML: "dispatch pending Event Timing entries" for each fully
        // active document, right after the paint timing
        if (_windowsWithEventTimingEntries.Count > 0)
        {
            var eventTimingWindows = _windowsWithEventTimingEntries.ToArray();
            _windowsWithEventTimingEntries.Clear();
            foreach (var window in eventTimingWindows)
            {
                if (window.Document.IsSkippedByRenderingUpdate(renderingUpdate))
                {
                    _windowsWithEventTimingEntries.Add(window);
                }
                else if (window.Document.IsFullyActive)
                {
                    window.EventTiming.DispatchPendingEntries();
                }
            }
        }

        // https://wicg.github.io/layout-instability/#sec-modifications-HTML: "report the layout shift" for each fully active
        // document, after the paint timing (the end of the rendering update)
        _documentsWithLayoutShiftTracking.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _documentsWithLayoutShiftTracking.ToArray())
        {
            if (reference.TryGetTarget(out var document) && document.IsFullyActive && !document.IsSkippedByRenderingUpdate(renderingUpdate))
            {
                document.ReportTheLayoutShift();
            }
        }
    }

    /// <summary>
    /// Associates a script engine with the event loop: the microtasks are queued in its job queue. The event loop does not
    /// keep the engine alive: it is associated as long as the browsing contexts that use it are alive.
    /// </summary>
    internal void AttachScriptEngine(Scripting.ScriptEngine engine)
    {
        _scriptEngines.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _scriptEngines)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, engine))
                return;
        }

        _scriptEngines.Add(new WeakReference<Scripting.ScriptEngine>(engine));
    }

    /// <summary>Returns the first script engine associated with the event loop, or null.</summary>
    private Scripting.ScriptEngine? GetScriptEngine()
    {
        foreach (var reference in _scriptEngines)
        {
            if (reference.TryGetTarget(out var engine))
                return engine;
        }

        return null;
    }

    /// <summary>Gets whether a script is running: the JavaScript execution context stack of a script engine of the event loop is not empty.</summary>
    internal bool IsScriptRunning
    {
        get
        {
            foreach (var reference in _scriptEngines)
            {
                if (reference.TryGetTarget(out var engine) && engine.IsScriptRunning)
                    return true;
            }

            return false;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#enqueue-an-element-on-the-appropriate-element-queue (when the custom element reactions stack is empty)</summary>
    internal void EnqueueOnBackupElementQueue(Element element)
    {
        if (IsDisposed)
            return;

        _backupElementQueue.Enqueue(element);
        if (_processingBackupElementQueue)
            return;

        _processingBackupElementQueue = true;
        QueueMicrotask(() =>
        {
            try
            {
                Html.CustomElementReactions.InvokeReactions(_backupElementQueue);
            }
            finally
            {
                _processingBackupElementQueue = false;
            }
        });
    }

    /// <summary>Registers a global object whose about-to-be-notified rejected promises list is not empty.</summary>
    internal void AddGlobalWithRejectedPromises(GlobalScope global) => (_globalsWithRejectedPromises ??= []).Add(global);

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-microtask</summary>
    public void QueueMicrotask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
#pragma warning disable CS0618 // The obsolete override is still supported
        if (MicrotaskQueueOverride is { } queue)
        {
            queue(callback);
            return;
        }
#pragma warning restore CS0618

        if (IsDisposed)
            return;

        if (_scriptEngines.Count > 0 && GetScriptEngine() is { } engine)
        {
            engine.QueueMicrotask(callback);
            return;
        }

        _microtasks.Enqueue(callback);
    }

    // The IndexedDB transactions whose cleanup event loop is this event loop
    private List<IdbTransaction>? _indexedDBTransactions;

    /// <summary>Sets the cleanup event loop of an IndexedDB transaction to this event loop (https://w3c.github.io/IndexedDB/#transaction-cleanup-event-loop).</summary>
    internal void AddIndexedDBTransaction(IdbTransaction transaction) => (_indexedDBTransactions ??= []).Add(transaction);

    /// <summary>Queues a task (https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-task).</summary>
    public void QueueTask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        QueueTask(callback, schedulerTaskSlot: null);
    }

    /// <summary>Queues a task, or the slot of a task of a scheduler (see <see cref="SchedulerTaskSlot"/>).</summary>
    private void QueueTask(Action callback, long? schedulerTaskSlot)
    {
        TaskCompletionSource? signal;
        lock (_tasksLock)
        {
            if (_isDisposed)
                return;

            _tasks.Enqueue(callback);
            if (schedulerTaskSlot is { } enqueueOrder)
            {
                _schedulerTaskSlots.Enqueue(enqueueOrder);
            }

            MarkBusyLocked();
            signal = _taskQueuedSignal;
            _taskQueuedSignal = null;
        }

        signal?.TrySetResult();
        _workerSignal?.Set();
        WorkSignal?.Invoke();
    }

    /// <summary>Gets whether there are pending tasks or microtasks.</summary>
    public bool HasPendingWork
    {
        get
        {
            lock (_tasksLock)
            {
                return _tasks.Count > 0 || _microtasks.Count > 0;
            }
        }
    }

    /// <summary>
    /// Gets whether asynchronous operations are in progress (e.g. documents loaded by <see cref="BrowsingContextOptions.DocumentLoader"/>).
    /// They queue a task when they complete.
    /// </summary>
    public bool HasPendingOperations => Volatile.Read(ref _pendingOperations) > 0;

    /// <summary>Registers an asynchronous operation, which must queue a task before calling <see cref="EndOperation"/>.</summary>
    internal void BeginOperation()
    {
        Interlocked.Increment(ref _pendingOperations);

        // A worker that waits for an operation keeps its owner busy
        if (Owner is not null)
        {
            lock (_tasksLock)
            {
                MarkBusyLocked();
            }
        }
    }

    internal void EndOperation()
    {
        Interlocked.Decrement(ref _pendingOperations);
        TaskCompletionSource? signal;
        lock (_tasksLock)
        {
            signal = _taskQueuedSignal;
            _taskQueuedSignal = null;
        }

        signal?.TrySetResult();
        _workerSignal?.Set();
        WorkSignal?.Invoke();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#perform-a-microtask-checkpoint</summary>
    public void PerformMicrotaskCheckpoint()
    {
        if (_performingMicrotaskCheckpoint)
            return;

        // The currently running task is a microtask: it has no timer nesting level
        _performingMicrotaskCheckpoint = true;
        var timerNestingLevel = CurrentTimerNestingLevel;
        CurrentTimerNestingLevel = 0;
        try
        {
            do
            {
                for (var i = 0; i < _scriptEngines.Count; i++)
                {
                    if (_scriptEngines[i].TryGetTarget(out var engine))
                    {
                        engine.PerformMicrotaskCheckpoint();
                    }
                }

                while (_microtasks.TryDequeue(out var microtask))
                {
                    microtask();
                }
            }
            while (_microtasks.Count > 0);

            // https://html.spec.whatwg.org/multipage/webappapis.html#notify-about-rejected-promises
            if (_globalsWithRejectedPromises is { Count: > 0 } globals)
            {
                _globalsWithRejectedPromises = null;
                foreach (var global in globals)
                {
                    global.NotifyAboutRejectedPromises();
                }
            }

            // https://w3c.github.io/IndexedDB/#cleanup-indexed-database-transactions
            if (_indexedDBTransactions is { Count: > 0 } transactions)
            {
                _indexedDBTransactions = null;
                foreach (var transaction in transactions)
                {
                    transaction.Cleanup();
                }
            }
        }
        finally
        {
            _performingMicrotaskCheckpoint = false;
            CurrentTimerNestingLevel = timerNestingLevel;
        }
    }

    /// <summary>
    /// Runs one task, followed by a microtask checkpoint. Returns false when there is no task. The tasks run in the order
    /// they were queued, except the tasks of the schedulers (<see cref="Scheduler.PostTask"/>, <see cref="Scheduler.Yield"/>),
    /// which run in the order of their priorities: see <see cref="SelectNextSchedulerTaskQueue"/>. When no task is queued,
    /// an idle period runs the idle callbacks of the windows (<see cref="Window.RequestIdleCallback"/>).
    /// </summary>
    public bool RunTask()
    {
        // The user-blocking tasks and the user-visible continuations of the schedulers precede the other tasks
        if (_schedulerTaskQueues.Count > 0 && !IsDisposed && SelectNextSchedulerTaskQueue(ElevatedEffectivePriority) is { } elevatedQueue)
        {
            RunSchedulerTask(elevatedQueue);
            return true;
        }

        while (true)
        {
            Action? task;
            var hasOtherTasks = false;
            var slotEnqueueOrder = 0L;
            lock (_tasksLock)
            {
                _tasks.TryDequeue(out task);
                if (task is not null && ReferenceEquals(task, SchedulerTaskSlot))
                {
                    slotEnqueueOrder = _schedulerTaskSlots.Dequeue();
                    hasOtherTasks = _tasks.Count > _schedulerTaskSlots.Count;
                }
            }

            // The idle tasks run when no other task is queued
            if (task is null)
                return RunIdleTask();

            if (ReferenceEquals(task, SchedulerTaskSlot))
            {
                // The task of the slot may have run already (with a higher priority), have been aborted, or belong to a
                // document that is no longer fully active
                if (SelectNextSchedulerTaskQueue(minimumEffectivePriority: 0) is not { First: { } first } queue)
                    continue;

                // The background tasks and continuations run when no other task is queued, and a user-visible task that
                // was queued after the task of the slot waits for its own slot: this slot goes after the other tasks
                if (hasOtherTasks && (queue.EffectivePriority < UserVisibleEffectivePriority || first.EnqueueOrder > slotEnqueueOrder))
                {
                    QueueTask(SchedulerTaskSlot, slotEnqueueOrder);
                    continue;
                }

                RunSchedulerTask(queue);
                return true;
            }

            // A task that is not a task of a scheduler has no scheduling state (it is set while a task of a scheduler spins the event loop)
            var continuationState = CurrentContinuationState;
            CurrentContinuationState = null;
            EndIdlePeriod();
            try
            {
                task();
                PerformMicrotaskCheckpoint();
            }
            finally
            {
                CurrentContinuationState = continuationState;
            }

            return true;
        }
    }

    /// <summary>Runs the microtasks and the tasks until both queues are empty.</summary>
    public void RunUntilIdle()
    {
        PerformMicrotaskCheckpoint();
        PromoteDueTimers();
        while (RunTask())
        {
            PromoteDueTimers();
        }
    }

    /// <summary>
    /// Runs the tasks until <paramref name="condition"/> is true, or until the queues are empty and no asynchronous
    /// operation is in progress.
    /// </summary>
    internal async Task RunUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            PerformMicrotaskCheckpoint();
            PromoteDueTimers();
            while (!condition() && RunTask())
            {
                PromoteDueTimers();
            }

            if (condition())
                return;

            Task signal;
            lock (_tasksLock)
            {
                if (_tasks.Count > 0)
                    continue;

                if (_isDisposed || Volatile.Read(ref _pendingOperations) == 0)
                    return;

                _taskQueuedSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                signal = _taskQueuedSignal.Task;
            }

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the microtasks and the tasks until both queues are empty and no asynchronous operation is in progress (e.g. the
    /// documents loaded by <see cref="BrowsingContextOptions.DocumentLoader"/>).
    /// </summary>
    public async Task RunUntilIdleAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            RunUntilIdle();

            Task signal;
            lock (_tasksLock)
            {
                if (_tasks.Count > 0)
                    continue;

                if (_isDisposed || Volatile.Read(ref _pendingOperations) == 0)
                    return;

                _taskQueuedSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                signal = _taskQueuedSignal.Task;
            }

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#create-an-element-for-the-token (step 9.2): the parser performs a
    /// microtask checkpoint before running a custom element constructor, when the JavaScript execution context stack is empty.
    /// </summary>
    internal void PerformMicrotaskCheckpointBeforeCustomElementConstruction()
    {
        if (!IsScriptRunning)
        {
            PerformMicrotaskCheckpoint();
        }
    }

    /// <summary>
    /// Discards the pending tasks, microtasks, timers and animation frames, and ignores the ones queued afterwards (called
    /// when the browsing context group is disposed). The asynchronous operations in progress complete without effect.
    /// </summary>
    internal void Dispose()
    {
        TaskCompletionSource? signal;
        OnStoppingRunning();
        lock (_tasksLock)
        {
            if (_isDisposed)
                return;

            Volatile.Write(ref _isDisposed, true);
            _tasks.Clear();
            _schedulerTaskSlots.Clear();
            signal = _taskQueuedSignal;
            _taskQueuedSignal = null;
        }

        _disposed.TrySetResult();

        _microtasks.Clear();
        lock (_timersLock)
        {
            _timers.Clear();
        }

        DisposeChildLoops();
        ReleaseOwner();
        _windowsWithAnimationFrameCallbacks.Clear();
        ClearIdleCallbacks();
        _windowsWithEventTimingEntries.Clear();
        _documentsWithCssResources.Clear();
        _documentsWithAnimations.Clear();
        _documentsWithAnimationStyles.Clear();
        _renderingScheduled = false;
        _globalsWithRejectedPromises = null;
        _scriptEngines.Clear();
        PendingMutationObservers.Clear();
        SignalSlots.Clear();
        _backupElementQueue.Clear();
        _processingBackupElementQueue = false;
        MutationObserverMicrotaskQueued = false;

        // Wake up RunUntilIdleAsync, which returns as the loop is disposed
        signal?.TrySetResult();
        _workerSignal?.Set();
    }

    /// <summary>Called by the DOM after an operation that may have queued microtasks.</summary>
    internal void OnOperationCompleted()
    {
        if (AutoPerformMicrotaskCheckpoint)
        {
            PerformMicrotaskCheckpoint();
        }
    }
}
