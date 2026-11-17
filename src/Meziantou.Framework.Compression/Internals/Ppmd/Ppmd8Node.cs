using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>A free block of the sub-allocator (CPpmd8_Node).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Ppmd8Node
{
    public uint Stamp;
    public uint Next;
    public uint NU;
}
