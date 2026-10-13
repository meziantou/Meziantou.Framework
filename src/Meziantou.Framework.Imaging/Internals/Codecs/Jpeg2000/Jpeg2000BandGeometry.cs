using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A sub-band of a tile-component (T.800, B.5), its code-blocks (B.7) and its quantization (Annex E).</summary>
[StructLayout(LayoutKind.Auto)]
internal struct Jpeg2000BandGeometry
{
    /// <summary>The left edge of the sub-band (tbx0).</summary>
    public long X0;

    /// <summary>The top edge of the sub-band (tby0).</summary>
    public long Y0;

    /// <summary>The right edge of the sub-band (tbx1).</summary>
    public long X1;

    /// <summary>The bottom edge of the sub-band (tby1).</summary>
    public long Y1;

    /// <summary>The orientation of the sub-band, for the contexts of the block coder (<see cref="Jpeg2000Tier1Tables"/>).</summary>
    public int Orientation;

    /// <summary>The column of the first coefficient of the sub-band in the plane of its tile-component.</summary>
    public int PlaneX;

    /// <summary>The row of the first coefficient of the sub-band in the plane of its tile-component.</summary>
    public int PlaneY;

    /// <summary>The number of magnitude bit-planes of a code-block of the sub-band (Mb, equation E-2), with the shift of the region of interest.</summary>
    public int BitPlanes;

    /// <summary>Half of the quantization step size of the sub-band (equation E-3), for the irreversible transformation.</summary>
    public float HalfStepSize;

    /// <summary>The exponent of the width of a code-block (xcb').</summary>
    public int BlockWidthExponent;

    /// <summary>The exponent of the height of a code-block (ycb').</summary>
    public int BlockHeightExponent;

    /// <summary>The index, in the code-block partition, of the column of the first code-block of the sub-band.</summary>
    public long BlockColumn;

    /// <summary>The index, in the code-block partition, of the row of the first code-block of the sub-band.</summary>
    public long BlockRow;

    /// <summary>The number of code-blocks in a row of the sub-band.</summary>
    public int BlocksWide;

    /// <summary>The number of rows of code-blocks of the sub-band.</summary>
    public int BlocksHigh;

    /// <summary>The index of the first code-block of the sub-band among the code-blocks of the tile: the code-blocks of a sub-band are in raster order.</summary>
    public int FirstBlock;
}
