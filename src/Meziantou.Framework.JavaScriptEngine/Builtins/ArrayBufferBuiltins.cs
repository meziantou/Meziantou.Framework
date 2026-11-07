namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The ArrayBuffer constructor and prototype (https://tc39.es/ecma262/#sec-arraybuffer-objects) and the
/// SharedArrayBuffer constructor and prototype (https://tc39.es/ecma262/#sec-sharedarraybuffer-objects), with the
/// immutable ArrayBuffers of the Immutable ArrayBuffer proposal (https://tc39.es/proposal-immutable-arraybuffer/).
/// </summary>
internal static class ArrayBufferBuiltins
{
    private static readonly PropertyKey MaxByteLengthKey = new("maxByteLength");

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("resize", 1, Resize),
        new("slice", 2, Slice),
        new("sliceToImmutable", 2, SliceToImmutable),
        new("transfer", 0, Transfer),
        new("transferToFixedLength", 0, TransferToFixedLength),
        new("transferToImmutable", 0, TransferToImmutable),
    ];

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.ArrayBufferPrototype;
        var constructor = realm.CreateBuiltinFunction("ArrayBuffer", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "isView", 1, IsView);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, GetSpecies, setter: null);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineAccessor(prototype, "byteLength", GetByteLength, setter: null);
        realm.DefineAccessor(prototype, "detached", GetDetached, setter: null);
        realm.DefineAccessor(prototype, "immutable", GetImmutable, setter: null);
        realm.DefineAccessor(prototype, "maxByteLength", GetMaxByteLength, setter: null);
        realm.DefineAccessor(prototype, "resizable", GetResizable, setter: null);
        realm.DefineMethods(prototype, PrototypeMethods);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "ArrayBuffer", PropertyFlags.Configurable);

        intrinsics.AddGlobal("ArrayBuffer", constructor);
        return constructor;
    }

    public static JsBuiltinFunction InitializeShared(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.SharedArrayBufferPrototype;
        var constructor = realm.CreateBuiltinFunction("SharedArrayBuffer", 1, ConstructShared, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, GetSpecies, setter: null);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineAccessor(prototype, "byteLength", GetSharedByteLength, setter: null);
        realm.DefineMethod(prototype, "grow", 1, Grow);
        realm.DefineAccessor(prototype, "growable", GetGrowable, setter: null);
        realm.DefineAccessor(prototype, "maxByteLength", GetSharedMaxByteLength, setter: null);
        realm.DefineMethod(prototype, "slice", 2, SliceShared);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "SharedArrayBuffer", PropertyFlags.Configurable);

        intrinsics.AddGlobal("SharedArrayBuffer", constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>GetArrayBufferMaxByteLengthOption(options)</summary>
    private static double? GetMaxByteLengthOption(JsValue options)
    {
        if (options.Cell is not JsObject obj)
            return null;

        var maxByteLength = obj.Get(MaxByteLengthKey);
        if (maxByteLength.IsUndefined)
            return null;

        return JsOperations.ToIndex(maxByteLength);
    }

    /// <summary>AllocateArrayBuffer(constructor, byteLength, maxByteLength)</summary>
    public static JsArrayBuffer AllocateArrayBuffer(JsObject constructor, double byteLength, double? maxByteLength)
    {
        if (byteLength > maxByteLength)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("The byte length of the ArrayBuffer exceeds its maximum byte length");

        var prototype = ObjectOperations.GetPrototypeFromConstructor(constructor, static i => i.ArrayBufferPrototype);
        return JsArrayBuffer.Create(prototype, byteLength, maxByteLength);
    }

    /// <summary>A non-shared ArrayBuffer (RequireInternalSlot(O, [[ArrayBufferData]]) and IsSharedArrayBuffer(O) is false).</summary>
    private static JsArrayBuffer ThisArrayBuffer(JsValue thisValue, string method)
    {
        if (thisValue.Cell is not JsArrayBuffer { IsShared: false } buffer)
            return ThrowHelper.ThrowTypeError<JsArrayBuffer>($"Method ArrayBuffer.prototype.{method} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        return buffer;
    }

    /// <summary>A SharedArrayBuffer (RequireInternalSlot(O, [[ArrayBufferData]]) and IsSharedArrayBuffer(O) is true).</summary>
    private static JsArrayBuffer ThisSharedArrayBuffer(JsValue thisValue, string method)
    {
        if (thisValue.Cell is not JsArrayBuffer { IsShared: true } buffer)
            return ThrowHelper.ThrowTypeError<JsArrayBuffer>($"Method SharedArrayBuffer.prototype.{method} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        return buffer;
    }

    /// <summary>ArrayBuffer(length [, options])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor ArrayBuffer requires 'new'");

        var byteLength = JsOperations.ToIndex(Arg(arguments, 0));
        var requestedMaxByteLength = GetMaxByteLengthOption(Arg(arguments, 1));
        return AllocateArrayBuffer(newTarget, byteLength, requestedMaxByteLength);
    }

    /// <summary>ArrayBuffer.isView(arg)</summary>
    private static JsValue IsView(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return Arg(arguments, 0).Cell is JsTypedArray or JsDataView;
    }

    /// <summary>get ArrayBuffer[%Symbol.species%] and get SharedArrayBuffer[%Symbol.species%]</summary>
    private static JsValue GetSpecies(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget) => thisValue;

    /// <summary>get ArrayBuffer.prototype.byteLength</summary>
    private static JsValue GetByteLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisArrayBuffer(thisValue, "byteLength").ByteLength;
    }

    /// <summary>get ArrayBuffer.prototype.detached</summary>
    private static JsValue GetDetached(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisArrayBuffer(thisValue, "detached").IsDetached;
    }

    /// <summary>get ArrayBuffer.prototype.immutable</summary>
    private static JsValue GetImmutable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisArrayBuffer(thisValue, "immutable").IsImmutable;
    }

    /// <summary>get ArrayBuffer.prototype.maxByteLength</summary>
    private static JsValue GetMaxByteLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var buffer = ThisArrayBuffer(thisValue, "maxByteLength");
        if (buffer.IsDetached)
            return 0;

        return buffer.MaxByteLength ?? buffer.ByteLength;
    }

    /// <summary>get ArrayBuffer.prototype.resizable</summary>
    private static JsValue GetResizable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return !ThisArrayBuffer(thisValue, "resizable").IsFixedLength;
    }

    /// <summary>ArrayBuffer.prototype.resize(newLength)</summary>
    private static JsValue Resize(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsArrayBuffer { IsShared: false, MaxByteLength: { } maxByteLength } buffer)
            return ThrowHelper.ThrowTypeError<JsValue>($"Method ArrayBuffer.prototype.resize called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        var newByteLength = JsOperations.ToIndex(Arg(arguments, 0));
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot resize a detached ArrayBuffer");

        if (newByteLength > maxByteLength)
            return ThrowHelper.ThrowRangeError<JsValue>("The new byte length exceeds the maximum byte length of the ArrayBuffer");

        buffer.Resize((int)newByteLength);
        return JsValue.Undefined;
    }

    /// <summary>ArrayBuffer.prototype.slice(start, end)</summary>
    private static JsValue Slice(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var buffer = ThisArrayBuffer(thisValue, "slice");
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot slice a detached ArrayBuffer");

        long length = buffer.ByteLength;
        var first = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var final = BinaryDataOperations.ToClampedIndex(Arg(arguments, 1), length, length);
        var newLength = Math.Max(final - first, 0);

        var constructor = ObjectOperations.SpeciesConstructor(buffer, function.Realm.Intrinsics.ArrayBuffer);
        var result = JsOperations.Construct(constructor, [(double)newLength]);
        if (result.Cell is not JsArrayBuffer newBuffer || newBuffer.IsShared)
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of ArrayBuffer.prototype.slice did not return an ArrayBuffer");

        if (newBuffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of ArrayBuffer.prototype.slice returned a detached ArrayBuffer");

        if (newBuffer.IsImmutable)
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of ArrayBuffer.prototype.slice returned an immutable ArrayBuffer");

        if (ReferenceEquals(newBuffer, buffer))
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of ArrayBuffer.prototype.slice returned the same ArrayBuffer");

        if (newBuffer.ByteLength < newLength)
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of ArrayBuffer.prototype.slice returned a too small ArrayBuffer");

        // The species constructor may have detached or resized the buffer
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot slice a detached ArrayBuffer");

        var currentLength = buffer.ByteLength;
        if (first < currentLength)
        {
            var count = (int)Math.Min(newLength, currentLength - first);
            buffer.GetSpan().Slice((int)first, count).CopyTo(newBuffer.GetSpan());
        }

        return newBuffer;
    }

    /// <summary>ArrayBuffer.prototype.sliceToImmutable(start, end)</summary>
    private static JsValue SliceToImmutable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var buffer = ThisArrayBuffer(thisValue, "sliceToImmutable");
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot slice a detached ArrayBuffer");

        // ResolveBounds(len, start, end)
        long length = buffer.ByteLength;
        var first = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var final = BinaryDataOperations.ToClampedIndex(Arg(arguments, 1), length, length);
        var newLength = Math.Max(final - first, 0);

        // The conversions of the arguments may have detached or resized the buffer
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot slice a detached ArrayBuffer");

        if (buffer.ByteLength < final)
            return ThrowHelper.ThrowRangeError<JsValue>("The ArrayBuffer was shrunk below the end of the slice");

        Span<byte> source = newLength == 0 ? [] : buffer.GetSpan().Slice((int)first, (int)newLength);
        return JsArrayBuffer.CreateImmutable(function.Realm, newLength, source);
    }

    /// <summary>ArrayBuffer.prototype.transfer([newLength])</summary>
    private static JsValue Transfer(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return CopyAndDetach(function.Realm, thisValue, Arg(arguments, 0), CopyAndDetachMode.PreserveResizability, "transfer");
    }

    /// <summary>ArrayBuffer.prototype.transferToFixedLength([newLength])</summary>
    private static JsValue TransferToFixedLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return CopyAndDetach(function.Realm, thisValue, Arg(arguments, 0), CopyAndDetachMode.FixedLength, "transferToFixedLength");
    }

    /// <summary>ArrayBuffer.prototype.transferToImmutable([newLength])</summary>
    private static JsValue TransferToImmutable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return CopyAndDetach(function.Realm, thisValue, Arg(arguments, 0), CopyAndDetachMode.Immutable, "transferToImmutable");
    }

    /// <summary>ArrayBufferCopyAndDetach(arrayBuffer, newLength, mode)</summary>
    private static JsArrayBuffer CopyAndDetach(JsRealm realm, JsValue thisValue, JsValue newLength, CopyAndDetachMode mode, string method)
    {
        var buffer = ThisArrayBuffer(thisValue, method);
        var newByteLength = newLength.IsUndefined ? buffer.ByteLength : JsOperations.ToIndex(newLength);
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsArrayBuffer>("Cannot transfer a detached ArrayBuffer");

        if (buffer.IsImmutable)
            return ThrowHelper.ThrowTypeError<JsArrayBuffer>("Cannot transfer an immutable ArrayBuffer");

        if (!buffer.DetachKey.IsUndefined)
            return ThrowHelper.ThrowTypeError<JsArrayBuffer>("Cannot transfer an ArrayBuffer that has a detach key");

        var newMaxByteLength = mode is CopyAndDetachMode.PreserveResizability ? buffer.MaxByteLength : null;
        if (newByteLength > newMaxByteLength)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("The byte length of the ArrayBuffer exceeds its maximum byte length");

        if (newByteLength > BinaryDataOperations.MaxByteLength)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("Array buffer allocation failed");

        return buffer.TransferAndDetach(realm, (int)newByteLength, newMaxByteLength, immutable: mode is CopyAndDetachMode.Immutable);
    }

    /// <summary>The preserveResizability argument of ArrayBufferCopyAndDetach.</summary>
    private enum CopyAndDetachMode
    {
        PreserveResizability,
        FixedLength,
        Immutable,
    }

    // ------------------------------------------------------------------------------------------------------------------
    // SharedArrayBuffer
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>SharedArrayBuffer(length [, options])</summary>
    private static JsValue ConstructShared(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor SharedArrayBuffer requires 'new'");

        var byteLength = JsOperations.ToIndex(Arg(arguments, 0));
        var requestedMaxByteLength = GetMaxByteLengthOption(Arg(arguments, 1));
        return AllocateSharedArrayBuffer(newTarget, byteLength, requestedMaxByteLength);
    }

    /// <summary>AllocateSharedArrayBuffer(constructor, byteLength, maxByteLength)</summary>
    public static JsArrayBuffer AllocateSharedArrayBuffer(JsObject constructor, double byteLength, double? maxByteLength)
    {
        if (byteLength > maxByteLength)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("The byte length of the SharedArrayBuffer exceeds its maximum byte length");

        var prototype = ObjectOperations.GetPrototypeFromConstructor(constructor, static i => i.SharedArrayBufferPrototype);
        return JsArrayBuffer.CreateShared(prototype, byteLength, maxByteLength);
    }

    /// <summary>get SharedArrayBuffer.prototype.byteLength</summary>
    private static JsValue GetSharedByteLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSharedArrayBuffer(thisValue, "byteLength").ByteLength;
    }

    /// <summary>get SharedArrayBuffer.prototype.growable</summary>
    private static JsValue GetGrowable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return !ThisSharedArrayBuffer(thisValue, "growable").IsFixedLength;
    }

    /// <summary>get SharedArrayBuffer.prototype.maxByteLength</summary>
    private static JsValue GetSharedMaxByteLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var buffer = ThisSharedArrayBuffer(thisValue, "maxByteLength");
        return buffer.MaxByteLength ?? buffer.ByteLength;
    }

    /// <summary>SharedArrayBuffer.prototype.grow(newLength)</summary>
    private static JsValue Grow(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsArrayBuffer { SharedBlock: { IsGrowable: true } block })
            return ThrowHelper.ThrowTypeError<JsValue>($"Method SharedArrayBuffer.prototype.grow called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        var newByteLength = JsOperations.ToIndex(Arg(arguments, 0));
        if (newByteLength > BinaryDataOperations.MaxByteLength || !block.TryGrow((int)newByteLength))
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid length for SharedArrayBuffer.prototype.grow");

        return JsValue.Undefined;
    }

    /// <summary>SharedArrayBuffer.prototype.slice(start, end)</summary>
    private static JsValue SliceShared(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var buffer = ThisSharedArrayBuffer(thisValue, "slice");
        long length = buffer.ByteLength;
        var first = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var final = BinaryDataOperations.ToClampedIndex(Arg(arguments, 1), length, length);
        var newLength = Math.Max(final - first, 0);

        var constructor = ObjectOperations.SpeciesConstructor(buffer, function.Realm.Intrinsics.SharedArrayBuffer);
        var result = JsOperations.Construct(constructor, [(double)newLength]);
        if (result.Cell is not JsArrayBuffer { IsShared: true } newBuffer)
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of SharedArrayBuffer.prototype.slice did not return a SharedArrayBuffer");

        if (newBuffer.HasSameData(buffer))
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of SharedArrayBuffer.prototype.slice returned the same SharedArrayBuffer");

        if (newBuffer.ByteLength < newLength)
            return ThrowHelper.ThrowTypeError<JsValue>("The species constructor of SharedArrayBuffer.prototype.slice returned a too small SharedArrayBuffer");

        // A growable buffer never shrinks: the bytes [first, first + newLength) are still in the buffer
        buffer.GetSpan().Slice((int)first, (int)newLength).CopyTo(newBuffer.GetSpan());
        return newBuffer;
    }
}
