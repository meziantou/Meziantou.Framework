using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A tile-component (T.800, B.3): the samples of one component in one tile, with the plane its coefficients are decoded
/// into. The sub-bands are laid out in the plane as the transformation leaves them: the lower resolution level at the top
/// left, the HL sub-band at its right, the LH sub-band below it and the HH sub-band in the corner. When the decoder discards
/// resolution levels, the bounds and the plane are those of the highest resolution level it decodes: the lower ones are at
/// the top left of that plane as they would be in the whole one. A region (<see cref="Jpeg2000RegionPlan"/>) may rebuild a
/// part of the tile-component only: the plane then holds the samples of <see cref="SampleArea"/>.
/// </summary>
internal sealed class Jpeg2000TileComponent : IDisposable
{
    private Av1PooledArray<int>? _plane;
    private Av1PooledArray<int>? _bandCoefficients;
    private int _sampleCount;

    public Jpeg2000TileComponent(int index, long x0, long y0, long x1, long y1, Jpeg2000CodingStyle style, int guardBits, int regionShift, int precision, int firstResolution, int decodedLevels)
    {
        DecodedLevels = decodedLevels;
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
        SampleArea = new Jpeg2000Area(x0, y0, x1, y1);
    }

    public int Index { get; }

    /// <summary>
    /// Gets the number of decomposition levels the decoder reverses: <see cref="Jpeg2000CodingStyle.DecompositionLevels"/>, less
    /// the resolution levels it discards. The resolution levels above it are read from the packets but not decoded.
    /// </summary>
    public int DecodedLevels { get; }

    /// <summary>Gets the left edge of the tile-component (tcx0), at the highest resolution level that is decoded.</summary>
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

    /// <summary>
    /// Gets the area of the tile-component whose samples the plane holds once it is transformed, in raster order: the whole
    /// tile-component, or the samples a region needs (<see cref="Jpeg2000RegionPlan"/>).
    /// </summary>
    public Jpeg2000Area SampleArea { get; private set; }

    /// <summary>Gets the plane as integers: the coefficients of the reversible transformation, then the samples.</summary>
    public Span<int> Samples => _plane is null ? [] : _plane.Span[.._sampleCount];

    /// <summary>Gets the coefficients that a region needs of each sub-band, once <see cref="AllocateWindowedPlane"/> rented them (<see cref="Jpeg2000RegionPlan.GetBandOffset"/>).</summary>
    public Span<int> BandCoefficients => _bandCoefficients is null ? [] : _bandCoefficients.Span;

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
        _sampleCount = _plane.Length;
    }

    /// <summary>
    /// Rents, for a region, the plane of the areas the resolution levels are rebuilt on and the coefficients of the
    /// sub-bands, not cleared: the decoder stores the coefficients of each code-block that meets them, or clears that part.
    /// </summary>
    /// <param name="scope">The allocation scope.</param>
    /// <param name="planeLength">The number of samples of the plane.</param>
    /// <param name="bandLength">The number of coefficients of the sub-bands.</param>
    /// <exception cref="ImageResourceLimitException">The arrays exceed the live-allocation limit.</exception>
    public void AllocateWindowedPlane(AllocationScope scope, long planeLength, long bandLength)
    {
        Dispose();
        _plane = Av1PooledArray<int>.Rent(scope, planeLength, clear: false);
        _bandCoefficients = Av1PooledArray<int>.Rent(scope, bandLength, clear: false);
    }

    /// <summary>Releases the coefficients of the sub-bands, once the plane is rebuilt from them.</summary>
    public void ReleaseBandCoefficients()
    {
        _bandCoefficients?.Dispose();
        _bandCoefficients = null;
    }

    /// <summary>Records that the plane holds the samples of an area, in raster order from its start.</summary>
    public void SetSampleArea(Jpeg2000Area area)
    {
        SampleArea = area;
        _sampleCount = (int)((area.X1 - area.X0) * (area.Y1 - area.Y0));
    }

    public void Dispose()
    {
        _plane?.Dispose();
        _plane = null;
        _sampleCount = 0;
        ReleaseBandCoefficients();
    }
}
