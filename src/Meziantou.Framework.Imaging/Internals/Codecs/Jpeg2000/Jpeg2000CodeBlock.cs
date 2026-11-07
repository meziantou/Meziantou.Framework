using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A code-block of a tile (T.800, B.7), and what the packet headers said of it so far (B.10).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Jpeg2000CodeBlock
{
    /// <summary>The column of the first coefficient, from the left edge of the sub-band.</summary>
    public int X;

    /// <summary>The row of the first coefficient, from the top edge of the sub-band.</summary>
    public int Y;

    public ushort Width;

    public ushort Height;

    /// <summary>The index of the first contribution of the code-block, or -1.</summary>
    public int FirstContribution;

    /// <summary>The index of the last contribution of the code-block, or -1.</summary>
    public int LastContribution;

    /// <summary>The number of coding passes the packets hold so far; 0 until the code-block is included in a packet.</summary>
    public ushort PassCount;

    /// <summary>The number of most significant bit-planes that are empty (B.10.5).</summary>
    public byte ZeroBitPlanes;

    /// <summary>Lblock: the number of bits of the length of a codeword segment, before the bits its number of coding passes adds (B.10.7.1).</summary>
    public byte LengthBits;

    /// <summary>1 once the code-block was included in a packet.</summary>
    public byte Included;

    /// <summary>For a code-block of the high-throughput block coder, P0: a third of the number of placeholder passes that precede its HT cleanup pass (T.814, B.1).</summary>
    public byte PlaceholderSets;
}
