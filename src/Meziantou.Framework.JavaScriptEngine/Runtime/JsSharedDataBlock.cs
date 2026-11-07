namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A Shared Data Block (https://tc39.es/ecma262/#sec-data-blocks): the memory of a SharedArrayBuffer. It can be shared by
/// several engines (agents) running on different threads, e.g. by the structured clone of a SharedArrayBuffer sent to a
/// worker. The memory is a managed array allocated once for the maximum byte length on the pinned object heap: a growable
/// block never moves, its byte length grows atomically. The bytes are 16-byte aligned (the atomic operations of 64-bit
/// values are aligned). The spans, references and memories of the block reference the array, so they keep the memory
/// alive by themselves, even once no SharedArrayBuffer references the block.
/// </summary>
public sealed class JsSharedDataBlock
{
    private const int Alignment = 16;

    private readonly byte[] _data;
    private readonly int _offset;
    private readonly Lock _growLock = new();
    private int _byteLength;

    private JsSharedDataBlock(byte[] data, int offset, int byteLength, int? maxByteLength)
    {
        _data = data;
        _offset = offset;
        _byteLength = byteLength;
        MaxByteLength = maxByteLength;
    }

    /// <summary>Gets the byte length ([[ArrayBufferByteLengthData]], read with the seq-cst order).</summary>
    public int ByteLength => Volatile.Read(ref _byteLength);

    /// <summary>Gets the maximum byte length of a growable block, or null for a fixed-length block.</summary>
    public int? MaxByteLength { get; }

    /// <summary>Gets whether the block is growable.</summary>
    public bool IsGrowable => MaxByteLength is not null;

    /// <summary>CreateSharedByteDataBlock(size): a zeroed block, or null when the memory cannot be allocated.</summary>
    /// <param name="byteLength">The initial byte length.</param>
    /// <param name="maxByteLength">The maximum byte length of a growable block, or null for a fixed-length block.</param>
    public static JsSharedDataBlock? TryCreate(int byteLength, int? maxByteLength)
    {
        if (byteLength is < 0 or > BinaryDataOperations.MaxByteLength || maxByteLength is < 0 or > BinaryDataOperations.MaxByteLength || byteLength > maxByteLength)
            return null;

        // The array has room to align the first byte of the block
        var capacity = Math.Max(maxByteLength ?? byteLength, 1);
        if (capacity > Array.MaxLength - (Alignment - 1))
            return null;

        JsEngine.EnsureCurrentCanAllocate(capacity + (Alignment - 1));
        byte[] data;
        try
        {
            // The array is pinned, so its address never changes: the alignment of its bytes is computed once. The bytes
            // beyond the byte length are cleared when the block grows.
            data = unsafe(GC.AllocateUninitializedArray<byte>(capacity + (Alignment - 1), pinned: true));
        }
        catch (OutOfMemoryException)
        {
            return null;
        }

        nuint address;
        unsafe
        {
            fixed (byte* pointer = data)
            {
                address = (nuint)pointer;
            }
        }

        var offset = (int)((Alignment - (address % Alignment)) % Alignment);
        data.AsSpan(offset, byteLength).Clear();
        return new JsSharedDataBlock(data, offset, byteLength, maxByteLength);
    }

    /// <summary>Gets the bytes of the block (its current byte length).</summary>
    public Span<byte> GetSpan() => _data.AsSpan(_offset, ByteLength);

    /// <summary>Gets a reference to a byte of the block (for the atomic operations); the index is less than the byte length.</summary>
    public ref byte GetReference(int byteIndex)
    {
        if ((uint)byteIndex >= (uint)ByteLength)
            throw new ArgumentOutOfRangeException(nameof(byteIndex));

        return ref _data[_offset + byteIndex];
    }

    /// <summary>
    /// Grows the block (SharedArrayBuffer.prototype.grow): false when the new length is less than the current length or
    /// greater than the maximum byte length. The new bytes are zeroed before the new length is published.
    /// </summary>
    public bool TryGrow(int newByteLength)
    {
        lock (_growLock)
        {
            var current = _byteLength;
            if (newByteLength == current)
                return true;

            if (newByteLength < current || MaxByteLength is not { } max || newByteLength > max)
                return false;

            _data.AsSpan(_offset + current, newByteLength - current).Clear();
            Volatile.Write(ref _byteLength, newByteLength);
            return true;
        }
    }

    /// <summary>Gets the bytes of the block (its current byte length) as a <see cref="Memory{T}"/>, which keeps the memory alive.</summary>
    public Memory<byte> GetMemory() => _data.AsMemory(_offset, ByteLength);
}
