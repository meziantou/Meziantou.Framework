namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An ArrayBuffer (https://tc39.es/ecma262/#sec-arraybuffer-objects) or a SharedArrayBuffer
/// (https://tc39.es/ecma262/#sec-sharedarraybuffer-objects): [[ArrayBufferData]], [[ArrayBufferByteLength]],
/// [[ArrayBufferMaxByteLength]], [[ArrayBufferDetachKey]] and the [[ArrayBufferIsImmutable]] of the Immutable ArrayBuffer
/// proposal (https://tc39.es/proposal-immutable-arraybuffer/). The data of an ArrayBuffer is a byte array (its length is
/// the capacity of a resizable buffer) or external memory; the data of a SharedArrayBuffer is a <see cref="JsSharedDataBlock"/>.
/// </summary>
internal sealed class JsArrayBuffer : JsObject
{
    private byte[]? _array;
    private Memory<byte> _external;
    private int _byteLength;
    private bool _isDetached;

    private JsArrayBuffer(JsObject? prototype, ObjectClass objectClass)
        : base(prototype, objectClass)
    {
    }

    /// <summary>Gets whether the buffer is a SharedArrayBuffer (IsSharedArrayBuffer).</summary>
    public bool IsShared => SharedBlock is not null;

    /// <summary>Gets the Shared Data Block of a SharedArrayBuffer.</summary>
    public JsSharedDataBlock? SharedBlock { get; private set; }

    /// <summary>Gets whether the data is external memory (it cannot be resized nor transferred without a copy).</summary>
    public bool IsExternal { get; private set; }

    /// <summary>Gets the [[ArrayBufferMaxByteLength]] of a resizable ArrayBuffer or a growable SharedArrayBuffer, or null for a fixed-length buffer.</summary>
    public int? MaxByteLength { get; private set; }

    /// <summary>Gets whether the buffer is fixed-length (IsFixedLengthArrayBuffer).</summary>
    public bool IsFixedLength => MaxByteLength is null;

    /// <summary>Gets whether the buffer is immutable (IsImmutableBuffer): its data never changes, and it cannot be detached nor resized.</summary>
    public bool IsImmutable { get; private set; }

    /// <summary>Gets the [[ArrayBufferDetachKey]].</summary>
    public JsValue DetachKey { get; set; }

    /// <summary>Gets whether the buffer is detached (IsDetachedBuffer).</summary>
    public bool IsDetached => _isDetached;

    /// <summary>Gets the byte length (ArrayBufferByteLength): 0 when the buffer is detached; the byte length of a growable SharedArrayBuffer is read atomically.</summary>
    public int ByteLength => SharedBlock is { } block ? block.ByteLength : _byteLength;

    /// <summary>Creates an ArrayBuffer with a zeroed data block (AllocateArrayBuffer once the prototype is known), or throws a RangeError when it cannot be allocated.</summary>
    public static JsArrayBuffer Create(JsObject? prototype, double byteLength, double? maxByteLength = null)
    {
        if (byteLength > BinaryDataOperations.MaxByteLength || maxByteLength > BinaryDataOperations.MaxByteLength)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("Array buffer allocation failed");

        JsEngine.EnsureCurrentCanAllocate((long)byteLength);
        byte[] array;
        try
        {
            array = byteLength == 0 ? [] : new byte[(int)byteLength];
        }
        catch (OutOfMemoryException)
        {
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("Array buffer allocation failed");
        }

        return new JsArrayBuffer(prototype, ObjectClass.ArrayBuffer)
        {
            _array = array,
            _byteLength = (int)byteLength,
            MaxByteLength = maxByteLength is { } max ? (int)max : null,
        };
    }

    /// <summary>Creates an ArrayBuffer of the realm with a zeroed data block (AllocateArrayBuffer(%ArrayBuffer%, byteLength)).</summary>
    public static JsArrayBuffer Create(JsRealm realm, double byteLength) => Create(realm.Intrinsics.ArrayBufferPrototype, byteLength);

    /// <summary>
    /// AllocateImmutableArrayBuffer(%ArrayBuffer%, byteLength, fromBlock, fromIndex, count): creates an immutable
    /// ArrayBuffer of the realm whose first bytes are a copy of <paramref name="source"/> (the other bytes are zero), or
    /// throws a RangeError when it cannot be allocated.
    /// </summary>
    public static JsArrayBuffer CreateImmutable(JsRealm realm, double byteLength, ReadOnlySpan<byte> source)
    {
        var result = Create(realm.Intrinsics.ArrayBufferPrototype, byteLength);
        source.CopyTo(result.GetSpan());
        result.IsImmutable = true;
        return result;
    }

    /// <summary>
    /// Creates an ArrayBuffer of the realm over external memory (the embedding owns the memory: the buffer reads and
    /// writes it directly). A detach key prevents the scripts from detaching or transferring the buffer.
    /// </summary>
    public static JsArrayBuffer CreateExternal(JsRealm realm, Memory<byte> memory, JsValue detachKey = default)
    {
        return new JsArrayBuffer(realm.Intrinsics.ArrayBufferPrototype, ObjectClass.ArrayBuffer)
        {
            _external = memory,
            _byteLength = memory.Length,
            IsExternal = true,
            DetachKey = detachKey,
        };
    }

    /// <summary>Creates a SharedArrayBuffer over a new Shared Data Block (AllocateSharedArrayBuffer once the prototype is known), or throws a RangeError.</summary>
    public static JsArrayBuffer CreateShared(JsObject? prototype, double byteLength, double? maxByteLength = null)
    {
        if (byteLength > BinaryDataOperations.MaxByteLength || maxByteLength > BinaryDataOperations.MaxByteLength)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("Array buffer allocation failed");

        var block = JsSharedDataBlock.TryCreate((int)byteLength, maxByteLength is { } max ? (int)max : null);
        if (block is null)
            return ThrowHelper.ThrowRangeError<JsArrayBuffer>("Array buffer allocation failed");

        return CreateShared(prototype, block);
    }

    /// <summary>Creates a SharedArrayBuffer whose data is an existing Shared Data Block (e.g. the structured clone of a SharedArrayBuffer of another agent).</summary>
    public static JsArrayBuffer CreateShared(JsObject? prototype, JsSharedDataBlock block)
    {
        return new JsArrayBuffer(prototype, ObjectClass.SharedArrayBuffer)
        {
            SharedBlock = block,
            MaxByteLength = block.MaxByteLength,
        };
    }

    /// <summary>Creates a SharedArrayBuffer of the realm whose data is an existing Shared Data Block.</summary>
    public static JsArrayBuffer CreateShared(JsRealm realm, JsSharedDataBlock block) => CreateShared(realm.Intrinsics.SharedArrayBufferPrototype, block);

    /// <summary>Gets the bytes of the buffer ([[ArrayBufferData]] up to the byte length); empty when the buffer is detached. It does not run JavaScript code.</summary>
    public Span<byte> GetSpan()
    {
        if (_array is { } array)
            return array.AsSpan(0, _byteLength);

        if (SharedBlock is { } block)
            return block.GetSpan();

        return _external.Span[.._byteLength];
    }

    /// <summary>Gets the bytes of the buffer as a <see cref="Memory{T}"/> (see <see cref="GetSpan"/>).</summary>
    public Memory<byte> GetMemory()
    {
        if (_array is { } array)
            return array.AsMemory(0, _byteLength);

        if (SharedBlock is { } block)
            return block.GetMemory();

        return _external[.._byteLength];
    }

    /// <summary>Gets whether two buffers have the same [[ArrayBufferData]] (two SharedArrayBuffers can share a Shared Data Block).</summary>
    public bool HasSameData(JsArrayBuffer other)
    {
        if (ReferenceEquals(this, other))
            return true;

        return SharedBlock is not null && ReferenceEquals(SharedBlock, other.SharedBlock);
    }

    /// <summary>
    /// DetachArrayBuffer(arrayBuffer, key): throws a TypeError when the key is not the [[ArrayBufferDetachKey]] of the
    /// buffer, or when the buffer is a SharedArrayBuffer or an immutable ArrayBuffer.
    /// </summary>
    public void Detach(JsValue key = default)
    {
        if (IsShared)
        {
            ThrowHelper.ThrowTypeError("Cannot detach a SharedArrayBuffer");
        }

        if (IsImmutable)
        {
            ThrowHelper.ThrowTypeError("Cannot detach an immutable ArrayBuffer");
        }

        if (!JsOperations.SameValue(DetachKey, key))
        {
            ThrowHelper.ThrowTypeError("Cannot detach the ArrayBuffer: the detach key does not match");
        }

        DetachCore();
    }

    private void DetachCore()
    {
        _array = null;
        _external = default;
        _byteLength = 0;
        _isDetached = true;
    }

    /// <summary>
    /// Changes the byte length of a resizable ArrayBuffer (ArrayBuffer.prototype.resize, after the checks): the new
    /// bytes are zeroed. Throws a RangeError when the memory cannot be allocated.
    /// </summary>
    public void Resize(int newByteLength)
    {
        var array = _array!;
        if (newByteLength <= array.Length)
        {
            if (newByteLength < _byteLength)
            {
                // The bytes beyond the length must read as zero if the buffer grows again
                array.AsSpan(newByteLength, _byteLength - newByteLength).Clear();
            }

            _byteLength = newByteLength;
            return;
        }

        byte[] newArray;
        try
        {
            var capacity = (int)Math.Min(Math.Max((long)array.Length * 2, newByteLength), MaxByteLength ?? newByteLength);
            JsEngine.EnsureCurrentCanAllocate(capacity);
            newArray = new byte[capacity];
        }
        catch (OutOfMemoryException)
        {
            ThrowHelper.ThrowRangeError("Array buffer allocation failed");
            return;
        }

        array.AsSpan(0, _byteLength).CopyTo(newArray);
        _array = newArray;
        _byteLength = newByteLength;
    }

    /// <summary>
    /// ArrayBufferCopyAndDetach(arrayBuffer, newLength, mode) after the checks: creates a new ArrayBuffer with the data
    /// (moved without a copy when possible), and detaches this buffer. An immutable result is fixed-length
    /// (<paramref name="newMaxByteLength"/> is null).
    /// </summary>
    public JsArrayBuffer TransferAndDetach(JsRealm realm, int newByteLength, int? newMaxByteLength, bool immutable = false)
    {
        JsArrayBuffer result;
        if (_array is { } array && !IsExternal && newByteLength <= array.Length && (newMaxByteLength is not null || newByteLength == array.Length))
        {
            // Move the data: the bytes between the new length and the old length must read as zero
            if (newByteLength < _byteLength)
            {
                array.AsSpan(newByteLength, _byteLength - newByteLength).Clear();
            }

            result = new JsArrayBuffer(realm.Intrinsics.ArrayBufferPrototype, ObjectClass.ArrayBuffer)
            {
                _array = array,
                _byteLength = newByteLength,
                MaxByteLength = newMaxByteLength,
                IsImmutable = immutable,
            };
        }
        else
        {
            result = Create(realm.Intrinsics.ArrayBufferPrototype, newByteLength, newMaxByteLength);
            var source = GetSpan();
            source[..Math.Min(source.Length, newByteLength)].CopyTo(result.GetSpan());
            result.IsImmutable = immutable;
        }

        DetachCore();
        return result;
    }
}
