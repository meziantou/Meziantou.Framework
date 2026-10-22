namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An object implemented by the host (e.g. a platform object of a browser): a derived class can override its internal
/// methods (https://tc39.es/ecma262/#sec-object-internal-methods-and-internal-slots). The default implementations are the
/// ones of an ordinary object, also available with the <c>Ordinary*</c> methods.
/// </summary>
public class JsHostObject : JsObject
{
    /// <summary>Creates a host object.</summary>
    /// <param name="prototype">The prototype of the object, or null.</param>
    /// <param name="target">A .NET object associated with the JavaScript object (e.g. the platform object it exposes).</param>
    public JsHostObject(JsObject? prototype, object? target = null)
        : this(prototype, target, hasOrdinaryInternalMethods: false)
    {
    }

    /// <summary>Creates a host object, telling whether its internal methods are the ordinary ones.</summary>
    /// <param name="prototype">The prototype of the object, or null.</param>
    /// <param name="target">A .NET object associated with the JavaScript object (e.g. the platform object it exposes).</param>
    /// <param name="hasOrdinaryInternalMethods">
    /// <see langword="true"/> when the type of the object does not override the methods that read or write properties
    /// (<see cref="GetOwnPropertyCore"/>, <see cref="DefineOwnPropertyCore"/>, <see cref="HasPropertyCore"/>,
    /// <see cref="GetCore"/>, <see cref="SetCore"/>, <see cref="DeleteCore"/>, <see cref="OwnPropertyKeysCore"/>,
    /// <see cref="GetPrototypeOfCore"/>): the engine then reads and writes its properties with the fast paths of the
    /// ordinary objects (shapes and inline caches) instead of calling these methods.
    /// </param>
    protected JsHostObject(JsObject? prototype, object? target, bool hasOrdinaryInternalMethods)
        : base(prototype, ObjectClass.Object)
    {
        Target = target;
        if (!hasOrdinaryInternalMethods)
        {
            Flags |= ObjectFlags.Exotic;
        }
    }

    /// <summary>Gets the .NET object associated with the JavaScript object.</summary>
    public object? Target { get; }

    /// <summary>Gets or sets whether the object is an [[IsHTMLDDA]] object (<c>document.all</c>): typeof is "undefined", and it is falsy and loosely equal to null.</summary>
    public bool IsHtmlDdaObject
    {
        get => IsHtmlDda;
        set => Flags = value ? Flags | ObjectFlags.IsHtmlDda : Flags & ~ObjectFlags.IsHtmlDda;
    }

    /// <summary>
    /// Gets or sets whether the object has a [[Call]] internal method, implemented by <see cref="CallCore"/> (e.g. the legacy
    /// caller of <c>document.all</c>): typeof is "function" unless it is an [[IsHTMLDDA]] object. It has no [[Construct]].
    /// </summary>
    public bool HasCallInternalMethod
    {
        get => IsCallable;
        set => Flags = value ? Flags | ObjectFlags.Callable : Flags & ~ObjectFlags.Callable;
    }

    /// <summary>Gets or sets whether the prototype of the object cannot be changed (an immutable prototype exotic object, e.g. <c>Object.prototype</c> or a <c>Window</c>).</summary>
    public bool HasImmutablePrototype { get; set; }

    // ------------------------------------------------------------------------------------------------------------------
    // Overridable internal methods
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>[[GetPrototypeOf]]</summary>
    protected virtual JsObject? GetPrototypeOfCore() => PrototypeSlot;

    /// <summary>[[SetPrototypeOf]]</summary>
    protected virtual bool SetPrototypeOfCore(JsObject? prototype)
    {
        if (HasImmutablePrototype)
            return ReferenceEquals(prototype, GetPrototypeOf());

        return OrdinarySetPrototypeOf(prototype);
    }

    /// <summary>[[IsExtensible]]</summary>
    protected virtual bool IsExtensibleCore() => IsExtensibleSlot;

    /// <summary>[[PreventExtensions]]</summary>
    protected virtual bool PreventExtensionsCore() => base.PreventExtensions();

    /// <summary>[[GetOwnProperty]]</summary>
    protected virtual bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor) => OrdinaryGetOwnProperty(key, out descriptor);

    /// <summary>[[DefineOwnProperty]]</summary>
    protected virtual bool DefineOwnPropertyCore(JsPropertyKey key, in JsPropertyDescriptor descriptor) => OrdinaryDefineOwnProperty(key, descriptor);

    /// <summary>[[HasProperty]]</summary>
    protected virtual bool HasPropertyCore(JsPropertyKey key) => OrdinaryHasProperty(key.Key);

    /// <summary>[[Get]]</summary>
    protected virtual JsValue GetCore(JsPropertyKey key, JsValue receiver) => OrdinaryGet(key.Key, receiver);

    /// <summary>[[Set]]</summary>
    protected virtual bool SetCore(JsPropertyKey key, JsValue value, JsValue receiver) => OrdinarySet(key.Key, value, receiver);

    /// <summary>[[Delete]]</summary>
    protected virtual bool DeleteCore(JsPropertyKey key) => OrdinaryDelete(key);

    /// <summary>[[OwnPropertyKeys]]</summary>
    protected virtual IReadOnlyList<JsPropertyKey> OwnPropertyKeysCore() => OrdinaryOwnPropertyKeys();

    /// <summary>[[Call]], when <see cref="HasCallInternalMethod"/> is true. It runs in the realm of the caller.</summary>
    protected virtual JsValue CallCore(JsValue thisValue, ReadOnlySpan<JsValue> arguments) => ThrowHelper.ThrowTypeError<JsValue>("object is not a function");

    // ------------------------------------------------------------------------------------------------------------------
    // Ordinary internal methods
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>OrdinaryGetOwnProperty (https://tc39.es/ecma262/#sec-ordinarygetownproperty), bypassing the overrides.</summary>
    public bool OrdinaryGetOwnProperty(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (OrdinaryGetOwnProperty(key.Key, out var internalDescriptor))
        {
            descriptor = JsPropertyDescriptor.FromInternal(internalDescriptor);
            return true;
        }

        descriptor = default;
        return false;
    }

    /// <summary>OrdinaryDefineOwnProperty (https://tc39.es/ecma262/#sec-ordinarydefineownproperty), bypassing the overrides.</summary>
    public bool OrdinaryDefineOwnProperty(JsPropertyKey key, in JsPropertyDescriptor descriptor) => OrdinaryDefineOwnProperty(key.Key, descriptor.ToInternal());

    /// <summary>OrdinaryDelete (https://tc39.es/ecma262/#sec-ordinarydelete), bypassing the overrides.</summary>
    public bool OrdinaryDelete(JsPropertyKey key) => OrdinaryDelete(key.Key);

    /// <summary>OrdinaryOwnPropertyKeys (https://tc39.es/ecma262/#sec-ordinaryownpropertykeys), bypassing the overrides.</summary>
    public new IReadOnlyList<JsPropertyKey> OrdinaryOwnPropertyKeys()
    {
        var keys = base.OrdinaryOwnPropertyKeys();
        var result = new JsPropertyKey[keys.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new JsPropertyKey(keys[i]);
        }

        return result;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The internal methods call the overridable methods
    // ------------------------------------------------------------------------------------------------------------------

    internal sealed override JsObject? GetPrototypeOf() => GetPrototypeOfCore();

    internal sealed override bool SetPrototypeOf(JsObject? prototype) => SetPrototypeOfCore(prototype);

    internal sealed override bool IsExtensible() => IsExtensibleCore();

    internal sealed override bool PreventExtensions() => PreventExtensionsCore();

    internal sealed override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (GetOwnPropertyCore(new JsPropertyKey(key), out var publicDescriptor))
        {
            descriptor = publicDescriptor.ToInternal();
            return true;
        }

        descriptor = default;
        return false;
    }

    internal sealed override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor) => DefineOwnPropertyCore(new JsPropertyKey(key), JsPropertyDescriptor.FromInternal(descriptor));

    internal sealed override bool HasProperty(in PropertyKey key) => HasPropertyCore(new JsPropertyKey(key));

    internal sealed override JsValue Get(in PropertyKey key, JsValue receiver) => GetCore(new JsPropertyKey(key), receiver);

    internal sealed override bool Set(in PropertyKey key, JsValue value, JsValue receiver) => SetCore(new JsPropertyKey(key), value, receiver);

    internal sealed override bool Delete(in PropertyKey key) => DeleteCore(new JsPropertyKey(key));

    internal sealed override JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        if (!IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("object is not a function");

        return CallCore(thisValue, arguments);
    }

    internal sealed override List<PropertyKey> OwnPropertyKeys()
    {
        var keys = OwnPropertyKeysCore();
        var result = new List<PropertyKey>(keys.Count);
        foreach (var key in keys)
        {
            result.Add(key.Key);
        }

        return result;
    }
}
