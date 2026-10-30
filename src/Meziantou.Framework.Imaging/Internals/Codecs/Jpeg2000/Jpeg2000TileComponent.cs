using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A tile-component (T.800, B.3): the samples of one component in one tile, with the plane its coefficients are decoded
/// into. The sub-bands are laid out in the plane as the transformation leaves them: the lower resolution level at the top
/// left, the HL sub-band at its right, the LH sub-band below it and the HH sub-band in the corner.
/// </summary>
internal sealed class Jpeg2000TileComponent : IDisposable
{
    private Av1PooledArray<int>? _plane;

    public Jpeg2000TileComponent(int index, long x0, long y0, long x1, long y1, Jpeg2000CodingStyle style, int guardBits, int regionShift, int precision, int firstResolution)
    {
        Index = index;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        Style = style;
        GuardBits = guardBits;
        RegionShift = regionShift;
        Precision = precision;
        FirstResolution = firstResolution;
    }

    public int Index { get; }

    /// <summary>Gets the left edge of the tile-component (tcx0).</summary>
    public long X0 { get; }

    /// <summary>Gets the top edge of the tile-component (tcy0).</summary>
    public long Y0 { get; }

    /// <summary>Gets the right edge of the tile-component (tcx1).</summary>
    public long X1 { get; }

    /// <summary>Gets the bottom edge of the tile-component (tcy1).</summary>
    public long Y1 { get; }

    public int Width => (int)(X1 - X0);

    public int Height => (int)(Y1 - Y0);

    public Jpeg2000CodingStyle Style { get; }

    public int GuardBits { get; }

    /// <summary>Gets the shift of the region of interest; 0 when there is none.</summary>
    public int RegionShift { get; }

    /// <summary>Gets the number of bits of the samples.</summary>
    public int Precision { get; }

    /// <summary>Gets the index of the lowest resolution level in <see cref="Jpeg2000TileGeometry.Resolutions"/>.</summary>
    public int FirstResolution { get; }

    /// <summary>Gets the plane as integers: the coefficients of the reversible transformation, then the samples.</summary>
    public Span<int> Samples => _plane is null ? [] : _plane.Span;

    /// <summary>Gets the array of the plane, once <see cref="AllocatePlane"/> rented it.</summary>
    public Av1PooledArray<int>? Plane => _plane;

    /// <summary>Gets the plane as floating-point numbers: the coefficients of the irreversible transformation.</summary>
    public Span<float> Coefficients => unsafe(MemoryMarshal.Cast<int, float>(Samples));

    /// <summary>
    /// Rents the plane, not cleared: the code-blocks of the sub-bands cover it exactly, and the decoder stores the
    /// coefficients of each code-block or clears its area when it has no coding pass.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The plane exceeds the live-allocation limit.</exception>
    public void AllocatePlane(AllocationScope scope)
    {
        _plane?.Dispose();
        _plane = null;
        _plane = Av1PooledArray<int>.Rent(scope, (long)Width * Height, clear: false);
    }

    public void Dispose()
    {
        _plane?.Dispose();
        _plane = null;
    }
}
