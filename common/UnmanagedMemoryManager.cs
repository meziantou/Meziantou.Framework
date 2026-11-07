using System.Buffers;

namespace Meziantou.Framework;

/// <summary>
/// The memory of a block of unmanaged memory that outlives it, such as the memory of a resource of a loaded assembly.
/// It neither copies nor frees the memory.
/// </summary>
/// <remarks>
/// The code has unsafe blocks, not unsafe members, so that it compiles with and without the updated memory safety rules.
/// </remarks>
internal sealed class UnmanagedMemoryManager : MemoryManager<byte>
{
    private readonly nint _address;
    private readonly int _length;

    public UnmanagedMemoryManager(nint address, int length)
    {
        _address = address;
        _length = length;
    }

    public override Span<byte> GetSpan()
    {
        unsafe
        {
            return new Span<byte>((void*)_address, _length);
        }
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        unsafe
        {
            return new MemoryHandle((void*)(_address + elementIndex));
        }
    }

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
