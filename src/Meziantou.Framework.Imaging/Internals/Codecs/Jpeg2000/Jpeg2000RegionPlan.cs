using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a tile decodes for a region (<see cref="ImageDecodeRequest.Region"/>): for each tile-component, the samples that
/// <see cref="Jpeg2000RowWriter"/> reads, the area of each resolution level that rebuilds them, and the coefficients of
/// each sub-band that those areas need.
/// </summary>
/// <remarks>
/// <para>
/// The areas are computed from the highest resolution level down. The samples that a level must give are widened by the
/// margin of its filter and cut to the level: the level is rebuilt on that area, whose low-pass half is what the level
/// below must give, and whose two halves are the coefficients of the sub-bands of the level (B-15). A level rebuilt on
/// part of itself extends its signals symmetrically at an edge that is not its own: each lifting step spreads that error
/// by one sample, so the samples that are at least the number of lifting steps (2 for the 5-3 filter, 4 for the 9-7 one)
/// away from such an edge are those of the whole level, bit for bit, since they are computed by the same operations.
/// </para>
/// <para>
/// A tile-component is rebuilt on its areas (<see cref="IsWindowed"/>) when the plane of the largest area and the
/// coefficients of its sub-bands hold fewer samples than its whole plane. Otherwise its whole plane is transformed,
/// without the code-blocks that do not reach the coefficients the areas need (zeros instead), then cut to its samples.
/// Both give the samples of a full decode.
/// </para>
/// </remarks>
internal sealed class Jpeg2000RegionPlan
{
    // The number of lifting steps of each filter (F.3.8.1 and F.3.8.2)
    private const int ReversibleMargin = 2;
    private const int IrreversibleMargin = 4;

    private readonly ComponentPlan?[] _components;
    private readonly long[] _bandOffsets;

    private Jpeg2000RegionPlan(Jpeg2000Area[] bands, long[] bandOffsets, ComponentPlan?[] components)
    {
        Bands = bands;
        _bandOffsets = bandOffsets;
        _components = components;
    }

    /// <summary>
    /// Gets, for each sub-band of the tile, the coefficients to decode, relative to the sub-band like the code-blocks:
    /// <see langword="default"/> (no coefficient) for a sub-band that is not needed.
    /// </summary>
    public Jpeg2000Area[] Bands { get; }

    /// <summary>Computes what a tile decodes for an area of the image.</summary>
    /// <param name="siz">The image and tile size.</param>
    /// <param name="geometry">The partition of the tile.</param>
    /// <param name="window">The area of the reference grid, at the decoded resolution, that the frame holds: it meets the tile.</param>
    public static Jpeg2000RegionPlan Create(Jpeg2000Siz siz, Jpeg2000TileGeometry geometry, Jpeg2000Area window)
    {
        var area = new Jpeg2000Area(geometry.OutputX0, geometry.OutputY0, geometry.OutputX1, geometry.OutputY1).Intersect(window);
        var bands = new Jpeg2000Area[geometry.Bands.Length];
        var bandOffsets = new long[geometry.Bands.Length];
        var components = new ComponentPlan?[geometry.Components.Length];
        foreach (var component in geometry.Components)
        {
            if (component.Width == 0 || component.Height == 0)
                continue;

            // The samples that the row writer reads for the area: a sub-sampled column of the reference grid takes the
            // sample floor(x / XRsiz), and the first or the last sample of the tile-component out of it
            var size = siz.Components[component.Index];
            var samples = new Jpeg2000Area(
                Math.Clamp(area.X0 / size.XSubsampling, component.X0, component.X1 - 1),
                Math.Clamp(area.Y0 / size.YSubsampling, component.Y0, component.Y1 - 1),
                Math.Clamp((area.X1 - 1) / size.XSubsampling, component.X0, component.X1 - 1) + 1,
                Math.Clamp((area.Y1 - 1) / size.YSubsampling, component.Y0, component.Y1 - 1) + 1);

            var levels = component.DecodedLevels;
            var margin = component.Style.IsReversible ? ReversibleMargin : IrreversibleMargin;
            var areas = new Jpeg2000Area[levels + 1];
            var steps = new Jpeg2000ResolutionGeometry[2 * levels];
            var firstBands = new int[levels + 1];
            var storeLength = 0L;
            var needed = samples;
            for (var r = levels; r >= 0; r--)
            {
                ref readonly var resolution = ref geometry.Resolutions[component.FirstResolution + r];
                firstBands[r] = resolution.FirstBand;
                if (r == 0)
                {
                    // The lowest resolution level is its LL sub-band
                    areas[0] = needed;
                    SetBand(geometry, bands, bandOffsets, resolution.FirstBand, needed, ref storeLength);
                    break;
                }

                // The area the level is rebuilt on, and its two halves (B-15): the low-pass samples of a signal that
                // starts at u0 are those of ceil(u0 / 2), the high-pass ones those of floor(u0 / 2)
                var x0 = Widen(needed.X0, needed.X1, margin, resolution.X0, resolution.X1, out var x1);
                var y0 = Widen(needed.Y0, needed.Y1, margin, resolution.Y0, resolution.Y1, out var y1);
                var current = new Jpeg2000Area(x0, y0, x1, y1);
                var low = current.Reduce(1);
                var high = new Jpeg2000Area(current.X0 >> 1, current.Y0 >> 1, current.X1 >> 1, current.Y1 >> 1);
                areas[r] = current;
                steps[2 * (r - 1)] = ToResolution(low);
                steps[(2 * (r - 1)) + 1] = ToResolution(current);

                // HL, LH and HH: high-pass horizontally, vertically, and both
                SetBand(geometry, bands, bandOffsets, resolution.FirstBand, new Jpeg2000Area(high.X0, low.Y0, high.X1, low.Y1), ref storeLength);
                SetBand(geometry, bands, bandOffsets, resolution.FirstBand + 1, new Jpeg2000Area(low.X0, high.Y0, low.X1, high.Y1), ref storeLength);
                SetBand(geometry, bands, bandOffsets, resolution.FirstBand + 2, high, ref storeLength);
                needed = low;
            }

            var stride = 0L;
            var height = 0L;
            foreach (var level in areas)
            {
                stride = Math.Max(stride, level.X1 - level.X0);
                height = Math.Max(height, level.Y1 - level.Y0);
            }

            var planeLength = stride * height;
            var isWindowed = planeLength + storeLength < (long)component.Width * component.Height;
            components[component.Index] = new ComponentPlan(samples, areas, steps, firstBands, (int)stride, (int)height, planeLength, storeLength, isWindowed);
        }

        return new Jpeg2000RegionPlan(bands, bandOffsets, components);
    }

    /// <summary>Determines whether a tile-component is rebuilt on its areas, from the coefficients of <see cref="Jpeg2000TileComponent.BandCoefficients"/>.</summary>
    public bool IsWindowed(Jpeg2000TileComponent component) => _components[component.Index]?.IsWindowed == true;

    /// <summary>Gets the offset, in <see cref="Jpeg2000TileComponent.BandCoefficients"/>, of the coefficients of a sub-band, which have the width of its area in <see cref="Bands"/>.</summary>
    public int GetBandOffset(int band) => (int)_bandOffsets[band];

    /// <summary>Rents the plane of a tile-component: its whole plane, or the plane of its areas and the coefficients of its sub-bands.</summary>
    /// <returns>The number of scratch elements its inverse transformation needs.</returns>
    /// <exception cref="ImageResourceLimitException">The planes exceed the live-allocation limit.</exception>
    public long AllocatePlane(AllocationScope scope, Jpeg2000TileComponent component)
    {
        if (_components[component.Index] is not { IsWindowed: true } plan)
        {
            component.AllocatePlane(scope);
            return Jpeg2000Dwt.GetScratchLength(component.Width, component.Height);
        }

        component.AllocateWindowedPlane(scope, plan.PlaneLength, plan.StoreLength);
        return Jpeg2000Dwt.GetScratchLength(plan.Stride, plan.Height);
    }

    /// <summary>
    /// Rebuilds the samples of a tile-component whose code-blocks are decoded: on its areas, from the coefficients of its
    /// sub-bands, or by cutting its whole plane, transformed already, to its samples. On return,
    /// the plane holds the samples of <see cref="Jpeg2000TileComponent.SampleArea"/>.
    /// </summary>
    /// <param name="component">The tile-component.</param>
    /// <param name="scratch">The scratch of each worker: at least the length <see cref="AllocatePlane"/> returns.</param>
    /// <param name="workers">The number of workers of the wavelet transformation.</param>
    public void Reconstruct(Jpeg2000TileComponent component, Av1PooledArray<int>[] scratch, int workers)
    {
        if (_components[component.Index] is not { } plan)
            return;

        var plane = component.Plane!;
        if (!plan.IsWindowed)
        {
            // The whole plane is transformed: its samples are cut from it
            Cut(plane.Span, component.Width, plan.Samples.X0 - component.X0, plan.Samples.Y0 - component.Y0, plan.Samples, plan.Samples.X1 - plan.Samples.X0);
            component.SetSampleArea(plan.Samples);
            return;
        }

        var store = component.BandCoefficients;
        var stride = plan.Stride;
        var areas = plan.Areas;
        var lowest = areas[0];
        CopyBand(store, GetBandOffset(plan.FirstBands[0]), plane.Span, stride, 0, 0, Width(lowest), Height(lowest));
        for (var r = 1; r < areas.Length; r++)
        {
            var current = areas[r];
            if (current.IsEmpty)
                continue;

            var low = current.Reduce(1);
            var lowWidth = Width(low);
            var lowHeight = Height(low);
            var highWidth = Width(current) - lowWidth;
            var highHeight = Height(current) - lowHeight;

            // The samples the level below gave for the low-pass half of this one go to the top left, then the sub-bands
            // around them, as the transformation of the whole plane has them (2D_DEINTERLEAVE)
            var previous = areas[r - 1];
            if (lowWidth > 0 && lowHeight > 0)
            {
                Cut(plane.Span, stride, low.X0 - previous.X0, low.Y0 - previous.Y0, low, stride);
            }

            var firstBand = plan.FirstBands[r];
            CopyBand(store, GetBandOffset(firstBand), plane.Span, stride, lowWidth, 0, highWidth, lowHeight);
            CopyBand(store, GetBandOffset(firstBand + 1), plane.Span, stride, 0, lowHeight, lowWidth, highHeight);
            CopyBand(store, GetBandOffset(firstBand + 2), plane.Span, stride, lowWidth, lowHeight, highWidth, highHeight);

            var level = new ArraySegment<Jpeg2000ResolutionGeometry>(plan.Steps, 2 * (r - 1), 2);
            if (component.Style.IsReversible)
            {
                if (workers > 1)
                {
                    Jpeg2000Dwt.InverseReversible(plane, stride, level, scratch, workers);
                }
                else
                {
                    Jpeg2000Dwt.InverseReversible(plane.Span, stride, level, scratch[0].Span);
                }
            }
            else if (workers > 1)
            {
                Jpeg2000Dwt.InverseIrreversible(plane, stride, level, scratch, workers);
            }
            else
            {
                Jpeg2000Dwt.InverseIrreversible(AsSingle(plane.Span), stride, level, AsSingle(scratch[0].Span));
            }
        }

        // The coefficients of the sub-bands are no longer needed; the samples are cut from the area of the highest level
        component.ReleaseBandCoefficients();
        var top = areas[^1];
        Cut(plane.Span, stride, plan.Samples.X0 - top.X0, plan.Samples.Y0 - top.Y0, plan.Samples, plan.Samples.X1 - plan.Samples.X0);
        component.SetSampleArea(plan.Samples);
    }

    /// <summary>Widens an interval by a margin, cut to the bounds of its resolution level; an empty interval stays empty.</summary>
    private static long Widen(long start, long end, int margin, long lower, long upper, out long widenedEnd)
    {
        if (start >= end)
        {
            widenedEnd = start;
            return start;
        }

        widenedEnd = Math.Min(upper, end + margin);
        return Math.Max(lower, start - margin);
    }

    private static Span<float> AsSingle(Span<int> values) => unsafe(MemoryMarshal.Cast<int, float>(values));

    private static Jpeg2000ResolutionGeometry ToResolution(Jpeg2000Area area) => new() { X0 = area.X0, Y0 = area.Y0, X1 = area.X1, Y1 = area.Y1 };

    private static int Width(Jpeg2000Area area) => (int)(area.X1 - area.X0);

    private static int Height(Jpeg2000Area area) => (int)(area.Y1 - area.Y0);

    /// <summary>Records the coefficients of a sub-band that are needed, given in the coordinates of the sub-band, and their place in the coefficients of the tile-component.</summary>
    private static void SetBand(Jpeg2000TileGeometry geometry, Jpeg2000Area[] bands, long[] offsets, int index, Jpeg2000Area area, ref long storeLength)
    {
        ref readonly var band = ref geometry.Bands[index];
        offsets[index] = storeLength;

        // An empty area meets no code-block
        if (area.IsEmpty)
            return;

        bands[index] = new Jpeg2000Area(area.X0 - band.X0, area.Y0 - band.Y0, area.X1 - band.X0, area.Y1 - band.Y0);
        storeLength += (area.X1 - area.X0) * (area.Y1 - area.Y0);
    }

    /// <summary>Copies the coefficients of a sub-band, row by row, to an area of the plane.</summary>
    private static void CopyBand(ReadOnlySpan<int> store, int offset, Span<int> plane, int stride, int x, int y, int width, int height)
    {
        if (width == 0 || height == 0)
            return;

        for (var row = 0; row < height; row++)
        {
            store.Slice(offset + (row * width), width).CopyTo(plane.Slice(((y + row) * stride) + x, width));
        }
    }

    /// <summary>
    /// Moves the samples of an area that starts at (<paramref name="left"/>, <paramref name="top"/>) of a plane to its top
    /// left, with the stride <paramref name="destinationStride"/>: the rows move to lower positions, in their order, so a
    /// row is never overwritten before it is moved.
    /// </summary>
    private static void Cut(Span<int> plane, int stride, long left, long top, Jpeg2000Area area, long destinationStride)
    {
        var width = Width(area);
        var height = Height(area);
        if (left == 0 && top == 0 && destinationStride == stride)
            return;

        for (var row = 0; row < height; row++)
        {
            plane.Slice((int)(((top + row) * stride) + left), width).CopyTo(plane.Slice((int)(row * destinationStride), width));
        }
    }

    /// <summary>The areas of one tile-component.</summary>
    /// <param name="Samples">The samples the row writer reads, at the decoded resolution.</param>
    /// <param name="Areas">The area each resolution level is rebuilt on, from the lowest (its LL sub-band).</param>
    /// <param name="Steps">The bounds that <see cref="Jpeg2000Dwt"/> reads for each level above the lowest: its low-pass half, then its area.</param>
    /// <param name="FirstBands">The index of the first sub-band of each resolution level.</param>
    /// <param name="Stride">The width of the plane of the areas: the largest one.</param>
    /// <param name="Height">The height of the plane of the areas: the largest one.</param>
    /// <param name="PlaneLength">The number of samples of the plane of the areas.</param>
    /// <param name="StoreLength">The number of coefficients of the sub-bands.</param>
    /// <param name="IsWindowed">Whether the tile-component is rebuilt on its areas.</param>
    private sealed record ComponentPlan(Jpeg2000Area Samples, Jpeg2000Area[] Areas, Jpeg2000ResolutionGeometry[] Steps, int[] FirstBands, int Stride, int Height, long PlaneLength, long StoreLength, bool IsWindowed);
}
