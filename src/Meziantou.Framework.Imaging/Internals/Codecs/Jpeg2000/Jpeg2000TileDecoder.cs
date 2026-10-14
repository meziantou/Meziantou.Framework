using System.Runtime.InteropServices;

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
/// </list>
/// </remarks>
internal sealed class Jpeg2000TileDecoder : IDisposable
{
    private const int MaxBlockSamples = 4096;
    private const int MaxSegments = 128;

    private readonly ImageCodecContext _context;
    private readonly Jpeg2000Tier1Decoder _blockDecoder;
    private Jpeg2000HtBlockDecoder? _highThroughputDecoder;
    private readonly Av1PooledArray<int> _blockSamples;
    private Av1PooledArray<int>? _scratch;
    private PooledBuffer? _blockData;

    public Jpeg2000TileDecoder(ImageCodecContext context)
    {
        _context = context;
        _blockDecoder = new Jpeg2000Tier1Decoder(context.Scope);
        try
        {
            _blockSamples = Av1PooledArray<int>.Rent(context.Scope, MaxBlockSamples, clear: false);
        }
        catch
        {
            _blockDecoder.Dispose();
            throw;
        }
    }

    /// <summary>Decodes a tile: on return, the plane of each tile-component of <paramref name="geometry"/> holds its samples.</summary>
    /// <returns>The features the tile used.</returns>
    /// <exception cref="InvalidImageContentException">The data of the tile is malformed.</exception>
    public Jpeg2000Tools Decode(Jpeg2000Siz siz, Jpeg2000TileCoding coding, Jpeg2000TileGeometry geometry, Jpeg2000Tile tile)
    {
        var scope = _context.Scope;
        var tools = coding.Tools;
        using var packets = new Jpeg2000PacketReader(scope, siz, coding, geometry, tile, _context.CancellationToken);
        packets.ReadPackets();
        tools |= packets.Tools;

        var scratchLength = 0L;
        foreach (var component in geometry.Components)
        {
            component.AllocatePlane(scope);
            scratchLength = Math.Max(scratchLength, Jpeg2000Dwt.GetScratchLength(component.Width, component.Height));
            for (var r = 0; r <= component.Style.DecompositionLevels; r++)
            {
                if (geometry.Resolutions[component.FirstResolution + r].PrecinctCount > 1)
                {
                    tools |= Jpeg2000Tools.Precincts;
                }
            }
        }

        if (_scratch is null || _scratch.Length < scratchLength)
        {
            _scratch?.Dispose();
            _scratch = null;
            _scratch = Av1PooledArray<int>.Rent(scope, scratchLength, clear: false);
        }

        var contributions = packets.Contributions;
        var body = tile.Body.Data;
        var blockCount = 0;
        foreach (var component in geometry.Components)
        {
            var levels = component.Style.DecompositionLevels;
            var resolutions = geometry.Resolutions.AsSpan(component.FirstResolution, levels + 1);
            foreach (ref readonly var resolution in resolutions)
            {
                for (var b = 0; b < resolution.BandCount; b++)
                {
                    ref readonly var band = ref geometry.Bands[resolution.FirstBand + b];
                    var blocks = geometry.Blocks.Slice(band.FirstBlock, band.BlocksWide * band.BlocksHigh);
                    foreach (ref readonly var block in blocks)
                    {
                        if (block.PassCount == 0)
                            continue;

                        if ((++blockCount & 63) == 0)
                        {
                            _context.CancellationToken.ThrowIfCancellationRequested();
                        }

                        tools |= DecodeBlock(component, in band, in block, contributions, body);
                    }
                }
            }

            if (component.Width == 0 || component.Height == 0)
                continue;

            if (component.Style.IsReversible)
            {
                Jpeg2000Dwt.InverseReversible(component.Samples, component.Width, resolutions, _scratch.Span);
            }
            else
            {
                Jpeg2000Dwt.InverseIrreversible(component.Coefficients, component.Width, resolutions, unsafe(MemoryMarshal.Cast<int, float>(_scratch.Span)));
            }
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
        FinishSamples(coding, geometry);
        return tools;
    }

    public void Dispose()
    {
        _blockDecoder.Dispose();
        _highThroughputDecoder?.Dispose();
        _highThroughputDecoder = null;
        _blockSamples.Dispose();
        _scratch?.Dispose();
        _scratch = null;
        _blockData?.Dispose();
        _blockData = null;
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
            if (component.Style.IsReversible)
            {
                Jpeg2000ColorTransform.ShiftLevel(component.Samples, component.Precision);
            }
            else
            {
                Jpeg2000ColorTransform.ShiftLevel(component.Coefficients, component.Samples, component.Precision);
            }
        }
    }

    private Jpeg2000Tools DecodeBlock(Jpeg2000TileComponent component, in Jpeg2000BandGeometry band, in Jpeg2000CodeBlock block, ReadOnlySpan<Jpeg2000Contribution> contributions, byte[] body)
    {
        var tools = Jpeg2000Tools.None;
        var style = component.Style.CodeBlockStyle;
        int width = block.Width;
        int height = block.Height;
        var samples = _blockSamples.Span[..(width * height)];
        if ((style & Jpeg2000CodingStyle.StyleHighThroughput) != 0)
        {
            tools = DecodeHighThroughputBlock(samples, in band, in block, contributions, body, (style & Jpeg2000CodingStyle.StyleVerticallyCausal) != 0);
            StoreBlock(component, in band, in block, samples);
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

            data = RentBlockData(total);
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

        _blockDecoder.Decode(samples, width, height, band.Orientation, style, bitPlanes, data, segments[..segmentCount]);
        StoreBlock(component, in band, in block, samples);
        return tools;
    }

    /// <summary>Decodes a code-block of the high-throughput block coder (T.814): its HT cleanup segment, then its HT refinement segment.</summary>
    private Jpeg2000Tools DecodeHighThroughputBlock(Span<int> samples, in Jpeg2000BandGeometry band, in Jpeg2000CodeBlock block, ReadOnlySpan<Jpeg2000Contribution> contributions, byte[] body, bool isVerticallyCausal)
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

                data = RentBlockData(total);
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
                _highThroughputDecoder ??= new Jpeg2000HtBlockDecoder(_context.Scope);
                return _highThroughputDecoder.Decode(samples, block.Width, block.Height, isVerticallyCausal, band.BitPlanes, block.ZeroBitPlanes + block.PlaceholderSets, GetPassCount(refinementPasses, refinementLength), data, 0, cleanup.Length, refinementOffset, refinementLength) | Jpeg2000Tools.SplitSegment;
            }
        }

        _highThroughputDecoder ??= new Jpeg2000HtBlockDecoder(_context.Scope);
        return _highThroughputDecoder.Decode(samples, block.Width, block.Height, isVerticallyCausal, band.BitPlanes, block.ZeroBitPlanes + block.PlaceholderSets, GetPassCount(refinementPasses, refinementLength), data, cleanup.Offset, cleanup.Length, refinementOffset, refinementLength);

        // T.814, B.3: the passes of an empty HT refinement segment are not decoded (Z_blk)
        static int GetPassCount(int refinementPasses, int refinementLength) => refinementLength == 0 ? 1 : 1 + refinementPasses;
    }

    private byte[] RentBlockData(long length)
    {
        if (_blockData is null || _blockData.Length < length)
        {
            if (length > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            _blockData?.Dispose();
            _blockData = null;
            _blockData = _context.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        }

        return _blockData.RawBuffer;
    }

    /// <summary>Dequantizes the coefficients of a code-block into the plane of its tile-component.</summary>
    private static void StoreBlock(Jpeg2000TileComponent component, in Jpeg2000BandGeometry band, in Jpeg2000CodeBlock block, ReadOnlySpan<int> samples)
    {
        int width = block.Width;
        int height = block.Height;

        // The area of the code-block in the plane
        var stride = component.Width;
        var origin = ((band.PlaneY + block.Y) * stride) + band.PlaneX + block.X;
        var regionShift = component.RegionShift;
        var regionThreshold = regionShift == 0 ? int.MaxValue : 1 << (regionShift + 1);
        if (component.Style.IsReversible)
        {
            var plane = component.Samples;
            for (var y = 0; y < height; y++)
            {
                var source = samples.Slice(y * width, width);
                var destination = plane.Slice(origin + (y * stride), width);
                for (var x = 0; x < width; x++)
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
        }
        else
        {
            var plane = component.Coefficients;
            var halfStep = band.HalfStepSize;
            for (var y = 0; y < height; y++)
            {
                var source = samples.Slice(y * width, width);
                var destination = plane.Slice(origin + (y * stride), width);
                for (var x = 0; x < width; x++)
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
        }
    }
}
