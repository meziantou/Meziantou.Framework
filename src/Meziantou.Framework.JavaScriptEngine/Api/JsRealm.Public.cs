namespace Meziantou.Framework.JavaScriptEngine;

// The objects created by the host in a realm.
public sealed partial class JsRealm
{
    /// <summary>Gets %Object.prototype% of the realm.</summary>
    public JsObject ObjectPrototype => Intrinsics.ObjectPrototype;

    /// <summary>Gets %Function.prototype% of the realm.</summary>
    public JsObject FunctionPrototype => Intrinsics.FunctionPrototype;

    /// <summary>
    /// Gets %AbstractModuleSource% of the realm (https://tc39.es/proposal-source-phase-imports/#sec-abstractmodulesource-objects):
    /// the abstract class that the classes of the module source objects (e.g. <c>WebAssembly.Module</c>) extend. It is not a
    /// global. See <see cref="JsModule.ModuleSource"/>.
    /// </summary>
    public JsFunction AbstractModuleSource => Intrinsics.AbstractModuleSource;

    /// <summary>Creates an ordinary object (OrdinaryObjectCreate(proto)).</summary>
    /// <param name="prototype">The prototype, or null for an object without prototype.</param>
    public JsObject CreateObject(JsObject? prototype) => new(prototype);

    /// <summary>Creates an ordinary object whose prototype is %Object.prototype%.</summary>
    public JsObject CreateObject() => new(Intrinsics.ObjectPrototype);

    /// <summary>Creates an array (CreateArrayFromList).</summary>
    public JsArray CreateArray(ReadOnlySpan<JsValue> items) => JsArray.Create(this, items);

    /// <summary>Creates a built-in function (CreateBuiltinFunction): its <c>name</c> and <c>length</c> are set; it is not a constructor.</summary>
    public JsFunction CreateFunction(string name, int length, JsNativeFunction function) => CreateFunction(name, length, function, runInCallerRealm: false);

    /// <summary>Creates a built-in function (CreateBuiltinFunction): its <c>name</c> and <c>length</c> are set; it is not a constructor.</summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="length">The number of parameters of the function.</param>
    /// <param name="function">The implementation of the function.</param>
    /// <param name="runInCallerRealm">
    /// Whether the current realm while the function runs is the realm of its caller instead of the realm of the function
    /// (<see cref="JsEngine.CurrentRealm"/>, the realm of the objects created by the built-ins and the API): e.g. a function
    /// that implements an internal method of an exotic object (the traps of a proxy), which runs in the realm of the code
    /// that uses the object, or the functions of a host whose objects are created in the realm of the calling code.
    /// </param>
    public JsFunction CreateFunction(string name, int length, JsNativeFunction function, bool runInCallerRealm)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(function);
        var result = CreateBuiltinFunction(name, length, (_, thisValue, arguments, _) => function(thisValue, arguments));
        result.RunsInCallerRealm = runInCallerRealm;
        return result;
    }

    /// <summary>Creates a built-in function that is a constructor; its <c>prototype</c> property is not defined.</summary>
    public JsFunction CreateConstructor(string name, int length, JsNativeConstructor constructor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(constructor);
        return CreateBuiltinFunction(name, length, (_, thisValue, arguments, newTarget) => constructor(thisValue, arguments, newTarget), isConstructor: true);
    }

    /// <summary>Creates a native error of the realm, with its stack trace.</summary>
    public JsObject CreateError(JsErrorType type, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var scope = Engine.Enter();
        return CreateError(ToErrorType(type), message);
    }

    /// <summary>Creates the exception that throws a new native error of the realm.</summary>
    public JsException CreateException(JsErrorType type, string message) => new(CreateError(type, message));

    /// <summary>Creates a Date object.</summary>
    /// <param name="timeValue">The time value: milliseconds since the Unix epoch, or NaN for an invalid date.</param>
    public JsObject CreateDate(double timeValue) => new JsDateObject(Intrinsics.DatePrototype, DateOperations.TimeClip(timeValue));

    /// <summary>Creates an ArrayBuffer containing a copy of <paramref name="bytes"/>.</summary>
    public JsObject CreateArrayBuffer(ReadOnlySpan<byte> bytes)
    {
        using var scope = Engine.Enter();
        var buffer = JsArrayBuffer.Create(this, bytes.Length);
        bytes.CopyTo(buffer.GetSpan());
        return buffer;
    }

    /// <summary>
    /// Creates an ArrayBuffer whose data block is <paramref name="memory"/> (the bytes are shared, not copied). When
    /// <paramref name="detachKey"/> is not undefined, the scripts cannot detach nor transfer the buffer (e.g. the buffer of a
    /// <c>WebAssembly.Memory</c>); <see cref="JsBinaryData.DetachArrayBuffer"/> detaches it with the key.
    /// </summary>
    public JsObject CreateArrayBuffer(Memory<byte> memory, JsValue detachKey) => JsArrayBuffer.CreateExternal(this, memory, detachKey);

    /// <summary>Creates a SharedArrayBuffer whose data is a Shared Data Block (e.g. the block of a SharedArrayBuffer of another engine).</summary>
    public JsObject CreateSharedArrayBuffer(JsSharedDataBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return JsArrayBuffer.CreateShared(this, block);
    }

    /// <summary>Creates a typed array or a DataView viewing <paramref name="buffer"/>.</summary>
    /// <param name="kind">The kind of view: <see cref="JsBufferKind.DataView"/> or a typed array.</param>
    /// <param name="buffer">An ArrayBuffer or a SharedArrayBuffer.</param>
    /// <param name="byteOffset">The offset of the view in the buffer.</param>
    /// <param name="length">The number of elements (the byte length of a DataView).</param>
    /// <exception cref="ArgumentException">The byte offset of a typed array is not a multiple of its element size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The view does not fit in the buffer.</exception>
    public JsObject CreateBufferView(JsBufferKind kind, JsObject buffer, int byteOffset, int length)
    {
        if (buffer is not JsArrayBuffer arrayBuffer)
            throw new ArgumentException("The value is not an ArrayBuffer nor a SharedArrayBuffer", nameof(buffer));

        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // The atomic operations access the elements of a typed array as aligned integers: as the typed array constructors
        // do, the offset of an element must be a multiple of its size
        var elementSize = kind is >= JsBufferKind.Int8Array and <= JsBufferKind.Float64Array ? BinaryDataOperations.GetElementSize((TypedArrayElementType)(kind - JsBufferKind.Int8Array)) : 1;
        if (byteOffset % elementSize != 0)
            throw new ArgumentException($"The byte offset must be a multiple of the element size ({elementSize.ToString(System.Globalization.CultureInfo.InvariantCulture)})", nameof(byteOffset));

        if (!arrayBuffer.IsDetached && byteOffset + ((long)length * elementSize) > arrayBuffer.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(length), length, "The view does not fit in the buffer");

        using var scope = Engine.Enter();
        return kind switch
        {
            JsBufferKind.DataView => JsDataView.Create(this, arrayBuffer, byteOffset, length),
            >= JsBufferKind.Int8Array and <= JsBufferKind.Float64Array => JsTypedArray.Create(this, (TypedArrayElementType)(kind - JsBufferKind.Int8Array), arrayBuffer, byteOffset, length),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    internal static ErrorType ToErrorType(JsErrorType type) => type switch
    {
        JsErrorType.Error => ErrorType.Error,
        JsErrorType.EvalError => ErrorType.EvalError,
        JsErrorType.RangeError => ErrorType.RangeError,
        JsErrorType.ReferenceError => ErrorType.ReferenceError,
        JsErrorType.SyntaxError => ErrorType.SyntaxError,
        JsErrorType.TypeError => ErrorType.TypeError,
        JsErrorType.UriError => ErrorType.URIError,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
