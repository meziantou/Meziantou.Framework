using System.Runtime.InteropServices;

namespace Meziantou.Framework.Http.Caching;

/// <summary>A satisfiable byte range, whose positions are within the representation.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ByteRange(long First, long Last)
{
    public long Length => Last - First + 1;
}
