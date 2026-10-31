using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes one tile (T.800): the packets are read (Annex B), the coding passes of each code-block are decoded (Annexes C
/// and D), the coefficients are dequantized into the plane of their tile-component (Annex E, and Annex H for a region of
/// interest), the planes go through the inverse wavelet transformation (Annex F), then through the inverse multiple
/// component transformation and the level shift (Annex G).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Dequantization: the block decoder returns twice the magnitude of a coefficient, with the bit-planes that are not in
/// the codestream replaced by half of their range. The reversible path halves it (the exact value when every bit-plane
/// is there); the irreversible path multiplies it by half of the step size of the sub-band (equation E-6 with r = 1/2).
/// </description></item>
/// <item><description>
/// Region of interest (H.1.2): a coefficient whose magnitude is at least 2^s, where s is the shift of the RGN marker
/// segment, is divided by 2^s.
/// </description></item>
/// <item><description>
/// A code-block whose last coding passes are not in the codestream is decoded as far as it goes: it is not an error.
/// </description></item>
/// <item><description>
/// Parallelism (<see cref="Jpeg2000Workers"/>): the packets are read in their order on the calling thread, which gives
/// every code-block its codeword segments. A code-block is then decoded from its own segments, with contexts and states
/// that never cross its edges (the code-block styles only change the passes of one code-block), into its own area of the
/// plane: the code-blocks of a tile of at least <see cref="RowBands.MinPixelsForParallelism"/> pixels are decoded by
/// several workers, each taking the next code-block that is left, with its own block decoders and buffers. The wavelet
/// transformation is then split by rows and by strips of columns, and the component transformation and the level shift
/// by ranges of samples. The samples, the features and the error (the one of the first code-block that fails, a
/// cancellation first) are those of one worker.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Jpeg2000TileDecoder : IDisposable
{
    private const int MaxBlockSamples = 4096;
    private const int MaxSegments = 128;

    // The smallest number of samples of a range of the level shift that a worker takes
    private const int MinSamplesPerRange = 16 * 1024;

    private readonly ImageCodecContext _context;
    private readonly int _maxDegreeOfParallelism;

    // The state of the calling thread, then the states of the other workers, kept from a tile to the next one
    private readonly List<BlockWorker> _workers = [];

    /// <summary>Initializes a new instance of the <see cref="Jpeg2000TileDecoder"/> class.</summary>
    /// <param name="context">The decoding context.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers that decode a tile: one when the caller is already parallel.</param>
    public Jpeg2000TileDecoder(ImageCodecContext context, int maxDegreeOfParallelism = 1)
    {
        _context = context;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        _workers.Add(new BlockWorker(context.Scope));
    }

    /// <summary>Decodes a tile: on return, the plane of each tile-component of <paramref name="geometry"/> holds its samples.</summary>
    /// <param name="siz">The image and tile size.</param>
    /// <param name="coding">The coding parameters of the tile.</param>
    /// <param name="geometry">The partition of the tile, which says the resolution levels to decode.</param>
    /// <param name="tile">The data of the tile.</param>
    /// <param name="window">
    /// The area of the reference grid, at the decoded resolution, that is needed (it meets the tile), or
    /// <see langword="null"/> for the whole tile: only the samples the area needs are rebuilt
    /// (<see cref="Jpeg2000TileComponent.SampleArea"/>), from the code-blocks that reach them (<see cref="Jpeg2000RegionPlan"/>).
    /// </param>
    /// <returns>The features the tile used.</returns>
    /// <exception cref="InvalidImageContentException">The data of the tile is malformed.</exception>
    public Jpeg2000Tools Decode(Jpeg2000Siz siz, Jpeg2000TileCoding coding, Jpeg2000TileGeometry geometry, Jpeg2000Tile tile, Jpeg2000Area? window = null)
    {
        var scope = _context.Scope;
        var tools = coding.Tools;
        using var packets = new Jpeg2000PacketReader(scope, siz, coding, geometry, tile, _context.CancellationToken);
        packets.ReadPackets();
        tools |= packets.Tools;

        var plan = window is { } area ? Jpeg2000RegionPlan.Create(siz, geometry, area) : null;
        var scratchLength = 0L;
        foreach (var component in geometry.Components)
        {
            if (plan is null)
            {
                component.AllocatePlane(scope);
                scratchLength = Math.Max(scratchLength, Jpeg2000Dwt.GetScratchLength(component.Width, component.Height));
            }
            else
            {
                scratchLength = Math.Max(scratchLength, plan.AllocatePlane(scope, component));
            }

            for (var r = 0; r <= component.Style.DecompositionLevels; r++)
            {
                if (geometry.Resolutions[component.FirstResolution + r].PrecinctCount > 1)
                {
                    tools |= Jpeg2000Tools.Precincts;
                }
            }
        }

        var main = _workers[0];
        main.EnsureScratch(scratchLength);
        var workers = Jpeg2000Workers.GetWorkerCount(_maxDegreeOfParallelism, (geometry.OutputX1 - geometry.OutputX0) * (geometry.OutputY1 - geometry.OutputY0), geometry.Blocks.Length);
        if (workers > 1)
            return tools | DecodeByWorkers(coding, geometry, packets, tile.Body.Data, scratchLength, workers, plan);

        var contributions = packets.Contributions;
        var body = tile.Body.Data;
        var blockCount = 0;
        var scratch = main.Scratch!;
        foreach (var component in geometry.Components)
        {
            // The resolution levels above the decoded ones are not in the plane
            var levels = component.DecodedLevels;
            var resolutions = geometry.Resolutions.AsSpan(component.FirstResolution, levels + 1);
            foreach (ref readonly var resolution in resolutions)
            {
                for (var b = 0; b < resolution.BandCount; b++)
                {
                    var bandIndex = resolution.FirstBand + b;
                    ref readonly var band = ref geometry.Bands[bandIndex];
                    var blocks = geometry.Blocks.Slice(band.FirstBlock, band.BlocksWide * band.BlocksHigh);
                    foreach (ref readonly var block in blocks)
                    {
                        // A code-block that does not reach the coefficients a region needs is not decoded: its area is
                        // not used, or it is zeros in a whole plane
                        var isNeeded = plan is null || IsInWindow(in block, plan.Bands[bandIndex]);
                        if (block.PassCount == 0 || !isNeeded)
                        {
                            // The plane is not cleared: a code-block without coding pass gives zeros
                            if (isNeeded || !plan!.IsWindowed(component))
                            {
                                ClearBlock(GetTarget(component, in band, in block, plan, bandIndex));
                            }

                            continue;
                        }

                        if ((++blockCount & 63) == 0)
                        {
                            _context.CancellationToken.ThrowIfCancellationRequested();
                        }

                        tools |= DecodeBlock(main, component, in band, in block, contributions, body, plan, bandIndex);
                    }
                }
            }

            if (component.Width == 0 || component.Height == 0)
                continue;

            if (plan is not null && plan.IsWindowed(component))
            {
                plan.Reconstruct(component, [scratch], workers: 1);
                continue;
            }

            if (component.Style.IsReversible)
            {
                Jpeg2000Dwt.InverseReversible(component.Samples, component.Width, resolutions, scratch.Span);
            }
            else
            {
                Jpeg2000Dwt.InverseIrreversible(component.Coefficients, component.Width, resolutions, unsafe(MemoryMarshal.Cast<int, float>(scratch.Span)));
            }

            plan?.Reconstruct(component, [scratch], workers: 1);
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
        FinishSamples(coding, geometry);
        return tools;
    }

    public void Dispose()
    {
        foreach (var worker in _workers)
        {
            worker.Dispose();
        }

        _workers.Clear();
    }

    /// <summary>
    /// Decodes the code-blocks of a tile by several workers, then transforms its planes by several workers: what
    /// <see cref="Decode"/> does with one worker.
    /// </summary>
    private Jpeg2000Tools DecodeByWorkers(Jpeg2000TileCoding coding, Jpeg2000TileGeometry geometry, Jpeg2000PacketReader packets, byte[] body, long scratchLength, int workers, Jpeg2000RegionPlan? plan)
    {
        var scope = _context.Scope;
        var cancellationToken = _context.CancellationToken;
        var components = geometry.Components;
        var bands = geometry.Bands;

        // The component of each sub-band (-1 for a sub-band of a resolution level that is not decoded), and what the
        // workers need: everything is rented here, on the calling thread
        var bandComponents = new int[bands.Length];
        bandComponents.AsSpan().Fill(-1);
        var usesHighThroughput = false;
        foreach (var component in components)
        {
            usesHighThroughput |= (component.Style.CodeBlockStyle & Jpeg2000CodingStyle.StyleHighThroughput) != 0;
            var resolutions = geometry.Resolutions.AsSpan(component.FirstResolution, component.DecodedLevels + 1);
            foreach (ref readonly var resolution in resolutions)
            {
                bandComponents.AsSpan(resolution.FirstBand, resolution.BandCount).Fill(component.Index);
            }
        }

        var dataLength = GetLargestJoinedLength(geometry.Blocks, packets.Contributions);
        var scratch = new Av1PooledArray<int>[workers];
        for (var w = 0; w < workers; w++)
        {
            if (w == _workers.Count)
            {
                _workers.Add(new BlockWorker(scope));
            }

            var worker = _workers[w];
            worker.EnsureScratch(scratchLength);
            if (usesHighThroughput)
            {
                worker.GetHighThroughputDecoder().RentSignificancePropagationFlags();
            }

            if (dataLength > 0)
            {
                worker.RentData(dataLength, _context.Limits);
            }

            worker.Tools = Jpeg2000Tools.None;
            scratch[w] = worker.Scratch!;
        }

        Jpeg2000Workers.Run(workers, geometry.Blocks.Length, (w, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bandIndex = FindBand(geometry.Bands, index);
            if (bandComponents[bandIndex] < 0)
                return;

            ref readonly var band = ref geometry.Bands[bandIndex];
            ref readonly var block = ref geometry.Blocks[index];
            var component = geometry.Components[bandComponents[bandIndex]];
            var isNeeded = plan is null || IsInWindow(in block, plan.Bands[bandIndex]);
            if (block.PassCount == 0 || !isNeeded)
            {
                if (isNeeded || !plan!.IsWindowed(component))
                {
                    ClearBlock(GetTarget(component, in band, in block, plan, bandIndex));
                }

                return;
            }

            var worker = _workers[w];
            worker.Tools |= DecodeBlock(worker, component, in band, in block, packets.Contributions, body, plan, bandIndex);
        });

        var tools = Jpeg2000Tools.None;
        for (var w = 0; w < workers; w++)
        {
            tools |= _workers[w].Tools;
        }

        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (component.Width == 0 || component.Height == 0)
                continue;

            if (plan is not null && plan.IsWindowed(component))
            {
                plan.Reconstruct(component, scratch, workers);
                continue;
            }

            var resolutions = new ArraySegment<Jpeg2000ResolutionGeometry>(geometry.Resolutions, component.FirstResolution, component.DecodedLevels + 1);
            if (component.Style.IsReversible)
            {
                Jpeg2000Dwt.InverseReversible(component.Plane!, component.Width, resolutions, scratch, workers);
            }
            else
            {
                Jpeg2000Dwt.InverseIrreversible(component.Plane!, component.Width, resolutions, scratch, workers);
            }

            plan?.Reconstruct(component, scratch, workers);
        }

        cancellationToken.ThrowIfCancellationRequested();
        FinishSamples(coding, geometry, workers);
        return tools;
    }

    /// <summary>Determines whether a code-block has coefficients in a window of its sub-band.</summary>
    private static bool IsInWindow(in Jpeg2000CodeBlock block, Jpeg2000Area window)
        => block.X < window.X1 && block.X + block.Width > window.X0 && block.Y < window.Y1 && block.Y + block.Height > window.Y0;

    /// <summary>Gets the length of the largest code-block whose segments several contributions hold: the buffer that makes them contiguous.</summary>
    private static long GetLargestJoinedLength(ReadOnlySpan<Jpeg2000CodeBlock> blocks, ReadOnlySpan<Jpeg2000Contribution> contributions)
    {
        var largest = 0L;
        foreach (ref readonly var block in blocks)
        {
            if (block.PassCount == 0 || contributions[block.FirstContribution].Next < 0)
                continue;

            var total = 0L;
            for (var index = block.FirstContribution; index >= 0; index = contributions[index].Next)
            {
                total += contributions[index].Length;
            }

            // A code-block that is too long fails when its turn comes, as with one worker
            if (total <= CheckedSizes.MaxBufferLength)
            {
                largest = Math.Max(largest, total);
            }
        }

        return largest;
    }

    /// <summary>Gets the sub-band of a code-block of the tile: the last one that starts at or before it.</summary>
    private static int FindBand(Jpeg2000BandGeometry[] bands, int block)
    {
        var low = 0;
        var high = bands.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (bands[middle].FirstBlock <= block)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static void FinishSamples(Jpeg2000TileCoding coding, Jpeg2000TileGeometry geometry)
    {
        var components = geometry.Components;
        if (coding.Defaults.UsesComponentTransform)
        {
            if (components[0].Style.IsReversible)
            {
                Jpeg2000ColorTransform.InverseReversible(components[0].Samples, components[1].Samples, components[2].Samples);
            }
            else
            {
                Jpeg2000ColorTransform.InverseIrreversible(components[0].Coefficients, components[1].Coefficients, components[2].Coefficients);
            }
        }

        foreach (var component in components)
        {
            ShiftLevel(component);
        }
    }

    /// <summary>
    /// The component transformation and the level shift by ranges of samples, one per worker: both work on each sample on
    /// its own, so that a range gives the samples of the whole plane.
    /// </summary>
    private static void FinishSamples(Jpeg2000TileCoding coding, Jpeg2000TileGeometry geometry, int workers)
    {
        var components = geometry.Components;
        var length = 0;
        foreach (var component in components)
        {
            length = Math.Max(length, component.Samples.Length);
        }

        var ranges = Jpeg2000Workers.GetBandCount(workers, length, length, MinSamplesPerRange);
        if (ranges == 1)
        {
            FinishSamples(coding, geometry);
            return;
        }

        Jpeg2000Workers.RunBands(ranges, range =>
        {
            if (coding.Defaults.UsesComponentTransform)
            {
                var (start, count) = GetRange(components[0].Samples.Length, range, ranges);
                if (components[0].Style.IsReversible)
                {
                    Jpeg2000ColorTransform.InverseReversible(components[0].Samples.Slice(start, count), components[1].Samples.Slice(start, count), components[2].Samples.Slice(start, count));
                }
                else
                {
                    Jpeg2000ColorTransform.InverseIrreversible(components[0].Coefficients.Slice(start, count), components[1].Coefficients.Slice(start, count), components[2].Coefficients.Slice(start, count));
                }
            }

            foreach (var component in components)
            {
                var (start, count) = GetRange(component.Samples.Length, range, ranges);
                ShiftLevel(component, start, count);
            }
        });

        static (int Start, int Count) GetRange(int length, int range, int ranges)
        {
            // The ranges start at a multiple of 64 samples, so that the vector paths cover them
            var start = range == 0 ? 0 : Math.Min(length, Jpeg2000Workers.GetBandStart(length, range, ranges) & ~63);
            var end = range == ranges - 1 ? length : Math.Min(length, Jpeg2000Workers.GetBandStart(length, range + 1, ranges) & ~63);
            return (start, end - start);
        }
    }

    private static void ShiftLevel(Jpeg2000TileComponent component) => ShiftLevel(component, 0, component.Samples.Length);

    private static void ShiftLevel(Jpeg2000TileComponent component, int start, int count)
    {
        if (component.Style.IsReversible)
        {
            Jpeg2000ColorTransform.ShiftLevel(component.Samples.Slice(start, count), component.Precision);
        }
        else
        {
            Jpeg2000ColorTransform.ShiftLevel(component.Coefficients.Slice(start, count), component.Samples.Slice(start, count), component.Precision);
        }
    }

    private Jpeg2000Tools DecodeBlock(BlockWorker worker, Jpeg2000TileComponent component, in Jpeg2000BandGeometry band, in Jpeg2000CodeBlock block, ReadOnlySpan<Jpeg2000Contribution> contributions, byte[] body, Jpeg2000RegionPlan? plan, int bandIndex)
    {
        var tools = Jpeg2000Tools.None;
        var style = component.Style.CodeBlockStyle;
        int width = block.Width;
        int height = block.Height;
        var samples = worker.Samples.Span[..(width * height)];
        if ((style & Jpeg2000CodingStyle.StyleHighThroughput) != 0)
        {
            tools = DecodeHighThroughputBlock(worker, samples, in band, in block, contributions, body, (style & Jpeg2000CodingStyle.StyleVerticallyCausal) != 0);
            StoreBlock(component, in band, GetTarget(component, in band, in block, plan, bandIndex), samples);
            return tools;
        }

        var bitPlanes = band.BitPlanes - block.ZeroBitPlanes;
        if (block.PassCount < (3 * bitPlanes) - 2)
        {
            tools |= Jpeg2000Tools.TruncatedCodeBlock;
        }

        // The codeword segments: the contributions of the packets between two terminations of the coder
        Span<Jpeg2000CodeBlockSegment> segments = stackalloc Jpeg2000CodeBlockSegment[MaxSegments];
        var segmentCount = 0;
        var data = body;
        var first = contributions[block.FirstContribution];
        if (first.Next < 0)
        {
            segments[segmentCount++] = new Jpeg2000CodeBlockSegment(first.Offset, first.Length, first.PassCount);
        }
        else
        {
            // The bytes of a segment that several packets hold are made contiguous
            var total = 0L;
            for (var index = block.FirstContribution; index >= 0; index = contributions[index].Next)
            {
                total += contributions[index].Length;
            }

            data = worker.RentData(total, _context.Limits);
            var position = 0;
            var pass = 0;
            var segmentOffset = 0;
            var segmentPasses = 0;
            var pieces = 0;
            for (var index = block.FirstContribution; index >= 0; index = contributions[index].Next)
            {
                ref readonly var contribution = ref contributions[index];
                body.AsSpan(contribution.Offset, contribution.Length).CopyTo(data.AsSpan(position));
                position += contribution.Length;
                pass += contribution.PassCount;
                segmentPasses += contribution.PassCount;
                pieces++;
                if (!Jpeg2000CodeBlockSegment.IsTerminatedAfter(pass - 1, style))
                    continue;

                segments[segmentCount++] = new Jpeg2000CodeBlockSegment(segmentOffset, position - segmentOffset, segmentPasses);
                if (pieces > 1)
                {
                    tools |= Jpeg2000Tools.SplitSegment;
                }

                segmentOffset = position;
                segmentPasses = 0;
                pieces = 0;
            }

            if (segmentPasses > 0)
            {
                segments[segmentCount++] = new Jpeg2000CodeBlockSegment(segmentOffset, position - segmentOffset, segmentPasses);
            }
        }

        worker.Tier1.Decode(samples, width, height, band.Orientation, style, bitPlanes, data, segments[..segmentCount]);
        StoreBlock(component, in band, GetTarget(component, in band, in block, plan, bandIndex), samples);
        return tools;
    }

    /// <summary>Decodes a code-block of the high-throughput block coder (T.814): its HT cleanup segment, then its HT refinement segment.</summary>
    private Jpeg2000Tools DecodeHighThroughputBlock(BlockWorker worker, Span<int> samples, in Jpeg2000BandGeometry band, in Jpeg2000CodeBlock block, ReadOnlySpan<Jpeg2000Contribution> contributions, byte[] body, bool isVerticallyCausal)
    {
        // The packet reader made one contribution of the placeholder passes and the HT cleanup pass; the others hold the
        // HT SigProp and HT MagRef passes, which several packets may share (T.814, B.3)
        ref readonly var cleanup = ref contributions[block.FirstContribution];
        var data = body;
        var refinementOffset = 0;
        var refinementLength = 0;
        var refinementPasses = 0;
        if (cleanup.Next >= 0)
        {
            ref readonly var refinement = ref contributions[cleanup.Next];
            if (refinement.Next < 0)
            {
                refinementOffset = refinement.Offset;
                refinementLength = refinement.Length;
                refinementPasses = refinement.PassCount;
            }
            else
            {
                // The HT segments are made contiguous: the bytes of the HT cleanup segment, then those of the others
                var total = 0L;
                for (var index = block.FirstContribution; index >= 0; index = contributions[index].Next)
                {
                    total += contributions[index].Length;
                }

                data = worker.RentData(total, _context.Limits);
                var position = 0;
                for (var index = block.FirstContribution; index >= 0; index = contributions[index].Next)
                {
                    ref readonly var contribution = ref contributions[index];
                    body.AsSpan(contribution.Offset, contribution.Length).CopyTo(data.AsSpan(position));
                    position += contribution.Length;
                    if (index != block.FirstContribution)
                    {
                        refinementPasses += contribution.PassCount;
                    }
                }

                refinementOffset = cleanup.Length;
                refinementLength = position - cleanup.Length;
                return worker.GetHighThroughputDecoder().Decode(samples, block.Width, block.Height, isVerticallyCausal, band.BitPlanes, block.ZeroBitPlanes + block.PlaceholderSets, GetPassCount(refinementPasses, refinementLength), data, 0, cleanup.Length, refinementOffset, refinementLength) | Jpeg2000Tools.SplitSegment;
            }
        }

        return worker.GetHighThroughputDecoder().Decode(samples, block.Width, block.Height, isVerticallyCausal, band.BitPlanes, block.ZeroBitPlanes + block.PlaceholderSets, GetPassCount(refinementPasses, refinementLength), data, cleanup.Offset, cleanup.Length, refinementOffset, refinementLength);

        // T.814, B.3: the passes of an empty HT refinement segment are not decoded (Z_blk)
        static int GetPassCount(int refinementPasses, int refinementLength) => refinementLength == 0 ? 1 : 1 + refinementPasses;
    }

    /// <summary>
    /// Gets where the coefficients of a code-block go: its area in the plane of its tile-component, or, when a region
    /// rebuilds the tile-component on its areas, the part of the code-block in the coefficients the region needs of the
    /// sub-band (<see cref="Jpeg2000TileComponent.BandCoefficients"/>).
    /// </summary>
    private static BlockTarget GetTarget(Jpeg2000TileComponent component, in Jpeg2000BandGeometry band, in Jpeg2000CodeBlock block, Jpeg2000RegionPlan? plan, int bandIndex)
    {
        if (plan is null || !plan.IsWindowed(component))
        {
            var stride = component.Width;
            return new BlockTarget(component.Samples, ((band.PlaneY + block.Y) * stride) + band.PlaneX + block.X, stride, 0, block.Width, block.Width, block.Height);
        }

        var window = plan.Bands[bandIndex];
        var x0 = (int)Math.Max(block.X, window.X0);
        var y0 = (int)Math.Max(block.Y, window.Y0);
        var x1 = (int)Math.Min(block.X + block.Width, window.X1);
        var y1 = (int)Math.Min(block.Y + block.Height, window.Y1);
        var windowWidth = (int)(window.X1 - window.X0);
        var origin = plan.GetBandOffset(bandIndex) + ((y0 - (int)window.Y0) * windowWidth) + x0 - (int)window.X0;
        return new BlockTarget(component.BandCoefficients, origin, windowWidth, ((y0 - block.Y) * block.Width) + x0 - block.X, block.Width, x1 - x0, y1 - y0);
    }

    /// <summary>Clears the area of a code-block where its coefficients go.</summary>
    private static void ClearBlock(BlockTarget target)
    {
        for (var y = 0; y < target.Height; y++)
        {
            target.Destination.Slice(target.Origin + (y * target.Stride), target.Width).Clear();
        }
    }

    /// <summary>Dequantizes the coefficients of a code-block where they go.</summary>
    /// <param name="component">The tile-component of the code-block.</param>
    /// <param name="band">The sub-band of the code-block.</param>
    /// <param name="target">Where the coefficients go.</param>
    /// <param name="samples">The coefficients of the block decoder, in rows of the width of the code-block.</param>
    private static void StoreBlock(Jpeg2000TileComponent component, in Jpeg2000BandGeometry band, BlockTarget target, ReadOnlySpan<int> samples)
    {
        var width = target.Width;
        var height = target.Height;
        var stride = target.Stride;
        var origin = target.Origin;
        var regionShift = component.RegionShift;
        if (component.Style.IsReversible)
        {
            var plane = target.Destination;
            for (var y = 0; y < height; y++)
            {
                StoreReversible(samples.Slice(target.Source + (y * target.SourceStride), width), plane.Slice(origin + (y * stride), width), regionShift);
            }
        }
        else
        {
            var plane = unsafe(MemoryMarshal.Cast<int, float>(target.Destination));
            var halfStep = band.HalfStepSize;
            for (var y = 0; y < height; y++)
            {
                StoreIrreversible(samples.Slice(target.Source + (y * target.SourceStride), width), plane.Slice(origin + (y * stride), width), regionShift, halfStep);
            }
        }
    }

    /// <summary>
    /// Gets the shift of the region of interest that the dequantization undoes. A coefficient of the region has a magnitude
    /// of at least 2^s (H.2), which a magnitude of at most <see cref="Jpeg2000Format.MaxBitPlanes"/> bit-planes never
    /// reaches when s is that number: every coefficient is then in the background and keeps its magnitude.
    /// </summary>
    /// <param name="regionShift">The shift of the region of interest; 0 when there is none.</param>
    private static int GetStoredRegionShift(int regionShift) => regionShift < Jpeg2000Format.MaxBitPlanes ? regionShift : 0;

    /// <summary>
    /// Dequantizes coefficients of the reversible path: the magnitude (twice the magnitude, from the block decoders) is
    /// divided by 2^s when it is at least 2^(s+1), s being the shift of the region of interest, then halved, and the sign
    /// is applied.
    /// </summary>
    /// <param name="source">The coefficients of the block decoder: the sign in the most significant bit.</param>
    /// <param name="destination">Receives the coefficients; as long as <paramref name="source"/>.</param>
    /// <param name="regionShift">The shift of the region of interest; 0 when there is none.</param>
    internal static void StoreReversible(ReadOnlySpan<int> source, Span<int> destination, int regionShift)
    {
        destination = destination[..source.Length];
        regionShift = GetStoredRegionShift(regionShift);
        var regionThreshold = regionShift == 0 ? int.MaxValue : 1 << (regionShift + 1);
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // The sign mask is 0 or -1: (m ^ mask) - mask is m or -m
            var thresholds = Vector128.Create(regionThreshold);
            for (; i <= source.Length - Vector128<int>.Count; i += Vector128<int>.Count)
            {
                var value = Vector128.Create(source[i..]);
                var magnitude = value & Vector128.Create(int.MaxValue);
                magnitude = Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(magnitude, thresholds), Vector128.ShiftRightArithmetic(magnitude, regionShift), magnitude);
                magnitude = Vector128.ShiftRightArithmetic(magnitude, 1);
                var sign = Vector128.ShiftRightArithmetic(value, 31);
                ((magnitude ^ sign) - sign).CopyTo(destination[i..]);
            }
        }

        StoreReversibleScalar(source[i..], destination[i..], regionShift);
    }

    /// <summary>The scalar reference of <see cref="StoreReversible"/>.</summary>
    internal static void StoreReversibleScalar(ReadOnlySpan<int> source, Span<int> destination, int regionShift)
    {
        regionShift = GetStoredRegionShift(regionShift);
        var regionThreshold = regionShift == 0 ? int.MaxValue : 1 << (regionShift + 1);
        for (var x = 0; x < source.Length; x++)
        {
            var value = source[x];
            var magnitude = value & int.MaxValue;
            if (magnitude >= regionThreshold)
            {
                magnitude >>= regionShift;
            }

            magnitude >>= 1;
            destination[x] = value < 0 ? -magnitude : magnitude;
        }
    }

    /// <summary>
    /// Dequantizes coefficients of the irreversible path: the magnitude is divided by 2^s when it is at least 2^(s+1), s
    /// being the shift of the region of interest, then multiplied by half of the step size, and the sign is applied.
    /// </summary>
    /// <param name="source">The coefficients of the block decoder: the sign in the most significant bit.</param>
    /// <param name="destination">Receives the coefficients; as long as <paramref name="source"/>.</param>
    /// <param name="regionShift">The shift of the region of interest; 0 when there is none.</param>
    /// <param name="halfStep">Half of the step size of the sub-band.</param>
    internal static void StoreIrreversible(ReadOnlySpan<int> source, Span<float> destination, int regionShift, float halfStep)
    {
        destination = destination[..source.Length];
        regionShift = GetStoredRegionShift(regionShift);
        var regionThreshold = regionShift == 0 ? int.MaxValue : 1 << (regionShift + 1);
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // The conversion to single precision rounds as the scalar one does, and the negation of the product is the
            // exchange of its sign bit
            var thresholds = Vector128.Create(regionThreshold);
            var steps = Vector128.Create(halfStep);
            for (; i <= source.Length - Vector128<int>.Count; i += Vector128<int>.Count)
            {
                var value = Vector128.Create(source[i..]);
                var magnitude = value & Vector128.Create(int.MaxValue);
                magnitude = Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(magnitude, thresholds), Vector128.ShiftRightArithmetic(magnitude, regionShift), magnitude);
                var coefficient = Vector128.ConvertToSingle(magnitude) * steps;
                (coefficient.AsInt32() ^ (value & Vector128.Create(int.MinValue))).AsSingle().CopyTo(destination[i..]);
            }
        }

        StoreIrreversibleScalar(source[i..], destination[i..], regionShift, halfStep);
    }

    /// <summary>The scalar reference of <see cref="StoreIrreversible"/>.</summary>
    internal static void StoreIrreversibleScalar(ReadOnlySpan<int> source, Span<float> destination, int regionShift, float halfStep)
    {
        regionShift = GetStoredRegionShift(regionShift);
        var regionThreshold = regionShift == 0 ? int.MaxValue : 1 << (regionShift + 1);
        for (var x = 0; x < source.Length; x++)
        {
            var value = source[x];
            var magnitude = value & int.MaxValue;
            if (magnitude >= regionThreshold)
            {
                magnitude >>= regionShift;
            }

            var coefficient = magnitude * halfStep;
            destination[x] = value < 0 ? -coefficient : coefficient;
        }
    }

    /// <summary>Where the coefficients of a code-block go: an area of a plane, and the coefficients of the code-block it receives.</summary>
    private readonly ref struct BlockTarget
    {
        public BlockTarget(Span<int> destination, int origin, int stride, int source, int sourceStride, int width, int height)
        {
            Destination = destination;
            Origin = origin;
            Stride = stride;
            Source = source;
            Width = width;
            Height = height;
            SourceStride = sourceStride;
        }

        /// <summary>Gets the plane.</summary>
        public Span<int> Destination { get; }

        /// <summary>Gets the position of the first coefficient of the area in <see cref="Destination"/>.</summary>
        public int Origin { get; }

        /// <summary>Gets the distance between two rows of the area.</summary>
        public int Stride { get; }

        /// <summary>Gets the position, in the coefficients of the code-block, of the first one the area receives.</summary>
        public int Source { get; }

        /// <summary>Gets the distance between two rows of the coefficients of the code-block: its width.</summary>
        public int SourceStride { get; }

        /// <summary>Gets the number of columns of the area.</summary>
        public int Width { get; }

        /// <summary>Gets the number of rows of the area.</summary>
        public int Height { get; }
    }

    /// <summary>The state of a worker: its block decoders, the coefficients of its code-block, its buffers.</summary>
    private sealed class BlockWorker : IDisposable
    {
        private readonly AllocationScope _scope;
        private Jpeg2000HtBlockDecoder? _highThroughputDecoder;
        private PooledBuffer? _data;

        public BlockWorker(AllocationScope scope)
        {
            _scope = scope;
            Tier1 = new Jpeg2000Tier1Decoder(scope);
            try
            {
                Samples = Av1PooledArray<int>.Rent(scope, MaxBlockSamples, clear: false);
            }
            catch
            {
                Tier1.Dispose();
                throw;
            }
        }

        public Jpeg2000Tier1Decoder Tier1 { get; }

        /// <summary>Gets the coefficients of the code-block being decoded.</summary>
        public Av1PooledArray<int> Samples { get; }

        /// <summary>Gets the scratch of the wavelet transformation.</summary>
        public Av1PooledArray<int>? Scratch { get; private set; }

        /// <summary>Gets or sets the features of the code-blocks the worker decoded.</summary>
        public Jpeg2000Tools Tools { get; set; }

        public Jpeg2000HtBlockDecoder GetHighThroughputDecoder() => _highThroughputDecoder ??= new Jpeg2000HtBlockDecoder(_scope);

        public void EnsureScratch(long length)
        {
            if (Scratch is null || Scratch.Length < length)
            {
                Scratch?.Dispose();
                Scratch = null;
                Scratch = Av1PooledArray<int>.Rent(_scope, length, clear: false);
            }
        }

        /// <summary>Gets a buffer of at least <paramref name="length"/> bytes, which joins the segments of a code-block.</summary>
        public byte[] RentData(long length, ImageResourceLimits limits)
        {
            if (_data is null || _data.Length < length)
            {
                if (length > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(limits);

                _data?.Dispose();
                _data = null;
                _data = _scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
            }

            return _data.RawBuffer;
        }

        public void Dispose()
        {
            Tier1.Dispose();
            _highThroughputDecoder?.Dispose();
            _highThroughputDecoder = null;
            Samples.Dispose();
            Scratch?.Dispose();
            Scratch = null;
            _data?.Dispose();
            _data = null;
        }
    }
}
