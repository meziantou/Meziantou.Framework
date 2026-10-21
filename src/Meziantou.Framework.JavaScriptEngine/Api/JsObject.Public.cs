namespace Meziantou.Framework.JavaScriptEngine;

// The public API of the objects. The methods may run JavaScript code (getters, proxies): they must be called on the
// thread of the engine, from a callback of the engine or in a scope of JsEngine.Enter.
public partial class JsObject
{
    /// <summary>[[Get]]: the value of a property (https://tc39.es/ecma262/#sec-get-o-p).</summary>
    public JsValue GetProperty(JsPropertyKey key) => Get(key.Key, new JsValue(this));

    /// <summary>[[Get]] with a receiver (the <c>this</c> value of a getter).</summary>
    public JsValue GetProperty(JsPropertyKey key, JsValue receiver) => Get(key.Key, receiver);

    /// <summary>[[Set]]: assigns a property; returns false when the assignment fails (e.g. a read-only property).</summary>
    public bool SetProperty(JsPropertyKey key, JsValue value) => Set(key.Key, value, new JsValue(this));

    /// <summary>[[Set]] with a receiver.</summary>
    public bool SetProperty(JsPropertyKey key, JsValue value, JsValue receiver) => Set(key.Key, value, receiver);

    /// <summary>[[DefineOwnProperty]]: returns false when the property cannot be defined.</summary>
    public bool DefineProperty(JsPropertyKey key, in JsPropertyDescriptor descriptor) => DefineOwnProperty(key.Key, descriptor.ToInternal());

    /// <summary>[[GetOwnProperty]]: returns false when the object has no such own property.</summary>
    public bool TryGetOwnProperty(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (GetOwnProperty(key.Key, out var internalDescriptor))
        {
            descriptor = JsPropertyDescriptor.FromInternal(internalDescriptor);
            return true;
        }

        descriptor = default;
        return false;
    }

    /// <summary>[[HasProperty]]: whether the object or its prototype chain has the property.</summary>
    public bool ContainsProperty(JsPropertyKey key) => HasProperty(key.Key);

    /// <summary>[[Delete]]: returns false when the property cannot be deleted.</summary>
    public bool DeleteProperty(JsPropertyKey key) => Delete(key.Key);

    /// <summary>[[OwnPropertyKeys]]: the keys of the own properties, in the order of the specification.</summary>
    public IReadOnlyList<JsPropertyKey> GetOwnPropertyKeys()
    {
        var keys = OwnPropertyKeys();
        var result = new JsPropertyKey[keys.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new JsPropertyKey(keys[i]);
        }

        return result;
    }

    /// <summary>[[GetPrototypeOf]]</summary>
    public JsObject? GetPrototype() => GetPrototypeOf();

    /// <summary>[[SetPrototypeOf]]: returns false when the prototype cannot be changed.</summary>
    public bool SetPrototype(JsObject? prototype) => SetPrototypeOf(prototype);

    /// <summary>[[IsExtensible]]</summary>
    public bool GetIsExtensible() => IsExtensible();

    /// <summary>[[PreventExtensions]]</summary>
    public bool PreventObjectExtensions() => PreventExtensions();

    /// <summary>Defines a data property whose value is created on first access (e.g. the interfaces of a global object, most of which are never used).</summary>
    public void DefineLazyDataProperty(JsPropertyKey key, Func<JsValue> factory, bool writable, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var flags = (writable ? PropertyFlags.Writable : 0) | (enumerable ? PropertyFlags.Enumerable : 0) | (configurable ? PropertyFlags.Configurable : 0);
        if (IsExotic)
        {
            DefineOwnProperty(key.Key, PropertyDescriptor.Data(factory(), flags));
            return;
        }

        DefineLazy(key.Key, factory, flags);
    }

    /// <summary>Defines an accessor property whose functions are created on first access; <paramref name="setterFactory"/> is null when the property has no setter.</summary>
    public void DefineLazyAccessorProperty(JsPropertyKey key, Func<JsValue> getterFactory, Func<JsValue>? setterFactory, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(getterFactory);
        var flags = (enumerable ? PropertyFlags.Enumerable : 0) | (configurable ? PropertyFlags.Configurable : 0);
        if (IsExotic)
        {
            DefineOwnProperty(key.Key, PropertyDescriptor.Accessor(getterFactory(), setterFactory is null ? default : setterFactory(), flags));
            return;
        }

        SetOwnSlot(key.Key, new JsValue(new AccessorPair(getterFactory, setterFactory)), flags | PropertyFlags.Accessor);
    }
}
