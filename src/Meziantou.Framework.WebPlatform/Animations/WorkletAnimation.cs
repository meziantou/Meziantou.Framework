using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// A worklet animation (https://drafts.css-houdini.org/css-animation-worklet-1/#worklet-animation-desc): an animation whose
/// current time does not determine the local time of its effects. An animator, an instance of a class registered in the
/// animation worklet of the document (<c>CSS.animationWorklet</c>, <see cref="Document.AnimationWorklet"/>), gets the
/// current time of the animation at each frame and sets the local times of the effects.
/// </summary>
/// <remarks>
/// The animator instance exists while the animation is not idle. An effect has no local time (it is not in effect) until the
/// animator sets it, and keeps the last one the animator set: a worklet animation that is finished still applies its effects.
/// </remarks>
/// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#workletanimation"/>
[IdlInterface("WorkletAnimation", "https://drafts.css-houdini.org/css-animation-worklet-1/#workletanimation")]
public sealed class WorkletAnimation : Animation
{
    // The effects of the WorkletGroupEffect of the animation after the first one, which is the associated effect
    private readonly List<AnimationEffect> _otherEffects = [];

    /// <summary>
    /// Creates a worklet animation of an effect on the default timeline of the document of the current global object
    /// (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation).
    /// </summary>
    /// <param name="animatorName">The name of an animator registered in the animation worklet of the document.</param>
    /// <param name="effects">The effect, or null.</param>
    /// <exception cref="TypeErrorException">No animator of the name is registered, or its registration is invalid.</exception>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation")]
    public WorkletAnimation(string animatorName, AnimationEffect? effects = null)
        : this(animatorName, effects is null ? [] : [effects], isGroup: false, Document.CurrentGlobalDocument.Timeline, options: null)
    {
    }

    /// <summary>Creates a worklet animation of an effect on a timeline (null for no timeline).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation")]
    public WorkletAnimation(string animatorName, AnimationEffect? effects, AnimationTimeline? timeline)
        : this(animatorName, effects is null ? [] : [effects], isGroup: false, timeline, options: null)
    {
    }

    /// <summary>Creates a worklet animation of an effect on a timeline, with the options given to the constructor of the animator (a value of the script engine, structured cloned).</summary>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the options cannot be cloned.</exception>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation")]
    public WorkletAnimation(string animatorName, AnimationEffect? effects, AnimationTimeline? timeline, object? options)
        : this(animatorName, effects is null ? [] : [effects], isGroup: false, timeline, options ?? UndefinedValue.Instance)
    {
    }

    /// <summary>Creates a worklet animation of a group of effects, whose local times the animator sets individually, on the default timeline of the document of the current global object.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation")]
    public WorkletAnimation(string animatorName, IReadOnlyList<AnimationEffect> effects)
        : this(animatorName, effects, isGroup: true, Document.CurrentGlobalDocument.Timeline, options: null)
    {
    }

    /// <summary>Creates a worklet animation of a group of effects on a timeline (null for no timeline).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation")]
    public WorkletAnimation(string animatorName, IReadOnlyList<AnimationEffect> effects, AnimationTimeline? timeline)
        : this(animatorName, effects, isGroup: true, timeline, options: null)
    {
    }

    /// <summary>Creates a worklet animation of a group of effects on a timeline, with the options given to the constructor of the animator.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-workletanimation")]
    public WorkletAnimation(string animatorName, IReadOnlyList<AnimationEffect> effects, AnimationTimeline? timeline, object? options)
        : this(animatorName, effects, isGroup: true, timeline, options ?? UndefinedValue.Instance)
    {
    }

    private WorkletAnimation(string animatorName, IReadOnlyList<AnimationEffect> effects, bool isGroup, AnimationTimeline? timeline, object? options)
        : base(effect: null, GetTimeline(animatorName, effects, timeline))
    {
        var document = Document.CurrentGlobalDocument;
        Worklet = document.AnimationWorkletCore;
        AnimatorName = animatorName;
        HasGroupEffect = isGroup;

        // "Let serializedOptions be the result of StructuredSerialize(options). Rethrow any exceptions."
        if (options is not null and not UndefinedValue && document.RelevantWindow is { ScriptEngine: { } engine } window)
        {
            SerializedOptions = new SerializedValue(engine.StructuredSerialize(window, options, []));
        }

        // The first effect of a group is the associated effect of the animation; the other effects belong to the animation too
        for (var i = 1; i < effects.Count; i++)
        {
            var effect = effects[i];
            if (effect.Animation is { } previousAnimation && !ReferenceEquals(previousAnimation, this))
            {
                previousAnimation.Effect = null;
            }

            _otherEffects.Add(effect);
            effect.Animation = this;
            effect.OnAnimationChanged(null);
        }

        if (effects.Count > 0)
        {
            SetEffectCore(effects[0]);
        }
    }

    /// <summary>The steps of the constructor that run before the animation is created: the animator must be registered.</summary>
    private static AnimationTimeline? GetTimeline(string animatorName, IReadOnlyList<AnimationEffect> effects, AnimationTimeline? timeline)
    {
        ArgumentNullException.ThrowIfNull(animatorName);
        ArgumentNullException.ThrowIfNull(effects);
        if (Document.CurrentGlobalDocument.AnimationWorkletOrNull is not { } worklet || !worklet.HasValidDocumentAnimatorDefinition(animatorName))
            throw new TypeErrorException($"The animator '{animatorName}' is not registered in the animation worklet, or its registration is invalid");

        return timeline;
    }

    /// <summary>The animation worklet of the document the animation was created in.</summary>
    internal AnimationWorklet Worklet { get; }

    /// <summary>The serialized options, or null when the options are undefined.</summary>
    internal SerializedValue? SerializedOptions { get; }

    /// <summary>Whether the effect of the animation is a WorkletGroupEffect: the animation was created with a sequence of effects.</summary>
    internal bool HasGroupEffect { get; }

    /// <summary>The corresponding animator instance (https://drafts.css-houdini.org/css-animation-worklet-1/#corresponding-animator-instance), or null.</summary>
    internal AnimatorInstance? AnimatorInstance { get; set; }

    /// <summary>Whether the animator instance was removed and is not created again while the animation is not idle: its constructor or its state function threw.</summary>
    internal bool IsAnimatorDropped { get; set; }

    /// <summary>Gets the name of the animator of the animation (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-animatorname).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-animatorname"/>
    [IdlMember("animatorName", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimation-animatorname")]
    public string AnimatorName { get; }

    /// <summary>The effects whose local times the animator sets: the associated effect, and the other effects of a group.</summary>
    internal IEnumerable<AnimationEffect> GetEffects()
    {
        if (EffectValue is { } effect)
        {
            yield return effect;
        }

        foreach (var other in _otherEffects)
        {
            if (ReferenceEquals(other.Animation, this))
            {
                yield return other;
            }
        }
    }

    internal override void UpdateEffectRegistrations()
    {
        foreach (var effect in GetEffects())
        {
            (effect as KeyframeEffect)?.UpdateRegistration();
        }
    }

    /// <summary>The effects have no local time while the animation is idle. Returns whether an effect had one.</summary>
    internal bool ClearLocalTimes()
    {
        IsAnimatorDropped = false;
        var changed = false;
        foreach (var effect in GetEffects())
        {
            changed |= effect.WorkletLocalTime is not null;
            effect.WorkletLocalTime = null;
        }

        return changed;
    }

    // https://drafts.css-houdini.org/css-animation-worklet-1/#web-animation-overrides: "When a given worklet animation's play
    // state changes from finished, running or paused to idle, [...] disassociate animator instance of worklet animation"
    private protected override void OnCanceledByScript()
    {
        Worklet.RemoveAnimatorInstance(this);
        ClearLocalTimes();
    }

    // "When the procedure to set the timeline of an animation for a given workletAnimation is called, then set animator
    // instance of worklet animation": the instance is created again by the next frame
    private protected override void OnTimelineSetByScript() => Worklet.RemoveAnimatorInstance(this);
}

/// <summary>
/// An animator effect (https://drafts.css-houdini.org/css-animation-worklet-1/#animator-effect): the effect of a worklet
/// animation in the animation worklet. The <c>animate()</c> callback of the animator sets its local time, which is synced
/// to the effect after the callback.
/// </summary>
/// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#workletanimationeffect"/>
[IdlInterface("WorkletAnimationEffect", "https://drafts.css-houdini.org/css-animation-worklet-1/#workletanimationeffect")]
public sealed class WorkletAnimationEffect
{
    private readonly AnimatorInstance _instance;
    private double? _localTime;

    internal WorkletAnimationEffect(AnimationEffect effect, AnimatorInstance instance)
    {
        Effect = effect;
        _instance = instance;
        _localTime = effect.WorkletLocalTime;
    }

    /// <summary>The corresponding effect.</summary>
    internal AnimationEffect Effect { get; }

    /// <summary>Returns the specified timing properties of the corresponding effect (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-gettiming).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-gettiming"/>
    [IdlMember("getTiming", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-gettiming")]
    public EffectTiming GetTiming() => Effect.GetTiming();

    /// <summary>
    /// Returns the calculated timing properties of the corresponding effect at the local time of this animator effect
    /// (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-getcomputedtiming).
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-getcomputedtiming"/>
    [IdlMember("getComputedTiming", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-getcomputedtiming")]
    public ComputedEffectTiming GetComputedTiming() => Effect.GetComputedTiming(_localTime);

    /// <summary>
    /// Gets or sets the local time of the effect, in milliseconds, or null when it is unresolved
    /// (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-localtime).
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-localtime"/>
    [IdlMember("localTime", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletanimationeffect-localtime")]
    public double? LocalTime
    {
        get => _localTime;
        set
        {
            if (_localTime == value)
                return;

            _localTime = value;
            _instance.IsSyncRequested = true;
        }
    }

    /// <summary>Restores the local time of the corresponding effect.</summary>
    internal void ResetLocalTime() => _localTime = Effect.WorkletLocalTime;
}

/// <summary>
/// The effect of a worklet animation created with several effects (https://drafts.css-houdini.org/css-animation-worklet-1/#worklet-group-effect):
/// the animator sets the local times of the children individually.
/// </summary>
/// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#workletgroupeffect"/>
[IdlInterface("WorkletGroupEffect", "https://drafts.css-houdini.org/css-animation-worklet-1/#workletgroupeffect")]
public sealed class WorkletGroupEffect
{
    private readonly List<WorkletAnimationEffect> _children;

    internal WorkletGroupEffect(List<WorkletAnimationEffect> children) => _children = children;

    /// <summary>Returns the animator effects of the effects of the group (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletgroupeffect-getchildren).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletgroupeffect-getchildren"/>
    [IdlMember("getChildren", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-workletgroupeffect-getchildren")]
    public IReadOnlyList<WorkletAnimationEffect> GetChildren() => [.. _children];
}
