using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An ECMAScript agent (https://tc39.es/ecma262/#sec-agents): it owns the realms, the job queue and the execution limits
/// of the scripts it runs. An engine is used by one thread at a time.
/// </summary>
public sealed class JsEngine : IDisposable
{
    [ThreadStatic]
    private static JsEngine? t_current;

    private readonly List<JsRealm> _realms = [];

    /// <summary>Initializes a new instance of the <see cref="JsEngine"/> class.</summary>
    public JsEngine(JsEngineOptions? options = null)
    {
        Options = options ?? new JsEngineOptions();
    }

    /// <summary>Gets the options of the engine.</summary>
    public JsEngineOptions Options { get; }

    /// <summary>Gets the engine whose code runs on the current thread, if any.</summary>
    internal static JsEngine? Current => t_current;

    /// <summary>Gets the realm of the running execution context (the current realm).</summary>
    internal JsRealm? CurrentRealm { get; set; }

    /// <summary>Gets the depth of the running calls.</summary>
    internal int CallDepth { get; set; }

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

    /// <summary>Makes the engine the engine of the current thread until the returned scope is disposed.</summary>
    internal EngineScope Enter()
    {
        var previous = t_current;
        t_current = this;
        return new EngineScope(previous);
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
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowStackOverflow() => ThrowHelper.ThrowRangeError("Maximum call stack size exceeded");

    /// <summary>Releases the resources of the engine.</summary>
    public void Dispose()
    {
    }

    internal readonly struct EngineScope(JsEngine? previous) : IDisposable
    {
        public void Dispose() => t_current = previous;
    }
}
