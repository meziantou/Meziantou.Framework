using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An ECMAScript agent (https://tc39.es/ecma262/#sec-agents): it owns the realms, the job queue and the execution limits
/// of the scripts it runs. An engine is used by one thread at a time.
/// </summary>
public sealed partial class JsEngine : IDisposable
{
    [ThreadStatic]
    private static JsEngine? s_current;

    private readonly List<JsRealm> _realms = [];

    /// <summary>Initializes a new instance of the <see cref="JsEngine"/> class.</summary>
    public JsEngine(JsEngineOptions? options = null)
    {
        Options = options ?? new JsEngineOptions();
        HostHooks = Options.Host ?? JsHost.Default;
    }

    /// <summary>Gets the options of the engine.</summary>
    public JsEngineOptions Options { get; }

    /// <summary>Gets the engine whose code runs on the current thread, if any.</summary>
    internal static JsEngine? Current => s_current;

    /// <summary>Gets the realm of the running execution context (the current realm).</summary>
    public JsRealm? CurrentRealm { get; internal set; }

    /// <summary>Gets the depth of the running calls.</summary>
    internal int CallDepth { get; set; }

    /// <summary>[[ModuleAsyncEvaluationCount]] of the agent.</summary>
    internal long ModuleAsyncEvaluationCount { get; set; }

    /// <summary>The objects being joined by Array.prototype.join (the cycle detection of the browsers).</summary>
    internal List<JsObject>? ArrayJoinStack { get; set; }

    /// <summary>Gets the realms created by the engine.</summary>
    public IReadOnlyList<JsRealm> Realms => _realms;

    /// <summary>Creates a realm with its intrinsics and an ordinary global object.</summary>
    public JsRealm CreateRealm()
    {
        using var scope = Enter();
        var realm = new JsRealm(this);
        _realms.Add(realm);
        return realm;
    }

    /// <summary>
    /// Makes the engine the engine of the current thread until the returned scope is disposed. The methods of the objects
    /// (<see cref="JsObject.GetProperty(JsPropertyKey)"/>...) need it when they are not called by the engine (a callback).
    /// </summary>
    public JsEngineScope Enter()
    {
        var previous = s_current;
        s_current = this;
        return new JsEngineScope(previous);
    }

    /// <summary>Enters a call: checks the recursion limit and makes the realm of the callee current; returns the previous realm.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsRealm? EnterNativeCall(JsRealm realm)
    {
        CheckCallDepth();
        CallDepth++;
        var previous = CurrentRealm;
        CurrentRealm = realm;
        return previous;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ExitNativeCall(JsRealm? previousRealm)
    {
        CallDepth--;
        CurrentRealm = previousRealm;
    }

    /// <summary>Throws a RangeError when the calls are too deep (the limit of the options, or the .NET stack is almost full).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckCallDepth()
    {
        if (CallDepth >= Options.RecursionLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            ThrowStackOverflow();
        }

        // A deep recursion without loops (e.g. a naive Fibonacci) must also check the limits
        CountInterruptPoint();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowStackOverflow() => ThrowHelper.ThrowRangeError("Maximum call stack size exceeded");

    /// <summary>Releases the resources of the engine.</summary>
    public void Dispose()
    {
    }

    internal static void RestoreCurrent(JsEngine? previous) => s_current = previous;
}
