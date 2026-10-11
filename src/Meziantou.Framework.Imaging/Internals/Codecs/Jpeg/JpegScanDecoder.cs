namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The entropy-coded data of one Huffman scan (ITU-T T.81 sections E.2.3/E.2.4 and F.2.2.5): the MCU structure (interleaved
/// or not), restart intervals and the bounded buffering of the data. The block decoding is the subclass's: sequential
/// (<see cref="JpegSequentialScanDecoder"/>) or progressive (<see cref="JpegProgressiveScanDecoder"/>).
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
/// </remarks>
internal abstract class JpegScanDecoder
{
    // Worst case of one block: DC (16-bit code + 11 bits), 63 AC coefficients (16 + 10 bits) and 4 ZRL codes: 1729 bits. The
    // progressive scans need less: an AC first scan at most 63 x (16 + 10) bits and 4 ZRL codes; a refinement scan at most
    // 63 x (16 + 1 + 1) bits (code, sign and correction bits), 3 ZRL codes and an EOB run (16 + 14 bits)
    private const int MaxBytesPerBlock = 220;

    private readonly ScanComponent[] _components;
    private readonly JpegEntropyReader _reader;
    private readonly bool _interleaved;
    private readonly int _mcusPerLine;
    private readonly int _totalMcus;
    private readonly int _restartInterval;
    private readonly int _safeBytes;
    private readonly Action<int>? _mcuRowDecoded;
    private readonly CancellationToken _cancellationToken;
    private int _mcu;
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
    protected JpegScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, Action<int>? mcuRowDecoded, CancellationToken cancellationToken)
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

        _safeBytes = (blocksPerMcu * MaxBytesPerBlock) + 8;
        _restartInterval = restartInterval;
        _intervalRemaining = restartInterval;
        _mcuRowDecoded = mcuRowDecoded;
        _cancellationToken = cancellationToken;
        reader.Reset();
    }

    /// <summary>Gets the number of bytes the entropy reader must be able to buffer.</summary>
    public static int GetRequiredBufferCapacity(int blocksPerMcu) => (blocksPerMcu * MaxBytesPerBlock) + 8;

    /// <summary>Gets a value indicating whether every MCU of the scan was decoded.</summary>
    public bool IsComplete => _mcu >= _totalMcus;

    /// <summary>Gets the entropy reader of the scan.</summary>
    protected JpegEntropyReader Reader => _reader;

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
    public void Finish()
    {
        if (_pendingMarkerByte)
            throw Invalid("The JPEG entropy-coded data ends with an incomplete marker.");

        _reader.MarkEndOfSegment();
        DecodeAvailable(endOfSegment: true);
        if (_mcu < _totalMcus)
            throw Invalid("The JPEG scan ends before its last MCU: a restart marker is missing.");
    }

    private static ReadOnlySpan<byte> StuffedByte => [0xFF];

    protected static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Jpeg);

    /// <summary>Decodes the next block of the scan.</summary>
    /// <param name="scanComponent">The component of the block.</param>
    /// <param name="blockRow">The block row in the component's block grid.</param>
    /// <param name="blockColumn">The block column in the component's block grid.</param>
    protected abstract void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn);

    /// <summary>Resets the decoding state at a restart marker (T.81 F.2.1.3.1, G.1.2.2): the DC predictions.</summary>
    protected virtual void OnRestart()
    {
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

        if (index != _expectedRestart)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG restart marker RST{index} is out of sequence (RST{_expectedRestart} expected)."));

        _reader.Reset();
        OnRestart();
        _intervalRemaining = _restartInterval;
        _expectedRestart = (index + 1) & 7;
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
        while (_mcu < _totalMcus)
        {
            if (_restartInterval > 0 && _intervalRemaining == 0)
                return;

            if (!endOfSegment && _reader.AvailableBytes < _safeBytes)
                return;

            DecodeMcu(_mcu);
            _mcu++;
            _intervalRemaining--;
            if (_mcu % _mcusPerLine == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _mcuRowDecoded?.Invoke((_mcu / _mcusPerLine) - 1);
            }
        }
    }

    private void DecodeMcu(int mcu)
    {
        var mcuX = mcu % _mcusPerLine;
        var mcuY = mcu / _mcusPerLine;
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
    /// <remarks>Progressive scans have only the table they use: DC first scans a DC table, DC refinements none, AC scans an AC table.</remarks>
    internal sealed class ScanComponent(JpegFrameComponent component, JpegHuffmanTable? dcTable, JpegHuffmanTable? acTable, ushort[] quantization)
    {
        public JpegFrameComponent Component { get; } = component;

        public JpegHuffmanTable? DcTable { get; } = dcTable;

        public JpegHuffmanTable? AcTable { get; } = acTable;

        /// <summary>Gets the quantization values in natural order.</summary>
        public ushort[] Quantization { get; } = quantization;

        public int Predictor { get; set; }
    }
}
