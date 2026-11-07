using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The callback of a view transition, which updates the document (https://drafts.csswg.org/css-view-transitions-1/#callbackdef-viewtransitionupdatecallback).
/// </summary>
/// <returns>
/// Null, a <see cref="Task"/> or a promise of the script engine: the new state of the document is captured once it is fulfilled,
/// and the transition is skipped when it is rejected.
/// </returns>
public delegate object? ViewTransitionUpdateCallback();

/// <summary>https://drafts.csswg.org/css-view-transitions-2/#dictdef-startviewtransitionoptions</summary>
/// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dictdef-startviewtransitionoptions"/>
[IdlInterface("StartViewTransitionOptions", "https://drafts.csswg.org/css-view-transitions-2/#dictdef-startviewtransitionoptions")]
public sealed class StartViewTransitionOptions
{
    /// <summary>Gets the callback that updates the document, or null.</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-startviewtransitionoptions-update"/>
    [IdlMember("update", "https://drafts.csswg.org/css-view-transitions-2/#dom-startviewtransitionoptions-update")]
    public ViewTransitionUpdateCallback? Update { get; init; }

    /// <summary>Gets the initial types of the transition (matched by <c>:active-view-transition-type()</c>), or null.</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-startviewtransitionoptions-types"/>
    [IdlMember("types", "https://drafts.csswg.org/css-view-transitions-2/#dom-startviewtransitionoptions-types")]
    public IReadOnlyList<string>? Types { get; init; }
}

/// <summary>The phases of a view transition (https://drafts.csswg.org/css-view-transitions-1/#viewtransition-phase), in order.</summary>
internal enum ViewTransitionPhase : byte
{
    PendingCapture,
    UpdateCallbackCalled,
    Animating,
    Done,
}

/// <summary>
/// A view transition (https://drafts.csswg.org/css-view-transitions-1/#viewtransition): the animated transition between two
/// states of a document. The old state is captured at the rendering opportunity that follows
/// <see cref="Document.StartViewTransition(ViewTransitionUpdateCallback?)"/>, the update callback then changes the document,
/// and the tree of <c>::view-transition</c> pseudo-elements, which shows the snapshots of the old state and the live
/// images of the new state, is animated until its animations finish.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-view-transitions-1/#viewtransition"/>
[IdlInterface("ViewTransition", "https://drafts.csswg.org/css-view-transitions-1/#viewtransition")]
public sealed class ViewTransition
{
    /// <summary>The duration after which a transition whose update callback did not settle is skipped (the timeout of the browsers).</summary>
    internal static readonly TimeSpan UpdateCallbackTimeout = TimeSpan.FromSeconds(4);

    private readonly TaskCompletionSource _updateCallbackDone = CreatePromise();
    private readonly TaskCompletionSource _ready = CreatePromise();
    private readonly TaskCompletionSource _finished = CreatePromise();
    private readonly ViewTransitionUpdateCallback? _updateCallback;
    private int _waitUntilCount;
    private bool _finishing;

    internal ViewTransition(Document document, Element? rootElement, ViewTransitionUpdateCallback? updateCallback, ViewTransitionNamedElements? namedElements = null)
    {
        Document = document;
        RootElement = rootElement;
        Sequence = document.NextViewTransitionSequence();
        _updateCallback = updateCallback;
        NamedElements = namedElements ?? new ViewTransitionNamedElements();
        Types = new ViewTransitionTypeSet(this);

        // "finished promise: [...] marked as handled": it only ever rejects along with the update callback done promise
        PromiseSlots.MarkAsHandled(_finished.Task);
    }

    internal Document Document { get; }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-2/#viewtransition-root-element: the document element for the transitions of a
    /// document (the global view transitions), the element <c>startViewTransition()</c> was called on for the scoped ones.
    /// </summary>
    internal Element? RootElement { get; }

    /// <summary>
    /// Whether the transition is a scoped view transition (https://drafts.csswg.org/css-view-transitions-2/#scoped-view-transitions):
    /// its root element is not the document element. It only captures the flat tree inclusive descendants of its root element,
    /// its pseudo-element tree is laid out in the padding box of its root element and painted above its contents, and the
    /// rendering is only suppressed within it.
    /// </summary>
    internal bool IsScoped { get; init; }

    /// <summary>The position of the transition in the order of the creations of the transitions of the document.</summary>
    internal long Sequence { get; }

    /// <summary>
    /// The text of the dynamic view transition style sheet of the transition (https://drafts.csswg.org/css-view-transitions-1/#document-dynamic-view-transition-style-sheet):
    /// the rules of the user agent origin generated for its pseudo-elements, or null.
    /// </summary>
    internal string? DynamicStyleSheet { get; set; }

    /// <summary>
    /// The frame of the root element of a scoped view transition displayed while its rendering is suppressed ("rendering is
    /// paused within the view transition root element"): the painting of its old state, in the coordinates of its border box.
    /// Null when the rendering is not suppressed.
    /// </summary>
    internal ViewTransitionFrozenFrame? FrozenFrame { get; private set; }

    internal ViewTransitionPhase Phase { get; private set; }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#viewtransition-named-elements: the captured elements by view transition name, in paint order.</summary>
    internal ViewTransitionNamedElements NamedElements { get; }

    /// <summary>
    /// Whether the transition is the outbound transition of a navigation, in the old document
    /// (https://drafts.csswg.org/css-view-transitions-2/#viewtransition-outbound-post-capture-steps): its update callback is the
    /// navigation, and a transition started by <c>startViewTransition()</c> afterwards is skipped.
    /// </summary>
    internal bool IsOutbound { get; init; }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#viewtransition-initial-snapshot-containing-block-size</summary>
    internal PhysicalSize? InitialSnapshotContainingBlockSize { get; set; }

    /// <summary>
    /// The tree of pseudo-elements of the transition (https://drafts.csswg.org/css-view-transitions-1/#view-transition-pseudo-elements),
    /// built when the transition is activated; null before, and once the transition is cleared.
    /// </summary>
    internal ViewTransitionPseudoTree? PseudoTree { get; private set; }

    /// <summary>
    /// Gets a task completed when the promise returned by the update callback is fulfilled; it fails when the promise is
    /// rejected (https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-updatecallbackdone).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-updatecallbackdone"/>
    [IdlMember("updateCallbackDone", "https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-updatecallbackdone")]
    public Task UpdateCallbackDone => _updateCallbackDone.Task;

    /// <summary>
    /// Gets a task completed when the pseudo-elements of the transition are created and the animations are about to start; it
    /// fails when the transition cannot begin or is skipped before (https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-ready).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-ready"/>
    [IdlMember("ready", "https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-ready")]
    public Task Ready => _ready.Task;

    /// <summary>
    /// Gets a task completed when the end state is visible: the animations finished or the transition was skipped. It only
    /// fails when the update callback failed (https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-finished).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-finished"/>
    [IdlMember("finished", "https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-finished")]
    public Task Finished => _finished.Task;

    /// <summary>Gets the types of the transition, matched by <c>:active-view-transition-type()</c> (https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-types).</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-types"/>
    [IdlMember("types", "https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-types")]
    public ViewTransitionTypeSet Types { get; }

    /// <summary>Gets the element that hosts the transition: the document element (https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-transitionroot).</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-transitionroot"/>
    [IdlMember("transitionRoot", "https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-transitionroot")]
    public Element? TransitionRoot => RootElement;

    /// <summary>
    /// Sets the rendering suppression of the transition (https://drafts.csswg.org/css-view-transitions-1/#document-rendering-suppression-for-view-transitions):
    /// the one of the document for a global view transition; "rendering is paused within the view transition root element" of
    /// a scoped view transition, which displays the frame of its old state and keeps its size (as in Chromium, it has size
    /// containment with the size of its old state, so that the changes of its contents do not move the rest of the document).
    /// </summary>
    private void SetRenderingSuppression(bool value)
    {
        var document = Document;
        if (!IsScoped)
        {
            document.SetViewTransitionRenderingSuppression(value);
            return;
        }

        if (value == (FrozenFrame is not null))
            return;

        FrozenFrame = value && RootElement is { } root ? ViewTransitionCapture.CaptureFrozenFrame(document, root) : null;
        document.OnViewTransitionChanged();
        document.InvalidatePaint();
        document.EventLoop?.RequestRenderingUpdate();
    }

    /// <summary>
    /// Finishes the transition immediately, or prevents it from starting; the update callback is still called
    /// (https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-skiptransition).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-skiptransition"/>
    [IdlMember("skipTransition", "https://drafts.csswg.org/css-view-transitions-1/#dom-viewtransition-skiptransition")]
    public void SkipTransition()
    {
        if (Phase != ViewTransitionPhase.Done)
        {
            Skip(new DomException("The view transition was skipped.", "AbortError"));
        }
    }

    /// <summary>Delays the end of the transition until the task completes (https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-waituntil).</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-waituntil"/>
    [IdlMember("waitUntil", "https://drafts.csswg.org/css-view-transitions-2/#dom-viewtransition-waituntil")]
    public void WaitUntil(Task<object?> promise)
    {
        ArgumentNullException.ThrowIfNull(promise);
        if (Phase == ViewTransitionPhase.Done || promise.IsCompleted)
            return;

        _waitUntilCount++;
        var eventLoop = Document.EventLoop;
        _ = promise.ContinueWith(
            _ =>
            {
                if (eventLoop is null)
                {
                    Settled();
                }
                else
                {
                    eventLoop.QueueMicrotask(Settled);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        void Settled()
        {
            _waitUntilCount--;
            if (Phase == ViewTransitionPhase.Animating)
            {
                Document.RequestViewTransitionUpdate();
            }
        }
    }

    private static TaskCompletionSource CreatePromise()
    {
        var promise = new TaskCompletionSource(TaskCreationOptions.None);
        PromiseSlots.Register(promise.Task);
        return promise;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // https://drafts.csswg.org/css-view-transitions-1/#setup-view-transition
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#setup-view-transition (the phase is "pending-capture", in the rendering steps).</summary>
    internal void Setup()
    {
        var document = Document;

        // The old state of the outbound transition of a navigation is captured by the navigation
        if (IsOutbound)
            return;

        // "Flush the update callback queue": the changes of the DOM of the skipped transitions are done before the capture
        document.FlushViewTransitionUpdateCallbackQueue();
        if (Phase != ViewTransitionPhase.PendingCapture)
            return;

        // The root element of a scoped view transition was removed, moved to another document, or is no longer rendered
        if (IsScoped && !ViewTransitionCapture.CanHostScopedViewTransition(document, RootElement!))
        {
            Skip(new DomException("The root element of the view transition is not rendered or cannot have layout containment.", "InvalidStateError"));
            return;
        }

        if (!ViewTransitionCapture.CaptureOldState(this))
        {
            Skip(new DomException("The old state of the document could not be captured (two elements have the same view-transition-name).", "InvalidStateError"));
            return;
        }

        SetRenderingSuppression(true);

        // "Queue a global task on the DOM manipulation task source": the capture of the images may be asynchronous
        document.QueueGlobalTask(() =>
        {
            if (Phase == ViewTransitionPhase.Done)
                return;

            document.ViewTransitionUpdateCallbackQueue.Add(this);
            document.FlushViewTransitionUpdateCallbackQueue();
        });
    }

    /// <summary>
    /// The inbound transition of a navigation, in the new document (https://drafts.csswg.org/css-view-transitions-2/#document-resolve-cross-document-view-transition):
    /// "Resolve transition's update callback done promise with undefined. Set transition's phase to update-callback-called."
    /// </summary>
    internal void SetInbound()
    {
        _updateCallbackDone.TrySetResult();
        Phase = ViewTransitionPhase.UpdateCallbackCalled;
    }

    /// <summary>Activates the inbound transition of a navigation once the <c>pagereveal</c> event was dispatched (https://html.spec.whatwg.org/multipage/dom.html#reveal).</summary>
    internal void ActivateInbound() => Activate();

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#call-the-update-callback</summary>
    internal void CallUpdateCallback()
    {
        if (Phase is not (ViewTransitionPhase.Done or ViewTransitionPhase.PendingCapture))
            return;

        if (Phase != ViewTransitionPhase.Done)
        {
            Phase = ViewTransitionPhase.UpdateCallbackCalled;
        }

        var window = Document.RelevantWindow;
        var engine = window?.ScriptEngine;
        object? callbackPromise = null;
        if (_updateCallback is { } callback)
        {
            using var scope = window is not null && Document.IsFullyActive ? engine?.PrepareToRunScript(window) : null;
            try
            {
                callbackPromise = callback();
            }
#pragma warning disable CA1031 // The exception rejects the promise (https://webidl.spec.whatwg.org/#invoke-a-callback-function)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                callbackPromise = new RejectedPromise(ex);
            }
        }

        // "To skip a transition after a timeout": the transition is skipped when the callback does not settle
        if (Phase == ViewTransitionPhase.UpdateCallbackCalled && Document.EventLoop is { } eventLoop)
        {
            eventLoop.QueueTimer(UpdateCallbackTimeout, () =>
            {
                if (Phase == ViewTransitionPhase.UpdateCallbackCalled && Document.IsFullyActive)
                {
                    Skip(new DomException("The update callback of the view transition did not settle in time.", "TimeoutError"));
                }
            });
        }

        React(callbackPromise, OnFulfilled, OnRejected);

        void OnFulfilled()
        {
            _updateCallbackDone.TrySetResult();
            Activate();
        }

        void OnRejected(object? reason)
        {
            _updateCallbackDone.TrySetException(ToException(reason));
            if (Phase == ViewTransitionPhase.Done)
                return;

            // "Mark as handled transition's ready promise": the update callback done promise provides the unhandledrejection
            PromiseSlots.MarkAsHandled(_ready.Task);
            Skip(reason);
        }
    }

    /// <summary>Reacts to the promise returned by the update callback: a value of the script engine, a task, or anything else (a fulfilled promise).</summary>
    private void React(object? value, Action onFulfilled, Action<object?> onRejected)
    {
        var window = Document.RelevantWindow;
        var eventLoop = Document.EventLoop;
        if (value is Task task)
        {
            if (task.IsCompleted || eventLoop is null)
            {
                Queue(() => Complete(task));
            }
            else
            {
                _ = task.ContinueWith(completed => eventLoop.QueueTask(() => Complete(completed)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            return;
        }

        if (window?.ScriptEngine is { } engine && Document.IsFullyActive)
        {
            engine.WaitForAll(window, [value], onFulfilled, onRejected);
            return;
        }

        Queue(value is RejectedPromise rejected ? () => onRejected(rejected.Reason) : onFulfilled);

        void Complete(Task completed)
        {
            if (completed.IsCompletedSuccessfully)
            {
                onFulfilled();
            }
            else
            {
                onRejected(completed.Exception?.InnerException ?? new OperationCanceledException());
            }
        }

        void Queue(Action action)
        {
            if (eventLoop is null)
            {
                action();
            }
            else
            {
                eventLoop.QueueMicrotask(action);
            }
        }
    }

    private static Exception ToException(object? reason) => reason as Exception ?? new AbortSignalException(reason);

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#activate-view-transition</summary>
    private void Activate()
    {
        if (Phase == ViewTransitionPhase.Done)
            return;

        var document = Document;
        SetRenderingSuppression(false);
        if (IsScoped)
        {
            // The root element was removed or moved to another document, is no longer rendered (display: none), or its box can
            // no longer have layout containment (e.g. it became a non-atomic inline box)
            if (!document.IsFullyActive || !ViewTransitionCapture.CanHostScopedViewTransition(document, RootElement!))
            {
                Skip(new DomException("The root element of the view transition is not rendered or cannot have layout containment.", "InvalidStateError"));
                return;
            }
        }
        else if (!document.IsFullyActive || RootElement is not { IsConnected: true } root || !ReferenceEquals(document.DocumentElement, root))
        {
            Skip(new DomException("The document element of the view transition was removed.", "InvalidStateError"));
            return;
        }

        // The snapshot containing block of a scoped view transition is the padding box of its root element, whose size can
        // change with the update (as in Chromium, the transition is not skipped)
        if (!IsScoped && InitialSnapshotContainingBlockSize != ViewTransitionCapture.GetSnapshotContainingBlockSize(document))
        {
            Skip(new DomException("The size of the snapshot containing block changed.", "InvalidStateError"));
            return;
        }

        if (!ViewTransitionCapture.CaptureNewState(this))
        {
            Skip(new DomException("The new state of the document could not be captured (two elements have the same view-transition-name).", "InvalidStateError"));
            return;
        }

        foreach (var captured in NamedElements.Values)
        {
            if (captured.NewElement is { } element)
            {
                document.SetCapturedInViewTransition(element, captured.NewPseudoElement, true);
            }
        }

        // "Setup transition pseudo-elements"
        PseudoTree = ViewTransitionPseudoTree.Create(this);
        document.OnViewTransitionChanged();

        // "Update pseudo-element styles"
        if (!ViewTransitionCapture.UpdatePseudoElementStyles(this))
        {
            Skip(new DomException("The new state of the document could not be captured.", "InvalidStateError"));
            return;
        }

        Phase = ViewTransitionPhase.Animating;
        document.OnViewTransitionChanged();

        // The styles of the pseudo-elements are computed: their animations start
        document.ComputeViewTransitionPseudoElementStyles();
        document.RequestViewTransitionUpdate();
        _ready.TrySetResult();
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#handle-transition-frame (the phase is "animating", in the rendering steps).</summary>
    internal void HandleTransitionFrame()
    {
        var document = Document;
        if (!document.HasActiveViewTransitionAnimations(this) && _waitUntilCount == 0)
        {
            // As in the browsers, the pseudo-elements are removed and the finished promise is resolved in a task that follows the
            // frame where the animations finished (the specification does it in the frame): this frame still shows them, and the
            // tasks queued by its animation frame callbacks still see the transition
            if (!_finishing && document.EventLoop is { } eventLoop)
            {
                _finishing = true;
                eventLoop.QueueTimer(TimeSpan.Zero, () => document.QueueGlobalTask(() =>
                {
                    if (Phase != ViewTransitionPhase.Animating)
                        return;

                    Phase = ViewTransitionPhase.Done;
                    Clear();
                    _finished.TrySetResult();
                }));
            }

            return;
        }

        _finishing = false;

        if (IsScoped)
        {
            if (!ViewTransitionCapture.CanHostScopedViewTransition(document, RootElement!))
            {
                Skip(new DomException("The root element of the view transition is not rendered or cannot have layout containment.", "InvalidStateError"));
                return;
            }
        }
        else if (InitialSnapshotContainingBlockSize != ViewTransitionCapture.GetSnapshotContainingBlockSize(document))
        {
            Skip(new DomException("The size of the snapshot containing block changed.", "InvalidStateError"));
            return;
        }

        if (!ViewTransitionCapture.UpdatePseudoElementStyles(this))
        {
            Skip(new DomException("An element of the view transition is no longer rendered.", "InvalidStateError"));
            return;
        }

        document.RequestViewTransitionUpdate();
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#skip-the-view-transition</summary>
    internal void Skip(object? reason)
    {
        if (Phase == ViewTransitionPhase.Done)
            return;

        var document = Document;
        if (Phase < ViewTransitionPhase.UpdateCallbackCalled)
        {
            // "Schedule the update callback"
            document.ViewTransitionUpdateCallbackQueue.Add(this);
            document.QueueGlobalTask(document.FlushViewTransitionUpdateCallbackQueue);
        }

        SetRenderingSuppression(false);

        // "If transition's root element's active view transition is transition or document's active view transition is transition"
        if (ReferenceEquals(document.ActiveViewTransition, this) || (RootElement is { } root && ReferenceEquals(document.GetScopedViewTransition(root), this)))
        {
            Clear();
        }

        Phase = ViewTransitionPhase.Done;
        _ready.TrySetException(ToException(reason));

        // "Resolve transition's finished promise with the result of reacting to transition's update callback done promise"
        _ = _updateCallbackDone.Task.ContinueWith(
            completed =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    _finished.TrySetResult();
                }
                else
                {
                    _finished.TrySetException(completed.Exception?.InnerException ?? new OperationCanceledException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Skips the outbound transition of a navigation: its document is unloaded, so the rejection of its ready promise is not reported.</summary>
    internal void SkipOutbound(object? reason)
    {
        PromiseSlots.MarkAsHandled(_ready.Task);
        Skip(reason);
    }

    /// <summary>
    /// Skips the outbound transition of a document that enters the back/forward cache: its promises are settled as for a
    /// skipped transition, but its captured elements are kept, as the inbound transition of the new document shows them.
    /// </summary>
    internal void SkipOutboundForBackForwardCache(object? reason)
    {
        PromiseSlots.MarkAsHandled(_ready.Task);
        SkipWithoutUpdateCallback(reason);
    }

    /// <summary>Skips the transition of a document that cannot run its update callback (it is not fully active).</summary>
    internal void SkipWithoutUpdateCallback(object? reason)
    {
        Phase = ViewTransitionPhase.Done;
        _updateCallbackDone.TrySetResult();
        _ready.TrySetException(ToException(reason));
        _finished.TrySetResult();
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#clear-view-transition</summary>
    private void Clear()
    {
        var document = Document;
        foreach (var captured in NamedElements.Values)
        {
            if (captured.NewElement is { } element)
            {
                document.SetCapturedInViewTransition(element, captured.NewPseudoElement, false);
            }

            captured.OldImage?.Dispose();
            captured.OldImage = null;
        }

        var tree = PseudoTree;
        PseudoTree = null;
        document.ClearViewTransition(this, tree);
    }
}
