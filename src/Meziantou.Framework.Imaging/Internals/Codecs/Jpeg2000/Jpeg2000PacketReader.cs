using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the packets of a tile (T.800, B.9 to B.12): it visits them in the progression order, parses each packet header
/// (the inclusion of each code-block, its empty bit-planes, its number of coding passes and the lengths of its codeword
/// segments) and records where the bytes of each code-block are in the data of the tile. It decodes no coding pass.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The packets are read as long as the tile has data. A tile may end after any of its packets: the packets it does not
/// hold add nothing to their code-blocks, as if they were empty (writers that follow a POC marker segment or that drop
/// layers produce such tiles). Data that ends inside a packet is invalid.
/// </description></item>
/// <item><description>
/// A packet is identified by its component, resolution level, precinct and layer; the layers of a precinct come in order,
/// so each precinct counts its packets and a POC progression never reads a packet twice (B.12.1.1).
/// </description></item>
/// <item><description>
/// The three progressions that follow the position visit each precinct at one point of the reference grid: its top-left
/// corner, or the corner of the tile for the precincts the tile cuts (B.12.1.3 to B.12.1.5). These points are computed
/// and sorted, rather than found by walking the reference grid.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Jpeg2000PacketReader : IDisposable
{
    private const int InitialContributionCapacity = 256;
    private const int MaxLengthBits = 31;

    private readonly AllocationScope _scope;
    private readonly Jpeg2000Siz _siz;
    private readonly Jpeg2000TileCoding _coding;
    private readonly Jpeg2000TileGeometry _geometry;
    private readonly byte[] _body;
    private readonly int _bodyLength;
    private readonly byte[]? _packedHeaders;
    private readonly int _packedHeadersLength;
    private readonly CancellationToken _cancellationToken;
    private int _bodyPosition;
    private int _headerPosition;
    private int _packetCount;
    private bool _isExhausted;
    private Av1PooledArray<Jpeg2000Contribution>? _contributions;
    private int _contributionCount;
    private Av1PooledArray<Jpeg2000PacketPosition>? _positions;
    private int _positionOrder = -1;

    public Jpeg2000PacketReader(AllocationScope scope, Jpeg2000Siz siz, Jpeg2000TileCoding coding, Jpeg2000TileGeometry geometry, Jpeg2000Tile tile, CancellationToken cancellationToken)
    {
        _scope = scope;
        _siz = siz;
        _coding = coding;
        _geometry = geometry;
        _body = tile.Body.Data;
        _bodyLength = tile.Body.Length;
        if (tile.PackedHeaders is { } packed)
        {
            _packedHeaders = packed.Data;
            _packedHeadersLength = packed.Length;
        }

        _cancellationToken = cancellationToken;
    }

    /// <summary>Gets the features the packets used.</summary>
    public Jpeg2000Tools Tools { get; private set; }

    /// <summary>Gets the contributions of the packets to the code-blocks.</summary>
    public ReadOnlySpan<Jpeg2000Contribution> Contributions => _contributions is null ? [] : _contributions.Span[.._contributionCount];

    /// <summary>Reads every packet of the tile.</summary>
    /// <exception cref="InvalidImageContentException">A packet header is malformed, or the data of the tile ends before its packets.</exception>
    public void ReadPackets()
    {
        var defaults = _coding.Defaults;
        var maxResolutions = 0;
        foreach (var style in _coding.Styles)
        {
            maxResolutions = Math.Max(maxResolutions, style.DecompositionLevels + 1);
        }

        var componentCount = _coding.Styles.Length;
        if (_coding.Progressions is { } progressions)
        {
            foreach (var progression in progressions)
            {
                ReadProgression(progression with
                {
                    LayerEnd = Math.Min(progression.LayerEnd, defaults.Layers),
                    ResolutionEnd = Math.Min(progression.ResolutionEnd, maxResolutions),
                    ComponentEnd = Math.Min(progression.ComponentEnd, componentCount),
                });
            }
        }
        else
        {
            ReadProgression(new Jpeg2000ProgressionChange(0, 0, defaults.Layers, maxResolutions, componentCount, defaults.Progression));
        }
    }

    public void Dispose()
    {
        _contributions?.Dispose();
        _contributions = null;
        _positions?.Dispose();
        _positions = null;
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid(message);

    private void ReadProgression(Jpeg2000ProgressionChange progression)
    {
        var components = _geometry.Components;
        var resolutions = _geometry.Resolutions;
        switch (progression.Progression)
        {
            case Jpeg2000CodingDefaults.ProgressionLrcp:
                for (var layer = 0; layer < progression.LayerEnd; layer++)
                {
                    for (var r = progression.ResolutionStart; r < progression.ResolutionEnd; r++)
                    {
                        ReadResolutionLayer(progression, r, layer);
                    }
                }

                break;

            case Jpeg2000CodingDefaults.ProgressionRlcp:
                for (var r = progression.ResolutionStart; r < progression.ResolutionEnd; r++)
                {
                    for (var layer = 0; layer < progression.LayerEnd; layer++)
                    {
                        ReadResolutionLayer(progression, r, layer);
                    }
                }

                break;

            default:
            {
                var nextLayers = _geometry.NextLayers;
                foreach (ref readonly var position in GetPositions(progression.Progression))
                {
                    if (position.Component < progression.ComponentStart || position.Component >= progression.ComponentEnd || position.Resolution < progression.ResolutionStart || position.Resolution >= progression.ResolutionEnd)
                        continue;

                    var resolutionIndex = components[position.Component].FirstResolution + position.Resolution;
                    ref readonly var resolution = ref resolutions[resolutionIndex];
                    for (int layer = nextLayers[resolution.FirstPrecinct + position.Precinct]; layer < progression.LayerEnd; layer++)
                    {
                        ReadPacket(position.Component, resolutionIndex, position.Precinct, layer);
                    }
                }

                break;
            }
        }
    }

    private void ReadResolutionLayer(Jpeg2000ProgressionChange progression, int r, int layer)
    {
        var components = _geometry.Components;
        var nextLayers = _geometry.NextLayers;
        for (var c = progression.ComponentStart; c < progression.ComponentEnd; c++)
        {
            var component = components[c];
            if (r > component.Style.DecompositionLevels)
                continue;

            var resolutionIndex = component.FirstResolution + r;
            var precinctCount = _geometry.Resolutions[resolutionIndex].PrecinctCount;
            var firstPrecinct = _geometry.Resolutions[resolutionIndex].FirstPrecinct;
            for (var precinct = 0; precinct < precinctCount; precinct++)
            {
                // The layers of a precinct come in order: this one is read unless an earlier progression read it
                if (nextLayers[firstPrecinct + precinct] == layer)
                {
                    ReadPacket(c, resolutionIndex, precinct, layer);
                }
            }
        }
    }

    /// <summary>Gets the precincts of the tile in the order one of the position progressions visits them.</summary>
    private ReadOnlySpan<Jpeg2000PacketPosition> GetPositions(int progression)
    {
        if (_positions is null)
        {
            _positions = Av1PooledArray<Jpeg2000PacketPosition>.Rent(_scope, _geometry.PrecinctCount, clear: false);
            Jpeg2000PacketPosition.Fill(_siz, _geometry, _positions.Span);
        }

        if (_positionOrder != progression)
        {
            _positionOrder = progression;
            Jpeg2000PacketPosition.Sort(_positions.Span, progression);
        }

        return _positions.Span;
    }

    private void ReadPacket(int component, int resolutionIndex, int precinct, int layer)
    {
        if ((++_packetCount & 0xFF) == 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
        }

        ref readonly var resolution = ref _geometry.Resolutions[resolutionIndex];
        _geometry.NextLayers[resolution.FirstPrecinct + precinct] = (ushort)(layer + 1);
        if (_isExhausted)
            return;

        if (_packedHeaders is null ? _bodyPosition >= _bodyLength : _headerPosition >= _packedHeadersLength)
        {
            // The tile ends here: its other packets are not in the codestream
            _isExhausted = true;
            Tools |= Jpeg2000Tools.MissingPackets;
            return;
        }

        var defaults = _coding.Defaults;
        var style = _coding.Styles[component].CodeBlockStyle;
        var body = _body.AsSpan(0, _bodyLength);
        if (defaults.MayUseSop && body.Length - _bodyPosition >= 6 && body[_bodyPosition] == 0xFF && body[_bodyPosition + 1] == (Jpeg2000Format.MarkerSop & 0xFF))
        {
            // A.8.1: the marker, the length 4 and the index of the packet
            if (body[_bodyPosition + 2] != 0 || body[_bodyPosition + 3] != 4)
                throw Invalid("the length of a SOP marker segment is not the one of its fields.");

            _bodyPosition += 6;
            Tools |= Jpeg2000Tools.StartOfPacket;
        }

        var isPacked = _packedHeaders is not null;
        var headers = isPacked ? _packedHeaders.AsSpan(0, _packedHeadersLength) : body;
        var reader = new Jpeg2000PacketBitReader(headers, isPacked ? _headerPosition : _bodyPosition);
        var firstContribution = _contributionCount;
        if (reader.ReadBit() != 0)
        {
            var precinctBands = _geometry.PrecinctBands;
            var blocks = _geometry.Blocks;
            var nodes = _geometry.TagNodes;
            for (var b = 0; b < resolution.BandCount; b++)
            {
                ref readonly var precinctBand = ref precinctBands[resolution.FirstPrecinctBand + (precinct * resolution.BandCount) + b];
                if (precinctBand.BlocksWide == 0)
                    continue;

                ref readonly var band = ref _geometry.Bands[resolution.FirstBand + b];
                var inclusion = nodes.Slice(precinctBand.InclusionTree, precinctBand.TreeLength);
                var zeroBitPlanes = nodes.Slice(precinctBand.ZeroBitPlaneTree, precinctBand.TreeLength);
                for (var by = 0; by < precinctBand.BlocksHigh; by++)
                {
                    for (var bx = 0; bx < precinctBand.BlocksWide; bx++)
                    {
                        ref var block = ref blocks[band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx];
                        if (block.Included != 0)
                        {
                            if (reader.ReadBit() == 0)
                                continue;
                        }
                        else
                        {
                            // B.10.4: the value of the inclusion tree is the first layer the code-block is in
                            if (!Jpeg2000TagTree.IsLessThan(inclusion, precinctBand.BlocksWide, precinctBand.BlocksHigh, bx, by, layer + 1, ref reader))
                                continue;

                            // B.10.5: the number of empty bit-planes is the value of the second tree
                            var threshold = 1;
                            while (!Jpeg2000TagTree.IsLessThan(zeroBitPlanes, precinctBand.BlocksWide, precinctBand.BlocksHigh, bx, by, threshold, ref reader))
                            {
                                if (threshold > band.BitPlanes)
                                    throw Invalid("a code-block has more empty bit-planes than its sub-band has bit-planes.");

                                threshold++;
                            }

                            block.Included = 1;
                            block.ZeroBitPlanes = (byte)(threshold - 1);
                            block.LengthBits = 3;
                        }

                        ReadContributions(ref reader, ref block, band.BitPlanes, style);
                    }
                }
            }
        }
        else
        {
            Tools |= Jpeg2000Tools.EmptyPacket;
        }

        var headerEnd = reader.Align();
        if (defaults.UsesEph)
        {
            if (headers.Length - headerEnd < 2 || headers[headerEnd] != 0xFF || headers[headerEnd + 1] != (Jpeg2000Format.MarkerEph & 0xFF))
                throw Invalid("a packet header is not followed by the EPH marker.");

            headerEnd += 2;
        }

        if (isPacked)
        {
            _headerPosition = headerEnd;
        }
        else
        {
            _bodyPosition = headerEnd;
        }

        // The bytes of the code-blocks follow the packet header, in the order of the header
        if (_contributionCount != firstContribution)
        {
            var contributions = _contributions!.Span[firstContribution.._contributionCount];
            foreach (ref var contribution in contributions)
            {
                if (contribution.Length > _bodyLength - _bodyPosition)
                    throw Invalid("the data of a tile ends before the bytes of a packet.");

                contribution.Offset = _bodyPosition;
                _bodyPosition += contribution.Length;
            }
        }
    }

    private void ReadContributions(ref Jpeg2000PacketBitReader reader, ref Jpeg2000CodeBlock block, int bandBitPlanes, int style)
    {
        // B.10.6: the number of coding passes the packet adds (Table B.4)
        int passes;
        if (reader.ReadBit() == 0)
        {
            passes = 1;
        }
        else if (reader.ReadBit() == 0)
        {
            passes = 2;
        }
        else
        {
            var value = reader.ReadBits(2);
            if (value != 3)
            {
                passes = 3 + value;
            }
            else
            {
                value = reader.ReadBits(5);
                passes = value != 31 ? 6 + value : 37 + reader.ReadBits(7);
            }
        }

        // The first bit-plane has one coding pass, the others three (D.3)
        var bitPlanes = bandBitPlanes - block.ZeroBitPlanes;
        var maxPasses = bitPlanes <= 0 ? 0 : (3 * bitPlanes) - 2;
        if (block.PassCount + passes > maxPasses)
            throw Invalid("a code-block has more coding passes than its bit-planes allow.");

        // B.10.7.1: Lblock grows by the number of 1 bits that precede a 0 bit
        while (reader.ReadBit() != 0)
        {
            if (block.LengthBits >= MaxLengthBits)
                throw Invalid("the length of a codeword segment does not fit in 31 bits.");

            block.LengthBits++;
        }

        int first = block.PassCount;
        if ((style & Jpeg2000CodingStyle.StyleHighThroughput) != 0)
        {
            ReadHighThroughputContributions(ref reader, ref block, passes);
            block.PassCount = (ushort)(first + passes);
            return;
        }

        // One length per codeword segment: the coding passes between two terminations of the coder (B.10.7.2)
        var segmentStart = first;
        for (var pass = first; pass < first + passes; pass++)
        {
            if (pass != first + passes - 1 && !Jpeg2000CodeBlockSegment.IsTerminatedAfter(pass, style))
                continue;

            var count = pass - segmentStart + 1;
            var bits = block.LengthBits + BitOperations.Log2((uint)count);
            if (bits > MaxLengthBits)
                throw Invalid("the length of a codeword segment does not fit in 31 bits.");

            AddContribution(ref block, count, reader.ReadBits(bits));
            segmentStart = pass + 1;
        }

        block.PassCount = (ushort)(first + passes);
    }

    /// <summary>
    /// The codeword segments of a code-block of the high-throughput block coder (T.814, Annex B): the placeholder passes
    /// and the HT cleanup pass are one segment, the HT SigProp and HT MagRef passes another one.
    /// </summary>
    private void ReadHighThroughputContributions(ref Jpeg2000PacketBitReader reader, ref Jpeg2000CodeBlock block, int passes)
    {
        if (block.PassCount == 0)
        {
            // B.3: the packet that holds the HT cleanup pass holds only one, so that the passes before it, by groups of
            // three, are placeholders
            var placeholderSets = (passes - 1) / 3;
            var cleanupPasses = (3 * placeholderSets) + 1;
            var cleanupLength = ReadSegmentLength(ref reader, in block, cleanupPasses);
            var refinementPasses = passes - cleanupPasses;
            var refinementLength = refinementPasses > 0 ? ReadSegmentLength(ref reader, in block, refinementPasses) : 0;
            if (cleanupLength == 0)
            {
                // B.3: the first HT cleanup segment is not empty, so that every pass of the packet is a placeholder
                if (refinementLength != 0)
                    throw Invalid("an HT refinement segment follows an empty HT cleanup segment.");

                throw Jpeg2000Format.Unsupported("A code-block of the JPEG 2000 codestream has its placeholder passes and its HT cleanup pass in different packets.", "placeholder passes over several packets");
            }

            if (cleanupLength == 1)
                throw Invalid("the HT cleanup segment of a code-block has one byte.");

            if (placeholderSets > 0)
            {
                Tools |= Jpeg2000Tools.HighThroughputPlaceholderPasses;
            }

            block.PlaceholderSets = (byte)placeholderSets;
            AddContribution(ref block, cleanupPasses, cleanupLength);
            if (refinementPasses > 0)
            {
                AddContribution(ref block, refinementPasses, refinementLength);
            }

            return;
        }

        // The HT SigProp and HT MagRef passes of the HT set, in a later packet than its HT cleanup pass. A fourth pass
        // would be the HT cleanup pass of another HT set
        if (block.PassCount + passes > (3 * block.PlaceholderSets) + 3)
            throw Jpeg2000Format.Unsupported("A code-block of the JPEG 2000 codestream has several HT sets.", "several HT sets");

        AddContribution(ref block, passes, ReadSegmentLength(ref reader, in block, passes));
    }

    private static int ReadSegmentLength(ref Jpeg2000PacketBitReader reader, in Jpeg2000CodeBlock block, int passCount)
    {
        var bits = block.LengthBits + BitOperations.Log2((uint)passCount);
        if (bits > MaxLengthBits)
            throw Invalid("the length of a codeword segment does not fit in 31 bits.");

        return reader.ReadBits(bits);
    }

    private void AddContribution(ref Jpeg2000CodeBlock block, int passCount, int length)
    {
        if (_contributions is null || _contributionCount == _contributions.Length)
        {
            var capacity = _contributions is null ? InitialContributionCapacity : checked(_contributions.Length * 2L);
            var larger = Av1PooledArray<Jpeg2000Contribution>.Rent(_scope, capacity, clear: false);
            if (_contributions is not null)
            {
                _contributions.Span.CopyTo(larger.Span);
                _contributions.Dispose();
            }

            _contributions = larger;
        }

        var index = _contributionCount++;
        var contributions = _contributions.Span;
        contributions[index] = new Jpeg2000Contribution { Next = -1, Length = length, PassCount = passCount };
        if (block.LastContribution >= 0)
        {
            contributions[block.LastContribution].Next = index;
        }
        else
        {
            block.FirstContribution = index;
        }

        block.LastContribution = index;
    }
}
