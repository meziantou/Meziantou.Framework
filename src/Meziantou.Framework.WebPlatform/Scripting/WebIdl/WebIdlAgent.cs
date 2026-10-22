using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The services the WebIDL bindings need from the script engine that uses them: error reporting, and the bookkeeping of
/// the JavaScript execution context stack around the callbacks.
/// </summary>
public interface IWebIdlHost
{
    /// <summary>
    /// Reports an exception thrown by a callback (e.g. an event listener) to the global object of a realm
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception).
    /// </summary>
    void ReportException(GlobalScope global, Exception exception);

    /// <summary>Called before a callback is invoked: the JavaScript execution context stack is not empty until <see cref="OnCallbackInvoked"/>.</summary>
    void OnCallbackInvoking();

    /// <summary>
    /// Called after a callback is invoked ("clean up after running a callback",
    /// https://webidl.spec.whatwg.org/#clean-up-after-running-a-callback): the engine performs a microtask checkpoint when no
    /// script is running.
    /// </summary>
    void OnCallbackInvoked(GlobalScope global);

    /// <summary>
    /// Gets whether JavaScript code is running (the JavaScript execution context stack is not empty): the current realm of
    /// the engine is then the realm of the running code. The default is false.
    /// </summary>
    bool IsScriptRunning => false;

    /// <summary>
    /// Called periodically while WebAssembly code runs: throws when the running script must be interrupted (e.g. the
    /// timeout of the engine). The default does nothing.
    /// </summary>
    void CheckInterrupt()
    {
    }
}

/// <summary>
/// The WebIDL bindings of the DOM for a JavaScript engine (an agent: the similar-origin window agent, or the agent of a
/// worker): the realms of the global objects, and the JavaScript objects of the .NET objects. A platform object has a single JavaScript object, whichever realm
/// accesses it, so the wrappers and the callbacks are shared by the realms.
/// </summary>
/// <typeparam name="TAdapter">The adapter of the JavaScript engine.</typeparam>
/// <typeparam name="TValue">The type of the JavaScript values of the engine.</typeparam>
public sealed class WebIdlAgent<TAdapter, TValue>
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    private readonly Dictionary<GlobalScope, WebIdlRealm<TAdapter, TValue>> _realms = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, WebIdlRealm<TAdapter, TValue>> _realmsByEngineRealm = new(ReferenceEqualityComparer.Instance);
    private object? _initialRealm;

    private const string CachesNeverCreateValues = "The ConditionalWeakTable caches never create their values with GetOrCreateValue (the only member that requires a public parameterless constructor)";

    /// <summary>Creates the bindings of an engine.</summary>
    /// <param name="adapter">The adapter of the engine.</param>
    /// <param name="host">The services of the script engine.</param>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    public WebIdlAgent(TAdapter adapter, IWebIdlHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Adapter = adapter;
        Host = host;
        _initialRealm = adapter.CurrentRealm;
    }

    /// <summary>Gets the adapter of the engine.</summary>
    public TAdapter Adapter { get; }

    /// <summary>Gets the services of the script engine.</summary>
    public IWebIdlHost Host { get; }

    /// <summary>
    /// Gets or sets a callback invoked when a realm is created, before any script runs in it: the host can add its own
    /// functions to the global object.
    /// </summary>
    public Action<WebIdlRealm<TAdapter, TValue>>? RealmCreated { get; set; }

    /// <summary>
    /// Gets or sets a factory of the realms of the engine. The first realm uses the realm that is current when the
    /// agent is created; the default factory of the other realms throws <see cref="NotSupportedException"/>.
    /// </summary>
    public Func<object>? RealmFactory { get; set; }

    /// <summary>Gets the realms.</summary>
    public IEnumerable<WebIdlRealm<TAdapter, TValue>> Realms => _realms.Values;

    // The caches shared by the realms
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    [field: UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    internal ConditionalWeakTable<object, TValue> Wrappers { get; } = [];

    /// <summary>The WebAssembly state of the agent (its store and its object caches), created on first use.</summary>
    internal WebAssemblyAgentState<TValue>? WebAssemblyState { get; set; }

    /// <summary>Releases the WebAssembly store of the agent (the native resources of the runtime).</summary>
    public void DisposeWebAssembly()
    {
        WebAssemblyState?.Store.Dispose();
        WebAssemblyState = null;
    }

    /// <summary>The WindowProxy objects of the browsing contexts (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy), shared by the realms of the agent.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    [field: UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    internal ConditionalWeakTable<BrowsingContextInstance, TValue> WindowProxies { get; } = [];

    /// <summary>The browsing contexts of the WindowProxy objects.</summary>
    internal ConditionalWeakTable<TValue, BrowsingContextInstance> WindowProxyBrowsingContexts { get; } = [];

    internal ConditionalWeakTable<object, Dictionary<string, TValue>> ObservableArrays { get; } = [];

    internal ConditionalWeakTable<TValue, object> CallableWrapperTargets { get; } = [];

    internal ConditionalWeakTable<TValue, JsCallbackInterface<TAdapter, TValue>> CallbackInterfaces { get; } = [];

    internal ConditionalWeakTable<TValue, Dictionary<Type, Delegate>> Delegates { get; } = [];

    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    [field: UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    internal ConditionalWeakTable<Delegate, TValue> DelegateFunctions { get; } = [];

    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    [field: UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    internal ConditionalWeakTable<object, TValue> FrozenArrays { get; } = [];

    internal ConditionalWeakTable<TValue, JsCustomElementConstructor<TAdapter, TValue>> CustomElementConstructors { get; } = [];

    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    [field: UnconditionalSuppressMessage("Trimming", "IL2091", Justification = CachesNeverCreateValues)]
    internal ConditionalWeakTable<Task, TValue> PendingPromises { get; } = [];

    /// <summary>The serialized forms of the platform objects of the structured clones, by the object of their record.</summary>
    internal ConditionalWeakTable<TValue, object> SerializedPlatformObjects { get; } = [];

    /// <summary>The WeakMap of the internal slots of the stream objects (Streams.js), shared by the realms of the agent.</summary>
    internal TValue? StreamInternals { get; set; }

    /// <summary>The WeakMap of the internal slots of the Request and Response objects (Fetch.js), shared by the realms of the agent.</summary>
    internal TValue? FetchInternals { get; set; }

    /// <summary>The WeakSet of the immutable prototype exotic objects of the realms of the agent (see <c>WebIdlRealm.DefineImmutablePrototypes</c>).</summary>
    internal TValue? ImmutablePrototypeObjects { get; set; }

    /// <summary>The WeakMap of the internal slots of the Web Cryptography API objects (WebCrypto.js), shared by the realms of the agent.</summary>
    internal TValue? CryptoInternals { get; set; }

    /// <summary>Returns the realm of a global object, or null when it was not created.</summary>
    public WebIdlRealm<TAdapter, TValue>? TryGetRealm(GlobalScope global) => _realms.GetValueOrDefault(global);

    /// <summary>Returns the realm of a global object, creating it on first use.</summary>
    public WebIdlRealm<TAdapter, TValue> GetRealm(GlobalScope global)
    {
        ArgumentNullException.ThrowIfNull(global);
        if (_realms.TryGetValue(global, out var realm))
            return realm;

        object engineRealm;
        if (_initialRealm is { } initialRealm)
        {
            engineRealm = initialRealm;
            _initialRealm = null;
        }
        else
        {
            engineRealm = RealmFactory?.Invoke() ?? throw new NotSupportedException("The engine does not support several realms");
        }

        realm = Adapter.RunInRealm(engineRealm, () => new WebIdlRealm<TAdapter, TValue>(this, global, engineRealm));
        _realms[global] = realm;
        _realmsByEngineRealm[engineRealm] = realm;
        Wrappers.AddOrUpdate(global, realm.Global);
        Adapter.RunInRealm(engineRealm, () =>
        {
            // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context: the this value of
            // the global code of a window is the WindowProxy of its browsing context
            if (global is Window window && realm.GetWindowProxy(window) is var windowProxy && !ReferenceEquals(windowProxy, realm.Global))
            {
                Adapter.TrySetGlobalThis(engineRealm, windowProxy);
            }

            realm.Initialize();
            RealmCreated?.Invoke(realm);
            return realm;
        });

        return realm;
    }

    /// <summary>Returns the realm of an engine realm, or null.</summary>
    public WebIdlRealm<TAdapter, TValue>? GetRealm(object engineRealm) => _realmsByEngineRealm.GetValueOrDefault(engineRealm);

    /// <summary>Gets the realm of the running execution context, or null when it is not the realm of a global object.</summary>
    public WebIdlRealm<TAdapter, TValue>? CurrentRealm => _realmsByEngineRealm.GetValueOrDefault(Adapter.CurrentRealm);

    /// <summary>Returns the realm whose global object is <paramref name="global"/>, or null.</summary>
    public WebIdlRealm<TAdapter, TValue>? GetRealmByGlobal(TValue global)
    {
        // A WindowProxy is the global object of the active window of its browsing context
        if (WindowProxyBrowsingContexts.TryGetValue(global, out var browsingContext))
            return browsingContext.ActiveWindow is { } activeWindow ? GetRealm(activeWindow) : null;

        foreach (var realm in _realms.Values)
        {
            if (ReferenceEquals(realm.Global, global))
                return realm;
        }

        return null;
    }

    /// <summary>
    /// Returns the associated realm of a callback (https://webidl.spec.whatwg.org/#dfn-associated-realm): the realm whose
    /// Function.prototype (for a function) or Object.prototype is in the prototype chain of the object. The prototype chain
    /// of a revoked proxy cannot be read: its associated realm is the realm that created it.
    /// </summary>
    public WebIdlRealm<TAdapter, TValue>? GetAssociatedRealm(TValue obj)
    {
        var adapter = Adapter;
        for (var current = obj; ;)
        {
            if (adapter.IsRevokedProxy(current, out var creationObjectPrototype))
                return creationObjectPrototype is null ? null : FindRealmByPrototype(creationObjectPrototype);

            current = adapter.GetPrototypeOf(current);
            if (adapter.IsNull(current))
                return null;

            if (FindRealmByPrototype(current) is { } realm)
                return realm;
        }
    }

    private WebIdlRealm<TAdapter, TValue>? FindRealmByPrototype(TValue prototype)
    {
        foreach (var candidate in _realms.Values)
        {
            if (ReferenceEquals(candidate.FunctionPrototype, prototype) || ReferenceEquals(candidate.ObjectPrototype, prototype))
                return candidate;
        }

        return null;
    }

    /// <summary>Whether the callback cannot run because its global object is a window whose document is not fully active.</summary>
    internal bool IsCallbackRealmInactive(TValue callback) => GetAssociatedRealm(callback) is { } realm && !realm.GlobalScope.CanRunScript;

    /// <summary>Reports an exception thrown by a callback to the global object of its associated realm (e.g. an event listener).</summary>
    internal void ReportCallbackException(TValue callback, WebIdlRealm<TAdapter, TValue> fallbackRealm, Exception exception)
    {
        var realm = GetAssociatedRealm(callback) ?? fallbackRealm;
        EventDispatcher.ReportedExceptionCount++;
        Host.ReportException(realm.GlobalScope, exception);
    }

    /// <summary>Returns the JavaScript value of an exception thrown by the engine or by the DOM, in the current realm.</summary>
    public TValue GetExceptionValue(Exception exception, WebIdlRealm<TAdapter, TValue> fallbackRealm)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(fallbackRealm);
        if (Adapter.TryGetThrownValue(exception, out var value))
            return value;

        return (CurrentRealm ?? fallbackRealm).ToJsError(exception);
    }
}
