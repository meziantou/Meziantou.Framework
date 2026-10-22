using System.Globalization;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Scripting;
using static Meziantou.Framework.WebPlatform.JavaScript.JavaScriptValues;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>
/// A WindowProxy exotic object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object):
/// the indexed properties are the WindowProxy objects of the child navigables, the other internal methods are forwarded to
/// the global object of the active window ([[Window]]) when it is same origin-domain with the current realm, else they are
/// the cross-origin ones. It does not belong to a realm (its prototype is the one of the window).
/// </summary>
/// <remarks>
/// As the specification says, the internal methods do not respect the invariants of the essential internal methods: e.g. a
/// non-configurable property of the window disappears when the browsing context navigates to another document.
/// </remarks>
internal sealed class JavaScriptWindowProxy(JsEngine engine, IJsWindowProxyHandler<object> handler) : JsHostObject(prototype: null)
{
    public IJsWindowProxyHandler<object> Handler => handler;

    private JsObject Window => (JsObject)handler.Window;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getprototypeof
    protected override JsObject? GetPrototypeOfCore() => handler.IsSameOrigin() ? Window.GetPrototype() : null;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-setprototypeof: SetImmutablePrototype
    protected override bool SetPrototypeOfCore(JsObject? prototype) => ReferenceEquals(prototype, GetPrototypeOfCore());

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-isextensible
    protected override bool IsExtensibleCore() => true;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-preventextensions
    protected override bool PreventExtensionsCore() => false;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getownproperty
    protected override bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (key.TryGetArrayIndex(out var index))
        {
            if (GetChild(index) is { } child)
            {
                descriptor = JsPropertyDescriptor.Data(child, writable: false, enumerable: true, configurable: true);
                return true;
            }

            if (handler.IsSameOrigin())
            {
                descriptor = default;
                return false;
            }

            throw handler.CreateSecurityError();
        }

        if (handler.IsSameOrigin())
            return Window.TryGetOwnProperty(key, out descriptor);

        descriptor = ToDescriptor(handler.GetCrossOriginOwnProperty(FromKey(key)));
        return true;
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-defineownproperty
    protected override bool DefineOwnPropertyCore(JsPropertyKey key, in JsPropertyDescriptor descriptor)
    {
        if (!handler.IsSameOrigin())
            throw handler.CreateSecurityError();

        return !key.TryGetArrayIndex(out _) && Window.DefineProperty(key, descriptor);
    }

    // OrdinaryHasProperty: the properties that are not indices are the ones of the window, when it is same origin-domain
    protected override bool HasPropertyCore(JsPropertyKey key)
    {
        if (!key.TryGetArrayIndex(out _) && handler.IsSameOrigin())
            return Window.ContainsProperty(key);

        return base.HasPropertyCore(key);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-get
    protected override JsValue GetCore(JsPropertyKey key, JsValue receiver)
    {
        if (!handler.IsSameOrigin())
            return CrossOriginGet(key, receiver);

        // OrdinaryGet(this, P, Receiver): the window has no indexed properties
        if (key.TryGetArrayIndex(out var index) && GetChild(index) is { } child)
            return child;

        return Window.GetProperty(key, receiver);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-set
    protected override bool SetCore(JsPropertyKey key, JsValue value, JsValue receiver)
    {
        if (!handler.IsSameOrigin())
            return CrossOriginSet(key, value, receiver);

        return !key.TryGetArrayIndex(out _) && Window.SetProperty(key, value, receiver);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-delete
    protected override bool DeleteCore(JsPropertyKey key)
    {
        if (!handler.IsSameOrigin())
            throw handler.CreateSecurityError();

        if (key.TryGetArrayIndex(out var index))
            return GetChild(index) is null;

        return Window.DeleteProperty(key);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-ownpropertykeys
    protected override IReadOnlyList<JsPropertyKey> OwnPropertyKeysCore()
    {
        var count = handler.ChildCount;
        IReadOnlyList<JsPropertyKey> keys = handler.IsSameOrigin() ? Window.GetOwnPropertyKeys() : ToKeys(handler.GetCrossOriginOwnPropertyKeys());
        var result = new List<JsPropertyKey>(count + keys.Count);
        for (var i = 0; i < count; i++)
        {
            result.Add(new JsPropertyKey(i.ToString(CultureInfo.InvariantCulture)));
        }

        result.AddRange(keys);
        return result;
    }

    private JsValue? GetChild(uint index) => index < (uint)handler.ChildCount && handler.GetChild((int)index) is { } child ? ToJs(child) : (JsValue?)null;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginget-(-o,-p,-receiver-)
    private JsValue CrossOriginGet(JsPropertyKey key, JsValue receiver) => CrossOriginObjects.Get(engine, handler, GetCrossOriginDescriptor(key), receiver);

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginset-(-o,-p,-v,-receiver-)
    private bool CrossOriginSet(JsPropertyKey key, JsValue value, JsValue receiver) => CrossOriginObjects.Set(engine, handler, GetCrossOriginDescriptor(key), value, receiver);

    private JsPropertyDescriptor GetCrossOriginDescriptor(JsPropertyKey key)
    {
        // The [[GetOwnProperty]] of a cross-origin window throws when the property is not a cross-origin property
        GetOwnPropertyCore(key, out var descriptor);
        return descriptor;
    }

    private static JsPropertyKey[] ToKeys(IReadOnlyList<object> keys)
    {
        var result = new JsPropertyKey[keys.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ToKey(keys[i]);
        }

        return result;
    }
}

/// <summary>
/// A platform object whose internal methods are the ones of a cross-origin object when it is not same origin-domain with the
/// current realm: a Location object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-location-interface).
/// </summary>
internal sealed class JavaScriptCrossOriginPlatformObject(JsEngine engine, JsObject? prototype, object target, IJsCrossOriginObjectHandler<object> handler)
    : JavaScriptPlatformObject(prototype, target, hasOrdinaryInternalMethods: false)
{
    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-getprototypeof
    protected override JsObject? GetPrototypeOfCore() => handler.IsSameOrigin() ? base.GetPrototypeOfCore() : null;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-setprototypeof: SetImmutablePrototype
    protected override bool SetPrototypeOfCore(JsObject? prototype) => ReferenceEquals(prototype, GetPrototypeOfCore());

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-isextensible
    protected override bool IsExtensibleCore() => true;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-preventextensions
    protected override bool PreventExtensionsCore() => false;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-getownproperty. The [[DefaultProperties]] are not
    // reported as configurable: as in the browsers, the [LegacyUnforgeable] members stay non-configurable
    protected override bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (handler.IsSameOrigin())
            return base.GetOwnPropertyCore(key, out descriptor);

        descriptor = ToDescriptor(handler.GetCrossOriginOwnProperty(FromKey(key)));
        return true;
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-defineownproperty
    protected override bool DefineOwnPropertyCore(JsPropertyKey key, in JsPropertyDescriptor descriptor)
    {
        if (!handler.IsSameOrigin())
            throw handler.CreateSecurityError();

        return base.DefineOwnPropertyCore(key, descriptor);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-get
    protected override JsValue GetCore(JsPropertyKey key, JsValue receiver)
    {
        if (handler.IsSameOrigin())
            return base.GetCore(key, receiver);

        GetOwnPropertyCore(key, out var descriptor);
        return CrossOriginObjects.Get(engine, handler, descriptor, receiver);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-set
    protected override bool SetCore(JsPropertyKey key, JsValue value, JsValue receiver)
    {
        if (handler.IsSameOrigin())
            return base.SetCore(key, value, receiver);

        GetOwnPropertyCore(key, out var descriptor);
        return CrossOriginObjects.Set(engine, handler, descriptor, value, receiver);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-delete
    protected override bool DeleteCore(JsPropertyKey key)
    {
        if (!handler.IsSameOrigin())
            throw handler.CreateSecurityError();

        return base.DeleteCore(key);
    }

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-ownpropertykeys
    protected override IReadOnlyList<JsPropertyKey> OwnPropertyKeysCore()
    {
        if (handler.IsSameOrigin())
            return base.OwnPropertyKeysCore();

        var keys = handler.GetCrossOriginOwnPropertyKeys();
        var result = new JsPropertyKey[keys.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ToKey(keys[i]);
        }

        return result;
    }
}

/// <summary>The abstract operations of the cross-origin objects.</summary>
internal static class CrossOriginObjects
{
    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginget-(-o,-p,-receiver-), from the [[GetOwnProperty]] of the object.</summary>
    public static JsValue Get(JsEngine engine, IJsCrossOriginObjectHandler<object> handler, in JsPropertyDescriptor descriptor, JsValue receiver)
    {
        if (descriptor.IsDataDescriptor)
            return descriptor.Value;

        if (descriptor.Getter.IsUndefined)
            throw handler.CreateSecurityError();

        return engine.Call(descriptor.Getter, receiver);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginset-(-o,-p,-v,-receiver-), from the [[GetOwnProperty]] of the object.</summary>
    public static bool Set(JsEngine engine, IJsCrossOriginObjectHandler<object> handler, in JsPropertyDescriptor descriptor, JsValue value, JsValue receiver)
    {
        if (descriptor.IsAccessorDescriptor && !descriptor.Setter.IsUndefined)
        {
            engine.Call(descriptor.Setter, receiver, value);
            return true;
        }

        throw handler.CreateSecurityError();
    }
}
