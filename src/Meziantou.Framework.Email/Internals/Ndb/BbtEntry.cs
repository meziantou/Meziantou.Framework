namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>An entry of the block B-tree: where a block is, its stored size and, in a 4K file, its inflated size.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct BbtEntry(ulong Bid, ulong Ib, int Size, int InflatedSize, int ReferenceCount);
