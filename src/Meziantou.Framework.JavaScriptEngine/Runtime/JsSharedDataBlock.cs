using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A Shared Data Block (https://tc39.es/ecma262/#sec-data-blocks): the memory of a SharedArrayBuffer. It can be shared by
/// several engines (agents) running on different threads, e.g. by the structured clone of a SharedArrayBuffer sent to a
/// worker. The memory is native, 16-byte aligned (the atomic operations of 64-bit values are aligned), allocated once
/// for the maximum byte length: a growable block never moves, its byte length grows atomically. The memory is freed by
/// the finalizer, once no SharedArrayBuffer references the block.
/// </summary>
public sealed class JsSharedDataBlock
{
    private const nuint Alignment = 16;

    private readonly NativeMemoryHandle _memory;
    private readonly nint _pointer;
    private readonly Lock _growLock = new();
    private int _byteLength;

    private JsSharedDataBlock(NativeMemoryHandle memory, int byteLength, int? maxByteLength)
    {
        _memory = memory;
        _pointer = memory.DangerousGetHandle();
        _byteLength = byteLength;
        MaxByteLength = maxByteLength;
        memory.AddMemoryPressure(byteLength);
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

        var capacity = (nuint)Math.Max(maxByteLength ?? byteLength, 1);

        // The native memory is not counted by the memory limit of the script, but a block larger than the limit is not created
        JsEngine.EnsureCurrentCanAllocate((long)capacity);
        nint pointer;
        try
        {
            unsafe
            {
                pointer = (nint)NativeMemory.AlignedAlloc(capacity, Alignment);
                NativeMemory.Clear((void*)pointer, (nuint)byteLength);
            }
        }
        catch (OutOfMemoryException)
        {
            return null;
        }

        return new JsSharedDataBlock(new NativeMemoryHandle(pointer), byteLength, maxByteLength);
    }

    /// <summary>Gets the bytes of the block (its current byte length).</summary>
    public Span<byte> GetSpan()
    {
        var length = ByteLength;
        unsafe
        {
            return new Span<byte>((void*)_pointer, length);
        }
    }

    /// <summary>Gets a reference to a byte of the block (for the atomic operations); the index is less than the byte length.</summary>
    public ref byte GetReference(int byteIndex)
    {
        if ((uint)byteIndex >= (uint)ByteLength)
            throw new ArgumentOutOfRangeException(nameof(byteIndex));

        unsafe
        {
            return ref ((byte*)_pointer)[byteIndex];
        }
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

            unsafe
            {
                NativeMemory.Clear((byte*)_pointer + current, (nuint)(newByteLength - current));
            }

            _memory.AddMemoryPressure(newByteLength - current);
            Volatile.Write(ref _byteLength, newByteLength);
            return true;
        }
    }

    /// <summary>Gets the bytes of the block (its current byte length) as a <see cref="Memory{T}"/>, which keeps the block alive.</summary>
    public Memory<byte> GetMemory() => new BlockMemoryManager(this, ByteLength).Memory;

    /// <summary>A memory manager over the native memory of a block.</summary>
    private sealed class BlockMemoryManager(JsSharedDataBlock block, int length) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan()
        {
            unsafe
            {
                return new Span<byte>((void*)block._pointer, length);
            }
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            unsafe
            {
                return new MemoryHandle((byte*)block._pointer + elementIndex);
            }
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>The native memory of a block, freed by the finalizer of the handle once no block references it.</summary>
    private sealed class NativeMemoryHandle : SafeHandle
    {
        private long _memoryPressure;

        public NativeMemoryHandle(nint pointer)
            : base(invalidHandleValue: 0, ownsHandle: true)
        {
            SetHandle(pointer);
        }

        public override bool IsInvalid => handle == 0;

        public void AddMemoryPressure(long bytes)
        {
            if (bytes > 0)
            {
                GC.AddMemoryPressure(bytes);
                Interlocked.Add(ref _memoryPressure, bytes);
            }
        }

        protected override bool ReleaseHandle()
        {
            unsafe
            {
                NativeMemory.AlignedFree((void*)handle);
            }

            var memoryPressure = Interlocked.Exchange(ref _memoryPressure, 0);
            if (memoryPressure > 0)
            {
                GC.RemoveMemoryPressure(memoryPressure);
            }

            return true;
        }
    }
}
