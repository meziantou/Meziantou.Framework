using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The WindowProxy exotic object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object).
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The names of the cross-origin properties of a Window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginproperties-(-o-)).</summary>
    private static readonly string[] CrossOriginWindowProperties = ["window", "self", "location", "close", "closed", "focus", "blur", "frames", "length", "top", "opener", "parent", "postMessage"];

    /// <summary>
    /// Returns the JavaScript value of a window for the code of a realm: the global object when the window is the one of the
    /// realm, else the WindowProxy of its browsing context (<see cref="GetWindowProxy"/>).
    /// </summary>
    /// <remarks>
    /// The engines do not support a global this value distinct from the global object: the this value of the scripts is
    /// the global object. So that the window compares equal to it, the window is the global object for the code of its own
    /// realm (e.g. <c>window</c>, <c>self</c>, <c>frames[0].parent</c> from the parent), and the WindowProxy for the code of
    /// the other realms (e.g. the window returned by <c>window.open()</c>, which stays the same object when the popup navigates).
    /// </remarks>
    internal TValue GetWindowValue(Window window, WebIdlRealm<TAdapter, TValue> caller)
    {
        if (ReferenceEquals(caller.Window, window))
            return caller.Global;

        return GetWindowProxy(window);
    }

    /// <summary>
    /// Returns the value of a window (its global object or its WindowProxy, see <see cref="GetWindowValue"/>) for the code of
    /// this realm, or <paramref name="value"/> when it is not a window: the this value of a callback is the one of the realm of the callback.
    /// </summary>
    internal TValue AdaptWindowValue(TValue value)
    {
        if (!Adapter.IsObject(value) || Agent.GetRealmByGlobal(value) is not { Window: { } window })
            return value;

        return GetWindowValue(window, this);
    }

    /// <summary>
    /// Returns the WindowProxy of the browsing context of a window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy):
    /// one object per browsing context, shared by the realms of the agent, whose internal methods are forwarded to the global
    /// object of the active window of the browsing context, so that it is the same object before and after a navigation
    /// (e.g. the window returned by window.open() and the source of the messages of the documents it navigates to).
    /// </summary>
    /// <remarks>
    /// It is a Proxy, used by the code of the other realms (see <see cref="GetWindowValue"/>). The traps are host functions, so that the
    /// realm of the caller stays the current realm (https://html.spec.whatwg.org/multipage/nav-history-apis.html#isplatformobjectsameorigin-(-o-)
    /// uses it): when the active window is not same origin-domain with the caller, the internal methods are the ones of the
    /// cross-origin objects (only the cross-origin properties are visible, the others throw a "SecurityError" DOMException).
    /// The invariants of the Proxy objects require the non-configurable properties of the global object to be properties of
    /// the target of the Proxy: they are copied to the target when they are reported (a property of the global object of a
    /// previous document stays on the target). A window that was never the active window of a browsing context, or that was
    /// replaced by a navigation, has no WindowProxy: its global object is returned.
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

        var agent = Agent;
        var adapter = Adapter;
        TValue? self = null;
        Window ActiveWindow() => instance.ActiveWindow ?? window;
        TValue Global() => agent.GetRealm(ActiveWindow()).Global;
        // The accessors of the global object get the global object as this value, except the __proto__ accessor of
        // Object.prototype, whose [[GetPrototypeOf]] and [[SetPrototypeOf]] are the ones of the WindowProxy
        TValue Receiver(TValue receiver, TValue key) => ReferenceEquals(receiver, self) && !(adapter.IsString(key) && adapter.AsString(key) == "__proto__") ? Global() : receiver;
        var reflect = EvaluateBuiltIn("Reflect");
        TValue Reflect(string name, params TValue[] arguments) => adapter.Call(adapter.Get(reflect, adapter.FromString(name)), adapter.Undefined, arguments);

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#isplatformobjectsameorigin-(-o-), with the realm of the caller
        WebIdlRealm<TAdapter, TValue>? CrossOriginCaller()
        {
            var caller = agent.CurrentRealm;
            var target = ActiveWindow();
            return caller is null || ReferenceEquals(caller.GlobalScope, target) || Document.IsSameOriginDomain(caller.GlobalScope.EnvironmentDocument, target.Document) ? null : caller;
        }

        var handler = CreateObject();
        void Trap(string name, int length, JsHostFunction<TValue> function) => SetMember(handler, name, adapter.CreateFunction(name, length, function));
        Trap("get", 3, (_, arguments) =>
        {
            if (CrossOriginCaller() is not { } caller)
            {
                // The properties whose value is the global object give the WindowProxy (e.g. globalThis)
                var global = Global();
                var value = Reflect("get", global, arguments[1], Receiver(arguments[2], arguments[1]));
                return ReferenceEquals(value, global) ? self! : value;
            }

            // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginget-(-o,-p,-receiver-)
            var descriptor = caller.CrossOriginGetOwnProperty(ActiveWindow(), Global(), arguments[1]);
            if (caller.Adapter.Get(descriptor, caller.Key("get")) is var getter && caller.Adapter.IsObject(getter))
                return caller.Adapter.Call(getter, self!, []);

            if (caller.Adapter.GetOwnProperty(descriptor, caller.Key("value"), out JsPropertyDescriptor<TValue> _))
                return caller.Adapter.Get(descriptor, caller.Key("value"));

            throw caller.CrossOriginSecurityError();
        });
        Trap("set", 4, (_, arguments) =>
        {
            if (CrossOriginCaller() is not { } caller)
                return Reflect("set", Global(), arguments[1], arguments[2], Receiver(arguments[3], arguments[1]));

            // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginset-(-o,-p,-v,-receiver-): only the location setter
            var descriptor = caller.CrossOriginGetOwnProperty(ActiveWindow(), Global(), arguments[1]);
            if (caller.Adapter.Get(descriptor, caller.Key("set")) is var setter && caller.Adapter.IsObject(setter))
            {
                caller.Adapter.Call(setter, self!, [arguments[2]]);
                return adapter.FromBoolean(true);
            }

            throw caller.CrossOriginSecurityError();
        });
        Trap("has", 2, (_, arguments) =>
        {
            if (CrossOriginCaller() is not { } caller)
                return Reflect("has", Global(), arguments[1]);

            caller.CrossOriginGetOwnProperty(ActiveWindow(), Global(), arguments[1]);
            return adapter.FromBoolean(true);
        });
        Trap("deleteProperty", 2, (_, arguments) => CrossOriginCaller() is { } caller ? throw caller.CrossOriginSecurityError() : Reflect("deleteProperty", Global(), arguments[1]));
        Trap("ownKeys", 1, (_, arguments) =>
        {
            if (CrossOriginCaller() is { } caller)
                return caller.CrossOriginOwnPropertyKeys(ActiveWindow());

            // The keys of the global object, and the non-configurable properties mirrored on the target (the invariants of the Proxy objects)
            var keys = Reflect("ownKeys", Global());
            var targetKeys = adapter.OwnKeys(arguments[0]);
            if (targetKeys.Count == 0)
                return keys;

            var result = new List<TValue>();
            var length = (int)adapter.ToNumber(adapter.Get(keys, Key("length")));
            for (var i = 0; i < length; i++)
            {
                result.Add(adapter.Get(keys, adapter.FromString(i.ToString(CultureInfo.InvariantCulture))));
            }

            foreach (var key in targetKeys)
            {
                if (!result.Exists(existing => ReferenceEquals(existing, key) || (adapter.IsString(existing) && adapter.IsString(key) && adapter.AsString(existing) == adapter.AsString(key))))
                {
                    result.Add(key);
                }
            }

            return CreateArray([.. result]);
        });

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getprototypeof: null for a cross-origin caller
        Trap("getPrototypeOf", 1, (_, arguments) => CrossOriginCaller() is not null ? adapter.Null : Reflect("getPrototypeOf", Global()));

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-setprototypeof: SetImmutablePrototype
        Trap("setPrototypeOf", 2, (_, arguments) =>
        {
            var current = CrossOriginCaller() is not null ? adapter.Null : Reflect("getPrototypeOf", Global());
            return adapter.FromBoolean(ReferenceEquals(arguments[1], current) || (adapter.IsNull(arguments[1]) && adapter.IsNull(current)));
        });

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-preventextensions
        Trap("preventExtensions", 1, (_, _) => adapter.FromBoolean(false));
        Trap("getOwnPropertyDescriptor", 2, (_, arguments) =>
        {
            if (CrossOriginCaller() is { } caller)
                return caller.CrossOriginGetOwnProperty(ActiveWindow(), Global(), arguments[1]);

            var descriptor = Reflect("getOwnPropertyDescriptor", Global(), arguments[1]);
            var targetDescriptor = Reflect("getOwnPropertyDescriptor", arguments[0], arguments[1]);
            if (adapter.IsUndefined(descriptor))
                return targetDescriptor;

            if (!adapter.ToBoolean(adapter.Get(descriptor, Key("configurable"))))
            {
                // A non-configurable property (e.g. a [LegacyUnforgeable] attribute, a global variable) is mirrored on the
                // target, unless the target has an incompatible one (a property of the global object of a previous document)
                return adapter.ToBoolean(Reflect("defineProperty", arguments[0], arguments[1], descriptor)) ? descriptor : targetDescriptor;
            }

            return adapter.IsUndefined(targetDescriptor) ? descriptor : targetDescriptor;
        });
        Trap("defineProperty", 3, (_, arguments) =>
        {
            if (CrossOriginCaller() is { } caller)
                throw caller.CrossOriginSecurityError();

            var global = Global();
            if (!adapter.ToBoolean(Reflect("defineProperty", global, arguments[1], arguments[2])))
                return adapter.FromBoolean(false);

            var descriptor = Reflect("getOwnPropertyDescriptor", global, arguments[1]);
            if (!adapter.IsUndefined(descriptor) && !adapter.ToBoolean(adapter.Get(descriptor, Key("configurable"))))
            {
                Reflect("defineProperty", arguments[0], arguments[1], descriptor);
            }

            return adapter.FromBoolean(true);
        });

        var create = EvaluateBuiltIn("(function (handler) { return new Proxy({}, handler); })");
        proxy = Adapter.RunInRealm(EngineRealm, () => adapter.Call(create, adapter.Undefined, [handler]));
        self = proxy;
        Agent.WindowProxies.AddOrUpdate(instance, proxy);
        Agent.WindowProxyBrowsingContexts.AddOrUpdate(proxy, instance);
        return proxy;
    }

    /// <summary>The "SecurityError" DOMException of this realm thrown by the access of a cross-origin property.</summary>
    private Exception CrossOriginSecurityError() => ToJsException(new DomException("Blocked a frame from accessing a cross-origin frame", "SecurityError"));

    /// <summary>
    /// The [[GetOwnProperty]] of a cross-origin WindowProxy (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getownproperty),
    /// for a caller of this realm: a property descriptor of this realm, or a "SecurityError" DOMException.
    /// </summary>
    private TValue CrossOriginGetOwnProperty(Window window, TValue global, TValue key)
    {
        var name = Adapter.IsString(key) ? Adapter.AsString(key) : null;

        // The windows of the document-tree child navigables
        if (name is not null && uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index.ToString(CultureInfo.InvariantCulture) == name)
        {
            if (index < window.Length && window[(int)index] is { } child)
                return CreateDataDescriptor(ToJs(child), writable: false, enumerable: true);

            throw CrossOriginSecurityError();
        }

        if (name is not null && Array.IndexOf(CrossOriginWindowProperties, name) >= 0)
        {
            // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossorigingetownpropertyhelper-(-o,-p-): the
            // functions and the accessors of the cross-origin properties
            for (var current = global; Adapter.IsObject(current); current = Adapter.GetPrototypeOf(current))
            {
                if (!Adapter.GetOwnProperty(current, key, out var original))
                    continue;

                var descriptor = CreateObject();
                if (original.IsAccessorDescriptor)
                {
                    SetMember(descriptor, "get", original.Getter ?? Adapter.Undefined);
                    SetMember(descriptor, "set", name == "location" && original.Setter is { } set ? set : Adapter.Undefined);
                }
                else
                {
                    SetMember(descriptor, "value", original.Value!);
                    SetMember(descriptor, "writable", Adapter.FromBoolean(false));
                }

                SetMember(descriptor, "enumerable", Adapter.FromBoolean(false));
                SetMember(descriptor, "configurable", Adapter.FromBoolean(true));
                return descriptor;
            }
        }

        // The first child navigable with the target name. The specification only exposes the names of the document-tree child
        // navigable target name property set (the children that are same origin with the document of the window), but the
        // browsers expose every named child to a cross-origin caller (e.g. a frame reaches its sibling with parent.name
        // when their parent is cross-origin), which the web-platform-tests rely on.
        if (name is not null && window.GetFirstNamedChildNavigable(name, out _) is { } namedChild)
            return CreateDataDescriptor(GetWindowProxy(namedChild.Window), writable: false, enumerable: false);

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginpropertyfallback-(-p-)
        if (name == "then" || IsCrossOriginFallbackSymbol(key))
            return CreateDataDescriptor(Adapter.Undefined, writable: false, enumerable: false);

        throw CrossOriginSecurityError();
    }

    private bool IsCrossOriginFallbackSymbol(TValue key)
    {
        if (!Adapter.IsSymbol(key))
            return false;

        return ReferenceEquals(key, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag))
            || ReferenceEquals(key, _hasInstanceSymbol ??= EvaluateBuiltIn("Symbol.hasInstance"))
            || ReferenceEquals(key, _isConcatSpreadableSymbol ??= EvaluateBuiltIn("Symbol.isConcatSpreadable"));
    }

    private TValue? _hasInstanceSymbol;
    private TValue? _isConcatSpreadableSymbol;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-ownpropertykeys for a cross-origin caller:
    /// the indices of the child navigables, the cross-origin properties, the target names of the child navigables, then and
    /// the well-known symbols of CrossOriginPropertyFallback.
    /// </summary>
    private TValue CrossOriginOwnPropertyKeys(Window window)
    {
        var keys = new List<TValue>();
        for (var i = 0; i < window.Length; i++)
        {
            keys.Add(Adapter.FromString(i.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var name in CrossOriginWindowProperties)
        {
            keys.Add(Key(name));
        }

        foreach (var child in window.Document.GetDocumentTreeChildNavigables())
        {
            if (child.Name.Length > 0 && !keys.Exists(key => Adapter.AsString(key) == child.Name))
            {
                keys.Add(Key(child.Name));
            }
        }

        keys.Add(Key("then"));
        keys.Add(Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag));
        keys.Add(_hasInstanceSymbol ??= EvaluateBuiltIn("Symbol.hasInstance"));
        keys.Add(_isConcatSpreadableSymbol ??= EvaluateBuiltIn("Symbol.isConcatSpreadable"));
        return CreateArray([.. keys]);
    }

    private TValue CreateDataDescriptor(TValue value, bool writable, bool enumerable)
    {
        var descriptor = CreateObject();
        SetMember(descriptor, "value", value);
        SetMember(descriptor, "writable", Adapter.FromBoolean(writable));
        SetMember(descriptor, "enumerable", Adapter.FromBoolean(enumerable));
        SetMember(descriptor, "configurable", Adapter.FromBoolean(true));
        return descriptor;
    }
}
