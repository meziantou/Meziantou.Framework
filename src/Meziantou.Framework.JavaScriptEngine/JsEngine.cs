using System.Diagnostics.CodeAnalysis;
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

    // The managed thread id of the thread that entered the engine (0: none)
    private int _ownerThreadId;

    /// <summary>Initializes a new instance of the <see cref="JsEngine"/> class.</summary>
    public JsEngine(JsEngineOptions? options = null)
    {
        // The engine uses its own copy: the options given to the constructor can be changed or shared afterwards
        Options = (options ?? new JsEngineOptions()).CreateEngineCopy();
        HostHooks = Options.Host ?? JsHost.Default;
        ShapeRoots = new ShapeRoots(this);
    }

    /// <summary>
    /// Gets the options of the engine: a copy of the options given to the constructor. The limits can be changed on the
    /// thread of the engine; the other options cannot (see <see cref="JsEngineOptions"/>).
    /// </summary>
    public JsEngineOptions Options { get; }

    /// <summary>Gets the engine whose code runs on the current thread, if any.</summary>
    internal static JsEngine? Current => s_current;

    /// <summary>Gets the managed thread id of the last thread that entered the engine (the thread of the engine).</summary>
    internal int LastEnteredThreadId { get; private set; }

    /// <summary>Gets the realm of the running execution context (the current realm).</summary>
    public JsRealm? CurrentRealm { get; internal set; }

    /// <summary>Gets the depth of the running calls.</summary>
    internal int CallDepth { get; set; }

    /// <summary>Gets the free frames of the ordinary calls.</summary>
    internal Interpreter.FramePool FramePool { get; } = new();

    /// <summary>Gets the roots of the shapes of the objects of the engine.</summary>
    internal ShapeRoots ShapeRoots { get; }

    /// <summary>Gets the inline cache shared by the megamorphic property accesses (created on first use).</summary>
    internal MegamorphicCache MegamorphicCache => field ??= new();

    /// <summary>[[ModuleAsyncEvaluationCount]] of the agent.</summary>
    internal long ModuleAsyncEvaluationCount { get; set; }

    /// <summary>The objects being joined by Array.prototype.join (the cycle detection of the browsers).</summary>
    internal List<JsObject>? ArrayJoinStack { get; set; }

    /// <summary>Gets the realms created by <see cref="CreateRealm"/> (the realms of the ShadowRealm objects are not included).</summary>
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
    /// Creates the realm of a ShadowRealm object (InitializeHostDefinedRealm): an ordinary global object whose properties
    /// are the ones of ECMAScript. It is not added to <see cref="Realms"/>: it is collected with its ShadowRealm object.
    /// </summary>
    internal JsRealm CreateShadowRealm() => new(this);

    /// <summary>
    /// Makes the engine the engine of the current thread until the returned scope is disposed. The methods of the objects
    /// (<see cref="JsObject.GetProperty(JsPropertyKey)"/>...) need it when they are not called by the engine (a callback).
    /// </summary>
    /// <remarks>
    /// An engine is used by one thread at a time: the thread that enters it owns it until it disposes its outermost scope.
    /// The owning thread can enter the engine again (nested scopes, callbacks); another thread gets an
    /// <see cref="InvalidOperationException"/> instead of corrupting the state of the engine.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another thread is using the engine.</exception>
    public JsEngineScope Enter()
    {
        var previous = s_current;
        var ownsEngine = AcquireOwnership();
        s_current = this;
        return new JsEngineScope(previous, ownsEngine ? this : null);
    }

    /// <summary>
    /// Makes the current thread the owner of the engine when it is not the engine of the thread yet; returns true when
    /// the caller took the ownership (it must call <see cref="ReleaseOwnership"/> when it exits).
    /// </summary>
    /// <exception cref="InvalidOperationException">Another thread is using the engine.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool AcquireOwnership()
    {
        if (ReferenceEquals(s_current, this))
            return false;

        // The first entry of the thread takes the ownership of the engine; a thread that owns it already (it entered
        // another engine in the meantime) keeps it
        var threadId = Environment.CurrentManagedThreadId;
        var owner = Interlocked.CompareExchange(ref _ownerThreadId, threadId, 0);
        if (owner != 0 && owner != threadId)
            ThrowConcurrentAccess();

        LastEnteredThreadId = threadId;
        return owner == 0;
    }

    /// <summary>Releases the ownership taken by the outermost <see cref="Enter"/> of the thread.</summary>
    internal void ReleaseOwnership() => Volatile.Write(ref _ownerThreadId, 0);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowConcurrentAccess() => throw new InvalidOperationException("The engine is used by another thread: an engine is used by one thread at a time (create one engine per thread)");

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
