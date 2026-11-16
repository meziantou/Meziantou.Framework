using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The JPEG 2000 encoder registration: one still image per output, a codestream or a JP2 file.</summary>
internal sealed class Jpeg2000EncoderCodec : ImageEncoderCodec
{
    public static Jpeg2000EncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Jpeg2000;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>Converts coefficients of the reversible path to the input of the block encoder: the magnitude in the 31 low bits and the sign in the high one.</summary>
    /// <param name="source">The coefficients.</param>
    /// <param name="destination">Receives the words; as long as <paramref name="source"/>.</param>
    internal static void ToSignMagnitude(ReadOnlySpan<int> source, Span<int> destination)
    {
        destination = destination[..source.Length];
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // The sign mask is 0 or -1: (v ^ mask) - mask is |v|, with the sign bit of the mask added
            for (; i <= source.Length - Vector128<int>.Count; i += Vector128<int>.Count)
            {
                var value = Vector128.Create(source[i..]);
                var sign = Vector128.ShiftRightArithmetic(value, 31);
                (((value ^ sign) - sign) | (sign & Vector128.Create(int.MinValue))).CopyTo(destination[i..]);
            }
        }

        ToSignMagnitudeScalar(source[i..], destination[i..]);
    }

    /// <summary>The scalar reference of <see cref="ToSignMagnitude"/>.</summary>
    internal static void ToSignMagnitudeScalar(ReadOnlySpan<int> source, Span<int> destination)
    {
        for (var x = 0; x < source.Length; x++)
        {
            var value = source[x];
            destination[x] = value < 0 ? -value | int.MinValue : value;
        }
    }

    /// <summary>
    /// Quantizes coefficients of the irreversible path (equation E-1): the magnitude is divided by the step size and
    /// truncated, and the sign of a coefficient whose quantized magnitude is not zero is set in the high bit.
    /// </summary>
    /// <param name="source">The coefficients.</param>
    /// <param name="destination">Receives the words; as long as <paramref name="source"/>.</param>
    /// <param name="step">The step size of the sub-band.</param>
    internal static void Quantize(ReadOnlySpan<float> source, Span<int> destination, float step)
    {
        destination = destination[..source.Length];
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // The division is the IEEE one, as the scalar one; the truncation of a quotient of 2^31 or more (or of a value
            // that is not a number), which the conversions may handle differently, is left to the scalar version
            var steps = Vector128.Create(step);
            var limit = Vector128.Create(2147483648f);
            for (; i <= source.Length - Vector128<float>.Count; i += Vector128<float>.Count)
            {
                var value = Vector128.Create(source[i..]);
                var quotient = Vector128.Abs(value) / steps;
                if (!Vector128.LessThanAll(quotient, limit))
                {
                    QuantizeScalar(source.Slice(i, Vector128<float>.Count), destination.Slice(i, Vector128<float>.Count), step);
                    continue;
                }

                var magnitude = Vector128.ConvertToInt32(quotient);
                var negative = Vector128.LessThan(value, Vector128<float>.Zero).AsInt32() & ~Vector128.Equals(magnitude, Vector128<int>.Zero);
                (magnitude | (negative & Vector128.Create(int.MinValue))).CopyTo(destination[i..]);
            }
        }

        QuantizeScalar(source[i..], destination[i..], step);
    }

    /// <summary>The scalar reference of <see cref="Quantize"/>.</summary>
    internal static void QuantizeScalar(ReadOnlySpan<float> source, Span<int> destination, float step)
    {
        for (var x = 0; x < source.Length; x++)
        {
            var value = source[x];
            var magnitude = (int)(MathF.Abs(value) / step);
            destination[x] = value < 0 && magnitude != 0 ? magnitude | int.MinValue : magnitude;
        }
    }

    /// <summary>
    /// The encoding state of one JPEG 2000 output (T.800). The image is cut into tiles (B.3); each component of a tile is
    /// level-shifted (G.1), goes through the forward component transformation (Annex G) and the forward wavelet
    /// transformation (Annex F), is quantized (Annex E) and its code-blocks are coded (Annexes C and D). Once every tile is
    /// coded, the coding passes are shared out among the quality layers and the packets of each tile are written in the
    /// progression order (Annex B).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// One component of one tile is transformed and coded at a time: the private state is one plane of 32-bit values as
    /// large as a tile, the coded bytes of every code-block (with quality layers, what each of their coding passes adds)
    /// and the partition of the current tile (shared with the decoder, <see cref="Jpeg2000TileGeometry"/>).
    /// </description></item>
    /// <item><description>
    /// Nothing is written before every code-block is coded: the lengths of the codestream box and of the tile-parts are
    /// then known, and the output needs no seek. The output is then emitted in pieces of at most 64 KiB.
    /// </description></item>
    /// <item><description>
    /// Lossless: the reversible filter and the reversible component transformation; no quantization. The exponents of the
    /// QCD marker segment are the nominal ones (the depth of the samples plus the gain of the sub-band), raised for a
    /// sub-band whose coefficients need more bit-planes in any tile.
    /// </description></item>
    /// <item><description>
    /// Lossy: the irreversible filter and component transformation; a dead-zone quantizer whose step sizes come from
    /// <see cref="Jpeg2000QuantizationDesign"/>; every bit-plane of the quantized coefficients is coded. The two
    /// chrominances have coarser step sizes than the luminance, in a QCC marker segment each. The opacity component keeps
    /// the reversible filter, through COC and QCC marker segments.
    /// </description></item>
    /// <item><description>
    /// Quality layers (J.14): the block coder records, for each coding pass, the length of the truncated codeword segment
    /// that decodes it (<see cref="Jpeg2000MqEncoder.GetTruncationLength"/>) and how much it reduces the squared error.
    /// The error is weighted by the squared norms of the synthesis of the sub-band and of the inverse component
    /// transformation, and squared step sizes, so that it is the error of the image. The passes on the convex hull of
    /// each code-block are its truncation points; a layer keeps, in every code-block, the points whose slope is at least
    /// a threshold, the smallest one whose output fits the target. The sizes are exact: each trial writes the packet
    /// headers of the layer from the state the previous layers left.
    /// </description></item>
    /// <item><description>
    /// The output only depends on the pixels and on the settings: integers on the reversible path, single-precision
    /// additions, products and one truncation on the irreversible one, double-precision sums in a fixed order for the
    /// layers, no date and no version string.
    /// </description></item>
    /// <item><description>
    /// Parallelism (<see cref="Jpeg2000Workers"/>): an image of at least <see cref="RowBands.MinPixelsForParallelism"/>
    /// pixels is coded by up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers. The rows of a component
    /// are read by bands, the wavelet transformation is split by strips of columns and by rows, and the code-blocks are
    /// coded by batches: in a batch, the worker <c>w</c> of <c>n</c> codes the code-blocks <c>w</c>, <c>w + n</c>... with
    /// its own block coder, into its own buffer, and the codeword segments are then appended in the order of the
    /// code-blocks. A codeword segment only depends on the coefficients of its code-block (the arithmetic coder starts and
    /// ends in it), and the quantization parameters, the layers and the packets are chosen and written once every
    /// code-block is coded, as with one worker: the bytes never depend on the number of workers.
    /// </description></item>
    /// </list>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const int BlockExponent = 6;
        private const int BlocksPerCall = 64;
        private const int BytesPerCall = 64 * 1024;
        private const int LosslessGuardBits = 2;
        private const int StepTableCount = 2;
        // The pieces of the three colors are on the stack of each worker, for each row: 64 samples keep them at 768 bytes
        private const int ColorPieceLength = 64;
        private const int SourceHead = 0;
        private const int SourcePacketHeaders = 1;
        private const int SourceFirstComponent = 2;
        private const int MaxPasses = Jpeg2000Tier1Encoder.MaxPasses;
        private const int TileHeaderLength = Jpeg2000Format.SotSegmentLength + 2;
        private const int MaxTileParts = 255;
        private const int MaxMarkerSegments = 256;
        private const int PltSegmentOverhead = 5;
        private const int MaxPltData = ushort.MaxValue - 3;
        private const int MaxTlmData = ushort.MaxValue - 4;
        private const int CapabilityHighThroughput = 0x4000;
        private const uint CapabilityPart15 = 1u << (32 - 15);

        private readonly AllocationScope _scope;
        private readonly Jpeg2000Encoder _encoder;
        private readonly PixelFormat _pixelFormat;
        private readonly int _width;
        private readonly int _height;
        private readonly int _precision;
        private readonly int _componentCount;
        private readonly int _colorCount;
        private readonly int _levels;
        private readonly bool _isLossy;
        private readonly bool _isHighThroughput;
        private readonly int _tileWidth;
        private readonly int _tileHeight;
        private readonly int _tileCount;
        private readonly int _layerCount;
        private readonly long[] _layerTargets;
        private readonly bool _tracksPasses;
        private readonly int _progression;
        private readonly Jpeg2000TilePartDivision _division;
        private readonly bool _writesTilePartLengths;
        private readonly bool _writesPacketLengths;
        private readonly byte[]? _precinctSizes;
        private readonly Av1PooledArray<byte>? _tilePartCounts;
        private readonly int _totalTileParts;
        private readonly long _packetsPerLayer;
        private readonly Jpeg2000Siz _siz;
        private readonly Jpeg2000TileCoding _coding;
        private readonly Jpeg2000Tier1Encoder _blockEncoder;
        private readonly Jpeg2000HtBlockEncoder? _highThroughputEncoder;
        private readonly PooledPayloadWriter[] _blockData;
        private readonly PooledPayloadWriter _head;
        private readonly PooledPayloadWriter _packetHeaders;
        private readonly Av1PooledArray<int> _plane;
        private readonly Av1PooledArray<int> _scratch;
        private readonly Av1PooledArray<int> _blockSamples;
        private readonly int[] _tileFirstBlock;
        private readonly int[] _bandBitPlanes;
        private readonly float[] _bandSteps;
        private readonly int[] _bandExponents;
        private readonly int[] _bandMantissas;
        private readonly double[] _bandWeights;
        private readonly int[] _bandMagnitudeBitPlanes;

        // The workers that code the code-blocks (the first one has the coder and the scratch of the session), the scratch
        // of each worker, and the code-blocks of a batch: null with one worker
        private readonly BlockCoder[]? _coders;
        private readonly Av1PooledArray<int>[]? _workerScratch;
        private readonly Av1PooledArray<int>? _batch;

        // With quality layers: the coding passes of the code-block being coded (one worker) or of each code-block of a
        // batch (several workers), and the coding passes of every code-block, in their order
        private readonly Av1PooledArray<Jpeg2000CodingPass>? _blockPasses;
        private Av1PooledArray<Jpeg2000CodingPass>? _passes;
        private int _passCount;

        // With the high-throughput block coder and several layers: the coding passes of the HT set of each code-block,
        // which the layers share out whole
        private Av1PooledArray<byte>? _highThroughputSets;

        // The lengths of the tile-parts (TLM marker segment) and of the packets of the current tile-part (PLT marker segments)
        private Av1PooledArray<long>? _tilePartLengths;
        private int _tilePartIndex;
        private Av1PooledArray<long>? _packetLengths;
        private int _packetLengthCount;

        // The code-blocks of every tile, in their order, and the number of coding passes of each one up to each layer
        private Av1PooledArray<EncodedBlock> _blocks;
        private int _blockCount;
        private Av1PooledArray<byte>? _layerPasses;
        private Av1PooledArray<double>? _slopes;
        private Jpeg2000TileGeometry _geometry;
        private Av1PooledArray<Piece>? _pieces;
        private int _pieceCount;
        private ImageFrame? _frame;
        private Step _step;
        private int _tile;
        private int _component;
        private int _band;
        private int _block;
        private int _piece;
        private int _pieceOffset;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _scope = options.Scope;
            _encoder = (Jpeg2000Encoder)options.Encoder;
            _pixelFormat = options.PixelFormat;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _precision = PixelFormats.GetBitsPerComponent(_pixelFormat);
            _colorCount = PixelFormats.IsGrayscale(_pixelFormat) ? 1 : 3;
            _componentCount = _colorCount + (PixelFormats.HasAlpha(_pixelFormat) ? 1 : 0);
            _isLossy = _encoder.Compression == Jpeg2000Compression.Lossy;
            _isHighThroughput = _encoder.BlockCoder == Jpeg2000BlockCoder.HighThroughput;
            _progression = (int)_encoder.ProgressionOrder;
            _division = _encoder.TilePartDivision;
            _writesTilePartLengths = _encoder.WriteTilePartLengths;
            _writesPacketLengths = _encoder.WritePacketLengths;

            // The tiles: the last column and the last row may be smaller
            _tileWidth = _encoder.TileWidth == 0 ? _width : Math.Min(_encoder.TileWidth, _width);
            _tileHeight = _encoder.TileHeight == 0 ? _height : Math.Min(_encoder.TileHeight, _height);
            var tileCount = (long)Jpeg2000Siz.CeilingDivide(_width, _tileWidth) * Jpeg2000Siz.CeilingDivide(_height, _tileHeight);
            if (tileCount > ushort.MaxValue)
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 image has at most 65535 tiles; tiles of {_tileWidth}x{_tileHeight} pixels cut this image into {tileCount}. Set a larger TileWidth or TileHeight."), nameof(options));

            _tileCount = (int)tileCount;
            _tileFirstBlock = new int[_tileCount + 1];

            // As many decomposition levels as the smaller side of a tile allows, so that the lowest resolution level of a
            // tile that is not on the last row or column is not empty
            var side = (long)Math.Min(_tileWidth, _tileHeight);
            while (_levels < _encoder.DecompositionLevels && side >> (_levels + 1) > 0)
            {
                _levels++;
            }

            // The layers: a target in bytes for each one (none for the complete layer)
            var layers = _encoder.QualityLayers;
            _layerCount = Math.Max(1, layers.Count);
            _layerTargets = new long[_layerCount];
            for (var l = 0; l < _layerCount; l++)
            {
                _layerTargets[l] = l < layers.Count ? layers[l].GetTargetSize((long)_width * _height) : long.MaxValue;
            }

            _tracksPasses = _layerCount > 1 || _layerTargets[0] != long.MaxValue;

            var components = new Jpeg2000ComponentSize[_componentCount];
            components.AsSpan().Fill(new Jpeg2000ComponentSize(_precision, IsSigned: false, 1, 1));
            _siz = new Jpeg2000Siz(_isHighThroughput ? CapabilityHighThroughput : 0, _width, _height, 0, 0, _tileWidth, _tileHeight, 0, 0, components);

            // The partition of the tile does not depend on the quantization: provisional values stand for it
            var bandCount = (3 * _levels) + 1;
            _precinctSizes = GetPrecinctSizes(_encoder.PrecinctSizes, _levels);
            _bandBitPlanes = new int[_componentCount * bandCount];
            _bandSteps = new float[StepTableCount * bandCount];
            _bandExponents = new int[StepTableCount * bandCount];
            _bandMantissas = new int[StepTableCount * bandCount];
            _bandWeights = new double[_componentCount * bandCount];
            _bandMagnitudeBitPlanes = new int[_componentCount * bandCount];
            _coding = CreateCoding(bandCount);
            ComputeWeights(bandCount);
            _geometry = Jpeg2000TileGeometry.Create(_scope, _siz, _coding, tileIndex: 0);
            try
            {
                // The tile-parts of every tile and the packets of a layer, which the headers and the rate control count
                if (_division != Jpeg2000TilePartDivision.None)
                {
                    _tilePartCounts = Av1PooledArray<byte>.Rent(_scope, _tileCount, clear: true, AllocationKind.Temporary);
                }

                for (var t = 0; t < _tileCount; t++)
                {
                    using var geometry = t == 0 ? null : Jpeg2000TileGeometry.Create(_scope, _siz, _coding, t);
                    var tileGeometry = geometry ?? _geometry;
                    _packetsPerLayer += tileGeometry.PrecinctCount;
                    var parts = _tilePartCounts is null ? 1 : CountTileParts(tileGeometry);
                    if (parts > MaxTileParts)
                        throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 tile has at most 255 tile-parts; the TilePartDivision {_division} cuts the tile {t} into {parts}. Set another TilePartDivision or ProgressionOrder, or fewer QualityLayers."), nameof(options));

                    if (_tilePartCounts is not null)
                    {
                        _tilePartCounts.Span[t] = (byte)parts;
                    }

                    _totalTileParts += parts;
                    if (_tilePartCounts is null && t == 0)
                    {
                        // One tile-part per tile: the other tiles need not be visited, and the packets of a layer, which
                        // only bound the HT sets of several layers, are estimated from the first tile
                        _totalTileParts = _tileCount;
                        _packetsPerLayer = (long)tileGeometry.PrecinctCount * _tileCount;
                        break;
                    }
                }

                if (_writesTilePartLengths && _totalTileParts > MaxMarkerSegments * (MaxTlmData / (GetTlmTileIndexLength() + 4)))
                    throw new ArgumentException("The tile-parts of the JPEG 2000 image are too many for the TLM marker segments: set WriteTilePartLengths to false, or use fewer tiles or tile-parts.", nameof(options));
            }
            catch
            {
                _geometry.Dispose();
                _tilePartCounts?.Dispose();
                throw;
            }

            Jpeg2000Tier1Encoder? blockEncoder = null;
            Jpeg2000HtBlockEncoder? highThroughputEncoder = null;
            PooledPayloadWriter? head = null;
            PooledPayloadWriter? packetHeaders = null;
            Av1PooledArray<EncodedBlock>? blocks = null;
            Av1PooledArray<int>? plane = null;
            Av1PooledArray<int>? scratch = null;
            Av1PooledArray<int>? blockSamples = null;
            Av1PooledArray<Jpeg2000CodingPass>? blockPasses = null;
            BlockCoder[]? coders = null;
            Av1PooledArray<int>? batch = null;
            var blockData = new PooledPayloadWriter[_componentCount];
            var workers = Jpeg2000Workers.GetWorkerCount(options.Configuration.MaxDegreeOfParallelism, (long)_width * _height, _geometry.Blocks.Length);
            try
            {
                blockEncoder = new Jpeg2000Tier1Encoder(_scope);
                if (_isHighThroughput)
                {
                    highThroughputEncoder = new Jpeg2000HtBlockEncoder(_scope);
                    if (_tracksPasses)
                    {
                        // The rate control codes the refinement passes: their buffers are rented before any worker starts
                        highThroughputEncoder.RentRefinementBuffers();
                    }
                }

                head = new PooledPayloadWriter(_scope);
                packetHeaders = new PooledPayloadWriter(_scope);
                for (var c = 0; c < _componentCount; c++)
                {
                    blockData[c] = new PooledPayloadWriter(_scope);
                }

                blocks = Av1PooledArray<EncodedBlock>.Rent(_scope, _geometry.Blocks.Length, clear: true, AllocationKind.Temporary);
                plane = Av1PooledArray<int>.Rent(_scope, (long)_tileWidth * _tileHeight, clear: false, AllocationKind.Temporary);
                scratch = Av1PooledArray<int>.Rent(_scope, Jpeg2000Dwt.GetScratchLength(_tileWidth, _tileHeight), clear: false, AllocationKind.Temporary);
                blockSamples = Av1PooledArray<int>.Rent(_scope, Jpeg2000Tier1Tables.MaxSamples, clear: false, AllocationKind.Temporary);
                if (_tracksPasses)
                {
                    blockPasses = Av1PooledArray<Jpeg2000CodingPass>.Rent(_scope, (long)MaxPasses * (workers > 1 ? BlocksPerCall * workers : 1), clear: false, AllocationKind.Temporary);
                }

                if (workers > 1)
                {
                    // Everything the workers use is rented here: the first worker has the coder and the scratch of the
                    // session, and each worker has a buffer for the codeword segments of its code-blocks
                    coders = new BlockCoder[workers];
                    coders[0] = new BlockCoder(_scope, blockEncoder, highThroughputEncoder, blockSamples, scratch);
                    for (var w = 1; w < workers; w++)
                    {
                        coders[w] = BlockCoder.Create(_scope, scratch.Length, _isHighThroughput, _tracksPasses);
                    }

                    batch = Av1PooledArray<int>.Rent(_scope, 2L * BlocksPerCall * workers, clear: false, AllocationKind.Temporary);
                }
            }
            catch
            {
                _geometry.Dispose();
                _tilePartCounts?.Dispose();
                blockEncoder?.Dispose();
                highThroughputEncoder?.Dispose();
                head?.Dispose();
                packetHeaders?.Dispose();
                blocks?.Dispose();
                plane?.Dispose();
                scratch?.Dispose();
                blockSamples?.Dispose();
                blockPasses?.Dispose();
                batch?.Dispose();
                foreach (var writer in blockData)
                {
                    writer?.Dispose();
                }

                foreach (var coder in coders ?? [])
                {
                    coder?.Dispose();
                }

                throw;
            }

            _coders = coders;
            _workerScratch = coders is null ? null : [.. coders.Select(coder => coder.Scratch)];
            _batch = batch;

            _blockEncoder = blockEncoder;
            _highThroughputEncoder = highThroughputEncoder;
            _head = head;
            _packetHeaders = packetHeaders;
            _blockData = blockData;
            _blocks = blocks;
            _plane = plane;
            _scratch = scratch;
            _blockSamples = blockSamples;
            _blockPasses = blockPasses;
        }

        private enum Step
        {
            None,
            Transform,
            Blocks,
            Assemble,
            Emit,
            Complete,
        }

        private bool HasAlpha => _componentCount > _colorCount;

        /// <summary>Gets the code-block style of every tile-component: the high-throughput block coder (T.814, Table A.3) or none.</summary>
        private int BlockStyle => _isHighThroughput ? Jpeg2000CodingStyle.StyleHighThroughput : 0;

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("JPEG 2000 output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A JPEG 2000 image stores a single frame.");

            _frame = frame;
            _tile = 0;
            _component = 0;
            _step = Step.Transform;
        }

        public override void BeginComplete(int frameCount) => _step = Step.Complete;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Transform:
                    if (_component == 0)
                    {
                        BeginTile();
                    }

                    TransformComponent();
                    _band = _geometry.Resolutions[_geometry.Components[_component].FirstResolution].FirstBand;
                    _block = 0;
                    _step = Step.Blocks;
                    return false;

                case Step.Blocks:
                    if (_coders is null ? EncodeBlocks() : EncodeBlocksByWorkers())
                    {
                        _component++;
                        if (_component == _componentCount)
                        {
                            _component = 0;
                            _tile++;
                        }

                        _step = _tile < _tileCount ? Step.Transform : Step.Assemble;
                    }

                    return false;

                case Step.Assemble:
                    Assemble();
                    _frame = null;
                    _piece = 0;
                    _pieceOffset = 0;
                    _step = Step.Emit;
                    return false;

                case Step.Emit:
                    if (!Emit(output))
                        return false;

                    _step = Step.None;
                    return true;

                case Step.Complete:
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No JPEG 2000 operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _geometry.Dispose();
                _blockEncoder.Dispose();
                _highThroughputEncoder?.Dispose();
                _head.Dispose();
                _packetHeaders.Dispose();
                _blocks.Dispose();
                _plane.Dispose();
                _scratch.Dispose();
                _blockSamples.Dispose();
                _blockPasses?.Dispose();
                _passes?.Dispose();
                _passes = null;
                _layerPasses?.Dispose();
                _layerPasses = null;
                _slopes?.Dispose();
                _slopes = null;
                _highThroughputSets?.Dispose();
                _highThroughputSets = null;
                _tilePartLengths?.Dispose();
                _tilePartLengths = null;
                _tilePartCounts?.Dispose();
                _packetLengths?.Dispose();
                _packetLengths = null;
                _batch?.Dispose();
                foreach (var coder in _coders ?? [])
                {
                    coder.Dispose();
                }

                _pieces?.Dispose();
                _pieces = null;
                foreach (var writer in _blockData)
                {
                    writer.Dispose();
                }

                _frame = null;
            }

            base.Dispose(disposing);
        }

        private bool IsReversible(int component) => !_isLossy || component >= _colorCount;

        private static int GetGain(int band) => band == 0 ? 0 : ((band - 1) % 3) == 2 ? 2 : 1;

        /// <summary>Gets the table of step sizes of a lossy component: 1 for the two chrominances of a color image, 0 for the luminance or the gray levels.</summary>
        private int GetStepTable(int component) => _colorCount == 3 && component is 1 or 2 ? 1 : 0;

        private Jpeg2000TileCoding CreateCoding(int bandCount)
        {
            // The step sizes of the lossy sub-bands: the base step of the quality divided by the norm of each sub-band, for
            // the luminance, then for the chrominances
            for (var table = 0; table < StepTableCount; table++)
            {
                var baseStep = Jpeg2000QuantizationDesign.GetBaseStep(_encoder.Quality, _precision, isChrominance: table == 1);
                for (var band = 0; band < bandCount; band++)
                {
                    GetBandLevel(band, out var level, out var isHighHorizontally, out var isHighVertically);
                    var norm = Jpeg2000QuantizationDesign.GetNorm(level, isHighHorizontally, isHighVertically);
                    var range = _precision + GetGain(band);
                    var index = (table * bandCount) + band;
                    Jpeg2000QuantizationDesign.GetStepParameters(baseStep / norm, range, out _bandExponents[index], out _bandMantissas[index]);
                    _bandSteps[index] = Jpeg2000QuantizationDesign.GetStep(_bandExponents[index], _bandMantissas[index], range);
                }
            }

            var headers = new Jpeg2000HeaderSet(_componentCount);
            var colorStyle = new Jpeg2000CodingStyle(_levels, BlockExponent, BlockExponent, BlockStyle, isReversible: !_isLossy, _precinctSizes);
            headers.CodingDefaults = new Jpeg2000CodingDefaults(mayUseSop: false, usesEph: false, _progression, _layerCount, usesComponentTransform: _colorCount == 3, colorStyle);
            var none = new Jpeg2000Quantization(Jpeg2000Quantization.StyleNone, LosslessGuardBits, new byte[bandCount], new ushort[bandCount]);
            headers.Quantization = _isLossy ? new Jpeg2000Quantization(Jpeg2000Quantization.StyleExpounded, 1, new byte[bandCount], new ushort[bandCount]) : none;
            if (_isLossy && HasAlpha)
            {
                headers.TrySetComponentStyle(_colorCount, new Jpeg2000CodingStyle(_levels, BlockExponent, BlockExponent, BlockStyle, isReversible: true, _precinctSizes));
                headers.TrySetComponentQuantization(_colorCount, none);
            }

            var capabilities = _isHighThroughput ? Jpeg2000Capabilities.Read([0x00, 0x02, 0x00, 0x00, 0x00, 0x00]) : null;
            return Jpeg2000TileCoding.Resolve(_siz, headers, tile: null, capabilities);
        }

        /// <summary>Gets the decomposition level of a sub-band (its index in a tile-component) and whether it is high-pass in each direction.</summary>
        private void GetBandLevel(int band, out int level, out bool isHighHorizontally, out bool isHighVertically)
        {
            var resolution = band == 0 ? 0 : ((band - 1) / 3) + 1;
            var orientation = band == 0 ? -1 : (band - 1) % 3;
            level = band == 0 ? _levels : _levels - resolution + 1;
            isHighHorizontally = orientation is 0 or 2;
            isHighVertically = orientation is 1 or 2;
        }

        /// <summary>
        /// Gets the precinct sizes of the COD and COC marker segments (Table A.21): for each resolution level from the
        /// lowest, the exponent of the width in the low half of a byte and that of the height in the high half; the sizes
        /// are given from the highest level down, and the last one is used for the lower levels. <see langword="null"/>
        /// without precincts.
        /// </summary>
        private static byte[]? GetPrecinctSizes(IReadOnlyList<Size> sizes, int levels)
        {
            if (sizes.Count == 0)
                return null;

            var result = new byte[levels + 1];
            for (var r = 0; r <= levels; r++)
            {
                var size = sizes[Math.Min(levels - r, sizes.Count - 1)];
                result[r] = (byte)(BitOperations.Log2((uint)size.Width) | (BitOperations.Log2((uint)size.Height) << 4));
            }

            return result;
        }

        /// <summary>Gets the number of tile-parts of a tile.</summary>
        private int GetTileParts(int tile) => _tilePartCounts is null ? 1 : _tilePartCounts.Span[tile];

        /// <summary>Gets the number of bytes of the index of a tile in the TLM marker segments (Ttlm): one for at most 256 tiles, two otherwise.</summary>
        private int GetTlmTileIndexLength() => _tileCount <= 256 ? 1 : 2;

        /// <summary>Counts the tile-parts of a tile: one, then one more at each packet that starts a tile-part.</summary>
        private int CountTileParts(Jpeg2000TileGeometry geometry)
        {
            using var order = RentPacketOrder(geometry, out var count);
            var packets = order.Span[..count];
            var parts = count == 0 ? 0 : 1;
            for (var i = 1; i < packets.Length; i++)
            {
                if (StartsTilePart(in packets[i - 1], in packets[i]))
                {
                    parts++;
                }
            }

            return parts;
        }

        /// <summary>Gets whether a packet starts a tile-part: its resolution level, layer or component, as <see cref="Jpeg2000Encoder.TilePartDivision"/> says, is not the one of the previous packet.</summary>
        private bool StartsTilePart(in PacketOrder previous, in PacketOrder packet) => _division switch
        {
            Jpeg2000TilePartDivision.Resolution => previous.Resolution != packet.Resolution,
            Jpeg2000TilePartDivision.Layer => previous.Layer != packet.Layer,
            Jpeg2000TilePartDivision.Component => previous.Component != packet.Component,
            _ => false,
        };

        /// <summary>Lists the packets of a tile in the progression order (B.12): every layer of every precinct of every resolution level of every component.</summary>
        /// <param name="geometry">The partition of the tile.</param>
        /// <param name="count">Receives the number of packets.</param>
        /// <returns>The packets; the caller disposes it.</returns>
        private Av1PooledArray<PacketOrder> RentPacketOrder(Jpeg2000TileGeometry geometry, out int count)
        {
            var total = checked((long)geometry.PrecinctCount * _layerCount);
            var order = Av1PooledArray<PacketOrder>.Rent(_scope, Math.Max(1, total), clear: false, AllocationKind.Temporary);
            try
            {
                var packets = order.Span;
                var components = geometry.Components;
                var index = 0;
                switch (_progression)
                {
                    case Jpeg2000CodingDefaults.ProgressionLrcp:
                    case Jpeg2000CodingDefaults.ProgressionRlcp:
                        var isLayerFirst = _progression == Jpeg2000CodingDefaults.ProgressionLrcp;
                        for (var outer = 0; outer < (isLayerFirst ? _layerCount : _levels + 1); outer++)
                        {
                            for (var inner = 0; inner < (isLayerFirst ? _levels + 1 : _layerCount); inner++)
                            {
                                var layer = isLayerFirst ? outer : inner;
                                var r = isLayerFirst ? inner : outer;
                                foreach (var component in components)
                                {
                                    var precincts = geometry.Resolutions[component.FirstResolution + r].PrecinctCount;
                                    for (var precinct = 0; precinct < precincts; precinct++)
                                    {
                                        packets[index++] = new PacketOrder { Component = component.Index, Resolution = r, Precinct = precinct, Layer = layer };
                                    }
                                }
                            }
                        }

                        break;

                    default:
                    {
                        using var positions = Av1PooledArray<Jpeg2000PacketPosition>.Rent(_scope, Math.Max(1, geometry.PrecinctCount), clear: false, AllocationKind.Temporary);
                        var sorted = positions.Span[..geometry.PrecinctCount];
                        Jpeg2000PacketPosition.Fill(_siz, geometry, sorted);
                        Jpeg2000PacketPosition.Sort(sorted, _progression);
                        foreach (ref readonly var position in sorted)
                        {
                            for (var layer = 0; layer < _layerCount; layer++)
                            {
                                packets[index++] = new PacketOrder { Component = position.Component, Resolution = position.Resolution, Precinct = position.Precinct, Layer = layer };
                            }
                        }

                        break;
                    }
                }

                count = index;
                return order;
            }
            catch
            {
                order.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Computes the weight of the squared error of a coefficient of each sub-band of each component in the squared error
        /// of the image: the squared norms of the synthesis of the sub-band and of the inverse component transformation, and
        /// the squared step size of a lossy sub-band.
        /// </summary>
        private void ComputeWeights(int bandCount)
        {
            for (var c = 0; c < _componentCount; c++)
            {
                // The squared norm of the column of the component in the inverse component transformation (G.2 and G.3)
                var componentWeight = 1.0;
                if (_colorCount == 3 && c < 3)
                {
                    componentWeight = (_isLossy, c) switch
                    {
                        (_, 0) => 3,
                        (true, 1) => (0.344136 * 0.344136) + (1.772 * 1.772),
                        (true, _) => (1.402 * 1.402) + (0.714136 * 0.714136),
                        _ => 11.0 / 16,
                    };
                }

                for (var band = 0; band < bandCount; band++)
                {
                    GetBandLevel(band, out var level, out var isHighHorizontally, out var isHighVertically);
                    double weight;
                    if (IsReversible(c))
                    {
                        weight = Jpeg2000QuantizationDesign.GetReversibleNorm(level, isHighHorizontally, isHighVertically);
                    }
                    else
                    {
                        weight = Jpeg2000QuantizationDesign.GetNorm(level, isHighHorizontally, isHighVertically) * _bandSteps[(GetStepTable(c) * bandCount) + band];
                    }

                    _bandWeights[(c * bandCount) + band] = componentWeight * weight * weight;
                }
            }
        }

        /// <summary>Computes the partition of the current tile and makes room for its code-blocks.</summary>
        private void BeginTile()
        {
            if (_tile != 0)
            {
                _geometry.Dispose();
                _geometry = Jpeg2000TileGeometry.Create(_scope, _siz, _coding, _tile);
            }

            var blockCount = _geometry.Blocks.Length;
            _tileFirstBlock[_tile] = _blockCount;
            if (_blocks.Length < (long)_blockCount + blockCount)
            {
                var larger = Av1PooledArray<EncodedBlock>.Rent(_scope, Math.Max(checked((long)_blockCount + blockCount), 2L * _blocks.Length), clear: true, AllocationKind.Temporary);
                _blocks.Span[.._blockCount].CopyTo(larger.Span);
                _blocks.Dispose();
                _blocks = larger;
            }

            _blockCount += blockCount;
            _tileFirstBlock[_tile + 1] = _blockCount;
        }

        /// <summary>Fills the plane with one component of the current tile and decomposes it into its sub-bands.</summary>
        private void TransformComponent()
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var component = _component;
            var geometry = _geometry;
            var left = (int)geometry.X0;
            var top = (int)geometry.Y0;
            var width = (int)(geometry.X1 - geometry.X0);
            var height = (int)(geometry.Y1 - geometry.Y0);
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_pixelFormat);
            var plane = _plane.Span[..(width * height)];
            var coders = _coders;
            using (var lease = frame.GetStorage().AcquireLease())
            {
                if (coders is null)
                {
                    for (var y = 0; y < height; y++)
                    {
                        if ((y & 63) == 0)
                        {
                            CancellationToken.ThrowIfCancellationRequested();
                        }

                        ReadComponentRow(lease.GetRowBytes(top + y).Slice(left * bytesPerPixel, width * bytesPerPixel), component, plane.Slice(y * width, width));
                    }
                }
                else
                {
                    // Each row of the plane is computed from its row of pixels alone. The lease is held while the workers
                    // read the rows of the leased storage
                    var storage = lease.Storage;
                    var cancellationToken = CancellationToken;
                    var bands = Jpeg2000Workers.GetBandCount(coders.Length, (long)width * height, height, RowBands.MinRowsPerBand);
                    Jpeg2000Workers.RunBands(bands, band =>
                    {
                        var rows = _plane.Span;
                        for (int y = Jpeg2000Workers.GetBandStart(height, band, bands), end = Jpeg2000Workers.GetBandStart(height, band + 1, bands); y < end; y++)
                        {
                            if ((y & 63) == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }

                            ReadComponentRow(storage.GetRowSpanCore(top + y).Slice(left * bytesPerPixel, width * bytesPerPixel), component, rows.Slice(y * width, width));
                        }
                    });
                }
            }

            var firstResolution = geometry.Components[component].FirstResolution;
            if (coders is not null)
            {
                var levels = new ArraySegment<Jpeg2000ResolutionGeometry>(geometry.Resolutions, firstResolution, _levels + 1);
                if (IsReversible(component))
                {
                    Jpeg2000Dwt.ForwardReversible(_plane, width, levels, _workerScratch!, coders.Length);
                }
                else
                {
                    Jpeg2000Dwt.ForwardIrreversible(_plane, width, levels, _workerScratch!, coders.Length);
                }

                return;
            }

            var resolutions = geometry.Resolutions.AsSpan(firstResolution, _levels + 1);
            if (IsReversible(component))
            {
                Jpeg2000Dwt.ForwardReversible(plane, width, resolutions, _scratch.Span);
            }
            else
            {
                Jpeg2000Dwt.ForwardIrreversible(unsafe(MemoryMarshal.Cast<int, float>(plane)), width, resolutions, unsafe(MemoryMarshal.Cast<int, float>(_scratch.Span)));
            }
        }

        /// <summary>Computes one row of a component from a row of pixels: the level shift (G.1) and the forward component transformation (G.2 or G.3).</summary>
        private void ReadComponentRow(ReadOnlySpan<byte> pixels, int component, Span<int> row)
        {
            var offset = 1 << (_precision - 1);
            var width = row.Length;
            if (_colorCount == 1 || component >= _colorCount)
            {
                // A gray level or an opacity is stored as it is
                var channels = PixelFormats.GetBytesPerPixel(_pixelFormat) / (_precision / 8);
                var channel = component >= _colorCount ? channels - 1 : 0;
                if (_precision == 8)
                {
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = pixels[(x * channels) + channel] - offset;
                    }
                }
                else
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = samples[(x * channels) + channel] - offset;
                    }
                }

                if (_isLossy && component < _colorCount)
                {
                    ConvertToSingle(row);
                }

                return;
            }

            // The samples of the three colors are separated piece by piece, then transformed together
            Span<int> red = stackalloc int[ColorPieceLength];
            Span<int> green = stackalloc int[ColorPieceLength];
            Span<int> blue = stackalloc int[ColorPieceLength];
            var floats = unsafe(MemoryMarshal.Cast<int, float>(row));
            var deep = _precision == 16 ? unsafe(MemoryMarshal.Cast<byte, ushort>(pixels)) : default;
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_pixelFormat);
            var redIndex = _pixelFormat == PixelFormat.Bgra32 ? 2 : 0;
            var blueIndex = 2 - redIndex;
            for (var start = 0; start < width; start += ColorPieceLength)
            {
                var count = Math.Min(ColorPieceLength, width - start);
                if (_precision == 8)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var pixel = pixels.Slice((start + i) * bytesPerPixel, 3);
                        red[i] = pixel[redIndex] - offset;
                        green[i] = pixel[1] - offset;
                        blue[i] = pixel[blueIndex] - offset;
                    }
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        var pixel = deep.Slice(4 * (start + i), 3);
                        red[i] = pixel[0] - offset;
                        green[i] = pixel[1] - offset;
                        blue[i] = pixel[2] - offset;
                    }
                }

                if (_isLossy)
                {
                    Jpeg2000ColorTransform.ForwardIrreversible(red[..count], green[..count], blue[..count], component, floats.Slice(start, count));
                }
                else
                {
                    Jpeg2000ColorTransform.ForwardReversible(red[..count], green[..count], blue[..count], component, row.Slice(start, count));
                }
            }
        }

        private static void ConvertToSingle(Span<int> row)
        {
            var floats = unsafe(MemoryMarshal.Cast<int, float>(row));
            for (var x = 0; x < row.Length; x++)
            {
                floats[x] = row[x];
            }
        }

        /// <summary>Codes a bounded number of code-blocks of the current component.</summary>
        /// <returns><see langword="true"/> once every code-block of the component is coded.</returns>
        private bool EncodeBlocks()
        {
            CancellationToken.ThrowIfCancellationRequested();
            var component = _component;
            var tileComponent = _geometry.Components[component];
            var firstBand = _geometry.Resolutions[tileComponent.FirstResolution].FirstBand;
            var bandCount = (3 * _levels) + 1;
            var firstBlock = _tileFirstBlock[_tile];
            var records = _blocks.Span;
            var output = _blockData[component];
            var passes = _blockPasses is null ? default : _blockPasses.Span[..MaxPasses];
            var budget = BlocksPerCall;
            while (_band < firstBand + bandCount)
            {
                ref readonly var band = ref _geometry.Bands[_band];
                var blockCount = band.BlocksWide * band.BlocksHigh;
                if (_block == blockCount)
                {
                    _band++;
                    _block = 0;
                    continue;
                }

                if (budget-- == 0)
                    return false;

                var index = band.FirstBlock + _block++;
                ref var record = ref records[firstBlock + index];
                record.Offset = output.Length;
                record.BitPlanes = (byte)EncodeBlock(_blockEncoder, _highThroughputEncoder, _blockSamples.Span, output, _band, index, passes, out record.Length);
                AddPasses(ref record, passes, component, _band - firstBand);
                ref var maximum = ref _bandBitPlanes[(component * bandCount) + (_band - firstBand)];
                maximum = Math.Max(maximum, record.BitPlanes);
            }

            return true;
        }

        /// <summary>
        /// Codes the next batch of code-blocks of the current component by several workers, then appends their codeword
        /// segments in their order: the bytes of <see cref="EncodeBlocks"/>.
        /// </summary>
        /// <returns><see langword="true"/> once every code-block of the component is coded.</returns>
        private bool EncodeBlocksByWorkers()
        {
            var cancellationToken = CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var coders = _coders!;
            var component = _component;
            var tileComponent = _geometry.Components[component];
            var firstBand = _geometry.Resolutions[tileComponent.FirstResolution].FirstBand;
            var bandCount = (3 * _levels) + 1;
            var endBand = firstBand + bandCount;
            var firstBlock = _tileFirstBlock[_tile];

            // The batch: the sub-band and the index of each code-block
            var batch = _batch!.Span;
            var capacity = batch.Length / 2;
            var count = 0;
            while (count < capacity && SkipCodedBands(endBand))
            {
                batch[2 * count] = _band;
                batch[(2 * count) + 1] = _geometry.Bands[_band].FirstBlock + _block++;
                count++;
            }

            foreach (var coder in coders)
            {
                coder.Output.Reset();
            }

            // The worker w codes the code-blocks w, w + n...: its buffer holds their segments in that order, and the offset
            // of each segment in it is kept in its record until the segments are appended. The coding passes of the k-th
            // code-block of the batch go to the k-th slot of the buffer of passes
            Jpeg2000Workers.RunInterleaved(coders.Length, count, (w, k) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var coder = coders[w];
                var items = _batch.Span;
                var index = items[(2 * k) + 1];
                var passes = _blockPasses is null ? default : _blockPasses.Span.Slice(k * MaxPasses, MaxPasses);
                ref var record = ref _blocks.Span[firstBlock + index];
                record.Offset = coder.Output.Length;
                record.BitPlanes = (byte)EncodeBlock(coder.Encoder, coder.HighThroughputEncoder, coder.Samples.Span, coder.Output, items[2 * k], index, passes, out record.Length);
            });

            var records = _blocks.Span;
            var output = _blockData[component];
            for (var k = 0; k < count; k++)
            {
                var band = batch[2 * k];
                ref var record = ref records[firstBlock + batch[(2 * k) + 1]];
                var segment = coders[k % coders.Length].Output.WrittenSpan.Slice(record.Offset, record.Length);
                record.Offset = output.Length;
                output.Write(segment);
                AddPasses(ref record, _blockPasses is null ? default : _blockPasses.Span.Slice(k * MaxPasses, MaxPasses), component, band - firstBand);
                ref var maximum = ref _bandBitPlanes[(component * bandCount) + (band - firstBand)];
                maximum = Math.Max(maximum, record.BitPlanes);
            }

            return !SkipCodedBands(endBand);
        }

        /// <summary>Moves past the sub-bands whose code-blocks are all coded.</summary>
        /// <returns><see langword="true"/> while a code-block of the component is left.</returns>
        private bool SkipCodedBands(int endBand)
        {
            while (_band < endBand)
            {
                ref readonly var band = ref _geometry.Bands[_band];
                if (_block < band.BlocksWide * band.BlocksHigh)
                    return true;

                _band++;
                _block = 0;
            }

            return false;
        }

        /// <summary>Quantizes a code-block of the current component and codes it.</summary>
        /// <returns>The number of coded bit-planes: 0 when every coefficient is zero, and nothing is written.</returns>
        private int EncodeBlock(Jpeg2000Tier1Encoder encoder, Jpeg2000HtBlockEncoder? highThroughputEncoder, Span<int> samples, PooledPayloadWriter output, int bandIndex, int index, Span<Jpeg2000CodingPass> passes, out int length)
        {
            var component = _component;
            var geometry = _geometry;
            var firstBand = geometry.Resolutions[geometry.Components[component].FirstResolution].FirstBand;
            var bandCount = (3 * _levels) + 1;
            var stride = (int)(geometry.X1 - geometry.X0);
            var plane = _plane.Span;
            ref readonly var band = ref geometry.Bands[bandIndex];
            ref readonly var block = ref geometry.Blocks[index];
            int width = block.Width;
            int height = block.Height;
            var origin = ((band.PlaneY + block.Y) * stride) + band.PlaneX + block.X;
            var isReversible = IsReversible(component);
            if (isReversible)
            {
                for (var y = 0; y < height; y++)
                {
                    ToSignMagnitude(plane.Slice(origin + (y * stride), width), samples.Slice(y * width, width));
                }
            }
            else
            {
                var coefficients = unsafe(MemoryMarshal.Cast<int, float>(plane));
                var step = _bandSteps[(GetStepTable(component) * bandCount) + (bandIndex - firstBand)];
                for (var y = 0; y < height; y++)
                {
                    Quantize(coefficients.Slice(origin + (y * stride), width), samples.Slice(y * width, width), step);
                }
            }

            if (highThroughputEncoder is not null)
                return highThroughputEncoder.Encode(samples, width, height, output, out length, passes, isIrreversible: !isReversible);

            return encoder.Encode(samples, width, height, band.Orientation, output, out length, passes, isIrreversible: !isReversible);
        }

        /// <summary>
        /// Gets the number of coding passes the rate control chooses from for a code-block: the passes of the standard
        /// coder, or, for the high-throughput block coder, as many points numbered alike: the HT set whose HT cleanup pass
        /// ends at each bit-plane, from the highest one, and its HT SigProp and HT MagRef passes.
        /// </summary>
        private static int GetPassTotal(in EncodedBlock record)
            => record.BitPlanes == 0 ? 0 : (3 * record.BitPlanes) - 2;

        /// <summary>Appends the coding passes of a code-block to those of the image, weighted so that they measure the error of the image.</summary>
        private void AddPasses(ref EncodedBlock record, ReadOnlySpan<Jpeg2000CodingPass> passes, int component, int band)
        {
            if (passes.IsEmpty || record.BitPlanes == 0)
                return;

            var count = GetPassTotal(in record);
            if (_passes is null || _passes.Length < (long)_passCount + count)
            {
                var larger = Av1PooledArray<Jpeg2000CodingPass>.Rent(_scope, Math.Max(checked((long)_passCount + count), _passes is null ? 4096 : 2L * _passes.Length), clear: false, AllocationKind.Temporary);
                _passes?.Span[.._passCount].CopyTo(larger.Span);
                _passes?.Dispose();
                _passes = larger;
            }

            var weight = _bandWeights[(component * ((3 * _levels) + 1)) + band];
            var destination = _passes.Span.Slice(_passCount, count);
            for (var i = 0; i < count; i++)
            {
                destination[i] = new Jpeg2000CodingPass { Length = passes[i].Length, Distortion = passes[i].Distortion * weight };
            }

            record.FirstPass = _passCount;
            _passCount += count;
        }

        /// <summary>Chooses the quantization parameters and the layers, then writes the headers and the packet headers and lists the pieces of the output.</summary>
        private void Assemble()
        {
            var bandCount = (3 * _levels) + 1;

            // The number of bit-planes of a sub-band is its exponent plus the guard bits, less one (equation E-2): the
            // exponents of the reversible sub-bands and the guard bits of the others are raised as the coefficients need.
            // The luminance and the chrominances have their own step sizes and the same guard bits
            Span<byte> reversibleExponents = stackalloc byte[bandCount];
            var guardBits = 1;
            for (var band = 0; band < bandCount; band++)
            {
                var needed = 0;
                for (var c = 0; c < _componentCount; c++)
                {
                    // The high-throughput block coder keeps a bit-plane above the largest magnitude: FFmpeg refuses an HT
                    // cleanup pass whose exponent bound uses every bit-plane, which T.814 allows
                    var bitPlanes = _bandBitPlanes[(c * bandCount) + band] + (_isHighThroughput ? 1 : 0);
                    if (IsReversible(c))
                    {
                        needed = Math.Max(needed, bitPlanes);
                    }
                    else
                    {
                        guardBits = Math.Max(guardBits, bitPlanes - _bandExponents[(GetStepTable(c) * bandCount) + band] + 1);
                    }
                }

                reversibleExponents[band] = (byte)Math.Max(_precision + GetGain(band), needed + 1 - LosslessGuardBits);
            }

            if (guardBits > 7)
                throw new InvalidOperationException("The quantized coefficients need more than seven guard bits.");

            for (var c = 0; c < _componentCount; c++)
            {
                for (var band = 0; band < bandCount; band++)
                {
                    _bandMagnitudeBitPlanes[(c * bandCount) + band] = IsReversible(c) ? reversibleExponents[band] + LosslessGuardBits - 1 : _bandExponents[(GetStepTable(c) * bandCount) + band] + guardBits - 1;
                }
            }

            // The first piece is the head of the output, which needs the length of what follows it
            AddPiece(SourceHead, 0, 0);
            if (_tracksPasses)
            {
                WriteHead(reversibleExponents, guardBits, codestreamLength: 0);
                var headLength = _head.Length;
                _head.Reset();
                AllocateLayers(headLength);
            }

            var codestreamLength = WriteTiles();
            WriteHead(reversibleExponents, guardBits, codestreamLength);
        }

        /// <summary>Gets the number of coding passes of a code-block in the layers up to <paramref name="layer"/>.</summary>
        private int GetPasses(int block, int layer)
        {
            if (_layerPasses is not null)
                return _layerPasses.Span[(block * _layerCount) + layer];

            return GetPassTotal(in _blocks.Span[block]);
        }

        /// <summary>Gets the length of the codeword segment of a code-block that decodes its first coding passes.</summary>
        private int GetSegmentLength(int block, int passes)
        {
            if (passes == 0)
                return 0;

            ref readonly var record = ref _blocks.Span[block];
            return _passes is null || passes == (3 * record.BitPlanes) - 2 ? record.Length : _passes.Span[record.FirstPass + passes - 1].Length;
        }

        /// <summary>
        /// Gets where the HT segments of a code-block that the rate control chose are, in the coded bytes of its component:
        /// without rate control, the only HT cleanup segment, which codes every bit-plane; otherwise the segments of the
        /// chosen HT set, after those of the previous sets. The HT refinement segment follows the HT cleanup segment.
        /// </summary>
        /// <param name="block">The code-block.</param>
        /// <param name="passes">The chosen point: <c>3 * set + 1</c> for the HT cleanup pass of a set, plus one for each of its refinement passes.</param>
        /// <param name="offset">Receives the offset of the HT cleanup segment from the first segment of the code-block.</param>
        /// <param name="cleanupLength">Receives the length of the HT cleanup segment.</param>
        /// <param name="refinementLength">Receives the length of the HT refinement segment: 0 without refinement pass.</param>
        private void GetHighThroughputSegments(int block, int passes, out int offset, out int cleanupLength, out int refinementLength)
        {
            ref readonly var record = ref _blocks.Span[block];
            offset = 0;
            if (_passes is null)
            {
                cleanupLength = record.Length;
                refinementLength = 0;
                return;
            }

            var candidates = _passes.Span.Slice(record.FirstPass, passes);
            var set = (passes - 1) / 3;
            for (var q = 0; q < set; q++)
            {
                offset += candidates[(3 * q) + 2].Length;
            }

            cleanupLength = candidates[3 * set].Length;
            refinementLength = candidates[passes - 1].Length - cleanupLength;
        }

        /// <summary>
        /// Gets the number of bit-planes a code-block of the high-throughput block coder declares as empty (its P_blk): the
        /// bit-plane its HT cleanup pass ends at is <c>Mb - 1 - P_blk</c> (T.814, 7.6), that of the chosen HT set. No
        /// placeholder pass is written, which OpenJPEG does not read.
        /// </summary>
        private int GetHighThroughputZeroBitPlanes(int block, int passes, int bandBitPlanes)
            => bandBitPlanes - _blocks.Span[block].BitPlanes + ((passes - 1) / 3);

        /// <summary>
        /// Gets the Ccap field of Part 15 (T.814, Table A.4): only code-blocks of the high-throughput block coder, one HT set
        /// each, no region of interest, whether a component is irreversible, and the largest number of magnitude bit-planes
        /// of a sub-band (MAGB) in the five low bits.
        /// </summary>
        private int GetHighThroughputCapabilities()
        {
            var largest = 0;
            foreach (var bitPlanes in _bandMagnitudeBitPlanes)
            {
                largest = Math.Max(largest, bitPlanes);
            }

            var bound = largest <= 8 ? 0 : largest <= 27 ? largest - 8 : Math.Min(31, 19 + ((largest - 27 + 3) / 4));
            return (_isLossy ? 0x20 : 0) | bound;
        }

        /// <summary>
        /// Chooses the number of coding passes of every code-block in each layer (J.14): the points of the convex hull of
        /// the rate and distortion of each code-block whose slope is at least a threshold, the smallest threshold whose
        /// output fits the target of the layer.
        /// </summary>
        /// <param name="headLength">The length of the head of the output: the boxes of a JP2 file and the main header.</param>
        /// <remarks>
        /// A code-block of the high-throughput block coder has one HT set, which a layer holds whole, so that OpenJPEG
        /// reads the output: the set of each code-block is first chosen for the last layer, as if it were the only one,
        /// then the layers share out the code-blocks, each with the coding passes of its set.
        /// </remarks>
        private void AllocateLayers(long headLength)
        {
            var blockCount = _blockCount;
            _layerPasses = Av1PooledArray<byte>.Rent(_scope, (long)blockCount * _layerCount, clear: true, AllocationKind.Temporary);
            _slopes = Av1PooledArray<double>.Rent(_scope, Math.Max(1, _passCount), clear: true, AllocationKind.Temporary);
            using var thresholds = Av1PooledArray<double>.Rent(_scope, Math.Max(1, _passCount), clear: false, AllocationKind.Temporary);
            var tiles = new TilePackets[_tileCount];
            try
            {
                for (var t = 0; t < _tileCount; t++)
                {
                    tiles[t] = CreateTilePackets(t);
                    tiles[t].Save();
                }

                using var scratch = new PooledPayloadWriter(_scope);
                var budget = new LayerBudget(headLength + (TileHeaderLength * (long)_totalTileParts) + 2 + (_writesPacketLengths ? PltSegmentOverhead * (long)_totalTileParts : 0), _writesPacketLengths);
                var sorted = GetThresholds(thresholds.Span);
                if (_isHighThroughput && _layerCount > 1)
                {
                    // The HT set of each code-block: what one layer would hold with the target of the last one, less a
                    // byte for each packet of the other layers (and its length in a PLT marker segment)
                    var target = _layerTargets[_layerCount - 1];
                    if (target != long.MaxValue)
                    {
                        target -= (_layerCount - 1) * _packetsPerLayer * (_writesPacketLengths ? 2 : 1);
                    }

                    ChooseLayer(tiles, layer: 0, target, budget, sorted, scratch);
                    _highThroughputSets = Av1PooledArray<byte>.Rent(_scope, Math.Max(1, blockCount), clear: false, AllocationKind.Temporary);
                    var sets = _highThroughputSets.Span;
                    var layerPasses = _layerPasses.Span;
                    for (var b = 0; b < blockCount; b++)
                    {
                        sets[b] = layerPasses[b * _layerCount];
                    }

                    // The slopes of the sets alone; the packets of the trials were measured from the saved state, which the
                    // layers start from
                    _slopes.Span.Clear();
                    sorted = GetThresholds(thresholds.Span);
                }

                for (var layer = 0; layer < _layerCount; layer++)
                {
                    ChooseLayer(tiles, layer, _layerTargets[layer], budget, sorted, scratch);

                    // The packets of the layer leave the state the next layer starts from
                    budget.Add(MeasureLayer(tiles, layer, threshold: null, scratch, out var entries), entries);
                    foreach (var tile in tiles)
                    {
                        tile.Save();
                    }
                }
            }
            finally
            {
                foreach (var tile in tiles)
                {
                    tile?.Dispose();
                }

                _slopes.Dispose();
                _slopes = null;
            }
        }

        /// <summary>Computes the slopes of the hull points and gets their distinct values, from the steepest.</summary>
        private Span<double> GetThresholds(Span<double> thresholds)
        {
            var slopes = _slopes!.Span;
            ComputeHulls(slopes);
            var count = 0;
            foreach (var slope in slopes[.._passCount])
            {
                if (slope > 0)
                {
                    thresholds[count++] = slope;
                }
            }

            var sorted = thresholds[..count];
            sorted.Sort((a, b) => b.CompareTo(a));
            count = sorted.Length == 0 ? 0 : 1;
            for (var i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] != sorted[count - 1])
                {
                    sorted[count++] = sorted[i];
                }
            }

            return sorted[..count];
        }

        /// <summary>Selects the coding passes of a layer: every pass left when they fit the target, otherwise the passes of the smallest threshold whose output fits it.</summary>
        private void ChooseLayer(TilePackets[] tiles, int layer, long target, LayerBudget budget, ReadOnlySpan<double> sorted, PooledPayloadWriter scratch)
        {
            // A layer that every coding pass left fits in holds them all, the passes off the hulls included
            if (target == long.MaxValue || budget.GetSize(MeasureLayer(tiles, layer, double.NegativeInfinity, scratch, out var entries), entries) <= target)
            {
                SelectPasses(layer, double.NegativeInfinity);
                return;
            }

            // The largest index of a threshold whose output fits, -1 when no threshold does: the layer then only holds the
            // passes that add no byte. The size grows with the index
            var low = -1;
            var high = sorted.Length - 1;
            if (high >= 0 && budget.GetSize(MeasureLayer(tiles, layer, sorted[high], scratch, out entries), entries) <= target)
            {
                low = high;
            }
            else
            {
                while (high - low > 1)
                {
                    var middle = low + ((high - low) / 2);
                    if (budget.GetSize(MeasureLayer(tiles, layer, sorted[middle], scratch, out entries), entries) <= target)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }
            }

            SelectPasses(layer, low < 0 ? double.PositiveInfinity : sorted[low]);
        }

        /// <summary>
        /// Computes the convex hull of the rate and distortion of the coding passes of each code-block: the slope of each
        /// pass on the hull (decreasing along a code-block), 0 for the others.
        /// </summary>
        /// <returns>The number of passes on the hulls.</returns>
        private int ComputeHulls(Span<double> slopes)
        {
            var passes = _passes is null ? default : _passes.Span;
            Span<int> hull = stackalloc int[MaxPasses + 1];
            Span<double> rates = stackalloc double[MaxPasses + 1];
            Span<double> distortions = stackalloc double[MaxPasses + 1];
            var total = 0;
            var records = _blocks.Span[.._blockCount];
            for (var b = 0; b < records.Length; b++)
            {
                ref readonly var record = ref records[b];
                if (record.BitPlanes == 0)
                    continue;

                var count = GetPassTotal(in record);
                var block = passes.Slice(record.FirstPass, count);
                if (_isHighThroughput)
                {
                    total += ComputeHighThroughputHull(block, slopes.Slice(record.FirstPass, count), _highThroughputSets is null ? 0 : _highThroughputSets.Span[b]);
                    continue;
                }

                var points = 1;
                hull[0] = 0;
                rates[0] = 0;
                distortions[0] = 0;
                var distortion = 0.0;
                for (var i = 0; i < count; i++)
                {
                    distortion += block[i].Distortion;
                    double rate = block[i].Length;
                    if (distortion <= distortions[points - 1])
                        continue;

                    // A point that the new one leaves below the line from the previous point is not on the hull
                    while (points > 1 && GetSlope(rates[points - 1], distortions[points - 1], rate, distortion) >= GetSlope(rates[points - 2], distortions[points - 2], rates[points - 1], distortions[points - 1]))
                    {
                        points--;
                    }

                    hull[points] = i + 1;
                    rates[points] = rate;
                    distortions[points] = distortion;
                    points++;
                }

                var destination = slopes.Slice(record.FirstPass, count);
                for (var k = 1; k < points; k++)
                {
                    destination[hull[k] - 1] = GetSlope(rates[k - 1], distortions[k - 1], rates[k], distortions[k]);
                }

                total += points - 1;
            }

            return total;
        }

        private static double GetSlope(double rate0, double distortion0, double rate1, double distortion1)
            => rate1 == rate0 ? double.PositiveInfinity : (distortion1 - distortion0) / (rate1 - rate0);

        /// <summary>
        /// Computes the convex hull of the truncation points of a code-block of the high-throughput block coder. A point is
        /// an HT set and the refinement passes it keeps: the points are not truncations of each other, and are ordered by
        /// their length. With <paramref name="set"/>, the code-block only has the point of its HT set.
        /// </summary>
        /// <param name="block">The coding passes of the code-block: the length of each point and what it adds to the reduction of the error of the previous one.</param>
        /// <param name="slopes">Receives the slope of each point on the hull, 0 for the others.</param>
        /// <param name="set">The number of coding passes of the HT set of the code-block, or 0 when any point can be chosen.</param>
        /// <returns>The number of points on the hull.</returns>
        private static int ComputeHighThroughputHull(ReadOnlySpan<Jpeg2000CodingPass> block, Span<double> slopes, int set)
        {
            Span<double> cumulative = stackalloc double[MaxPasses];
            var distortion = 0.0;
            for (var i = 0; i < block.Length; i++)
            {
                distortion += block[i].Distortion;
                cumulative[i] = distortion;
            }

            if (set != 0)
            {
                // The HT set the code-block keeps, against nothing
                if (cumulative[set - 1] <= 0)
                    return 0;

                slopes[set - 1] = GetSlope(0, 0, block[set - 1].Length, cumulative[set - 1]);
                return 1;
            }

            // The points by length, the one that reduces the error most first among those of the same length
            Span<int> order = stackalloc int[MaxPasses];
            for (var i = 0; i < block.Length; i++)
            {
                var j = i;
                for (; j > 0; j--)
                {
                    var previous = order[j - 1];
                    if (block[previous].Length < block[i].Length || (block[previous].Length == block[i].Length && cumulative[previous] >= cumulative[i]))
                        break;

                    order[j] = previous;
                }

                order[j] = i;
            }

            Span<int> hull = stackalloc int[MaxPasses + 1];
            Span<double> rates = stackalloc double[MaxPasses + 1];
            Span<double> distortions = stackalloc double[MaxPasses + 1];
            var points = 1;
            hull[0] = -1;
            rates[0] = 0;
            distortions[0] = 0;
            foreach (var i in order[..block.Length])
            {
                double rate = block[i].Length;
                var value = cumulative[i];
                if (value <= distortions[points - 1])
                    continue;

                while (points > 1 && GetSlope(rates[points - 1], distortions[points - 1], rate, value) >= GetSlope(rates[points - 2], distortions[points - 2], rates[points - 1], distortions[points - 1]))
                {
                    points--;
                }

                hull[points] = i;
                rates[points] = rate;
                distortions[points] = value;
                points++;
            }

            for (var k = 1; k < points; k++)
            {
                slopes[hull[k]] = GetSlope(rates[k - 1], distortions[k - 1], rates[k], distortions[k]);
            }

            return points - 1;
        }

        /// <summary>
        /// Sets the number of coding passes of every code-block up to a layer: the passes of the previous layers, and the
        /// hull points whose slope is at least <paramref name="threshold"/> (every pass for negative infinity).
        /// </summary>
        private void SelectPasses(int layer, double threshold)
        {
            var layerPasses = _layerPasses!.Span;
            var slopes = _slopes is null ? default : _slopes.Span;
            var passes = _passes is null ? default : _passes.Span;
            var sets = _highThroughputSets is null ? default : _highThroughputSets.Span;
            var records = _blocks.Span[.._blockCount];
            for (var b = 0; b < records.Length; b++)
            {
                ref readonly var record = ref records[b];
                var previous = layer == 0 ? 0 : layerPasses[(b * _layerCount) + layer - 1];
                var selected = previous;
                if (record.BitPlanes != 0)
                {
                    var count = GetPassTotal(in record);
                    var block = slopes.Slice(record.FirstPass, count);
                    if (!sets.IsEmpty)
                    {
                        // The HT set of the code-block, whole
                        var set = sets[b];
                        if (set != 0 && (double.IsNegativeInfinity(threshold) || (block[set - 1] >= threshold && block[set - 1] > 0)))
                        {
                            selected = set;
                        }
                    }
                    else if (double.IsNegativeInfinity(threshold))
                    {
                        selected = count;
                    }
                    else if (_isHighThroughput)
                    {
                        // The longest hull point whose slope is at least the threshold: the slopes decrease with the length
                        var length = -1;
                        for (var i = 0; i < count; i++)
                        {
                            if (block[i] >= threshold && block[i] > 0 && passes[record.FirstPass + i].Length > length)
                            {
                                length = passes[record.FirstPass + i].Length;
                                selected = i + 1;
                            }
                        }
                    }
                    else
                    {
                        for (var i = previous; i < count; i++)
                        {
                            if (block[i] >= threshold && block[i] > 0)
                            {
                                selected = i + 1;
                            }
                        }
                    }
                }

                layerPasses[(b * _layerCount) + layer] = (byte)selected;
            }
        }

        /// <summary>Measures the packets of a layer in every tile, from the state the previous layers left.</summary>
        /// <param name="tiles">The packet state of every tile.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="threshold">The threshold of the passes of the layer, or <see langword="null"/> when they are already selected.</param>
        /// <param name="scratch">A buffer for the packet headers.</param>
        /// <param name="entries">Receives the number of bytes the lengths of the packets take in PLT marker segments.</param>
        /// <returns>The number of bytes of the packets.</returns>
        private long MeasureLayer(TilePackets[] tiles, int layer, double? threshold, PooledPayloadWriter scratch, out long entries)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (threshold is { } value)
            {
                SelectPasses(layer, value);
            }

            var length = 0L;
            entries = 0;
            foreach (var tile in tiles)
            {
                tile.Restore();
                foreach (var component in tile.Geometry.Components)
                {
                    for (var r = 0; r <= _levels; r++)
                    {
                        var resolutionIndex = component.FirstResolution + r;
                        for (var precinct = 0; precinct < tile.Geometry.Resolutions[resolutionIndex].PrecinctCount; precinct++)
                        {
                            scratch.Reset();
                            var packet = WritePacket(tile, component.Index, resolutionIndex, precinct, layer, scratch, emit: false);
                            length += packet;
                            entries += GetPacketLengthBytes(packet);
                        }
                    }
                }
            }

            return length;
        }

        /// <summary>Computes the partition of a tile and the state of its packet headers before the first layer.</summary>
        private TilePackets CreateTilePackets(int tile)
        {
            var geometry = Jpeg2000TileGeometry.Create(_scope, _siz, _coding, tile);
            try
            {
                var packets = new TilePackets(_scope, geometry, _tileFirstBlock[tile]);
                var bandCount = (3 * _levels) + 1;
                var records = _blocks.Span;
                var values = packets.Values;
                foreach (var component in geometry.Components)
                {
                    var firstBand = geometry.Resolutions[component.FirstResolution].FirstBand;
                    for (var r = 0; r <= _levels; r++)
                    {
                        ref readonly var resolution = ref geometry.Resolutions[component.FirstResolution + r];
                        for (var p = 0; p < resolution.PrecinctCount * resolution.BandCount; p++)
                        {
                            ref readonly var precinctBand = ref geometry.PrecinctBands[resolution.FirstPrecinctBand + p];
                            if (precinctBand.BlocksWide == 0)
                                continue;

                            // The inclusion tree is not known yet; the other tree holds the number of bit-planes that are empty
                            ref readonly var band = ref geometry.Bands[resolution.FirstBand + (p % resolution.BandCount)];
                            var bandBitPlanes = _bandMagnitudeBitPlanes[(component.Index * bandCount) + (resolution.FirstBand + (p % resolution.BandCount) - firstBand)];
                            var nodes = precinctBand.TreeLength / 2;
                            values.Slice(precinctBand.InclusionTree / 2, nodes).Fill(int.MaxValue);
                            var zeroBitPlanes = values.Slice(precinctBand.ZeroBitPlaneTree / 2, nodes);
                            for (var by = 0; by < precinctBand.BlocksHigh; by++)
                            {
                                for (var bx = 0; bx < precinctBand.BlocksWide; bx++)
                                {
                                    var bitPlanes = records[packets.FirstBlock + band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx].BitPlanes;
                                    zeroBitPlanes[(by * precinctBand.BlocksWide) + bx] = bitPlanes != 0 ? bandBitPlanes - bitPlanes : int.MaxValue;
                                }
                            }

                            Jpeg2000TagTreeEncoder.ComputeMinima(zeroBitPlanes, precinctBand.BlocksWide, precinctBand.BlocksHigh);
                        }
                    }
                }

                return packets;
            }
            catch
            {
                geometry.Dispose();
                throw;
            }
        }

        /// <summary>Writes the tile-parts of every tile and lists their pieces, then the EOC marker.</summary>
        /// <returns>The length of the codestream after its main header.</returns>
        private long WriteTiles()
        {
            var length = 0L;
            _tilePartIndex = 0;
            for (var t = 0; t < _tileCount; t++)
            {
                using var tile = CreateTilePackets(t);
                using var order = RentPacketOrder(tile.Geometry, out var count);
                var packets = order.Span[..count];
                var components = tile.Geometry.Components;
                var parts = GetTileParts(t);
                var part = -1;
                var header = 0;
                var lengthPiece = -1;
                var partLength = 0L;
                for (var i = 0; i < packets.Length; i++)
                {
                    ref readonly var packet = ref packets[i];
                    if (i == 0 || StartsTilePart(in packets[i - 1], in packet))
                    {
                        if (part >= 0)
                        {
                            length += EndTilePart(header, lengthPiece, partLength, isLast: false);
                        }

                        part++;
                        BeginTilePart(t, part, parts, out header, out lengthPiece);
                        partLength = 0;
                    }

                    CancellationToken.ThrowIfCancellationRequested();
                    var packetLength = WritePacket(tile, packet.Component, components[packet.Component].FirstResolution + packet.Resolution, packet.Precinct, packet.Layer, _packetHeaders, emit: true);
                    partLength += packetLength;
                    if (_writesPacketLengths)
                    {
                        AddPacketLength(packetLength);
                    }
                }

                length += EndTilePart(header, lengthPiece, partLength, isLast: t == _tileCount - 1);
            }

            // The EOC marker ends the codestream
            AddPiece(SourcePacketHeaders, _packetHeaders.Length, 2);
            _packetHeaders.Write([0xFF, Jpeg2000Format.MarkerEoc & 0xFF]);
            return length + 2;
        }

        /// <summary>
        /// Writes the SOT marker segment of a tile-part (A.4.2), whose length is patched once its packets are written, and
        /// its SOD marker, and lists them; with PLT marker segments, a piece between them is kept for these segments.
        /// </summary>
        private void BeginTilePart(int tile, int part, int parts, out int header, out int lengthPiece)
        {
            header = _packetHeaders.Length;
            var boxes = new HeifBoxWriter(_packetHeaders);
            boxes.WriteUInt16(Jpeg2000Format.MarkerSot);
            boxes.WriteUInt16(Jpeg2000Format.SotSegmentLength - 2);
            boxes.WriteUInt16(tile);
            boxes.WriteUInt32(0);
            boxes.WriteByte((byte)part);
            boxes.WriteByte((byte)parts);
            boxes.WriteUInt16(Jpeg2000Format.MarkerSod);
            _packetLengthCount = 0;
            if (_writesPacketLengths)
            {
                AddPiece(SourcePacketHeaders, header, Jpeg2000Format.SotSegmentLength);
                lengthPiece = _pieceCount;
                AddPiece(SourcePacketHeaders, 0, 0);
                AddPiece(SourcePacketHeaders, header + Jpeg2000Format.SotSegmentLength, 2);
            }
            else
            {
                AddPiece(SourcePacketHeaders, header, TileHeaderLength);
                lengthPiece = -1;
            }
        }

        /// <summary>
        /// Ends a tile-part: writes its PLT marker segments (A.7.3), patches the length of the tile-part in its SOT marker
        /// segment and keeps it for the TLM marker segments. The length of the last tile-part of the codestream is written
        /// as 0 when it does not fit in 32 bits: the tile-part then extends to the EOC marker.
        /// </summary>
        /// <returns>The length of the tile-part.</returns>
        private long EndTilePart(int header, int lengthPiece, long packetsLength, bool isLast)
        {
            var tilePartLength = TileHeaderLength + packetsLength;
            if (lengthPiece >= 0)
            {
                var start = _packetHeaders.Length;
                WritePacketLengthSegments();
                ref var piece = ref _pieces!.Span[lengthPiece];
                piece.Offset = start;
                piece.Length = _packetHeaders.Length - start;
                tilePartLength += piece.Length;
            }

            if (tilePartLength > uint.MaxValue && (!isLast || _writesTilePartLengths))
                throw new InvalidOperationException("A JPEG 2000 tile-part is larger than 4 GiB: set a smaller TileWidth or TileHeight, or a TilePartDivision.");

            BinaryPrimitives.WriteUInt32BigEndian(_packetHeaders.WrittenSpan[(header + 6)..], tilePartLength > uint.MaxValue ? 0 : (uint)tilePartLength);
            if (_writesTilePartLengths)
            {
                _tilePartLengths ??= Av1PooledArray<long>.Rent(_scope, _totalTileParts, clear: true, AllocationKind.Temporary);
                _tilePartLengths.Span[_tilePartIndex] = tilePartLength;
            }

            _tilePartIndex++;
            return tilePartLength;
        }

        private void AddPacketLength(long length)
        {
            if (_packetLengths is null || _packetLengthCount == _packetLengths.Length)
            {
                var larger = Av1PooledArray<long>.Rent(_scope, _packetLengths is null ? 256 : checked(_packetLengths.Length * 2L), clear: false, AllocationKind.Temporary);
                _packetLengths?.Span.CopyTo(larger.Span);
                _packetLengths?.Dispose();
                _packetLengths = larger;
            }

            _packetLengths.Span[_packetLengthCount++] = length;
        }

        /// <summary>
        /// Writes the PLT marker segments of the packets of the current tile-part (A.7.3): each length in groups of seven
        /// bits from the most significant one, the high bit of a byte set when a group follows. A length is never split
        /// between two segments.
        /// </summary>
        private void WritePacketLengthSegments()
        {
            var lengths = _packetLengths is null ? default : _packetLengths.Span[.._packetLengthCount];
            var index = 0;
            var segment = 0;
            Span<byte> bytes = stackalloc byte[10];
            while (index < lengths.Length)
            {
                if (segment == MaxMarkerSegments)
                    throw new InvalidOperationException("A JPEG 2000 tile-part has too many packets for its PLT marker segments: set WritePacketLengths to false, or use a TilePartDivision.");

                var start = _packetHeaders.Length;
                var boxes = new HeifBoxWriter(_packetHeaders);
                boxes.WriteUInt16(Jpeg2000Format.MarkerPlt);
                boxes.WriteUInt16(0);
                boxes.WriteByte((byte)segment++);
                var data = 0;
                while (index < lengths.Length)
                {
                    var count = GetPacketLengthBytes(lengths[index]);
                    if (data + count > MaxPltData)
                        break;

                    var value = lengths[index++];
                    for (var i = count - 1; i >= 0; i--)
                    {
                        bytes[i] = (byte)((int)(value & 0x7F) | (i == count - 1 ? 0 : 0x80));
                        value >>= 7;
                    }

                    _packetHeaders.Write(bytes[..count]);
                    data += count;
                }

                BinaryPrimitives.WriteUInt16BigEndian(_packetHeaders.WrittenSpan[(start + 2)..], (ushort)(3 + data));
            }
        }

        /// <summary>Gets the number of bytes of the length of a packet in a PLT marker segment: one per group of seven bits.</summary>
        private static int GetPacketLengthBytes(long length) => Math.Max(1, (64 - BitOperations.LeadingZeroCount((ulong)length) + 6) / 7);

        /// <summary>
        /// Writes the header of a packet (B.10) and, when <paramref name="emit"/> is set, lists its pieces: the header, then
        /// the bytes each code-block adds in the layer. The state of the code-blocks and of the tag trees of the precinct
        /// moves to the end of the layer.
        /// </summary>
        /// <returns>The length of the packet: its header and its body.</returns>
        private long WritePacket(TilePackets tile, int component, int resolutionIndex, int precinct, int layer, PooledPayloadWriter headers, bool emit)
        {
            var geometry = tile.Geometry;
            ref readonly var resolution = ref geometry.Resolutions[resolutionIndex];
            var precinctBands = geometry.PrecinctBands;
            var blocks = geometry.Blocks;
            var values = tile.Values;
            var lows = tile.Lows;
            var known = tile.Known;
            var headerStart = headers.Length;
            var headerPiece = _pieceCount;
            if (emit)
            {
                AddPiece(SourcePacketHeaders, headerStart, 0);
            }

            // B.10.3: a packet that holds no coding pass is one 0 bit
            var isEmpty = true;
            for (var b = 0; b < resolution.BandCount && isEmpty; b++)
            {
                ref readonly var precinctBand = ref precinctBands[resolution.FirstPrecinctBand + (precinct * resolution.BandCount) + b];
                ref readonly var band = ref geometry.Bands[resolution.FirstBand + b];
                for (var by = 0; by < precinctBand.BlocksHigh && isEmpty; by++)
                {
                    for (var bx = 0; bx < precinctBand.BlocksWide; bx++)
                    {
                        var index = band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx;
                        if (GetPasses(tile.FirstBlock + index, layer) > blocks[index].PassCount)
                        {
                            isEmpty = false;
                            break;
                        }
                    }
                }
            }

            var writer = new Jpeg2000PacketBitWriter(headers);
            writer.WriteBit(isEmpty ? 0 : 1);
            var bodyLength = 0L;
            for (var b = 0; b < resolution.BandCount && !isEmpty; b++)
            {
                ref readonly var precinctBand = ref precinctBands[resolution.FirstPrecinctBand + (precinct * resolution.BandCount) + b];
                if (precinctBand.BlocksWide == 0)
                    continue;

                ref readonly var band = ref geometry.Bands[resolution.FirstBand + b];
                var wide = precinctBand.BlocksWide;
                var high = precinctBand.BlocksHigh;
                var nodes = precinctBand.TreeLength / 2;
                var inclusion = precinctBand.InclusionTree / 2;
                var zero = precinctBand.ZeroBitPlaneTree / 2;

                // The inclusion tree holds the first layer of each code-block: the earlier layers are known, the code-blocks
                // that first appear in this layer have its number, and the others a larger one
                var inclusionValues = values.Slice(inclusion, nodes);
                for (var by = 0; by < high; by++)
                {
                    for (var bx = 0; bx < wide; bx++)
                    {
                        var index = band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx;
                        if (blocks[index].Included == 0)
                        {
                            inclusionValues[(by * wide) + bx] = GetPasses(tile.FirstBlock + index, layer) != 0 ? layer : int.MaxValue;
                        }
                    }
                }

                Jpeg2000TagTreeEncoder.ComputeMinima(inclusionValues, wide, high);
                if (_isHighThroughput)
                {
                    // The empty bit-planes of a code-block of the high-throughput block coder give the lowest bit-plane of
                    // its HT cleanup pass, which the rate control chooses: with one layer, the passes of the layer; with
                    // several, the HT set of the code-block, which every layer gives alike
                    var sets = _highThroughputSets is null ? default : _highThroughputSets.Span;
                    var firstBand = geometry.Resolutions[geometry.Components[component].FirstResolution].FirstBand;
                    var bandBitPlanes = _bandMagnitudeBitPlanes[(component * ((3 * _levels) + 1)) + (resolution.FirstBand + b - firstBand)];
                    var zeroValues = values.Slice(zero, nodes);
                    for (var by = 0; by < high; by++)
                    {
                        for (var bx = 0; bx < wide; bx++)
                        {
                            var global = tile.FirstBlock + band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx;
                            var passes = sets.IsEmpty ? GetPasses(global, layer) : sets[global];
                            zeroValues[(by * wide) + bx] = passes != 0 ? GetHighThroughputZeroBitPlanes(global, passes, bandBitPlanes) : int.MaxValue;
                        }
                    }

                    Jpeg2000TagTreeEncoder.ComputeMinima(zeroValues, wide, high);
                }

                for (var by = 0; by < high; by++)
                {
                    for (var bx = 0; bx < wide; bx++)
                    {
                        var index = band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx;
                        ref var block = ref blocks[index];
                        var global = tile.FirstBlock + index;
                        var passes = GetPasses(global, layer);
                        int previous = block.PassCount;
                        if (block.Included != 0)
                        {
                            writer.WriteBit(passes > previous ? 1 : 0);
                        }
                        else
                        {
                            Jpeg2000TagTreeEncoder.Encode(ref writer, inclusionValues, lows.Slice(inclusion, nodes), known.Slice(inclusion, nodes), wide, high, bx, by, threshold: layer + 1);
                            if (passes == 0)
                                continue;

                            // B.10.5: the empty bit-planes, the first time the code-block is included
                            var zeroValues = values.Slice(zero, nodes);
                            Jpeg2000TagTreeEncoder.Encode(ref writer, zeroValues, lows.Slice(zero, nodes), known.Slice(zero, nodes), wide, high, bx, by, zeroValues[(by * wide) + bx] + 1);
                            block.Included = 1;
                            block.LengthBits = 3;
                        }

                        if (passes == previous)
                            continue;

                        int start;
                        int length;
                        if (_isHighThroughput)
                        {
                            // T.814, B.3: the HT set of a code-block is in one packet, so that the code-block is in one layer;
                            // the HT cleanup segment and the HT refinement segment have a length each
                            if (previous != 0)
                                throw new InvalidOperationException("An HT set of the JPEG 2000 encoder is split between layers.");

                            GetHighThroughputSegments(global, passes, out start, out var cleanup, out var refinement);
                            var refinementPasses = (passes - 1) % 3;
                            WritePassCount(ref writer, 1 + refinementPasses);
                            var refinementBits = refinementPasses == 0 ? 0 : BitOperations.Log2((uint)refinementPasses);
                            var growth = Math.Max(0, GetBitLength(cleanup) - block.LengthBits);
                            if (refinementPasses != 0)
                            {
                                growth = Math.Max(growth, GetBitLength(refinement) - block.LengthBits - refinementBits);
                            }

                            WriteLengthBits(ref writer, growth);
                            block.LengthBits = (byte)(block.LengthBits + growth);
                            writer.WriteBits(cleanup, block.LengthBits);
                            if (refinementPasses != 0)
                            {
                                writer.WriteBits(refinement, block.LengthBits + refinementBits);
                            }

                            length = cleanup + refinement;
                        }
                        else
                        {
                            var added = passes - previous;
                            start = GetSegmentLength(global, previous);
                            length = GetSegmentLength(global, passes) - start;
                            WritePassCount(ref writer, added);

                            // B.10.7.1: the length is written in Lblock bits plus those the number of passes adds; Lblock starts
                            // at 3 and grows by the number of 1 bits written before a 0 bit
                            var available = block.LengthBits + BitOperations.Log2((uint)added);
                            var growth = Math.Max(0, GetBitLength(length) - available);
                            WriteLengthBits(ref writer, growth);
                            writer.WriteBits(length, available + growth);
                            block.LengthBits = (byte)(block.LengthBits + growth);
                        }

                        block.PassCount = (ushort)passes;
                        if (emit)
                        {
                            AddPiece(SourceFirstComponent + component, _blocks.Span[global].Offset + start, length);
                        }

                        bodyLength += length;
                    }
                }
            }

            writer.Flush();
            var headerLength = headers.Length - headerStart;
            if (emit)
            {
                _pieces!.Span[headerPiece].Length = headerLength;
            }

            return headerLength + bodyLength;
        }

        private static int GetBitLength(int value) => 32 - BitOperations.LeadingZeroCount((uint)value);

        /// <summary>Writes the increment of Lblock (B.10.7.1): as many 1 bits, then a 0 bit.</summary>
        private static void WriteLengthBits(ref Jpeg2000PacketBitWriter writer, int growth)
        {
            for (var i = 0; i < growth; i++)
            {
                writer.WriteBit(1);
            }

            writer.WriteBit(0);
        }

        /// <summary>Writes the number of coding passes of a code-block in a packet (Table B.4).</summary>
        private static void WritePassCount(ref Jpeg2000PacketBitWriter writer, int passes)
        {
            switch (passes)
            {
                case 1:
                    writer.WriteBit(0);
                    break;

                case 2:
                    writer.WriteBits(0b10, 2);
                    break;

                case <= 5:
                    writer.WriteBits(0b11, 2);
                    writer.WriteBits(passes - 3, 2);
                    break;

                case <= 36:
                    writer.WriteBits(0b1111, 4);
                    writer.WriteBits(passes - 6, 5);
                    break;

                default:
                    writer.WriteBits(0b1_1111_1111, 9);
                    writer.WriteBits(passes - 37, 7);
                    break;
            }
        }

        /// <summary>Writes what precedes the tile-parts: the boxes of a JP2 file and the main header.</summary>
        /// <param name="reversibleExponents">The exponents of the reversible sub-bands.</param>
        /// <param name="guardBits">The guard bits of the irreversible sub-bands.</param>
        /// <param name="codestreamLength">The length of the codestream after the main header: the tile-parts and the EOC marker.</param>
        private void WriteHead(ReadOnlySpan<byte> reversibleExponents, int guardBits, long codestreamLength)
        {
            var head = _head;
            var boxes = new HeifBoxWriter(head);
            var isFile = _encoder.Container == Jpeg2000Container.Jp2;
            var codestreamBox = -1;
            var isLargeBox = false;
            if (isFile)
            {
                WriteFileBoxes(boxes);

                // The length of the codestream box is patched once the main header is written; a codestream of 4 GiB or
                // more needs the 64-bit length field
                isLargeBox = codestreamLength > uint.MaxValue - (1 << 20);
                codestreamBox = boxes.BeginBox(Jpeg2000Format.CodestreamBox);
                if (isLargeBox)
                {
                    boxes.WriteUInt64(0);
                }
            }

            var bandCount = (3 * _levels) + 1;
            var hasLossyAlpha = _isLossy && HasAlpha;
            boxes.WriteUInt16(Jpeg2000Format.MarkerSoc);

            // A.5.1: the image and the tiles, at the origin of the reference grid
            boxes.WriteUInt16(Jpeg2000Format.MarkerSiz);
            boxes.WriteUInt16(38 + (3 * _componentCount));
            boxes.WriteUInt16(_isHighThroughput ? CapabilityHighThroughput : 0);
            boxes.WriteUInt32((uint)_width);
            boxes.WriteUInt32((uint)_height);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32((uint)_tileWidth);
            boxes.WriteUInt32((uint)_tileHeight);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32(0);
            boxes.WriteUInt16(_componentCount);
            for (var c = 0; c < _componentCount; c++)
            {
                boxes.WriteByte((byte)(_precision - 1));
                boxes.WriteByte(1);
                boxes.WriteByte(1);
            }

            if (_isHighThroughput)
            {
                // A.5.2 and T.814, A.2: the capabilities of Part 15
                boxes.WriteUInt16(Jpeg2000Format.MarkerCap);
                boxes.WriteUInt16(8);
                boxes.WriteUInt32(CapabilityPart15);
                boxes.WriteUInt16(GetHighThroughputCapabilities());
            }

            // A.6.1: the precincts, no SOP or EPH marker, the progression order and the layers, code-blocks of 64 by 64
            // without style
            var precinctBytes = _precinctSizes?.Length ?? 0;
            boxes.WriteUInt16(Jpeg2000Format.MarkerCod);
            boxes.WriteUInt16(12 + precinctBytes);
            boxes.WriteByte((byte)(_precinctSizes is null ? 0 : 1));
            boxes.WriteByte((byte)_progression);
            boxes.WriteUInt16(_layerCount);
            boxes.WriteByte((byte)(_colorCount == 3 ? 1 : 0));
            WriteComponentStyle(boxes, isReversible: !_isLossy);
            if (hasLossyAlpha)
            {
                // A.6.2: the opacity keeps the reversible filter
                boxes.WriteUInt16(Jpeg2000Format.MarkerCoc);
                boxes.WriteUInt16(9 + precinctBytes);
                boxes.WriteByte((byte)_colorCount);
                boxes.WriteByte((byte)(_precinctSizes is null ? 0 : 1));
                WriteComponentStyle(boxes, isReversible: true);
            }

            // A.6.4
            boxes.WriteUInt16(Jpeg2000Format.MarkerQcd);
            if (_isLossy)
            {
                boxes.WriteUInt16(3 + (2 * bandCount));
                WriteStepSizes(boxes, guardBits, table: 0, bandCount);
            }
            else
            {
                boxes.WriteUInt16(3 + bandCount);
                WriteReversibleQuantization(boxes, reversibleExponents);
            }

            if (_isLossy && _colorCount == 3)
            {
                // A.6.5: the two chrominances have coarser step sizes than the luminance
                for (var c = 1; c <= 2; c++)
                {
                    boxes.WriteUInt16(Jpeg2000Format.MarkerQcc);
                    boxes.WriteUInt16(4 + (2 * bandCount));
                    boxes.WriteByte((byte)c);
                    WriteStepSizes(boxes, guardBits, GetStepTable(c), bandCount);
                }
            }

            if (hasLossyAlpha)
            {
                // A.6.5
                boxes.WriteUInt16(Jpeg2000Format.MarkerQcc);
                boxes.WriteUInt16(4 + bandCount);
                boxes.WriteByte((byte)_colorCount);
                WriteReversibleQuantization(boxes, reversibleExponents);
            }

            if (_writesTilePartLengths)
            {
                WriteTilePartLengthSegments(boxes);
            }

            if (isFile)
            {
                // The box holds the codestream to its EOC marker
                var boxLength = (long)(head.Length - codestreamBox) + codestreamLength;
                if (isLargeBox)
                {
                    boxes.PatchUInt32(codestreamBox, 1);
                    BinaryPrimitives.WriteUInt64BigEndian(head.WrittenSpan[(codestreamBox + 8)..], (ulong)boxLength);
                }
                else
                {
                    boxes.PatchUInt32(codestreamBox, (uint)boxLength);
                }
            }

            _pieces!.Span[0].Length = head.Length;
        }

        private void WriteComponentStyle(HeifBoxWriter boxes, bool isReversible)
        {
            boxes.WriteByte((byte)_levels);
            boxes.WriteByte(BlockExponent - 2);
            boxes.WriteByte(BlockExponent - 2);
            boxes.WriteByte((byte)BlockStyle);
            boxes.WriteByte((byte)(isReversible ? 1 : 0));
            if (_precinctSizes is not null)
            {
                boxes.WriteBytes(_precinctSizes);
            }
        }

        /// <summary>
        /// Writes the TLM marker segments (A.7.1): the index of the tile and the length of each tile-part, in the order of
        /// the codestream, on 32 bits. The lengths are 0 until the tile-parts are written.
        /// </summary>
        private void WriteTilePartLengthSegments(HeifBoxWriter boxes)
        {
            var indexLength = GetTlmTileIndexLength();
            var perSegment = MaxTlmData / (indexLength + 4);
            var lengths = _tilePartLengths is null ? default : _tilePartLengths.Span;
            var index = 0;
            var tile = 0;
            var part = 0;
            for (var segment = 0; index < _totalTileParts; segment++)
            {
                var count = Math.Min(perSegment, _totalTileParts - index);
                boxes.WriteUInt16(Jpeg2000Format.MarkerTlm);
                boxes.WriteUInt16(4 + (count * (indexLength + 4)));
                boxes.WriteByte((byte)segment);
                boxes.WriteByte((byte)((indexLength << 4) | 0x40));
                for (var i = 0; i < count; i++)
                {
                    while (part == GetTileParts(tile))
                    {
                        tile++;
                        part = 0;
                    }

                    if (indexLength == 1)
                    {
                        boxes.WriteByte((byte)tile);
                    }
                    else
                    {
                        boxes.WriteUInt16(tile);
                    }

                    boxes.WriteUInt32(lengths.IsEmpty ? 0 : (uint)lengths[index]);
                    index++;
                    part++;
                }
            }
        }

        private void WriteStepSizes(HeifBoxWriter boxes, int guardBits, int table, int bandCount)
        {
            boxes.WriteByte((byte)((guardBits << 5) | Jpeg2000Quantization.StyleExpounded));
            for (var band = 0; band < bandCount; band++)
            {
                var index = (table * bandCount) + band;
                boxes.WriteUInt16((_bandExponents[index] << 11) | _bandMantissas[index]);
            }
        }

        private static void WriteReversibleQuantization(HeifBoxWriter boxes, ReadOnlySpan<byte> exponents)
        {
            boxes.WriteByte((LosslessGuardBits << 5) | Jpeg2000Quantization.StyleNone);
            foreach (var exponent in exponents)
            {
                boxes.WriteByte((byte)(exponent << 3));
            }
        }

        /// <summary>Writes the boxes of a JP2 file that precede the codestream box (T.800, Annex I).</summary>
        private void WriteFileBoxes(HeifBoxWriter boxes)
        {
            var metadata = Options.Metadata;
            boxes.WriteBytes(Jpeg2000Format.FileSignature);

            // I.5.2: the brand, the minor version, and the brand again as the only compatible one; a codestream of the
            // high-throughput block coder is in a JPH file (T.814, Annex D)
            var brand = _isHighThroughput ? Jpeg2000Format.BrandJph : Jpeg2000Format.BrandJp2;
            var box = boxes.BeginBox(Jpeg2000Format.FileTypeBox);
            boxes.WriteUInt32(brand);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32(brand);
            boxes.EndBox(box);

            var header = boxes.BeginBox(Jpeg2000Format.HeaderBox);

            // I.5.3.1: every component has the same depth; the compression type is always 7
            box = boxes.BeginBox(Jpeg2000Format.ImageHeaderBox);
            boxes.WriteUInt32((uint)_height);
            boxes.WriteUInt32((uint)_width);
            boxes.WriteUInt16(_componentCount);
            boxes.WriteByte((byte)(_precision - 1));
            boxes.WriteByte(7);
            boxes.WriteByte(0);
            boxes.WriteByte(0);
            boxes.EndBox(box);

            // I.5.3.3: the profile of the image, or else the enumerated color space of untagged pixels
            box = boxes.BeginBox(Jpeg2000Format.ColorSpecificationBox);
            if (metadata.IccProfile is { } profile)
            {
                boxes.WriteByte(Jpeg2000ColorSpecification.MethodRestrictedIcc);
                boxes.WriteByte(0);
                boxes.WriteByte(0);
                boxes.WriteBytes(profile.Data.Span);
            }
            else
            {
                boxes.WriteByte(Jpeg2000ColorSpecification.MethodEnumerated);
                boxes.WriteByte(0);
                boxes.WriteByte(0);
                boxes.WriteUInt32(_colorCount == 1 ? Jpeg2000ColorSpecification.EnumeratedGray : Jpeg2000ColorSpecification.EnumeratedSrgb);
            }

            boxes.EndBox(box);
            if (HasAlpha)
            {
                // I.5.3.6: the colors in order, then the opacity of the whole image
                box = boxes.BeginBox(Jpeg2000Format.ChannelDefinitionBox);
                boxes.WriteUInt16(_componentCount);
                for (var c = 0; c < _colorCount; c++)
                {
                    boxes.WriteUInt16(c);
                    boxes.WriteUInt16(Jpeg2000ChannelDefinition.TypeColor);
                    boxes.WriteUInt16(c + 1);
                }

                boxes.WriteUInt16(_colorCount);
                boxes.WriteUInt16(Jpeg2000ChannelDefinition.TypeOpacity);
                boxes.WriteUInt16(Jpeg2000ChannelDefinition.AssociationImage);
                boxes.EndBox(box);
            }

            if (metadata.Resolution is { } resolution)
            {
                // I.5.3.7: the capture resolution, vertical then horizontal
                var (horizontal, vertical) = ResolutionConversion.ToJp2Resolution(resolution);
                var superbox = boxes.BeginBox(Jpeg2000Format.ResolutionBox);
                box = boxes.BeginBox(Jpeg2000Format.CaptureResolutionBox);
                boxes.WriteUInt16(vertical.Numerator);
                boxes.WriteUInt16(vertical.Denominator);
                boxes.WriteUInt16(horizontal.Numerator);
                boxes.WriteUInt16(horizontal.Denominator);
                boxes.WriteByte((byte)vertical.Exponent);
                boxes.WriteByte((byte)horizontal.Exponent);
                boxes.EndBox(box);
                boxes.EndBox(superbox);
            }

            boxes.EndBox(header);
            if (metadata.XmpProfile is { } xmp)
            {
                box = boxes.BeginBox(Jpeg2000Format.UuidBox);
                boxes.WriteBytes(Jpeg2000Format.XmpUuid);
                boxes.WriteBytes(xmp.Data.Span);
                boxes.EndBox(box);
            }

            if (metadata.Exif is { } exif)
            {
                box = boxes.BeginBox(Jpeg2000Format.UuidBox);
                boxes.WriteBytes(Jpeg2000Format.ExifUuid);
                boxes.WriteBytes(exif);
                boxes.EndBox(box);
            }
        }

        private bool Emit(ImageOutputBuffer output)
        {
            var pieces = _pieces!.Span[.._pieceCount];
            var budget = BytesPerCall;
            while (_piece < pieces.Length)
            {
                ref readonly var piece = ref pieces[_piece];
                var remaining = piece.Length - _pieceOffset;
                if (remaining == 0)
                {
                    _piece++;
                    _pieceOffset = 0;
                    continue;
                }

                if (budget == 0)
                    return false;

                var source = piece.Source switch
                {
                    SourceHead => _head,
                    SourcePacketHeaders => _packetHeaders,
                    _ => _blockData[piece.Source - SourceFirstComponent],
                };

                var count = Math.Min(remaining, budget);
                output.Write(source.WrittenSpan.Slice(piece.Offset + _pieceOffset, count));
                _pieceOffset += count;
                budget -= count;
            }

            return true;
        }

        private void AddPiece(int source, int offset, int length)
        {
            if (_pieces is null || _pieceCount == _pieces.Length)
            {
                var larger = Av1PooledArray<Piece>.Rent(_scope, _pieces is null ? 256 : checked(_pieces.Length * 2L), clear: false, AllocationKind.Temporary);
                if (_pieces is not null)
                {
                    _pieces.Span.CopyTo(larger.Span);
                    _pieces.Dispose();
                }

                _pieces = larger;
            }

            _pieces.Span[_pieceCount++] = new Piece { Source = source, Offset = offset, Length = length };
        }

        /// <summary>
        /// The state of a worker that codes code-blocks: its block coder, the coefficients of its code-block, the scratch of
        /// its share of the wavelet transformation, and the codeword segments it coded in the current batch.
        /// </summary>
        private sealed class BlockCoder : IDisposable
        {
            private readonly bool _ownsCoder;

            /// <summary>Initializes a worker that uses the coders, the coefficients and the scratch of the session.</summary>
            public BlockCoder(AllocationScope scope, Jpeg2000Tier1Encoder encoder, Jpeg2000HtBlockEncoder? highThroughputEncoder, Av1PooledArray<int> samples, Av1PooledArray<int> scratch)
                : this(encoder, highThroughputEncoder, samples, scratch, new PooledPayloadWriter(scope), ownsCoder: false)
            {
            }

            private BlockCoder(Jpeg2000Tier1Encoder encoder, Jpeg2000HtBlockEncoder? highThroughputEncoder, Av1PooledArray<int> samples, Av1PooledArray<int> scratch, PooledPayloadWriter output, bool ownsCoder)
            {
                Encoder = encoder;
                HighThroughputEncoder = highThroughputEncoder;
                Samples = samples;
                Scratch = scratch;
                Output = output;
                _ownsCoder = ownsCoder;
            }

            public Jpeg2000Tier1Encoder Encoder { get; }

            /// <summary>Gets the coder of the high-throughput block coder; <see langword="null"/> when the output does not use it.</summary>
            public Jpeg2000HtBlockEncoder? HighThroughputEncoder { get; }

            public Av1PooledArray<int> Samples { get; }

            public Av1PooledArray<int> Scratch { get; }

            public PooledPayloadWriter Output { get; }

            /// <summary>Creates a worker with coders, coefficients and a scratch of its own.</summary>
            public static BlockCoder Create(AllocationScope scope, long scratchLength, bool isHighThroughput, bool codesRefinementPasses)
            {
                Jpeg2000Tier1Encoder? encoder = null;
                Jpeg2000HtBlockEncoder? highThroughputEncoder = null;
                Av1PooledArray<int>? samples = null;
                Av1PooledArray<int>? scratch = null;
                try
                {
                    encoder = new Jpeg2000Tier1Encoder(scope);
                    if (isHighThroughput)
                    {
                        highThroughputEncoder = new Jpeg2000HtBlockEncoder(scope);
                        if (codesRefinementPasses)
                        {
                            highThroughputEncoder.RentRefinementBuffers();
                        }
                    }

                    samples = Av1PooledArray<int>.Rent(scope, Jpeg2000Tier1Tables.MaxSamples, clear: false, AllocationKind.Temporary);
                    scratch = Av1PooledArray<int>.Rent(scope, scratchLength, clear: false, AllocationKind.Temporary);
                    return new BlockCoder(encoder, highThroughputEncoder, samples, scratch, new PooledPayloadWriter(scope), ownsCoder: true);
                }
                catch
                {
                    encoder?.Dispose();
                    highThroughputEncoder?.Dispose();
                    samples?.Dispose();
                    scratch?.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                Output.Dispose();
                if (_ownsCoder)
                {
                    Encoder.Dispose();
                    HighThroughputEncoder?.Dispose();
                    Samples.Dispose();
                    Scratch.Dispose();
                }
            }
        }

        /// <summary>What the block coder produced for one code-block.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct EncodedBlock
        {
            /// <summary>The offset of the codeword segment in the coded bytes of the component.</summary>
            public int Offset;

            /// <summary>The length of the codeword segment.</summary>
            public int Length;

            /// <summary>With quality layers, the index of the first coding pass of the code-block among the passes of the image.</summary>
            public int FirstPass;

            /// <summary>The number of coded bit-planes; 0 when the code-block is not in the codestream.</summary>
            public byte BitPlanes;
        }

        /// <summary>A packet of a tile: the layer of the code-blocks of a precinct of a resolution level of a component.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PacketOrder
        {
            public int Component;

            /// <summary>The resolution level, from the lowest one of the component.</summary>
            public int Resolution;

            public int Precinct;

            public int Layer;
        }

        /// <summary>A run of bytes of the output, in one of the buffers of the session.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Piece
        {
            public int Source;
            public int Offset;
            public int Length;
        }

        /// <summary>
        /// The size of the output up to a layer, for the rate control: the fixed part (the head, the tile-part headers, the
        /// EOC marker and, with PLT marker segments, one segment per tile-part), the packets of the previous layers and,
        /// with PLT marker segments, their lengths and a bound of the segments they add.
        /// </summary>
        private sealed class LayerBudget(long fixedLength, bool countsPacketLengths)
        {
            private long _packets;
            private long _entries;

            /// <summary>Gets the size of the output with the packets of one more layer.</summary>
            public long GetSize(long packets, long entries)
            {
                var size = fixedLength + _packets + packets;
                if (countsPacketLengths)
                {
                    // A segment of a tile-part that is not its last one holds more than its largest data less one length
                    var total = _entries + entries;
                    size += total + (PltSegmentOverhead * ((total + MaxPltData - 5) / (MaxPltData - 4)));
                }

                return size;
            }

            public void Add(long packets, long entries)
            {
                _packets += packets;
                _entries += entries;
            }
        }

        /// <summary>
        /// The partition of a tile and the state of its packet headers between two layers: the tag trees of every precinct
        /// (the value of each node, the lower bound written so far and whether the value is written) and, in the code-blocks
        /// of the partition, their coding passes, Lblock and inclusion so far. A copy of the state can be saved and
        /// restored, so that the packets of a layer can be measured several times.
        /// </summary>
        private sealed class TilePackets : IDisposable
        {
            private readonly AllocationScope _scope;
            private readonly Av1PooledArray<int> _values;
            private readonly Av1PooledArray<int> _lows;
            private readonly Av1PooledArray<int> _known;
            private Av1PooledArray<int>? _saved;
            private Av1PooledArray<Jpeg2000CodeBlock>? _savedBlocks;

            public TilePackets(AllocationScope scope, Jpeg2000TileGeometry geometry, int firstBlock)
            {
                _scope = scope;
                Geometry = geometry;
                FirstBlock = firstBlock;
                var nodes = geometry.TagNodes.Length / 2;
                Av1PooledArray<int>? values = null;
                Av1PooledArray<int>? lows = null;
                try
                {
                    values = Av1PooledArray<int>.Rent(scope, nodes, clear: true, AllocationKind.Temporary);
                    lows = Av1PooledArray<int>.Rent(scope, nodes, clear: true, AllocationKind.Temporary);
                    _known = Av1PooledArray<int>.Rent(scope, nodes, clear: true, AllocationKind.Temporary);
                }
                catch
                {
                    values?.Dispose();
                    lows?.Dispose();
                    throw;
                }

                _values = values;
                _lows = lows;
            }

            public Jpeg2000TileGeometry Geometry { get; }

            /// <summary>Gets the index of the first code-block of the tile among the code-blocks of the image.</summary>
            public int FirstBlock { get; }

            public Span<int> Values => _values.Span;

            public Span<int> Lows => _lows.Span;

            public Span<int> Known => _known.Span;

            public void Save()
            {
                var nodes = _values.Length;
                _saved ??= Av1PooledArray<int>.Rent(_scope, 3 * nodes, clear: false, AllocationKind.Temporary);
                _savedBlocks ??= Av1PooledArray<Jpeg2000CodeBlock>.Rent(_scope, Geometry.Blocks.Length, clear: false, AllocationKind.Temporary);
                var saved = _saved.Span;
                _values.Span.CopyTo(saved);
                _lows.Span.CopyTo(saved[nodes..]);
                _known.Span.CopyTo(saved[(2 * nodes)..]);
                Geometry.Blocks.CopyTo(_savedBlocks.Span);
            }

            public void Restore()
            {
                var nodes = _values.Length;
                var saved = _saved!.Span;
                saved[..nodes].CopyTo(_values.Span);
                saved.Slice(nodes, nodes).CopyTo(_lows.Span);
                saved.Slice(2 * nodes, nodes).CopyTo(_known.Span);
                _savedBlocks!.Span[..Geometry.Blocks.Length].CopyTo(Geometry.Blocks);
            }

            public void Dispose()
            {
                _values.Dispose();
                _lows.Dispose();
                _known.Dispose();
                _saved?.Dispose();
                _savedBlocks?.Dispose();
                Geometry.Dispose();
            }
        }
    }
}
