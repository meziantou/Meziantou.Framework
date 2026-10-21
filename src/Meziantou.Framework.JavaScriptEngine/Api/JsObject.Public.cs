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

    /// <summary>Gets whether the object has an internal slot of the built-in objects, without running code.</summary>
    public bool HasInternalSlot(JsInternalSlot slot) => slot switch
    {
        JsInternalSlot.BooleanData => Class == ObjectClass.Boolean,
        JsInternalSlot.NumberData => Class == ObjectClass.Number,
        JsInternalSlot.BigIntData => Class == ObjectClass.BigInt,
        JsInternalSlot.StringData => Class == ObjectClass.String,
        JsInternalSlot.SymbolData => Class == ObjectClass.Symbol,
        JsInternalSlot.DateValue => this is JsDateObject,
        JsInternalSlot.ErrorData => this is JsErrorObject,
        JsInternalSlot.RegExpMatcher => this is JsRegExpObject,
        JsInternalSlot.ArrayBufferData => this is JsArrayBuffer { IsShared: false },
        JsInternalSlot.SharedArrayBufferData => this is JsArrayBuffer { IsShared: true },
        JsInternalSlot.DataView => this is JsDataView,
        JsInternalSlot.TypedArrayName => this is JsTypedArray,
        JsInternalSlot.MapData => this is JsMap,
        JsInternalSlot.SetData => this is JsSet,
        JsInternalSlot.WeakMapData => this is JsWeakMap,
        JsInternalSlot.WeakSetData => this is JsWeakSet,
        JsInternalSlot.PromiseState => this is JsPromise,
        JsInternalSlot.ProxyHandler => this is JsProxy,
        _ => false,
    };

    /// <summary>Gets the [[DateValue]] of a Date object (milliseconds since the Unix epoch, or NaN).</summary>
    public bool TryGetDateValue(out double timeValue)
    {
        if (this is JsDateObject date)
        {
            timeValue = date.DateValue;
            return true;
        }

        timeValue = default;
        return false;
    }

    /// <summary>Gets the primitive value of a Boolean, Number, BigInt, String or Symbol object ([[BooleanData]]...).</summary>
    public bool TryGetPrimitiveValue(out JsValue value)
    {
        switch (this)
        {
            case JsPrimitiveObject primitive:
                value = primitive.PrimitiveValue;
                return true;
            case JsStringObject stringObject:
                value = new JsValue(stringObject.StringData);
                return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the stack trace of an error object (its [[Stack]] internal slot, read by the <c>stack</c> accessor).</summary>
    public string? GetErrorStack() => (this as JsErrorObject)?.StackTrace;

    /// <summary>Sets the stack trace of an error object (e.g. a deserialized error); returns false when the object is not an error.</summary>
    public bool TrySetErrorStack(string stack)
    {
        if (this is not JsErrorObject error)
            return false;

        error.StackTrace = stack;
        return true;
    }

    /// <summary>Gets the state of a promise ([[PromiseState]], [[PromiseResult]] and [[PromiseIsHandled]]); returns false when the object is not a promise.</summary>
    public bool TryGetPromiseState(out JsPromiseState state, out JsValue result, out bool isHandled)
    {
        if (this is JsPromise promise)
        {
            state = (JsPromiseState)promise.State;
            result = promise.Result;
            isHandled = promise.IsHandled;
            return true;
        }

        state = default;
        result = default;
        isHandled = false;
        return false;
    }
}

/// <summary>The state of a promise ([[PromiseState]]).</summary>
public enum JsPromiseState
{
    /// <summary>The promise is pending.</summary>
    Pending,

    /// <summary>The promise is fulfilled.</summary>
    Fulfilled,

    /// <summary>The promise is rejected.</summary>
    Rejected,
}
