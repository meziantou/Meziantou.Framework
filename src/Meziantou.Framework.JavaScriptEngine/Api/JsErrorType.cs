namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The native error types (https://tc39.es/ecma262/#sec-native-error-types-used-in-this-standard).</summary>
public enum JsErrorType
{
    /// <summary>Error</summary>
    Error,

    /// <summary>EvalError</summary>
    EvalError,

    /// <summary>RangeError</summary>
    RangeError,

    /// <summary>ReferenceError</summary>
    ReferenceError,

    /// <summary>SyntaxError</summary>
    SyntaxError,

    /// <summary>TypeError</summary>
    TypeError,

    /// <summary>URIError</summary>
    UriError,
}

/// <summary>The implementation of a function of the host: returns the result of a call with <paramref name="thisValue"/> and <paramref name="arguments"/>.</summary>
public delegate JsValue JsNativeFunction(JsValue thisValue, ReadOnlySpan<JsValue> arguments);

/// <summary>The implementation of a constructor of the host: <paramref name="newTarget"/> is null for [[Call]], and the new.target of [[Construct]] otherwise.</summary>
public delegate JsValue JsNativeConstructor(JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget);

/// <summary>The internal slots of the built-in objects (<see cref="JsObject.HasInternalSlot"/>).</summary>
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

    /// <summary>[[ErrorData]] (an error)</summary>
    ErrorData,

    /// <summary>[[RegExpMatcher]] (a RegExp)</summary>
    RegExpMatcher,

    /// <summary>[[ArrayBufferData]] of an ArrayBuffer that is not shared</summary>
    ArrayBufferData,

    /// <summary>[[ArrayBufferData]] of a SharedArrayBuffer</summary>
    SharedArrayBufferData,

    /// <summary>[[DataView]] (a DataView)</summary>
    DataView,

    /// <summary>[[TypedArrayName]] (a typed array)</summary>
    TypedArrayName,

    /// <summary>[[MapData]] (a Map)</summary>
    MapData,

    /// <summary>[[SetData]] (a Set)</summary>
    SetData,

    /// <summary>[[WeakMapData]] (a WeakMap)</summary>
    WeakMapData,

    /// <summary>[[WeakSetData]] (a WeakSet)</summary>
    WeakSetData,

    /// <summary>[[PromiseState]] (a Promise)</summary>
    PromiseState,

    /// <summary>[[ProxyHandler]] (a Proxy)</summary>
    ProxyHandler,
}

/// <summary>The kinds of buffer sources: ArrayBuffer, SharedArrayBuffer, DataView and the typed arrays.</summary>
public enum JsBufferKind
{
    /// <summary>ArrayBuffer</summary>
    ArrayBuffer,

    /// <summary>SharedArrayBuffer</summary>
    SharedArrayBuffer,

    /// <summary>DataView</summary>
    DataView,

    /// <summary>Int8Array</summary>
    Int8Array,

    /// <summary>Uint8Array</summary>
    Uint8Array,

    /// <summary>Uint8ClampedArray</summary>
    Uint8ClampedArray,

    /// <summary>Int16Array</summary>
    Int16Array,

    /// <summary>Uint16Array</summary>
    Uint16Array,

    /// <summary>Int32Array</summary>
    Int32Array,

    /// <summary>Uint32Array</summary>
    Uint32Array,

    /// <summary>BigInt64Array</summary>
    BigInt64Array,

    /// <summary>BigUint64Array</summary>
    BigUint64Array,

    /// <summary>Float16Array</summary>
    Float16Array,

    /// <summary>Float32Array</summary>
    Float32Array,

    /// <summary>Float64Array</summary>
    Float64Array,
}
