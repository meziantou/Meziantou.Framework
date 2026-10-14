using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A resolution level of a tile-component (T.800, B.5): its area, its precincts (B.6) and its sub-bands.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct Jpeg2000ResolutionGeometry
{
    /// <summary>The left edge of the resolution level (trx0).</summary>
    public long X0;

    /// <summary>The top edge of the resolution level (try0).</summary>
    public long Y0;

    /// <summary>The right edge of the resolution level (trx1).</summary>
    public long X1;

    /// <summary>The bottom edge of the resolution level (try1).</summary>
    public long Y1;

    /// <summary>The exponent of the width of a precinct (PPx).</summary>
    public int PrecinctWidthExponent;

    /// <summary>The exponent of the height of a precinct (PPy).</summary>
    public int PrecinctHeightExponent;

    /// <summary>The number of precincts in a row (numprecinctswide).</summary>
    public int PrecinctsWide;

    /// <summary>The number of rows of precincts (numprecinctshigh).</summary>
    public int PrecinctsHigh;

    /// <summary>The index of the first precinct of the resolution level among the precincts of the tile.</summary>
    public int FirstPrecinct;

    /// <summary>The index of the first sub-band of the resolution level among the sub-bands of the tile.</summary>
    public int FirstBand;

    /// <summary>The number of sub-bands: one for the lowest resolution level (LL), three for the others (HL, LH, HH).</summary>
    public int BandCount;

    /// <summary>The index of the first precinct-band of the resolution level: the precinct-bands of a precinct follow each other.</summary>
    public int FirstPrecinctBand;

    public readonly int Width => (int)(X1 - X0);

    public readonly int Height => (int)(Y1 - Y0);

    public readonly int PrecinctCount => PrecinctsWide * PrecinctsHigh;
}
