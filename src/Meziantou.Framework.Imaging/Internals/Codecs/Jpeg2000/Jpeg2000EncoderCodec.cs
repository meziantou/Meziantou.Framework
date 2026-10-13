using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// The encoding state of one JPEG 2000 output (T.800). The image is one tile; each component is level-shifted (G.1),
    /// goes through the forward component transformation (Annex G) and the forward wavelet transformation (Annex F), is
    /// quantized (Annex E) and its code-blocks are coded (Annexes C and D) into one layer; then the packets are written in
    /// the layer-resolution level-component-position order (Annex B).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// One component is transformed and coded at a time: the private state is one plane of 32-bit values, the coded bytes
    /// of every code-block and the partition of the tile (shared with the decoder, <see cref="Jpeg2000TileGeometry"/>).
    /// </description></item>
    /// <item><description>
    /// Nothing is written before every code-block is coded: the lengths of the codestream box and of the tile-part are then
    /// known, and the output needs no seek. The output is then emitted in pieces of at most 64 KiB.
    /// </description></item>
    /// <item><description>
    /// Lossless: the reversible filter and the reversible component transformation; no quantization. The exponents of the
    /// QCD marker segment are the nominal ones (the depth of the samples plus the gain of the sub-band), raised for a
    /// sub-band whose coefficients need more bit-planes.
    /// </description></item>
    /// <item><description>
    /// Lossy: the irreversible filter and component transformation; a dead-zone quantizer whose step sizes come from
    /// <see cref="Jpeg2000QuantizationDesign"/>; every bit-plane of the quantized coefficients is coded. The opacity
    /// component keeps the reversible filter, through COC and QCC marker segments.
    /// </description></item>
    /// <item><description>
    /// The output only depends on the pixels and on the settings: integers on the reversible path, single-precision
    /// additions, products and one truncation on the irreversible one, no parallelism, no date and no version string.
    /// </description></item>
    /// </list>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const int BlockExponent = 6;
        private const int BlocksPerCall = 64;
        private const int BytesPerCall = 64 * 1024;
        private const int LosslessGuardBits = 2;
        private const int SourceHead = 0;
        private const int SourcePacketHeaders = 1;
        private const int SourceFirstComponent = 2;

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
        private readonly Jpeg2000Siz _siz;
        private readonly Jpeg2000TileCoding _coding;
        private readonly Jpeg2000TileGeometry _geometry;
        private readonly Jpeg2000Tier1Encoder _blockEncoder;
        private readonly PooledPayloadWriter[] _blockData;
        private readonly PooledPayloadWriter _head;
        private readonly PooledPayloadWriter _packetHeaders;
        private readonly Av1PooledArray<EncodedBlock> _blocks;
        private readonly Av1PooledArray<int> _plane;
        private readonly Av1PooledArray<int> _scratch;
        private readonly Av1PooledArray<int> _blockSamples;
        private readonly int[] _bandBitPlanes;
        private readonly float[] _bandSteps;
        private readonly int[] _bandExponents;
        private readonly int[] _bandMantissas;
        private Av1PooledArray<Piece>? _pieces;
        private int _pieceCount;
        private ImageFrame? _frame;
        private Step _step;
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

            // As many decomposition levels as the smaller side allows, so that the lowest resolution level is not empty
            while (_levels < Jpeg2000QuantizationDesign.MaxLevels && Math.Min(_width, _height) >> (_levels + 1) > 0)
            {
                _levels++;
            }

            var components = new Jpeg2000ComponentSize[_componentCount];
            components.AsSpan().Fill(new Jpeg2000ComponentSize(_precision, IsSigned: false, 1, 1));
            _siz = new Jpeg2000Siz(capabilities: 0, _width, _height, 0, 0, _width, _height, 0, 0, components);

            // The partition of the tile does not depend on the quantization: provisional values stand for it
            var bandCount = (3 * _levels) + 1;
            _bandBitPlanes = new int[_componentCount * bandCount];
            _bandSteps = new float[bandCount];
            _bandExponents = new int[bandCount];
            _bandMantissas = new int[bandCount];
            _coding = CreateCoding(bandCount);
            _geometry = Jpeg2000TileGeometry.Create(_scope, _siz, _coding, tileIndex: 0);
            Jpeg2000Tier1Encoder? blockEncoder = null;
            PooledPayloadWriter? head = null;
            PooledPayloadWriter? packetHeaders = null;
            Av1PooledArray<EncodedBlock>? blocks = null;
            Av1PooledArray<int>? plane = null;
            Av1PooledArray<int>? scratch = null;
            Av1PooledArray<int>? blockSamples = null;
            var blockData = new PooledPayloadWriter[_componentCount];
            try
            {
                blockEncoder = new Jpeg2000Tier1Encoder(_scope);
                head = new PooledPayloadWriter(_scope);
                packetHeaders = new PooledPayloadWriter(_scope);
                for (var c = 0; c < _componentCount; c++)
                {
                    blockData[c] = new PooledPayloadWriter(_scope);
                }

                blocks = Av1PooledArray<EncodedBlock>.Rent(_scope, _geometry.Blocks.Length, clear: true, AllocationKind.Temporary);
                plane = Av1PooledArray<int>.Rent(_scope, (long)_width * _height, clear: false, AllocationKind.Temporary);
                scratch = Av1PooledArray<int>.Rent(_scope, Jpeg2000Dwt.GetScratchLength(_width, _height), clear: false, AllocationKind.Temporary);
                blockSamples = Av1PooledArray<int>.Rent(_scope, Jpeg2000Tier1Tables.MaxSamples, clear: false, AllocationKind.Temporary);
            }
            catch
            {
                _geometry.Dispose();
                blockEncoder?.Dispose();
                head?.Dispose();
                packetHeaders?.Dispose();
                blocks?.Dispose();
                plane?.Dispose();
                scratch?.Dispose();
                blockSamples?.Dispose();
                foreach (var writer in blockData)
                {
                    writer?.Dispose();
                }

                throw;
            }

            _blockEncoder = blockEncoder;
            _head = head;
            _packetHeaders = packetHeaders;
            _blockData = blockData;
            _blocks = blocks;
            _plane = plane;
            _scratch = scratch;
            _blockSamples = blockSamples;
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

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("JPEG 2000 output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A JPEG 2000 image stores a single frame.");

            _frame = frame;
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
                    TransformComponent();
                    _band = _geometry.Resolutions[_geometry.Components[_component].FirstResolution].FirstBand;
                    _block = 0;
                    _step = Step.Blocks;
                    return false;

                case Step.Blocks:
                    if (EncodeBlocks())
                    {
                        _component++;
                        _step = _component < _componentCount ? Step.Transform : Step.Assemble;
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
                _head.Dispose();
                _packetHeaders.Dispose();
                _blocks.Dispose();
                _plane.Dispose();
                _scratch.Dispose();
                _blockSamples.Dispose();
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

        private Jpeg2000TileCoding CreateCoding(int bandCount)
        {
            // The step sizes of the lossy sub-bands: the base step of the quality divided by the norm of each sub-band
            var baseStep = Jpeg2000QuantizationDesign.GetBaseStep(_encoder.Quality, _precision);
            for (var band = 0; band < bandCount; band++)
            {
                var resolution = band == 0 ? 0 : ((band - 1) / 3) + 1;
                var orientation = band == 0 ? -1 : (band - 1) % 3;
                var level = band == 0 ? _levels : _levels - resolution + 1;
                var norm = Jpeg2000QuantizationDesign.GetNorm(level, isHighHorizontally: orientation is 0 or 2, isHighVertically: orientation is 1 or 2);
                var range = _precision + GetGain(band);
                Jpeg2000QuantizationDesign.GetStepParameters(baseStep / norm, range, out _bandExponents[band], out _bandMantissas[band]);
                _bandSteps[band] = Jpeg2000QuantizationDesign.GetStep(_bandExponents[band], _bandMantissas[band], range);
            }

            var headers = new Jpeg2000HeaderSet(_componentCount);
            var colorStyle = new Jpeg2000CodingStyle(_levels, BlockExponent, BlockExponent, codeBlockStyle: 0, isReversible: !_isLossy, precinctSizes: null);
            headers.CodingDefaults = new Jpeg2000CodingDefaults(mayUseSop: false, usesEph: false, Jpeg2000CodingDefaults.ProgressionLrcp, layers: 1, usesComponentTransform: _colorCount == 3, colorStyle);
            var none = new Jpeg2000Quantization(Jpeg2000Quantization.StyleNone, LosslessGuardBits, new byte[bandCount], new ushort[bandCount]);
            headers.Quantization = _isLossy ? new Jpeg2000Quantization(Jpeg2000Quantization.StyleExpounded, 1, new byte[bandCount], new ushort[bandCount]) : none;
            if (_isLossy && HasAlpha)
            {
                headers.TrySetComponentStyle(_colorCount, new Jpeg2000CodingStyle(_levels, BlockExponent, BlockExponent, codeBlockStyle: 0, isReversible: true, precinctSizes: null));
                headers.TrySetComponentQuantization(_colorCount, none);
            }

            return Jpeg2000TileCoding.Resolve(_siz, headers, tile: null, capabilities: null);
        }

        /// <summary>Fills the plane with one component of the frame and decomposes it into its sub-bands.</summary>
        private void TransformComponent()
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var component = _component;
            var width = _width;
            var plane = _plane.Span[..(width * _height)];
            using (var lease = frame.GetStorage().AcquireLease())
            {
                for (var y = 0; y < _height; y++)
                {
                    if ((y & 63) == 0)
                    {
                        CancellationToken.ThrowIfCancellationRequested();
                    }

                    ReadComponentRow(lease.GetRowBytes(y), component, plane.Slice(y * width, width));
                }
            }

            var resolutions = _geometry.Resolutions.AsSpan(_geometry.Components[component].FirstResolution, _levels + 1);
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

            var floats = unsafe(MemoryMarshal.Cast<int, float>(row));
            var deep = _precision == 16 ? unsafe(MemoryMarshal.Cast<byte, ushort>(pixels)) : default;
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_pixelFormat);
            var redIndex = _pixelFormat == PixelFormat.Bgra32 ? 2 : 0;
            var blueIndex = 2 - redIndex;
            for (var x = 0; x < width; x++)
            {
                int red;
                int green;
                int blue;
                if (_precision == 8)
                {
                    var pixel = pixels.Slice(x * bytesPerPixel, 3);
                    red = pixel[redIndex] - offset;
                    green = pixel[1] - offset;
                    blue = pixel[blueIndex] - offset;
                }
                else
                {
                    red = deep[4 * x] - offset;
                    green = deep[(4 * x) + 1] - offset;
                    blue = deep[(4 * x) + 2] - offset;
                }

                if (!_isLossy)
                {
                    // Equation G-1: the reversible component transformation
                    row[x] = component switch
                    {
                        0 => (red + (2 * green) + blue) >> 2,
                        1 => blue - green,
                        _ => red - green,
                    };
                }
                else
                {
                    // Equation G-3: the irreversible component transformation
                    floats[x] = component switch
                    {
                        0 => ((0.299f * red) + (0.587f * green)) + (0.114f * blue),
                        1 => ((-0.16875f * red) - (0.33126f * green)) + (0.5f * blue),
                        _ => ((0.5f * red) - (0.41869f * green)) - (0.08131f * blue),
                    };
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
            var isReversible = IsReversible(component);
            var stride = _width;
            var plane = _plane.Span;
            var coefficients = unsafe(MemoryMarshal.Cast<int, float>(plane));
            var samples = _blockSamples.Span;
            var records = _blocks.Span;
            var output = _blockData[component];
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
                ref readonly var block = ref _geometry.Blocks[index];
                int width = block.Width;
                int height = block.Height;
                var origin = ((band.PlaneY + block.Y) * stride) + band.PlaneX + block.X;
                if (isReversible)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var source = plane.Slice(origin + (y * stride), width);
                        var destination = samples.Slice(y * width, width);
                        for (var x = 0; x < width; x++)
                        {
                            var value = source[x];
                            destination[x] = value < 0 ? -value | int.MinValue : value;
                        }
                    }
                }
                else
                {
                    // Equation E-1: the magnitude is divided by the step size and truncated
                    var step = _bandSteps[_band - firstBand];
                    for (var y = 0; y < height; y++)
                    {
                        var source = coefficients.Slice(origin + (y * stride), width);
                        var destination = samples.Slice(y * width, width);
                        for (var x = 0; x < width; x++)
                        {
                            var value = source[x];
                            var magnitude = (int)(MathF.Abs(value) / step);
                            destination[x] = value < 0 && magnitude != 0 ? magnitude | int.MinValue : magnitude;
                        }
                    }
                }

                ref var record = ref records[index];
                record.Offset = output.Length;
                record.BitPlanes = (byte)_blockEncoder.Encode(samples, width, height, band.Orientation, output, out record.Length);
                ref var maximum = ref _bandBitPlanes[(component * bandCount) + (_band - firstBand)];
                maximum = Math.Max(maximum, record.BitPlanes);
            }

            return true;
        }

        /// <summary>Chooses the quantization parameters, then writes the headers and the packet headers and lists the pieces of the output.</summary>
        private void Assemble()
        {
            var bandCount = (3 * _levels) + 1;

            // The number of bit-planes of a sub-band is its exponent plus the guard bits, less one (equation E-2): the
            // exponents of the reversible sub-bands and the guard bits of the others are raised as the coefficients need
            Span<byte> reversibleExponents = stackalloc byte[bandCount];
            var guardBits = 1;
            for (var band = 0; band < bandCount; band++)
            {
                var needed = 0;
                for (var c = 0; c < _componentCount; c++)
                {
                    var bitPlanes = _bandBitPlanes[(c * bandCount) + band];
                    if (IsReversible(c))
                    {
                        needed = Math.Max(needed, bitPlanes);
                    }
                    else
                    {
                        guardBits = Math.Max(guardBits, bitPlanes - _bandExponents[band] + 1);
                    }
                }

                reversibleExponents[band] = (byte)Math.Max(_precision + GetGain(band), needed + 1 - LosslessGuardBits);
            }

            if (guardBits > 7)
                throw new InvalidOperationException("The quantized coefficients need more than seven guard bits.");

            var packetsLength = WritePackets(reversibleExponents, guardBits);
            WriteHead(reversibleExponents, guardBits, packetsLength);
        }

        /// <summary>Writes the packet headers (B.10) and lists the pieces of the packets, in the progression order.</summary>
        /// <returns>The length of the packets.</returns>
        private long WritePackets(ReadOnlySpan<byte> reversibleExponents, int guardBits)
        {
            var records = _blocks.Span;
            var precinctBands = _geometry.PrecinctBands;
            using var inclusionTree = new Jpeg2000TagTreeEncoder(_scope);
            using var zeroBitPlaneTree = new Jpeg2000TagTreeEncoder(_scope);

            // The first piece is the head of the output, which needs the length of the packets
            AddPiece(SourceHead, 0, 0);
            var packetsLength = 0L;
            for (var r = 0; r <= _levels; r++)
            {
                for (var c = 0; c < _componentCount; c++)
                {
                    ref readonly var resolution = ref _geometry.Resolutions[_geometry.Components[c].FirstResolution + r];
                    var firstBand = _geometry.Resolutions[_geometry.Components[c].FirstResolution].FirstBand;
                    for (var precinct = 0; precinct < resolution.PrecinctCount; precinct++)
                    {
                        CancellationToken.ThrowIfCancellationRequested();
                        var headerStart = _packetHeaders.Length;
                        var headerPiece = _pieceCount;
                        AddPiece(SourcePacketHeaders, headerStart, 0);
                        var writer = new Jpeg2000PacketBitWriter(_packetHeaders);

                        // B.10.3: a packet that holds no coding pass is one 0 bit
                        var isEmpty = IsEmptyPacket(in resolution, precinct);
                        writer.WriteBit(isEmpty ? 0 : 1);
                        for (var b = 0; b < resolution.BandCount && !isEmpty; b++)
                        {
                            ref readonly var precinctBand = ref precinctBands[resolution.FirstPrecinctBand + (precinct * resolution.BandCount) + b];
                            if (precinctBand.BlocksWide == 0)
                                continue;

                            ref readonly var band = ref _geometry.Bands[resolution.FirstBand + b];
                            var bandIndex = resolution.FirstBand + b - firstBand;
                            var bandBitPlanes = IsReversible(c) ? reversibleExponents[bandIndex] + LosslessGuardBits - 1 : _bandExponents[bandIndex] + guardBits - 1;
                            var wide = precinctBand.BlocksWide;
                            var high = precinctBand.BlocksHigh;

                            // The inclusion tree holds the layer a code-block first appears in: the only layer, or none; the
                            // other tree holds the number of bit-planes that are empty
                            var inclusion = inclusionTree.Begin(wide, high);
                            var zeroBitPlanes = zeroBitPlaneTree.Begin(wide, high);
                            for (var by = 0; by < high; by++)
                            {
                                for (var bx = 0; bx < wide; bx++)
                                {
                                    var bitPlanes = records[band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx].BitPlanes;
                                    inclusion[(by * wide) + bx] = bitPlanes != 0 ? 0 : 1;
                                    zeroBitPlanes[(by * wide) + bx] = bitPlanes != 0 ? bandBitPlanes - bitPlanes : int.MaxValue;
                                }
                            }

                            inclusionTree.Complete();
                            zeroBitPlaneTree.Complete();
                            for (var by = 0; by < high; by++)
                            {
                                for (var bx = 0; bx < wide; bx++)
                                {
                                    ref readonly var record = ref records[band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx];
                                    inclusionTree.Encode(ref writer, bx, by, threshold: 1);
                                    if (record.BitPlanes == 0)
                                        continue;

                                    zeroBitPlaneTree.Encode(ref writer, bx, by, bandBitPlanes - record.BitPlanes + 1);

                                    // Every bit-plane has three coding passes, but the first one, which has one (D.3)
                                    var passes = (3 * record.BitPlanes) - 2;
                                    WritePassCount(ref writer, passes);

                                    // B.10.7.1: the length is written in Lblock bits plus those the number of passes adds;
                                    // Lblock starts at 3 and grows by the number of 1 bits written before a 0 bit
                                    var available = 3 + BitOperations.Log2((uint)passes);
                                    var needed = 32 - BitOperations.LeadingZeroCount((uint)record.Length);
                                    var growth = Math.Max(0, needed - available);
                                    for (var i = 0; i < growth; i++)
                                    {
                                        writer.WriteBit(1);
                                    }

                                    writer.WriteBit(0);
                                    writer.WriteBits(record.Length, available + growth);
                                    AddPiece(SourceFirstComponent + c, record.Offset, record.Length);
                                    packetsLength += record.Length;
                                }
                            }
                        }

                        writer.Flush();
                        var headerLength = _packetHeaders.Length - headerStart;
                        _pieces!.Span[headerPiece].Length = headerLength;
                        packetsLength += headerLength;
                    }
                }
            }

            // The EOC marker ends the codestream
            AddPiece(SourcePacketHeaders, _packetHeaders.Length, 2);
            _packetHeaders.Write([0xFF, Jpeg2000Format.MarkerEoc & 0xFF]);
            return packetsLength;
        }

        private bool IsEmptyPacket(in Jpeg2000ResolutionGeometry resolution, int precinct)
        {
            var records = _blocks.Span;
            var precinctBands = _geometry.PrecinctBands;
            for (var b = 0; b < resolution.BandCount; b++)
            {
                ref readonly var precinctBand = ref precinctBands[resolution.FirstPrecinctBand + (precinct * resolution.BandCount) + b];
                ref readonly var band = ref _geometry.Bands[resolution.FirstBand + b];
                for (var by = 0; by < precinctBand.BlocksHigh; by++)
                {
                    for (var bx = 0; bx < precinctBand.BlocksWide; bx++)
                    {
                        if (records[band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx].BitPlanes != 0)
                            return false;
                    }
                }
            }

            return true;
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

        /// <summary>Writes what precedes the packets: the boxes of a JP2 file, the main header and the header of the tile-part.</summary>
        private void WriteHead(ReadOnlySpan<byte> reversibleExponents, int guardBits, long packetsLength)
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
                isLargeBox = packetsLength > uint.MaxValue - (1 << 20);
                codestreamBox = boxes.BeginBox(Jpeg2000Format.CodestreamBox);
                if (isLargeBox)
                {
                    boxes.WriteUInt64(0);
                }
            }

            var bandCount = (3 * _levels) + 1;
            var hasLossyAlpha = _isLossy && HasAlpha;
            boxes.WriteUInt16(Jpeg2000Format.MarkerSoc);

            // A.5.1: one tile that covers the image, at the origin of the reference grid
            boxes.WriteUInt16(Jpeg2000Format.MarkerSiz);
            boxes.WriteUInt16(38 + (3 * _componentCount));
            boxes.WriteUInt16(0);
            boxes.WriteUInt32((uint)_width);
            boxes.WriteUInt32((uint)_height);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32((uint)_width);
            boxes.WriteUInt32((uint)_height);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32(0);
            boxes.WriteUInt16(_componentCount);
            for (var c = 0; c < _componentCount; c++)
            {
                boxes.WriteByte((byte)(_precision - 1));
                boxes.WriteByte(1);
                boxes.WriteByte(1);
            }

            // A.6.1: no precinct, no SOP or EPH marker, one layer in the LRCP order, code-blocks of 64 by 64 without style
            boxes.WriteUInt16(Jpeg2000Format.MarkerCod);
            boxes.WriteUInt16(12);
            boxes.WriteByte(0);
            boxes.WriteByte(Jpeg2000CodingDefaults.ProgressionLrcp);
            boxes.WriteUInt16(1);
            boxes.WriteByte((byte)(_colorCount == 3 ? 1 : 0));
            WriteComponentStyle(boxes, isReversible: !_isLossy);
            if (hasLossyAlpha)
            {
                // A.6.2: the opacity keeps the reversible filter
                boxes.WriteUInt16(Jpeg2000Format.MarkerCoc);
                boxes.WriteUInt16(9);
                boxes.WriteByte((byte)_colorCount);
                boxes.WriteByte(0);
                WriteComponentStyle(boxes, isReversible: true);
            }

            // A.6.4
            boxes.WriteUInt16(Jpeg2000Format.MarkerQcd);
            if (_isLossy)
            {
                boxes.WriteUInt16(3 + (2 * bandCount));
                boxes.WriteByte((byte)((guardBits << 5) | Jpeg2000Quantization.StyleExpounded));
                for (var band = 0; band < bandCount; band++)
                {
                    boxes.WriteUInt16((_bandExponents[band] << 11) | _bandMantissas[band]);
                }
            }
            else
            {
                boxes.WriteUInt16(3 + bandCount);
                WriteReversibleQuantization(boxes, reversibleExponents);
            }

            if (hasLossyAlpha)
            {
                // A.6.5
                boxes.WriteUInt16(Jpeg2000Format.MarkerQcc);
                boxes.WriteUInt16(4 + bandCount);
                boxes.WriteByte((byte)_colorCount);
                WriteReversibleQuantization(boxes, reversibleExponents);
            }

            // A.4.2: the only tile-part of the only tile; a length that does not fit in 32 bits is written as 0, which
            // means that the tile-part extends to the EOC marker
            var tilePartLength = Jpeg2000Format.SotSegmentLength + 2 + packetsLength;
            boxes.WriteUInt16(Jpeg2000Format.MarkerSot);
            boxes.WriteUInt16(Jpeg2000Format.SotSegmentLength - 2);
            boxes.WriteUInt16(0);
            boxes.WriteUInt32(tilePartLength > uint.MaxValue ? 0 : (uint)tilePartLength);
            boxes.WriteByte(0);
            boxes.WriteByte(1);
            boxes.WriteUInt16(Jpeg2000Format.MarkerSod);
            if (isFile)
            {
                // The box holds the codestream to its EOC marker
                var boxLength = (long)(head.Length - codestreamBox) + packetsLength + 2;
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
            boxes.WriteByte(0);
            boxes.WriteByte((byte)(isReversible ? 1 : 0));
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

            // I.5.2: the brand, the minor version, and the brand again as the only compatible one
            var box = boxes.BeginBox(Jpeg2000Format.FileTypeBox);
            boxes.WriteUInt32(Jpeg2000Format.BrandJp2);
            boxes.WriteUInt32(0);
            boxes.WriteUInt32(Jpeg2000Format.BrandJp2);
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

        /// <summary>What the block coder produced for one code-block.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct EncodedBlock
        {
            /// <summary>The offset of the codeword segment in the coded bytes of the component.</summary>
            public int Offset;

            /// <summary>The length of the codeword segment.</summary>
            public int Length;

            /// <summary>The number of coded bit-planes; 0 when the code-block is not in the codestream.</summary>
            public byte BitPlanes;
        }

        /// <summary>A run of bytes of the output, in one of the buffers of the session.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Piece
        {
            public int Source;
            public int Offset;
            public int Length;
        }
    }
}
