using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Svg.Animations;

/// <summary>
/// The time container of an SVG document fragment (https://svgwg.org/specs/animations/#TimingAttributes): the document
/// timeline of its outermost svg element, which begins when the load event of the document is fired, can be paused and
/// seeked (<c>pauseAnimations()</c>, <c>setCurrentTime()</c>), and the animation elements of the fragment.
/// </summary>
internal sealed class SmilTimeContainer(SvgSvgElement owner)
{
    private bool _started;
    private bool _paused;

    // The time of the timeline when it is paused or not started, or at the reference time when it runs
    private SmilTime _presentationTime;
    private double _referenceTime;

    /// <summary>The time of the last update of the intervals of the elements.</summary>
    private SmilTime _latestUpdateTime;

    /// <summary>The animation elements of the document fragment, in document order.</summary>
    public List<SvgAnimationElement> Elements { get; } = [];

    public SvgSvgElement Owner => owner;

    public bool IsPaused => _paused;

    public bool IsStarted => _started;

    /// <summary>The time of the last update: the time of the instances created by the events and the scripts.</summary>
    public SmilTime LatestUpdateTime => _latestUpdateTime;

    /// <summary>The time of the clock of the document, in milliseconds (0 for a document without window: its timeline only changes when it is seeked).</summary>
    private double Now => owner.NodeDocument.RelevantWindow is { } window && owner.NodeDocument.BrowsingContext is not null ? window.GetCurrentHighResolutionTime() : 0;

    /// <summary>The current time of the timeline.</summary>
    public SmilTime Elapsed
    {
        get
        {
            if (!_started || _paused)
                return _presentationTime;

            var elapsed = _presentationTime + SmilTime.FromMilliseconds(Now - _referenceTime);

            // The clock never goes backwards
            return SmilTime.Max(elapsed, _latestUpdateTime);
        }
    }

    /// <summary>Starts the timeline (the load event of the document was fired): a time set before is the time it starts at.</summary>
    /// <param name="referenceTime">The time of the clock of the document the timeline began at, or null for the current time.</param>
    public void Start(double? referenceTime = null)
    {
        if (_started)
            return;

        _started = true;
        _referenceTime = referenceTime ?? Now;
    }

    public void Pause()
    {
        if (_paused)
            return;

        if (_started)
        {
            _presentationTime = Elapsed;
        }

        _paused = true;
    }

    public void Unpause()
    {
        if (!_paused)
            return;

        _paused = false;
        _referenceTime = Now;
    }

    /// <summary>Seeks the timeline (https://svgwg.org/specs/animations/#__svg__SVGSVGElement__setCurrentTime).</summary>
    public void SetElapsed(SmilTime time)
    {
        _presentationTime = time;
        _referenceTime = Now;
        if (!_started)
            return;

        Seek(time);
    }

    /// <summary>
    /// Updates the intervals of the elements up to the current time. The first update of a timeline that starts at a time
    /// that was set before is a seek.
    /// </summary>
    public void Update()
    {
        if (!_started)
            return;

        var target = Elapsed;
        if (target < _latestUpdateTime)
        {
            Seek(target);
            return;
        }

        if (_firstUpdate && !target.IsZero)
        {
            Seek(target);
            return;
        }

        _firstUpdate = false;
        UpdateTimings(target, events: true);
    }

    private bool _firstUpdate = true;

    /// <summary>
    /// https://www.w3.org/TR/SMIL3/smil-timing.html#q110 (seeking): the elements that were active and are not after the seek get
    /// an end event, the elements that were not active and are after the seek get a begin event; the events of the intervals
    /// that are skipped are not fired.
    /// </summary>
    private void Seek(SmilTime target)
    {
        _firstUpdate = false;
        var elements = Elements.ToArray();
        var wasActive = new bool[elements.Length];
        var activeBegins = new SmilTime[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            wasActive[i] = elements[i].IsActive;
            activeBegins[i] = elements[i].ActiveBegin;
        }

        if (target < _latestUpdateTime)
        {
            // Seeking backwards: the intervals are resolved again from the begin of the timeline
            foreach (var element in elements)
            {
                element.ResetTiming(keepInstanceTimes: true);
            }

            _latestUpdateTime = SmilTime.Zero;
        }

        UpdateTimings(target, events: false);
        for (var i = 0; i < elements.Length; i++)
        {
            // An element that is active in another interval ended and began
            var isActive = elements[i].IsActive;
            var newInterval = wasActive[i] && isActive && activeBegins[i] != elements[i].ActiveBegin;
            if (wasActive[i] && (!isActive || newInterval))
            {
                elements[i].QueueTimeEvent("endEvent", 0);
            }

            if (isActive && (!wasActive[i] || newInterval))
            {
                elements[i].QueueTimeEvent("beginEvent", 0);
            }
        }
    }

    private void UpdateTimings(SmilTime target, bool events)
    {
        // The intervals are updated at each time one of them begins, repeats or ends, in order; a bound stops the timelines
        // whose intervals never settle (e.g. cyclic syncbase values without duration)
        var time = SmilTime.Min(_latestUpdateTime, target);
        for (var guard = 0; guard < 20_000; guard++)
        {
            ProcessAt(time, events);
            var next = SmilTime.Unresolved;
            foreach (var element in Elements)
            {
                next = SmilTime.Min(next, element.GetNextIntervalTime(time, target, includeRepeats: events));
            }

            if (next > target)
                break;

            time = next;
        }

        if (time < target)
        {
            ProcessAt(target, events);
        }

        _latestUpdateTime = target;
    }

    private void ProcessAt(SmilTime time, bool events)
    {
        // The new intervals create instance times in the elements that depend on them (syncbase values), which resolve their
        // intervals in turn
        for (var guard = 0; guard < 32; guard++)
        {
            var changed = false;
            foreach (var element in Elements)
            {
                changed |= element.UpdateInterval(time);
            }

            if (!changed)
                break;
        }

        foreach (var element in Elements)
        {
            element.UpdateActiveState(time, events);
        }
    }

    /// <summary>Whether the values of the animations change with the time: the timeline runs and an interval is active or will begin.</summary>
    public bool NeedsFrames
    {
        get
        {
            if (!_started || _paused)
                return false;

            foreach (var element in Elements)
            {
                if (element.NeedsFrames(_latestUpdateTime))
                    return true;
            }

            return false;
        }
    }
}

/// <summary>
/// The SMIL animations of a document (https://svgwg.org/specs/animations/): the animation elements, the time containers of
/// their document fragments, and the animated values they apply to their targets.
/// </summary>
internal sealed class SmilAnimations(Document document)
{
    private readonly List<SvgAnimationElement> _elements = [];
    private readonly List<SmilTimeContainer> _containers = [];
    private int _domVersion = -1;
    private int _treeVersion = -1;
    private bool _dirty;
    private bool _updating;

    // The animated values applied by the last update
    private Dictionary<Element, Dictionary<string, string>> _attributeValues = [];
    private Dictionary<Element, List<SvgAnimationElement>> _cssTargets = [];
    private Dictionary<Element, SvgAffine> _motionTransforms = [];

    /// <summary>Whether the timelines are started: the load event of the document was fired (a document without browsing context has no load event).</summary>
    public bool IsStarted => document.BrowsingContext is null || document.LoadEventFired;

    public void Register(SvgAnimationElement element)
    {
        if (!_elements.Contains(element))
        {
            _elements.Add(element);
        }

        _treeVersion = -1;
        SetDirty();
    }

    public void Unregister(SvgAnimationElement element)
    {
        if (_elements.Remove(element))
        {
            element.TimeContainer?.Elements.Remove(element);
            element.TimeContainer = null;
            _treeVersion = -1;
            SetDirty();
        }
    }

    /// <summary>The animations must be sampled again (an attribute of an animation element, an instance time or a timeline changed).</summary>
    public void SetDirty()
    {
        _dirty = true;
        document.Animations.RequestSmilFrame();
    }

    /// <summary>Starts the timelines of the document fragments (the load event of the document was fired).</summary>
    public void Start()
    {
        foreach (var container in _containers)
        {
            container.Start();
        }

        // The animations are sampled at the time the timelines begin at
        SetDirty();
        Update();
    }

    /// <summary>Returns the time container of an outermost svg element, created when needed.</summary>
    public SmilTimeContainer GetContainer(SvgSvgElement outermost)
    {
        if (outermost.TimeContainer is not { } container)
        {
            container = new SmilTimeContainer(outermost);
            outermost.TimeContainer = container;

            // The timeline began when the load event of the document was fired, or when the element was inserted afterwards
            if (IsStarted)
            {
                container.Start(outermost.TimelineBeginTime ?? document.LoadEventTime);
            }
        }

        if (!_containers.Contains(container))
        {
            _containers.Add(container);
        }

        if (IsStarted)
        {
            container.Start();
        }

        return container;
    }

    /// <summary>Samples the animations when something changed since the last update (before the styles or the animated values are read).</summary>
    public void Flush()
    {
        if (_updating)
            return;

        if (_dirty || document.DomVersion != _domVersion)
        {
            Update();
        }
    }

    /// <summary>Samples the animations at the time of a frame; returns whether another frame is needed.</summary>
    public bool OnFrame()
    {
        if (_elements.Count == 0 && _attributeValues.Count == 0 && _cssTargets.Count == 0 && _motionTransforms.Count == 0)
            return false;

        Update();
        foreach (var container in _containers)
        {
            if (container.NeedsFrames)
                return true;
        }

        return false;
    }

    /// <summary>Updates the timelines and applies the animated values.</summary>
    public void Update()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            _dirty = false;
            ResolveStructure();
            foreach (var container in _containers.ToArray())
            {
                container.Update();
            }

            ApplyAnimationValues();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Updates the timelines to the current time without applying the animated values (they are applied by the next sample):
    /// the state of the elements when an event or a script creates an instance time.
    /// </summary>
    /// <param name="force">Whether the timelines are updated even when nothing changed since the last update (the clock advanced).</param>
    public void UpdateTimelines(bool force = true)
    {
        if (_updating || (!force && !_dirty && document.DomVersion == _domVersion))
            return;

        _updating = true;
        try
        {
            ResolveStructure();
            foreach (var container in _containers.ToArray())
            {
                container.Update();
            }
        }
        finally
        {
            _updating = false;
        }

        _dirty = true;
    }

    /// <summary>Called by a seek of a timeline: the animated values are applied at once.</summary>
    public void OnSeek()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            ApplyAnimationValues();
        }
        finally
        {
            _updating = false;
        }

        SetDirty();
    }

    /// <summary>Connects the conditions of the elements to the elements they refer to, before an event is dispatched in the document.</summary>
    public void ResolveReferencesBeforeEvent()
    {
        if (_elements.Count > 0 && document.DomVersion != _domVersion)
        {
            PrepareSeek();
            _dirty = true;
        }
    }

    /// <summary>Resolves the structure before a seek (the time containers, the targets and the conditions of the elements).</summary>
    public void PrepareSeek()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            ResolveStructure();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Assigns the elements to the time container of their outermost svg element, and resolves their targets and the
    /// elements their conditions refer to, after a change of the document.
    /// </summary>
    private void ResolveStructure()
    {
        if (document.DomVersion == _domVersion)
            return;

        _domVersion = document.DomVersion;
        if (document.TreeVersion != _treeVersion)
        {
            _treeVersion = document.TreeVersion;

            // The document order of the elements gives their priorities
            if (_elements.Count > 1)
            {
                var order = 0;
                var indexes = new Dictionary<SvgAnimationElement, int>(_elements.Count);
                AssignOrder(document, indexes, ref order);
                foreach (var element in _elements)
                {
                    element.DocumentOrder = indexes.GetValueOrDefault(element, int.MaxValue);
                }

                _elements.Sort(static (a, b) => a.DocumentOrder.CompareTo(b.DocumentOrder));
            }

            foreach (var container in _containers)
            {
                container.Elements.Clear();
            }

            foreach (var element in _elements)
            {
                // https://svgwg.org/svg2-draft/struct.html#ConditionalProcessing: the animation elements that are not rendered are not timed
                var outermost = SvgUserSpace.GetOutermostSvgElement(element);
                var container = outermost is null || ReferenceEquals(outermost, element) || !IsConditionallyProcessed(element) ? null : GetContainer(outermost);
                if (!ReferenceEquals(container, element.TimeContainer))
                {
                    element.TimeContainer = container;
                    element.ResetTiming(keepInstanceTimes: false);
                }

                container?.Elements.Add(element);
            }
        }

        foreach (var element in _elements)
        {
            element.ResolveReferences();
        }
    }

    /// <summary>
    /// Whether the conditional processing attributes of an element and of its ancestors evaluate to true, and the element (and
    /// each of its ancestors) is the child that its parent <c>switch</c> element renders.
    /// </summary>
    private static bool IsConditionallyProcessed(Element element)
    {
        for (Node? node = element; node is SvgElement current; node = current.ParentNode)
        {
            if (!SvgConditionalProcessing.Evaluate(current))
                return false;

            if (current.ParentNode is SvgSwitchElement parent)
            {
                // The first child whose conditional processing attributes evaluate to true
                for (var sibling = parent.FirstChild; sibling is not null && !ReferenceEquals(sibling, current); sibling = sibling.NextSibling)
                {
                    if (sibling is SvgElement previous && SvgConditionalProcessing.Evaluate(previous))
                        return false;
                }
            }
        }

        return true;
    }

    private static void AssignOrder(Node root, Dictionary<SvgAnimationElement, int> indexes, ref int order)
    {
        for (var node = root.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is SvgAnimationElement animation)
            {
                indexes[animation] = order++;
            }

            if (node is Element element)
            {
                if (element.AttachedShadowRoot is { } shadowRoot)
                {
                    AssignOrder(shadowRoot, indexes, ref order);
                }

                if (element.FirstChild is not null)
                {
                    AssignOrder(element, indexes, ref order);
                }
            }
        }
    }

    /// <summary>The animation elements of the document, in document order.</summary>
    public List<SvgAnimationElement> Elements => _elements;

    // ----------------------------------------------------------------------------------------------------------------
    // The animated values (https://www.w3.org/TR/SMIL3/smil-animation.html#q29: the animation sandwich model)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the animated value of an attribute of an element, or null when it is not animated.</summary>
    public string? GetAttributeValue(Element element, string name)
        => _attributeValues.TryGetValue(element, out var values) ? values.GetValueOrDefault(name) : null;

    /// <summary>Returns the animated values of the attributes of an element, or null.</summary>
    public Dictionary<string, string>? GetAttributeValues(Element element) => _attributeValues.GetValueOrDefault(element);

    /// <summary>
    /// Returns the animations in effect that animate a property of an element (the animations of the presentation attributes
    /// that are not reflected by an SVG DOM object), sorted by priority, or null.
    /// </summary>
    public List<SvgAnimationElement>? GetCssAnimations(Element element) => _cssTargets.GetValueOrDefault(element);

    /// <summary>Returns the supplemental transform of the <c>animateMotion</c> elements that target an element, or null.</summary>
    public SvgAffine? GetMotionTransform(Element element) => _motionTransforms.TryGetValue(element, out var transform) ? transform : null;

    private readonly record struct SandwichKey(SvgElement Target, SmilTargetKind Kind, string Attribute);

    private void ApplyAnimationValues()
    {
        // The sandwiches: the animations in effect of each target attribute, sorted by priority (the begin time of their
        // interval, then the document order)
        Dictionary<SandwichKey, List<SvgAnimationElement>>? sandwiches = null;
        foreach (var element in _elements)
        {
            if (!element.IsContributing || element.ResolvedTarget is not { } target || element.TargetKind == SmilTargetKind.None)
                continue;

            sandwiches ??= [];
            var key = new SandwichKey(target, element.TargetKind, element.TargetKind == SmilTargetKind.Motion ? "" : element.TargetAttribute);
            if (!sandwiches.TryGetValue(key, out var list))
            {
                list = [];
                sandwiches.Add(key, list);
            }

            list.Add(element);
        }

        var attributeValues = new Dictionary<Element, Dictionary<string, string>>();
        var cssTargets = new Dictionary<Element, List<SvgAnimationElement>>();
        var motionTransforms = new Dictionary<Element, SvgAffine>();
        var cssChanged = false;
        if (sandwiches is not null)
        {
            foreach (var (key, list) in sandwiches)
            {
                if (list.Count > 1)
                {
                    list.Sort(static (a, b) => a.ComparePriority(b));
                }

                switch (key.Kind)
                {
                    case SmilTargetKind.Attribute:
                        if (ComputeAttributeValue(key.Target, key.Attribute, list) is { } value)
                        {
                            if (!attributeValues.TryGetValue(key.Target, out var values))
                            {
                                values = new Dictionary<string, string>(StringComparer.Ordinal);
                                attributeValues.Add(key.Target, values);
                            }

                            values[key.Attribute] = value;
                        }

                        break;

                    case SmilTargetKind.Css:
                        if (!cssTargets.TryGetValue(key.Target, out var animations))
                        {
                            animations = [];
                            cssTargets.Add(key.Target, animations);
                        }

                        animations.AddRange(list);
                        foreach (var animation in list)
                        {
                            cssChanged |= animation.UpdateSampleState();
                        }

                        break;

                    case SmilTargetKind.Motion:
                        var transform = SvgAffine.Identity;
                        foreach (var animation in list)
                        {
                            if (animation is SvgAnimateMotionElement motion)
                            {
                                transform = motion.ComputeTransform(transform);
                            }
                        }

                        motionTransforms[key.Target] = transform;
                        break;
                }
            }
        }

        var changed = cssChanged || !SameAttributeValues(attributeValues, _attributeValues) || !SameCssTargets(cssTargets, _cssTargets) || !SameTransforms(motionTransforms, _motionTransforms);
        _attributeValues = attributeValues;
        _cssTargets = cssTargets;
        _motionTransforms = motionTransforms;
        if (changed)
        {
            // The styles (the presentation attributes), the layout and the painting depend on the animated values
            document.Animations.InvalidateStylesForSmil();
        }
    }

    /// <summary>Computes the animated value of an attribute: the animations are applied in order on top of the base value.</summary>
    private static string? ComputeAttributeValue(SvgElement target, string attribute, List<SvgAnimationElement> animations)
    {
        if (SmilValueType.Create(target, attribute, animations[0]) is not { } type)
            return null;

        var baseValue = type.GetBaseValue();
        var current = baseValue;
        var animated = false;
        foreach (var animation in animations)
        {
            if (animation.ComputeAnimatedValue(type, current) is { } value)
            {
                current = value;
                animated = true;
            }
        }

        return animated && current is not null ? type.Serialize(current) : null;
    }

    private static bool SameAttributeValues(Dictionary<Element, Dictionary<string, string>> a, Dictionary<Element, Dictionary<string, string>> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (element, values) in a)
        {
            if (!b.TryGetValue(element, out var other) || values.Count != other.Count)
                return false;

            foreach (var (name, value) in values)
            {
                if (!other.TryGetValue(name, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
                    return false;
            }
        }

        return true;
    }

    private static bool SameCssTargets(Dictionary<Element, List<SvgAnimationElement>> a, Dictionary<Element, List<SvgAnimationElement>> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (element, animations) in a)
        {
            if (!b.TryGetValue(element, out var other) || !animations.SequenceEqual(other))
                return false;
        }

        return true;
    }

    private static bool SameTransforms(Dictionary<Element, SvgAffine> a, Dictionary<Element, SvgAffine> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (element, transform) in a)
        {
            if (!b.TryGetValue(element, out var other) || transform != other)
                return false;
        }

        return true;
    }
}

/// <summary>What an animation element animates.</summary>
internal enum SmilTargetKind
{
    /// <summary>Nothing: there is no target element, or the attribute cannot be animated.</summary>
    None,

    /// <summary>An attribute reflected by an SVG DOM object (its animated value, <c>animVal</c>).</summary>
    Attribute,

    /// <summary>A property (a presentation attribute that is not reflected by an SVG DOM object).</summary>
    Css,

    /// <summary>The supplemental transform of <c>animateMotion</c>.</summary>
    Motion,
}
