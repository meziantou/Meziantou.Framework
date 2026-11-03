using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The entropy-coded data of one scan (ITU-T T.81 sections E.2.3/E.2.4 and F.2.2.5): the MCU structure (interleaved
/// or not), restart intervals and the bounded buffering of the data. The block decoding is the subclass's: sequential
/// (<see cref="JpegSequentialScanDecoder"/>) or progressive (<see cref="JpegProgressiveScanDecoder"/>) Huffman coding,
/// arithmetic coding (<see cref="JpegArithmeticScanDecoder"/>), or the samples of a lossless frame, whose data unit is one
/// sample instead of a block (<see cref="JpegLosslessScanDecoder"/>).
/// </summary>
/// <remarks>
/// <para>
/// The data is pushed as the container walker delivers it (raw bytes, then <c>FF 00</c> stuffing pairs and <c>FF Dn</c>
/// restart markers as separate two-byte chunks). An MCU is decoded only once the bytes of the largest possible MCU are
/// buffered, so decoding never waits for input in the middle of an MCU and the buffer stays bounded.
/// </para>
/// <para>
/// Validation (all <see cref="InvalidImageContentException"/>): entropy-coded data that ends before the last MCU of the scan
/// or of a restart interval, restart markers without a restart interval or out of sequence, and a missing restart marker.
/// Bits after the last MCU of an interval or of the scan (fill bits, extra bytes) and a restart marker after the last MCU of
/// the scan are ignored. The subclasses validate the coded values.
/// </para>
/// <para>
/// Lenient decoding (<see cref="AcceptsGarbledRestartNumbers"/>): a restart marker that ends a complete interval but whose
/// number is 3 to 5 after the expected one is read as the expected marker, as libjpeg-turbo, FFmpeg and Apple ImageIO
/// read it in a sequential scan; the next marker is expected after the expected one. A number one or two away from the
/// expected one stays an error: libjpeg-turbo and ImageIO then take the marker for a lost or a spurious one, and give
/// other pixels than FFmpeg.
/// </para>
/// </remarks>
internal abstract class JpegScanDecoder
{
    // Worst case of one block of 8-bit samples: DC (16-bit code + 11 bits), 63 AC coefficients (16 + 10 bits) and 4 ZRL
    // codes: 1729 bits. The progressive scans need less: an AC first scan at most 63 x (16 + 10) bits and 4 ZRL codes; a
    // refinement scan at most 63 x (16 + 1 + 1) bits (code, sign and correction bits), 3 ZRL codes and an EOB run (16 + 14 bits)
    private const int MaxBytesPerBlock = 220;

    // The same with the categories of 12-bit samples: DC 16 + 15 bits and AC 16 + 14 bits, 1985 bits
    private const int MaxBytesPerDeepBlock = 256;

    // Arithmetic coding: a binary decision renormalizes the interval by at most 15 bits (the smallest estimate is 1), and a
    // block has at most 2111 decisions: 32 for the DC difference (zero, sign, magnitude 1, 15 magnitude categories, 14
    // magnitude bits) and 33 for each of the 63 AC coefficients (end of block, zero, sign, magnitude 1 and 2, 14 categories,
    // 14 bits). That is 3959 bytes
    private const int MaxBytesPerArithmeticBlock = 4096;

    // The difference of a sample of a lossless frame: a 16-bit code and at most 15 bits (T.81 table H.2)
    private const int MaxBytesPerLosslessSample = 4;

    private readonly ScanComponent[] _components;
    private readonly JpegEntropyReader _reader;
    private readonly bool _interleaved;
    private readonly int _mcusPerLine;
    private readonly int _totalMcus;
    private readonly int _restartInterval;
    private readonly int _safeBytes;
    private readonly int _uncheckedBytes;
    private readonly Action<int>? _mcuRowDecoded;
    private readonly CancellationToken _cancellationToken;
    private int _mcu;

    // The position of _mcu in the MCU grid of the scan
    private int _mcuX;
    private int _mcuY;
    private int _intervalRemaining;
    private int _expectedRestart;
    private bool _pendingMarkerByte;

    /// <param name="components">The scan components with their tables, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="mcuRowDecoded">Called after each completed MCU row (block row for a non-interleaved scan) with its index.</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    /// <param name="arithmetic">Whether the scan is arithmetic coded.</param>
    /// <param name="lossless">Whether the scan codes the samples of a lossless frame.</param>
    protected JpegScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, Action<int>? mcuRowDecoded, CancellationToken cancellationToken, bool arithmetic = false, bool lossless = false)
    {
        _components = components;
        _reader = reader;
        _interleaved = components.Length > 1;
        if (_interleaved)
        {
            _mcusPerLine = mcusPerLine;
            _totalMcus = mcusPerLine * mcuRows;
        }
        else
        {
            // Non-interleaved: one block per MCU over the component's own block grid (T.81 A.2.2)
            var component = components[0].Component;
            _mcusPerLine = component.BlocksPerLine;
            _totalMcus = component.BlocksPerLine * component.BlocksPerColumn;
        }

        var blocksPerMcu = 0;
        foreach (var scanComponent in components)
        {
            blocksPerMcu += _interleaved ? scanComponent.Component.HorizontalSampling * scanComponent.Component.VerticalSampling : 1;
        }

        var precision = components[0].Component.Precision;
        _safeBytes = GetRequiredBufferCapacity(blocksPerMcu, precision, arithmetic, lossless);

        // A Huffman-coded MCU decoded from buffered bytes reads at most its own bytes and 16 more (UncheckedCursor)
        _uncheckedBytes = arithmetic || lossless ? int.MaxValue : _safeBytes - 8;
        MaxDcCategory = precision > 8 ? 15 : 11;
        MaxAcCategory = precision > 8 ? 14 : 10;
        _restartInterval = restartInterval;
        _intervalRemaining = restartInterval;
        _mcuRowDecoded = mcuRowDecoded;
        _cancellationToken = cancellationToken;
        reader.Reset();
    }

    /// <summary>Gets the number of bytes the entropy reader must be able to buffer.</summary>
    /// <param name="blocksPerMcu">The largest number of blocks of an MCU.</param>
    /// <param name="precision">The number of bits of a sample (8 or 12).</param>
    /// <param name="arithmetic">Whether the scans are arithmetic coded.</param>
    /// <param name="lossless">Whether the scans code the samples of a lossless frame.</param>
    public static int GetRequiredBufferCapacity(int blocksPerMcu, int precision, bool arithmetic, bool lossless = false)
        => (blocksPerMcu * (lossless ? MaxBytesPerLosslessSample : arithmetic ? MaxBytesPerArithmeticBlock : precision > 8 ? MaxBytesPerDeepBlock : MaxBytesPerBlock)) + 24;

    /// <summary>Gets a value indicating whether every MCU of the scan was decoded.</summary>
    public bool IsComplete => _mcu >= _totalMcus;

    /// <summary>
    /// Gets a value indicating whether a restart marker whose number is 3 to 5 after the expected one is read as the
    /// expected marker (lenient decoding) instead of being an error.
    /// </summary>
    public bool AcceptsGarbledRestartNumbers { get; init; }

    /// <summary>
    /// Gets a value indicating whether the MCU being decoded is in the buffer with 16 more bytes, before the end of its
    /// segment: its blocks can be read with <see cref="JpegEntropyReader.UncheckedCursor"/>. Always <see langword="false"/>
    /// for arithmetic-coded and lossless scans.
    /// </summary>
    protected bool UncheckedReads { get; private set; }

    /// <summary>
    /// Gets or sets the number of the next MCUs that code nothing (the rest of an EOB run of a progressive AC first scan,
    /// whose MCU is one block): they are counted as decoded without <see cref="DecodeBlock"/>, up to the end of their MCU row
    /// or restart interval. Reset at each restart marker.
    /// </summary>
    protected int SkippableMcus { get; set; }

    /// <summary>Gets the entropy reader of the scan.</summary>
    protected JpegEntropyReader Reader => _reader;

    /// <summary>Gets a value indicating whether the scan codes several components, whose data units are interleaved.</summary>
    protected bool IsInterleaved => _interleaved;

    /// <summary>Gets the number of MCUs of an MCU row of the scan.</summary>
    protected int McusPerLine => _mcusPerLine;

    /// <summary>Gets the largest category of a DC difference: 11 for 8-bit samples, 15 for 12-bit samples (T.81 tables F.1 and F.6).</summary>
    protected int MaxDcCategory { get; }

    /// <summary>Gets the largest category of an AC coefficient: 10 for 8-bit samples, 14 for 12-bit samples (T.81 tables F.2 and F.7).</summary>
    protected int MaxAcCategory { get; }

    private bool IsDiscarding => _mcu >= _totalMcus || (_restartInterval > 0 && _intervalRemaining == 0);

    /// <summary>Consumes entropy-coded bytes as delivered by the container walker (stuffing pairs and restart markers included).</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            if (_pendingMarkerByte)
            {
                _pendingMarkerByte = false;
                OnMarkerByte(data[0]);
                data = data[1..];
                continue;
            }

            var index = data.IndexOf((byte)0xFF);
            if (index != 0)
            {
                var plain = index < 0 ? data : data[..index];
                AppendData(plain);
                data = data[plain.Length..];
                continue;
            }

            if (data.Length == 1)
            {
                _pendingMarkerByte = true;
                return;
            }

            OnMarkerByte(data[1]);
            data = data[2..];
        }
    }

    /// <summary>Decodes the rest of the scan at the end of its entropy-coded data.</summary>
    /// <param name="endOfInput">
    /// Whether the data ends with the input instead of at a marker (lenient decoding of a file without EOI marker).
    /// </param>
    public void Finish(bool endOfInput = false)
    {
        if (_pendingMarkerByte)
            throw Invalid("The JPEG entropy-coded data ends with an incomplete marker.");

        _reader.MarkEndOfSegment();
        DecodeAvailable(endOfSegment: true);
        if (_mcu < _totalMcus)
            throw Invalid(endOfInput ? "The JPEG data ends before the last MCU of its last scan." : "The JPEG scan ends before its last MCU: a restart marker is missing.");
    }

    private static ReadOnlySpan<byte> StuffedByte => [0xFF];

    protected static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Jpeg);

    /// <summary>Decodes the next block of the scan.</summary>
    /// <param name="scanComponent">The component of the block.</param>
    /// <param name="blockRow">The block row in the component's block grid.</param>
    /// <param name="blockColumn">The block column in the component's block grid.</param>
    protected abstract void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn);

    /// <summary>Gets the components of the scan, in scan order.</summary>
    protected ReadOnlySpan<ScanComponent> Components => _components;

    /// <summary>
    /// Writes the samples of a block of a sequential scan: the inverse DCT of its dequantized coefficients.
    /// </summary>
    /// <param name="scanComponent">The component of the block.</param>
    /// <param name="blockRow">The block row in the component's block grid.</param>
    /// <param name="blockColumn">The block column in the component's block grid.</param>
    /// <param name="block">The dequantized AC coefficients (natural order), when <paramref name="hasAc"/>; cleared.</param>
    /// <param name="dc">The quantized DC coefficient.</param>
    /// <param name="hasAc">Whether an AC coefficient is not zero.</param>
    /// <param name="sparse">Whether every coefficient outside the top-left 4x4 corner is zero (<see cref="JpegIdct.IsSparse(int)"/>).</param>
    protected static void TransformBlock(ScanComponent scanComponent, int blockRow, int blockColumn, double[] block, int dc, bool hasAc, bool sparse)
        => TransformBlock(scanComponent, scanComponent.GetBlockOffset(blockRow, blockColumn), block, dc, hasAc, sparse);

    /// <summary>Writes the samples of a block of a sequential scan at an offset of the sample plane.</summary>
    /// <param name="scanComponent">The component of the block.</param>
    /// <param name="offset">The offset of the first sample of the block in the plane of the component.</param>
    /// <param name="block">The dequantized AC coefficients (natural order), when <paramref name="hasAc"/>; cleared.</param>
    /// <param name="dc">The quantized DC coefficient.</param>
    /// <param name="hasAc">Whether an AC coefficient is not zero.</param>
    /// <param name="sparse">Whether every coefficient outside the top-left 4x4 corner is zero (<see cref="JpegIdct.IsSparse(int)"/>).</param>
    protected static void TransformBlock(ScanComponent scanComponent, int offset, double[] block, int dc, bool hasAc, bool sparse)
    {
        var component = scanComponent.Component;
        var quantization = scanComponent.Quantization;
        if (!component.IsReduced && !component.HasDeepSamples)
        {
            // 8-bit samples at full scale
            var samples = scanComponent.Samples.AsSpan(offset);
            if (hasAc)
            {
                block[0] = dc * (double)quantization[0];
                JpegIdct.Transform(block, samples, component.Stride, sparse);
                JpegIdct.ClearBlock(block);
            }
            else
            {
                JpegIdct.TransformDcOnly((long)dc * quantization[0], samples, component.Stride);
            }

            return;
        }

        if (component.IsReduced)
        {
            if (hasAc)
            {
                block[0] = dc * (double)quantization[0];
                TransformScaledBlock(component, offset, block, (long)dc * quantization[0], hasAc: true, sparse);
                JpegIdct.ClearBlock(block);
            }
            else
            {
                TransformScaledBlock(component, offset, block, (long)dc * quantization[0], hasAc: false, sparse);
            }

            return;
        }

        if (hasAc)
        {
            block[0] = dc * (double)quantization[0];
            if (component.Precision > 8)
            {
                JpegIdct.TransformDeep(block, component.DeepSamples[offset..], component.Stride, component.Precision);
            }
            else
            {
                JpegIdct.Transform(block, component.Samples.AsSpan(offset), component.Stride, sparse);
            }

            JpegIdct.ClearBlock(block);
        }
        else if (component.Precision > 8)
        {
            JpegIdct.TransformDeepDcOnly((long)dc * quantization[0], component.DeepSamples[offset..], component.Stride, component.Precision);
        }
        else
        {
            JpegIdct.TransformDcOnly((long)dc * quantization[0], component.Samples.AsSpan(offset), component.Stride);
        }
    }

    /// <summary>
    /// Writes the samples of a block of a frame decoded at a reduced scale (<see cref="JpegFrameComponent.IsReduced"/>): the
    /// means of the samples of its inverse DCT (<see cref="JpegIdct.TransformReduced(Span{double}, Span{byte}, int, int, int, bool)"/>),
    /// or the value of its DC coefficient for a block without AC coefficients and for a block of one sample.
    /// </summary>
    /// <param name="component">The component of the block.</param>
    /// <param name="offset">The offset of the first sample of the block in the plane of the component.</param>
    /// <param name="block">The dequantized coefficients (natural order), DC included, when <paramref name="hasAc"/>; overwritten.</param>
    /// <param name="dequantizedDc">The DC coefficient multiplied by its quantization value.</param>
    /// <param name="hasAc">Whether an AC coefficient is not zero.</param>
    /// <param name="sparse">Whether every coefficient outside the top-left 4x4 corner is zero (<see cref="JpegIdct.IsSparse(int)"/>).</param>
    internal static void TransformScaledBlock(JpegFrameComponent component, int offset, Span<double> block, long dequantizedDc, bool hasAc, bool sparse)
    {
        var width = component.BlockWidth;
        var height = component.BlockHeight;
        if (hasAc && width * height > 1)
        {
            if (component.Precision > 8)
            {
                JpegIdct.TransformDeepReduced(block, component.DeepSamples[offset..], component.Stride, component.Precision, width, height, sparse);
            }
            else
            {
                JpegIdct.TransformReduced(block, component.Samples.AsSpan(offset), component.Stride, width, height, sparse);
            }
        }
        else if (component.Precision > 8)
        {
            JpegIdct.TransformDeepDcOnly(dequantizedDc, component.DeepSamples[offset..], component.Stride, component.Precision, width, height);
        }
        else
        {
            JpegIdct.TransformDcOnly(dequantizedDc, component.Samples.AsSpan(offset), component.Stride, width, height);
        }
    }

    /// <summary>Completes an MCU row (a row of data units for a non-interleaved scan), before the frame is told about it.</summary>
    /// <param name="row">The index of the row.</param>
    protected virtual void OnMcuRowDecoded(int row)
    {
    }

    /// <summary>Resets the decoding state at a restart marker (T.81 F.2.1.3.1, G.1.2.2): the DC predictions.</summary>
    protected virtual void OnRestart()
    {
        SkippableMcus = 0;
        foreach (var component in _components)
        {
            component.Predictor = 0;
        }
    }

    private void OnMarkerByte(byte value)
    {
        if (value == 0x00)
        {
            // Stuffed 0xFF data byte
            AppendData(StuffedByte);
            return;
        }

        if (value is < 0xD0 or > 0xD7)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG marker 0xFF{value:X2} is not valid inside entropy-coded data."));

        OnRestartMarker(value - 0xD0);
    }

    private void OnRestartMarker(int index)
    {
        if (_restartInterval == 0)
            throw Invalid("The JPEG entropy-coded data contains a restart marker but no restart interval is defined.");

        if (_mcu >= _totalMcus)
            return; // A restart marker after the last MCU is ignored

        _reader.MarkEndOfSegment();
        DecodeAvailable(endOfSegment: true);
        if (_mcu >= _totalMcus)
            return;

        if (index != _expectedRestart && !(AcceptsGarbledRestartNumbers && ((index - _expectedRestart) & 7) is >= 3 and <= 5))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG restart marker RST{index} is out of sequence (RST{_expectedRestart} expected)."));

        _reader.Reset();
        OnRestart();
        _intervalRemaining = _restartInterval;
        _expectedRestart = (_expectedRestart + 1) & 7;
    }

    private void AppendData(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty && !IsDiscarding)
        {
            if (_reader.FreeSpace < data.Length)
            {
                _reader.Compact();
            }

            var count = Math.Min(_reader.FreeSpace, data.Length);
            _reader.Append(data[..count]);
            data = data[count..];
            DecodeAvailable(endOfSegment: false);
        }
    }

    private void DecodeAvailable(bool endOfSegment)
    {
        // Before the end of the segment, an MCU is only decoded when _safeBytes are available: its bytes and 16 more are
        // then buffered (the accumulator holds at most 7 whole bytes)
        UncheckedReads = !endOfSegment && _uncheckedBytes != int.MaxValue;
        while (_mcu < _totalMcus)
        {
            if (_restartInterval > 0 && _intervalRemaining == 0)
                return;

            var count = 1;
            if (SkippableMcus > 0)
            {
                // MCUs that consume no data: as many as the row and the interval have at once
                count = Math.Min(SkippableMcus, _mcusPerLine - _mcuX);
                if (_restartInterval > 0)
                {
                    count = Math.Min(count, _intervalRemaining);
                }

                SkippableMcus -= count;
            }
            else
            {
                if (!endOfSegment && _reader.AvailableBytes < _safeBytes)
                    return;

                Debug.Assert(!UncheckedReads || _reader.BufferedBytes >= _uncheckedBytes);
                if (UncheckedReads)
                {
                    // The MCUs up to the end of the row or of the restart interval, as far as their bytes are buffered
                    count = _mcusPerLine - _mcuX;
                    if (_restartInterval > 0)
                    {
                        count = Math.Min(count, _intervalRemaining);
                    }

                    count = DecodeMcus(_mcuX, _mcuY, count);
                    Debug.Assert(count >= 1);
                }
                else
                {
                    DecodeMcu(_mcuX, _mcuY);
                }
            }

            _mcu += count;
            _intervalRemaining -= count;
            _mcuX += count;
            if (_mcuX == _mcusPerLine)
            {
                _mcuX = 0;
                var row = _mcuY++;
                _cancellationToken.ThrowIfCancellationRequested();
                OnMcuRowDecoded(row);
                _mcuRowDecoded?.Invoke(row);
            }
        }
    }

    /// <summary>
    /// Decodes the next MCUs of a row whose bytes are buffered (<see cref="UncheckedReads"/>): at least the first one, then
    /// each next one as long as <see cref="HasUncheckedBytes"/> holds before it, which subclasses check with the bit state in
    /// locals instead of returning to the scan after each MCU. The default decodes one MCU (<see cref="DecodeMcu"/>).
    /// </summary>
    /// <param name="mcuX">The column of the first MCU.</param>
    /// <param name="mcuY">The row of the MCUs.</param>
    /// <param name="count">The number of MCUs to the end of the row or of the restart interval (at least 1).</param>
    /// <returns>The number of MCUs decoded, from 1 to <paramref name="count"/>.</returns>
    /// <remarks>An MCU that sets <see cref="SkippableMcus"/> must be the last one decoded.</remarks>
    protected virtual int DecodeMcus(int mcuX, int mcuY, int count)
    {
        DecodeMcu(mcuX, mcuY);
        return 1;
    }

    /// <summary>
    /// Gets a value indicating whether the next MCU of <see cref="DecodeMcus"/> can be decoded with the bit state of a cursor:
    /// the bytes of the largest MCU and 16 more are buffered after it, as the scan checks before each MCU.
    /// </summary>
    /// <param name="cursor">The bit state.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool HasUncheckedBytes(in JpegEntropyReader.UncheckedCursor cursor) => cursor.GetAvailableBytes(_reader) >= _safeBytes;

    /// <summary>Decodes the blocks of an MCU, in scan order (<see cref="DecodeBlock"/>).</summary>
    /// <param name="mcuX">The column of the MCU.</param>
    /// <param name="mcuY">The row of the MCU.</param>
    protected virtual void DecodeMcu(int mcuX, int mcuY)
    {
        if (!_interleaved)
        {
            DecodeBlock(_components[0], mcuY, mcuX);
            return;
        }

        foreach (var scanComponent in _components)
        {
            var component = scanComponent.Component;
            var h = component.HorizontalSampling;
            var v = component.VerticalSampling;
            for (var by = 0; by < v; by++)
            {
                for (var bx = 0; bx < h; bx++)
                {
                    DecodeBlock(scanComponent, (mcuY * v) + by, (mcuX * h) + bx);
                }
            }
        }
    }

    /// <summary>A component of a scan: its plane or coefficients, tables (captured when the scan starts) and DC prediction.</summary>
    /// <remarks>
    /// Progressive scans have only the table they use: DC first scans a DC table, DC refinements none, AC scans an AC table.
    /// An arithmetic-coded scan has no Huffman table but the conditioning of its components (<see cref="Arithmetic"/>).
    /// </remarks>
    internal sealed class ScanComponent(JpegFrameComponent component, JpegHuffmanTable? dcTable, JpegHuffmanTable? acTable, ushort[] quantization)
    {
        public JpegFrameComponent Component { get; } = component;

        public JpegHuffmanTable? DcTable { get; } = dcTable;

        public JpegHuffmanTable? AcTable { get; } = acTable;

        /// <summary>Gets the quantization values in natural order.</summary>
        public ushort[] Quantization { get; } = quantization;

        /// <summary>
        /// Gets, for each zig-zag position, <c>(quantization value &lt;&lt; 6) | natural index</c>: the two lookups of a
        /// coefficient in one (empty for the samples of a lossless frame, which have no quantization).
        /// </summary>
        public int[] ZigZagQuantization { get; } = quantization.Length == 64 ? CreateZigZagQuantization(quantization) : [];

        public int Predictor { get; set; }

        /// <summary>
        /// Gets the coefficients of a block of the coefficient grid, like <see cref="JpegFrameComponent.GetCoefficients"/>
        /// with the buffer of the grid read once: it is not reallocated while a scan decodes.
        /// </summary>
        /// <param name="blockRow">The block row in the coefficient grid.</param>
        /// <param name="blockColumn">The block column in the coefficient grid.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<short> GetCoefficients(int blockRow, int blockColumn)
        {
            var buffer = _coefficientBuffer ??= Component.CoefficientBuffer;
            var offset = ((blockRow * Component.CoefficientBlocksPerLine) + blockColumn) * 64 * sizeof(short);
            return unsafe(MemoryMarshal.Cast<byte, short>(buffer.AsSpan(offset, 64 * sizeof(short))));
        }

        private byte[]? _coefficientBuffer;

        // The first block row of the band whose offset is cached (none at first), and the offset of that row
        private int _bandFirstBlockRow = int.MinValue / 2;
        private int _bandOffset;

        /// <summary>
        /// Gets the 8-bit samples of the plane of the component (<see cref="JpegFrameComponent.Samples"/>), read once: the plane
        /// is not reallocated while a scan decodes.
        /// </summary>
        public byte[] Samples => field ??= Component.Samples;

        /// <summary>
        /// Gets the offset of the first sample of a block in the plane, like <see cref="JpegFrameComponent.GetBlockOffset"/>,
        /// with the offset of the first block row of its band cached: the rows of a band are contiguous in the plane, so that
        /// the other blocks of the band need no division.
        /// </summary>
        /// <param name="blockRow">The block row in the component's block grid.</param>
        /// <param name="blockColumn">The block column in the component's block grid.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetBlockOffset(int blockRow, int blockColumn)
        {
            var plane = Component;
            var rowInBand = blockRow - _bandFirstBlockRow;
            if ((uint)rowInBand >= (uint)plane.VerticalSampling)
            {
                rowInBand = LoadBand(blockRow);
            }

            return _bandOffset + (rowInBand * plane.BlockHeight * plane.Stride) + (blockColumn * plane.BlockWidth);
        }

        private int LoadBand(int blockRow)
        {
            // A band is the rows of one MCU row: VerticalSampling block rows (1 for the only component of a grayscale frame)
            var first = blockRow - (blockRow % Component.VerticalSampling);
            _bandFirstBlockRow = first;
            _bandOffset = Component.GetBlockOffset(first, 0);
            return blockRow - first;
        }

        /// <summary>Gets the statistics and the conditioning of the component in an arithmetic-coded scan, or <see langword="null"/>.</summary>
        public JpegArithmeticConditioning? Arithmetic { get; init; }

        private static int[] CreateZigZagQuantization(ushort[] quantization)
        {
            var zigZag = JpegIdct.ZigZag;
            var table = new int[64];
            for (var k = 0; k < 64; k++)
            {
                table[k] = (quantization[zigZag[k]] << 6) | zigZag[k];
            }

            return table;
        }
    }
}
