using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What one packet adds to one code-block: the bytes of consecutive coding passes that belong to one codeword segment.
/// The contributions of a code-block form a list, in the order of the packets.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Jpeg2000Contribution
{
    /// <summary>The index of the next contribution of the code-block, or -1.</summary>
    public int Next;

    /// <summary>The offset of the bytes in the data of the tile.</summary>
    public int Offset;

    /// <summary>The number of bytes.</summary>
    public int Length;

    /// <summary>The number of coding passes.</summary>
    public int PassCount;
}
