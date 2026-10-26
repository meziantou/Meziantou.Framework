namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>The JavaScript objects of the buffer source types (https://webidl.spec.whatwg.org/#idl-buffer-source-types).</summary>
public enum JsBufferKind
{
    /// <summary>An <c>ArrayBuffer</c>.</summary>
    ArrayBuffer,

    /// <summary>A <c>SharedArrayBuffer</c>.</summary>
    SharedArrayBuffer,

    /// <summary>A <c>DataView</c>.</summary>
    DataView,

    /// <summary>An <c>Int8Array</c>.</summary>
    Int8Array,

    /// <summary>A <c>Uint8Array</c>.</summary>
    Uint8Array,

    /// <summary>A <c>Uint8ClampedArray</c>.</summary>
    Uint8ClampedArray,

    /// <summary>An <c>Int16Array</c>.</summary>
    Int16Array,

    /// <summary>A <c>Uint16Array</c>.</summary>
    Uint16Array,

    /// <summary>An <c>Int32Array</c>.</summary>
    Int32Array,

    /// <summary>A <c>Uint32Array</c>.</summary>
    Uint32Array,

    /// <summary>A <c>BigInt64Array</c>.</summary>
    BigInt64Array,

    /// <summary>A <c>BigUint64Array</c>.</summary>
    BigUint64Array,

    /// <summary>A <c>Float16Array</c>.</summary>
    Float16Array,

    /// <summary>A <c>Float32Array</c>.</summary>
    Float32Array,

    /// <summary>A <c>Float64Array</c>.</summary>
    Float64Array,
}

/// <summary>
/// The IDL value of a buffer source type (https://webidl.spec.whatwg.org/#idl-buffer-source-types): a reference to the
/// JavaScript object, whose bytes are a view of the data block of its buffer (the writes are visible to JavaScript).
/// </summary>
internal sealed class JsBufferSource(JsBufferKind kind, Memory<byte> bytes)
{
    public JsBufferKind Kind { get; } = kind;

    /// <summary>The bytes viewed by the object (empty when its buffer is detached).</summary>
    public Memory<byte> Bytes { get; private set; } = bytes;

    /// <summary>
    /// The JavaScript object (a value of the script engine), or null for bytes created by .NET code: the bindings convert a
    /// source to this object (e.g. the <c>data</c> of an <c>ImageData</c> is the array given to its constructor).
    /// </summary>
    public object? Value { get; private set; }

    /// <summary>Creates the source of a JavaScript object.</summary>
    public static JsBufferSource FromScript(JsBufferKind kind, Memory<byte> bytes, object value) => new(kind, bytes) { Value = value };

    /// <summary>
    /// Binds the source to the JavaScript object created for it (a copy of its bytes): from now on, the bytes are the ones of
    /// the object, so the writes of the scripts and of .NET are visible to each other.
    /// </summary>
    public void Bind(object value, Memory<byte> bytes)
    {
        Value = value;
        Bytes = bytes;
    }

    /// <summary>
    /// Returns whether the buffer of the JavaScript object is detached (e.g. transferred to a worker): the bytes are a view of
    /// the data block it had when it was converted or bound. Set by the bindings for the sources of the scripts.
    /// </summary>
    public Func<bool>? DetachedProbe { get; set; }

    /// <summary>Whether the buffer of the JavaScript object is detached (https://webidl.spec.whatwg.org/#buffersource-detached).</summary>
    public bool IsDetached => DetachedProbe?.Invoke() == true;

    /// <summary>Gets a copy of the bytes (https://webidl.spec.whatwg.org/#dfn-get-buffer-source-copy).</summary>
    public byte[] ToArray() => Bytes.ToArray();

    /// <summary>The names of the JavaScript constructors of the kinds.</summary>
    public static string GetName(JsBufferKind kind) => kind switch
    {
        JsBufferKind.ArrayBuffer => "ArrayBuffer",
        JsBufferKind.SharedArrayBuffer => "SharedArrayBuffer",
        JsBufferKind.DataView => "DataView",
        JsBufferKind.Int8Array => "Int8Array",
        JsBufferKind.Uint8Array => "Uint8Array",
        JsBufferKind.Uint8ClampedArray => "Uint8ClampedArray",
        JsBufferKind.Int16Array => "Int16Array",
        JsBufferKind.Uint16Array => "Uint16Array",
        JsBufferKind.Int32Array => "Int32Array",
        JsBufferKind.Uint32Array => "Uint32Array",
        JsBufferKind.BigInt64Array => "BigInt64Array",
        JsBufferKind.BigUint64Array => "BigUint64Array",
        JsBufferKind.Float16Array => "Float16Array",
        JsBufferKind.Float32Array => "Float32Array",
        JsBufferKind.Float64Array => "Float64Array",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The size of an element of a typed array (1 for the buffers and DataView).</summary>
    public static int GetElementSize(JsBufferKind kind) => kind switch
    {
        JsBufferKind.Int16Array or JsBufferKind.Uint16Array or JsBufferKind.Float16Array => 2,
        JsBufferKind.Int32Array or JsBufferKind.Uint32Array or JsBufferKind.Float32Array => 4,
        JsBufferKind.BigInt64Array or JsBufferKind.BigUint64Array or JsBufferKind.Float64Array => 8,
        _ => 1,
    };
}
