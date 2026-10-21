using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The primitives of a JavaScript engine used by the WebIDL bindings (<see cref="WebIdlAgent{TAdapter, TValue}"/>). An engine
/// exposes the DOM by implementing this interface with a struct: the bindings are generic over the adapter
/// (<c>where TAdapter : struct, IJsAdapter&lt;TValue&gt;</c>), so the JIT specializes them for each engine (no boxing, no
/// interface dispatch).
/// </summary>
/// <typeparam name="TValue">The type of the JavaScript values of the engine (e.g. <c>object</c> for Meziantou.Framework.JavaScriptEngine).</typeparam>
/// <remarks>
/// <para>
/// The operations that can run JavaScript code (<see cref="ToNumber"/>, <see cref="Get"/>, <see cref="Call"/>...) throw the
/// exceptions of the engine; <see cref="TryGetThrownValue"/> converts them back to JavaScript values.
/// </para>
/// <para>
/// Realms are represented by opaque objects (<see cref="CurrentRealm"/>). The objects and functions created by the
/// adapter belong to the current realm: the bindings use <see cref="RunInRealm"/> to create them in the realm of a window.
/// </para>
/// <para>
/// Identity: the bindings compare the JavaScript objects by reference and cache data per object (the wrappers of the
/// callbacks, the custom element constructors...), so the adapter must return the same <typeparamref name="TValue"/>
/// instance for the same JavaScript object. <typeparamref name="TValue"/> may be <see cref="object"/> (e.g. when the
/// values of the engine are .NET strings and numbers).
/// </para>
/// <para>
/// Versioning: new primitives are added to this interface as default interface members implemented with the existing
/// primitives, so that the existing adapters keep compiling; an engine overrides them for performance.
/// </para>
/// </remarks>
public interface IJsAdapter<TValue>
    where TValue : class
{
    // ----------------------------------------------------------------------------------------------------------------
    // Values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the <c>undefined</c> value.</summary>
    TValue Undefined { get; }

    /// <summary>Gets the <c>null</c> value.</summary>
    TValue Null { get; }

    /// <summary>Creates a boolean value.</summary>
    TValue FromBoolean(bool value);

    /// <summary>Creates a number value.</summary>
    TValue FromNumber(double value);

    /// <summary>Creates a string value.</summary>
    TValue FromString(string value);

    /// <summary>Creates a BigInt value.</summary>
    TValue FromBigInt(BigInteger value);

    /// <summary>Gets whether the value is <c>undefined</c>.</summary>
    bool IsUndefined(TValue value);

    /// <summary>Gets whether the value is <c>null</c>.</summary>
    bool IsNull(TValue value);

    /// <summary>Gets whether the value is a boolean.</summary>
    bool IsBoolean(TValue value);

    /// <summary>Gets whether the value is a number.</summary>
    bool IsNumber(TValue value);

    /// <summary>Gets whether the value is a BigInt.</summary>
    bool IsBigInt(TValue value);

    /// <summary>Gets whether the value is a string.</summary>
    bool IsString(TValue value);

    /// <summary>Gets whether the value is a symbol.</summary>
    bool IsSymbol(TValue value);

    /// <summary>Gets whether the value is an object (including functions).</summary>
    bool IsObject(TValue value);

    /// <summary>Gets whether the value is callable (https://tc39.es/ecma262/#sec-iscallable).</summary>
    bool IsCallable(TValue value);

    /// <summary>Gets whether the value is a constructor (https://tc39.es/ecma262/#sec-isconstructor).</summary>
    bool IsConstructor(TValue value);

    /// <summary>Gets whether the value is an Array exotic object (https://tc39.es/ecma262/#sec-array-exotic-objects), not a Proxy of an array.</summary>
    bool IsArray(TValue value);

    /// <summary>Gets whether the value is a Date object.</summary>
    bool IsDate(TValue value);

    /// <summary>
    /// Returns whether a .NET object is a value of the engine, and not another .NET object given to the bindings (e.g. a
    /// platform object or a list, which the bindings convert). The default implementation tests whether it is a
    /// <typeparamref name="TValue"/>: an adapter whose values have no specific type (e.g. <typeparamref name="TValue"/> is
    /// <see cref="object"/>) overrides it.
    /// </summary>
    bool IsValue(object value) => value is TValue;

    /// <summary>Returns the value of a boolean.</summary>
    bool AsBoolean(TValue value);

    /// <summary>Returns the value of a number.</summary>
    double AsNumber(TValue value);

    /// <summary>Returns the value of a string.</summary>
    string AsString(TValue value);

    /// <summary>Returns the time value of a Date object (milliseconds since the Unix epoch, or NaN).</summary>
    double AsDate(TValue value);

    /// <summary>Creates a Date object in the current realm.</summary>
    TValue CreateDate(double timeValue);

    // ----------------------------------------------------------------------------------------------------------------
    // Abstract operations (https://tc39.es/ecma262/#sec-type-conversion). They may run JavaScript code.
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://tc39.es/ecma262/#sec-tonumber</summary>
    double ToNumber(TValue value);

    /// <summary>https://tc39.es/ecma262/#sec-tostring (throws a TypeError for a symbol).</summary>
    string ToString(TValue value);

    /// <summary>https://tc39.es/ecma262/#sec-toboolean</summary>
    bool ToBoolean(TValue value);

    /// <summary>https://tc39.es/ecma262/#sec-tobigint</summary>
    BigInteger ToBigInt(TValue value);

    // ----------------------------------------------------------------------------------------------------------------
    // Objects
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>[[Get]] (https://tc39.es/ecma262/#sec-get-o-p). <paramref name="key"/> is a string or a symbol.</summary>
    TValue Get(TValue obj, TValue key);

    /// <summary>[[Set]] (https://tc39.es/ecma262/#sec-set-o-p-v-throw), throwing a TypeError on failure.</summary>
    void Set(TValue obj, TValue key, TValue value);

    /// <summary>[[DefineOwnProperty]] (https://tc39.es/ecma262/#sec-definepropertyorthrow). Returns false on failure.</summary>
    bool DefineOwnProperty(TValue obj, TValue key, in JsPropertyDescriptor<TValue> descriptor);

    /// <summary>[[GetOwnProperty]]. Returns false when the property does not exist.</summary>
    bool GetOwnProperty(TValue obj, TValue key, out JsPropertyDescriptor<TValue> descriptor);

    /// <summary>[[HasProperty]].</summary>
    bool HasProperty(TValue obj, TValue key);

    /// <summary>[[Delete]].</summary>
    bool Delete(TValue obj, TValue key);

    /// <summary>[[OwnPropertyKeys]]: the strings and symbols.</summary>
    IReadOnlyList<TValue> OwnKeys(TValue obj);

    /// <summary>[[GetPrototypeOf]].</summary>
    TValue GetPrototypeOf(TValue obj);

    /// <summary>
    /// Whether the value is a revoked proxy (its [[GetPrototypeOf]] throws). <paramref name="objectPrototype"/> is the
    /// <c>%Object.prototype%</c> of the realm in which the proxy was created, or <see langword="null"/> when the engine does not track it.
    /// </summary>
    bool IsRevokedProxy(TValue value, out TValue? objectPrototype)
    {
        objectPrototype = null;
        return false;
    }

    /// <summary>[[SetPrototypeOf]].</summary>
    bool SetPrototypeOf(TValue obj, TValue prototype);

    /// <summary>Creates an ordinary object in the current realm, whose prototype is <paramref name="prototype"/> (an object or <c>null</c>).</summary>
    TValue CreateObject(TValue prototype);

    /// <summary>Creates an array in the current realm.</summary>
    TValue CreateArray(ReadOnlySpan<TValue> items);

    /// <summary>
    /// Creates a built-in function object in the current realm (https://tc39.es/ecma262/#sec-createbuiltinfunction): its
    /// <c>name</c> and <c>length</c> properties are set, it is not a constructor, and it has no <c>prototype</c> property.
    /// </summary>
    TValue CreateFunction(string name, int length, JsHostFunction<TValue> function);

    /// <summary>[[Call]] (https://tc39.es/ecma262/#sec-call).</summary>
    TValue Call(TValue function, TValue thisValue, ReadOnlySpan<TValue> arguments);

    /// <summary>[[Construct]] (https://tc39.es/ecma262/#sec-construct).</summary>
    TValue Construct(TValue constructor, ReadOnlySpan<TValue> arguments, TValue newTarget);

    // ----------------------------------------------------------------------------------------------------------------
    // Platform objects (https://webidl.spec.whatwg.org/#dfn-platform-object)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates the JavaScript object of a .NET platform object in the current realm. When <paramref name="handler"/> is not
    /// null, the object is a host exotic object whose internal methods call the handler first (the legacy platform objects).
    /// </summary>
    TValue CreatePlatformObject(TValue prototype, object target, IJsHostObjectHandler<TValue>? handler);

    /// <summary>Returns the .NET object of a JavaScript platform object created by <see cref="CreatePlatformObject"/>.</summary>
    bool TryGetPlatformObject(TValue value, [NotNullWhen(true)] out object? target);

    /// <summary>
    /// Performs an ordinary internal method of a host exotic object, bypassing its <see cref="IJsHostObjectHandler{TValue}"/>
    /// (e.g. OrdinaryGetOwnProperty, https://tc39.es/ecma262/#sec-ordinarygetownproperty).
    /// </summary>
    bool OrdinaryGetOwnProperty(TValue obj, TValue key, out JsPropertyDescriptor<TValue> descriptor);

    /// <summary>OrdinaryDefineOwnProperty (https://tc39.es/ecma262/#sec-ordinarydefineownproperty) of a host exotic object.</summary>
    bool OrdinaryDefineOwnProperty(TValue obj, TValue key, in JsPropertyDescriptor<TValue> descriptor);

    /// <summary>OrdinaryDelete (https://tc39.es/ecma262/#sec-ordinarydelete) of a host exotic object.</summary>
    bool OrdinaryDelete(TValue obj, TValue key);

    /// <summary>OrdinaryOwnPropertyKeys (https://tc39.es/ecma262/#sec-ordinaryownpropertykeys) of a host exotic object.</summary>
    IReadOnlyList<TValue> OrdinaryOwnKeys(TValue obj);

    // ----------------------------------------------------------------------------------------------------------------
    // Errors
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the .NET exception that throws <paramref name="value"/> in the engine.</summary>
    Exception CreateException(TValue value);

    /// <summary>Returns the JavaScript value thrown by an exception of the engine, or false for another exception.</summary>
    bool TryGetThrownValue(Exception exception, [NotNullWhen(true)] out TValue? value);

    /// <summary>
    /// Returns whether an exception aborts the running script (e.g. the termination of a worker): it is not a JavaScript
    /// exception, and is not converted by the DOM. The default implementation returns false.
    /// </summary>
    bool IsTerminationException(Exception exception) => false;

    /// <summary>Returns the information of an error value: its message, file name, line and column.</summary>
    JsErrorInformation GetErrorInformation(TValue error);

    // ----------------------------------------------------------------------------------------------------------------
    // Realms and symbols
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the realm of the running execution context.</summary>
    object CurrentRealm { get; }

    /// <summary>Gets the global object of a realm.</summary>
    TValue GetGlobalObject(object realm);

    /// <summary>Runs <paramref name="action"/> with <paramref name="realm"/> as the current realm.</summary>
    TResult RunInRealm<TResult>(object realm, Func<TResult> action);

    /// <summary>Returns whether <paramref name="realm"/> is the realm of the running execution context.</summary>
    bool IsCurrentRealm(object realm) => ReferenceEquals(CurrentRealm, realm);

    /// <summary>Evaluates code in a realm, as an indirect eval (https://tc39.es/ecma262/#sec-performeval). It does not run the jobs.</summary>
    TValue Evaluate(object realm, string code);

    /// <summary>
    /// Evaluates a script of the implementation (a constant source text, e.g. Fetch.js) in a realm, as <see cref="Evaluate"/>:
    /// the adapter may parse it once for all the realms. It does not run the jobs. The default implementation calls
    /// <see cref="Evaluate"/>.
    /// </summary>
    TValue EvaluateBuiltIn(object realm, string code) => Evaluate(realm, code);

    /// <summary>Returns a well-known symbol (https://tc39.es/ecma262/#sec-well-known-symbols).</summary>
    TValue GetWellKnownSymbol(JsWellKnownSymbol symbol);

    /// <summary>
    /// Returns whether a value is an object that has an internal slot, or null when the adapter cannot tell: the structured
    /// serialization then calls the intrinsic function that reads the slot, which throws when the slot is missing (a thrown
    /// exception is slow, and most objects are ordinary objects). The default implementation returns null.
    /// </summary>
    bool? HasInternalSlot(TValue value, JsInternalSlot slot) => null;

    /// <summary>
    /// Defines a data property whose value is created on first access (e.g. the interface objects of the global object,
    /// most of which are never used). The default implementation creates the value immediately.
    /// </summary>
    void DefineLazyDataProperty(TValue obj, TValue key, Func<TValue> factory, bool writable, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        DefineOwnProperty(obj, key, JsPropertyDescriptor<TValue>.Data(factory(), writable, enumerable, configurable));
    }

    /// <summary>
    /// Defines an accessor property whose functions are created on first access (e.g. the attributes of the interfaces, most
    /// of which are never used). <paramref name="setterFactory"/> is null when the property has no setter. The default
    /// implementation creates the functions immediately.
    /// </summary>
    void DefineLazyAccessorProperty(TValue obj, TValue key, Func<TValue> getterFactory, Func<TValue>? setterFactory, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(getterFactory);
        DefineOwnProperty(obj, key, JsPropertyDescriptor<TValue>.Accessor(getterFactory(), setterFactory is null ? Undefined : setterFactory(), enumerable, configurable));
    }

    /// <summary>
    /// Defines a data property whose value is created on first access by <paramref name="factory"/> (one object per property
    /// instead of a closure: the interface objects of a realm are defined this way). The default implementation calls
    /// <see cref="DefineLazyDataProperty(TValue, TValue, Func{TValue}, bool, bool, bool)"/>.
    /// </summary>
    void DefineLazyDataProperty(TValue obj, TValue key, IJsLazyValue<TValue> factory, bool writable, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        DefineLazyDataProperty(obj, key, factory.CreateValue, writable, enumerable, configurable);
    }

    /// <summary>
    /// Defines an accessor property whose functions are created on first access by <paramref name="factory"/> (one object per
    /// property instead of closures: the attributes of the interfaces are defined this way). The default implementation calls
    /// <see cref="DefineLazyAccessorProperty(TValue, TValue, Func{TValue}, Func{TValue}?, bool, bool)"/>.
    /// </summary>
    void DefineLazyAccessorProperty(TValue obj, TValue key, IJsLazyAccessor<TValue> factory, bool hasSetter, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        DefineLazyAccessorProperty(obj, key, factory.CreateGetter, hasSetter ? factory.CreateSetter : null, enumerable, configurable);
    }

    /// <summary>
    /// Prepares an object for <paramref name="count"/> more properties (e.g. the interface objects of a global object), so
    /// that an engine can allocate their storage once. It does not change the behavior of the object. The default
    /// implementation does nothing.
    /// </summary>
    void ReserveProperties(TValue obj, int count)
    {
    }

    /// <summary>
    /// Makes an object an immutable prototype exotic object (https://tc39.es/ecma262/#sec-immutable-prototype-exotic-objects),
    /// if the engine supports it: its [[SetPrototypeOf]] only accepts its current prototype. A proxy is left unchanged (its
    /// handler defines its [[SetPrototypeOf]]). Returns false when the engine does not support it: the bindings then replace
    /// the built-in functions that change prototypes (see <see cref="WebIdlRealm{TAdapter, TValue}"/>). The default
    /// implementation returns false.
    /// </summary>
    bool TryMakePrototypeImmutable(TValue obj) => false;

    /// <summary>
    /// Sets the [[Stack]] internal slot of an error (https://tc39.es/proposal-error-stack-accessor/), read by the
    /// <c>Error.prototype.stack</c> accessor: the stack of a deserialized error
    /// (https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize). Returns false when the engine
    /// has no such slot (the stack is then an own data property of the error). The default implementation returns false.
    /// </summary>
    [Experimental(ExperimentalApis.ErrorStackAccessor, UrlFormat = ExperimentalApis.UrlFormat)]
    bool TrySetErrorStack(TValue error, string stack) => false;

    // ----------------------------------------------------------------------------------------------------------------
    // Buffer sources (https://webidl.spec.whatwg.org/#idl-buffer-source-types)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the kind and the bytes of an <c>ArrayBuffer</c>, a <c>SharedArrayBuffer</c>, a typed array or a <c>DataView</c>
    /// (the objects with an [[ArrayBufferData]], [[TypedArrayName]] or [[DataView]] internal slot), without running
    /// JavaScript code. <paramref name="bytes"/> is a view of the data block of the buffer, so that the writes are visible
    /// to JavaScript; it is empty when the buffer is detached. <paramref name="isShared"/> is true when the buffer (or the
    /// viewed buffer) is a <c>SharedArrayBuffer</c>. Returns false for other values. The default implementation returns
    /// false: the buffer source types cannot be converted.
    /// </summary>
    bool TryGetBufferSource(TValue value, out JsBufferKind kind, out bool isShared, out Memory<byte> bytes)
    {
        kind = default;
        isShared = false;
        bytes = default;
        return false;
    }

    /// <summary>
    /// Returns the shared data block of a <c>SharedArrayBuffer</c> ([[ArrayBufferData]]), an opaque object that
    /// <see cref="SetSharedDataBlock"/> can give to another <c>SharedArrayBuffer</c> of any engine instance of the adapter
    /// (possibly on another thread), so that they share their memory (the structured serialization of the
    /// <c>SharedArrayBuffer</c> objects). Returns null for other values, or when the engine cannot share the memory of its
    /// buffers. The default implementation returns null.
    /// </summary>
    object? GetSharedDataBlock(TValue sharedArrayBuffer) => null;

    /// <summary>
    /// Replaces the data block of a new <c>SharedArrayBuffer</c> of the same byte length by a data block returned by
    /// <see cref="GetSharedDataBlock"/>. Returns false when it is not supported. The default implementation returns false.
    /// </summary>
    bool SetSharedDataBlock(TValue sharedArrayBuffer, object dataBlock) => false;

    /// <summary>
    /// Creates a <c>SharedArrayBuffer</c> in the current realm whose data block is a data block returned by
    /// <see cref="GetSharedDataBlock"/> (of any engine instance of the adapter), so that the buffers share their memory.
    /// Returns null when it is not supported (the bindings then use <see cref="SetSharedDataBlock"/>). The default
    /// implementation returns null.
    /// </summary>
    TValue? CreateSharedArrayBuffer(object dataBlock) => null;

    /// <summary>
    /// Creates an <c>ArrayBuffer</c> containing a copy of <paramref name="bytes"/> in the current realm. The default
    /// implementation constructs it with the <c>ArrayBuffer</c> and <c>Uint8Array</c> constructors of the global object.
    /// </summary>
    TValue CreateArrayBuffer(ReadOnlySpan<byte> bytes)
    {
        var global = GetGlobalObject(CurrentRealm);
        var arrayBufferConstructor = Get(global, FromString("ArrayBuffer"));
        var buffer = Construct(arrayBufferConstructor, [FromNumber(bytes.Length)], arrayBufferConstructor);
        var uint8ArrayConstructor = Get(global, FromString("Uint8Array"));
        var view = Construct(uint8ArrayConstructor, [buffer], uint8ArrayConstructor);
        for (var i = 0; i < bytes.Length; i++)
        {
            Set(view, FromString(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), FromNumber(bytes[i]));
        }

        return buffer;
    }

    /// <summary>
    /// Creates an <c>ArrayBuffer</c> in the current realm whose data block is <paramref name="data"/> (the bytes are shared,
    /// not copied), and whose [[ArrayBufferDetachKey]] is <paramref name="detachKey"/>: it cannot be transferred or detached
    /// by scripts (e.g. the buffer of a <c>WebAssembly.Memory</c>). Returns null when the engine does not support it. The
    /// default implementation returns null.
    /// </summary>
    TValue? CreateArrayBufferWithData(byte[] data, string detachKey) => null;

    /// <summary>Detaches an <c>ArrayBuffer</c> created by <see cref="CreateArrayBufferWithData"/> (DetachArrayBuffer with its detach key).</summary>
    void DetachArrayBuffer(TValue arrayBuffer)
    {
    }

    /// <summary>
    /// Creates a typed array or a <c>DataView</c> (<paramref name="kind"/>) of <paramref name="length"/> elements viewing
    /// <paramref name="buffer"/> from <paramref name="byteOffset"/>, in the current realm. The default implementation calls
    /// the constructor of the global object.
    /// </summary>
    TValue CreateArrayBufferView(JsBufferKind kind, TValue buffer, int byteOffset, int length)
    {
        var constructor = Get(GetGlobalObject(CurrentRealm), FromString(JsBufferSource.GetName(kind)));
        return Construct(constructor, [buffer, FromNumber(byteOffset), FromNumber(length)], constructor);
    }
}

/// <summary>A JavaScript function in a boxed union value (a member type that is a callback function).</summary>
internal sealed record JsFunctionValue<TValue>(TValue Function)
    where TValue : class;

/// <summary>A built-in function implemented in .NET: returns the result of a call with <paramref name="thisValue"/> and <paramref name="arguments"/>.</summary>
public delegate TValue JsHostFunction<TValue>(TValue thisValue, ReadOnlySpan<TValue> arguments);

/// <summary>The internal slots of the built-in objects read by the structured serialization (<see cref="IJsAdapter{TValue}.HasInternalSlot"/>).</summary>
public enum JsInternalSlot
{
    /// <summary>[[BooleanData]] (a Boolean object)</summary>
    BooleanData,

    /// <summary>[[NumberData]] (a Number object)</summary>
    NumberData,

    /// <summary>[[BigIntData]] (a BigInt object)</summary>
    BigIntData,

    /// <summary>[[StringData]] (a String object)</summary>
    StringData,

    /// <summary>[[SymbolData]] (a Symbol object)</summary>
    SymbolData,

    /// <summary>[[DateValue]] (a Date)</summary>
    DateValue,

    /// <summary>[[ArrayBufferData]] of an ArrayBuffer that is not shared</summary>
    ArrayBufferData,

    /// <summary>[[ArrayBufferData]] of a SharedArrayBuffer</summary>
    SharedArrayBufferData,

    /// <summary>[[DataView]] (a DataView)</summary>
    DataView,

    /// <summary>[[MapData]] (a Map)</summary>
    MapData,

    /// <summary>[[SetData]] (a Set)</summary>
    SetData,

    /// <summary>[[WeakMapData]] (a WeakMap)</summary>
    WeakMapData,

    /// <summary>[[WeakSetData]] (a WeakSet)</summary>
    WeakSetData,
}

/// <summary>The well-known symbols used by the bindings.</summary>
public enum JsWellKnownSymbol
{
    /// <summary><c>Symbol.iterator</c></summary>
    Iterator,

    /// <summary><c>Symbol.asyncIterator</c></summary>
    AsyncIterator,

    /// <summary><c>Symbol.toStringTag</c></summary>
    ToStringTag,

    /// <summary><c>Symbol.unscopables</c></summary>
    Unscopables,
}

/// <summary>The information of an error value (https://html.spec.whatwg.org/multipage/webappapis.html#extract-error).</summary>
/// <param name="Message">The message.</param>
/// <param name="Filename">The URL of the script, or an empty string.</param>
/// <param name="LineNumber">The line (1-based), or 0.</param>
/// <param name="ColumnNumber">The column (1-based), or 0.</param>
public readonly record struct JsErrorInformation(string Message, string Filename, int LineNumber, int ColumnNumber);

/// <summary>A property descriptor (https://tc39.es/ecma262/#sec-property-descriptor-specification-type).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory methods of the descriptor, as ImmutableArray<T>.Create")]
public readonly struct JsPropertyDescriptor<TValue>
    where TValue : class
{
    private readonly JsPropertyFlags _flags;

    private JsPropertyDescriptor(TValue? value, TValue? getter, TValue? setter, JsPropertyFlags flags)
    {
        Value = value;
        Getter = getter;
        Setter = setter;
        _flags = flags;
    }

    /// <summary>Creates a data property descriptor.</summary>
    public static JsPropertyDescriptor<TValue> Data(TValue value, bool writable, bool enumerable, bool configurable)
        => new(value, getter: null, setter: null, JsPropertyFlags.HasValue | JsPropertyFlags.HasWritable | JsPropertyFlags.HasEnumerable | JsPropertyFlags.HasConfigurable
            | (writable ? JsPropertyFlags.Writable : JsPropertyFlags.None) | (enumerable ? JsPropertyFlags.Enumerable : JsPropertyFlags.None) | (configurable ? JsPropertyFlags.Configurable : JsPropertyFlags.None));

    /// <summary>Creates an accessor property descriptor. <paramref name="getter"/> and <paramref name="setter"/> are functions or <c>undefined</c>.</summary>
    public static JsPropertyDescriptor<TValue> Accessor(TValue getter, TValue setter, bool enumerable, bool configurable)
        => new(value: null, getter, setter, JsPropertyFlags.HasGet | JsPropertyFlags.HasSet | JsPropertyFlags.HasEnumerable | JsPropertyFlags.HasConfigurable
            | (enumerable ? JsPropertyFlags.Enumerable : JsPropertyFlags.None) | (configurable ? JsPropertyFlags.Configurable : JsPropertyFlags.None));

    /// <summary>Creates a partial descriptor (e.g. the argument of <c>Object.defineProperty</c>).</summary>
    public static JsPropertyDescriptor<TValue> Create(TValue? value, TValue? getter, TValue? setter, JsPropertyFlags flags) => new(value, getter, setter, flags);

    /// <summary>Gets the [[Value]] field, or null when it is absent.</summary>
    public TValue? Value { get; }

    /// <summary>Gets the [[Get]] field, or null when it is absent.</summary>
    public TValue? Getter { get; }

    /// <summary>Gets the [[Set]] field, or null when it is absent.</summary>
    public TValue? Setter { get; }

    /// <summary>Gets the fields and the boolean attributes of the descriptor.</summary>
    public JsPropertyFlags Flags => _flags;

    /// <summary>Gets whether the descriptor is a data descriptor (https://tc39.es/ecma262/#sec-isdatadescriptor).</summary>
    public bool IsDataDescriptor => (_flags & (JsPropertyFlags.HasValue | JsPropertyFlags.HasWritable)) != JsPropertyFlags.None;

    /// <summary>Gets whether the descriptor is an accessor descriptor (https://tc39.es/ecma262/#sec-isaccessordescriptor).</summary>
    public bool IsAccessorDescriptor => (_flags & (JsPropertyFlags.HasGet | JsPropertyFlags.HasSet)) != JsPropertyFlags.None;

    /// <summary>Gets the [[Writable]] attribute.</summary>
    public bool Writable => (_flags & JsPropertyFlags.Writable) != JsPropertyFlags.None;

    /// <summary>Gets the [[Enumerable]] attribute.</summary>
    public bool Enumerable => (_flags & JsPropertyFlags.Enumerable) != JsPropertyFlags.None;

    /// <summary>Gets the [[Configurable]] attribute.</summary>
    public bool Configurable => (_flags & JsPropertyFlags.Configurable) != JsPropertyFlags.None;
}

/// <summary>The fields and attributes of a <see cref="JsPropertyDescriptor{TValue}"/>.</summary>
[Flags]
public enum JsPropertyFlags
{
    /// <summary>No field.</summary>
    None = 0,

    /// <summary>The [[Value]] field is present.</summary>
    HasValue = 1,

    /// <summary>The [[Get]] field is present.</summary>
    HasGet = 2,

    /// <summary>The [[Set]] field is present.</summary>
    HasSet = 4,

    /// <summary>The [[Writable]] field is present.</summary>
    HasWritable = 8,

    /// <summary>The [[Enumerable]] field is present.</summary>
    HasEnumerable = 16,

    /// <summary>The [[Configurable]] field is present.</summary>
    HasConfigurable = 32,

    /// <summary>[[Writable]] is true.</summary>
    Writable = 64,

    /// <summary>[[Enumerable]] is true.</summary>
    Enumerable = 128,

    /// <summary>[[Configurable]] is true.</summary>
    Configurable = 256,
}

/// <summary>
/// The internal methods of a host exotic object created by <see cref="IJsAdapter{TValue}.CreatePlatformObject"/> (the
/// legacy platform objects, https://webidl.spec.whatwg.org/#es-legacy-platform-objects). Each method returns true when it
/// handled the operation; otherwise the adapter performs the ordinary internal method.
/// </summary>
public interface IJsHostObjectHandler<TValue>
    where TValue : class
{
    /// <summary>[[GetOwnProperty]].</summary>
    bool TryGetOwnProperty(TValue obj, TValue key, out JsPropertyDescriptor<TValue> descriptor);

    /// <summary>[[DefineOwnProperty]]. Sets <paramref name="result"/> to the result of the operation.</summary>
    bool TryDefineOwnProperty(TValue obj, TValue key, in JsPropertyDescriptor<TValue> descriptor, out bool result);

    /// <summary>[[Set]] when the receiver is the object. Sets <paramref name="result"/> to the result of the operation.</summary>
    bool TrySet(TValue obj, TValue key, TValue value, out bool result);

    /// <summary>
    /// [[Set]] when the receiver is another object (an object whose prototype chain contains the object). Sets
    /// <paramref name="result"/> to the result of the operation.
    /// </summary>
    bool TrySet(TValue obj, TValue key, TValue value, TValue receiver, out bool result);

    /// <summary>[[Delete]]. Sets <paramref name="result"/> to the result of the operation.</summary>
    bool TryDelete(TValue obj, TValue key, out bool result);

    /// <summary>[[OwnPropertyKeys]]: the keys that precede the ordinary keys, or null.</summary>
    IReadOnlyList<TValue>? GetOwnKeys(TValue obj);
}
