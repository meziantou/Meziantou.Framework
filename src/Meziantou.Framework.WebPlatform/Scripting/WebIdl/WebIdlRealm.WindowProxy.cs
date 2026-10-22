using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The WindowProxy exotic object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object)
// and the cross-origin objects (https://html.spec.whatwg.org/multipage/nav-history-apis.html#cross-origin-objects).
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>CrossOriginProperties of a Window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginproperties-(-o-)), in order.</summary>
    private static readonly CrossOriginProperty[] CrossOriginWindowProperties =
    [
        new("window", NeedsGet: true, NeedsSet: false),
        new("self", NeedsGet: true, NeedsSet: false),
        new("location", NeedsGet: true, NeedsSet: true),
        new("close"),
        new("closed", NeedsGet: true, NeedsSet: false),
        new("focus"),
        new("blur"),
        new("frames", NeedsGet: true, NeedsSet: false),
        new("length", NeedsGet: true, NeedsSet: false),
        new("top", NeedsGet: true, NeedsSet: false),
        new("opener", NeedsGet: true, NeedsSet: false),
        new("parent", NeedsGet: true, NeedsSet: false),
        new("postMessage"),
    ];

    /// <summary>CrossOriginProperties of a Location object.</summary>
    private static readonly CrossOriginProperty[] CrossOriginLocationProperties =
    [
        new("href", NeedsGet: false, NeedsSet: true),
        new("replace"),
    ];

    /// <summary>
    /// The [[CrossOriginPropertyDescriptorMap]] entries whose current settings object is this realm: the descriptors of the
    /// cross-origin properties of a Window or a Location object, by property name. The functions are created in this realm.
    /// </summary>
    private readonly ConditionalWeakTable<object, Dictionary<string, JsPropertyDescriptor<TValue>>> _crossOriginDescriptors = [];

    /// <summary>The IDL members of the cross-origin properties of the Window and Location interfaces of this realm, by interface and name.</summary>
    private readonly Dictionary<(string Interface, string Name), CrossOriginMember> _crossOriginMembers = [];

    private TValue? _hasInstanceSymbol;
    private TValue? _isConcatSpreadableSymbol;

    /// <summary>
    /// Returns the WindowProxy of the browsing context of a window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy):
    /// one object per browsing context, shared by the realms of the agent, whose [[Window]] is the active window of the
    /// browsing context, so that it is the same object before and after a navigation (e.g. the window returned by
    /// window.open() and the source of the messages of the documents it navigates to). It is the value of a window for every
    /// realm, also the this value of the scripts of the window and the value of <c>globalThis</c>.
    /// </summary>
    /// <remarks>
    /// When the window is not same origin-domain with the realm of the caller, the internal methods are the ones of the
    /// cross-origin objects (only the cross-origin properties are visible, the others throw a "SecurityError" DOMException).
    /// A window that never had a browsing context (e.g. the window of a document created by DOMParser) has no WindowProxy:
    /// its global object is returned, as when the engine does not support the WindowProxy objects.
    /// </remarks>
    public TValue GetWindowProxy(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        BrowsingContextInstance instance;
        if (window.BrowsingContext is { } browsingContext)
        {
            instance = browsingContext.ActiveBrowsingContext;
        }
        else if (window.DiscardedBrowsingContext is { } discarded && ReferenceEquals(discarded.ActiveWindow, window))
        {
            // The last window of a discarded browsing context (e.g. a removed iframe) keeps its WindowProxy
            instance = discarded;
        }
        else
        {
            return Agent.GetRealm(window).Global;
        }

        if (Agent.WindowProxies.TryGetValue(instance, out var proxy))
            return proxy;

        proxy = Adapter.CreateWindowProxy(new WindowProxyHandler(Agent, instance, window));
        if (proxy is null)
            return Agent.GetRealm(window).Global;

        Agent.WindowProxies.AddOrUpdate(instance, proxy);
        Agent.WindowProxyBrowsingContexts.AddOrUpdate(proxy, instance);
        return proxy;
    }

    /// <summary>Creates the JavaScript object of a Location object: a cross-origin object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-location-interface).</summary>
    private TValue CreateLocationObject(TValue prototype, Location location)
        => Adapter.RunInRealm(EngineRealm, () => Adapter.CreateCrossOriginPlatformObject(prototype, location, new LocationHandler(Agent, location)) ?? Adapter.CreatePlatformObject(prototype, location, handler: null));

    /// <summary>
    /// IsPlatformObjectSameOrigin (https://html.spec.whatwg.org/multipage/nav-history-apis.html#isplatformobjectsameorigin-(-o-))
    /// of a Window or a Location object whose relevant global object is <paramref name="window"/>, for the code of the
    /// current realm (the code of the implementation can access every window).
    /// </summary>
    private static bool IsSameOriginWithCurrentRealm(WebIdlAgent<TAdapter, TValue> agent, Window window)
    {
        var caller = agent.CurrentRealm;
        return caller is null || ReferenceEquals(caller.GlobalScope, window) || Document.IsSameOriginDomain(caller.GlobalScope.EnvironmentDocument, window.Document);
    }

    /// <summary>The "SecurityError" DOMException of this realm thrown by the access of a cross-origin property.</summary>
    private Exception CrossOriginSecurityError() => ToJsException(new DomException("Blocked a frame from accessing a cross-origin frame", "SecurityError"));

    /// <summary>
    /// The [[GetOwnProperty]] of a cross-origin Window or Location object for a key that is not an array index, for the code
    /// of this realm: CrossOriginGetOwnPropertyHelper, the named child navigables of a window, then CrossOriginPropertyFallback.
    /// </summary>
    private JsPropertyDescriptor<TValue> CrossOriginGetOwnProperty(object target, TValue key)
    {
        var name = Adapter.IsString(key) ? Adapter.AsString(key) : null;
        if (name is not null && CrossOriginGetOwnPropertyHelper(target, name) is { } descriptor)
            return descriptor;

        // The first child navigable with the target name. The specification only exposes the names of the document-tree child
        // navigable target name property set (the children that are same origin with the document of the window), but the
        // browsers expose every named child to a cross-origin caller (e.g. a frame reaches its sibling with parent.name
        // when their parent is cross-origin), which the web-platform-tests rely on.
        if (name is not null && target is Window window && window.GetFirstNamedChildNavigable(name, out _) is { } namedChild)
            return JsPropertyDescriptor<TValue>.Data(GetWindowProxy(namedChild.Window), writable: false, enumerable: false, configurable: true);

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginpropertyfallback-(-p-)
        if (name == "then" || IsCrossOriginFallbackSymbol(key))
            return JsPropertyDescriptor<TValue>.Data(Adapter.Undefined, writable: false, enumerable: false, configurable: true);

        throw CrossOriginSecurityError();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossorigingetownpropertyhelper-(-o,-p-): the descriptor
    /// of a cross-origin property, whose functions are created in this realm once per object and property (the
    /// [[CrossOriginPropertyDescriptorMap]]). They perform the steps of the IDL operation or attribute of the object, as
    /// functions of this realm (the errors they throw are the ones of this realm), whatever the current value of the property
    /// is (e.g. a window that replaced its <c>focus</c> method).
    /// </summary>
    private JsPropertyDescriptor<TValue>? CrossOriginGetOwnPropertyHelper(object target, string name)
    {
        var (interfaceName, properties, window) = target is Window targetWindow ? ("Window", CrossOriginWindowProperties, targetWindow) : ("Location", CrossOriginLocationProperties, ((Location)target).Window);
        var index = Array.FindIndex(properties, property => property.Name == name);
        if (index < 0)
            return null;

        var descriptors = _crossOriginDescriptors.GetOrCreateValue(target);
        if (descriptors.TryGetValue(name, out var descriptor))
            return descriptor;

        var property = properties[index];
        var targetRealm = Agent.GetRealm(window);
        if (!targetRealm._crossOriginMembers.TryGetValue((interfaceName, name), out var member))
            return null;

        // The this value of the steps is the object when the functions are called without this value
        var obj = target is Window ? targetRealm.Global : targetRealm.GetWrapper(target);
        TValue ThisOrObject(TValue thisValue) => Adapter.IsUndefined(thisValue) || Adapter.IsNull(thisValue) ? obj : thisValue;

        descriptor = Adapter.RunInRealm(EngineRealm, () =>
        {
            if (member.Operation is { } operation)
            {
                var function = Adapter.CreateFunction(name, member.Length, (thisValue, arguments) => InvokeOperation(operation, ThisOrObject(thisValue), arguments, member.CEReactions));
                return JsPropertyDescriptor<TValue>.Data(function, writable: false, enumerable: false, configurable: true);
            }

            var getter = property.NeedsGet && member.Getter is { } memberGetter
                ? Adapter.CreateFunction("get " + name, 0, (thisValue, _) => InvokeGetter(memberGetter, ThisOrObject(thisValue)))
                : Adapter.Undefined;
            var setter = property.NeedsSet && member.Setter is { } memberSetter
                ? Adapter.CreateFunction("set " + name, 1, (thisValue, arguments) =>
                {
                    InvokeSetter(memberSetter, ThisOrObject(thisValue), arguments.Length > 0 ? arguments[0] : Adapter.Undefined);
                    return Adapter.Undefined;
                })
                : Adapter.Undefined;
            return JsPropertyDescriptor<TValue>.Accessor(getter, setter, enumerable: false, configurable: true);
        });

        descriptors[name] = descriptor;
        return descriptor;
    }

    private bool IsCrossOriginFallbackSymbol(TValue key)
    {
        if (!Adapter.IsSymbol(key))
            return false;

        return ReferenceEquals(key, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag))
            || ReferenceEquals(key, _hasInstanceSymbol ??= EvaluateBuiltIn("Symbol.hasInstance"))
            || ReferenceEquals(key, _isConcatSpreadableSymbol ??= EvaluateBuiltIn("Symbol.isConcatSpreadable"));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginownpropertykeys-(-o-): the names of the
    /// cross-origin properties, then <c>then</c> and the well-known symbols of CrossOriginPropertyFallback (the indices of
    /// the child navigables of a window precede them).
    /// </summary>
    private TValue[] CrossOriginOwnPropertyKeys(object target)
    {
        var properties = target is Window ? CrossOriginWindowProperties : CrossOriginLocationProperties;
        var keys = new TValue[properties.Length + 4];
        for (var i = 0; i < properties.Length; i++)
        {
            keys[i] = Key(properties[i].Name);
        }

        keys[^4] = Key("then");
        keys[^3] = Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag);
        keys[^2] = _hasInstanceSymbol ??= EvaluateBuiltIn("Symbol.hasInstance");
        keys[^1] = _isConcatSpreadableSymbol ??= EvaluateBuiltIn("Symbol.isConcatSpreadable");
        return keys;
    }

    /// <summary>An entry of CrossOriginProperties: a method when <see cref="NeedsGet"/> and <see cref="NeedsSet"/> are false.</summary>
    private readonly record struct CrossOriginProperty(string Name, bool NeedsGet = false, bool NeedsSet = false);

    /// <summary>The steps of the IDL member of a cross-origin property: the getter and the setter of an attribute, or an operation.</summary>
    private readonly record struct CrossOriginMember(Getter? Getter, Setter? Setter, Operation? Operation, int Length, bool CEReactions);

    /// <summary>
    /// The operations of a WindowProxy: its [[Window]] is the active window of the browsing context (the last window of a
    /// discarded browsing context), and the cross-origin operations run for the realm of the caller.
    /// </summary>
    private sealed class WindowProxyHandler(WebIdlAgent<TAdapter, TValue> agent, BrowsingContextInstance instance, Window window) : IJsWindowProxyHandler<TValue>
    {
        private Window ActiveWindow => instance.ActiveWindow ?? window;

        public TValue Window => agent.GetRealm(ActiveWindow).Global;

        public int ChildCount => (int)ActiveWindow.Length;

        public TValue? GetChild(int index) => ActiveWindow[index] is { } child ? Caller.GetWindowProxy(child) : null;

        public bool IsSameOrigin() => IsSameOriginWithCurrentRealm(agent, ActiveWindow);

        public JsPropertyDescriptor<TValue> GetCrossOriginOwnProperty(TValue key) => Caller.CrossOriginGetOwnProperty(ActiveWindow, key);

        public IReadOnlyList<TValue> GetCrossOriginOwnPropertyKeys() => Caller.CrossOriginOwnPropertyKeys(ActiveWindow);

        public Exception CreateSecurityError() => Caller.CrossOriginSecurityError();

        /// <summary>The realm of the code that uses the WindowProxy.</summary>
        private WebIdlRealm<TAdapter, TValue> Caller => agent.CurrentRealm ?? agent.GetRealm(ActiveWindow);
    }

    /// <summary>The operations of a Location object, whose relevant global object is the window of the Location object.</summary>
    private sealed class LocationHandler(WebIdlAgent<TAdapter, TValue> agent, Location location) : IJsCrossOriginObjectHandler<TValue>
    {
        public bool IsSameOrigin() => IsSameOriginWithCurrentRealm(agent, location.Window);

        public JsPropertyDescriptor<TValue> GetCrossOriginOwnProperty(TValue key) => Caller.CrossOriginGetOwnProperty(location, key);

        public IReadOnlyList<TValue> GetCrossOriginOwnPropertyKeys() => Caller.CrossOriginOwnPropertyKeys(location);

        public Exception CreateSecurityError() => Caller.CrossOriginSecurityError();

        private WebIdlRealm<TAdapter, TValue> Caller => agent.CurrentRealm ?? agent.GetRealm(location.Window);
    }
}
