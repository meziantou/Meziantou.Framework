using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The options of a keyframe effect (https://drafts.csswg.org/web-animations-1/#dictdef-keyframeeffectoptions).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#dictdef-keyframeeffectoptions"/>
[IdlInterface("KeyframeEffectOptions", "https://drafts.csswg.org/web-animations-1/#dictdef-keyframeeffectoptions")]
public class KeyframeEffectOptions : EffectTiming
{
    /// <summary>Gets the composite operation of the effect.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffectoptions-composite"/>
    [IdlMember("composite", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffectoptions-composite")]
    public CompositeOperation Composite { get; init; }

    /// <summary>Gets the pseudo-element targeted by the effect (e.g. <c>::before</c>), or null to target the element.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffectoptions-pseudoelement"/>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffectoptions-pseudoelement")]
    public string? PseudoElement { get; init; }

    /// <summary>Gets the iteration composite operation of the effect.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-keyframeeffectoptions-iterationcomposite"/>
    [IdlMember("iterationComposite", "https://drafts.csswg.org/web-animations-2/#dom-keyframeeffectoptions-iterationcomposite")]
    public IterationCompositeOperation IterationComposite { get; init; }
}

/// <summary>An animation effect that animates the properties of an element with keyframes (https://drafts.csswg.org/web-animations-1/#keyframe-effects).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#keyframeeffect"/>
[IdlInterface("KeyframeEffect", "https://drafts.csswg.org/web-animations-1/#keyframeeffect")]
public partial class KeyframeEffect : AnimationEffect
{
    private Element? _target;
    private string? _pseudoElement;
    private List<ProcessedKeyframe> _keyframes = [];
    private Document? _registeredDocument;
    private Element? _registeredTarget;
    private string? _registeredPseudoElement;

    /// <summary>Creates a keyframe effect whose duration is <paramref name="duration"/> milliseconds (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect).</summary>
    /// <exception cref="TypeErrorException">The keyframes or the duration are invalid.</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect")]
    public KeyframeEffect(Element? target, IEnumerable<Keyframe>? keyframes, double duration)
        : this(target, keyframes, new KeyframeEffectOptions { Duration = duration })
    {
    }

    /// <summary>Creates a keyframe effect (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect).</summary>
    /// <exception cref="TypeErrorException">The keyframes or the timing properties are invalid.</exception>
    /// <exception cref="DomException">The pseudo-element is invalid (SyntaxError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect")]
    public KeyframeEffect(Element? target, IEnumerable<Keyframe>? keyframes, KeyframeEffectOptions? options = null)
    {
        options ??= new KeyframeEffectOptions();
        UpdateTimingCore(ToOptionalTiming(options));
        CompositeValue = options.Composite;
        IterationCompositeValue = options.IterationComposite;
        _pseudoElement = ParsePseudoElement(options.PseudoElement);
        _target = target;
        _keyframes = KeyframeProcessing.Process(keyframes);
    }

    /// <summary>Creates the effect of a CSS animation or transition.</summary>
    internal KeyframeEffect(Element target, string? pseudoElement)
    {
        _target = target;
        _pseudoElement = pseudoElement;
    }

    /// <summary>Creates a copy of a keyframe effect (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect-source).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect-source"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-keyframeeffect-source")]
    public KeyframeEffect(KeyframeEffect source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _target = source._target;
        _pseudoElement = source._pseudoElement;
        CompositeValue = source.CompositeValue;
        IterationCompositeValue = source.IterationCompositeValue;
        Delay = source.Delay;
        EndDelay = source.EndDelay;
        Fill = source.Fill;
        IterationStart = source.IterationStart;
        Iterations = source.Iterations;
        Duration = source.Duration;
        Direction = source.Direction;
        Easing = source.Easing;
        EasingFunction = source.EasingFunction;
        _keyframes = [.. source._keyframes];
    }

    /// <summary>Gets or sets the element targeted by the effect (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-target).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-target"/>
    [IdlMember("target", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-target")]
    public Element? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value))
                return;

            _target = value;
            OnTargetChanged();
        }
    }

    /// <summary>The target element, without side effects.</summary>
    internal Element? TargetElement => _target;

    /// <summary>
    /// Gets or sets the pseudo-element targeted by the effect, or null to target the element
    /// (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-pseudoelement).
    /// </summary>
    /// <exception cref="DomException">The pseudo-element is invalid (SyntaxError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-pseudoelement"/>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-pseudoelement")]
    public string? PseudoElement
    {
        get => _pseudoElement is null ? null : "::" + _pseudoElement;
        set
        {
            var pseudoElement = ParsePseudoElement(value);
            if (pseudoElement == _pseudoElement)
                return;

            _pseudoElement = pseudoElement;
            OnTargetChanged();
        }
    }

    /// <summary>The name of the targeted pseudo-element (e.g. "before"), or null.</summary>
    internal string? PseudoElementName => _pseudoElement;

    /// <summary>Gets or sets the composite operation of the effect (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-composite).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-composite"/>
    [IdlMember("composite", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-composite")]
    public CompositeOperation Composite
    {
        get => CompositeValue;
        set
        {
            CompositeValue = value;
            Animation?.OnKeyframesSetByScript();
            Animation?.OnEffectChanged();
        }
    }

    /// <summary>The composite operation, set without side effects (CSS animations).</summary>
    internal CompositeOperation CompositeValue { get; set; }

    /// <summary>Gets or sets the iteration composite operation of the effect (https://drafts.csswg.org/web-animations-2/#dom-keyframeeffect-iterationcomposite).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-keyframeeffect-iterationcomposite"/>
    [IdlMember("iterationComposite", "https://drafts.csswg.org/web-animations-2/#dom-keyframeeffect-iterationcomposite")]
    public IterationCompositeOperation IterationComposite
    {
        get => IterationCompositeValue;
        set
        {
            IterationCompositeValue = value;
            Animation?.OnEffectChanged();
        }
    }

    internal IterationCompositeOperation IterationCompositeValue { get; set; }

    /// <summary>The processed keyframes.</summary>
    internal List<ProcessedKeyframe> Keyframes => _keyframes;

    /// <summary>Returns the keyframes of the effect (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-getkeyframes).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-getkeyframes"/>
    [IdlMember("getKeyframes", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-getkeyframes")]
    public IReadOnlyList<ComputedKeyframe> GetKeyframes()
    {
        Animation?.FlushPendingStyle();
        return GetKeyframesCore();
    }

    /// <summary>Whether a keyframe of the effect has a timeline offset (e.g. "entry 10%").</summary>
    internal bool HasTimelineOffsets
    {
        get
        {
            foreach (var keyframe in _keyframes)
            {
                if (keyframe.RangeOffset is not null)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Resolves the computed offsets of the keyframes with a timeline offset (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes):
    /// the position of their point of the timeline in the attachment range of the animation. A keyframe whose timeline has no
    /// such range (e.g. a document timeline, an inactive timeline) or whose attachment range is empty has no computed offset (NaN).
    /// </summary>
    internal void ResolveTimelineOffsets()
    {
        if (!HasTimelineOffsets)
            return;

        var animation = Animation;
        ScrollTimelineSnapshot? snapshot = animation?.TimelineValue is ScrollTimeline { Snapshot: { IsActive: true } activeSnapshot } ? activeSnapshot : null;
        double start = 0;
        double end = 0;
        if (snapshot is not null)
        {
            start = snapshot.GetFraction(animation!.RangeStartValue, isEnd: false);
            end = snapshot.GetFraction(animation.RangeEndValue, isEnd: true);
        }

        foreach (var keyframe in _keyframes)
        {
            if (keyframe.RangeOffset is not { } rangeOffset)
                continue;

            // An empty attachment range gives a NaN offset (WPT timeline-offset-keyframes-zero-length-attachment-range.html)
            keyframe.IsOffsetUnresolved = snapshot is null;
            keyframe.ComputedOffset = snapshot is null || end == start ? double.NaN : (snapshot.GetFraction(rangeOffset, isEnd: false) - start) / (end - start);
        }
    }

    /// <summary>
    /// Returns the keyframes of a CSS animation with the implicit keyframes of the properties of its keyframes with a timeline
    /// offset (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes): the automatic 0% (100%) keyframe of a
    /// property is only generated when it has no keyframe at or before 0% (at or after 100%), once the timeline offsets are
    /// resolved. The implicit values are the underlying values.
    /// </summary>
    private List<ProcessedKeyframe> GetKeyframesWithImplicitTimelineKeyframes()
    {
        if (Animation is not CssAnimation || !HasTimelineOffsets)
            return _keyframes;

        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyframe in _keyframes)
        {
            if (keyframe.RangeOffset is not null)
            {
                if (double.IsNaN(keyframe.ComputedOffset))
                    return _keyframes;

                properties.UnionWith(keyframe.Longhands.Keys);
            }
        }

        // The keyframes with a timeline offset are reported in the order of their computed offsets, after the other ones
        var result = new List<ProcessedKeyframe>(_keyframes.Count + 2);
        result.AddRange(_keyframes.Where(k => k.RangeOffset is null));
        result.AddRange(_keyframes.Where(k => k.RangeOffset is not null).OrderBy(k => k.ComputedOffset));
        foreach (var offset in (ReadOnlySpan<double>)[0, 1])
        {
            var missing = new SortedSet<string>(properties, StringComparer.Ordinal);
            foreach (var keyframe in _keyframes)
            {
                if (offset == 0 ? keyframe.ComputedOffset <= 0 : keyframe.ComputedOffset >= 1)
                {
                    missing.ExceptWith(keyframe.Longhands.Keys);
                }
            }

            if (missing.Count == 0)
                continue;

            var index = result.FindIndex(k => k.IsImplicit && k.ComputedOffset == offset);
            var implicitKeyframe = index >= 0 ? result[index] : new ProcessedKeyframe
            {
                Offset = offset,
                ComputedOffset = offset,
                Easing = result.Find(k => k.IsImplicit)?.Easing ?? "linear",
                Composite = CompositeOperationOrAuto.Replace,
                IsImplicit = true,
            };

            // The keyframe is copied: the keyframes of the effect are not changed
            var copy = new ProcessedKeyframe
            {
                Offset = implicitKeyframe.Offset,
                ComputedOffset = implicitKeyframe.ComputedOffset,
                Easing = implicitKeyframe.Easing,
                EasingFunction = implicitKeyframe.EasingFunction,
                Composite = implicitKeyframe.Composite,
                Source = implicitKeyframe.Source,
                IsImplicit = true,
                Properties = [.. implicitKeyframe.Properties],
                Longhands = new Dictionary<string, Css.Values.CssLonghandValue>(implicitKeyframe.Longhands, StringComparer.Ordinal),
            };

            foreach (var property in missing)
            {
                copy.Longhands[property] = new Css.Values.CssLonghandValue(property, Css.Animations.CssKeyframesResolver.NeutralValue);
            }

            if (index >= 0)
            {
                result[index] = copy;
            }
            else if (offset == 0)
            {
                result.Insert(0, copy);
            }
            else
            {
                var rangeIndex = result.FindIndex(k => k.RangeOffset is not null);
                result.Insert(rangeIndex >= 0 ? rangeIndex : result.Count, copy);
            }
        }

        return result;
    }

    private protected virtual IReadOnlyList<ComputedKeyframe> GetKeyframesCore()
    {
        ResolveTimelineOffsets();
        var keyframes = GetKeyframesWithImplicitTimelineKeyframes();
        var result = new List<ComputedKeyframe>(keyframes.Count);
        Css.Cascade.ComputedStyle? style = null;
        foreach (var keyframe in keyframes)
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, value) in keyframe.Properties)
            {
                properties[name] = value;
            }

            // The implicit values of a CSS animation are reported as the computed values of the target, like browsers
            // (https://drafts.csswg.org/css-animations-2/#keyframes)
            foreach (var (name, value) in keyframe.Longhands)
            {
                if (value.Value != Css.Animations.CssKeyframesResolver.NeutralValue || properties.ContainsKey(name) || _target is null || CssPropertyRegistry.IsCustomProperty(name))
                    continue;

                style ??= _target.NodeDocument.GetStyleEngine().GetStyle(_target, PseudoElementName);
                properties[name] = style.GetUnanimatedComputedValue(name);
            }

            result.Add(new ComputedKeyframe
            {
                Offset = keyframe.Offset,
                RangeOffset = keyframe.RangeOffset,
                IsOffsetUnresolved = keyframe.IsOffsetUnresolved,
                ComputedOffset = keyframe.ComputedOffset,
                Easing = keyframe.Easing,
                Composite = keyframe.Composite,
                Properties = properties,
            });
        }

        return result;
    }

    /// <summary>Replaces the keyframes of the effect (https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-setkeyframes).</summary>
    /// <exception cref="TypeErrorException">The keyframes are invalid.</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-setkeyframes"/>
    [IdlMember("setKeyframes", "https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-setkeyframes")]
    public void SetKeyframes(IEnumerable<Keyframe>? keyframes)
    {
        _keyframes = KeyframeProcessing.Process(keyframes);
        Animation?.OnKeyframesSetByScript();
        Animation?.OnEffectChanged();
    }

    /// <summary>Replaces the keyframes without notifying the animation (CSS animations).</summary>
    internal void SetProcessedKeyframes(List<ProcessedKeyframe> keyframes)
    {
        _keyframes = keyframes;
        Animation?.OnEffectChanged();
    }

    /// <summary>Whether the effect is relevant: current or in effect (https://drafts.csswg.org/web-animations-1/#relevant-animation).</summary>
    internal bool IsRelevant => IsCurrent || IsInEffect;

    /// <summary>The properties animated by the effect (longhands and custom properties).</summary>
    internal IEnumerable<string> AnimatedProperties
    {
        get
        {
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var keyframe in _keyframes)
            {
                properties.UnionWith(keyframe.Longhands.Keys);
            }

            return properties;
        }
    }

    /// <summary>The properties of the effect, used to determine whether it is replaced by other effects (https://drafts.csswg.org/web-animations-1/#removing-replaced-animations).</summary>
    internal HashSet<string> GetTargetPropertiesForReplacement() => [.. AnimatedProperties];

    private protected override Window? GetRealmWindow() => base.GetRealmWindow() ?? _target?.NodeDocument.RelevantWindow;

    internal override void OnAnimationChanged(Animation? previous) => UpdateRegistration();

    private void OnTargetChanged()
    {
        UpdateRegistration();
        Animation?.OnEffectChanged();
    }

    /// <summary>
    /// Registers the effect with the document of its target while it is associated with an animation that is not idle and that
    /// is not settled out of effect (the effect stack of the target, see <see cref="IsSettledOutOfEffect"/>): the canceled and
    /// completed CSS transitions and animations would otherwise stay in the effect stack of their target. A change of the
    /// animation (e.g. <c>play()</c>) registers the effect again (see <see cref="Animations.Animation.IsIdle"/>).
    /// </summary>
    internal void UpdateRegistration()
    {
        var target = Animation is { IsIdle: false } animation && !IsSettledOutOfEffect(animation) ? _target : null;
        if (ReferenceEquals(target, _registeredTarget) && _pseudoElement == _registeredPseudoElement && ReferenceEquals(target?.NodeDocument, _registeredDocument))
            return;

        if (_registeredTarget is not null)
        {
            _registeredDocument!.Animations.RemoveEffect(_registeredTarget, _registeredPseudoElement, this);
        }

        _registeredTarget = target;
        _registeredPseudoElement = _pseudoElement;
        _registeredDocument = target?.NodeDocument;
        if (target is not null)
        {
            _registeredDocument!.Animations.AddEffect(target, _pseudoElement, this);
        }
    }

    /// <summary>
    /// Returns whether the animation is finished, and its effect is neither current nor in effect and stays so until the animation
    /// or the effect is changed: its current time is held, on a timeline whose time never goes backwards. The effect stays in the
    /// effect stack while the animation has pending events (the view transitions find the animations through the effect stacks).
    /// </summary>
    private bool IsSettledOutOfEffect(Animation animation)
    {
        if (animation.PlayStateValue != AnimationPlayState.Finished || animation.TimelineValue is { IsMonotonicallyIncreasing: false } || IsInEffect)
            return false;

        // (an effect is current before its active phase while its animation plays forwards, and after it while it plays backwards,
        // see IsCurrent)
        var phase = GetPhase(LocalTime);
        if ((animation.EffectivePlaybackRate > 0 && phase == AnimationEffectPhase.Before) || (animation.EffectivePlaybackRate < 0 && phase == AnimationEffectPhase.After))
            return false;

        return animation.DocumentForTiming?.AnimationsOrNull?.HasPendingEvents(animation) != true;
    }

    /// <summary>
    /// Parses the pseudo-element of an effect: null, or a valid pseudo-element selector (e.g. <c>::before</c>, or the legacy
    /// <c>:before</c>); returns its name. Throws a SyntaxError when it is invalid.
    /// </summary>
    internal static string? ParsePseudoElement(string? value)
    {
        if (value is null)
            return null;

        if (!value.StartsWith(':', StringComparison.Ordinal) || Css.Cascade.ComputedStyleDeclarations.ParsePseudoElement(value) is not { } name)
            throw new DomException("'" + value + "' is not a valid pseudo-element.", "SyntaxError");

        return name;
    }

    private static OptionalEffectTiming ToOptionalTiming(EffectTiming timing) => new()
    {
        Delay = timing.Delay,
        EndDelay = timing.EndDelay,
        Fill = timing.Fill,
        IterationStart = timing.IterationStart,
        Iterations = timing.Iterations,
        Duration = timing.Duration,
        Direction = timing.Direction,
        Easing = timing.Easing,
    };

    internal static OptionalEffectTiming ToOptionalTimingInternal(EffectTiming timing) => ToOptionalTiming(timing);
}
