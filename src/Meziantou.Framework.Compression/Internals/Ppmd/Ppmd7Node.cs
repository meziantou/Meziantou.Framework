using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>A free block while the sub-allocator glues the free blocks (RAR_MEM_BLK of unrar).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Ppmd7Node
{
    public ushort Stamp;
    public ushort NU;
    public uint Next;
    public uint Prev;
}
