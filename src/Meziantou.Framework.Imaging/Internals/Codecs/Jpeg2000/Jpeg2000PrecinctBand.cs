using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The code-blocks of one sub-band that belong to one precinct (T.800, B.7), with the two tag trees of the packet headers (B.10.2).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Jpeg2000PrecinctBand
{
    /// <summary>The column, among the code-blocks of the sub-band, of the first code-block.</summary>
    public int BlockX;

    /// <summary>The row, among the code-blocks of the sub-band, of the first code-block.</summary>
    public int BlockY;

    /// <summary>The number of code-blocks in a row; 0 when the precinct has no code-block in the sub-band.</summary>
    public int BlocksWide;

    /// <summary>The number of rows of code-blocks.</summary>
    public int BlocksHigh;

    /// <summary>The offset of the inclusion tag tree in the nodes of the tile.</summary>
    public int InclusionTree;

    /// <summary>The offset of the tag tree of the zero bit-planes in the nodes of the tile.</summary>
    public int ZeroBitPlaneTree;

    /// <summary>The number of integers of each tag tree.</summary>
    public int TreeLength;
}
