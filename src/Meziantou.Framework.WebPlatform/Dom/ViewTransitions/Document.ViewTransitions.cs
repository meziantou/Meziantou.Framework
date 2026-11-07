using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Layout;
using PhysicalSize = Meziantou.Framework.WebPlatform.Css.Layout.PhysicalSize;

namespace Meziantou.Framework.WebPlatform.Dom;

// CSS View Transitions (https://drafts.csswg.org/css-view-transitions-1/, https://drafts.csswg.org/css-view-transitions-2/):
// the active view transition of the document, its update callback queue, the rendering suppression and the steps of the
// rendering update ("perform pending transition operations").
public partial class Document
{
    private ViewTransition? _activeViewTransition;
    private Dictionary<Element, ViewTransition>? _scopedViewTransitions;
    private Dictionary<(Element Element, string? PseudoElement), int>? _capturedInViewTransition;
    private long _viewTransitionSequence;
    private List<ViewTransition>? _viewTransitionUpdateCallbackQueue;
    private bool _viewTransitionUpdateRequested;

    /// <summary>Whether the view transitions are enabled (see <see cref="WebPlatformFeatures.ViewTransitionsEnabled"/>).</summary>
    internal bool ViewTransitionsEnabled => BrowsingContext is not { Options.EffectiveFeatures.ViewTransitionsEnabled: false };

    /// <summary>
    /// Gets the active view transition of the document, or null (https://drafts.csswg.org/css-view-transitions-2/#dom-document-activeviewtransition).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-document-activeviewtransition"/>
    [IdlMember("activeViewTransition", "https://drafts.csswg.org/css-view-transitions-2/#dom-document-activeviewtransition")]
    public ViewTransition? ActiveViewTransition => _activeViewTransition;

    /// <summary>
    /// Starts a view transition (https://drafts.csswg.org/css-view-transitions-1/#dom-document-startviewtransition): the old state of
    /// the document is captured at the next rendering opportunity, then the callback updates the document, and the transition
    /// between the two states is animated. The active view transition of the document, if any, is skipped.
    /// </summary>
    /// <param name="callback">The callback that updates the document. It is always called, even when the transition cannot happen.</param>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-1/#dom-document-startviewtransition"/>
    [IdlMember("startViewTransition", "https://drafts.csswg.org/css-view-transitions-1/#dom-document-startviewtransition")]
    public ViewTransition StartViewTransition(ViewTransitionUpdateCallback callback) => StartViewTransitionCore(DocumentElement, callback, types: null);

    /// <summary>
    /// Starts a view transition with options (https://drafts.csswg.org/css-view-transitions-2/#dom-document-startviewtransition):
    /// the update callback and the types of the transition.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-document-startviewtransition"/>
    [IdlMember("startViewTransition", "https://drafts.csswg.org/css-view-transitions-2/#dom-document-startviewtransition")]
    public ViewTransition StartViewTransition(StartViewTransitionOptions? options = null) => StartViewTransitionCore(DocumentElement, options?.Update, options?.Types);

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition: the method steps of
    /// <c>Element.startViewTransition()</c>, which <c>Document.startViewTransition()</c> performs for the document element. A
    /// transition whose root element is another element is a scoped view transition.
    /// </summary>
    internal ViewTransition StartViewTransitionCore(Element? root, ViewTransitionUpdateCallback? callback, IReadOnlyList<string>? types)
    {
        var scoped = root is not null && !ReferenceEquals(root, DocumentElement);
        var transition = new ViewTransition(this, root, callback) { IsScoped = scoped };
        if (types is not null)
        {
            foreach (var type in types)
            {
                transition.Types.Add(type);
            }
        }

        // A document that is not fully active (e.g. the document of a removed iframe) is not rendered and runs no task: as in the
        // browsers, the transition is skipped at once and the update callback is not called
        if (!IsFullyActive || BrowsingContext is null)
        {
            transition.SkipWithoutUpdateCallback(new DomException("The document is not fully active.", "AbortError"));
            return transition;
        }

        // "If document's visibility state is "hidden", then skip transition with an "InvalidStateError" DOMException"
        if (!IsVisible)
        {
            transition.Skip(new DomException("The document is not visible.", "InvalidStateError"));
            return transition;
        }

        // "This ensures that a same-document transition that started after firing pageswap is skipped"
        if (_activeViewTransition is { IsOutbound: true })
        {
            transition.Skip(new DomException("The document is being navigated away with a view transition.", "InvalidStateError"));
            return transition;
        }

        if (scoped)
        {
            // "If this's principal box is not rendered, or is an internal table box other than table-cell, or is an internal ruby
            // box or a non-atomic inline-level box, then skip viewTransition with an "InvalidStateError" DOMException": the element
            // "has a principal box and qualifies for layout containment"
            if (!ViewTransitionCapture.CanHostScopedViewTransition(this, root!))
            {
                transition.Skip(new DomException("The element is not rendered or cannot have layout containment.", "InvalidStateError"));
                return transition;
            }

            // "If this's active view transition is not null, then skip that view transition with an "AbortError" DOMException"
            GetActiveViewTransition(root!)?.Skip(new DomException("The view transition was replaced by another view transition.", "AbortError"));

            // "Force this to have both layout containment and view transition scoping for the duration of the transition": the
            // styles are computed again (see LayoutStyle.Contain and the :active-view-transition rules of the user agent style sheet)
            (_scopedViewTransitions ??= new(ReferenceEqualityComparer.Instance))[root!] = transition;
            IncrementElementStateVersion();
            InvalidateLayout(rebuildBoxTree: true);
            RequestViewTransitionUpdate();
            return transition;
        }

        _activeViewTransition?.Skip(new DomException("The view transition was replaced by another view transition.", "AbortError"));
        _activeViewTransition = transition;

        // The :active-view-transition pseudo-classes match from now on; the old state is captured by the next rendering update
        IncrementElementStateVersion();
        RequestViewTransitionUpdate();
        return transition;
    }

    /// <summary>
    /// Returns the active view transition of an element (https://drafts.csswg.org/css-view-transitions-2/#element-active-view-transition):
    /// the one of the document for the document element, else the scoped view transition whose root element is the element.
    /// </summary>
    internal ViewTransition? GetActiveViewTransition(Element element)
    {
        if (_scopedViewTransitions is { Count: > 0 } scoped && scoped.TryGetValue(element, out var transition))
            return transition;

        return _activeViewTransition is not null && ReferenceEquals(element, DocumentElement) ? _activeViewTransition : null;
    }

    /// <summary>
    /// Returns the scoped view transition whose root element is the element, or null: the element then has layout containment
    /// and view transition scoping (https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition).
    /// </summary>
    internal ViewTransition? GetScopedViewTransition(Element element) => _scopedViewTransitions is { Count: > 0 } scoped ? scoped.GetValueOrDefault(element) : null;

    /// <summary>Returns the position of a new view transition in the order of the creations.</summary>
    internal long NextViewTransitionSequence() => ++_viewTransitionSequence;

    /// <summary>Whether the rendering of the root element of a scoped view transition is suppressed (see <see cref="ViewTransition.FrozenFrame"/>).</summary>
    internal bool HasFrozenViewTransitionScopes
    {
        get
        {
            if (_scopedViewTransitions is { Count: > 0 } scoped)
            {
                foreach (var transition in scoped.Values)
                {
                    if (transition.FrozenFrame is not null)
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>Whether elements of the document host a scoped view transition.</summary>
    internal bool HasScopedViewTransitions => _scopedViewTransitions is { Count: > 0 };

    /// <summary>
    /// Returns the active view transitions of the document: the one of the document and the scoped ones, in the order of their
    /// creation (the order the update callbacks are called in).
    /// </summary>
    internal ViewTransition[] GetActiveViewTransitions()
    {
        if (_scopedViewTransitions is not { Count: > 0 } scoped)
            return _activeViewTransition is { } single ? [single] : [];

        var result = new List<ViewTransition>(scoped.Count + 1);
        if (_activeViewTransition is { } transition)
        {
            result.Add(transition);
        }

        result.AddRange(scoped.Values);
        result.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        return [.. result];
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Cross-document view transitions (https://drafts.csswg.org/css-view-transitions-2/#cross-document-view-transitions)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#view-transition-params: the captured elements of the old document of a navigation and the size of its snapshot containing block.</summary>
    internal sealed record ViewTransitionParams(Css.Layout.ViewTransitionNamedElements NamedElements, PhysicalSize? InitialSnapshotContainingBlockSize);

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#document-inbound-view-transition-params</summary>
    internal ViewTransitionParams? InboundViewTransitionParams { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#has-been-revealed: whether the <c>pagereveal</c> event was fired at the window of the document.</summary>
    internal bool HasBeenRevealed { get; private set; }

    /// <summary>The document is reactivated (https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document): the <c>pagereveal</c> event is fired again.</summary>
    internal void ResetHasBeenRevealed() => HasBeenRevealed = false;

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-2/#resolve-view-transition-rule: returns the types of the last
    /// <c>@view-transition</c> rule of the document when it opts in (<c>navigation: auto</c>) and the document is visible, else
    /// null ("skip transition").
    /// </summary>
    internal IReadOnlyList<string>? ResolveViewTransitionRule()
    {
        if (!IsVisible || !ViewTransitionsEnabled)
            return null;

        return GetStyleEngine().GetViewTransitionRule() is { Navigation: "auto" } rule ? rule.Types : null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-2/#setup-cross-document-view-transition (in the old document): creates the
    /// outbound view transition of a navigation when the document opts in; its old state is captured after the <c>pageswap</c>
    /// event (<see cref="CaptureOutboundViewTransition"/>).
    /// </summary>
    internal ViewTransition? SetupOutboundViewTransition()
    {
        // "If oldDocument's has been revealed is false, then return null": a document that was never rendered has no old state
        // to capture. The documents are revealed lazily (when the pagereveal event is observed, or for an inbound view
        // transition), so only the documents that are still render-blocked are known not to have been revealed.
        if (!HasBeenRevealed && IsRenderBlocked)
            return null;

        if (ResolveViewTransitionRule() is not { } types || DocumentElement is null)
            return null;

        var transition = new ViewTransition(this, DocumentElement, updateCallback: null) { IsOutbound = true };
        foreach (var type in types)
        {
            transition.Types.Add(type);
        }

        _activeViewTransition?.Skip(new DomException("The view transition was replaced by the view transition of a navigation.", "AbortError"));
        _activeViewTransition = transition;
        IncrementElementStateVersion();
        return transition;
    }

    /// <summary>
    /// Captures the old state of the outbound view transition of a navigation and returns the parameters given to the new
    /// document, or null when the transition was skipped or cannot be captured. The specification captures it at the next
    /// rendering opportunity of the old document, before the navigation proceeds; the old document is captured at once, as its
    /// rendering does not change in between.
    /// </summary>
    internal ViewTransitionParams? CaptureOutboundViewTransition(ViewTransition transition)
    {
        if (transition.Phase != ViewTransitionPhase.PendingCapture || !ReferenceEquals(_activeViewTransition, transition))
            return null;

        if (!ViewTransitionCapture.CaptureOldState(transition))
        {
            transition.Skip(new DomException("The old state of the document could not be captured.", "InvalidStateError"));
            return null;
        }

        return new ViewTransitionParams(transition.NamedElements, transition.InitialSnapshotContainingBlockSize);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#reveal: fires the <c>pagereveal</c> event at the window of the document, with
    /// the inbound view transition of the navigation that created it, if any, which is then activated.
    /// </summary>
    internal void Reveal()
    {
        if (HasBeenRevealed || RelevantWindow is not { } window)
            return;

        HasBeenRevealed = true;
        var transition = ResolveInboundViewTransition();
        using (IsFullyActive ? window.ScriptEngine?.PrepareToRunScript(window) : null)
        {
            window.FireEvent(new PageRevealEvent("pagereveal", new PageRevealEventInit { ViewTransition = transition }));
            transition?.ActivateInbound();
        }
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#document-resolve-cross-document-view-transition</summary>
    private ViewTransition? ResolveInboundViewTransition()
    {
        var inboundParams = InboundViewTransitionParams;
        InboundViewTransitionParams = null;
        if (inboundParams is null || _activeViewTransition is not null || DocumentElement is null)
            return null;

        if (ResolveViewTransitionRule() is not { } types)
            return null;

        var transition = new ViewTransition(this, DocumentElement, updateCallback: null, inboundParams.NamedElements) { InitialSnapshotContainingBlockSize = inboundParams.InitialSnapshotContainingBlockSize };
        foreach (var type in types)
        {
            transition.Types.Add(type);
        }

        _activeViewTransition = transition;
        transition.SetInbound();
        IncrementElementStateVersion();
        return transition;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-1/#document-rendering-suppression-for-view-transitions: while it is set, the
    /// document is not rendered (its animation frame callbacks do not run, its last painted frame is displayed) and the hit
    /// testing targets its document element.
    /// </summary>
    internal bool ViewTransitionRenderingSuppressed { get; private set; }

    internal void SetViewTransitionRenderingSuppression(bool value)
    {
        if (ViewTransitionRenderingSuppressed == value)
            return;

        // The frame displayed while the rendering is suppressed is the one of the old state: it is painted now when the
        // document is rasterized
        if (value && RenderingBackend is not null)
        {
            _ = GetDisplayList();
        }

        ViewTransitionRenderingSuppressed = value;
        if (!value)
        {
            // The rendering steps that were skipped run at the next rendering opportunity
            InvalidatePaint();
            EventLoop?.ResumeRendering(this);
        }
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#document-update-callback-queue</summary>
    internal List<ViewTransition> ViewTransitionUpdateCallbackQueue => _viewTransitionUpdateCallbackQueue ??= [];

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#flush-the-update-callback-queue</summary>
    internal void FlushViewTransitionUpdateCallbackQueue()
    {
        if (_viewTransitionUpdateCallbackQueue is not { Count: > 0 } queue)
            return;

        var transitions = queue.ToArray();
        queue.Clear();
        foreach (var transition in transitions)
        {
            transition.CallUpdateCallback();
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-1/#captured-in-a-view-transition: the elements (and the pseudo-elements, e.g.
    /// <c>::backdrop</c>) captured by the active view transition. While it is animating, they are not painted nor hit (their
    /// images are painted by the <c>::view-transition-new()</c> pseudo-elements).
    /// </summary>
    internal bool IsCapturedInViewTransition(Element element, string? pseudoElement = null) => _capturedInViewTransition is { Count: > 0 } captured && captured.ContainsKey((element, pseudoElement));

    /// <summary>Whether elements are captured by the active view transition.</summary>
    internal bool HasElementsCapturedInViewTransition => _capturedInViewTransition is { Count: > 0 };

    internal void SetCapturedInViewTransition(Element element, string? pseudoElement, bool value)
    {
        // The captures of several transitions are counted: an element marked while a transition captures its images can be
        // captured by another transition that is animating
        var captured = _capturedInViewTransition ??= [];
        var count = captured.GetValueOrDefault((element, pseudoElement));
        if (value)
        {
            captured[(element, pseudoElement)] = count + 1;
        }
        else if (count > 1)
        {
            captured[(element, pseudoElement)] = count - 1;
        }
        else
        {
            captured.Remove((element, pseudoElement));
        }
    }

    /// <summary>The pseudo-element tree of the active view transition of the document (it is animating), or null.</summary>
    internal ViewTransitionPseudoTree? ViewTransitionPseudoTree => _activeViewTransition?.PseudoTree;

    /// <summary>
    /// Returns the pseudo-element tree of the active view transition whose root element is the element (the tree of
    /// <c>::view-transition</c> pseudo-elements that originate from it), or null.
    /// </summary>
    internal ViewTransitionPseudoTree? GetViewTransitionPseudoTree(Element root)
    {
        if (_scopedViewTransitions is { Count: > 0 } scoped && scoped.TryGetValue(root, out var transition))
            return transition.PseudoTree;

        return _activeViewTransition?.PseudoTree is { } tree && ReferenceEquals(tree.Root, root) ? tree : null;
    }

    /// <summary>Whether an active view transition of the document has a pseudo-element tree.</summary>
    internal bool HasViewTransitionPseudoTrees
    {
        get
        {
            if (_activeViewTransition?.PseudoTree is not null)
                return true;

            if (_scopedViewTransitions is { Count: > 0 } scoped)
            {
                foreach (var transition in scoped.Values)
                {
                    if (transition.PseudoTree is not null)
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>Whether the elements captured by an active view transition are painted by its pseudo-elements (its phase is "animating").</summary>
    internal bool IsViewTransitionAnimating
    {
        get
        {
            if (_activeViewTransition is { Phase: ViewTransitionPhase.Animating })
                return true;

            if (_scopedViewTransitions is { Count: > 0 } scoped)
            {
                foreach (var transition in scoped.Values)
                {
                    if (transition.Phase == ViewTransitionPhase.Animating)
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Called when the pseudo-element tree or the dynamic style sheet of the active view transition changed: the styles are
    /// computed again and the box tree is built again.
    /// </summary>
    internal void OnViewTransitionChanged()
    {
        IncrementElementStateVersion();
        InvalidateLayout(rebuildBoxTree: true);
    }

    /// <summary>Schedules a rendering opportunity that performs the pending operations of the active view transition.</summary>
    internal void RequestViewTransitionUpdate()
    {
        if (_viewTransitionUpdateRequested || EventLoop is not { } eventLoop)
            return;

        _viewTransitionUpdateRequested = true;
        eventLoop.RequestViewTransitionUpdate(this);
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#perform-pending-transition-operations</summary>
    internal void PerformPendingTransitionOperations()
    {
        _viewTransitionUpdateRequested = false;
        if (_activeViewTransition is null && _scopedViewTransitions is not { Count: > 0 })
            return;

        // "Let transitions be a new list [...] For each transition of transitions": the specification lists the transition of the
        // document, then the ones of the elements in tree order. As in Chromium, they are processed in the order of their
        // creation, so that the update callbacks are called in this order ("the callback execution order is only specified for
        // callbacks that result from view transitions on the same root element, or on elements related by DOM ancestors")
        foreach (var transition in GetActiveViewTransitions())
        {
            switch (transition.Phase)
            {
                case ViewTransitionPhase.PendingCapture:
                    transition.Setup();
                    break;

                case ViewTransitionPhase.Animating:
                    transition.HandleTransitionFrame();
                    break;
            }
        }
    }

    /// <summary>
    /// The document enters the back/forward cache: its view transitions are skipped at once (the task of the page visibility
    /// change steps would only run once the document is restored, when it is visible again).
    /// </summary>
    internal void SkipViewTransitionsForBackForwardCache()
    {
        if (_activeViewTransition is null && _scopedViewTransitions is not { Count: > 0 } && InboundViewTransitionParams is null)
            return;

        foreach (var transition in GetActiveViewTransitions())
        {
            var reason = new DomException("The document is hidden.", "InvalidStateError");
            if (transition.IsOutbound)
            {
                transition.SkipOutboundForBackForwardCache(reason);
                if (ReferenceEquals(_activeViewTransition, transition))
                {
                    _activeViewTransition = null;
                }
            }
            else
            {
                transition.Skip(reason);
            }
        }

        InboundViewTransitionParams = null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-2/#page-visibility-change-steps: a document that becomes hidden skips its
    /// active view transition, and forgets its inbound view transition params (a document hidden before it is revealed, e.g.
    /// while it is render-blocked, has no inbound view transition).
    /// </summary>
    private void RunViewTransitionPageVisibilityChangeSteps()
    {
        if (_activeViewTransition is null && _scopedViewTransitions is not { Count: > 0 } && InboundViewTransitionParams is null)
            return;

        QueueGlobalTask(() =>
        {
            if (!IsVisible)
            {
                foreach (var transition in GetActiveViewTransitions())
                {
                    transition.Skip(new DomException("The document is hidden.", "InvalidStateError"));
                }

                InboundViewTransitionParams = null;
            }
        });
    }

    /// <summary>
    /// Computes the styles of the pseudo-elements of the active view transition: their CSS animations are created, updated
    /// and canceled.
    /// </summary>
    internal void ComputeViewTransitionPseudoElementStyles()
    {
        if (_activeViewTransition is null && _scopedViewTransitions is not { Count: > 0 })
            return;

        var engine = GetStyleEngine();
        foreach (var transition in GetActiveViewTransitions())
        {
            if (transition.PseudoTree is not { } tree)
                continue;

            foreach (var pseudoElement in tree.EnumeratePseudoElements())
            {
                engine.GetStyle(tree.Root, pseudoElement);
            }
        }
    }

    /// <summary>
    /// Returns whether an animation of a pseudo-element of the view transition is running or paused, or has pending events
    /// (https://drafts.csswg.org/css-view-transitions-1/#handle-transition-frame).
    /// </summary>
    internal bool HasActiveViewTransitionAnimations(ViewTransition transition)
    {
        if (transition.PseudoTree is not { } tree)
            return false;

        // The CSS animations and transitions of the pseudo-elements are up to date
        FlushAnimationStyles();
        ComputeViewTransitionPseudoElementStyles();
        if (_animations is not { } animations || animations.GetElementAnimations(tree.Root) is not { } elementAnimations)
            return false;

        foreach (var pseudoElement in tree.EnumeratePseudoElements())
        {
            if (elementAnimations.Get(pseudoElement) is not { } effects)
                continue;

            foreach (var effect in effects)
            {
                if (effect.Animation is not { } animation || animation.Timeline is not DocumentTimeline)
                    continue;

                if (animation.PlayStateValue is AnimationPlayState.Running or AnimationPlayState.Paused || animations.HasPendingEvents(animation))
                    return true;
            }
        }

        return false;
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#clear-view-transition (the part of the document).</summary>
    internal void ClearViewTransition(ViewTransition transition, ViewTransitionPseudoTree? tree)
    {
        if (ReferenceEquals(_activeViewTransition, transition))
        {
            _activeViewTransition = null;
        }

        if (transition.RootElement is { } root && _scopedViewTransitions is { } scoped && scoped.TryGetValue(root, out var active) && ReferenceEquals(active, transition))
        {
            scoped.Remove(root);
        }

        if (_activeViewTransition is null && _scopedViewTransitions is not { Count: > 0 })
        {
            _capturedInViewTransition?.Clear();
        }

        transition.DynamicStyleSheet = null;

        // The pseudo-elements are removed: their CSS animations and transitions are canceled
        if (tree is not null)
        {
            _animations?.CancelDeclarativeAnimations(tree.Root, static pseudoElement => pseudoElement.StartsWith("view-transition", StringComparison.Ordinal));
        }

        OnViewTransitionChanged();
        EventLoop?.RequestRenderingUpdate();
    }

    /// <summary>
    /// Replaces the dynamic view transition style sheet of a transition (https://drafts.csswg.org/css-view-transitions-1/#document-dynamic-view-transition-style-sheet):
    /// the rules of the user agent origin generated for its pseudo-elements (the sizes and the transforms of the groups, their
    /// animations). The styles are computed again when it changed.
    /// </summary>
    internal void SetViewTransitionStyleSheet(ViewTransition transition, string text)
    {
        if (transition.DynamicStyleSheet == text)
            return;

        transition.DynamicStyleSheet = text;
        OnViewTransitionChanged();
    }
}
