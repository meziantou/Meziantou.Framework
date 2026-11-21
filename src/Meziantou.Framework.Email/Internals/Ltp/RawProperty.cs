namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>A property as stored in a property context or a table row: either its bytes, or an HNID that points to them.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct RawProperty(MapiPropertyTag Tag, byte[]? Inline, uint Hnid)
{
    public static RawProperty FromInline(MapiPropertyTag tag, byte[] data) => new(tag, data, 0);

    public static RawProperty FromHnid(MapiPropertyTag tag, uint hnid) => new(tag, null, hnid);
}
