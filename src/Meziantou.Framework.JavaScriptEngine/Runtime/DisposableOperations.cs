namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The hint of a disposable resource: sync-dispose (<c>using</c>, DisposableStack) or async-dispose (<c>await using</c>, AsyncDisposableStack).</summary>
internal enum DisposeHint : byte
{
    SyncDispose,
    AsyncDispose,
}

/// <summary>How the dispose method of a resource is called.</summary>
internal enum DisposeMethodKind : byte
{
    /// <summary>Call(method, value): the [Symbol.dispose] / [Symbol.asyncDispose] method of the value, or a function given to <c>defer</c>.</summary>
    Method,

    /// <summary>
    /// The closure created by GetDisposeMethod for an async-dispose resource that has only a [Symbol.dispose] method: the
    /// method is called, and the result is a promise of %Promise% (rejected when the method throws, resolved with undefined
    /// otherwise; the value returned by the method is not awaited). The closure is not observable, so no function is created.
    /// </summary>
    SyncMethodForAsyncDispose,

    /// <summary>The closure created by <c>adopt(value, onDispose)</c>: onDispose(undefined, value). The closure is not observable.</summary>
    Adopted,
}

/// <summary>A DisposableResource Record (https://tc39.es/ecma262/#sec-disposableresource-records).</summary>
/// <param name="Value">[[ResourceValue]]: an object, or undefined.</param>
/// <param name="Hint">[[Hint]]</param>
/// <param name="Method">[[DisposeMethod]]: a callable object, or null (undefined).</param>
/// <param name="Kind">How <paramref name="Method"/> is called.</param>
internal readonly record struct DisposableResource(JsValue Value, DisposeHint Hint, JsObject? Method, DisposeMethodKind Kind)
{
    /// <summary>Calls the dispose method of the resource (Call(method, value)) and returns its result.</summary>
    public JsValue Invoke(JsRealm realm)
    {
        var method = Method!;
        switch (Kind)
        {
            case DisposeMethodKind.Adopted:
                return method.Call(JsValue.Undefined, [Value]);

            case DisposeMethodKind.SyncMethodForAsyncDispose:
                var capability = PromiseCapability.CreateIntrinsic(realm);
                try
                {
                    method.Call(Value, []);
                }
                catch (JsException exception)
                {
                    capability.Reject(exception.Value);
                    return capability.Promise;
                }

                capability.Resolve(JsValue.Undefined);
                return capability.Promise;

            default:
                return method.Call(Value, []);
        }
    }
}

/// <summary>A DisposeCapability Record (https://tc39.es/ecma262/#sec-disposecapability-records): the stack of the resources of a scope or of a DisposableStack.</summary>
internal sealed class DisposeCapability
{
    private List<DisposableResource>? _stack;

    /// <summary>Gets the number of resources of [[DisposableResourceStack]].</summary>
    public int Count => _stack?.Count ?? 0;

    /// <summary>Gets a resource of [[DisposableResourceStack]] (0 is the first resource added).</summary>
    public DisposableResource this[int index] => _stack![index];

    /// <summary>Appends a resource to [[DisposableResourceStack]].</summary>
    public void Add(in DisposableResource resource) => (_stack ??= []).Add(resource);

    /// <summary>Sets [[DisposableResourceStack]] to a new empty List.</summary>
    public void Clear() => _stack = null;
}

/// <summary>
/// The abstract operations of the explicit resource management (
/// https://tc39.es/proposal-explicit-resource-management/), shared by DisposableStack, AsyncDisposableStack and the
/// <c>using</c> / <c>await using</c> declarations.
/// </summary>
/// <remarks>
/// <para>
/// A <c>using</c> declaration: create a capability with <see cref="NewDisposeCapability"/> when entering the scope, call
/// <see cref="AddDisposableResource(DisposeCapability, JsValue, DisposeHint)"/> for each initialized binding, and
/// <see cref="DisposeResources"/> when leaving the scope with the completion of the block (a thrown value, or null for a
/// normal, return, break or continue completion). DisposeResources returns the thrown value of the resulting completion,
/// or null when the original completion stands.
/// </para>
/// <para>
/// An <c>await using</c> declaration: the interpreter drives an <see cref="AsyncDisposeResourcesOperation"/>: while
/// <see cref="AsyncDisposeResourcesOperation.MoveNext(out JsValue)"/> returns true, it performs Await(value) as the
/// <c>await</c> operator does (a synchronous exception of PromiseResolve is a throw completion of the Await), then calls
/// <see cref="AsyncDisposeResourcesOperation.SetAwaitResult(JsValue?)"/> with the result (null when the promise is
/// fulfilled, the reason when it is rejected) and MoveNext again. When MoveNext returns false,
/// <see cref="AsyncDisposeResourcesOperation.Completion"/> is the thrown value of the resulting completion (or null).
/// Built-in code that has no frame to suspend uses <see cref="DisposeResourcesAsync(JsRealm, DisposeCapability, JsValue?)"/>,
/// which drives the operation with promise continuations and returns a promise.
/// </para>
/// </remarks>
internal static class DisposableOperations
{
    private static readonly PropertyKey ErrorKey = new("error");
    private static readonly PropertyKey SuppressedKey = new("suppressed");

    /// <summary>NewDisposeCapability()</summary>
    public static DisposeCapability NewDisposeCapability() => new();

    /// <summary>AddDisposableResource(disposeCapability, V, hint): the value of a <c>using</c> / <c>await using</c> declaration, or of <c>use(value)</c>.</summary>
    public static void AddDisposableResource(DisposeCapability capability, JsValue value, DisposeHint hint)
    {
        if (value.IsNullOrUndefined && hint is DisposeHint.SyncDispose)
            return;

        // When V is null or undefined and hint is async-dispose, the resource is recorded to ensure an Await when the resources are disposed
        capability.Add(CreateDisposableResource(value, hint));
    }

    /// <summary>AddDisposableResource(disposeCapability, undefined, hint, method): a function given to <c>defer</c>.</summary>
    public static void AddDisposableResource(DisposeCapability capability, DisposeHint hint, JsValue method)
    {
        if (!method.IsCallable)
        {
            ThrowHelper.ThrowTypeError($"{JsOperations.DescribeForError(method)} is not a function");
        }

        capability.Add(new DisposableResource(JsValue.Undefined, hint, method.AsObject(), DisposeMethodKind.Method));
    }

    /// <summary>
    /// AddDisposableResource(disposeCapability, undefined, hint, F) where F is the closure created by <c>adopt(value, onDispose)</c>:
    /// F calls onDispose(undefined, value). <paramref name="onDispose"/> must be callable.
    /// </summary>
    public static void AddAdoptedResource(DisposeCapability capability, JsValue value, DisposeHint hint, JsObject onDispose)
    {
        capability.Add(new DisposableResource(value, hint, onDispose, DisposeMethodKind.Adopted));
    }

    /// <summary>CreateDisposableResource(V, hint)</summary>
    public static DisposableResource CreateDisposableResource(JsValue value, DisposeHint hint)
    {
        if (value.IsNullOrUndefined)
            return new DisposableResource(JsValue.Undefined, hint, Method: null, DisposeMethodKind.Method);

        if (!value.IsObject)
            return ThrowHelper.ThrowTypeError<DisposableResource>($"{JsOperations.DescribeForError(value)} is not an object: it cannot be disposed");

        var method = GetDisposeMethod(value, hint, out var kind);
        if (method is null)
            return ThrowHelper.ThrowTypeError<DisposableResource>(hint is DisposeHint.AsyncDispose ? "The value has neither a [Symbol.asyncDispose] nor a [Symbol.dispose] method" : "The value has no [Symbol.dispose] method");

        return new DisposableResource(value, hint, method, kind);
    }

    /// <summary>
    /// GetDisposeMethod(V, hint). For an async-dispose resource that has only a [Symbol.dispose] method,
    /// <paramref name="kind"/> is <see cref="DisposeMethodKind.SyncMethodForAsyncDispose"/>: the method is called as the
    /// closure of the spec does (see <see cref="DisposableResource.Invoke(JsRealm)"/>).
    /// </summary>
    public static JsObject? GetDisposeMethod(JsValue value, DisposeHint hint, out DisposeMethodKind kind)
    {
        kind = DisposeMethodKind.Method;
        if (hint is DisposeHint.AsyncDispose)
        {
            var asyncMethod = JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.AsyncDispose));
            if (asyncMethod is not null)
                return asyncMethod;

            var syncMethod = JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.Dispose));
            if (syncMethod is not null)
            {
                kind = DisposeMethodKind.SyncMethodForAsyncDispose;
            }

            return syncMethod;
        }

        return JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.Dispose));
    }

    /// <summary>
    /// DisposeResources(disposeCapability, completion) for a capability whose resources are all sync-dispose (a
    /// <c>using</c> declaration, DisposableStack). <paramref name="completion"/> is the thrown value of a throw completion,
    /// or null for any other completion. Returns the thrown value of the resulting completion, or null when the original
    /// completion stands (the caller then completes as it would have without the resources).
    /// </summary>
    public static JsValue? DisposeResources(DisposeCapability capability, JsValue? completion)
    {
        var realm = JsEngine.Current!.CurrentRealm!;
        for (var i = capability.Count - 1; i >= 0; i--)
        {
            var resource = capability[i];
            if (resource.Method is null)
                continue;

            try
            {
                resource.Invoke(realm);
            }
            catch (JsException exception)
            {
                completion = MergeCompletion(realm, completion, exception.Value);
            }
        }

        capability.Clear();
        return completion;
    }

    /// <summary>
    /// DisposeResources(disposeCapability, completion) with the Awaits (an <c>await using</c> declaration,
    /// AsyncDisposableStack), driven by promise continuations: returns a promise of %Promise% of the realm, resolved with
    /// undefined when the original completion stands, or rejected with the thrown value of the resulting completion. The
    /// steps before the first Await run synchronously.
    /// </summary>
    public static JsPromise DisposeResourcesAsync(JsRealm realm, DisposeCapability capability, JsValue? completion)
    {
        var promiseCapability = PromiseCapability.CreateIntrinsic(realm);
        DisposeResourcesAsync(realm, capability, completion, promiseCapability);
        return (JsPromise)promiseCapability.Promise;
    }

    /// <summary>DisposeResourcesAsync, settling an existing promise capability (resolved with undefined, or rejected).</summary>
    public static void DisposeResourcesAsync(JsRealm realm, DisposeCapability capability, JsValue? completion, PromiseCapability promiseCapability)
    {
        var driver = new AsyncDisposeDriver(realm, new AsyncDisposeResourcesOperation(realm, capability, completion), promiseCapability);
        driver.Run();
    }

    /// <summary>The steps of DisposeResources that combine the completion with the throw completion of a disposal.</summary>
    internal static JsValue MergeCompletion(JsRealm realm, JsValue? completion, JsValue error)
    {
        if (completion is { } suppressed)
            return CreateSuppressedError(realm, error, suppressed);

        return error;
    }

    /// <summary>A newly created SuppressedError object of the realm, with the <c>error</c> and <c>suppressed</c> properties (no message).</summary>
    public static JsObject CreateSuppressedError(JsRealm realm, JsValue error, JsValue suppressed)
    {
        var result = new JsObject(realm.Intrinsics.SuppressedErrorPrototype, ObjectClass.Error);
        result.DefineBuiltIn(ErrorKey, error);
        result.DefineBuiltIn(SuppressedKey, suppressed);
        return result;
    }

    /// <summary>Drives an <see cref="AsyncDisposeResourcesOperation"/> with promise continuations (the Await of a built-in function).</summary>
    private sealed class AsyncDisposeDriver(JsRealm realm, AsyncDisposeResourcesOperation operation, PromiseCapability promiseCapability)
    {
        private static readonly PromiseContinuation OnFulfilled = static (state, _) => ((AsyncDisposeDriver)state!).Resume(thrown: null);
        private static readonly PromiseContinuation OnRejected = static (state, reason) => ((AsyncDisposeDriver)state!).Resume(reason);

        public void Run()
        {
            while (operation.MoveNext(out var value))
            {
                JsPromise promise;
                try
                {
                    promise = JsPromise.PromiseResolve(realm, value);
                }
                catch (JsException exception)
                {
                    operation.SetAwaitResult(exception.Value);
                    continue;
                }

                JsPromise.PerformPromiseThen(promise, OnFulfilled, OnRejected, this);
                return;
            }

            if (operation.Completion is { } error)
            {
                promiseCapability.Reject(error);
            }
            else
            {
                promiseCapability.Resolve(JsValue.Undefined);
            }
        }

        private void Resume(JsValue? thrown)
        {
            var engine = realm.Engine;
            var previousRealm = engine.CurrentRealm;
            engine.CurrentRealm = realm;
            try
            {
                operation.SetAwaitResult(thrown);
                Run();
            }
            finally
            {
                engine.CurrentRealm = previousRealm;
            }
        }
    }
}

/// <summary>
/// The steps of DisposeResources(disposeCapability, completion) with its Awaits, as a state machine driven by the caller
/// (see <see cref="DisposableOperations"/>).
/// </summary>
internal sealed class AsyncDisposeResourcesOperation
{
    private readonly JsRealm _realm;
    private readonly DisposeCapability _capability;
    private int _index;
    private bool _needsAwait;
    private bool _hasAwaited;
    private bool _awaitedBeforeCurrentResource;
    private bool _awaitingDisposal;
    private bool _awaiting;
    private JsValue? _completion;

    /// <param name="realm">The current realm (the realm of the SuppressedError objects and of the promises).</param>
    /// <param name="capability">The resources.</param>
    /// <param name="completion">The thrown value of the completion of the scope, or null for a normal (return, break...) completion.</param>
    public AsyncDisposeResourcesOperation(JsRealm realm, DisposeCapability capability, JsValue? completion)
    {
        _realm = realm;
        _capability = capability;
        _index = capability.Count;
        _completion = completion;
    }

    /// <summary>Gets the thrown value of the resulting completion once <see cref="MoveNext(out JsValue)"/> returned false, or null when the original completion stands.</summary>
    public JsValue? Completion => _completion;

    /// <summary>
    /// Runs the steps until the next Await, or until the end. Returns true when the caller must perform Await(<paramref name="value"/>),
    /// then call <see cref="SetAwaitResult(JsValue?)"/> and MoveNext again; false when the resources are disposed.
    /// </summary>
    public bool MoveNext(out JsValue value)
    {
        if (_awaiting)
            throw new InvalidOperationException("SetAwaitResult must be called before MoveNext");

        while (_index > 0)
        {
            var resource = _capability[_index - 1];
            if (!_awaitedBeforeCurrentResource && resource.Hint is DisposeHint.SyncDispose && _needsAwait && !_hasAwaited)
            {
                // Perform ! Await(undefined)
                _needsAwait = false;
                _awaitedBeforeCurrentResource = true;
                _awaiting = true;
                value = JsValue.Undefined;
                return true;
            }

            _awaitedBeforeCurrentResource = false;
            _index--;
            if (resource.Method is not null)
            {
                JsValue result;
                try
                {
                    result = resource.Invoke(_realm);
                }
                catch (JsException exception)
                {
                    _completion = DisposableOperations.MergeCompletion(_realm, _completion, exception.Value);
                    continue;
                }

                if (resource.Hint is DisposeHint.AsyncDispose)
                {
                    // Set result to Completion(Await(result.[[Value]]))
                    _hasAwaited = true;
                    _awaitingDisposal = true;
                    _awaiting = true;
                    value = result;
                    return true;
                }
            }
            else
            {
                // Only the null or undefined value of an await using declaration (or AsyncDisposableStack.prototype.use)
                _needsAwait = true;
            }
        }

        if (_needsAwait && !_hasAwaited)
        {
            // Perform ! Await(undefined)
            _needsAwait = false;
            _awaiting = true;
            value = JsValue.Undefined;
            return true;
        }

        _capability.Clear();
        value = JsValue.Undefined;
        return false;
    }

    /// <summary>Sets the result of the Await requested by <see cref="MoveNext(out JsValue)"/>: null when it completed normally, the thrown value otherwise.</summary>
    public void SetAwaitResult(JsValue? thrown)
    {
        if (!_awaiting)
            throw new InvalidOperationException("No Await is pending");

        _awaiting = false;
        if (_awaitingDisposal)
        {
            _awaitingDisposal = false;
            if (thrown is { } error)
            {
                _completion = DisposableOperations.MergeCompletion(_realm, _completion, error);
            }
        }
    }
}
