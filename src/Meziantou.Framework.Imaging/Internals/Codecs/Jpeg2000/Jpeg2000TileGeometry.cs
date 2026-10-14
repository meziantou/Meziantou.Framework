namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The partition of a tile (T.800, Annex B): its tile-components (B.3), their resolution levels and sub-bands (B.5), the
/// precincts (B.6) and the code-blocks (B.7), all computed from the coordinates of the reference grid, with the state the
/// packet headers build for each code-block.
/// </summary>
/// <remarks>
/// The number of precincts and of code-blocks follows from the header alone, so every array is sized with checked
/// arithmetic and charged to the allocation scope before it is rented.
/// </remarks>
internal sealed class Jpeg2000TileGeometry : IDisposable
{
    private Av1PooledArray<Jpeg2000PrecinctBand>? _precinctBands;
    private Av1PooledArray<Jpeg2000CodeBlock>? _blocks;
    private Av1PooledArray<ushort>? _nextLayers;
    private Av1PooledArray<int>? _tagNodes;

    private Jpeg2000TileGeometry(long x0, long y0, long x1, long y1, Jpeg2000TileComponent[] components, Jpeg2000ResolutionGeometry[] resolutions, Jpeg2000BandGeometry[] bands)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        Components = components;
        Resolutions = resolutions;
        Bands = bands;
    }

    /// <summary>Gets the left edge of the tile on the reference grid (tx0).</summary>
    public long X0 { get; }

    /// <summary>Gets the top edge of the tile on the reference grid (ty0).</summary>
    public long Y0 { get; }

    /// <summary>Gets the right edge of the tile on the reference grid (tx1).</summary>
    public long X1 { get; }

    /// <summary>Gets the bottom edge of the tile on the reference grid (ty1).</summary>
    public long Y1 { get; }

    public Jpeg2000TileComponent[] Components { get; }

    /// <summary>Gets the resolution levels of every component: those of a component follow each other, from the lowest.</summary>
    public Jpeg2000ResolutionGeometry[] Resolutions { get; }

    /// <summary>Gets the sub-bands of every resolution level.</summary>
    public Jpeg2000BandGeometry[] Bands { get; }

    /// <summary>Gets the number of precincts of the tile.</summary>
    public int PrecinctCount { get; private set; }

    /// <summary>Gets the code-blocks of each precinct in each sub-band of its resolution level.</summary>
    public Span<Jpeg2000PrecinctBand> PrecinctBands => _precinctBands!.Span;

    /// <summary>Gets the code-blocks of the tile.</summary>
    public Span<Jpeg2000CodeBlock> Blocks => _blocks!.Span;

    /// <summary>Gets, for each precinct, the layer of its next packet.</summary>
    public Span<ushort> NextLayers => _nextLayers!.Span;

    /// <summary>Gets the nodes of the tag trees.</summary>
    public Span<int> TagNodes => _tagNodes!.Span;

    /// <summary>Computes the partition of a tile.</summary>
    /// <exception cref="ImageResourceLimitException">The partition exceeds the live-allocation limit.</exception>
    public static Jpeg2000TileGeometry Create(AllocationScope scope, Jpeg2000Siz siz, Jpeg2000TileCoding coding, int tileIndex)
    {
        siz.GetTileBounds(tileIndex, out var tx0, out var ty0, out var tx1, out var ty1);
        var componentCount = siz.Components.Length;
        var components = new Jpeg2000TileComponent[componentCount];
        var resolutionCount = 0;
        for (var c = 0; c < componentCount; c++)
        {
            resolutionCount += coding.Styles[c].DecompositionLevels + 1;
        }

        var resolutions = new Jpeg2000ResolutionGeometry[resolutionCount];
        var bands = new Jpeg2000BandGeometry[(3 * resolutionCount) - (2 * componentCount)];
        var precinctCount = 0L;
        var precinctBandCount = 0L;
        var blockCount = 0L;
        var resolutionIndex = 0;
        var bandIndex = 0;
        for (var c = 0; c < componentCount; c++)
        {
            var size = siz.Components[c];
            var style = coding.Styles[c];
            var quantization = coding.Quantizations[c];
            var levels = style.DecompositionLevels;

            // Equation B-12
            var tcx0 = Jpeg2000Siz.CeilingDivide(tx0, size.XSubsampling);
            var tcy0 = Jpeg2000Siz.CeilingDivide(ty0, size.YSubsampling);
            var tcx1 = Jpeg2000Siz.CeilingDivide(tx1, size.XSubsampling);
            var tcy1 = Jpeg2000Siz.CeilingDivide(ty1, size.YSubsampling);
            components[c] = new Jpeg2000TileComponent(c, tcx0, tcy0, tcx1, tcy1, style, quantization.GuardBits, coding.RegionShifts[c], size.Precision, resolutionIndex);
            for (var r = 0; r <= levels; r++)
            {
                // Equation B-14
                var shift = levels - r;
                ref var resolution = ref resolutions[resolutionIndex];
                resolution.X0 = CeilingShift(tcx0, shift);
                resolution.Y0 = CeilingShift(tcy0, shift);
                resolution.X1 = CeilingShift(tcx1, shift);
                resolution.Y1 = CeilingShift(tcy1, shift);
                resolution.PrecinctWidthExponent = style.GetPrecinctWidthExponent(r);
                resolution.PrecinctHeightExponent = style.GetPrecinctHeightExponent(r);
                if (resolution.X1 > resolution.X0 && resolution.Y1 > resolution.Y0)
                {
                    // Equation B-16
                    var wide = CeilingShift(resolution.X1, resolution.PrecinctWidthExponent) - (resolution.X0 >> resolution.PrecinctWidthExponent);
                    var high = CeilingShift(resolution.Y1, resolution.PrecinctHeightExponent) - (resolution.Y0 >> resolution.PrecinctHeightExponent);
                    if (wide * high > CheckedSizes.MaxBufferLength)
                        throw CheckedSizes.CreateOverflowException(scope.Limits);

                    resolution.PrecinctsWide = (int)wide;
                    resolution.PrecinctsHigh = (int)high;
                }

                resolution.BandCount = r == 0 ? 1 : 3;
                resolution.FirstBand = bandIndex;
                resolution.FirstPrecinct = checked((int)Math.Min(precinctCount, int.MaxValue));
                resolution.FirstPrecinctBand = checked((int)Math.Min(precinctBandCount, int.MaxValue));
                precinctCount += resolution.PrecinctCount;
                precinctBandCount += (long)resolution.PrecinctCount * resolution.BandCount;
                for (var b = 0; b < resolution.BandCount; b++)
                {
                    ref var band = ref bands[bandIndex];

                    // Equation B-15: the decomposition level of the sub-band, and whether it is high-pass in each direction
                    var level = r == 0 ? levels : levels - r + 1;
                    var isHighX = r != 0 && b != 1;
                    var isHighY = r != 0 && b != 0;
                    var offsetX = isHighX ? 1L << (level - 1) : 0;
                    var offsetY = isHighY ? 1L << (level - 1) : 0;
                    band.X0 = CeilingShift(tcx0 - offsetX, level);
                    band.Y0 = CeilingShift(tcy0 - offsetY, level);
                    band.X1 = CeilingShift(tcx1 - offsetX, level);
                    band.Y1 = CeilingShift(tcy1 - offsetY, level);
                    band.Orientation = isHighX ? (isHighY ? Jpeg2000Tier1Tables.OrientationHighHigh : Jpeg2000Tier1Tables.OrientationHighLow) : Jpeg2000Tier1Tables.OrientationLowHorizontal;
                    if (r != 0)
                    {
                        // The sub-bands of a resolution level surround the lower resolution level in the plane
                        ref var lower = ref resolutions[resolutionIndex - 1];
                        band.PlaneX = isHighX ? lower.Width : 0;
                        band.PlaneY = isHighY ? lower.Height : 0;
                    }

                    // Equations E-2 and E-3; the nominal range of a sub-band grows with its gain (Table E.1)
                    var quantizationIndex = r == 0 ? 0 : (3 * (r - 1)) + 1 + b;
                    quantization.GetStep(quantizationIndex, out var exponent, out var mantissa);
                    var gain = (isHighX ? 1 : 0) + (isHighY ? 1 : 0);
                    band.BitPlanes = Math.Max(0, quantization.GuardBits + exponent - 1) + coding.RegionShifts[c];
                    band.HalfStepSize = (float)Math.ScaleB(1 + (mantissa / 2048.0), size.Precision + gain - exponent - 1);

                    // Equation B-17 and B-18
                    band.BlockWidthExponent = Math.Min(style.CodeBlockWidthExponent, r == 0 ? resolution.PrecinctWidthExponent : resolution.PrecinctWidthExponent - 1);
                    band.BlockHeightExponent = Math.Min(style.CodeBlockHeightExponent, r == 0 ? resolution.PrecinctHeightExponent : resolution.PrecinctHeightExponent - 1);
                    if (band.X1 > band.X0 && band.Y1 > band.Y0)
                    {
                        band.BlockColumn = band.X0 >> band.BlockWidthExponent;
                        band.BlockRow = band.Y0 >> band.BlockHeightExponent;
                        var wide = CeilingShift(band.X1, band.BlockWidthExponent) - band.BlockColumn;
                        var high = CeilingShift(band.Y1, band.BlockHeightExponent) - band.BlockRow;
                        if (wide * high > CheckedSizes.MaxBufferLength)
                            throw CheckedSizes.CreateOverflowException(scope.Limits);

                        band.BlocksWide = (int)wide;
                        band.BlocksHigh = (int)high;
                    }

                    band.FirstBlock = checked((int)Math.Min(blockCount, int.MaxValue));
                    blockCount += (long)band.BlocksWide * band.BlocksHigh;
                    bandIndex++;
                }

                resolutionIndex++;
            }
        }

        var geometry = new Jpeg2000TileGeometry(tx0, ty0, tx1, ty1, components, resolutions, bands);
        try
        {
            geometry.Allocate(scope, precinctCount, precinctBandCount, blockCount);
            return geometry;
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _precinctBands?.Dispose();
        _precinctBands = null;
        _blocks?.Dispose();
        _blocks = null;
        _nextLayers?.Dispose();
        _nextLayers = null;
        _tagNodes?.Dispose();
        _tagNodes = null;
        foreach (var component in Components)
        {
            component.Dispose();
        }
    }

    /// <summary>Returns the smallest integer that is not less than <paramref name="value"/> / 2^<paramref name="shift"/>.</summary>
    internal static long CeilingShift(long value, int shift) => (value + (1L << shift) - 1) >> shift;

    private void Allocate(AllocationScope scope, long precinctCount, long precinctBandCount, long blockCount)
    {
        if (precinctCount > int.MaxValue || precinctBandCount > int.MaxValue || blockCount > int.MaxValue)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        PrecinctCount = (int)precinctCount;
        _nextLayers = Av1PooledArray<ushort>.Rent(scope, precinctCount);
        _precinctBands = Av1PooledArray<Jpeg2000PrecinctBand>.Rent(scope, precinctBandCount);
        _blocks = Av1PooledArray<Jpeg2000CodeBlock>.Rent(scope, blockCount);

        // The code-blocks of each precinct in each sub-band, and the size of their tag trees
        var precinctBands = _precinctBands.Span;
        var nodeCount = 0L;
        foreach (ref readonly var resolution in Resolutions.AsSpan())
        {
            var precinctBand = resolution.FirstPrecinctBand;
            var isLowest = resolution.BandCount == 1;
            var widthExponent = isLowest ? resolution.PrecinctWidthExponent : resolution.PrecinctWidthExponent - 1;
            var heightExponent = isLowest ? resolution.PrecinctHeightExponent : resolution.PrecinctHeightExponent - 1;
            var firstColumn = resolution.X0 >> resolution.PrecinctWidthExponent;
            var firstRow = resolution.Y0 >> resolution.PrecinctHeightExponent;
            for (var py = 0; py < resolution.PrecinctsHigh; py++)
            {
                for (var px = 0; px < resolution.PrecinctsWide; px++)
                {
                    for (var b = 0; b < resolution.BandCount; b++)
                    {
                        // The area of the precinct in the sub-band (B.7): half of it in each direction above the lowest resolution level
                        ref readonly var band = ref Bands[resolution.FirstBand + b];
                        ref var entry = ref precinctBands[precinctBand++];
                        var x0 = Math.Max((firstColumn + px) << widthExponent, band.X0);
                        var y0 = Math.Max((firstRow + py) << heightExponent, band.Y0);
                        var x1 = Math.Min((firstColumn + px + 1) << widthExponent, band.X1);
                        var y1 = Math.Min((firstRow + py + 1) << heightExponent, band.Y1);
                        if (x0 >= x1 || y0 >= y1)
                            continue;

                        entry.BlockX = (int)((x0 >> band.BlockWidthExponent) - band.BlockColumn);
                        entry.BlockY = (int)((y0 >> band.BlockHeightExponent) - band.BlockRow);
                        entry.BlocksWide = (int)(CeilingShift(x1, band.BlockWidthExponent) - (x0 >> band.BlockWidthExponent));
                        entry.BlocksHigh = (int)(CeilingShift(y1, band.BlockHeightExponent) - (y0 >> band.BlockHeightExponent));
                        var length = 2 * Jpeg2000TagTree.GetNodeCount(entry.BlocksWide, entry.BlocksHigh);
                        if (nodeCount + (2 * length) > CheckedSizes.MaxBufferLength / sizeof(int))
                            throw CheckedSizes.CreateOverflowException(scope.Limits);

                        entry.TreeLength = (int)length;
                        entry.InclusionTree = (int)nodeCount;
                        entry.ZeroBitPlaneTree = (int)(nodeCount + length);
                        nodeCount += 2 * length;
                    }
                }
            }
        }

        _tagNodes = Av1PooledArray<int>.Rent(scope, nodeCount, clear: false);
        var nodes = _tagNodes.Span;
        foreach (ref readonly var entry in precinctBands)
        {
            if (entry.TreeLength != 0)
            {
                Jpeg2000TagTree.Reset(nodes.Slice(entry.InclusionTree, 2 * entry.TreeLength));
            }
        }

        // The area of each code-block: its cell of the partition, clipped to the sub-band
        var blocks = _blocks.Span;
        foreach (ref readonly var band in Bands.AsSpan())
        {
            var index = band.FirstBlock;
            for (var row = 0; row < band.BlocksHigh; row++)
            {
                var y0 = Math.Max((band.BlockRow + row) << band.BlockHeightExponent, band.Y0);
                var y1 = Math.Min((band.BlockRow + row + 1) << band.BlockHeightExponent, band.Y1);
                for (var column = 0; column < band.BlocksWide; column++)
                {
                    var x0 = Math.Max((band.BlockColumn + column) << band.BlockWidthExponent, band.X0);
                    var x1 = Math.Min((band.BlockColumn + column + 1) << band.BlockWidthExponent, band.X1);
                    ref var block = ref blocks[index++];
                    block.X = (int)(x0 - band.X0);
                    block.Y = (int)(y0 - band.Y0);
                    block.Width = (ushort)(x1 - x0);
                    block.Height = (ushort)(y1 - y0);
                    block.FirstContribution = -1;
                    block.LastContribution = -1;
                }
            }
        }
    }
}
