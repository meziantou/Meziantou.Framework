using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG encoder registration: 8-bit Huffman JPEG, baseline sequential (SOF0) or progressive (SOF2), grayscale or
/// YCbCr, written by one streaming session.
/// </summary>
internal sealed class JpegEncoderCodec : ImageEncoderCodec
{
    /// <summary>The worst-case size of one entropy-coded block: DC (at most 11 + 11 bits) and 63 AC coefficients (at most 16 + 10 bits each), doubled for byte stuffing.</summary>
    internal const int MaxBlockBytes = 2 * ((22 + (63 * 26) + 7) / 8);

    public static JpegEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Jpeg;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>Gets the luma sampling factors of an output (chroma components are always 1x1), or (1, 1) for grayscale output.</summary>
    /// <param name="pixelFormat">The pixel format of the frames.</param>
    /// <param name="subsampling">The encoder setting.</param>
    /// <returns>Whether the output has a single (grayscale) component, and the horizontal and vertical luma factors.</returns>
    internal static (bool Grayscale, int H, int V) GetSampling(PixelFormat pixelFormat, JpegChromaSubsampling subsampling)
    {
        // The component layout follows the pixel format, never the data: gray formats are one component (no chroma to
        // subsample, whatever the setting); color formats are YCbCr, Auto meaning 4:2:0
        if (PixelFormats.IsGrayscale(pixelFormat))
            return (true, 1, 1);

        return subsampling switch
        {
            JpegChromaSubsampling.Ratio444 => (false, 1, 1),
            JpegChromaSubsampling.Ratio422 => (false, 2, 1),
            _ => (false, 2, 2),
        };
    }

    /// <summary>
    /// The encoding state of one JPEG. The frame operation writes SOI and the JFIF APP0
    /// segment, then one metadata segment per <see cref="Encode"/> call (<see cref="JpegMetadataWriter"/>), the tables (DQT,
    /// SOF0, DHT) and the scan header, then the entropy-coded MCUs in bounded steps: each call loads the source rows of an
    /// MCU row (converted and level-shifted into single-precision Y, Cb and Cr rows, the canvas padded to whole MCUs by edge
    /// replication) and codes MCUs until the output buffer should be flushed. Completion writes EOI. Settings, the
    /// bit-depth policy, metadata sizes and the working buffers fail on creation, before any output; non-opaque pixels
    /// without a background fail in <see cref="ValidateFrame"/>, before anything of the frame is written.
    /// </summary>
    /// <remarks>
    /// A progressive JPEG (<see cref="JpegEncodingProcess.Progressive"/>) has the same quantized coefficients, written in
    /// several scans. After DQT and SOF2, each call transforms one MCU row into the coefficients of the whole image
    /// (<see cref="JpegCoefficientPlane"/>, allocated on creation); the source rows are not needed afterwards. Each scan of
    /// <see cref="JpegProgressiveScanEncoder.GetScript"/> is then coded twice from the coefficients: once to count its
    /// symbols, one row of blocks per call, then, after the DHT segment of the tables built from the counts and the scan
    /// header, to write it in bounded steps.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const byte MarkerSoi = 0xD8;
        private const byte MarkerEoi = 0xD9;
        private const byte MarkerSof0 = 0xC0;
        private const byte MarkerSof2 = 0xC2;
        private const byte MarkerDht = 0xC4;
        private const byte MarkerDqt = 0xDB;
        private const byte MarkerSos = 0xDA;

        private readonly JpegEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _workFormat;
        private readonly Rgba64? _background;
        private readonly bool _grayscale;
        private readonly int _h;
        private readonly int _v;
        private readonly int _width;
        private readonly int _height;
        private readonly int _mcusX;
        private readonly int _mcusY;
        private readonly int _planeWidth;
        private readonly ushort[] _luminanceQuantization = new ushort[64];
        private readonly ushort[] _chrominanceQuantization = new ushort[64];
        private readonly JpegHuffmanEncoder _luminanceDc;
        private readonly JpegHuffmanEncoder _luminanceAc;
        private readonly JpegHuffmanEncoder? _chrominanceDc;
        private readonly JpegHuffmanEncoder? _chrominanceAc;
        private readonly JpegMetadataWriter _metadata;
        private readonly double[] _scratch = new double[64];
        private readonly float[] _block = new float[64];
        private readonly int[] _coefficients = new int[64];
        private readonly int[] _predictions = new int[3];
        private readonly JpegProgressiveScanEncoder? _scanEncoder;
        private JpegCoefficientPlane?[] _coefficientPlanes = [];
        private PooledBuffer? _row;
        private PooledBuffer? _planeY;
        private PooledBuffer? _planeCb;
        private PooledBuffer? _planeCr;
        private ImageFrame? _frame;
        private Step _step;
        private int _metadataIndex;
        private int _mcuRow;
        private int _mcuX;
        private bool _rowLoaded;
        private bool _frameWritten;
        private JpegBitWriter _bits;
        private int _scanIndex;
        private ScanPass _scanPass;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (JpegEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !_encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"JPEG output stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set JpegEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Jpeg, "Bit depth reduction");

            (_grayscale, _h, _v) = GetSampling(_sourceFormat, _encoder.ChromaSubsampling);
            _workFormat = _grayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24;
            _background = PixelFormats.HasAlpha(_sourceFormat) ? _encoder.BackgroundColor : null;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _mcusX = (_width + (8 * _h) - 1) / (8 * _h);
            _mcusY = (_height + (8 * _v) - 1) / (8 * _v);
            _planeWidth = _mcusX * 8 * _h;

            JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.LuminanceQuantization, _encoder.Quality, _luminanceQuantization);
            JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.ChrominanceQuantization, _encoder.Quality, _chrominanceQuantization);
            _luminanceDc = new JpegHuffmanEncoder(JpegEncodingTables.LuminanceDcBits, JpegEncodingTables.LuminanceDcValues);
            _luminanceAc = new JpegHuffmanEncoder(JpegEncodingTables.LuminanceAcBits, JpegEncodingTables.LuminanceAcValues);
            if (!_grayscale)
            {
                _chrominanceDc = new JpegHuffmanEncoder(JpegEncodingTables.ChrominanceDcBits, JpegEncodingTables.ChrominanceDcValues);
                _chrominanceAc = new JpegHuffmanEncoder(JpegEncodingTables.ChrominanceAcBits, JpegEncodingTables.ChrominanceAcValues);
            }

            _metadata = new JpegMetadataWriter(options.Metadata);

            // One MCU row of samples per component, at full resolution (chroma is box-filtered when blocks are extracted)
            var planeBytes = (long)_planeWidth * 8 * _v * sizeof(float);
            var rowBytes = (long)_width * PixelFormats.GetBytesPerPixel(_workFormat);
            if (planeBytes > CheckedSizes.MaxBufferLength || rowBytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Scope.Limits);

            var scope = options.Scope;
            try
            {
                if (_sourceFormat != _workFormat)
                {
                    _row = scope.Rent((int)rowBytes, AllocationKind.Temporary, clear: false);
                }

                _planeY = scope.Rent((int)planeBytes, AllocationKind.Temporary, clear: false);
                if (!_grayscale)
                {
                    _planeCb = scope.Rent((int)planeBytes, AllocationKind.Temporary, clear: false);
                    _planeCr = scope.Rent((int)planeBytes, AllocationKind.Temporary, clear: false);
                }

                if (_encoder.Process == JpegEncodingProcess.Progressive)
                {
                    // The grid of a scan of one component alone only has the blocks that hold samples of the image
                    _scanEncoder = new JpegProgressiveScanEncoder();
                    _coefficientPlanes = new JpegCoefficientPlane?[_grayscale ? 1 : 3];
                    _coefficientPlanes[0] = new JpegCoefficientPlane(scope, _mcusX * _h, _mcusY * _v, (_width + 7) / 8, (_height + 7) / 8);
                    for (var c = 1; c < _coefficientPlanes.Length; c++)
                    {
                        _coefficientPlanes[c] = new JpegCoefficientPlane(scope, _mcusX, _mcusY, (((_width + _h - 1) / _h) + 7) / 8, (((_height + _v - 1) / _v) + 7) / 8);
                    }
                }
            }
            catch
            {
                ReleaseBuffers();
                throw;
            }
        }

        private enum Step
        {
            None,
            Header,
            Metadata,
            Tables,
            Scan,
            Transform,
            ProgressiveScans,
            Trailer,
        }

        /// <summary>The passes over the blocks of a progressive scan.</summary>
        private enum ScanPass
        {
            /// <summary>The scan is not started.</summary>
            None,

            /// <summary>The symbols are counted.</summary>
            Count,

            /// <summary>The tables and the scan header are written.</summary>
            Header,

            /// <summary>The entropy-coded data is written.</summary>
            Write,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // Alpha is never discarded silently: without a background every pixel must be opaque.
            // The scan runs before anything of the frame is written, so a rejection leaves the writer usable.
            if (!PixelFormats.HasAlpha(_sourceFormat) || _background is not null)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
                if (x >= 0)
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"JPEG output has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Set JpegEncoder.BackgroundColor to composite the pixels over an opaque color, or flatten the image explicitly."), ImageFormat.Jpeg, "Alpha removal");
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("JPEG output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0 || _frameWritten)
                throw new InvalidOperationException("JPEG output stores a single frame.");

            _frame = frame;
            _frameWritten = true;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount)
        {
            if (frameCount != 1 || _step != Step.None)
                throw new InvalidOperationException("JPEG output stores exactly one frame.");

            _step = Step.Trailer;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    WriteMarker(output, MarkerSoi);
                    JpegMetadataWriter.WriteJfif(output, Options.Metadata);
                    _step = Step.Metadata;
                    return false;

                case Step.Metadata:
                    if (_metadataIndex < _metadata.Count)
                    {
                        _metadata.Write(_metadataIndex++, output);
                        return false;
                    }

                    _step = Step.Tables;
                    goto case Step.Tables;

                case Step.Tables:
                    WriteQuantizationTables(output);
                    if (_scanEncoder is not null)
                    {
                        WriteFrameHeader(output, MarkerSof2);
                        _step = Step.Transform;
                        return false;
                    }

                    WriteFrameHeader(output, MarkerSof0);
                    WriteHuffmanTables(output);
                    WriteScanHeader(output, firstComponent: 0, _grayscale ? 1 : 3, dcTables: true, acTables: true, spectralStart: 0, spectralEnd: 63, approximation: 0);
                    _step = Step.Scan;
                    return false;

                case Step.Transform:
                    TransformMcuRow();
                    CancellationToken.ThrowIfCancellationRequested();
                    if (++_mcuRow < _mcusY)
                        return false;

                    // Every scan is coded from the coefficients: the frame and the sample rows are not needed any more
                    _frame = null;
                    ReleaseSampleBuffers();
                    _mcuRow = 0;
                    _step = Step.ProgressiveScans;
                    return false;

                case Step.ProgressiveScans:
                    if (!EncodeProgressiveScans(output))
                        return false;

                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                case Step.Scan:
                    if (!EncodeScan(output))
                        return false;

                    // The frame is no longer referenced once its operation completes; the buffers are not needed any more
                    _frame = null;
                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                case Step.Trailer:
                    WriteMarker(output, MarkerEoi);
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No JPEG encoding operation is in progress.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            _frame = null;
            ReleaseBuffers();
            base.Dispose(disposing);
        }

        private static void WriteMarker(ImageOutputBuffer output, byte marker) => output.Write([0xFF, marker]);

        private static void WriteHuffmanTable(Span<byte> destination, ref int offset, int tableClass, int identifier, JpegHuffmanEncoder table)
        {
            destination[offset++] = (byte)((tableClass << 4) | identifier);
            table.Bits.Span.CopyTo(destination[offset..]);
            offset += 16;
            table.Values.Span.CopyTo(destination[offset..]);
            offset += table.Values.Length;
        }

        private void ReleaseBuffers()
        {
            ReleaseSampleBuffers();
            foreach (var plane in _coefficientPlanes)
            {
                plane?.Dispose();
            }

            _coefficientPlanes = [];
        }

        private void ReleaseSampleBuffers()
        {
            _row?.Dispose();
            _row = null;
            _planeY?.Dispose();
            _planeY = null;
            _planeCb?.Dispose();
            _planeCb = null;
            _planeCr?.Dispose();
            _planeCr = null;
        }

        private void WriteQuantizationTables(ImageOutputBuffer output)
        {
            // 8-bit precision (Pq = 0): the quality scaling clamps every value to 1..255; values in zig-zag order
            var count = _grayscale ? 1 : 2;
            Span<byte> payload = stackalloc byte[65 * 2];
            var zigZag = JpegIdct.ZigZag;
            for (var t = 0; t < count; t++)
            {
                var table = t == 0 ? _luminanceQuantization : _chrominanceQuantization;
                payload[t * 65] = (byte)t;
                for (var k = 0; k < 64; k++)
                {
                    payload[(t * 65) + 1 + k] = (byte)table[zigZag[k]];
                }
            }

            JpegMetadataWriter.WriteSegment(output, MarkerDqt, payload[..(65 * count)]);
        }

        private void WriteFrameHeader(ImageOutputBuffer output, byte marker)
        {
            // Precision 8, height, width, components (identifiers 1, 2, 3 as JFIF requires; chroma 1x1, tables 1)
            var count = _grayscale ? 1 : 3;
            Span<byte> payload = stackalloc byte[6 + (3 * 3)];
            payload[0] = 8;
            BinaryPrimitives.WriteUInt16BigEndian(payload[1..], (ushort)_height);
            BinaryPrimitives.WriteUInt16BigEndian(payload[3..], (ushort)_width);
            payload[5] = (byte)count;
            for (var c = 0; c < count; c++)
            {
                payload[6 + (3 * c)] = (byte)(c + 1);
                payload[7 + (3 * c)] = c == 0 ? (byte)((_h << 4) | _v) : (byte)0x11;
                payload[8 + (3 * c)] = c == 0 ? (byte)0 : (byte)1;
            }

            JpegMetadataWriter.WriteSegment(output, marker, payload[..(6 + (3 * count))]);
        }

        private void WriteHuffmanTables(ImageOutputBuffer output)
        {
            Span<byte> payload = stackalloc byte[4 * (17 + 256)];
            var offset = 0;
            WriteHuffmanTable(payload, ref offset, tableClass: 0, identifier: 0, _luminanceDc);
            WriteHuffmanTable(payload, ref offset, tableClass: 1, identifier: 0, _luminanceAc);
            if (!_grayscale)
            {
                WriteHuffmanTable(payload, ref offset, tableClass: 0, identifier: 1, _chrominanceDc!);
                WriteHuffmanTable(payload, ref offset, tableClass: 1, identifier: 1, _chrominanceAc!);
            }

            JpegMetadataWriter.WriteSegment(output, MarkerDht, payload[..offset]);
        }

        /// <summary>Writes the header of a scan of consecutive components (T.81 B.2.3).</summary>
        /// <param name="output">The output.</param>
        /// <param name="firstComponent">The index of the first component of the scan.</param>
        /// <param name="count">The number of components of the scan.</param>
        /// <param name="dcTables">Whether the scan uses DC tables (0 for the luminance, 1 for the chrominance).</param>
        /// <param name="acTables">Whether the scan uses AC tables (0 for the luminance, 1 for the chrominance).</param>
        /// <param name="spectralStart">Ss: 0 for a baseline scan.</param>
        /// <param name="spectralEnd">Se: 63 for a baseline scan.</param>
        /// <param name="approximation">Ah and Al: 0 for a baseline scan.</param>
        private static void WriteScanHeader(ImageOutputBuffer output, int firstComponent, int count, bool dcTables, bool acTables, int spectralStart, int spectralEnd, int approximation)
        {
            Span<byte> payload = stackalloc byte[1 + (2 * 3) + 3];
            payload[0] = (byte)count;
            for (var i = 0; i < count; i++)
            {
                var component = firstComponent + i;
                var table = JpegProgressiveScanEncoder.GetTableIndex(component);
                payload[1 + (2 * i)] = (byte)(component + 1);
                payload[2 + (2 * i)] = (byte)((dcTables ? table << 4 : 0) | (acTables ? table : 0));
            }

            var tail = 1 + (2 * count);
            payload[tail] = (byte)spectralStart;
            payload[tail + 1] = (byte)spectralEnd;
            payload[tail + 2] = (byte)approximation;
            JpegMetadataWriter.WriteSegment(output, MarkerSos, payload[..(tail + 3)]);
        }

        /// <summary>Transforms the blocks of the next MCU row into the coefficients of the image.</summary>
        private void TransformMcuRow()
        {
            var frame = _frame ?? throw new InvalidOperationException("No JPEG frame is being written.");
            using (var lease = frame.GetStorage().AcquireLease())
            {
                LoadMcuRow(in lease, _mcuRow);
            }

            var planeY = unsafe(MemoryMarshal.Cast<byte, float>(_planeY!.Span));
            var luminance = _coefficientPlanes[0]!;
            for (var mcuX = 0; mcuX < _mcusX; mcuX++)
            {
                var x0 = mcuX * 8 * _h;
                for (var by = 0; by < _v; by++)
                {
                    for (var bx = 0; bx < _h; bx++)
                    {
                        ExtractBlock(planeY, x0 + (bx * 8), by * 8, factorX: 1, factorY: 1);
                        StoreBlock(_luminanceQuantization, luminance.GetBlock((_mcuRow * _v) + by, (mcuX * _h) + bx));
                    }
                }

                if (!_grayscale)
                {
                    ExtractBlock(unsafe(MemoryMarshal.Cast<byte, float>(_planeCb!.Span)), x0, 0, _h, _v);
                    StoreBlock(_chrominanceQuantization, _coefficientPlanes[1]!.GetBlock(_mcuRow, mcuX));
                    ExtractBlock(unsafe(MemoryMarshal.Cast<byte, float>(_planeCr!.Span)), x0, 0, _h, _v);
                    StoreBlock(_chrominanceQuantization, _coefficientPlanes[2]!.GetBlock(_mcuRow, mcuX));
                }
            }
        }

        private void StoreBlock(ReadOnlySpan<ushort> quantization, Span<short> destination)
        {
            var coefficients = _coefficients.AsSpan();
            JpegForwardDct.TransformAndQuantize(_block, quantization, _scratch, coefficients);
            for (var k = 0; k < 64; k++)
            {
                destination[k] = (short)coefficients[k];
            }
        }

        /// <returns><see langword="true"/> when the last scan is written.</returns>
        private bool EncodeProgressiveScans(ImageOutputBuffer output)
        {
            var scanEncoder = _scanEncoder!;
            var scan = JpegProgressiveScanEncoder.GetScript(_grayscale)[_scanIndex];

            // A DC refinement scan has no Huffman-coded symbol: nothing to count and no table
            var hasTables = scan.Ss > 0 || scan.Ah == 0;
            if (_scanPass == ScanPass.None)
            {
                _scanPass = hasTables ? ScanPass.Count : ScanPass.Header;
                scanEncoder.BeginScan(scan, counting: hasTables);
            }

            var interleaved = scan.ComponentCount > 1;
            var rows = interleaved ? _mcusY : _coefficientPlanes[scan.FirstComponent]!.ScanBlockRows;
            var columns = interleaved ? _mcusX : _coefficientPlanes[scan.FirstComponent]!.ScanBlocksPerLine;
            var position = 0;
            switch (_scanPass)
            {
                case ScanPass.Count:
                    for (var x = 0; x < columns; x++)
                    {
                        CodeProgressiveMcu(scan, _mcuRow, x, default, ref position);
                    }

                    if (++_mcuRow < rows)
                        return false;

                    scanEncoder.EndScan(default, ref position);
                    _mcuRow = 0;
                    _scanPass = ScanPass.Header;
                    return false;

                case ScanPass.Header:
                    if (hasTables)
                    {
                        WriteProgressiveTables(output, scan);
                    }

                    WriteScanHeader(output, scan.FirstComponent, scan.ComponentCount, dcTables: scan.Ss == 0 && scan.Ah == 0, acTables: scan.Ss > 0, scan.Ss, scan.Se, (scan.Ah << 4) | scan.Al);
                    scanEncoder.BeginScan(scan, counting: false);
                    _scanPass = ScanPass.Write;
                    return false;
            }

            var blocksPerMcu = interleaved ? (_h * _v) + 2 : 1;
            var coded = 0;
            while (_mcuRow < rows)
            {
                while (_mcuX < columns)
                {
                    // Bounded output per call: stop once the buffer should be flushed (at least one MCU per call)
                    if (coded > 0 && output.ShouldFlush)
                        return false;

                    var span = output.GetSpan(blocksPerMcu * JpegProgressiveScanEncoder.MaxBlockBytes);
                    position = 0;
                    CodeProgressiveMcu(scan, _mcuRow, _mcuX, span, ref position);
                    output.Advance(position);
                    _mcuX++;
                    coded++;
                }

                _mcuX = 0;
                _mcuRow++;
                CancellationToken.ThrowIfCancellationRequested();
            }

            var end = output.GetSpan(JpegProgressiveScanEncoder.MaxBlockBytes);
            position = 0;
            scanEncoder.EndScan(end, ref position);
            output.Advance(position);
            _mcuRow = 0;
            _scanPass = ScanPass.None;
            _scanIndex++;
            return _scanIndex == JpegProgressiveScanEncoder.GetScript(_grayscale).Length;
        }

        /// <summary>Codes one MCU of a progressive scan: the blocks of every component for an interleaved scan, else one block (T.81 A.2).</summary>
        private void CodeProgressiveMcu(JpegProgressiveScan scan, int row, int column, Span<byte> destination, ref int position)
        {
            var scanEncoder = _scanEncoder!;
            if (scan.ComponentCount == 1)
            {
                scanEncoder.CodeBlock(scan.FirstComponent, _coefficientPlanes[scan.FirstComponent]!.GetBlock(row, column), destination, ref position);
                return;
            }

            var luminance = _coefficientPlanes[0]!;
            for (var by = 0; by < _v; by++)
            {
                for (var bx = 0; bx < _h; bx++)
                {
                    scanEncoder.CodeBlock(0, luminance.GetBlock((row * _v) + by, (column * _h) + bx), destination, ref position);
                }
            }

            scanEncoder.CodeBlock(1, _coefficientPlanes[1]!.GetBlock(row, column), destination, ref position);
            scanEncoder.CodeBlock(2, _coefficientPlanes[2]!.GetBlock(row, column), destination, ref position);
        }

        /// <summary>Writes the DHT segment of the tables of a progressive scan: DC tables for a DC first scan, the AC table of the component otherwise.</summary>
        private void WriteProgressiveTables(ImageOutputBuffer output, JpegProgressiveScan scan)
        {
            var scanEncoder = _scanEncoder!;
            Span<byte> payload = stackalloc byte[2 * (17 + 256)];
            var offset = 0;
            var tableClass = scan.Ss == 0 ? 0 : 1;
            var first = JpegProgressiveScanEncoder.GetTableIndex(scan.FirstComponent);
            var last = JpegProgressiveScanEncoder.GetTableIndex(scan.FirstComponent + scan.ComponentCount - 1);
            for (var table = first; table <= last; table++)
            {
                WriteHuffmanTable(payload, ref offset, tableClass, table, scanEncoder.GetTable(table));
            }

            JpegMetadataWriter.WriteSegment(output, MarkerDht, payload[..offset]);
        }

        /// <returns><see langword="true"/> when the last MCU was coded and the entropy-coded segment padded.</returns>
        private bool EncodeScan(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No JPEG frame is being written.");
            var blocksPerMcu = _grayscale ? 1 : (_h * _v) + 2;
            using (var lease = frame.GetStorage().AcquireLease())
            {
                var coded = 0;
                while (_mcuRow < _mcusY)
                {
                    if (!_rowLoaded)
                    {
                        LoadMcuRow(in lease, _mcuRow);
                        _rowLoaded = true;
                    }

                    while (_mcuX < _mcusX)
                    {
                        // Bounded output per call: stop once the buffer should be flushed (at least one MCU per call)
                        if (coded > 0 && output.ShouldFlush)
                            return false;

                        var span = output.GetSpan(blocksPerMcu * MaxBlockBytes);
                        var length = EncodeMcu(_mcuX, span);
                        output.Advance(length);
                        _mcuX++;
                        coded++;
                    }

                    _mcuX = 0;
                    _mcuRow++;
                    _rowLoaded = false;
                    CancellationToken.ThrowIfCancellationRequested();
                }
            }

            // Pad the last byte with 1-bits (T.81 F.1.2.3)
            var end = output.GetSpan(2);
            var endPosition = 0;
            _bits.Pad(end, ref endPosition);
            output.Advance(endPosition);
            return true;
        }

        /// <summary>
        /// Loads the rows of one MCU row: each source row (the last row replicated below the image) is converted to 8-bit RGB
        /// or gray (alpha flattened onto the background and 16-bit samples reduced, by the shared converter), then to
        /// level-shifted Y, Cb, Cr samples, the last column replicated to the padded width.
        /// </summary>
        private void LoadMcuRow(scoped in PixelLease lease, int mcuRow)
        {
            var rows = 8 * _v;
            var planeY = unsafe(MemoryMarshal.Cast<byte, float>(_planeY!.Span));
            var planeCb = _grayscale ? default : unsafe(MemoryMarshal.Cast<byte, float>(_planeCb!.Span));
            var planeCr = _grayscale ? default : unsafe(MemoryMarshal.Cast<byte, float>(_planeCr!.Span));
            for (var r = 0; r < rows; r++)
            {
                var y = Math.Min((mcuRow * rows) + r, _height - 1);
                ReadOnlySpan<byte> source = lease.GetRowBytes(y);
                if (_row is not null)
                {
                    var converted = _row.Span;
                    PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, converted, _background, ImageFormat.Jpeg);
                    source = converted;
                }

                var offset = r * _planeWidth;
                if (_grayscale)
                {
                    ConvertGrayRow(source, planeY.Slice(offset, _planeWidth));
                }
                else
                {
                    ConvertColorRow(source, planeY.Slice(offset, _planeWidth), planeCb.Slice(offset, _planeWidth), planeCr.Slice(offset, _planeWidth));
                }
            }
        }

        private void ConvertGrayRow(ReadOnlySpan<byte> source, Span<float> luma)
        {
            for (var x = 0; x < _width; x++)
            {
                luma[x] = source[x] - 128f;
            }

            luma[_width..].Fill(luma[_width - 1]);
        }

        /// <summary>
        /// JFIF 1.02 full-range YCbCr (ITU-R BT.601 luma weights): <c>Y = 0.299 R + 0.587 G + 0.114 B</c>,
        /// <c>Cb = (B - Y) / 1.772 + 128</c>, <c>Cr = (R - Y) / 1.402 + 128</c>, computed in double precision and stored
        /// unrounded (single precision) after the level shift of 128, so the only rounding before the DCT is the conversion
        /// to single precision.
        /// </summary>
        private void ConvertColorRow(ReadOnlySpan<byte> source, Span<float> luma, Span<float> blue, Span<float> red)
        {
            for (var x = 0; x < _width; x++)
            {
                double r = source[3 * x];
                double g = source[(3 * x) + 1];
                double b = source[(3 * x) + 2];
                var y = (0.299 * r) + (0.587 * g) + (0.114 * b);
                luma[x] = (float)(y - 128);
                blue[x] = (float)((b - y) / 1.772);
                red[x] = (float)((r - y) / 1.402);
            }

            luma[_width..].Fill(luma[_width - 1]);
            blue[_width..].Fill(blue[_width - 1]);
            red[_width..].Fill(red[_width - 1]);
        }

        private int EncodeMcu(int mcuX, Span<byte> destination)
        {
            var position = 0;
            var planeY = unsafe(MemoryMarshal.Cast<byte, float>(_planeY!.Span));
            var x0 = mcuX * 8 * _h;
            for (var by = 0; by < _v; by++)
            {
                for (var bx = 0; bx < _h; bx++)
                {
                    ExtractBlock(planeY, x0 + (bx * 8), by * 8, factorX: 1, factorY: 1);
                    EncodeBlock(destination, ref position, component: 0, _luminanceQuantization, _luminanceDc, _luminanceAc);
                }
            }

            if (!_grayscale)
            {
                ExtractBlock(unsafe(MemoryMarshal.Cast<byte, float>(_planeCb!.Span)), x0, 0, _h, _v);
                EncodeBlock(destination, ref position, component: 1, _chrominanceQuantization, _chrominanceDc!, _chrominanceAc!);
                ExtractBlock(unsafe(MemoryMarshal.Cast<byte, float>(_planeCr!.Span)), x0, 0, _h, _v);
                EncodeBlock(destination, ref position, component: 2, _chrominanceQuantization, _chrominanceDc!, _chrominanceAc!);
            }

            return position;
        }

        /// <summary>
        /// Copies an 8x8 block of samples starting at (<paramref name="x0"/>, <paramref name="y0"/>) of the MCU-row plane. With
        /// subsampling factors above 1, each block sample is the mean of the <c>factorX x factorY</c> full-resolution samples
        /// it covers (box filter: the subsampled sample sits at the center of those samples, the JFIF chroma siting).
        /// </summary>
        private void ExtractBlock(ReadOnlySpan<float> plane, int x0, int y0, int factorX, int factorY)
        {
            var block = _block.AsSpan();
            if (factorX == 1 && factorY == 1)
            {
                for (var y = 0; y < 8; y++)
                {
                    plane.Slice(((y0 + y) * _planeWidth) + x0, 8).CopyTo(block.Slice(y * 8, 8));
                }

                return;
            }

            var scale = 1d / (factorX * factorY);
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var sum = 0d;
                    for (var dy = 0; dy < factorY; dy++)
                    {
                        var line = plane.Slice(((y0 + (y * factorY) + dy) * _planeWidth) + x0 + (x * factorX), factorX);
                        foreach (var sample in line)
                        {
                            sum += sample;
                        }
                    }

                    block[(y * 8) + x] = (float)(sum * scale);
                }
            }
        }

        private void EncodeBlock(Span<byte> destination, ref int position, int component, ReadOnlySpan<ushort> quantization, JpegHuffmanEncoder dc, JpegHuffmanEncoder ac)
        {
            var coefficients = _coefficients.AsSpan();
            JpegForwardDct.TransformAndQuantize(_block, quantization, _scratch, coefficients);

            // DC: difference with the previous block of the component, size category then the additional bits (T.81 F.1.2.1)
            var difference = coefficients[0] - _predictions[component];
            _predictions[component] = coefficients[0];
            WriteValue(destination, ref position, dc, symbolHigh: 0, difference);

            // AC: run lengths of zeros (ZRL for 16), EOB after the last nonzero coefficient (T.81 F.1.2.2)
            var run = 0;
            for (var k = 1; k < 64; k++)
            {
                var value = coefficients[k];
                if (value == 0)
                {
                    run++;
                    continue;
                }

                while (run > 15)
                {
                    WriteSymbol(destination, ref position, ac, 0xF0);
                    run -= 16;
                }

                WriteValue(destination, ref position, ac, run, value);
                run = 0;
            }

            if (run > 0)
            {
                WriteSymbol(destination, ref position, ac, 0x00);
            }
        }

        /// <summary>Writes the symbol <c>(symbolHigh &lt;&lt; 4) | SSSS</c> for a value, then its SSSS additional bits (negative values as <c>value - 1</c>).</summary>
        private void WriteValue(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbolHigh, int value)
        {
            var magnitude = (uint)Math.Abs(value);
            var size = magnitude == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(magnitude);
            WriteSymbol(destination, ref position, table, (symbolHigh << 4) | size);
            if (size > 0)
            {
                var bits = value < 0 ? (uint)(value - 1) & ((1u << size) - 1) : (uint)value;
                _bits.Write(destination, ref position, bits, size);
            }
        }

        private void WriteSymbol(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbol)
        {
            var length = table.GetLength(symbol);
            if (length == 0)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The Huffman table has no code for the symbol 0x{symbol:X2}."));

            _bits.Write(destination, ref position, table.GetCode(symbol), length);
        }
    }
}
