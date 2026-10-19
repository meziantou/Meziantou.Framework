using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The animation worklet of a document (https://drafts.css-houdini.org/css-animation-worklet-1/#animation-worklet-desc,
/// <c>CSS.animationWorklet</c>): its global scopes register the animators that drive the effects of the
/// <see cref="WorkletAnimation"/> objects of the document, and the document keeps what it needs to know about them (the
/// document animator definitions).
/// </summary>
/// <remarks>
/// The animators run when the animations of the document are updated for a frame (the rendering update, before the animation
/// frame callbacks): the <c>animate()</c> callback of the animator of each worklet animation whose current time may have
/// changed is invoked in the current global scope, and the local times it sets are synced to the effects at once (the global
/// scopes do not run in parallel). The animator instances are migrated to the other global scope every
/// <see cref="FramesPerGlobalScope"/> frames (https://drafts.css-houdini.org/css-animation-worklet-1/#migrating-animator),
/// so that the stateless animators cannot rely on their state.
/// </remarks>
internal sealed class AnimationWorklet : Worklet
{
    // The frames an animator instance runs in a global scope before the instances are migrated to the next global scope
    private const int FramesPerGlobalScope = 12;

    // The document animator definitions: the stateful flag by name, null for an invalid definition
    private readonly Dictionary<string, bool?> _documentAnimatorDefinitions = new(StringComparer.Ordinal);

    // The worklet animations that have a corresponding animator instance
    private readonly List<WorkletAnimation> _animations = [];
    private double _lastFrameTime = double.NaN;
    private int _frameCount;
    private int _globalScopeIndex;

    public AnimationWorklet(Document document)
        : base(document)
    {
    }

    // Fetch has no destination for the animation worklets (https://fetch.spec.whatwg.org/#concept-request-destination):
    // their modules are fetched as scripts
    internal override RequestDestination Destination => RequestDestination.Script;

    // The stateless animators must not rely on the state of a global scope: the instances are moved between two global scopes
    private protected override int GlobalScopeCount => 2;

    private protected override WorkletGlobalScope CreateGlobalScopeCore() => new AnimationWorkletGlobalScope(this);

    private protected override void OnTerminated()
    {
        _documentAnimatorDefinitions.Clear();
        foreach (var animation in _animations)
        {
            animation.AnimatorInstance = null;
        }

        _animations.Clear();
    }

    /// <summary>The global scope that runs the animators.</summary>
    private AnimationWorkletGlobalScope? CurrentGlobalScope
        => IsTerminated || GlobalScopes.Count == 0 ? null : (AnimationWorkletGlobalScope)GlobalScopes[_globalScopeIndex % GlobalScopes.Count];

    /// <summary>
    /// The task queued by <c>registerAnimator()</c> (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-animationworkletglobalscope-registeranimator):
    /// the definition becomes invalid when the global scopes do not register classes with the same stateful flag.
    /// </summary>
    public void RegisterDocumentAnimatorDefinition(string name, bool isStateful)
    {
        if (_documentAnimatorDefinitions.TryGetValue(name, out var existing))
        {
            if (existing is null || existing == isStateful)
                return;

            _documentAnimatorDefinitions[name] = null;
            Document.RelevantWindow?.ReportConsoleMessage(new ConsoleMessage("error", ConsoleMessageLevel.Error, $"The animator '{name}' was registered with and without a state function in the global scopes of the animation worklet."));
            return;
        }

        _documentAnimatorDefinitions[name] = isStateful;
    }

    /// <summary>Whether the document has a valid animator definition of a name.</summary>
    public bool HasValidDocumentAnimatorDefinition(string name) => _documentAnimatorDefinitions.TryGetValue(name, out var definition) && definition is not null;

    /// <summary>
    /// Updates the animator instance of a worklet animation when the animations of its timeline are updated for a frame: the
    /// instance is created or removed with the play state of the animation
    /// (https://drafts.css-houdini.org/css-animation-worklet-1/#web-animation-overrides), the instances are migrated when their
    /// global scope changes, and the animator runs (https://drafts.css-houdini.org/css-animation-worklet-1/#running-animators).
    /// </summary>
    /// <param name="animation">The worklet animation.</param>
    /// <param name="frameTime">The time of the frame.</param>
    /// <returns>Whether the local time of an effect changed: the styles of the targets are computed again.</returns>
    public bool UpdateAnimator(WorkletAnimation animation, double frameTime)
    {
        // The frames are counted when an animator runs: the instances are migrated every few frames
        if (frameTime != _lastFrameTime)
        {
            _lastFrameTime = frameTime;
            if (_animations.Count > 0 && ++_frameCount >= FramesPerGlobalScope && GlobalScopes.Count > 1)
            {
                _frameCount = 0;
                MigrateAnimatorInstances();
            }
        }

        var isIdle = animation.PlayStateValue == AnimationPlayState.Idle || animation.ReplaceState != AnimationReplaceState.Active;
        if (isIdle)
        {
            // "disassociate animator instance of worklet animation"
            var changed = animation.ClearLocalTimes();
            if (animation.AnimatorInstance is not null)
            {
                animation.AnimatorInstance = null;
                _animations.Remove(animation);
            }

            return changed;
        }

        // "When the procedure to set the target effect of an animation for a given worklet animation is called, then set
        // animator instance of worklet animation": the instance is created again for the new effects
        if (animation.AnimatorInstance is { } current && !current.HasEffectsOf(animation))
        {
            RemoveAnimatorInstance(animation);
        }

        if (animation.AnimatorInstance is null && !animation.IsAnimatorDropped)
        {
            // "associate animator instance of worklet animation"
            if (CurrentGlobalScope is not { } globalScope)
                return false;

            animation.AnimatorInstance = CreateAnimatorInstance(globalScope, animation, state: null);
            if (animation.AnimatorInstance is null)
                return false;

            _animations.Add(animation);
        }

        return animation.AnimatorInstance is { } instance && RunAnimator(instance);
    }

    /// <summary>Removes the animator instance of a worklet animation that becomes idle, or whose effects or timeline change (the instance is created again by the next frame).</summary>
    public void RemoveAnimatorInstance(WorkletAnimation animation)
    {
        if (animation.AnimatorInstance is null)
            return;

        animation.AnimatorInstance = null;
        _animations.Remove(animation);
    }

    /// <summary>https://drafts.css-houdini.org/css-animation-worklet-1/#create-a-new-animator-instance</summary>
    private static AnimatorInstance? CreateAnimatorInstance(AnimationWorkletGlobalScope globalScope, WorkletAnimation animation, SerializedValue? state)
    {
        if (!globalScope.CanRunScript || !globalScope.AnimatorDefinitions.TryGetValue(animation.AnimatorName, out var definition) || globalScope.ScriptEngine is not { } engine)
            return null;

        try
        {
            // The options and the state are undefined when they were not given (the state of a stateless animator)
            var options = animation.SerializedOptions is { } serializedOptions ? engine.StructuredDeserialize(globalScope, serializedOptions.Value) : UndefinedValue.Instance;
            var stateValue = state is not null ? engine.StructuredDeserialize(globalScope, state.Value) : UndefinedValue.Instance;
            var animator = definition.ClassConstructor.Construct([options, stateValue]);
            return new AnimatorInstance(globalScope, definition, animator, animation);
        }
#pragma warning disable CA1031 // An exception of the constructor is reported: the animation has no animator instance
        catch (Exception ex)
#pragma warning restore CA1031
        {
            definition.ClassConstructor.ReportException(ex);
            animation.IsAnimatorDropped = true;
            return null;
        }
    }

    /// <summary>
    /// Runs the animator of an instance (https://drafts.css-houdini.org/css-animation-worklet-1/#run-animators), then syncs the
    /// local times it set to the effects (https://drafts.css-houdini.org/css-animation-worklet-1/#sync-local-times-to-document).
    /// </summary>
    private static bool RunAnimator(AnimatorInstance instance)
    {
        if (!instance.GlobalScope.CanRunScript)
            return false;

        // "sync animation timings to worklet": the animator current time is the current time of the animation; the timing
        // properties of the animator effects are the ones of their effects
        var animation = instance.Animation;
        try
        {
            using (Html.AuthorCodeScope.Enter())
            {
                instance.Definition.ClassConstructor.Invoke(instance.Definition.AnimateFunction, instance.Animator, [animation.CurrentTimeValue, instance.Effect]);
            }
        }
#pragma warning disable CA1031 // An exception of the animate function is reported: the local times it set are ignored
        catch (Exception ex)
#pragma warning restore CA1031
        {
            instance.Definition.ClassConstructor.ReportException(ex);
            instance.RevertLocalTimes();
            return false;
        }

        return instance.SyncLocalTimesToDocument();
    }

    /// <summary>
    /// https://drafts.css-houdini.org/css-animation-worklet-1/#migrating-animator: the animator instances are created again in
    /// the next global scope, with the state that the <c>state()</c> function of a stateful animator returns. An instance
    /// whose state function throws, or returns a value that cannot be serialized, is removed and not created again.
    /// </summary>
    private void MigrateAnimatorInstances()
    {
        _globalScopeIndex = (_globalScopeIndex + 1) % GlobalScopes.Count;
        var destination = CurrentGlobalScope;
        foreach (var animation in _animations.ToArray())
        {
            if (animation.AnimatorInstance is not { } instance)
                continue;

            var source = instance.GlobalScope;
            var definition = instance.Definition;
            SerializedValue? serializedState = null;
            var dropped = false;
            if (definition.StateFunction is { } stateFunction && source.CanRunScript && source.ScriptEngine is { } engine)
            {
                try
                {
                    var state = definition.ClassConstructor.Invoke(stateFunction, instance.Animator, []);
                    serializedState = definition.ClassConstructor.IsUndefined(state) ? null : new SerializedValue(engine.StructuredSerialize(source, state, []));
                }
#pragma warning disable CA1031 // "If any exception is thrown, [...] abort the following steps": the instance is removed
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    definition.ClassConstructor.ReportException(ex);
                    dropped = true;
                }
            }

            animation.AnimatorInstance = null;
            if (!dropped && destination is not null)
            {
                animation.AnimatorInstance = CreateAnimatorInstance(destination, animation, serializedState);
            }

            if (animation.AnimatorInstance is null)
            {
                animation.IsAnimatorDropped = true;
                _animations.Remove(animation);
            }
        }
    }
}

/// <summary>
/// The global object of an animation worklet (https://drafts.css-houdini.org/css-animation-worklet-1/#animationworkletglobalscope):
/// the scripts added to <c>CSS.animationWorklet</c> register the animators of the <see cref="WorkletAnimation"/> objects.
/// </summary>
/// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#animationworkletglobalscope"/>
[IdlInterface("AnimationWorkletGlobalScope", "https://drafts.css-houdini.org/css-animation-worklet-1/#animationworkletglobalscope")]
public sealed class AnimationWorkletGlobalScope : WorkletGlobalScope
{
    internal AnimationWorkletGlobalScope(AnimationWorklet worklet)
        : base(worklet)
    {
        AnimationWorklet = worklet;
    }

    internal AnimationWorklet AnimationWorklet { get; }

    internal override ExposureSet Exposure => ExposureSet.AnimationWorklet;

    internal override string InterfaceName => "AnimationWorkletGlobalScope";

    /// <summary>The animator definitions of the global scope (https://drafts.css-houdini.org/css-animation-worklet-1/#animator-definitions), by name.</summary>
    internal Dictionary<string, AnimatorDefinition> AnimatorDefinitions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a class whose instances drive the effects of the worklet animations of the name
    /// (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-animationworkletglobalscope-registeranimator).
    /// </summary>
    /// <param name="name">The name of the animator: an identifier.</param>
    /// <param name="animatorCtor">The class: its prototype has the <c>animate</c> callback, and a <c>state</c> function when the animator is stateful.</param>
    /// <exception cref="TypeErrorException">The name is not an identifier, the value is not a constructor, or its prototype has no <c>animate</c> function.</exception>
    /// <exception cref="DomException">A <c>NotSupportedError</c> when the name is already registered in the global scope.</exception>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-animationworkletglobalscope-registeranimator"/>
    [IdlMember("registerAnimator", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-animationworkletglobalscope-registeranimator")]
    public void RegisterAnimator(string name, WorkletClassConstructor animatorCtor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(animatorCtor);
        if (CssParser.ParseListOfComponentValues(name) is not [CssPreservedToken { Type: CssTokenType.Ident }])
            throw new TypeErrorException($"'{name}' is not a valid identifier");

        if (AnimatorDefinitions.ContainsKey(name))
            throw new DomException($"The animator '{name}' is already registered", "NotSupportedError");

        if (!animatorCtor.IsConstructor)
            throw new TypeErrorException("The animator class is not a constructor");

        if (!animatorCtor.HasPrototypeObject)
            throw new TypeErrorException("The prototype of the animator class is not an object");

        var animate = animatorCtor.GetPrototypeFunction("animate");

        // "If an exception is thrown, set stateful to be false": an animator is stateful when its class has a state function
        object? state;
        try
        {
            state = animatorCtor.GetPrototypeFunction("state");
        }
#pragma warning disable CA1031 // The conversion fails when the prototype has no state function
        catch (Exception)
#pragma warning restore CA1031
        {
            state = null;
        }

        AnimatorDefinitions[name] = new AnimatorDefinition(animatorCtor, animate, state);

        // The document animator definition is registered by a task
        var isStateful = state is not null;
        QueueGlobalTask(() => AnimationWorklet.RegisterDocumentAnimatorDefinition(name, isStateful));
    }
}

/// <summary>An animator definition (https://drafts.css-houdini.org/css-animation-worklet-1/#animator-definition): a class registered in a global scope.</summary>
internal sealed class AnimatorDefinition(WorkletClassConstructor classConstructor, object animateFunction, object? stateFunction)
{
    public WorkletClassConstructor ClassConstructor { get; } = classConstructor;

    public object AnimateFunction { get; } = animateFunction;

    /// <summary>The state function of a stateful animator definition, else null.</summary>
    public object? StateFunction { get; } = stateFunction;
}

/// <summary>
/// An animator instance (https://drafts.css-houdini.org/css-animation-worklet-1/#animator-instance): the instance of an
/// animator class that drives the effects of a worklet animation, in a global scope.
/// </summary>
internal sealed class AnimatorInstance
{
    private readonly List<WorkletAnimationEffect> _effects = [];

    public AnimatorInstance(AnimationWorkletGlobalScope globalScope, AnimatorDefinition definition, object animator, WorkletAnimation animation)
    {
        GlobalScope = globalScope;
        Definition = definition;
        Animator = animator;
        Animation = animation;

        // "Let animatorEffect be the result of constructing a WorkletAnimationEffect with its corresponding effect being effect":
        // the animator effects start with the local times of their effects (they are kept when the instance is migrated)
        foreach (var effect in animation.GetEffects())
        {
            _effects.Add(new WorkletAnimationEffect(effect, this));
        }

        Effect = animation.HasGroupEffect ? new WorkletGroupEffect(_effects) : _effects.Count > 0 ? _effects[0] : null;
    }

    public AnimationWorkletGlobalScope GlobalScope { get; }

    public AnimatorDefinition Definition { get; }

    /// <summary>The instance of the animator class (a value of the script engine).</summary>
    public object Animator { get; }

    public WorkletAnimation Animation { get; }

    /// <summary>The animator effect given to the animate function: a <see cref="WorkletAnimationEffect"/>, a <see cref="WorkletGroupEffect"/>, or null for an animation without effect.</summary>
    public object? Effect { get; }

    /// <summary>The sync requested flag: an animator effect has a new local time.</summary>
    public bool IsSyncRequested { get; set; }

    /// <summary>Whether the animator effects are the ones of the current effects of the animation.</summary>
    public bool HasEffectsOf(WorkletAnimation animation)
    {
        var index = 0;
        foreach (var effect in animation.GetEffects())
        {
            if (index >= _effects.Count || !ReferenceEquals(_effects[index].Effect, effect))
                return false;

            index++;
        }

        return index == _effects.Count;
    }

    /// <summary>
    /// https://drafts.css-houdini.org/css-animation-worklet-1/#sync-local-times-to-document: sets the local times of the effects
    /// to the ones of the animator effects. Returns whether a local time changed.
    /// </summary>
    public bool SyncLocalTimesToDocument()
    {
        if (!IsSyncRequested)
            return false;

        IsSyncRequested = false;
        var changed = false;
        foreach (var effect in _effects)
        {
            if (effect.Effect.WorkletLocalTime != effect.LocalTime)
            {
                effect.Effect.WorkletLocalTime = effect.LocalTime;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Restores the local times of the animator effects after an animate function that threw.</summary>
    public void RevertLocalTimes()
    {
        IsSyncRequested = false;
        foreach (var effect in _effects)
        {
            effect.ResetLocalTime();
        }
    }
}

/// <summary>A value serialized by the structured serialization (the serialized options of a worklet animation, the state of a stateful animator): absent for the value undefined.</summary>
internal sealed record SerializedValue(object? Value);
