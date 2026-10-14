namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The features of Rec. ITU-T T.800 and T.814 that a JPEG 2000 file used, recorded while it is walked and decoded. It is a
/// diagnostic for the tests, which check that the fixtures reach every feature of the decoder; it never changes the decoding.
/// </summary>
[Flags]
internal enum Jpeg2000Tools : ulong
{
    None = 0,

    /// <summary>Layer-resolution level-component-position progression.</summary>
    ProgressionLrcp = 1UL << 0,

    /// <summary>Resolution level-layer-component-position progression.</summary>
    ProgressionRlcp = 1UL << 1,

    /// <summary>Resolution level-position-component-layer progression.</summary>
    ProgressionRpcl = 1UL << 2,

    /// <summary>Position-component-resolution level-layer progression.</summary>
    ProgressionPcrl = 1UL << 3,

    /// <summary>Component-position-resolution level-layer progression.</summary>
    ProgressionCprl = 1UL << 4,

    /// <summary>A POC marker segment changes the progression.</summary>
    ProgressionChange = 1UL << 5,

    /// <summary>A resolution level has more than one precinct.</summary>
    Precincts = 1UL << 6,

    /// <summary>More than one layer.</summary>
    Layers = 1UL << 7,

    /// <summary>More than one tile.</summary>
    Tiles = 1UL << 8,

    /// <summary>A tile has more than one tile-part.</summary>
    TileParts = 1UL << 9,

    /// <summary>The tile-parts of a tile are separated by a tile-part of another tile.</summary>
    InterleavedTileParts = 1UL << 10,

    /// <summary>A tile-part does not give its length (Psot is 0): it extends to the EOC marker.</summary>
    TilePartToEnd = 1UL << 11,

    /// <summary>A tile does not give its number of tile-parts (TNsot is 0).</summary>
    UnknownTilePartCount = 1UL << 12,

    /// <summary>Selective arithmetic coding bypass.</summary>
    Bypass = 1UL << 13,

    /// <summary>Reset of the context probabilities at each coding pass.</summary>
    ResetContexts = 1UL << 14,

    /// <summary>Termination at each coding pass.</summary>
    TerminateAll = 1UL << 15,

    /// <summary>Vertically causal contexts.</summary>
    VerticallyCausal = 1UL << 16,

    /// <summary>Predictable termination.</summary>
    PredictableTermination = 1UL << 17,

    /// <summary>Segmentation symbols.</summary>
    SegmentationSymbols = 1UL << 18,

    /// <summary>A region of interest with a non-zero shift.</summary>
    RegionOfInterest = 1UL << 19,

    /// <summary>Packet headers in the main header (PPM).</summary>
    PackedHeadersMain = 1UL << 20,

    /// <summary>Packet headers in tile-part headers (PPT).</summary>
    PackedHeadersTile = 1UL << 21,

    /// <summary>A SOP marker segment precedes a packet.</summary>
    StartOfPacket = 1UL << 22,

    /// <summary>EPH markers end the packet headers.</summary>
    EndOfPacketHeader = 1UL << 23,

    /// <summary>A PLT marker segment.</summary>
    PacketLengths = 1UL << 24,

    /// <summary>A TLM marker segment.</summary>
    TilePartLengths = 1UL << 25,

    /// <summary>A COC marker segment.</summary>
    ComponentCodingStyle = 1UL << 26,

    /// <summary>A QCC marker segment.</summary>
    ComponentQuantization = 1UL << 27,

    /// <summary>No quantization.</summary>
    QuantizationNone = 1UL << 28,

    /// <summary>Scalar derived quantization.</summary>
    QuantizationDerived = 1UL << 29,

    /// <summary>Scalar expounded quantization.</summary>
    QuantizationExpounded = 1UL << 30,

    /// <summary>The reversible 5-3 wavelet transformation.</summary>
    Reversible = 1UL << 31,

    /// <summary>The irreversible 9-7 wavelet transformation.</summary>
    Irreversible = 1UL << 32,

    /// <summary>The reversible component transformation.</summary>
    ReversibleComponentTransform = 1UL << 33,

    /// <summary>The irreversible component transformation.</summary>
    IrreversibleComponentTransform = 1UL << 34,

    /// <summary>A component is sub-sampled.</summary>
    Subsampling = 1UL << 35,

    /// <summary>The image area or the tile grid does not start at the origin of the reference grid.</summary>
    Offsets = 1UL << 36,

    /// <summary>A coding style in a tile-part header (COD or COC).</summary>
    TileCodingStyle = 1UL << 37,

    /// <summary>A quantization in a tile-part header (QCD or QCC).</summary>
    TileQuantization = 1UL << 38,

    /// <summary>A code-block whose last coding passes are not in the codestream.</summary>
    TruncatedCodeBlock = 1UL << 39,

    /// <summary>A code-block of more than one codeword segment whose segment is split over several packets.</summary>
    SplitSegment = 1UL << 40,

    /// <summary>A palette (JP2 <c>pclr</c> and <c>cmap</c> boxes).</summary>
    Palette = 1UL << 41,

    /// <summary>Channel definitions (JP2 <c>cdef</c> box).</summary>
    ChannelDefinition = 1UL << 42,

    /// <summary>Code-blocks of the high-throughput block coder.</summary>
    HighThroughput = 1UL << 43,

    /// <summary>A high-throughput code-block with a significance propagation pass.</summary>
    HighThroughputSignificancePropagation = 1UL << 44,

    /// <summary>A high-throughput code-block with a magnitude refinement pass.</summary>
    HighThroughputMagnitudeRefinement = 1UL << 45,

    /// <summary>A high-throughput code-block whose first coding passes are placeholders.</summary>
    HighThroughputPlaceholderPasses = 1UL << 46,

    /// <summary>An empty packet.</summary>
    EmptyPacket = 1UL << 47,

    /// <summary>A tile whose data ends before its last packets.</summary>
    MissingPackets = 1UL << 48,
}
