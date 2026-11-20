namespace Meziantou.Framework.Mail.Internals.Ndb;

/// <summary>A reference to a block or a page: its identifier and its offset in the file.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct Bref(ulong Bid, ulong Ib);
