using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// The animations of a document: its default timeline, the animations that may change with the time, the effects that target
/// its elements, and the pending animation event queue (https://drafts.csswg.org/web-animations-1/#pending-animation-event-queue).
/// </summary>
internal sealed class DocumentAnimations
{
    private readonly Document _document;
    private readonly HashSet<Animation> _trackedAnimations = [];
    private readonly List<PendingAnimationEvent> _pendingEvents = [];
    private readonly Dictionary<Element, ElementAnimations> _targets = [];
    private readonly Dictionary<(Element Element, string? PseudoElement), List<CssAnimation>> _cssAnimations = [];
    private readonly Dictionary<(Element Element, string? PseudoElement), ElementTransitions> _transitions = [];
    private int _invalidationSuppression;
    private double? _timelineTime;
    private DocumentTimeline? _defaultTimeline;
    private bool _frameRequested;
    private List<WeakReference<ScrollTimeline>>? _scrollDrivenTimelines;

    private readonly record struct PendingAnimationEvent(Event Event, EventTarget Target, Animation Animation, double? ScheduledTime, long Sequence);

    private long _eventSequence;

    public DocumentAnimations(Document document)
    {
        _document = document;
    }

    /// <summary>The default document timeline (https://drafts.csswg.org/web-animations-1/#the-documents-default-timeline).</summary>
    public DocumentTimeline DefaultTimeline => _defaultTimeline ??= new DocumentTimeline(_document, originTime: 0);

    /// <summary>
    /// Incremented when the animated values of the elements of the document may have changed (a time update or a change of an
    /// animation): the computed styles are then recomputed.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Whether effects target the elements of the document.</summary>
    public bool HasTargets => _targets.Count > 0;

    /// <summary>
    /// Returns the time of the document timelines, in milliseconds relative to the time origin of the document: the time of the
    /// last frame (https://drafts.csswg.org/web-animations-1/#document-timelines). Before the first frame, the time is sampled
    /// once.
    /// </summary>
    public double GetTimelineTime()
    {
        if (_timelineTime is null)
        {
            // The time of the last rendering opportunity (the current time before the first one of the document)
            var window = _document.RelevantWindow;
            _timelineTime = window is null ? 0
                : window.EventLoop.LastFrameTimestamp is { } frameTimestamp && frameTimestamp >= window.TimeOriginTimestamp ? window.ToRelativeHighResolutionTime(frameTimestamp)
                : window.GetCurrentHighResolutionTime();
            RequestFrame();
        }

        return _timelineTime.Value;
    }

    /// <summary>Called when the playback state, the timeline or the effect of an animation changes.</summary>
    public void OnAnimationChanged(Animation animation)
    {
        _trackedAnimations.Add(animation);
        InvalidateStyles(animation);
        RequestFrame();
    }

    /// <summary>Invalidates all the computed styles that depend on the animations.</summary>
    public void InvalidateStyles()
    {
        if (_invalidationSuppression == 0)
        {
            _stylesInvalidatedAll = true;
            _invalidatedTargets.Clear();
            Version++;
        }
    }

    /// <summary>Invalidates the computed style of the target of the effect of an animation (all the styles when it has no target).</summary>
    private void InvalidateStyles(Animation animation)
    {
        if (animation.EffectValue is KeyframeEffect { TargetElement: { } target } effect)
        {
            InvalidateStyles(target, effect.PseudoElement);
        }
        else
        {
            InvalidateStyles();
        }
    }

    /// <summary>Invalidates the computed style of an element (or of one of its pseudo-elements) that an effect targets.</summary>
    private void InvalidateStyles(Element target, string? pseudoElement)
    {
        if (_invalidationSuppression != 0)
            return;

        if (!_stylesInvalidatedAll)
        {
            _invalidatedTargets.Add((target, pseudoElement));
            if (_invalidatedTargets.Count > MaxInvalidatedTargets)
            {
                _stylesInvalidatedAll = true;
                _invalidatedTargets.Clear();
            }
        }

        Version++;
    }

    /// <summary>The number of invalidated targets from which all the styles are invalidated.</summary>
    private const int MaxInvalidatedTargets = 1000;

    /// <summary>The targets whose styles were invalidated since the last call of <see cref="ConsumeStyleInvalidations"/> (see <see cref="Version"/>).</summary>
    private readonly HashSet<(Element Element, string? PseudoElement)> _invalidatedTargets = [];

    /// <summary>Whether all the styles were invalidated since the last call of <see cref="ConsumeStyleInvalidations"/>.</summary>
    private bool _stylesInvalidatedAll;

    /// <summary>
    /// Returns the targets of the effects whose styles were invalidated since the last call (null when all the styles were
    /// invalidated), for the style engine of the document (see <c>StyleEngine.Invalidation.cs</c>).
    /// </summary>
    public List<(Element Element, string? PseudoElement)>? ConsumeStyleInvalidations()
    {
        List<(Element Element, string? PseudoElement)>? result = _stylesInvalidatedAll ? null : [.. _invalidatedTargets];
        _stylesInvalidatedAll = false;
        _invalidatedTargets.Clear();
        return result;
    }

    /// <summary>
    /// Suppresses the invalidation of the styles while a style is computed: the declarative animations it creates or updates
    /// only affect the element and its descendants, which are computed after it.
    /// </summary>
    public InvalidationSuppressionScope SuppressInvalidation()
    {
        _invalidationSuppression++;
        return new InvalidationSuppressionScope(this);
    }

    public readonly struct InvalidationSuppressionScope(DocumentAnimations animations) : IDisposable
    {
        public void Dispose() => animations._invalidationSuppression--;
    }

    /// <summary>Whether the elements of the document have CSS animations or transitions.</summary>
    public bool HasDeclarativeAnimations => _cssAnimations.Count > 0 || _transitions.Count > 0;

    /// <summary>Returns the running and completed transitions of an element (or of one of its pseudo-elements), or null.</summary>
    public ElementTransitions? GetTransitions(Element element, string? pseudoElement) => _transitions.GetValueOrDefault((element, pseudoElement));

    /// <summary>Returns the running and completed transitions of an element, created when needed.</summary>
    public ElementTransitions GetOrCreateTransitions(Element element, string? pseudoElement)
    {
        if (!_transitions.TryGetValue((element, pseudoElement), out var transitions))
        {
            transitions = new ElementTransitions();
            _transitions.Add((element, pseudoElement), transitions);
        }

        return transitions;
    }

    /// <summary>Removes the transitions of an element when it has no running or completed transition.</summary>
    public void RemoveTransitionsIfEmpty(Element element, string? pseudoElement)
    {
        if (_transitions.TryGetValue((element, pseudoElement), out var transitions) && transitions.IsEmpty)
        {
            _transitions.Remove((element, pseudoElement));
        }
    }

    /// <summary>Removes a running transition canceled through the API.</summary>
    public void RemoveRunningTransition(Element element, string? pseudoElement, CssTransition transition)
    {
        if (_transitions.TryGetValue((element, pseudoElement), out var transitions)
            && transitions.Running.TryGetValue(transition.TransitionProperty, out var running) && ReferenceEquals(running, transition))
        {
            transitions.Running.Remove(transition.TransitionProperty);
            RemoveTransitionsIfEmpty(element, pseudoElement);
        }
    }

    /// <summary>Returns the CSS animations of an element (or of one of its pseudo-elements), in the order of animation-name, or null.</summary>
    public List<CssAnimation>? GetCssAnimations(Element element, string? pseudoElement) => _cssAnimations.GetValueOrDefault((element, pseudoElement));

    /// <summary>Sets the CSS animations of an element (or of one of its pseudo-elements).</summary>
    public void SetCssAnimations(Element element, string? pseudoElement, List<CssAnimation> animations)
    {
        if (animations.Count == 0)
        {
            _cssAnimations.Remove((element, pseudoElement));
        }
        else
        {
            _cssAnimations[(element, pseudoElement)] = animations;
        }
    }

    /// <summary>
    /// Cancels the CSS animations and transitions of the elements that are no longer connected
    /// (https://drafts.csswg.org/css-animations-1/#animations: the animations stop when the element is removed).
    /// </summary>
    public void CancelDisconnectedDeclarativeAnimations()
    {
        foreach (var (key, transitions) in _transitions.ToArray())
        {
            if (key.Element.IsConnected && ReferenceEquals(key.Element.NodeDocument, _document))
                continue;

            _transitions.Remove(key);
            foreach (var transition in transitions.Running.Values)
            {
                transition.CancelByStyle();
            }
        }

        if (_cssAnimations.Count == 0)
            return;

        foreach (var (key, animations) in _cssAnimations.ToArray())
        {
            if (key.Element.IsConnected && ReferenceEquals(key.Element.NodeDocument, _document))
                continue;

            _cssAnimations.Remove(key);
            foreach (var animation in animations)
            {
                animation.CancelByStyle();
            }
        }
    }

    /// <summary>
    /// Cancels the CSS animations and transitions of the pseudo-elements of an element that no longer exist (the pseudo-elements
    /// of a view transition that was cleared).
    /// </summary>
    public void CancelDeclarativeAnimations(Element element, Func<string, bool> pseudoElements)
    {
        foreach (var (key, transitions) in _transitions.ToArray())
        {
            if (!ReferenceEquals(key.Element, element) || key.PseudoElement is null || !pseudoElements(key.PseudoElement))
                continue;

            _transitions.Remove(key);
            foreach (var transition in transitions.Running.Values)
            {
                transition.CancelByStyle();
            }
        }

        foreach (var (key, animations) in _cssAnimations.ToArray())
        {
            if (!ReferenceEquals(key.Element, element) || key.PseudoElement is null || !pseudoElements(key.PseudoElement))
                continue;

            _cssAnimations.Remove(key);
            foreach (var animation in animations)
            {
                animation.CancelByStyle();
            }
        }
    }

    /// <summary>Returns whether the pending animation event queue has an event of the animation.</summary>
    public bool HasPendingEvents(Animation animation)
    {
        foreach (var pending in _pendingEvents)
        {
            if (ReferenceEquals(pending.Animation, animation))
                return true;
        }

        return false;
    }

    /// <summary>Appends an event to the pending animation event queue (https://drafts.csswg.org/web-animations-1/#pending-animation-event-queue).</summary>
    public void QueueEvent(Event animationEvent, EventTarget target, Animation animation, double? scheduledTime)
    {
        _pendingEvents.Add(new PendingAnimationEvent(animationEvent, target, animation, scheduledTime, _eventSequence++));
        RequestFrame();
    }

    /// <summary>Schedules a frame that samples the SMIL animations of the document.</summary>
    public void RequestSmilFrame() => RequestFrame();

    /// <summary>
    /// Invalidates the styles after the animated values of the SMIL animations changed (the presentation attributes and the
    /// properties they animate), whatever style is being computed: the layout and the painting are updated with the styles.
    /// </summary>
    public void InvalidateStylesForSmil()
    {
        _stylesInvalidatedAll = true;
        _invalidatedTargets.Clear();
        Version++;
    }

    private void RequestFrame()
    {
        if (_frameRequested)
            return;

        if (_document.IsFullyActive && _document.BrowsingContext?.EventLoop is { } eventLoop)
        {
            _frameRequested = true;
            eventLoop.RequestAnimationsUpdate(_document);
        }
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#update-animations-and-send-events</summary>
    private static Element? GetOwningElement(Animation animation) => animation switch
    {
        CssAnimation cssAnimation => cssAnimation.OwningElement,
        CssTransition cssTransition => cssTransition.OwningElement,
        _ => null,
    };

    public void UpdateAnimationsAndSendEvents(double now)
    {
        _frameRequested = false;

        // The transitions and animations canceled by the styles are canceled before they are sampled (see StyleEngine.CommitPendingCancellations)
        _document.StyleEngineOrNull?.CommitPendingCancellations();

        // Update the current time of the timelines (the time never goes backwards); the scroll-driven timelines are sampled once,
        // as the step 1 of "update animations and send events" (https://drafts.csswg.org/scroll-animations-1/#event-loop)
        _timelineTime = _timelineTime is { } previous ? Math.Max(previous, now) : now;
        UpdateScrollDrivenTimelines();

        // The SMIL animations are sampled at the time of the frame (https://svgwg.org/specs/animations/)
        var smilNeedsFrames = _document.SmilOrNull is { } smil && smil.OnFrame();

        var animations = _trackedAnimations.ToArray();
        Array.Sort(animations, static (a, b) => a.GlobalIndex.CompareTo(b.GlobalIndex));
        var changed = false;
        var mayHaveSkippedContents = animations.Length > 0 && _document.GetStyleEngine().MayHaveContentVisibility();

        // The animations whose effects may have changed: the styles of their targets are invalidated
        List<Animation>? sampled = null;
        foreach (var animation in animations)
        {
            var timeDependent = animation.NeedsFrames;
            if (animation.RunPendingTask())
            {
                changed = true;
                timeDependent = true;
            }

            if (timeDependent)
            {
                (sampled ??= []).Add(animation);
            }

            // https://drafts.csswg.org/css-contain-2/#cv-notes: "While an element is skipped, CSS transitions and animations on the
            // element do not update": the CSS animations and transitions of the skipped contents do not finish nor fire events (the
            // computed styles still sample them) until they stop being skipped; the ones without owning element behave as the
            // programmatic animations, which are not affected (as in the browsers). A content-visibility: auto element whose proximity
            // to the viewport is not determined yet has not started skipping its contents (its initial determination is made by the
            // rendering update after the animations are updated)
            if (mayHaveSkippedContents && GetOwningElement(animation) is { } owningElement && Css.Layout.ContentRelevancy.IsInSkippedContents(owningElement, ignoreUndetermined: true))
                continue;

            animation.UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
            animation.QueueDeclarativeEvents();
            if (timeDependent)
            {
                changed = true;
            }

            // https://drafts.css-houdini.org/css-animation-worklet-1/#running-animators: the animator of a worklet animation sets the
            // local times of its effects from the current time of the animation, before the animation frame callbacks
            if (animation is WorkletAnimation workletAnimation && workletAnimation.Worklet.UpdateAnimator(workletAnimation, _timelineTime.Value))
            {
                changed = true;
                if (!timeDependent)
                {
                    (sampled ??= []).Add(animation);
                }
            }

            if (!animation.NeedsFrames)
            {
                _trackedAnimations.Remove(animation);
            }
        }

        if (changed && sampled is not null)
        {
            // (the time-dependent animations of the skipped contents are sampled again too, as their computed styles are)
            foreach (var animation in sampled)
            {
                InvalidateStyles(animation);
            }
        }

        RemoveReplacedAnimations();

        _document.EventLoop?.PerformMicrotaskCheckpoint();

        // Dispatch the pending events, sorted by their scheduled event time (unresolved first) and the composite order
        if (_pendingEvents.Count > 0)
        {
            var events = _pendingEvents.ToArray();
            _pendingEvents.Clear();
            Array.Sort(events, static (a, b) =>
            {
                var result = (a.ScheduledTime, b.ScheduledTime) switch
                {
                    (null, null) => 0,
                    (null, _) => -1,
                    (_, null) => 1,
                    var (x, y) => x.Value.CompareTo(y.Value),
                };

                if (result == 0 && !ReferenceEquals(a.Animation, b.Animation))
                {
                    result = a.Animation.CompareCompositeOrder(b.Animation);
                }

                return result != 0 ? result : a.Sequence.CompareTo(b.Sequence);
            });

            foreach (var pending in events)
            {
                pending.Target.FireEvent(pending.Event);
            }
        }

        if (_trackedAnimations.Count > 0 || _pendingEvents.Count > 0 || smilNeedsFrames)
        {
            RequestFrame();
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/web-animations-1/#removing-replaced-animations: the finished, filling animations whose effect is
    /// entirely replaced by animations later in the composite order are removed.
    /// </summary>
    private void RemoveReplacedAnimations()
    {
        if (_targets.Count == 0)
            return;

        foreach (var (element, elementAnimations) in _targets.ToArray())
        {
            foreach (var effects in elementAnimations.EnumerateEffectLists())
            {
                RemoveReplacedAnimations(element, effects);
            }
        }
    }

    private void RemoveReplacedAnimations(Element element, List<KeyframeEffect> effects)
    {
        if (effects.Count < 2)
            return;

        var candidates = effects
            .Select(effect => effect.Animation)
            .Where(animation => animation is not null)
            .Cast<Animation>()
            .OrderBy(animation => animation, Comparer<Animation>.Create(static (a, b) => a.CompareCompositeOrder(b)))
            .ToList();

        // The properties replaced by the animations later in the composite order
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var animation = candidates[i];
            if (animation.EffectValue is not KeyframeEffect effect || !effect.IsInEffect)
                continue;

            var properties = effect.GetTargetPropertiesForReplacement();
            var isReplaceable = animation.ReplaceState == AnimationReplaceState.Active
                && animation.PlayStateValue == AnimationPlayState.Finished
                && animation.Timeline is DocumentTimeline
                && animation.CompositeOrderClass == 2
                && ReferenceEquals(animation.DocumentForTiming, _document);

            if (isReplaceable && properties.Count > 0 && properties.All(replaced.Contains))
            {
                animation.ReplaceState = AnimationReplaceState.Removed;
                animation.OnRemoved();
                var timelineTime = animation.Timeline?.CurrentTimeValue;
                var removeEvent = new AnimationPlaybackEvent("remove", new AnimationPlaybackEventInit
                {
                    CurrentTime = animation.CurrentTimeValue is { } currentTime ? currentTime : null,
                    TimelineTime = timelineTime is { } t ? t : null,
                });
                QueueEvent(removeEvent, animation, animation, animation.Timeline?.ToOriginRelativeTime(timelineTime));
                continue;
            }

            if (animation.ReplaceState != AnimationReplaceState.Removed)
            {
                foreach (var property in properties)
                {
                    replaced.Add(property);
                }
            }
        }

        _ = element;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scroll-driven timelines (https://drafts.csswg.org/scroll-animations-1/#event-loop)
    // ----------------------------------------------------------------------------------------------------------------

    private Css.Animations.CssScrollTimelines? _cssTimelines;

    /// <summary>The named scroll progress and view progress timelines declared by the styles of the document.</summary>
    public Css.Animations.CssScrollTimelines CssTimelines => _cssTimelines ??= new Css.Animations.CssScrollTimelines(_document);

    /// <summary>
    /// Collects the named timelines declared by the styles of the document, then associates the CSS animations that reference
    /// a timeline by name with their target timeline (https://drafts.csswg.org/scroll-animations-1/#timeline-scoping), once the
    /// styles of all the elements are computed.
    /// </summary>
    public void UpdateCssNamedTimelines(Css.Cascade.StyleEngine engine)
    {
        if (_cssTimelines is not { HasNamedReferences: true } timelines)
            return;

        timelines.Update(engine);
        foreach (var ((element, _), animations) in _cssAnimations.ToArray())
        {
            foreach (var animation in animations)
            {
                if (animation.TimelineOverridden || animation.TimelineReference is not { Kind: Css.Animations.CssTimelineKind.Named, Name: { } name })
                    continue;

                var timeline = timelines.Find(element, name, animation.TimelineTreeScope) ?? timelines.InactiveTimeline;
                if (!ReferenceEquals(animation.TimelineValue, timeline))
                {
                    animation.SetTimelineByStyle(timeline);
                }
            }
        }
    }

    /// <summary>Whether scroll progress or view progress timelines were created for the document (and may still be alive).</summary>
    public bool HasScrollDrivenTimelines => _scrollDrivenTimelines is { Count: > 0 };

    /// <summary>Registers a scroll progress or view progress timeline: the rendering steps update it.</summary>
    public void RegisterScrollDrivenTimeline(ScrollTimeline timeline)
    {
        (_scrollDrivenTimelines ??= []).Add(new WeakReference<ScrollTimeline>(timeline));
        if (_document.EventLoop is { } eventLoop)
        {
            eventLoop.AddDocumentWithScrollDrivenTimelines(_document);
        }

        RequestFrame();
    }

    /// <summary>Called when a scroll position or the layout of the document changed: the scroll-driven timelines are updated by the next rendering update.</summary>
    public void OnScrollOrLayoutChanged()
    {
        if (HasScrollDrivenTimelines)
        {
            RequestFrame();
        }
    }

    /// <summary>
    /// Updates the current time and the ranges of the scroll-driven timelines of the document from the current layout (it is
    /// computed when it is out of date), then the auto-aligned start times of their animations
    /// (https://drafts.csswg.org/web-animations-2/#auto-aligning-start-time). The animations whose timing changed are updated
    /// by the next frame, and the styles are invalidated. Returns whether a timeline or a start time changed. After the layout of
    /// a rendering update, only the stale timelines are updated (see <see cref="ScrollTimeline.UpdateSnapshot"/>).
    /// </summary>
    public bool UpdateScrollDrivenTimelines(bool staleOnly = false)
    {
        if (_scrollDrivenTimelines is not { Count: > 0 } timelines)
            return false;

        var changed = false;
        var count = 0;
        for (var i = 0; i < timelines.Count; i++)
        {
            var reference = timelines[i];
            if (!reference.TryGetTarget(out var timeline))
                continue;

            timelines[count++] = reference;
            var timelineChanged = timeline.UpdateSnapshot(staleOnly);
            changed |= timelineChanged;
            if (timeline.Animations.Count == 0)
                continue;

            foreach (var animation in timeline.Animations.ToArray())
            {
                if (animation.UpdateAutoAlignedStartTime() || timelineChanged)
                {
                    changed = true;
                    animation.UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
                    OnAnimationChanged(animation);
                }
            }
        }

        timelines.RemoveRange(count, timelines.Count - count);
        return changed;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Targets
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the animations of the effects that target an element (or one of its pseudo-elements), or null.</summary>
    public ElementAnimations? GetElementAnimations(Element element) => _targets.GetValueOrDefault(element);

    /// <summary>Registers an effect that targets an element of the document (or one of its pseudo-elements).</summary>
    public void AddEffect(Element element, string? pseudoElement, KeyframeEffect effect)
    {
        if (!_targets.TryGetValue(element, out var elementAnimations))
        {
            elementAnimations = new ElementAnimations();
            _targets.Add(element, elementAnimations);
        }

        elementAnimations.Add(pseudoElement, effect);
        InvalidateStyles(element, pseudoElement);
    }

    /// <summary>Unregisters an effect that targets an element of the document.</summary>
    public void RemoveEffect(Element element, string? pseudoElement, KeyframeEffect effect)
    {
        if (!_targets.TryGetValue(element, out var elementAnimations))
            return;

        elementAnimations.Remove(pseudoElement, effect);
        if (elementAnimations.IsEmpty)
        {
            _targets.Remove(element);
        }

        InvalidateStyles(element, pseudoElement);
    }

    /// <summary>Returns whether a pseudo-element is another pseudo-element of the same element or one of its descendants in the view transition tree.</summary>
    private bool IsInclusiveDescendantPseudoElement(Element element, string? pseudoElement, string ancestor)
    {
        var tree = _document.GetViewTransitionPseudoTree(element);
        for (var current = pseudoElement; current is not null; current = Css.Layout.ViewTransitionPseudoTree.GetParent(tree, current))
        {
            if (string.Equals(current, ancestor, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the relevant animations whose target is an element of <paramref name="root"/> (or one of its pseudo-elements),
    /// sorted by their composite order (https://drafts.csswg.org/web-animations-1/#dom-documentorshadowroot-getanimations).
    /// </summary>
    public List<Animation> GetAnimations(Node root, bool shadowIncluding, Element? subtreeOf = null, string? pseudoElement = null, bool includeSubtree = false)
    {
        var result = new List<Animation>();
        foreach (var (element, elementAnimations) in _targets)
        {
            if (subtreeOf is not null)
            {
                // The subtree of a pseudo-element is made of its descendant pseudo-elements (the view transition tree)
                if (!ReferenceEquals(element, subtreeOf) && !(includeSubtree && pseudoElement is null && subtreeOf.IsShadowIncludingInclusiveAncestorOf(element)))
                    continue;
            }
            else if (!(shadowIncluding ? root.IsShadowIncludingInclusiveAncestorOf(element) : root.IsInclusiveAncestorOf(element)))
            {
                continue;
            }

            foreach (var (pseudo, effects) in elementAnimations.EnumerateEntries())
            {
                if (subtreeOf is not null && ReferenceEquals(element, subtreeOf) && !includeSubtree && pseudo != pseudoElement)
                    continue;

                if (subtreeOf is not null && includeSubtree && pseudoElement is not null && !IsInclusiveDescendantPseudoElement(element, pseudo, pseudoElement))
                    continue;

                foreach (var effect in effects)
                {
                    if (effect.Animation is { } animation && effect.IsRelevant && !result.Contains(animation))
                    {
                        result.Add(animation);
                    }
                }
            }
        }

        result.Sort(static (a, b) => a.CompareCompositeOrder(b));
        return result;
    }
}

/// <summary>
/// The running transitions of an element, by property, and the end values of its completed transitions
/// (https://drafts.csswg.org/css-transitions-1/#starting).
/// </summary>
internal sealed class ElementTransitions
{
    public Dictionary<string, CssTransition> Running { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Completed { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Running.Count == 0 && Completed.Count == 0;
}

/// <summary>The keyframe effects that target an element and its pseudo-elements (::before, ::after, ::marker).</summary>
internal sealed class ElementAnimations
{
    private List<KeyframeEffect>? _element;
    private Dictionary<string, List<KeyframeEffect>>? _pseudoElements;

    public bool IsEmpty => (_element is null || _element.Count == 0) && (_pseudoElements is null || _pseudoElements.Count == 0);

    /// <summary>Returns the effects that target the element (null) or one of its pseudo-elements (e.g. "before"), or null.</summary>
    public List<KeyframeEffect>? Get(string? pseudoElement) => pseudoElement is null ? _element : _pseudoElements?.GetValueOrDefault(pseudoElement);

    public void Add(string? pseudoElement, KeyframeEffect effect)
    {
        List<KeyframeEffect> effects;
        if (pseudoElement is null)
        {
            effects = _element ??= [];
        }
        else
        {
            _pseudoElements ??= new(StringComparer.Ordinal);
            if (!_pseudoElements.TryGetValue(pseudoElement, out effects!))
            {
                effects = [];
                _pseudoElements.Add(pseudoElement, effects);
            }
        }

        if (!effects.Contains(effect))
        {
            effects.Add(effect);
        }
    }

    public void Remove(string? pseudoElement, KeyframeEffect effect)
    {
        if (Get(pseudoElement) is not { } effects)
            return;

        effects.Remove(effect);
        if (effects.Count == 0 && pseudoElement is not null)
        {
            _pseudoElements!.Remove(pseudoElement);
        }
    }

    public IEnumerable<List<KeyframeEffect>> EnumerateEffectLists()
    {
        if (_element is { Count: > 0 })
            yield return _element;

        if (_pseudoElements is not null)
        {
            foreach (var effects in _pseudoElements.Values)
            {
                yield return effects;
            }
        }
    }

    public IEnumerable<(string? PseudoElement, List<KeyframeEffect> Effects)> EnumerateEntries()
    {
        if (_element is { Count: > 0 })
            yield return (null, _element);

        if (_pseudoElements is not null)
        {
            foreach (var (pseudo, effects) in _pseudoElements.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                yield return (pseudo, effects);
            }
        }
    }
}
