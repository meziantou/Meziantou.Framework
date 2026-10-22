using System.Diagnostics.CodeAnalysis;
using System.Collections;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The realm of a global object (https://html.spec.whatwg.org/multipage/webappapis.html#realms-settings-objects-global-objects):
/// its global object is a window or the global object of a worker, and its interface objects expose the interfaces of the
/// platform that are exposed in the global object. The interface objects of a realm are created on first use.
/// </summary>
/// <typeparam name="TAdapter">The adapter of the JavaScript engine.</typeparam>
/// <typeparam name="TValue">The type of the JavaScript values of the engine.</typeparam>
public sealed partial class WebIdlRealm<TAdapter, TValue>
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    /// <summary>The getter of an attribute.</summary>
    internal delegate TValue Getter(WebIdlRealm<TAdapter, TValue> realm, TValue thisValue);

    /// <summary>The setter of an attribute.</summary>
    internal delegate void Setter(WebIdlRealm<TAdapter, TValue> realm, TValue thisValue, TValue value);

    /// <summary>An operation: converts the arguments, invokes the .NET method and converts its result.</summary>
    internal delegate TValue Operation(WebIdlRealm<TAdapter, TValue> realm, TValue thisValue, ReadOnlySpan<TValue> arguments);

    /// <summary>The constructor steps of an interface: returns the new .NET object.</summary>
    internal delegate object ConstructorSteps(WebIdlRealm<TAdapter, TValue> realm, ReadOnlySpan<TValue> arguments);

    // Creates an interface object (https://webidl.spec.whatwg.org/#interface-object): a constructor calling construct(NewTarget, args)
    private const string MakeInterfaceScript = """
            (function (name, length, construct, parent, prototype, htmlConstructor, classString) {
              // Strict: the function has no own caller and arguments properties (an extension of the engines for the
              // sloppy functions), as the built-in function objects
              'use strict';
              let f = function (...args) { return construct(new.target, args); };
              Object.defineProperty(f, 'length', { value: length });
              Object.defineProperty(f, 'name', { value: name });
              Object.defineProperty(f, 'prototype', { value: prototype, writable: false, enumerable: false, configurable: false });
              if (parent) Object.setPrototypeOf(f, parent);

              // The [[Construct]] of an ordinary function gets NewTarget.prototype before running the function: the
              // HTML element constructors must get it only once, after their checks
              if (htmlConstructor) f = new Proxy(f, { construct: (target, args, newTarget) => construct(newTarget, args) });
              Object.defineProperty(prototype, 'constructor', { value: f, writable: true, enumerable: false, configurable: true });
              Object.defineProperty(prototype, Symbol.toStringTag, { value: classString, writable: false, enumerable: false, configurable: true });
              return f;
            })
            """;

    private readonly Dictionary<string, (TValue InterfaceObject, TValue Prototype)> _interfaceObjects = new(StringComparer.Ordinal);
    private TValue? _makeInterface;
    private TValue? _syncFrames;
    private TValue? _createPromiseCapability;
    private TValue? _markAsHandled;
    private TValue? _waitForAll;
    private TValue? _jsonParse;
    private TValue? _reflectSet;
    private TValue? _toPrimitiveSymbol;
    private readonly Dictionary<InterfaceBinding<TAdapter, TValue>, TValue> _unforgeableHolders = [];

    internal WebIdlRealm(WebIdlAgent<TAdapter, TValue> agent, GlobalScope global, object engineRealm)
    {
        Agent = agent;
        Adapter = agent.Adapter;
        GlobalScope = global;
        EngineRealm = engineRealm;
        Global = Adapter.GetGlobalObject(engineRealm);
        ObjectPrototype = EvaluateBuiltIn("Object.prototype");
        FunctionPrototype = EvaluateBuiltIn("Function.prototype");
    }

    /// <summary>Gets the bindings of the engine.</summary>
    public WebIdlAgent<TAdapter, TValue> Agent { get; }

    /// <summary>Gets the adapter of the engine.</summary>
    public TAdapter Adapter { get; }

    /// <summary>Gets the .NET global object (a window or the global object of a worker), whose JavaScript object is the global object of the realm.</summary>
    public GlobalScope GlobalScope { get; }

    /// <summary>Gets the window when the global object of the realm is a window, else null.</summary>
    public Window? Window => GlobalScope as Window;

    /// <summary>Gets the realm of the engine.</summary>
    public object EngineRealm { get; }

    /// <summary>Gets the global object of the realm.</summary>
    public TValue Global { get; }

    /// <summary>Gets <c>Object.prototype</c> of the realm.</summary>
    public TValue ObjectPrototype { get; }

    /// <summary>Gets <c>Function.prototype</c> of the realm.</summary>
    public TValue FunctionPrototype { get; }

    /// <summary>
    /// Whether the realm is a secure context (https://html.spec.whatwg.org/multipage/webappapis.html#secure-context): the
    /// [SecureContext] interfaces and members are only exposed in a secure context (https://webidl.spec.whatwg.org/#SecureContext).
    /// </summary>
    internal bool IsSecureContext => GlobalScope.IsSecureContext;

    /// <summary>The kind of the global object of the realm, to check whether the interfaces and the members are exposed (https://webidl.spec.whatwg.org/#dfn-exposed).</summary>
    internal ExposureSet GlobalExposure => GlobalScope.Exposure;

    /// <summary>Whether an interface or a member whose exposure set is <paramref name="exposure"/> is exposed in the realm (https://webidl.spec.whatwg.org/#dfn-exposed).</summary>
    internal bool IsExposed(ExposureSet exposure) => (exposure & GlobalExposure) != ExposureSet.None;

    /// <summary>Evaluates code in the realm, as an indirect eval. It does not run the pending jobs.</summary>
    public TValue Evaluate(string code) => Adapter.Evaluate(EngineRealm, code);

    /// <summary>Evaluates a script of the implementation (a constant source text) in the realm, as <see cref="Evaluate"/>: its parse is shared by the realms.</summary>
    internal TValue EvaluateBuiltIn(string code) => Adapter.EvaluateBuiltIn(EngineRealm, code);

    /// <summary>Runs code with this realm as the current realm, and its global object as the current global object.</summary>
    public TResult Run<TResult>(Func<TResult> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previousDocument = Document.CurrentGlobalDocumentOrNull;
        var previousEntryDocument = Document.EntryGlobalDocument;
        Document.CurrentGlobalDocument = GlobalScope.EnvironmentDocument;
        Document.EntryGlobalDocument = GlobalScope.EnvironmentDocument;
        try
        {
            return Adapter.RunInRealm(EngineRealm, action);
        }
        finally
        {
            Document.CurrentGlobalDocumentOrNull = previousDocument;
            Document.EntryGlobalDocument = previousEntryDocument;
        }
    }

    internal TValue Key(string name) => Adapter.FromString(name);

    // ----------------------------------------------------------------------------------------------------------------
    // Interface objects
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The %SharedArrayBuffer% intrinsic of the realm, or null when the engine does not support it.</summary>
    internal TValue? SharedArrayBufferConstructor { get; private set; }

    /// <summary>
    /// Defines the interface objects exposed in the global object, the legacy callback interface objects, the legacy factory
    /// functions, and the prototype chain of the global object.
    /// </summary>
    internal void Initialize()
    {
        // https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-new-javascript-realm: the SharedArrayBuffer
        // property is deleted when the agent cluster is not cross-origin isolated (the intrinsic constructor is kept for the
        // structured serialization)
        SharedArrayBufferConstructor = Adapter.Get(Global, Key("SharedArrayBuffer")) is var sharedArrayBuffer && Adapter.IsCallable(sharedArrayBuffer) ? sharedArrayBuffer : null;
        if (!GlobalScope.IsAgentClusterCrossOriginIsolated)
        {
            Adapter.Delete(Global, Key("SharedArrayBuffer"));
        }

        // The schedulers are exposed in the windows and in the workers: the realms of the worklets, which run on the event loop
        // of their document, keep their Promise.prototype.then
        var isWindowOrWorker = IsExposed(ExposureSet.Window | ExposureSet.DedicatedWorker | ExposureSet.SharedWorker | ExposureSet.ServiceWorker);
        if (Features.PrioritizedTaskSchedulingEnabled && isWindowOrWorker)
        {
            DefineContinuationStatePropagation();
        }

        // The global object gets an interface object per exposed interface, and the members of its interface
        Adapter.ReserveProperties(Global, WebIdlBindings<TAdapter, TValue>.Interfaces.Count + 256);

        var isSecureContext = IsSecureContext;
        foreach (var binding in WebIdlBindings<TAdapter, TValue>.Interfaces.Values)
        {
            // https://webidl.spec.whatwg.org/#dfn-exposed: an interface is exposed in the global objects of its exposure set,
            // and a [SecureContext] interface is only exposed in a secure context
            // and the interfaces of the disabled features are not exposed
            if (!IsExposed(binding.Exposure) || (!isSecureContext && binding.Flags.HasFlag(InterfaceBindingFlags.SecureContext)) || !IsInterfaceEnabled(binding.Name))
                continue;

            DefineLazyGlobal(binding.Name, binding);
        }

        // The streams, the console and WebAssembly are exposed in every global object ([Exposed=*]); fetch, the Web
        // Cryptography API and the Cache API in the windows and the workers, not in the worklets
        DefineStreamGlobals();
        if (Features.FetchEnabled && isWindowOrWorker)
        {
            DefineFetchGlobals();
        }

        if (Features.WebCryptoEnabled && isWindowOrWorker)
        {
            DefineCryptoGlobals();
        }

        // The Cache API stores the Request and Response objects of Fetch.js ([SecureContext])
        if (Features.CacheStorageEnabled && Features.FetchEnabled && isSecureContext && isWindowOrWorker)
        {
            DefineCacheStorageGlobals();
        }

        // FileSystemWritableFileStream is a WritableStream of Streams.js ([SecureContext, Exposed=(Window,Worker)])
        if (Features.FileSystemEnabled && Features.StorageManagerEnabled && isSecureContext && isWindowOrWorker && IsInterfaceEnabled("WritableStream"))
        {
            DefineFileSystemGlobals();
        }

        DefineConsoleGlobal();
        DefineWebAssemblyGlobal();
        if (Window is null)
        {
            // The global object of a worker implements the interface of its kind (e.g. DedicatedWorkerGlobalScope)
            Adapter.SetPrototypeOf(Global, GetPrototype(GlobalScope.InterfaceName));
            DefineImmutablePrototypes();
            return;
        }

        // https://webidl.spec.whatwg.org/#LegacyWindowAlias
        foreach (var (alias, target) in WebIdlBindings<TAdapter, TValue>.LegacyWindowAliases)
        {
            if (WebIdlBindings<TAdapter, TValue>.Interfaces.TryGetValue(target, out var binding) && IsInterfaceEnabled(target))
            {
                DefineLazyGlobal(alias, binding);
            }
        }

        // https://html.spec.whatwg.org/multipage/dom.html#htmldocument: window.HTMLDocument is the Document interface object
        if (WebIdlBindings<TAdapter, TValue>.Interfaces.TryGetValue("Document", out var documentBinding))
        {
            DefineLazyGlobal("HTMLDocument", documentBinding);
        }

        // The legacy callback interfaces and the legacy factory functions are exposed in the windows (e.g. NodeFilter, Option)
        WebIdlBindings<TAdapter, TValue>.DefineLegacyGlobals(this);

        // The global object implements Window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-window-object).
        // Its prototype chain includes the named properties object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#named-access-on-the-window-object).
        var windowPrototype = GetPrototype("Window");
        Adapter.SetPrototypeOf(windowPrototype, CreateNamedPropertiesObject("Window", Adapter.GetPrototypeOf(windowPrototype)));
        Adapter.SetPrototypeOf(Global, windowPrototype);
        DefineImmutablePrototypes();
    }

    /// <summary>
    /// https://webidl.spec.whatwg.org/#es-immutable-prototype: the global object of a [Global] interface, its interface
    /// prototype object and the interface prototype objects of its inherited interfaces are immutable prototype exotic
    /// objects. An engine that supports them marks the objects (<see cref="IJsAdapter{TValue}.TryMakePrototypeImmutable"/>).
    /// The other engines do not let the host define the [[SetPrototypeOf]] internal method of these objects, so the
    /// built-in functions that call it (<c>Object.setPrototypeOf()</c>, <c>Reflect.setPrototypeOf()</c> and the setter of
    /// <c>Object.prototype.__proto__</c>) are replaced in the realm by functions that implement SetImmutablePrototype
    /// (https://tc39.es/ecma262/#sec-set-immutable-prototype) for the objects of the prototype chains of the global objects
    /// of the agent, and call the built-in functions for the other objects.
    /// </summary>
    private void DefineImmutablePrototypes()
    {
        // An engine that supports the immutable prototype exotic objects marks the objects of the prototype chain (the
        // named properties object is a proxy whose setPrototypeOf trap implements SetImmutablePrototype)
        if (Adapter.TryMakePrototypeImmutable(Global))
        {
            for (var prototype = Adapter.GetPrototypeOf(Global); !Adapter.IsNull(prototype); prototype = Adapter.GetPrototypeOf(prototype))
            {
                Adapter.TryMakePrototypeImmutable(prototype);
            }

            return;
        }

        var adapter = Adapter;
        var objects = Agent.ImmutablePrototypeObjects ??= EvaluateBuiltIn("new WeakSet()");
        var support = Adapter.Call(EvaluateBuiltIn("""
            (function (objects) {
              'use strict';
              const { setPrototypeOf, getPrototypeOf, getOwnPropertyDescriptor, prototype: objectPrototype } = Object;
              const { apply, setPrototypeOf: reflectSetPrototypeOf } = Reflect;
              const protoSetter = getOwnPropertyDescriptor(objectPrototype, '__proto__').set;
              const weakSetHas = WeakSet.prototype.has;
              const weakSetAdd = WeakSet.prototype.add;
              const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';
              const isImmutable = (value) => isObject(value) && apply(weakSetHas, objects, [value]);
              const isPrototype = (value) => value === null || isObject(value);
              return {
                mark(object) {
                  for (; object !== null && object !== objectPrototype; object = getPrototypeOf(object)) apply(weakSetAdd, objects, [object]);
                },
                objectSetPrototypeOf(object, prototype) {
                  if (!isImmutable(object) || !isPrototype(prototype)) return apply(setPrototypeOf, this, arguments);
                  if (prototype !== getPrototypeOf(object)) throw new TypeError('The prototype of an immutable prototype object cannot be changed');
                  return object;
                },
                reflectSetPrototypeOf(target, prototype) {
                  if (!isImmutable(target) || !isPrototype(prototype)) return apply(reflectSetPrototypeOf, this, arguments);
                  return prototype === getPrototypeOf(target);
                },
                protoSetter(prototype) {
                  if (!isImmutable(this) || !isPrototype(prototype)) return apply(protoSetter, this, arguments);
                  if (prototype !== getPrototypeOf(this)) throw new TypeError('The prototype of an immutable prototype object cannot be changed');
                  return undefined;
                },
              };
            })
            """), Adapter.Undefined, [objects]);
        Adapter.Call(Adapter.Get(support, Key("mark")), Adapter.Undefined, [Global]);

        // The functions of the realm are built-in function objects (their source text is not exposed)
        TValue CreateBuiltIn(string name, int length, string implementation)
        {
            var function = adapter.Get(support, Key(implementation));
            return adapter.CreateFunction(name, length, (thisValue, arguments) => adapter.Call(function, thisValue, arguments));
        }

        Adapter.DefineOwnProperty(EvaluateBuiltIn("Object"), Key("setPrototypeOf"), JsPropertyDescriptor<TValue>.Data(CreateBuiltIn("setPrototypeOf", 2, "objectSetPrototypeOf"), writable: true, enumerable: false, configurable: true));
        Adapter.DefineOwnProperty(EvaluateBuiltIn("Reflect"), Key("setPrototypeOf"), JsPropertyDescriptor<TValue>.Data(CreateBuiltIn("setPrototypeOf", 2, "reflectSetPrototypeOf"), writable: true, enumerable: false, configurable: true));
        var objectPrototype = EvaluateBuiltIn("Object.prototype");
        if (Adapter.GetOwnProperty(objectPrototype, Key("__proto__"), out var protoDescriptor) && protoDescriptor.Getter is { } protoGetter)
        {
            Adapter.DefineOwnProperty(objectPrototype, Key("__proto__"), JsPropertyDescriptor<TValue>.Accessor(protoGetter, CreateBuiltIn("set __proto__", 1, "protoSetter"), enumerable: false, configurable: true));
        }
    }

    /// <summary>
    /// Creates the named properties object of a [Global] interface (https://webidl.spec.whatwg.org/#named-properties-object),
    /// which exposes the named properties of the global object (e.g. the elements with an id) and sits between the interface
    /// prototype object and the prototype of the inherited interface. It is a Proxy whose target is an ordinary object with
    /// the class string: the engines do not support the immutable prototype of the host objects. The traps called by the
    /// lookups of the global variables are .NET functions, so that no JavaScript code runs when a variable is resolved (the
    /// location of a ReferenceError is the location of the script).
    /// </summary>
    private TValue CreateNamedPropertiesObject(string interfaceName, TValue prototype)
    {
        var target = Adapter.CreateObject(prototype);
        Adapter.DefineOwnProperty(target, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag), JsPropertyDescriptor<TValue>.Data(Key(interfaceName + "Properties"), writable: false, enumerable: false, configurable: true));

        // The value of the named property, or null when it is not supported or is shadowed by a property of the prototype
        // chain of the named properties object. https://webidl.spec.whatwg.org/#dfn-named-property-visibility also hides
        // the named properties shadowed by an own property of the global object or of the interface prototype object,
        // but the browsers expose them on the named properties object (the lookups from the global object find the
        // shadowing property first anyway), which the web-platform-tests expect.
        TValue? GetVisibleNamedProperty(TValue key)
        {
            if (!Adapter.IsString(key))
                return null;

            var name = Adapter.AsString(key);
            if (Window!.GetNamedItem(name) is not { } value)
                return null;

            for (var current = prototype; !Adapter.IsNull(current); current = Adapter.GetPrototypeOf(current))
            {
                if (Adapter.GetOwnProperty(current, key, out _))
                    return null;
            }

            return ToJs(value);
        }

        TValue Trap(string name, int length, JsHostFunction<TValue> function) => Adapter.CreateFunction(name, length, (thisValue, arguments) =>
        {
            try
            {
                return function(thisValue, arguments);
            }
            catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }
        });

        var reflectGet = EvaluateBuiltIn("Reflect.get");
        var reflectGetOwnPropertyDescriptor = EvaluateBuiltIn("Reflect.getOwnPropertyDescriptor");
        var visible = Trap("getVisibleNamedProperty", 1, (_, arguments) => GetVisibleNamedProperty(arguments[0]) ?? Adapter.Undefined);
        var has = Trap("has", 2, (_, arguments) => Adapter.FromBoolean(GetVisibleNamedProperty(arguments[1]) is not null || Adapter.HasProperty(arguments[0], arguments[1])));
        var get = Trap("get", 3, (_, arguments) => GetVisibleNamedProperty(arguments[1]) ?? Adapter.Call(reflectGet, Adapter.Undefined, arguments));
        var getOwnPropertyDescriptor = Trap("getOwnPropertyDescriptor", 2, (_, arguments) =>
        {
            if (GetVisibleNamedProperty(arguments[1]) is not { } value)
                return Adapter.Call(reflectGetOwnPropertyDescriptor, Adapter.Undefined, arguments);

            // [LegacyUnenumerableNamedProperties]: the named properties are not enumerable
            var descriptor = CreateObject();
            SetMember(descriptor, "value", value);
            SetMember(descriptor, "writable", Adapter.FromBoolean(true));
            SetMember(descriptor, "enumerable", Adapter.FromBoolean(false));
            SetMember(descriptor, "configurable", Adapter.FromBoolean(true));
            return descriptor;
        });

        var create = EvaluateBuiltIn("""
            (function (target, getVisibleNamedProperty, has, get, getOwnPropertyDescriptor) {
              const { defineProperty, getOwnPropertyDescriptor: getOwnPropertyDescriptorOf, getPrototypeOf, set } = Reflect;
              const hasOwn = Object.hasOwn;
              return new Proxy(target, {
                getOwnPropertyDescriptor,
                has,
                get,
                set(t, key, value, receiver) {
                  if (getVisibleNamedProperty(key) === undefined)
                    return set(t, key, value, receiver);

                  // OrdinarySetWithOwnDescriptor with the writable data property of the named property
                  if (receiver === null || (typeof receiver !== "object" && typeof receiver !== "function"))
                    return false;

                  const existing = getOwnPropertyDescriptorOf(receiver, key);
                  if (existing === undefined)
                    return defineProperty(receiver, key, { __proto__: null, value, writable: true, enumerable: true, configurable: true });

                  if (hasOwn(existing, "get") || !existing.writable)
                    return false;

                  return defineProperty(receiver, key, { __proto__: null, value });
                },
                defineProperty() { return false; },
                deleteProperty() { return false; },
                preventExtensions() { return false; },
                // SetImmutablePrototype
                setPrototypeOf(t, prototype) { return prototype === getPrototypeOf(t); },
              });
            })
            """);
        return Adapter.Call(create, Adapter.Undefined, [target, visible, has, get, getOwnPropertyDescriptor]);
    }

    private void DefineLazyGlobal(string name, InterfaceBinding<TAdapter, TValue> binding)
    {
        Adapter.DefineLazyDataProperty(Global, Key(name), new LazyInterfaceObject(this, binding), writable: true, enumerable: false, configurable: true);
    }

    /// <summary>The interface object of a global property, created on first access (a realm has hundreds of them).</summary>
    private sealed class LazyInterfaceObject(WebIdlRealm<TAdapter, TValue> realm, InterfaceBinding<TAdapter, TValue> binding) : IJsLazyValue<TValue>
    {
        public TValue CreateValue() => realm.EnsureInterfaceObject(binding).InterfaceObject;
    }

    /// <summary>Returns the interface prototype object of an interface, creating it in this realm.</summary>
    public TValue GetPrototype(string interfaceName) => EnsureInterfaceObject(WebIdlBindings<TAdapter, TValue>.Interfaces[interfaceName]).Prototype;

    /// <summary>Returns the interface object of an interface, creating it in this realm.</summary>
    public TValue GetInterfaceObject(string interfaceName) => EnsureInterfaceObject(WebIdlBindings<TAdapter, TValue>.Interfaces[interfaceName]).InterfaceObject;

    internal (TValue InterfaceObject, TValue Prototype) EnsureInterfaceObject(InterfaceBinding<TAdapter, TValue> binding)
    {
        if (_interfaceObjects.TryGetValue(binding.ClassString ?? binding.Name, out var existing))
            return existing;

        // The objects must be created in this realm, whichever realm is running
        return Adapter.RunInRealm(EngineRealm, () => CreateInterfaceObject(binding));
    }

    private (TValue InterfaceObject, TValue Prototype) CreateInterfaceObject(InterfaceBinding<TAdapter, TValue> binding)
    {
        if (_interfaceObjects.TryGetValue(binding.ClassString ?? binding.Name, out var existing))
            return existing;

        // https://webidl.spec.whatwg.org/#namespace-object: an ordinary object with the operations and attributes
        if ((binding.Flags & InterfaceBindingFlags.Namespace) != InterfaceBindingFlags.None)
        {
            var namespaceObject = Adapter.CreateObject(ObjectPrototype);
            ReserveLearnedProperties(namespaceObject, binding.PrototypePropertyCount);
            Adapter.DefineOwnProperty(namespaceObject, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag), JsPropertyDescriptor<TValue>.Data(Key(binding.Name), writable: false, enumerable: false, configurable: true));
            _interfaceObjects[binding.Name] = (namespaceObject, namespaceObject);
            var previousDefiningNamespace = _definingInterface;
            _definingInterface = binding.Name;
            try
            {
                binding.DefineMembers(this, namespaceObject, namespaceObject, namespaceObject);
            }
            finally
            {
                _definingInterface = previousDefiningNamespace;
            }

            if (binding.Name == "CSS")
            {
                DefineCssParserStreamSources(namespaceObject);
            }

            binding.PrototypePropertyCount = Math.Max(binding.PrototypePropertyCount, Adapter.GetPropertyCount(namespaceObject));
            return (namespaceObject, namespaceObject);
        }

        var parentInterfaceObject = Adapter.Undefined;
        TValue parentPrototype;
        if (binding.Parent is not null)
        {
            (parentInterfaceObject, parentPrototype) = CreateInterfaceObject(binding.Parent);
        }
        else if (binding.Name == "DOMException")
        {
            // https://webidl.spec.whatwg.org/#es-DOMException-specialness
            parentPrototype = EvaluateBuiltIn("Error.prototype");
        }
#pragma warning disable WEBPLATEXP0014 // The source phase imports are experimental for the consumers of the package only
        else if ((binding.Flags & InterfaceBindingFlags.ModuleSource) != InterfaceBindingFlags.None && Adapter.GetAbstractModuleSource() is { } abstractModuleSource)
#pragma warning restore WEBPLATEXP0014
        {
            // https://webassembly.github.io/esm-integration/js-api/index.html#webassembly-module-objects: WebAssembly.Module extends %AbstractModuleSource%
            parentInterfaceObject = abstractModuleSource;
            parentPrototype = Adapter.Get(abstractModuleSource, Key("prototype"));
        }
        else
        {
            parentPrototype = ObjectPrototype;
        }

        // The interface objects and the prototypes get their properties after their creation: their storage is allocated once
        // with the sizes that the previous realms learned
        var prototype = Adapter.CreateObject(parentPrototype);
        ReserveLearnedProperties(prototype, binding.PrototypePropertyCount);
        var construct = Adapter.CreateFunction("construct", 2, (_, arguments) => Construct(binding, arguments[0], arguments[1]));
        var isHtmlConstructor = (binding.Flags & InterfaceBindingFlags.HtmlConstructor) != InterfaceBindingFlags.None;
        var interfaceObject = Adapter.Call(_makeInterface ??= EvaluateBuiltIn(MakeInterfaceScript), Adapter.Undefined, [Key(binding.Name), Adapter.FromNumber(binding.ConstructorLength), construct, parentInterfaceObject, prototype, Adapter.FromBoolean(isHtmlConstructor), Key(binding.ClassString ?? binding.Name)]);
        _interfaceObjects[binding.ClassString ?? binding.Name] = (interfaceObject, prototype);
        ReserveLearnedProperties(interfaceObject, binding.InterfaceObjectPropertyCount);

        // https://webidl.spec.whatwg.org/#Global: the regular attributes and operations of a global interface are properties of the global object
        var instanceTarget = (binding.Flags & InterfaceBindingFlags.Global) != InterfaceBindingFlags.None ? Global : prototype;
        var previousDefiningInterface = _definingInterface;
        _definingInterface = binding.Name;
        try
        {
            binding.DefineMembers(this, interfaceObject, prototype, instanceTarget);
        }
        finally
        {
            _definingInterface = previousDefiningInterface;
        }

        if (binding.Name == "DOMException")
        {
            // DOMException objects have a stack property in browsers, which testharness.js reads to format them
            var defineStack = EvaluateBuiltIn("""
                (function (prototype) {
                  Object.defineProperty(prototype, 'stack', { get() { return this instanceof DOMException ? this.name + ': ' + this.message : undefined; }, configurable: true });
                })
                """);
            Adapter.Call(defineStack, Adapter.Undefined, [prototype]);
        }
        else if (binding.Name == "Blob")
        {
            DefineBlobStreamMethods(prototype);
        }
        else if (binding.Name is "Element" or "ShadowRoot" or "CharacterData")
        {
            DefineHtmlStreamingMethods(prototype, binding.Name);
        }
        else if (binding.Name is "FileSystemHandle" or "FileSystemFileHandle" or "FileSystemDirectoryHandle" or "FileSystemSyncAccessHandle")
        {
            DefineFileSystemMembers(prototype, binding.Name);
        }
        else if (binding.Name == "HTMLTemplateElement")
        {
            DefineTemplatePatchingMembers(prototype);
        }

        binding.InterfaceObjectPropertyCount = Math.Max(binding.InterfaceObjectPropertyCount, Adapter.GetPropertyCount(interfaceObject));
        binding.PrototypePropertyCount = Math.Max(binding.PrototypePropertyCount, Adapter.GetPropertyCount(prototype));
        return (interfaceObject, prototype);
    }

    /// <summary>Prepares an object for the number of properties that the same object had in the previous realms (0: not learned yet).</summary>
    private void ReserveLearnedProperties(TValue obj, int learnedCount)
    {
        var count = learnedCount - Adapter.GetPropertyCount(obj);
        if (count > 0)
        {
            Adapter.ReserveProperties(obj, count);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Members (the generated bindings call these methods)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Defines the tentative members of <c>HTMLTemplateElement</c> of the declarative partial updates
    /// (https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md), which are not in the IDL
    /// of the HTML Standard: <c>buffer</c>, which reflects the <c>buffer</c> attribute ([CEReactions, Reflect] boolean).
    /// </summary>
    private void DefineTemplatePatchingMembers(TValue prototype)
    {
        var previousDefiningInterface = _definingInterface;
        _definingInterface = "HTMLTemplateElement";
        try
        {
            DefineAttribute(prototype, "buffer",
                static (realm, thisValue) => realm.Adapter.FromBoolean(((HtmlTemplateElement)realm.This(thisValue, Generated.Binding_HTMLTemplateElement<TAdapter, TValue>.Binding)).Buffer),
                static (realm, thisValue, value) => ((HtmlTemplateElement)realm.This(thisValue, Generated.Binding_HTMLTemplateElement<TAdapter, TValue>.Binding)).Buffer = realm.ToBoolean(value));
        }
        finally
        {
            _definingInterface = previousDefiningInterface;
        }
    }

    /// <summary>https://webidl.spec.whatwg.org/#es-constants: defined on the interface object and the prototype.</summary>
    internal void DefineConstant(TValue interfaceObject, TValue prototype, string name, TValue value)
    {
        var descriptor = JsPropertyDescriptor<TValue>.Data(value, writable: false, enumerable: true, configurable: false);
        Adapter.DefineOwnProperty(interfaceObject, Key(name), descriptor);
        if (!ReferenceEquals(interfaceObject, prototype))
        {
            Adapter.DefineOwnProperty(prototype, Key(name), descriptor);
        }
    }

    /// <summary>https://webidl.spec.whatwg.org/#es-attributes</summary>
    internal void DefineAttribute(TValue target, string name, Getter getter, Setter? setter, bool enumerable = true, bool configurable = true, bool returnsPromise = false)
    {
        if (!IsMemberEnabled(_definingInterface, name))
            return;

        if (_definingInterface is { } interfaceName)
        {
            if (IsCrossOriginProperty(interfaceName, name, CrossOriginAccess.Get))
            {
                var crossOriginGetter = getter;
                getter = (realm, thisValue) => AllowCrossOriginAccess(() => crossOriginGetter(realm, thisValue));
            }

            if (setter is not null && IsCrossOriginProperty(interfaceName, name, CrossOriginAccess.Set))
            {
                var crossOriginSetter = setter;
                setter = (realm, thisValue, value) => AllowCrossOriginAccess(() =>
                {
                    crossOriginSetter(realm, thisValue, value);
                    return true;
                });
            }
        }

        // Most attributes are never used: their functions are created on first access, in this realm
        Adapter.DefineLazyAccessorProperty(target, Key(name), new LazyAttribute(this, name, getter, setter, returnsPromise), hasSetter: setter is not null, enumerable, configurable);
    }

    /// <summary>The functions of an attribute, created on first access in the realm (one object per attribute, instead of closures).</summary>
    private sealed class LazyAttribute(WebIdlRealm<TAdapter, TValue> realm, string name, Getter getter, Setter? setter, bool returnsPromise) : IJsLazyAccessor<TValue>
    {
        public TValue CreateGetter() => realm.Adapter.RunInRealm(realm.EngineRealm, CreateGetterInRealm);

        public TValue CreateSetter() => realm.Adapter.RunInRealm(realm.EngineRealm, CreateSetterInRealm);

        // https://webidl.spec.whatwg.org/#dfn-attribute-getter: the getter of a promise attribute returns a rejected promise
        // instead of throwing (e.g. when this is not a platform object)
        private TValue CreateGetterInRealm() => returnsPromise
            ? realm.Adapter.CreateFunction("get " + name, 0, (thisValue, _) => realm.InvokePromiseGetter(getter, thisValue))
            : realm.Adapter.CreateFunction("get " + name, 0, (thisValue, _) => realm.InvokeGetter(getter, thisValue));

        private TValue CreateSetterInRealm()
        {
            var attributeSetter = setter!;
            return realm.Adapter.CreateFunction("set " + name, 1, (thisValue, arguments) =>
            {
                realm.InvokeSetter(attributeSetter, thisValue, arguments.Length > 0 ? arguments[0] : realm.Adapter.Undefined);
                return realm.Adapter.Undefined;
            });
        }
    }

    /// <summary>A readonly attribute with [PutForwards] (https://webidl.spec.whatwg.org/#PutForwards).</summary>
    internal void DefinePutForwardsAttribute(TValue target, string name, Getter getter, string forwardName, bool configurable = true)
    {
        DefineAttribute(target, name, getter, (realm, thisValue, value) =>
        {
            // https://webidl.spec.whatwg.org/#dfn-attribute-setter: the this value must implement the interface (the getter
            // checks it), then the assignment is forwarded to the value of the attribute, as returned by [[Get]] (which may
            // be an overridden getter). Set(Q, forwardId, V, false) does not throw when the assignment fails.
            realm.InvokeGetter(getter, thisValue);
            var forward = realm.Adapter.Get(realm.ThisOrGlobal(thisValue), realm.Key(name));
            if (!realm.Adapter.IsObject(forward))
                throw realm.CreateTypeError("Cannot forward the assignment");

            realm.Adapter.Call(realm.ReflectSet, realm.Adapter.Undefined, [forward, realm.Key(forwardName), value]);
        }, configurable: configurable);
    }

    /// <summary>A readonly attribute with [LegacyLenientSetter] (https://webidl.spec.whatwg.org/#LegacyLenientSetter): its setter does nothing.</summary>
    internal void DefineLenientSetterAttribute(TValue target, string name, Getter getter, bool configurable = true)
    {
        // https://webidl.spec.whatwg.org/#dfn-attribute-setter: the this value must implement the interface (the getter
        // checks it), then "If attribute is declared with the [LegacyLenientSetter] extended attribute, then return undefined"
        DefineAttribute(target, name, getter, (realm, thisValue, _) => realm.InvokeGetter(getter, thisValue), configurable: configurable);
    }

    /// <summary>A readonly attribute with [Replaceable] (https://webidl.spec.whatwg.org/#Replaceable).</summary>
    internal void DefineReplaceableAttribute(TValue target, string name, Getter getter, bool configurable = true)
    {
        DefineAttribute(target, name, getter, (realm, thisValue, value) =>
        {
            // https://webidl.spec.whatwg.org/#dfn-attribute-setter: the this value must implement the interface (the getter
            // checks it), then CreateDataPropertyOrThrow(jsValue, id, V)
            realm.InvokeGetter(getter, thisValue);
            if (!realm.Adapter.DefineOwnProperty(realm.ThisOrGlobal(thisValue), realm.Key(name), JsPropertyDescriptor<TValue>.Data(value, writable: true, enumerable: true, configurable: true)))
                throw realm.CreateTypeError($"Cannot redefine property: {name}");
        }, configurable: configurable);
    }

    /// <summary>CreateDataPropertyOrThrow on the this value of an attribute setter (the global object when it is undefined or null).</summary>
    internal void CreateDataPropertyOrThrow(TValue thisValue, string name, TValue value)
    {
        if (!Adapter.DefineOwnProperty(ThisOrGlobal(thisValue), Key(name), JsPropertyDescriptor<TValue>.Data(value, writable: true, enumerable: true, configurable: true)))
            throw CreateTypeError($"Cannot redefine property: {name}");
    }

    /// <summary>The Reflect.set function of the realm.</summary>
    internal TValue ReflectSet => _reflectSet ??= EvaluateBuiltIn("Reflect.set");

    /// <summary>The this value of an attribute or an operation: the global object when it is undefined or null.</summary>
    private TValue ThisOrGlobal(TValue thisValue) => Adapter.IsUndefined(thisValue) || Adapter.IsNull(thisValue) ? Global : thisValue;

    /// <summary>https://webidl.spec.whatwg.org/#es-operations</summary>
    internal void DefineOperation(TValue target, string name, int length, Operation operation, bool ceReactions = false, bool returnsPromise = false, bool enumerable = true, bool configurable = true)
    {
        if (!IsMemberEnabled(_definingInterface, name))
            return;

        if (_definingInterface is { } interfaceName && IsCrossOriginProperty(interfaceName, name, CrossOriginAccess.Call))
        {
            var crossOriginOperation = operation;
            operation = (realm, thisValue, arguments) =>
            {
                s_crossOriginAccessAllowed = true;
                try
                {
                    return crossOriginOperation(realm, thisValue, arguments);
                }
                finally
                {
                    s_crossOriginAccessAllowed = false;
                }
            };
        }

        // Most operations are never used: their functions are created on first access, in this realm
        Adapter.DefineLazyDataProperty(target, Key(name), new LazyOperation(this, name, length, operation, ceReactions, returnsPromise), writable: configurable, enumerable, configurable);
    }

    /// <summary>The function of an operation, created on first access in the realm (one object per operation, instead of closures).</summary>
    private sealed class LazyOperation(WebIdlRealm<TAdapter, TValue> realm, string name, int length, Operation operation, bool ceReactions, bool returnsPromise) : IJsLazyValue<TValue>
    {
        public TValue CreateValue() => realm.Adapter.RunInRealm(realm.EngineRealm, CreateOperation);

        private TValue CreateOperation() => returnsPromise
            ? realm.Adapter.CreateFunction(name, length, (thisValue, arguments) => realm.InvokePromiseOperation(operation, thisValue, arguments, ceReactions))
            : realm.Adapter.CreateFunction(name, length, (thisValue, arguments) => realm.InvokeOperation(operation, thisValue, arguments, ceReactions));
    }

    /// <summary>Defines a data property whose value is a function (e.g. the methods of the iterators).</summary>
    internal void DefineMethod(TValue target, TValue key, TValue function, bool enumerable)
        => Adapter.DefineOwnProperty(target, key, JsPropertyDescriptor<TValue>.Data(function, writable: true, enumerable, configurable: true));

    /// <summary>https://webidl.spec.whatwg.org/#interface-prototype-object: the @@unscopables property.</summary>
    internal void DefineUnscopables(TValue prototype, string[] names)
    {
        var unscopables = Adapter.CreateObject(Adapter.Null);
        foreach (var name in names)
        {
            Adapter.DefineOwnProperty(unscopables, Key(name), JsPropertyDescriptor<TValue>.Data(Adapter.FromBoolean(value: true), writable: true, enumerable: true, configurable: true));
        }

        Adapter.DefineOwnProperty(prototype, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Unscopables), JsPropertyDescriptor<TValue>.Data(unscopables, writable: false, enumerable: false, configurable: true));
    }

    /// <summary>https://webidl.spec.whatwg.org/#es-iterator: the value iterators of the interfaces with indexed properties use the methods of Array.prototype.</summary>
    internal void DefineValueIterator(TValue prototype, bool iterable)
    {
        var arrayPrototype = EvaluateBuiltIn("Array.prototype");
        if (iterable)
        {
            foreach (var name in new[] { "entries", "keys", "values", "forEach" })
            {
                DefineMethod(prototype, Key(name), Adapter.Get(arrayPrototype, Key(name)), enumerable: true);
            }
        }

        DefineMethod(prototype, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator), Adapter.Get(arrayPrototype, Key("values")), enumerable: false);
    }

    /// <summary>
    /// https://webidl.spec.whatwg.org/#es-iterators: the pair iterators. <paramref name="pairAt"/> returns the pair at an index
    /// of the current value pairs to iterate over of the .NET object, or null when the index is out of range.
    /// </summary>
    internal void DefinePairIterator(TValue prototype, Func<WebIdlRealm<TAdapter, TValue>, object, int, (TValue Key, TValue Value)?> pairAt, InterfaceBinding<TAdapter, TValue> binding)
    {
        // Only the pair at the index is converted (and computed when the object supports it): converting all the pairs at
        // each step would make the iteration quadratic. A negative index only checks the brand of the object.
        var pairAtFunction = Adapter.CreateFunction("pairAt", 1, (thisValue, arguments) =>
        {
            var target = This(thisValue, binding);
            return pairAt(this, target, (int)Adapter.ToNumber(arguments[0])) is ({ } key, { } value) ? Adapter.CreateArray([key, value]) : Adapter.Undefined;
        });

        // The default iterator objects (https://webidl.spec.whatwg.org/#es-default-iterator-object) keep an index into the
        // current pairs of the target, so they see the changes made during the iteration. Their prototype is the iterator
        // prototype object of the interface (https://webidl.spec.whatwg.org/#es-iterator-prototype-object).
        var definePairIterators = EvaluateBuiltIn("""
            (function (prototype, pairAt, name) {
              const iterators = new WeakMap();
              const iteratorPrototype = Object.create(Object.getPrototypeOf(Object.getPrototypeOf([][Symbol.iterator]())));
              Object.defineProperty(iteratorPrototype, 'next', { value: { next() {
                const iterator = iterators.get(Object(this));
                if (iterator === undefined) throw new TypeError(`next() called on an object that is not a ${name} iterator`);
                const pair = pairAt.call(iterator.target, iterator.index);
                if (pair === undefined) return { value: undefined, done: true };
                iterator.index++;
                return { value: iterator.kind === 'key' ? pair[0] : iterator.kind === 'value' ? pair[1] : pair, done: false };
              } }.next, writable: true, enumerable: true, configurable: true });
              Object.defineProperty(iteratorPrototype, Symbol.toStringTag, { value: name + ' Iterator', configurable: true });
              const createIterator = (target, kind) => {
                pairAt.call(target, -1);
                const iterator = Object.create(iteratorPrototype);
                iterators.set(iterator, { target, kind, index: 0 });
                return iterator;
              };
              const define = (name, value) => Object.defineProperty(prototype, name, { value: value, writable: true, enumerable: true, configurable: true });
              define('entries', { entries() { return createIterator(this, 'key+value'); } }.entries);
              define('keys', { keys() { return createIterator(this, 'key'); } }.keys);
              define('values', { values() { return createIterator(this, 'value'); } }.values);
              define('forEach', { forEach(callback, thisArg = undefined) {
                let pair = pairAt.call(this, 0);
                if (typeof callback !== 'function') throw new TypeError('The callback is not a function');
                for (let i = 0; pair !== undefined; pair = pairAt.call(this, ++i)) {
                  callback.call(thisArg, pair[1], pair[0], this);
                }
              } }.forEach);
              Object.defineProperty(prototype, Symbol.iterator, { value: prototype.entries, writable: true, enumerable: false, configurable: true });
            })
            """);
        Adapter.Call(definePairIterators, Adapter.Undefined, [prototype, pairAtFunction, Adapter.FromString(binding.Name)]);
    }

    /// <summary>
    /// Defines the members of a setlike declaration (https://webidl.spec.whatwg.org/#es-setlike) with the .NET members of the
    /// collection. The iterators see the changes made during the iteration, as the iterators of Set objects.
    /// </summary>
    internal void DefineSetlike(TValue prototype, InterfaceBinding<TAdapter, TValue> binding, SetlikeBinding setlike)
    {
        TValue Invoke(TValue thisValue, Func<object, TValue> action)
        {
            var target = This(thisValue, binding);
            try
            {
                return action(target);
            }
            catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }
        }

        // The value is converted to the type of the setlike (https://webidl.spec.whatwg.org/#js-set-has)
        object Value(ReadOnlySpan<TValue> arguments)
        {
            var value = arguments.Length > 0 ? arguments[0] : Adapter.Undefined;
            if (setlike.ElementType is null)
                return ToDomString(value);

            return Unwrap(value) is { } target && setlike.ElementType.IsInstanceOfType(target) ? target : throw CreateTypeError($"The value is not of type '{setlike.ElementName}'");
        }

        var functions = Adapter.CreateObject(ObjectPrototype);
        void Set(string name, int length, JsHostFunction<TValue> function) => Adapter.Set(functions, Key(name), Adapter.CreateFunction(name, length, function));
        Set("size", 0, (thisValue, _) => Invoke(thisValue, target => Adapter.FromNumber(setlike.Count(target))));
        Set("has", 1, (thisValue, arguments) =>
        {
            var value = Value(arguments);
            return Invoke(thisValue, target => Adapter.FromBoolean(setlike.Has(target, value)));
        });
        Set("values", 0, (thisValue, _) =>
        {
            var enumerator = setlike.GetEnumerator(This(thisValue, binding));
            return Adapter.CreateFunction("next", 0, (_, _) => enumerator.MoveNext() ? (enumerator.Current is string text ? Adapter.FromString(text) : ToJs(enumerator.Current)) : Adapter.Undefined);
        });

        if (setlike.Add is not null)
        {
            Set("add", 1, (thisValue, arguments) =>
            {
                var value = Value(arguments);
                Invoke(thisValue, target =>
                {
                    setlike.Add(target, value);
                    return Adapter.Undefined;
                });
                return thisValue;
            });
            Set("delete", 1, (thisValue, arguments) =>
            {
                var value = Value(arguments);
                return Invoke(thisValue, target => Adapter.FromBoolean(setlike.Delete!(target, value)));
            });
            Set("clear", 0, (thisValue, _) => Invoke(thisValue, target =>
            {
                setlike.Clear!(target);
                return Adapter.Undefined;
            }));
        }

        var define = EvaluateBuiltIn("""
            (function (prototype, f) {
              const defineMethod = (name, value) => Object.defineProperty(prototype, name, { value: value, writable: true, enumerable: true, configurable: true });
              Object.defineProperty(prototype, 'size', { get: Object.getOwnPropertyDescriptor({ get size() { return f.size.call(this); } }, 'size').get, enumerable: true, configurable: true });
              defineMethod('has', { has(value) { return f.has.call(this, value); } }.has);
              const iterate = function* (set, pair) {
                const next = f.values.call(set);
                for (let value = next(); value !== undefined; value = next()) yield pair ? [value, value] : value;
              };
              defineMethod('values', { values() { return iterate(this, false); } }.values);
              defineMethod('keys', { keys() { return iterate(this, false); } }.keys);
              defineMethod('entries', { entries() { return iterate(this, true); } }.entries);
              defineMethod('forEach', { forEach(callback, thisArg = undefined) {
                if (typeof callback !== 'function') throw new TypeError('The callback is not a function');
                for (const value of iterate(this, false)) callback.call(thisArg, value, value, this);
              } }.forEach);
              if (f.add) {
                defineMethod('add', { add(value) { return f.add.call(this, value); } }.add);
                defineMethod('delete', { delete(value) { return f.delete.call(this, value); } }.delete);
                defineMethod('clear', { clear() { return f.clear.call(this); } }.clear);
              }
              Object.defineProperty(prototype, Symbol.iterator, { value: prototype.values, writable: true, enumerable: false, configurable: true });
            })
            """);
        Adapter.Call(define, Adapter.Undefined, [prototype, functions]);
    }

    /// <summary>
    /// Defines the members of a maplike declaration (https://webidl.spec.whatwg.org/#es-maplike) with the .NET members of the
    /// collection. The iterators see the changes made during the iteration, as the iterators of Map objects.
    /// </summary>
    internal void DefineMaplike(TValue prototype, InterfaceBinding<TAdapter, TValue> binding, MaplikeBinding maplike)
    {
        TValue Invoke(TValue thisValue, Func<object, TValue> action)
        {
            var target = This(thisValue, binding);
            try
            {
                return action(target);
            }
            catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }
        }

        // The key and the value are converted to the types of the maplike (https://webidl.spec.whatwg.org/#js-map-get)
        string ToKey(ReadOnlySpan<TValue> arguments) => ToDomString(arguments.Length > 0 ? arguments[0] : Adapter.Undefined);

        object Value(ReadOnlySpan<TValue> arguments)
        {
            var value = arguments.Length > 1 ? arguments[1] : Adapter.Undefined;
            return Unwrap(value) is { } target && maplike.ValueType.IsInstanceOfType(target) ? target : throw CreateTypeError($"The value is not of type '{maplike.ValueName}'");
        }

        var functions = Adapter.CreateObject(ObjectPrototype);
        void Set(string name, int length, JsHostFunction<TValue> function) => Adapter.Set(functions, Key(name), Adapter.CreateFunction(name, length, function));
        Set("size", 0, (thisValue, _) => Invoke(thisValue, target => Adapter.FromNumber(maplike.Count(target))));
        Set("get", 1, (thisValue, arguments) =>
        {
            var key = ToKey(arguments);
            return Invoke(thisValue, target => maplike.Get(target, key) is { } value ? ToJs(value) : Adapter.Undefined);
        });
        Set("has", 1, (thisValue, arguments) =>
        {
            var key = ToKey(arguments);
            return Invoke(thisValue, target => Adapter.FromBoolean(maplike.Has(target, key)));
        });
        Set("entries", 0, (thisValue, _) =>
        {
            var enumerator = maplike.GetEnumerator(This(thisValue, binding));
            return Adapter.CreateFunction("next", 0, (_, _) => enumerator.MoveNext() ? Adapter.CreateArray([Adapter.FromString(enumerator.Current.Key), ToJs(enumerator.Current.Value)]) : Adapter.Undefined);
        });

        // The set, delete and clear operations declared by the interface replace the ones of the maplike
        if (maplike.Set is not null)
        {
            Set("set", 2, (thisValue, arguments) =>
            {
                var key = ToKey(arguments);
                var value = Value(arguments);
                Invoke(thisValue, target =>
                {
                    maplike.Set(target, key, value);
                    return Adapter.Undefined;
                });
                return thisValue;
            });
        }

        if (maplike.Delete is not null)
        {
            Set("delete", 1, (thisValue, arguments) =>
            {
                var key = ToKey(arguments);
                return Invoke(thisValue, target => Adapter.FromBoolean(maplike.Delete(target, key)));
            });
        }

        if (maplike.Clear is not null)
        {
            Set("clear", 0, (thisValue, _) => Invoke(thisValue, target =>
            {
                maplike.Clear(target);
                return Adapter.Undefined;
            }));
        }

        var define = EvaluateBuiltIn("""
            (function (prototype, f) {
              const defineMethod = (name, value) => Object.defineProperty(prototype, name, { value: value, writable: true, enumerable: true, configurable: true });
              Object.defineProperty(prototype, 'size', { get: Object.getOwnPropertyDescriptor({ get size() { return f.size.call(this); } }, 'size').get, enumerable: true, configurable: true });
              const iterate = function* (map, kind) {
                const next = f.entries.call(map);
                for (let entry = next(); entry !== undefined; entry = next()) yield kind === 'key' ? entry[0] : kind === 'value' ? entry[1] : entry;
              };
              defineMethod('entries', { entries() { return iterate(this, 'key+value'); } }.entries);
              defineMethod('keys', { keys() { return iterate(this, 'key'); } }.keys);
              defineMethod('values', { values() { return iterate(this, 'value'); } }.values);
              defineMethod('forEach', { forEach(callback, thisArg = undefined) {
                if (typeof callback !== 'function') throw new TypeError('The callback is not a function');
                for (const [key, value] of iterate(this, 'key+value')) callback.call(thisArg, value, key, this);
              } }.forEach);
              defineMethod('get', { get(key) { return f.get.call(this, key); } }.get);
              defineMethod('has', { has(key) { return f.has.call(this, key); } }.has);
              if (f.set) defineMethod('set', { set(key, value) { return f.set.call(this, key, value); } }.set);
              if (f.delete) defineMethod('delete', { delete(key) { return f.delete.call(this, key); } }.delete);
              if (f.clear) defineMethod('clear', { clear() { return f.clear.call(this); } }.clear);
              Object.defineProperty(prototype, Symbol.iterator, { value: prototype.entries, writable: true, enumerable: false, configurable: true });
            })
            """);
        Adapter.Call(define, Adapter.Undefined, [prototype, functions]);
    }

    /// <summary>Runs the getter of a static attribute (e.g. CSS.highlights) with the window of the realm as the current global object.</summary>
    internal TValue InCurrentGlobal(Func<WebIdlRealm<TAdapter, TValue>, TValue> getter)
    {
        var previousDocument = Document.CurrentGlobalDocumentOrNull;
        Document.CurrentGlobalDocument = GlobalScope.EnvironmentDocument;
        try
        {
            return getter(this);
        }
        finally
        {
            Document.CurrentGlobalDocumentOrNull = previousDocument;
        }
    }

    /// <summary>
    /// The IDL attributes of the CSS properties (https://drafts.csswg.org/cssom/#cssstyleproperties): the camel-cased,
    /// webkit-cased and dashed attributes of each supported property.
    /// </summary>
    internal void DefineCssPropertyAccessors(TValue prototype, InterfaceBinding<TAdapter, TValue> binding, (string AttributeName, string PropertyName)[] properties)
    {
        foreach (var (attributeName, propertyName) in properties)
        {
            DefineAttribute(prototype, attributeName,
                (realm, thisValue) => realm.Adapter.FromString(((Css.Om.CssStyleDeclaration)realm.This(thisValue, binding)).GetPropertyValue(propertyName)),
                (realm, thisValue, value) =>
                {
                    var target = (Css.Om.CssStyleDeclaration)realm.This(thisValue, binding);

                    // [LegacyNullToEmptyString]
                    var text = realm.Adapter.IsNull(value) ? "" : realm.ToDomString(value);
                    target.SetProperty(propertyName, text, "");
                });
        }
    }

    internal TValue InvokeGetter(Getter getter, TValue thisValue)
    {
        try
        {
            return getter(this, thisValue);
        }
        catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            throw ToJsException(ex);
        }
    }

    private void InvokeSetter(Setter setter, TValue thisValue, TValue value)
    {
        using var scope = Html.CEReactionsScope.EnterAndPush();
        try
        {
            setter(this, thisValue, value);
        }
        catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            throw ToJsException(ex);
        }
    }

    private TValue InvokeOperation(Operation operation, TValue thisValue, ReadOnlySpan<TValue> arguments, bool ceReactions)
    {
        // An operation decorated with [CEReactions] pushes an element queue (https://html.spec.whatwg.org/multipage/custom-elements.html#cereactions);
        // the reactions of the other operations are added to the current element queue, or to the backup element queue.
        using var scope = ceReactions ? Html.CEReactionsScope.EnterAndPush() : Html.CEReactionsScope.EnterWithoutPush();

        // The current global object is the one of the realm of the operation (e.g. the URL of the documents created by DOMParser)
        var previousDocument = Document.CurrentGlobalDocumentOrNull;
        Document.CurrentGlobalDocument = GlobalScope.EnvironmentDocument;
        try
        {
            return operation(this, thisValue, arguments);
        }
        catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            throw ToJsException(ex);
        }
        finally
        {
            Document.CurrentGlobalDocumentOrNull = previousDocument;
        }
    }

    /// <summary>
    /// Invokes an operation whose return type is a promise: an exception (e.g. a TypeError when this is not a platform object
    /// of the interface, or when an argument cannot be converted) is returned as a rejected promise
    /// (https://webidl.spec.whatwg.org/#dfn-create-operation-function).
    /// </summary>
    private TValue InvokePromiseGetter(Getter getter, TValue thisValue)
    {
        try
        {
            return InvokeGetter(getter, thisValue);
        }
        catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out TValue? reason))
        {
            var promiseConstructor = Adapter.Get(Global, Key("Promise"));
            return Adapter.Call(Adapter.Get(promiseConstructor, Key("reject")), promiseConstructor, [reason]);
        }
    }

    private TValue InvokePromiseOperation(Operation operation, TValue thisValue, ReadOnlySpan<TValue> arguments, bool ceReactions)
    {
        try
        {
            return InvokeOperation(operation, thisValue, arguments, ceReactions);
        }
        catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out TValue? reason))
        {
            var promiseConstructor = Adapter.Get(Global, Key("Promise"));
            return Adapter.Call(Adapter.Get(promiseConstructor, Key("reject")), promiseConstructor, [reason]);
        }
    }

    /// <summary>The [[Construct]] of the interface objects (https://webidl.spec.whatwg.org/#es-interface-call).</summary>
    private TValue Construct(InterfaceBinding<TAdapter, TValue> binding, TValue newTarget, TValue argumentsArray)
    {
        if (Adapter.IsUndefined(newTarget))
            throw CreateTypeError($"Failed to construct '{binding.Name}': Please use the 'new' operator");

        if ((binding.Flags & InterfaceBindingFlags.HtmlConstructor) != InterfaceBindingFlags.None)
            return ConstructHtmlElement(binding, newTarget);

        if (binding.Constructor is null)
            throw CreateTypeError("Illegal constructor");

        // The constructors use the associated document of the current global object (e.g. new Text())
        var arguments = ToArgumentArray(argumentsArray);
        var previousDocument = Document.CurrentGlobalDocumentOrNull;
        Document.CurrentGlobalDocument = GlobalScope.EnvironmentDocument;
        object instance;
        try
        {
            instance = binding.Constructor(this, arguments);
        }
        catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            throw ToJsException(ex);
        }
        finally
        {
            Document.CurrentGlobalDocumentOrNull = previousDocument;
        }

        // https://webidl.spec.whatwg.org/#internally-create-a-new-object-implementing-the-interface
        var prototype = GetPrototypeFromConstructor(newTarget, binding);

        if (Agent.Wrappers.TryGetValue(instance, out var existing))
            return existing;

        return GetWrapper(instance, prototype);
    }

    private TValue[] ToArgumentArray(TValue array)
    {
        var length = (int)Adapter.ToNumber(Adapter.Get(array, Key("length")));
        var result = new TValue[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = Adapter.Get(array, Adapter.FromString(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // WindowProxy (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getownproperty)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Defines the indexed properties of the global object of a window: the windows of the document-tree child navigables.</summary>
    public void SyncFrames()
    {
        if (Window is not { } window)
            return;

        if (_syncFrames is null)
        {
            var frame = Adapter.CreateFunction("frame", 1, (_, arguments) => window[(int)Adapter.ToNumber(arguments[0])] is { } frameWindow ? ToJs(frameWindow) : Adapter.Undefined);
            var create = EvaluateBuiltIn("""
                (function (frame, global) {
                  return function (count) {
                    for (let i = 0; i < count; i++) {
                      if (!Object.prototype.hasOwnProperty.call(global, i)) {
                        Object.defineProperty(global, i, { get: function () { return frame(i); }, enumerable: true, configurable: true });
                      }
                    }
                    for (let i = count; Object.prototype.hasOwnProperty.call(global, i); i++) delete global[i];
                  };
                })
                """);
            _syncFrames = Adapter.Call(create, Adapter.Undefined, [frame, Global]);
        }

        Adapter.Call(_syncFrames, Adapter.Undefined, [Adapter.FromNumber(window.Length)]);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Wrappers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the JavaScript object of a .NET platform object.</summary>
    public TValue GetWrapper(object target, TValue? prototype = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        // The global object for the code of the realm of the window, else the WindowProxy of its browsing context
        if (target is Window window)
            return GetWindowValue(window, (Agent.Host.IsScriptRunning ? Agent.CurrentRealm : null) ?? this);

        if (Agent.Wrappers.TryGetValue(target, out var wrapper))
            return wrapper;

        // A node is created in the realm of its node document when it is created, which it keeps when it is adopted
        if (prototype is null && target is Node node && node.CreationRealmWindow is { } nodeWindow && !ReferenceEquals(nodeWindow, Window))
            return Agent.GetRealm(nodeWindow).GetWrapper(target);

        // An object created by the user agent for another window (e.g. the MediaQueryList of its matchMedia()) is created in the realm of that window
        if (prototype is null && target is IRealmBoundPlatformObject { RealmWindow: { } realmWindow } && !ReferenceEquals(realmWindow, Window))
            return Agent.GetRealm(realmWindow).GetWrapper(target);

        var binding = WebIdlBindings<TAdapter, TValue>.GetInterface(target.GetType()) ?? throw new InvalidOperationException($"The type '{target.GetType()}' is not an IDL interface");
        wrapper = CreatePlatformObject(prototype ?? EnsureInterfaceObject(binding).Prototype, target, GetLegacyPlatformObjectHandler(binding));
        if (target is HtmlAllCollection all)
        {
            wrapper = CreateHtmlAllCollectionWrapper(all, wrapper);
        }

        if ((binding.Flags & InterfaceBindingFlags.ModuleSource) != InterfaceBindingFlags.None)
        {
#pragma warning disable WEBPLATEXP0014 // The source phase imports are experimental for the consumers of the package only
            Adapter.SetModuleSourceClassName(wrapper, binding.ClassString ?? binding.Name);
#pragma warning restore WEBPLATEXP0014
        }

        Agent.Wrappers.AddOrUpdate(target, wrapper);
        DefineUnforgeableMembers(binding, wrapper);

        // https://drafts.csswg.org/css-view-transitions-1/#viewtransition-ready-promise: "the ready promise and update callback
        // done promise are immediately created, so rejections will cause unhandledrejections unless they're handled, even if the
        // getters such as updateCallbackDone are not accessed"
        if (target is ViewTransition transition)
        {
            Adapter.RunInRealm(EngineRealm, () =>
            {
                ToPromise(transition.UpdateCallbackDone);
                ToPromise(transition.Ready);
                return ToPromise(transition.Finished);
            });
        }

        return wrapper;
    }

    // The closure is created in its own method: GetWrapper would otherwise allocate it on each call, for the existing wrappers too
    private TValue CreatePlatformObject(TValue prototype, object target, LegacyPlatformObjectHandler<TAdapter, TValue>? handler) =>
        Adapter.RunInRealm(EngineRealm, () => Adapter.CreatePlatformObject(prototype, target, handler));

    /// <summary>Defines the [LegacyUnforgeable] members of the interfaces of a new instance (https://webidl.spec.whatwg.org/#LegacyUnforgeable).</summary>
    private void DefineUnforgeableMembers(InterfaceBinding<TAdapter, TValue> binding, TValue instance)
    {
        // The properties are defined once per realm and interface on a holder object, whose descriptors are copied to the
        // instances (the getters, setters and functions are the same for all the instances, as the [[Unforgeables]] of
        // https://webidl.spec.whatwg.org/#interface-prototype-object)
        for (var current = binding; current is not null; current = current.Parent)
        {
            if (current.DefineUnforgeableMembers is null)
                continue;

            if (!_unforgeableHolders.TryGetValue(current, out var holder))
            {
                holder = Adapter.RunInRealm(EngineRealm, () => Adapter.CreateObject(Adapter.Null));
                var previousDefiningInterface = _definingInterface;
                _definingInterface = current.Name;
                try
                {
                    current.DefineUnforgeableMembers(this, holder);
                }
                finally
                {
                    _definingInterface = previousDefiningInterface;
                }

                _unforgeableHolders[current] = holder;
            }

            foreach (var key in Adapter.OwnKeys(holder))
            {
                if (Adapter.GetOwnProperty(holder, key, out var descriptor))
                {
                    // The ordinary definition: a named property of a [LegacyOverrideBuiltIns] instance with the name of the
                    // member (e.g. <img name=location> for a document) must not prevent it, as the own properties hide
                    // the named properties (https://webidl.spec.whatwg.org/#dfn-named-property-visibility)
                    Adapter.OrdinaryDefineOwnProperty(instance, key, descriptor);
                }
            }
        }

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-location-interface: valueOf and @@toPrimitive are
        // non-configurable own properties of the Location objects
        if (binding.Name == "Location")
        {
            Adapter.DefineOwnProperty(instance, Key("valueOf"), JsPropertyDescriptor<TValue>.Data(Adapter.Get(ObjectPrototype, Key("valueOf")), writable: false, enumerable: false, configurable: false));
            Adapter.DefineOwnProperty(instance, _toPrimitiveSymbol ??= EvaluateBuiltIn("Symbol.toPrimitive"), JsPropertyDescriptor<TValue>.Data(Adapter.Undefined, writable: false, enumerable: false, configurable: false));
        }
    }

    private readonly Dictionary<InterfaceBinding<TAdapter, TValue>, LegacyPlatformObjectHandler<TAdapter, TValue>?> _legacyHandlers = [];

    private LegacyPlatformObjectHandler<TAdapter, TValue>? GetLegacyPlatformObjectHandler(InterfaceBinding<TAdapter, TValue> binding)
    {
        if (_legacyHandlers.TryGetValue(binding, out var handler))
            return handler;

        var legacy = WebIdlBindings<TAdapter, TValue>.GetLegacyPlatformObject(binding);
        handler = legacy is null || !(legacy.SupportsIndexedProperties || legacy.SupportsNamedProperties) ? null : new LegacyPlatformObjectHandler<TAdapter, TValue>(this, binding, legacy);
        _legacyHandlers[binding] = handler;
        return handler;
    }

    /// <summary>
    /// document.all has a custom [[Call]] internal method (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#HTMLAllCollection-call).
    /// It is exposed through a Proxy of a function. The [[IsHTMLDDA]] slot (falsy, typeof "undefined") cannot be emulated.
    /// </summary>
    private TValue CreateHtmlAllCollectionWrapper(HtmlAllCollection target, TValue platformObject)
    {
        var call = Adapter.CreateFunction("call", 0, (_, arguments) =>
        {
            try
            {
                if (arguments.Length == 0 || Adapter.IsUndefined(arguments[0]))
                    return Adapter.Null;

                return ToJs(target.Item(ToDomString(arguments[0])));
            }
            catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }
        });

        var create = EvaluateBuiltIn("""
            (function (target, call) {
              return new Proxy(() => {}, {
                apply(t, thisArg, args) { return call(...args); },
                get(t, p, r) { return Reflect.get(target, p, target); },
                set(t, p, v, r) { return Reflect.set(target, p, v, target); },
                has(t, p) { return Reflect.has(target, p); },
                ownKeys(t) { return Reflect.ownKeys(target); },
                getOwnPropertyDescriptor(t, p) { return Reflect.getOwnPropertyDescriptor(target, p); },
                defineProperty(t, p, d) { return Reflect.defineProperty(target, p, d); },
                deleteProperty(t, p) { return Reflect.deleteProperty(target, p); },
                getPrototypeOf(t) { return Reflect.getPrototypeOf(target); },
                setPrototypeOf(t, proto) { return Reflect.setPrototypeOf(target, proto); },
                preventExtensions(t) { return false; },
              });
            })
            """);
        var proxy = Adapter.Call(create, Adapter.Undefined, [platformObject, call]);
        Agent.CallableWrapperTargets.AddOrUpdate(proxy, target);
        return proxy;
    }

    /// <summary>Returns whether a .NET object is a value of the engine (see <see cref="IJsAdapter{TValue}.IsValue"/>).</summary>
    internal bool TryAsValue(object? value, [NotNullWhen(true)] out TValue? jsValue)
    {
        if (value is TValue candidate && Adapter.IsValue(candidate))
        {
            jsValue = candidate;
            return true;
        }

        jsValue = null;
        return false;
    }

    /// <summary>Returns the .NET object of a JavaScript platform object (or the window of a global object), or null.</summary>
    public object? Unwrap(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Adapter.TryGetPlatformObject(value, out var target))
            return target;

        if (Adapter.IsObject(value))
        {
            if (Agent.CallableWrapperTargets.TryGetValue(value, out var callableTarget))
                return callableTarget;

            if (Agent.GetRealmByGlobal(value) is { } realm)
                return realm.GlobalScope;
        }

        return null;
    }

    /// <summary>Returns the .NET object of the <c>this</c> value of an operation or an attribute of an interface.</summary>
    internal object This(TValue thisValue, InterfaceBinding<TAdapter, TValue> binding)
    {
        // https://webidl.spec.whatwg.org/#dfn-create-operation-function: when this is undefined or null, use the global object
        if (Adapter.IsUndefined(thisValue) || Adapter.IsNull(thisValue))
        {
            thisValue = Global;
        }

        var target = Unwrap(thisValue);
        if (target is not null && binding.IsInstance(target))
        {
            if (target is Dom.Window or Dom.Location)
            {
                CheckCrossOriginAccess(target);
            }

            return target;
        }

        throw CreateTypeError($"Illegal invocation: 'this' does not implement interface {binding.Name}");
    }

    /// <summary>
    /// Returns the .NET object of the <c>this</c> value of an attribute declared with [LegacyLenientThis]
    /// (https://webidl.spec.whatwg.org/#LegacyLenientThis), or null when it does not implement the interface.
    /// </summary>
    internal T? TryThis<T>(TValue thisValue, InterfaceBinding<TAdapter, TValue> binding)
        where T : class
    {
        if (Unwrap(ThisOrGlobal(thisValue)) is not { } target || !binding.IsInstance(target))
            return null;

        if (target is Dom.Window or Dom.Location)
        {
            CheckCrossOriginAccess(target);
        }

        return (T)target;
    }

    /// <summary>Returns the .NET object of the <c>this</c> value, cast to the .NET type of the interface.</summary>
    internal T This<T>(TValue thisValue, InterfaceBinding<TAdapter, TValue> binding)
        where T : class => (T)This(thisValue, binding);

    /// <summary>Whether a .NET object implements an IDL interface.</summary>
    internal static bool ImplementsInterface(object platformObject, string interfaceName)
        => WebIdlBindings<TAdapter, TValue>.GetInterface(platformObject.GetType())?.IsA(interfaceName) ?? false;

    // ----------------------------------------------------------------------------------------------------------------
    // Cross-origin objects (https://html.spec.whatwg.org/multipage/nav-history-apis.html#cross-origin-objects)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The interface whose members are being defined, or null.</summary>
    private string? _definingInterface;

    /// <summary>Whether the running member of a Window or a Location object is a cross-origin property, which is accessible from any origin.</summary>
    [ThreadStatic]
    private static bool s_crossOriginAccessAllowed;

    /// <summary>
    /// Whether a member of the Window and Location interfaces is in CrossOriginProperties
    /// (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginproperties-(-o-)), for the kind of access.
    /// </summary>
    private static bool IsCrossOriginProperty(string interfaceName, string name, CrossOriginAccess access) => interfaceName switch
    {
        "Window" => access switch
        {
            CrossOriginAccess.Get => name is "window" or "self" or "location" or "closed" or "frames" or "length" or "top" or "opener" or "parent",
            CrossOriginAccess.Set => name is "location",
            _ => name is "close" or "focus" or "blur" or "postMessage",
        },
        "Location" => access switch
        {
            CrossOriginAccess.Set => name is "href",
            CrossOriginAccess.Call => name is "replace",
            _ => false,
        },
        _ => false,
    };

    private static T AllowCrossOriginAccess<T>(Func<T> action)
    {
        s_crossOriginAccessAllowed = true;
        try
        {
            return action();
        }
        finally
        {
            s_crossOriginAccessAllowed = false;
        }
    }

    /// <summary>
    /// The security check of the members of the Window and Location objects that are not cross-origin properties
    /// (https://html.spec.whatwg.org/multipage/nav-history-apis.html#isplatformobjectsameorigin-(-o-)): the window must be
    /// same origin-domain with the realm of the caller, otherwise a "SecurityError" DOMException of the caller's realm is
    /// thrown. The check is done when the member is invoked, not by the internal methods of the objects (an own property
    /// that is not an IDL member, e.g. a global variable, is not checked).
    /// </summary>
    private void CheckCrossOriginAccess(object target)
    {
        if (s_crossOriginAccessAllowed)
        {
            // The access of a cross-origin property: the flag is consumed by the first check of the member
            s_crossOriginAccessAllowed = false;
            return;
        }

        var window = target as Dom.Window ?? ((Dom.Location)target).Window;
        if (Agent.CurrentRealm is not { } currentRealm || ReferenceEquals(currentRealm.GlobalScope, window) || Document.IsSameOriginDomain(currentRealm.GlobalScope.EnvironmentDocument, window.Document))
            return;

        throw currentRealm.ToJsException(new DomException("Blocked a frame from accessing a cross-origin frame", "SecurityError"));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Exceptions
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the exception that throws a TypeError of this realm.</summary>
    public Exception CreateTypeError(string message) => Adapter.CreateException(CreateError("TypeError", message));

    /// <summary>Returns the exception that throws a RangeError of this realm.</summary>
    public Exception CreateRangeError(string message) => Adapter.CreateException(CreateError("RangeError", message));

    /// <summary>Creates an error object of this realm (e.g. <c>TypeError</c>).</summary>
    public TValue CreateError(string constructorName, string message)
        => Adapter.Construct(Adapter.Get(Global, Key(constructorName)), [Adapter.FromString(message)], Adapter.Get(Global, Key(constructorName)));

    /// <summary>Converts a .NET exception thrown by the DOM to the JavaScript exception a browser would throw.</summary>
    public Exception ToJsException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Adapter.TryGetThrownValue(exception, out _) || Adapter.IsTerminationException(exception))
            return exception;

        return Adapter.CreateException(ToJsError(exception));
    }

    /// <summary>Returns the JavaScript value of a .NET exception thrown by the DOM.</summary>
    public TValue ToJsError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Adapter.TryGetThrownValue(exception, out var thrown))
            return thrown;

        return exception switch
        {
            DomException domException => GetWrapper(domException),
            RangeErrorException => CreateError("RangeError", exception.Message),
            JavaScriptSyntaxErrorException => CreateError("SyntaxError", exception.Message),
            EvalErrorException => CreateError("EvalError", exception.Message),
            AbortSignalException { Reason: var reason } => ToJs(reason),
            ArgumentException => CreateError("TypeError", exception.Message),
            _ => CreateError("Error", exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace),
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CLR to JavaScript
    // ----------------------------------------------------------------------------------------------------------------

    // The closures of the conversions are created in their own methods: ToJs would otherwise allocate them on each call
    private TValue CreateDate(DateTime dateTime) => Adapter.RunInRealm(EngineRealm, () => Adapter.CreateDate((dateTime.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds));

    /// <summary>Returns the Date object of a date and time; null is a Date object whose time value is NaN (an "Invalid Date").</summary>
    public TValue FromDate(DateTime? dateTime) => dateTime is { } value ? CreateDate(value) : Adapter.RunInRealm(EngineRealm, () => Adapter.CreateDate(double.NaN));

    /// <summary>Returns the value of a JSON text, parsed once (e.g. XMLHttpRequest.response); an invalid JSON text is null.</summary>
    private TValue ParseJson(JsonText json)
    {
        if (!Agent.Wrappers.TryGetValue(json, out var jsonValue))
        {
            try
            {
                _jsonParse ??= EvaluateBuiltIn("JSON.parse");
                jsonValue = Adapter.RunInRealm(EngineRealm, () => Adapter.Call(_jsonParse, Adapter.Undefined, [Adapter.FromString(json.Text)]));
            }
            catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out _))
            {
                jsonValue = Adapter.Null;
            }

            Agent.Wrappers.AddOrUpdate(json, jsonValue);
        }

        return jsonValue;
    }

    /// <summary>
    /// Returns the JavaScript object of a buffer source: the object it was converted from, or a new object of its kind containing
    /// a copy of its bytes, to which the source is then bound (the .NET code and the scripts share the bytes of the object).
    /// </summary>
    private TValue FromBufferSource(JsBufferSource source)
    {
        if (source.Value is TValue value)
            return value;

        var created = FromBuffer(source.Kind, source.Bytes);
        if (Adapter.TryGetBufferSource(created, out _, out _, out var bytes))
        {
            source.Bind(created, bytes);
        }

        return created;
    }

    /// <summary>Converts a .NET value to a JavaScript value, using its runtime type (for the IDL type <c>any</c>).</summary>
    public TValue ToJs(object? value)
    {
        switch (value)
        {
            case null:
                return Adapter.Null;
            case UndefinedValue:
                return Adapter.Undefined;
            case NullValue:
                return Adapter.Null;
            case string s:
                return Adapter.FromString(s);
            case bool b:
                return Adapter.FromBoolean(b);
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                return Adapter.FromNumber(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
            case System.Numerics.BigInteger bigint:
                return Adapter.FromBigInt(bigint);
            case Delegate @delegate:
                return GetDelegateFunction(@delegate);
            case JsCallbackInterface<TAdapter, TValue> callback:
                return callback.Object;
            case IUnionValue union:
                return ToJs(union.Value);
            case Task task when !task.GetType().IsGenericType:
                return ToPromise(task);
            case JsCustomElementConstructor<TAdapter, TValue> customElementConstructor:
                return customElementConstructor.Function;
            case CustomElementConstructor:
                return Adapter.Undefined;
            case DateTime dateTime:
                return CreateDate(dateTime);
            case JsonText json:
                return ParseJson(json);
            case ReportBody { IsIdlDictionary: false } reportBody:
                // A report body defined as an object without IDL (e.g. the coep reports): the object of its JSON serialization
                return ParseJson(new JsonText(reportBody.ToJson()));
            case JsBufferSource source:
                return FromBufferSource(source);
            case byte[] bytes:
                // An ArrayBuffer (e.g. FileReader.result), created once for the array
                if (!Agent.Wrappers.TryGetValue(bytes, out var buffer))
                {
                    buffer = FromBuffer(JsBufferKind.ArrayBuffer, bytes);
                    Agent.Wrappers.AddOrUpdate(bytes, buffer);
                }

                return buffer;

            // The most common platform objects (e.g. the events and their targets passed to the listeners), which are not
            // dictionaries or enumerations: the conversions of the generated bindings are skipped
            case Event or EventTarget when WebIdlBindings<TAdapter, TValue>.GetInterface(value.GetType()) is not null:
                return GetWrapper(value);
        }

        if (WebIdlBindings<TAdapter, TValue>.TryConvertToJs(this, value, out var converted))
            return converted;

        if (WebIdlBindings<TAdapter, TValue>.GetInterface(value.GetType()) is not null)
            return GetWrapper(value);

        // A JavaScript value stored by the DOM (e.g. CustomEvent.detail). It is tested after the .NET types, because the
        // values of an engine may be .NET objects (e.g. an engine whose strings are .NET strings)
        if (TryAsValue(value, out var jsValue))
            return jsValue;

        if (value is IEnumerable enumerable)
        {
            var items = new List<TValue>();
            foreach (var item in enumerable)
            {
                items.Add(ToJs(item));
            }

            return CreateArray(items);
        }

        throw new InvalidOperationException($"The type '{value.GetType()}' cannot be converted to a JavaScript value");
    }

    /// <summary>Returns the JavaScript object of a platform object, or null.</summary>
    internal TValue Wrap(object? value) => value is null ? Adapter.Null : GetWrapper(value);

    internal TValue FromString(string? value) => value is null ? Adapter.Null : Adapter.FromString(value);

    internal TValue FromNumber(double value) => Adapter.FromNumber(value);

    internal TValue FromNumber(double? value) => value is { } number ? Adapter.FromNumber(number) : Adapter.Null;

    internal TValue FromBoolean(bool value) => Adapter.FromBoolean(value);

    internal TValue FromBoolean(bool? value) => value is { } boolean ? Adapter.FromBoolean(boolean) : Adapter.Null;

    /// <summary>Creates an array in this realm.</summary>
    internal TValue CreateArray(IReadOnlyList<TValue> items)
    {
        var array = items as TValue[] ?? [.. items];
        return Adapter.RunInRealm(EngineRealm, () => Adapter.CreateArray(array));
    }

    /// <summary>Converts a sequence to an array (https://webidl.spec.whatwg.org/#js-sequence).</summary>
    internal TValue FromSequence<T>(IEnumerable<T>? values, Func<WebIdlRealm<TAdapter, TValue>, T, TValue> convert)
    {
        if (values is null)
            return Adapter.Null;

        var items = new List<TValue>();
        foreach (var item in values)
        {
            items.Add(convert(this, item));
        }

        return CreateArray(items);
    }

    /// <summary>Converts a record to an object with a data property per entry (https://webidl.spec.whatwg.org/#js-record).</summary>
    internal TValue FromRecord<T>(IEnumerable<KeyValuePair<string, T>>? values, Func<WebIdlRealm<TAdapter, TValue>, T, TValue> convert)
    {
        if (values is null)
            return Adapter.Null;

        var result = CreateObject();
        foreach (var (key, value) in values)
        {
            SetMember(result, key, convert(this, value));
        }

        return result;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-frozen-array: a frozen array, the same object while the .NET object is the same.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "GetValue creates the value with the callback, not with the public parameterless constructor")]
    internal TValue FromFrozenArray<T>(IEnumerable<T>? values, Func<WebIdlRealm<TAdapter, TValue>, T, TValue> convert)
    {
        if (values is null)
            return Adapter.Null;

        return Agent.FrozenArrays.GetValue(values, _ =>
        {
            var array = FromSequence(values, convert);
            return Adapter.Call(EvaluateBuiltIn("Object.freeze"), Adapter.Undefined, [array]);
        });
    }

    /// <summary>Converts a task to a promise (https://webidl.spec.whatwg.org/#es-promise).</summary>
    internal TValue ToPromise(Task task) => ToPromiseCore(task, () => Adapter.Undefined);

    /// <summary>Converts a task to a promise (https://webidl.spec.whatwg.org/#es-promise).</summary>
    internal TValue ToPromise<T>(Task<T> task, Func<WebIdlRealm<TAdapter, TValue>, T, TValue> convert) => ToPromiseCore(task, () => convert(this, task.Result));

    private TValue ToPromiseCore(Task task, Func<TValue> getResult)
    {
        // The promise of a task that was converted while pending, or of a task that represents a promise slot of a platform
        // object (e.g. FontFace.loaded), is the same object at each conversion
        if (Agent.PendingPromises.TryGetValue(task, out var existing))
            return existing;

        if (task.IsCompleted)
        {
            var promiseConstructor = Adapter.Get(Global, Key("Promise"));
            TValue result;
            if (task.IsFaulted)
            {
                var reason = ToJsError(task.Exception!.InnerException!);
                result = Adapter.RunInRealm(EngineRealm, () => Adapter.Call(Adapter.Get(promiseConstructor, Key("reject")), promiseConstructor, [reason]));
            }
            else if (task.IsCanceled)
            {
                result = Adapter.Call(Adapter.Get(promiseConstructor, Key("reject")), promiseConstructor, [CreateError("Error", "The operation was canceled")]);
            }
            else
            {
                result = Adapter.Call(Adapter.Get(promiseConstructor, Key("resolve")), promiseConstructor, [getResult()]);
            }

            if (PromiseSlots.Contains(task))
            {
                Agent.PendingPromises.AddOrUpdate(task, result);
            }

            MarkAsHandledIfNeeded(task, result);
            return result;
        }

        // A pending task: the promise is resolved when the task completes (e.g. customElements.whenDefined())

        _createPromiseCapability ??= EvaluateBuiltIn("(function () { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return [promise, resolve, reject]; })");
        var capability = Adapter.Call(_createPromiseCapability, Adapter.Undefined, []);
        var promise = Adapter.Get(capability, Key("0"));
        var resolve = Adapter.Get(capability, Key("1"));
        var reject = Adapter.Get(capability, Key("2"));
        Agent.PendingPromises.AddOrUpdate(task, promise);
        MarkAsHandledIfNeeded(task, promise);

        // The library completes the task synchronously on the thread of the event loop (e.g. when the element is defined)
        _ = task.ContinueWith(completed =>
        {
            if (completed.IsFaulted)
            {
                // A promise marked as handled after its conversion (e.g. the ready promise of a canceled animation) does not report its rejection
                MarkAsHandledIfNeeded(completed, promise);

                // The exception is created in the realm of the promise (e.g. "a new AbortError DOMException created in navigation's relevant realm"),
                // and the promise is rejected in its realm: its global object tracks the rejection (HostPromiseRejectionTracker
                // uses the current realm, the one of the reject function, when it is not called by a script), not the global
                // object of the script that happens to run (e.g. the script of the parent document that removes the iframe)
                var reason = ToJsError(completed.Exception!.InnerException!);
                Adapter.RunInRealm(EngineRealm, () => Adapter.Call(reject, Adapter.Undefined, [reason]));
            }
            else
            {
                Adapter.Call(resolve, Adapter.Undefined, [getResult()]);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise;
    }

    /// <summary>https://webidl.spec.whatwg.org/#mark-a-promise-as-handled: a reaction that does nothing handles the rejection.</summary>
    private void MarkAsHandledIfNeeded(Task task, TValue promise)
    {
        if (!PromiseSlots.IsMarkedAsHandled(task))
            return;

        _markAsHandled ??= EvaluateBuiltIn("(function (promise) { Promise.prototype.then.call(promise, undefined, () => {}); })");
        Adapter.Call(_markAsHandled, Adapter.Undefined, [promise]);
    }

    /// <summary>
    /// https://webidl.spec.whatwg.org/#wait-for-all: the values are resolved as promises of this realm (a <see cref="RejectedPromise"/>
    /// is a rejected promise), and the success steps run when all are fulfilled, the failure steps when one is rejected.
    /// </summary>
    internal void WaitForAll(IReadOnlyList<object?> values, Action successSteps, Action<object?> failureSteps)
    {
        _waitForAll ??= EvaluateBuiltIn("""
            (function () {
              const then = Promise.prototype.then;
              const resolve = Promise.resolve.bind(Promise);
              const reject = Promise.reject.bind(Promise);
              return function (values, rejected, success, failure) {
                const total = values.length;
                if (total === 0) {
                  then.call(resolve(), () => success());
                  return;
                }

                let fulfilledCount = 0;
                let isRejected = false;
                for (let i = 0; i < total; i++) {
                  const promise = rejected[i] ? reject(values[i]) : resolve(values[i]);
                  then.call(promise, () => {
                    fulfilledCount++;
                    if (fulfilledCount === total) success();
                  }, reason => {
                    if (isRejected) return;
                    isRejected = true;
                    failure(reason);
                  });
                }
              };
            })()
            """);

        var items = new TValue[values.Count];
        var rejected = new TValue[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is RejectedPromise { Reason: var reason })
            {
                items[i] = reason is Exception exception ? Agent.GetExceptionValue(exception, this) : ToJs(reason);
                rejected[i] = Adapter.FromBoolean(true);
            }
            else
            {
                items[i] = TryAsValue(values[i], out var value) ? value : ToJs(values[i]);
                rejected[i] = Adapter.FromBoolean(false);
            }
        }

        var success = Adapter.CreateFunction("", 0, (_, _) =>
        {
            successSteps();
            return Adapter.Undefined;
        });
        var failure = Adapter.CreateFunction("", 1, (_, arguments) =>
        {
            failureSteps(arguments.Length > 0 ? arguments[0] : Adapter.Undefined);
            return Adapter.Undefined;
        });
        Adapter.RunInRealm(EngineRealm, () => Adapter.Call(_waitForAll, Adapter.Undefined, [CreateArray(items), CreateArray(rejected), success, failure]));
    }

    /// <summary>Returns the relevant realm of a platform object (https://html.spec.whatwg.org/multipage/webappapis.html#concept-relevant-realm), or this realm when it is unknown.</summary>
    internal WebIdlRealm<TAdapter, TValue> GetRelevantRealm(TValue platformObject) => Agent.GetAssociatedRealm(platformObject) ?? this;

    /// <summary>Creates an ordinary object of this realm.</summary>
    internal TValue CreateObject() => Adapter.RunInRealm(EngineRealm, () => Adapter.CreateObject(ObjectPrototype));

    /// <summary>CreateDataProperty (https://tc39.es/ecma262/#sec-createdataproperty) with a string key.</summary>
    internal void SetMember(TValue obj, string name, TValue value) => Adapter.DefineOwnProperty(obj, Key(name), JsPropertyDescriptor<TValue>.Data(value, writable: true, enumerable: true, configurable: true));

    /// <summary>Defines a legacy callback interface object (https://webidl.spec.whatwg.org/#legacy-callback-interface-object), e.g. NodeFilter.</summary>
    internal void DefineLegacyCallbackInterface(string name, (string Name, TValue Value)[] constants)
    {
        var interfaceObject = EvaluateBuiltIn($"({{ {name}() {{ throw new TypeError(\"Illegal constructor\"); }} }}).{name}");
        foreach (var (constantName, value) in constants)
        {
            Adapter.DefineOwnProperty(interfaceObject, Key(constantName), JsPropertyDescriptor<TValue>.Data(value, writable: false, enumerable: true, configurable: false));
        }

        DefineMethod(Global, Key(name), interfaceObject, enumerable: false);
    }

    /// <summary>Defines a legacy factory function (https://webidl.spec.whatwg.org/#LegacyFactoryFunction), e.g. Option().</summary>
    internal void DefineLegacyFactoryFunction(string name, string interfaceName, Func<WebIdlRealm<TAdapter, TValue>, ReadOnlySpan<TValue>, TValue> factory)
    {
        Adapter.DefineLazyDataProperty(Global, Key(name), () => Adapter.RunInRealm(EngineRealm, () =>
        {
            var function = Adapter.CreateFunction(name, 0, (_, arguments) =>
            {
                var previousDocument = Document.CurrentGlobalDocumentOrNull;
                Document.CurrentGlobalDocument = GlobalScope.EnvironmentDocument;
                try
                {
                    return factory(this, arguments);
                }
                catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
                {
                    throw ToJsException(ex);
                }
                finally
                {
                    Document.CurrentGlobalDocumentOrNull = previousDocument;
                }
            });
            var constructor = EvaluateBuiltIn("""
                (function (name, factory, prototype) {
                  'use strict';
                  let f;
                  f = { [name]: function (...args) {
                    if (new.target === undefined) throw new TypeError("Failed to construct '" + name + "': Please use the 'new' operator");
                    const result = factory(...args);
                    // The prototype of the object is the one of NewTarget (e.g. a subclass)
                    const newTargetPrototype = new.target.prototype;
                    if (new.target !== f && newTargetPrototype !== null && (typeof newTargetPrototype === 'object' || typeof newTargetPrototype === 'function')) Object.setPrototypeOf(result, newTargetPrototype);
                    return result;
                  } }[name];
                  Object.defineProperty(f, 'prototype', { value: prototype, writable: false, enumerable: false, configurable: false });
                  return f;
                })
                """);
            return Adapter.Call(constructor, Adapter.Undefined, [Key(name), function, GetPrototype(interfaceName)]);
        }), writable: true, enumerable: false, configurable: true);
    }
}

/// <summary>The kind of access of a cross-origin property (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginproperties-(-o-)).</summary>
internal enum CrossOriginAccess
{
    Get,
    Set,
    Call,
}

/// <summary>The .NET members of a setlike declaration (https://webidl.spec.whatwg.org/#idl-setlike).</summary>
internal sealed class SetlikeBinding
{
    /// <summary>The .NET type of the platform objects of a setlike of an interface type (e.g. FontFace), or null for a setlike&lt;DOMString&gt;.</summary>
    public Type? ElementType { get; init; }

    /// <summary>The name of the interface of the values, used in the error messages.</summary>
    public string? ElementName { get; init; }

    public required Func<object, int> Count { get; init; }

    public required Func<object, object, bool> Has { get; init; }

    public required Func<object, IEnumerator> GetEnumerator { get; init; }

    public Action<object, object>? Add { get; init; }

    public Func<object, object, bool>? Delete { get; init; }

    public Action<object>? Clear { get; init; }
}

/// <summary>The .NET members of a maplike declaration whose keys are strings (https://webidl.spec.whatwg.org/#idl-maplike).</summary>
internal sealed class MaplikeBinding
{
    /// <summary>The .NET type of the platform objects of the values (e.g. Highlight).</summary>
    public required Type ValueType { get; init; }

    /// <summary>The name of the interface of the values, used in the error messages.</summary>
    public required string ValueName { get; init; }

    public required Func<object, int> Count { get; init; }

    public required Func<object, string, object?> Get { get; init; }

    public required Func<object, string, bool> Has { get; init; }

    /// <summary>Enumerates the entries; the enumeration sees the changes made while it is in progress, as for a Map.</summary>
    public required Func<object, IEnumerator<(string Key, object Value)>> GetEnumerator { get; init; }

    public Action<object, string, object>? Set { get; init; }

    public Func<object, string, bool>? Delete { get; init; }

    public Action<object>? Clear { get; init; }
}
