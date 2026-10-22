using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>How a callback function is invoked (https://webidl.spec.whatwg.org/#invoke-a-callback-function).</summary>
[Flags]
internal enum CallbackBehavior
{
    None = 0,

    /// <summary>The exceptions are reported to the global object of the callback ("report" exception behavior), and the callback returns undefined.</summary>
    ReportExceptions = 1,

    /// <summary>A callback whose realm is not fully active is not invoked and returns undefined (the event listeners); otherwise it throws.</summary>
    IgnoreInactive = 2,

    /// <summary>The callback is invoked even when its realm is not fully active (e.g. the custom element reactions).</summary>
    RunWhenInactive = 4,
}

// The callback functions and the callback interfaces (https://webidl.spec.whatwg.org/#es-callback-function)
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>Returns the .NET delegate of a JavaScript function (the same delegate for the same function and delegate type).</summary>
    internal TDelegate GetDelegate<TDelegate>(TValue function, Func<WebIdlRealm<TAdapter, TValue>, TValue, TDelegate> factory)
        where TDelegate : Delegate
    {
        var cache = Agent.Delegates.GetValue(function, _ => []);
        if (cache.TryGetValue(typeof(TDelegate), out var existing))
            return (TDelegate)existing;

        var result = factory(this, function);
        cache[typeof(TDelegate)] = result;
        Agent.DelegateFunctions.AddOrUpdate(result, function);
        return result;
    }

    /// <summary>Returns the JavaScript function of a delegate created by <see cref="GetDelegate"/>, or null.</summary>
    internal TValue GetDelegateFunction(Delegate? @delegate) => @delegate is not null && Agent.DelegateFunctions.TryGetValue(@delegate, out var function) ? function : Adapter.Null;

    /// <summary>
    /// Invokes a JavaScript function passed as an IDL callback function (https://webidl.spec.whatwg.org/#invoke-a-callback-function).
    /// Returns undefined when the function is not callable ([LegacyTreatNonObjectAsNull]).
    /// </summary>
    internal TValue InvokeCallback(TValue function, TValue thisValue, ReadOnlySpan<TValue> arguments, CallbackBehavior behavior)
    {
        if (!Adapter.IsCallable(function))
            return Adapter.Undefined;

        // The callbacks whose global object is a window of a document that is not fully active are not invoked
        if ((behavior & CallbackBehavior.RunWhenInactive) == CallbackBehavior.None && Agent.IsCallbackRealmInactive(function))
        {
            if ((behavior & CallbackBehavior.IgnoreInactive) != CallbackBehavior.None)
                return Adapter.Undefined;

            throw ToJsException(new DomException("The document of the callback is not fully active", "InvalidStateError"));
        }

        var callbackRealm = Agent.GetAssociatedRealm(function) ?? this;

        using var authorCode = Html.AuthorCodeScope.Enter();
        Agent.Host.OnCallbackInvoking();

        // https://webidl.spec.whatwg.org/#prepare-to-run-a-callback: the realm of the callback is the entry realm
        var previousEntryDocument = Document.EntryGlobalDocument;
        Document.EntryGlobalDocument = callbackRealm.GlobalScope.EnvironmentDocument;
        try
        {
            return Adapter.Call(function, thisValue, arguments);
        }
        catch (Exception ex) when ((behavior & CallbackBehavior.ReportExceptions) != CallbackBehavior.None && Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            // The exceptions of the callbacks are reported to the global of the callback
            Agent.ReportCallbackException(function, this, ex);
            return Adapter.Undefined;
        }
        finally
        {
            Document.EntryGlobalDocument = previousEntryDocument;
            Agent.Host.OnCallbackInvoked(callbackRealm.GlobalScope);
        }
    }

    /// <summary>https://webidl.spec.whatwg.org/#call-a-user-objects-operation</summary>
    internal TValue CallUserObjectOperation(TValue obj, string operationName, ReadOnlySpan<TValue> arguments, TValue thisValue)
    {
        // https://webidl.spec.whatwg.org/#call-a-user-objects-operation: the steps run in the associated realm of the callback
        // object ("prepare to run script"), which is the entry realm: the TypeErrors are created in this realm
        var callbackRealm = Agent.GetAssociatedRealm(obj) ?? this;
        var function = obj;
        Agent.Host.OnCallbackInvoking();
        var previousEntryDocument = Document.EntryGlobalDocument;
        Document.EntryGlobalDocument = callbackRealm.GlobalScope.EnvironmentDocument;
        try
        {
            // Fast path: a function (e.g. an event listener) called from its realm
            if (Adapter.IsCallable(obj) && Adapter.IsCurrentRealm(callbackRealm.EngineRealm))
                return Adapter.Call(obj, thisValue, arguments);

            var argumentArray = arguments.ToArray();
            return Adapter.RunInRealm(callbackRealm.EngineRealm, () =>
            {
                if (!Adapter.IsCallable(obj))
                {
                    function = Adapter.Get(obj, Key(operationName));
                    if (!Adapter.IsCallable(function))
                        throw callbackRealm.CreateTypeError($"'{operationName}' is not a function");

                    thisValue = obj;
                }

                return Adapter.Call(function, thisValue, argumentArray);
            });
        }
        finally
        {
            Document.EntryGlobalDocument = previousEntryDocument;
            Agent.Host.OnCallbackInvoked((ReferenceEquals(function, obj) || !Adapter.IsCallable(function) ? callbackRealm : Agent.GetAssociatedRealm(function) ?? this).GlobalScope);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Event handlers (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-idl-attributes)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The getter of an event handler IDL attribute.</summary>
    internal TValue GetEventHandler(EventTarget target, string type) => target.GetEventHandlerCallback(type) switch
    {
        JsEventHandlerCallback<TAdapter, TValue> callback => callback.Function,
        null => Adapter.Null,
        var callback => ToJs(EventHandlerCallback.ToDelegate(callback)),
    };

    /// <summary>The setter of an event handler IDL attribute ([LegacyTreatNonObjectAsNull]).</summary>
    internal void SetEventHandler(EventTarget target, string type, TValue value, bool isOnBeforeUnloadEventHandler)
    {
        var callback = Adapter.IsObject(value) ? new JsEventHandlerCallback<TAdapter, TValue>(this, value, isOnBeforeUnloadEventHandler) : null;
        target.SetEventHandlerCallback(type, callback);
    }

    /// <summary>Creates the value of an event handler whose function is <paramref name="function"/> (e.g. a compiled event handler content attribute).</summary>
    public EventHandlerCallback CreateEventHandlerCallback(TValue function, bool isOnBeforeUnloadEventHandler)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new JsEventHandlerCallback<TAdapter, TValue>(this, function, isOnBeforeUnloadEventHandler);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Observable arrays (https://webidl.spec.whatwg.org/#es-observable-array)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the observable array exotic object of an attribute: a proxy of an array whose modifications replace the
    /// items of the .NET list (which validates them).
    /// </summary>
    internal TValue GetObservableArray(object target, string attributeName, Func<WebIdlRealm<TAdapter, TValue>, TValue> getItems, Action<WebIdlRealm<TAdapter, TValue>, TValue> setItems)
    {
        var arrays = Agent.ObservableArrays.GetOrCreateValue(target);
        if (arrays.TryGetValue(attributeName, out var existing))
            return existing;

        var getItemsFunction = Adapter.CreateFunction("getItems", 0, (_, _) => getItems(this));
        var setItemsFunction = Adapter.CreateFunction("setItems", 1, (_, arguments) =>
        {
            try
            {
                setItems(this, arguments[0]);
            }
            catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }

            return Adapter.Undefined;
        });

        var create = EvaluateBuiltIn("""
            (function (getItems, setItems) {
              const backing = [];
              const refresh = () => { const items = getItems(); backing.length = 0; for (const item of items) backing.push(item); };
              const isIndex = p => typeof p === 'string' && String(p >>> 0) === p && (p >>> 0) !== 4294967295;
              refresh();
              // The handler has no prototype: the traps that are not defined must not be looked up in Object.prototype
              return new Proxy(backing, {
                __proto__: null,
                get(t, p, r) { refresh(); return Reflect.get(t, p, r); },
                has(t, p) { refresh(); return Reflect.has(t, p); },
                ownKeys(t) { refresh(); return Reflect.ownKeys(t); },
                getOwnPropertyDescriptor(t, p) { refresh(); return Reflect.getOwnPropertyDescriptor(t, p); },
                defineProperty(t, p, d) {
                  if (p !== 'length' && !isIndex(p)) return Reflect.defineProperty(t, p, d);
                  refresh();
                  const copy = t.slice();
                  if (p === 'length') copy.length = d.value; else { if ((p >>> 0) > copy.length) return false; copy[p >>> 0] = d.value; }
                  setItems(copy);
                  refresh();
                  return true;
                },
                set(t, p, v, r) {
                  if (p !== 'length' && !isIndex(p)) return Reflect.set(t, p, v, r);
                  refresh();
                  const copy = t.slice();
                  if (p === 'length') {
                    const length = Number(v);
                    if (length !== (length >>> 0)) throw new RangeError('Invalid array length');
                    if (length > copy.length) return false;
                    copy.length = length;
                  } else {
                    if ((p >>> 0) > copy.length) return false;
                    copy[p >>> 0] = v;
                  }
                  setItems(copy);
                  refresh();
                  return true;
                },
                deleteProperty(t, p) {
                  if (!isIndex(p)) return Reflect.deleteProperty(t, p);
                  refresh();
                  if ((p >>> 0) !== t.length - 1) return false;
                  const copy = t.slice(0, t.length - 1);
                  setItems(copy);
                  refresh();
                  return true;
                },
              });
            })
            """);
        var proxy = Adapter.Call(create, Adapter.Undefined, [getItemsFunction, setItemsFunction]);
        arrays[attributeName] = proxy;
        return proxy;
    }
}

/// <summary>A JavaScript object passed where the IDL expects a callback interface (EventListener, NodeFilter, XPathNSResolver).</summary>
internal sealed class JsCallbackInterface<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, TValue obj) : IEventListener, INodeFilter, IXPathNSResolver, IScriptCallback
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    public TValue Object { get; } = obj;

    public GlobalScope CallbackGlobal => (realm.Agent.GetAssociatedRealm(Object) ?? realm).GlobalScope;

    public void HandleEvent(Event @event)
    {
        // The listeners whose callback's global object is a window of a document that is not fully active are not invoked
        if (realm.Agent.IsCallbackRealmInactive(Object))
            return;

        // https://dom.spec.whatwg.org/#concept-event-listener-inner-invoke: the exception is reported to the listener's global
        using var authorCode = Html.AuthorCodeScope.Enter();
        try
        {
            realm.CallUserObjectOperation(Object, "handleEvent", [realm.ToJs(@event)], thisValue: realm.ToJs(@event.CurrentTarget));
        }
        catch (Exception ex) when (realm.Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            realm.Agent.ReportCallbackException(Object, realm, ex);
        }
    }

    public string? LookupNamespaceUri(string? prefix)
    {
        ThrowIfInactive();
        using var authorCode = Html.AuthorCodeScope.Enter();
        TValue result;
        try
        {
            result = realm.CallUserObjectOperation(Object, "lookupNamespaceURI", [prefix is null ? realm.Adapter.Null : realm.Adapter.FromString(prefix)], thisValue: realm.Adapter.Undefined);
        }
        catch (Exception ex) when (realm.Adapter.TryGetThrownValue(ex, out TValue? _))
        {
            // The exception is reported to the global of the resolver, and the prefix is not resolved
            realm.Agent.ReportCallbackException(Object, realm, ex);
            return null;
        }

        return realm.IsNullOrUndefined(result) ? null : realm.ToDomString(result);
    }

    public ushort AcceptNode(Node node)
    {
        ThrowIfInactive();
        using var authorCode = Html.AuthorCodeScope.Enter();
        var result = realm.CallUserObjectOperation(Object, "acceptNode", [realm.ToJs(node)], thisValue: realm.Adapter.Undefined);
        return realm.ToUnsignedShort(result);
    }

    /// <summary>A callback whose global object is a window of a document that is not fully active cannot run.</summary>
    private void ThrowIfInactive()
    {
        if (realm.Agent.IsCallbackRealmInactive(Object))
            throw realm.ToJsException(new DomException("The document of the callback is not fully active", "InvalidStateError"));
    }
}

/// <summary>An event handler whose value is a JavaScript function (https://html.spec.whatwg.org/multipage/webappapis.html#event-handlers).</summary>
internal sealed class JsEventHandlerCallback<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, TValue function, bool isOnBeforeUnloadEventHandler) : EventHandlerCallback, IScriptCallback
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    public TValue Function { get; } = function;

    public GlobalScope CallbackGlobal => (realm.Agent.GetAssociatedRealm(Function) ?? realm).GlobalScope;

    protected internal override object? Invoke(EventTarget thisValue, object?[] arguments)
    {
        var adapter = realm.Adapter;

        // [LegacyTreatNonObjectAsNull]: a non-callable object is ignored
        if (!adapter.IsCallable(Function))
            return null;

        // The event handlers whose callback's global object is a window of a document that is not fully active are not invoked
        if (realm.Agent.IsCallbackRealmInactive(Function))
            return null;

        var jsArguments = new TValue[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            jsArguments[i] = realm.ToJs(arguments[i]);
        }

        var result = realm.InvokeCallback(Function, realm.ToJs(thisValue), jsArguments, CallbackBehavior.ReportExceptions);
        if (realm.IsNullOrUndefined(result))
            return null;

        if (isOnBeforeUnloadEventHandler)
            return realm.ToDomString(result);

        if (adapter.IsBoolean(result))
            return adapter.AsBoolean(result);

        return result;
    }
}
