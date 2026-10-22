namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>[[PromiseState]]</summary>
internal enum PromiseState : byte
{
    Pending,
    Fulfilled,
    Rejected,
}

/// <summary>A .NET continuation of a promise reaction (used by <c>await</c>: no function object is created).</summary>
internal delegate void PromiseContinuation(object? state, JsValue value);

/// <summary>
/// A Promise instance (https://tc39.es/ecma262/#sec-promise-objects) and the promise abstract operations
/// (https://tc39.es/ecma262/#sec-promise-abstract-operations).
/// </summary>
internal sealed class JsPromise : JsObject
{
    private static readonly PropertyKey ThenKey = new("then");
    private static readonly JsJobAction FulfillReactionJobAction = static (state, argument) => RunReactionJob((PromiseReaction)state!, argument, fulfill: true);
    private static readonly JsJobAction RejectReactionJobAction = static (state, argument) => RunReactionJob((PromiseReaction)state!, argument, fulfill: false);
    private static readonly JsJobAction ResolveThenableJobAction = RunResolveThenableJob;

    // [[PromiseFulfillReactions]] and [[PromiseRejectReactions]]: the two lists always have the same length, so a reaction holds both handlers
    private PromiseReaction? _firstReaction;
    private List<PromiseReaction>? _otherReactions;

    public JsPromise(JsObject prototype)
        : base(prototype, ObjectClass.Promise)
    {
    }

    /// <summary>[[PromiseState]]</summary>
    public PromiseState State { get; private set; }

    /// <summary>[[PromiseResult]] (undefined while the promise is pending)</summary>
    public JsValue Result { get; private set; }

    /// <summary>[[PromiseIsHandled]]</summary>
    public bool IsHandled { get; set; }

    /// <summary>Creates a pending promise whose prototype is %Promise.prototype% of the realm.</summary>
    public static JsPromise Create(JsRealm realm) => new(realm.Intrinsics.PromisePrototype);

    /// <summary>FulfillPromise(promise, value)</summary>
    public void Fulfill(JsValue value)
    {
        var (first, others) = TakeReactions();
        Result = value;
        State = PromiseState.Fulfilled;
        TriggerPromiseReactions(first, others, value, fulfill: true);
    }

    /// <summary>RejectPromise(promise, reason)</summary>
    public void Reject(JsValue reason)
    {
        var (first, others) = TakeReactions();
        Result = reason;
        State = PromiseState.Rejected;
        if (!IsHandled)
        {
            JsEngine.Current!.TrackPromiseRejection(this, JsPromiseRejectionOperation.Reject);
        }

        TriggerPromiseReactions(first, others, reason, fulfill: false);
    }

    /// <summary>The steps of a promise resolve function (https://tc39.es/ecma262/#sec-promise-resolve-functions) once the "already resolved" check passed.</summary>
    internal void ResolveWith(JsValue resolution, JsRealm realm)
    {
        if (ReferenceEquals(resolution.Cell, this))
        {
            Reject(realm.CreateError(ErrorType.TypeError, "Chaining cycle detected for promise"));
            return;
        }

        if (resolution.Cell is not JsObject resolutionObject)
        {
            Fulfill(resolution);
            return;
        }

        JsValue then;
        try
        {
            then = resolutionObject.Get(ThenKey);
        }
        catch (JsException exception)
        {
            Reject(exception.Value);
            return;
        }

        if (then.Cell is not JsObject { IsCallable: true } thenAction)
        {
            Fulfill(resolution);
            return;
        }

        // NewPromiseResolveThenableJob(promise, resolution, thenJobCallback)
        var engine = JsEngine.Current!;
        var thenJobCallback = engine.MakeJobCallback(thenAction);
        engine.EnqueuePromiseJob(ResolveThenableJobAction, new ResolveThenableJobState(this, thenJobCallback), resolution, GetHandlerRealm(engine, thenAction));
    }

    /// <summary>PerformPromiseThen(promise, onFulfilled, onRejected, resultCapability)</summary>
    public static JsValue PerformPromiseThen(JsPromise promise, JsValue onFulfilled, JsValue onRejected, PromiseCapability? resultCapability)
    {
        var engine = JsEngine.Current!;
        var reaction = new PromiseReaction
        {
            Capability = resultCapability,
            OnFulfilled = onFulfilled.Cell is JsObject { IsCallable: true } fulfilledCallback ? engine.MakeJobCallback(fulfilledCallback) : null,
            OnRejected = onRejected.Cell is JsObject { IsCallable: true } rejectedCallback ? engine.MakeJobCallback(rejectedCallback) : null,
        };

        promise.AddReaction(engine, reaction);
        return resultCapability is null ? JsValue.Undefined : resultCapability.Promise;
    }

    /// <summary>
    /// PerformPromiseThen(promise, onFulfilled, onRejected) without a result capability, whose handlers are .NET
    /// continuations: the fast path of <c>await</c>. The continuation runs in a promise job; an exception it throws is
    /// reported as the exception of the job. The handlers of the specification are built-in functions, so the host state is
    /// captured here (HostMakeJobCallback) and restored when the continuation runs (HostCallJobCallback).
    /// </summary>
    public static void PerformPromiseThen(JsPromise promise, PromiseContinuation onFulfilled, PromiseContinuation onRejected, object? state)
    {
        var engine = JsEngine.Current!;
        var reaction = new PromiseReaction
        {
            NativeOnFulfilled = onFulfilled,
            NativeOnRejected = onRejected,
            NativeState = state,
            NativeHostDefined = engine.MakeContinuationHostDefined(),
        };

        promise.AddReaction(engine, reaction);
    }

    /// <summary>PromiseResolve(%Promise%, value) for the %Promise% of the realm: the promise of <c>await</c>.</summary>
    public static JsPromise PromiseResolve(JsRealm realm, JsValue value)
    {
        var constructor = realm.Intrinsics.Promise;
        if (value.Cell is JsPromise promise)
        {
            var valueConstructor = promise.Get(new PropertyKey("constructor"));
            if (ReferenceEquals(valueConstructor.Cell, constructor))
                return promise;
        }

        var capability = PromiseCapability.CreateIntrinsic(realm);
        capability.Resolve(value);
        return (JsPromise)capability.Promise;
    }

    /// <summary>PromiseResolve(C, x)</summary>
    public static JsObject PromiseResolve(JsObject constructor, JsValue value)
    {
        if (value.Cell is JsPromise promise)
        {
            var valueConstructor = promise.Get(new PropertyKey("constructor"));
            if (ReferenceEquals(valueConstructor.Cell, constructor))
                return promise;
        }

        var capability = PromiseCapability.Create(constructor);
        capability.Resolve(value);
        return capability.Promise;
    }

    private void AddReaction(JsEngine engine, PromiseReaction reaction)
    {
        switch (State)
        {
            case PromiseState.Pending:
                if (_firstReaction is null)
                {
                    _firstReaction = reaction;
                }
                else
                {
                    (_otherReactions ??= []).Add(reaction);
                }

                break;

            case PromiseState.Fulfilled:
                EnqueueReactionJob(engine, reaction, Result, fulfill: true);
                break;

            default:
                if (!IsHandled)
                {
                    engine.TrackPromiseRejection(this, JsPromiseRejectionOperation.Handle);
                }

                EnqueueReactionJob(engine, reaction, Result, fulfill: false);
                break;
        }

        IsHandled = true;
    }

    private (PromiseReaction? First, List<PromiseReaction>? Others) TakeReactions()
    {
        var result = (_firstReaction, _otherReactions);
        _firstReaction = null;
        _otherReactions = null;
        return result;
    }

    /// <summary>TriggerPromiseReactions(reactions, argument)</summary>
    private static void TriggerPromiseReactions(PromiseReaction? first, List<PromiseReaction>? others, JsValue argument, bool fulfill)
    {
        if (first is null)
            return;

        var engine = JsEngine.Current!;
        EnqueueReactionJob(engine, first, argument, fulfill);
        if (others is not null)
        {
            foreach (var reaction in others)
            {
                EnqueueReactionJob(engine, reaction, argument, fulfill);
            }
        }
    }

    /// <summary>NewPromiseReactionJob(reaction, argument) + HostEnqueuePromiseJob</summary>
    private static void EnqueueReactionJob(JsEngine engine, PromiseReaction reaction, JsValue argument, bool fulfill)
    {
        var handler = fulfill ? reaction.OnFulfilled : reaction.OnRejected;
        var realm = handler is { } callback ? GetHandlerRealm(engine, callback.Callback) : null;
        engine.EnqueuePromiseJob(fulfill ? FulfillReactionJobAction : RejectReactionJobAction, reaction, argument, realm);
    }

    /// <summary>The realm of a handler job: GetFunctionRealm(handler), or the current realm when it throws (a revoked proxy).</summary>
    private static JsRealm? GetHandlerRealm(JsEngine engine, JsObject callback)
    {
        try
        {
            return ObjectOperations.GetFunctionRealm(callback);
        }
        catch (JsException)
        {
            return engine.CurrentRealm;
        }
    }

    /// <summary>The steps of the job of NewPromiseReactionJob.</summary>
    private static void RunReactionJob(PromiseReaction reaction, JsValue argument, bool fulfill)
    {
        if (reaction.NativeOnFulfilled is not null)
        {
            var continuation = fulfill ? reaction.NativeOnFulfilled : reaction.NativeOnRejected!;
            if (reaction.NativeHostDefined is { } hostDefined)
            {
                var engine = JsEngine.Current!;
                engine.HostHooks.CallContinuation(engine, hostDefined, new JsContinuation(continuation, reaction.NativeState, argument));
            }
            else
            {
                continuation(reaction.NativeState, argument);
            }

            return;
        }

        var capability = reaction.Capability;
        var handler = fulfill ? reaction.OnFulfilled : reaction.OnRejected;
        JsValue handlerResult;
        var isAbrupt = false;
        if (handler is not { } callback)
        {
            handlerResult = argument;
            isAbrupt = !fulfill;
        }
        else
        {
            try
            {
                handlerResult = JsEngine.Current!.CallJobCallback(callback, JsValue.Undefined, [argument]);
            }
            catch (JsException exception) when (capability is not null)
            {
                handlerResult = exception.Value;
                isAbrupt = true;
            }
        }

        if (capability is null)
            return;

        if (isAbrupt)
        {
            capability.Reject(handlerResult);
        }
        else
        {
            capability.Resolve(handlerResult);
        }
    }

    /// <summary>The steps of the job of NewPromiseResolveThenableJob.</summary>
    private static void RunResolveThenableJob(object? state, JsValue thenable)
    {
        var jobState = (ResolveThenableJobState)state!;
        var engine = JsEngine.Current!;
        var resolvingFunctions = new PromiseResolvingFunctions(jobState.Promise, engine.CurrentRealm!);
        var resolve = resolvingFunctions.ResolveFunction;
        var reject = resolvingFunctions.RejectFunction;
        try
        {
            engine.CallJobCallback(jobState.Then, thenable, [resolve, reject]);
        }
        catch (JsException exception)
        {
            reject.Call(JsValue.Undefined, [exception.Value]);
        }
    }

    private sealed class ResolveThenableJobState(JsPromise promise, JsJobCallback then)
    {
        public JsPromise Promise { get; } = promise;

        public JsJobCallback Then { get; } = then;
    }
}

/// <summary>A PromiseReaction Record (https://tc39.es/ecma262/#sec-promisereaction-records) for the two types: fulfill and reject.</summary>
internal sealed class PromiseReaction
{
    /// <summary>[[Capability]]: null for the reactions created by the engine (await...).</summary>
    public PromiseCapability? Capability { get; init; }

    /// <summary>[[Handler]] of the fulfill reaction (null: empty).</summary>
    public JsJobCallback? OnFulfilled { get; init; }

    /// <summary>[[Handler]] of the reject reaction (null: empty).</summary>
    public JsJobCallback? OnRejected { get; init; }

    public PromiseContinuation? NativeOnFulfilled { get; init; }

    public PromiseContinuation? NativeOnRejected { get; init; }

    public object? NativeState { get; init; }

    /// <summary>The [[HostDefined]] value of the .NET continuations (<see cref="JsHost.MakeContinuationHostDefined"/>), or null.</summary>
    public object? NativeHostDefined { get; init; }
}

/// <summary>
/// The resolving functions of a promise (CreateResolvingFunctions): the shared "promise or empty" record, and the
/// resolve and reject function objects, created when they are first needed.
/// </summary>
internal sealed class PromiseResolvingFunctions(JsPromise promise, JsRealm realm)
{
    private static readonly BuiltinFunctionCallback ResolveCallback = static (function, _, arguments, _) =>
    {
        ((PromiseResolvingFunctions)function.State!).Resolve(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.Undefined;
    };

    private static readonly BuiltinFunctionCallback RejectCallback = static (function, _, arguments, _) =>
    {
        ((PromiseResolvingFunctions)function.State!).Reject(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.Undefined;
    };

    private JsPromise? _promise = promise;
    private JsBuiltinFunction? _resolve;
    private JsBuiltinFunction? _reject;

    /// <summary>Gets the realm of the functions (the current realm when CreateResolvingFunctions was called).</summary>
    public JsRealm Realm { get; } = realm;

    public JsBuiltinFunction ResolveFunction => _resolve ??= CreateFunction(ResolveCallback);

    public JsBuiltinFunction RejectFunction => _reject ??= CreateFunction(RejectCallback);

    /// <summary>The steps of the resolve function.</summary>
    public void Resolve(JsValue resolution)
    {
        var target = _promise;
        if (target is null)
            return;

        _promise = null;
        target.ResolveWith(resolution, Realm);
    }

    /// <summary>The steps of the reject function.</summary>
    public void Reject(JsValue reason)
    {
        var target = _promise;
        if (target is null)
            return;

        _promise = null;
        target.Reject(reason);
    }

    private JsBuiltinFunction CreateFunction(BuiltinFunctionCallback callback)
    {
        var function = Realm.CreateBuiltinFunction("", 1, callback);
        function.State = this;
        return function;
    }
}

/// <summary>
/// A PromiseCapability Record (https://tc39.es/ecma262/#sec-promisecapability-records). The capabilities of %Promise%
/// do not call the Promise constructor (it is not observable): their resolving functions are created on demand.
/// </summary>
internal sealed class PromiseCapability
{
    private static readonly PropertyKey ResolveKey = new("resolve");

    private readonly PromiseResolvingFunctions? _resolvingFunctions;
    private readonly JsValue _resolve;
    private readonly JsValue _reject;

    private PromiseCapability(JsObject promise, JsValue resolve, JsValue reject)
    {
        Promise = promise;
        _resolve = resolve;
        _reject = reject;
    }

    private PromiseCapability(JsPromise promise, PromiseResolvingFunctions resolvingFunctions)
    {
        Promise = promise;
        _resolvingFunctions = resolvingFunctions;
    }

    /// <summary>[[Promise]]</summary>
    public JsObject Promise { get; }

    /// <summary>[[Resolve]]</summary>
    public JsValue ResolveFunction => _resolvingFunctions is { } functions ? functions.ResolveFunction : _resolve;

    /// <summary>[[Reject]]</summary>
    public JsValue RejectFunction => _resolvingFunctions is { } functions ? functions.RejectFunction : _reject;

    /// <summary>NewPromiseCapability(%Promise%) for the %Promise% of the realm.</summary>
    public static PromiseCapability CreateIntrinsic(JsRealm realm)
    {
        var promise = JsPromise.Create(realm);
        return new PromiseCapability(promise, new PromiseResolvingFunctions(promise, realm));
    }

    /// <summary>NewPromiseCapability(C)</summary>
    public static PromiseCapability Create(JsValue constructor)
    {
        if (constructor.Cell is not JsObject { IsConstructor: true } constructorObject)
            return ThrowHelper.ThrowTypeError<PromiseCapability>($"{JsOperations.DescribeForError(constructor)} is not a constructor");

        if (constructorObject is JsFunction function && ReferenceEquals(function, function.Realm.Intrinsics.Promise))
            return CreateIntrinsic(function.Realm);

        // GetCapabilitiesExecutor: created in the current realm
        var record = new ExecutorRecord();
        var executor = JsEngine.Current!.CurrentRealm!.CreateBuiltinFunction("", 2, ExecutorCallback);
        executor.State = record;
        var promise = JsOperations.Construct(constructorObject, [executor]);
        if (!record.Resolve.IsCallable)
            return ThrowHelper.ThrowTypeError<PromiseCapability>("Promise resolve function is not callable");

        if (!record.Reject.IsCallable)
            return ThrowHelper.ThrowTypeError<PromiseCapability>("Promise reject function is not callable");

        return new PromiseCapability(promise.AsObject(), record.Resolve, record.Reject);
    }

    /// <summary>Call(capability.[[Resolve]], undefined, [value])</summary>
    public void Resolve(JsValue value)
    {
        if (_resolvingFunctions is { } functions)
        {
            functions.Resolve(value);
        }
        else
        {
            JsOperations.Call(_resolve, JsValue.Undefined, [value]);
        }
    }

    /// <summary>Call(capability.[[Reject]], undefined, [reason])</summary>
    public void Reject(JsValue reason)
    {
        if (_resolvingFunctions is { } functions)
        {
            functions.Reject(reason);
        }
        else
        {
            JsOperations.Call(_reject, JsValue.Undefined, [reason]);
        }
    }

    /// <summary>GetPromiseResolve(promiseConstructor)</summary>
    public static JsValue GetPromiseResolve(JsObject constructor)
    {
        var promiseResolve = constructor.Get(ResolveKey);
        if (!promiseResolve.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise resolve is not a function");

        return promiseResolve;
    }

    /// <summary>The steps of GetCapabilitiesExecutor.</summary>
    private static JsValue ExecutorCallback(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var record = (ExecutorRecord)function.State!;
        if (!record.Resolve.IsUndefined)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise executor has already been invoked with non-undefined arguments");

        if (!record.Reject.IsUndefined)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise executor has already been invoked with non-undefined arguments");

        record.Resolve = arguments.Length > 0 ? arguments[0] : default;
        record.Reject = arguments.Length > 1 ? arguments[1] : default;
        return JsValue.Undefined;
    }

    private sealed class ExecutorRecord
    {
        public JsValue Resolve { get; set; }

        public JsValue Reject { get; set; }
    }
}
