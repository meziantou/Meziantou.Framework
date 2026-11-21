namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>An entry of the node B-tree.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct NbtEntry(uint Nid, ulong BidData, ulong BidSub, uint NidParent);
