using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

// The public API of the objects. The methods that implement the internal methods may run JavaScript code (getters,
// setters, proxies): they must be called on the thread of the engine, from a callback of the engine or in a scope of
// JsEngine.Enter, and they throw an InvalidOperationException otherwise.
public partial class JsObject
{
    /// <summary>[[Get]]: the value of a property (https://tc39.es/ecma262/#sec-get-o-p).</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public JsValue GetProperty(JsPropertyKey key)
    {
        CheckHostAccess();
        return Get(key.Key, new JsValue(this));
    }

    /// <summary>[[Get]] with a receiver (the <c>this</c> value of a getter).</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public JsValue GetProperty(JsPropertyKey key, JsValue receiver)
    {
        CheckHostAccess(receiver);
        return Get(key.Key, receiver);
    }

    /// <summary>[[Set]]: assigns a property; returns false when the assignment fails (e.g. a read-only property).</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object or the value belongs to another engine.</exception>
    public bool SetProperty(JsPropertyKey key, JsValue value)
    {
        CheckHostAccess(value);
        return Set(key.Key, value, new JsValue(this));
    }

    /// <summary>[[Set]] with a receiver.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object or a value belongs to another engine.</exception>
    public bool SetProperty(JsPropertyKey key, JsValue value, JsValue receiver)
    {
        var engine = CheckHostAccess(value);
        CheckValueOwner(engine, receiver);
        return Set(key.Key, value, receiver);
    }

    /// <summary>[[DefineOwnProperty]]: returns false when the property cannot be defined.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object or a value of the descriptor belongs to another engine.</exception>
    public bool DefineProperty(JsPropertyKey key, in JsPropertyDescriptor descriptor)
    {
        var engine = CheckHostAccess(descriptor.Value);
        CheckValueOwner(engine, descriptor.Getter);
        CheckValueOwner(engine, descriptor.Setter);
        return DefineOwnProperty(key.Key, descriptor.ToInternal());
    }

    /// <summary>[[GetOwnProperty]]: returns false when the object has no such own property.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public bool TryGetOwnProperty(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        CheckHostAccess();
        if (GetOwnProperty(key.Key, out var internalDescriptor))
        {
            descriptor = JsPropertyDescriptor.FromInternal(internalDescriptor);
            return true;
        }

        descriptor = default;
        return false;
    }

    /// <summary>[[HasProperty]]: whether the object or its prototype chain has the property.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public bool ContainsProperty(JsPropertyKey key)
    {
        CheckHostAccess();
        return HasProperty(key.Key);
    }

    /// <summary>[[Delete]]: returns false when the property cannot be deleted.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public bool DeleteProperty(JsPropertyKey key)
    {
        CheckHostAccess();
        return Delete(key.Key);
    }

    /// <summary>[[OwnPropertyKeys]]: the keys of the own properties, in the order of the specification.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public IReadOnlyList<JsPropertyKey> GetOwnPropertyKeys()
    {
        CheckHostAccess();
        var keys = OwnPropertyKeys();
        var result = new JsPropertyKey[keys.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new JsPropertyKey(keys[i]);
        }

        return result;
    }

    /// <summary>[[GetPrototypeOf]]</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public JsObject? GetPrototype()
    {
        CheckHostAccess();
        return GetPrototypeOf();
    }

    /// <summary>[[SetPrototypeOf]]: returns false when the prototype cannot be changed.</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object or the prototype belongs to another engine.</exception>
    public bool SetPrototype(JsObject? prototype)
    {
        CheckHostAccess(prototype is null ? default : new JsValue(prototype));
        return SetPrototypeOf(prototype);
    }

    /// <summary>
    /// Makes the object an immutable prototype exotic object (https://tc39.es/ecma262/#sec-immutable-prototype-exotic-objects):
    /// its [[SetPrototypeOf]] is SetImmutablePrototype, which only accepts the current prototype (e.g. the global object of
    /// a window and its prototype chain).
    /// </summary>
    /// <exception cref="InvalidOperationException">The object is a proxy: its handler defines its [[SetPrototypeOf]].</exception>
    public void MakePrototypeImmutable()
    {
        if (this is JsProxy)
            throw new InvalidOperationException("The [[SetPrototypeOf]] of a proxy is defined by its handler");

        Flags |= ObjectFlags.ImmutablePrototype;
    }

    /// <summary>[[IsExtensible]]</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public bool GetIsExtensible()
    {
        CheckHostAccess();
        return IsExtensible();
    }

    /// <summary>[[PreventExtensions]]</summary>
    /// <exception cref="InvalidOperationException">No engine is entered on the thread, or the object belongs to another engine.</exception>
    public bool PreventObjectExtensions()
    {
        CheckHostAccess();
        return PreventExtensions();
    }

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

    /// <summary>
    /// Defines a data property whose value is created on first access by <paramref name="factory"/>, which gets
    /// <paramref name="state"/>: a static callback with a state does not allocate a closure per property.
    /// </summary>
    public void DefineLazyDataProperty(JsPropertyKey key, Func<object?, JsValue> factory, object? state, bool writable, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var flags = (writable ? PropertyFlags.Writable : 0) | (enumerable ? PropertyFlags.Enumerable : 0) | (configurable ? PropertyFlags.Configurable : 0);
        if (IsExotic)
        {
            DefineOwnProperty(key.Key, PropertyDescriptor.Data(factory(state), flags));
            return;
        }

        DefineLazy(key.Key, factory, state, flags);
    }

    /// <summary>
    /// Defines an accessor property whose functions are created on first access by the factories, which get their state;
    /// <paramref name="setterFactory"/> is null when the property has no setter.
    /// </summary>
    public void DefineLazyAccessorProperty(JsPropertyKey key, Func<object?, JsValue> getterFactory, object? getterState, Func<object?, JsValue>? setterFactory, object? setterState, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(getterFactory);
        var flags = (enumerable ? PropertyFlags.Enumerable : 0) | (configurable ? PropertyFlags.Configurable : 0);
        if (IsExotic)
        {
            DefineOwnProperty(key.Key, PropertyDescriptor.Accessor(getterFactory(getterState), setterFactory is null ? default : setterFactory(setterState), flags));
            return;
        }

        // The objects that get lazy properties (prototypes, global objects) get many of them: a table, not a chain of shapes
        GetPropertyTable();
        SetOwnSlot(key.Key, new JsValue(new LazyAccessorPair(getterFactory, getterState, setterFactory, setterState)), flags | PropertyFlags.Accessor);
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

        GetPropertyTable();
        SetOwnSlot(key.Key, new JsValue(new LazyAccessorPair(getterFactory, setterFactory)), flags | PropertyFlags.Accessor);
    }

    /// <summary>
    /// Prepares the object for <paramref name="count"/> more properties (e.g. a host that defines hundreds of properties on a
    /// global object): their storage is allocated once. It does not change the behavior of the object.
    /// </summary>
    public void ReserveProperties(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (IsExotic)
            return;

        var current = _map switch
        {
            PropertyTable table => table.Count,
            Shape shape => shape.Count,
            _ => 0,
        };

        UsePropertyTable(current + count);
    }

    /// <summary>
    /// Gets the number of own properties in the storage of the object, without running code (the elements of an array are not
    /// counted): a host that creates objects with the same properties in each realm can give it to <see cref="ReserveProperties"/>
    /// for the next ones. It is 0 for an exotic object.
    /// </summary>
    public int OwnPropertyCount => IsExotic ? 0 : GetOwnPropertyCount();

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

    /// <summary>
    /// Gets the engine that owns the object, when it is known without running code: the engine of the realm of a function,
    /// or the engine whose shapes the object uses. It is null for the other objects (e.g. an object without properties).
    /// </summary>
    internal JsEngine? KnownOwner => this is JsFunction function ? function.Realm.Engine : (_map as Shape)?.Owner;

    /// <summary>
    /// Checks that a member of the public API is used in a scope of the engine that owns the object (see
    /// <see cref="JsEngine.Enter"/>), and returns this engine.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsEngine CheckHostAccess()
    {
        var engine = JsEngine.Current;
        if (engine is null)
            ThrowNoEngine();

        if (KnownOwner is { } owner && !ReferenceEquals(owner, engine))
            ThrowForeignObject();

        return engine;
    }

    /// <summary><see cref="CheckHostAccess()"/>, and checks that <paramref name="value"/> does not belong to another engine.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsEngine CheckHostAccess(JsValue value)
    {
        var engine = CheckHostAccess();
        CheckValueOwner(engine, value);
        return engine;
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> when the value is an object known to belong to another engine than <paramref name="engine"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CheckValueOwner(JsEngine engine, JsValue value)
    {
        if (value.Cell is JsObject obj && obj.KnownOwner is { } owner && !ReferenceEquals(owner, engine))
            ThrowForeignObject();
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowNoEngine() => throw new InvalidOperationException("No engine is entered on the current thread: use the members of the JavaScript objects in a scope of 'JsEngine.Enter()' (the callbacks of the engine already run in one)");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowForeignObject() => throw new InvalidOperationException("The object belongs to another engine: the objects of an engine cannot be used by another engine (the primitive values can)");

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
