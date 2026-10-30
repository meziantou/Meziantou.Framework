using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG pixel decoder plugged into the <see cref="JpegStructureParser"/> marker walk, shared by eager loads
/// and sequential readers: sequential frames (baseline SOF0 and extended
/// sequential SOF1) and progressive frames (SOF2), of 8-bit or 12-bit samples, Huffman coded or arithmetic coded (SOF9
/// and SOF10), and lossless frames (SOF3, Huffman coded) of 2-bit to 16-bit samples. The walker validates the container (markers, segment lengths,
/// frame and scan headers, progressive scan parameters and coefficient progression, unsupported modes, metadata, limits);
/// this observer parses the tables, decodes every scan and writes the rows through the frame sink.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Tables: DQT (8- or 16-bit values, nonzero), DHT (class 0/1, identifiers 0-3, valid prefix codes) and DRI may appear before
/// any scan; a scan uses the tables defined when it starts. Defects are <see cref="InvalidImageContentException"/> found by
/// decoding only (identification does not parse table contents).
/// </description></item>
/// <item><description>
/// Arithmetic coding: the scans of an arithmetic-coded frame use no Huffman table but the conditioning of DAC segments
/// (class 0/1, identifiers 0-3; DC bounds <c>0 &lt;= L &lt;= U &lt;= 15</c>, AC threshold <c>1 &lt;= Kx &lt;= 63</c>), with the
/// defaults <c>L = 0</c>, <c>U = 1</c> and <c>Kx = 5</c> for a table no segment defines (T.81 F.1.4.4.1.4 and F.1.4.4.2.1). The
/// segments of the other entropy coding are validated and unused: DHT in an arithmetic-coded file, DAC in a
/// Huffman-coded one.
/// </description></item>
/// <item><description>
/// Streaming: when the first scan codes every component (always for grayscale and for usual baseline files), rows are
/// produced while the scan is decoded, one MCU row behind (the triangle upsampling filter needs the next sample row), with
/// three MCU rows of samples per component. Frames coded in several scans keep full component planes until their last scan.
/// </description></item>
/// <item><description>
/// Progressive frames are never row-streamed: any later scan may refine any coefficient of any block, so the quantized
/// coefficients of the whole image are kept (16 bits each, charged to the scope as decoder state) until EOI, where the
/// blocks are transformed one MCU row at a time into a ring of three MCU rows of samples and written like a streamed
/// sequential frame. Each scan only updates coefficients (<see cref="JpegProgressiveScanDecoder"/>).
/// </description></item>
/// <item><description>
/// A sequential reader that asks for renderings gets one after each scan of a progressive frame once every component has
/// its DC coefficients: the coefficients decoded so far are transformed like at EOI, and kept. When the input ends before
/// the frame, the rendering shows the coefficients decoded so far (progressive frames) or the rows that were written
/// (streamed frames, the other rows being zero).
/// </description></item>
/// <item><description>
/// The image (one frame, duration zero) ends at EOI after <see cref="DecodedFrameSink.UpdateMetadata"/>, so reader frames
/// carry the metadata found after the scans like eager loads. EXIF orientation is metadata only: pixels are never rotated.
/// </description></item>
/// <item><description>
/// Parallelism: with <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1, a frame of at least
/// <see cref="RowBands.MinPixelsForParallelism"/> pixels is reconstructed by several workers (<see cref="JpegReconstruction"/>):
/// the progressive frames at EOI, and the Huffman-coded sequential frames whose first scan codes every component, by batches
/// of MCU rows whose quantized coefficients the scan decoder keeps in a ring. The rows of the frames coded in several
/// sequential scans are written by several workers. The entropy-coded data is always decoded by the calling thread, which
/// also decodes the arithmetic-coded sequential frames, the lossless frames and the datastreams embedded in another format
/// as one worker does. The pixels and the errors never depend on the number of workers.
/// </description></item>
/// <item><description>
/// Four-component frames (CMYK, YCCK) are inks: no pixel format holds them, so they are decoded only when the request
/// carries a <see cref="CmykConversionOptions"/>. The rows are converted to RGB by an ICC conversion from the CMYK profile
/// of the file (or the assumed one) to the destination profile, which labels the image: the pixels and their profile
/// change together, and the CMYK profile is not kept. Without the options, or without any source profile, the frame is
/// an <see cref="UnsupportedImageFeatureException"/> raised before any allocation.
/// </description></item>
/// <item><description>
/// Samples that do not have 8 bits are kept in 16-bit planes and written scaled to 16 bits (<see cref="JpegDeepRowWriter"/>):
/// <see cref="PixelFormat.Gray16"/> for one component, opaque <see cref="PixelFormat.Rgba64"/> otherwise. The container
/// of an embedded datastream is told the precision and receives the scaled samples.
/// </description></item>
/// <item><description>
/// Lossless frames (T.81 Annex H) share the scan structure, the planes and the row writers of the sequential frames,
/// with a data unit of one sample instead of a block of 8 by 8: an MCU row is <c>Vmax</c> rows, a scan codes each
/// component once, and <see cref="JpegLosslessScanDecoder"/> reconstructs the samples from their predictions. They use
/// no quantization table. Subsampled components are upsampled and three components are converted like the ones of the
/// other frames.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegDecoder : JpegDecodeObserver, IPartialFrameRenderer
{
    private const byte MarkerDht = 0xC4;
    private const byte MarkerDac = 0xCC;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerDqt = 0xDB;
    private const byte MarkerDri = 0xDD;
    private const int EntropyBufferCapacity = 64 * 1024;
    private const int FastAcMinimumPixels = 128 * 128;

    private readonly ImageDecodeRequest? _request;
    private readonly ImageCodecContext _context;

    // The container of an embedded datastream and the meaning it gives to the components; null for an image
    private readonly JpegSampleReceiver? _receiver;
    private readonly ImageColorModel _embeddedColorModel;
    private readonly JpegHuffmanTable?[] _dcTables = new JpegHuffmanTable?[4];
    private readonly JpegHuffmanTable?[] _acTables = new JpegHuffmanTable?[4];
    private readonly ushort[]?[] _quantizationTables = new ushort[]?[4];

    // The conditioning of the arithmetic coding tables: the bounds of the DC classes and the AC threshold
    private readonly (int Lower, int Upper)[] _dcConditioning = [(0, 1), (0, 1), (0, 1), (0, 1)];
    private readonly int[] _acConditioning = [5, 5, 5, 5];
    private int _restartInterval;
    private JpegStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private JpegFrameComponent[] _components = [];
    private ImageColorModel _colorModel;
    private IccPipeline? _inkConversion;
    private int _mcusX;
    private int _mcusY;
    private int _maxVertical;
    private bool _streaming;
    private bool _progressive;
    private bool _arithmetic;
    private bool _lossless;

    // The number of samples of a side of a data unit: 8, or 1 for a lossless frame
    private int _dataUnitSize = 8;
    private int _scanCount;

    // Progressive frames: the scans that were completed, whether the current one codes AC coefficients, and whether one did
    private int _completedScans;
    private bool _scanHasAc;
    private bool _hasAcCoefficients;
    private JpegEntropyReader? _reader;
    private JpegBandWriter? _writer;

    // The writers of the workers that write bands at the same time, the first one being _writer; empty for one worker
    private JpegBandWriter[] _writers = [];

    // The number of workers that reconstruct the frame, and the reconstruction of a sequential frame decoded by several
    private int _workers = 1;
    private JpegReconstruction? _reconstruction;
    private int _decodedRows;
    private int _precision;
    private JpegScanDecoder? _scan;
    private JpegScanDecoder.ScanComponent[] _scanComponents = [];

    public JpegDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    /// <summary>
    /// Creates the decoder of a datastream embedded in another format (a strip or a tile of a TIFF page): no image is
    /// built, the rows are handed to <paramref name="receiver"/>, and the container says what the components are, whatever
    /// the JFIF and Adobe segments of the datastream say.
    /// </summary>
    /// <param name="context">The context of the operation that decodes the container.</param>
    /// <param name="receiver">The destination of the rows.</param>
    /// <param name="colorModel">
    /// <see cref="ImageColorModel.YCbCr"/> to convert three components to RGB; any other value hands the stored samples
    /// over.
    /// </param>
    public JpegDecoder(ImageCodecContext context, JpegSampleReceiver receiver, ImageColorModel colorModel)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(receiver);
        _context = context;
        _receiver = receiver;
        _embeddedColorModel = colorModel;
    }

    public override void OnSegment(byte marker, ReadOnlySpan<byte> payload)
    {
        switch (marker)
        {
            case MarkerDqt:
                ParseQuantizationTables(payload);
                break;

            case MarkerDht:
                ParseHuffmanTables(payload);
                break;

            case MarkerDac:
                ParseArithmeticConditioning(payload);
                break;

            case MarkerDri:
                // The walker checked the length
                _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(payload);
                break;

            case MarkerSos:
                StartScan(payload);
                break;
        }
    }

    public override void OnHeaderComplete(JpegStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _progressive = structure.IsProgressive;
        _arithmetic = structure.IsArithmetic;
        _lossless = structure.IsLossless;
        _dataUnitSize = _lossless ? 1 : 8;
        _precision = structure.Precision;
        _colorModel = structure.ColorModel;

        var frameComponents = structure.Components;
        var components = new JpegFrameComponent[frameComponents.Count];
        for (var i = 0; i < components.Length; i++)
        {
            var component = frameComponents[i];

            // The only component of a grayscale frame is coded in a non-interleaved scan: its sampling factors play no role
            components[i] = components.Length == 1
                ? new JpegFrameComponent(i, component.Id, 1, 1, component.QuantizationTable, _precision, _dataUnitSize)
                : new JpegFrameComponent(i, component.Id, component.HorizontalSampling, component.VerticalSampling, component.QuantizationTable, _precision, _dataUnitSize);
        }

        var maxHorizontal = components.Max(component => component.HorizontalSampling);
        var maxVertical = components.Max(component => component.VerticalSampling);
        foreach (var component in components)
        {
            // The walker rejects sampling factors that do not divide the maximum factors
            component.SetGeometry(structure.Width, structure.Height, maxHorizontal, maxVertical);
        }

        _components = components;
        _maxVertical = maxVertical;
        _mcusX = (structure.Width + (_dataUnitSize * maxHorizontal) - 1) / (_dataUnitSize * maxHorizontal);
        _mcusY = (structure.Height + (_dataUnitSize * maxVertical) - 1) / (_dataUnitSize * maxVertical);

        // The container of an embedded datastream receives the rows on the calling thread, one at a time
        _workers = _receiver is null && !_lossless ? JpegReconstruction.GetWorkerCount(structure.Size, _mcusY, _context.Configuration.MaxDegreeOfParallelism) : 1;
        if (_receiver is not null)
        {
            _colorModel = _embeddedColorModel;
            _receiver.OnFrame(structure.Width, structure.Height, components.Length, _precision);
            return;
        }

        var request = _request!;
        // The rows of the writer: 8-bit samples as they are, the other ones scaled to 16 bits (16-bit color as opaque RGBA)
        var source = PixelFormatWidening.GetWidenedFormat(DefaultPixelFormats.ForJpeg(components.Length, _precision));
        var profile = structure.Metadata.IccProfile;
        if (components.Length == 4)
        {
            // The walker only adopts a CMYK profile for four components. The rows are converted to the destination
            // profile, which is the one that labels the image
            var conversion = request.CmykConversion ?? throw Unsupported("CMYK and YCCK JPEG images are only loaded with a color conversion: set the CmykConversion property of the decode or reader options.", "JPEG CMYK/YCCK");
            var transform = conversion.CreateTransform(profile) ?? throw Unsupported("The CMYK JPEG image has no CMYK color profile and CmykConversionOptions.AssumedSourceProfile is null.", "JPEG CMYK without color profile");
            _inkConversion = transform.Pipeline;
            profile = conversion.DestinationProfile;
        }

        _sink = DecodedFrameSink.Create(_context, request, ImageFormat.Jpeg, structure.Size, structure.DefaultPixelFormat, source, profile);
        _sink.BeginFrame(FrameDuration.Zero);
        _sink.SetPartialFrameRenderer(this);
    }

    public override void OnEntropyData(ReadOnlySpan<byte> data)
    {
        var scan = _scan ?? throw new InvalidOperationException("No JPEG scan was started.");
        scan.Feed(data);
    }

    public override bool OnScanEnd()
    {
        var scan = _scan ?? throw new InvalidOperationException("No JPEG scan was started.");
        scan.Finish();
        _scan = null;
        foreach (var scanComponent in _scanComponents)
        {
            scanComponent.Component.IsDecoded = true;
        }

        _scanComponents = [];
        if (_progressive)
        {
            // The coefficients are complete at EOI only: a rendering shows the ones decoded so far
            _completedScans++;
            _hasAcCoefficients |= _scanHasAc;
            if (_sink is { WantsRefinement: true } sink && _components.All(component => component.IsDecoded))
            {
                // A rendering counts like the pixels of a frame that is not shown
                var structure = _structure!;
                _context.Tracker.ChargeHiddenPixels((long)structure.Width * structure.Height);
                RenderCoefficients(sink);
            }

            return true;
        }

        if (_components.All(component => component.IsDecoded))
        {
            // Streaming: the last MCU row (no lower neighbor) is still pending; several scans: every row is written now
            var writer = _writer!;
            if (_reconstruction is { } reconstruction)
            {
                reconstruction.Reconstruct(_mcusY);
            }
            else if (_writers.Length > 1)
            {
                JpegBandWriter.WriteBands(_writers, _mcusY, _context.CancellationToken);
            }
            else
            {
                while (writer.BandsWritten < _mcusY)
                {
                    writer.WriteNextBand(_context.CancellationToken);
                }
            }

            ReleaseDecoderState();
        }

        return true;
    }

    public override void OnEnd(JpegStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        foreach (var component in _components)
        {
            if (!component.IsDecoded)
                throw Invalid(_progressive
                    ? string.Create(CultureInfo.InvariantCulture, $"The JPEG data has no DC scan for the component {component.Id}.")
                    : string.Create(CultureInfo.InvariantCulture, $"The JPEG data has no scan for the component {component.Id}."));
        }

        if (_progressive)
        {
            _context.Sequential?.SetFramePassCount(_completedScans);
            WriteProgressiveFrame();
        }

        if (_receiver is not null)
            return;

        var sink = _sink ?? throw new InvalidOperationException("The JPEG header was not parsed.");
        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The JPEG file was not decoded.");
        var sink = _sink ?? throw new InvalidOperationException("An embedded JPEG datastream is not an image.");
        return sink.Build(structure.Metadata, animation: null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseDecoderState();
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Jpeg);

    private static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Jpeg, feature);

    private void ReleaseDecoderState()
    {
        // Planes, coefficients, entropy buffer and rows
        _scan = null;
        _reconstruction = null;
        DisposeWriters();
        _reader?.Dispose();
        _reader = null;
        foreach (var component in _components)
        {
            component.Dispose();
        }
    }

    /// <summary>Parses the tables of a DQT segment (T.81 B.2.4.1): precision 0 (8-bit) or 1 (16-bit), identifier 0-3, 64 nonzero values in zig-zag order.</summary>
    private void ParseQuantizationTables(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw Invalid("The JPEG DQT segment is empty.");

        var zigZag = JpegIdct.ZigZag;
        while (!payload.IsEmpty)
        {
            var precision = payload[0] >> 4;
            var id = payload[0] & 0x0F;
            if (precision > 1)
                throw Invalid("The JPEG quantization table precision is not 8 or 16 bits.");

            if (id > 3)
                throw Invalid("The JPEG quantization table identifier is greater than 3.");

            var length = 1 + (64 * (precision + 1));
            if (payload.Length < length)
                throw Invalid("The JPEG DQT segment is truncated.");

            // A new array: scans that already captured the previous table keep it
            var table = new ushort[64];
            for (var k = 0; k < 64; k++)
            {
                var value = precision == 0 ? payload[1 + k] : BinaryPrimitives.ReadUInt16BigEndian(payload[(1 + (2 * k))..]);
                if (value == 0)
                    throw Invalid("The JPEG quantization table contains a zero value.");

                table[zigZag[k]] = value;
            }

            _quantizationTables[id] = table;
            payload = payload[length..];
        }
    }

    /// <summary>Parses the tables of a DHT segment (T.81 B.2.4.2): class 0 (DC) or 1 (AC), identifier 0-3, 16 code counts and the symbols.</summary>
    private void ParseHuffmanTables(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw Invalid("The JPEG DHT segment is empty.");

        while (!payload.IsEmpty)
        {
            if (payload.Length < 17)
                throw Invalid("The JPEG DHT segment is truncated.");

            var tableClass = payload[0] >> 4;
            var id = payload[0] & 0x0F;
            if (tableClass > 1)
                throw Invalid("The JPEG Huffman table class is not 0 (DC) or 1 (AC).");

            if (id > 3)
                throw Invalid("The JPEG Huffman table identifier is greater than 3.");

            var counts = payload.Slice(1, 16);
            var total = 0;
            foreach (var count in counts)
            {
                total += count;
            }

            if (payload.Length < 17 + total)
                throw Invalid("The JPEG DHT segment is truncated.");

            var table = JpegHuffmanTable.TryCreate(counts, payload.Slice(17, total), out var error, ac: tableClass == 1) ?? throw Invalid(error!);
            (tableClass == 0 ? _dcTables : _acTables)[id] = table;
            payload = payload[(17 + total)..];
        }
    }

    /// <summary>Parses the tables of a DAC segment (T.81 B.2.4.3): class 0 (DC) or 1 (AC), identifier 0-3 and the conditioning value.</summary>
    /// <remarks>
    /// A segment may define no table, unlike a DQT or a DHT segment: the JPEG reference software writes such a segment before
    /// each scan that uses no conditioning (the refinement scans of a progressive frame), and other decoders read it.
    /// </remarks>
    private void ParseArithmeticConditioning(ReadOnlySpan<byte> payload)
    {
        if (payload.Length % 2 != 0)
            throw Invalid("The JPEG DAC segment does not hold a whole number of conditioning tables.");

        for (; !payload.IsEmpty; payload = payload[2..])
        {
            var tableClass = payload[0] >> 4;
            var id = payload[0] & 0x0F;
            var value = payload[1];
            if (tableClass > 1)
                throw Invalid("The JPEG arithmetic conditioning table class is not 0 (DC) or 1 (AC).");

            if (id > 3)
                throw Invalid("The JPEG arithmetic conditioning table identifier is greater than 3.");

            if (tableClass == 0)
            {
                // The lower bound in the low bits, the upper one in the high bits
                var lower = value & 0x0F;
                var upper = value >> 4;
                if (lower > upper)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG DC conditioning bounds are inverted (L = {lower}, U = {upper})."));

                _dcConditioning[id] = (lower, upper);
            }
            else
            {
                if (value is < 1 or > 63)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG AC conditioning threshold {value} is not between 1 and 63."));

                _acConditioning[id] = value;
            }
        }
    }

    /// <summary>
    /// Gets the conditioning of the components of an arithmetic-coded scan: the components that select the same table share
    /// its statistics area.
    /// </summary>
    /// <param name="selectors">The table selectors of each component: the DC table in the high bits, the AC table in the low bits.</param>
    /// <param name="dc">Whether the scan codes DC differences.</param>
    /// <param name="ac">Whether the scan codes AC coefficients.</param>
    private JpegArithmeticConditioning[] CreateArithmeticConditioning(ReadOnlySpan<byte> selectors, bool dc, bool ac)
    {
        var dcStatistics = new byte[]?[4];
        var acStatistics = new byte[]?[4];
        var result = new JpegArithmeticConditioning[selectors.Length];
        for (var i = 0; i < selectors.Length; i++)
        {
            var dcId = selectors[i] >> 4;
            var acId = selectors[i] & 0x0F;
            if ((dc && dcId > 3) || (ac && acId > 3))
                throw Invalid("A JPEG scan refers to an arithmetic conditioning table identifier greater than 3.");

            // The selector of a table the scan does not use has no meaning
            var (lower, upper) = dc ? _dcConditioning[dcId] : default;
            result[i] = new JpegArithmeticConditioning(
                dc ? dcStatistics[dcId] ??= new byte[JpegArithmeticConditioning.DcBinCount] : [],
                ac ? acStatistics[acId] ??= new byte[JpegArithmeticConditioning.AcBinCount] : [],
                lower,
                upper,
                ac ? _acConditioning[acId] : 0);
        }

        return result;
    }

    private void StartScan(ReadOnlySpan<byte> payload)
    {
        if (_progressive)
        {
            StartProgressiveScan(payload);
            return;
        }

        if (_lossless)
        {
            StartLosslessScan(payload);
            return;
        }

        // The walker validated the length, the component count and that every component exists once
        var count = payload[0];
        var scanComponents = new JpegScanDecoder.ScanComponent[count];
        Span<byte> selectors = stackalloc byte[count];
        for (var i = 0; i < count; i++)
        {
            selectors[i] = payload[2 + (2 * i)];
        }

        var conditioning = _arithmetic ? CreateArithmeticConditioning(selectors, dc: true, ac: true) : null;
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var tables = payload[2 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            if (component.IsDecoded)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} is coded in several sequential scans."));

            var quantization = _quantizationTables[component.QuantizationTable] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} refers to the undefined quantization table {component.QuantizationTable}."));
            if (conditioning is not null)
            {
                scanComponents[i] = new JpegScanDecoder.ScanComponent(component, dcTable: null, acTable: null, quantization) { Arithmetic = conditioning[i] };
                continue;
            }

            var dcId = tables >> 4;
            var acId = tables & 0x0F;
            if (dcId > 3 || acId > 3)
                throw Invalid("A JPEG scan refers to a Huffman table identifier greater than 3.");

            var dc = _dcTables[dcId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined DC Huffman table {dcId}."));
            var ac = _acTables[acId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined AC Huffman table {acId}."));
            scanComponents[i] = new JpegScanDecoder.ScanComponent(component, dc, ac, quantization);
        }

        // Ss, Se, Ah and Al have no meaning for sequential scans and are ignored, as by common decoders
        if (_scanCount == 0)
        {
            AllocateDecoderState(streaming: count == _components.Length);
        }

        _scanCount++;
        var reader = _reader ?? throw new InvalidOperationException("The JPEG decoder state is not allocated.");
        _scanComponents = scanComponents;
        if (_scanCount == 1 && _streaming && _workers > 1 && !_arithmetic)
        {
            // The blocks are transformed by the workers, a batch of MCU rows at a time
            var quantization = new ushort[_components.Length][];
            foreach (var scanComponent in scanComponents)
            {
                quantization[scanComponent.Component.Index] = scanComponent.Quantization;
            }

            _reconstruction = new JpegReconstruction(_components, _writers, quantization, _mcusY, JpegReconstruction.GetBatchRows(_workers, _mcusY), _context.CancellationToken);
        }

        _scan = _arithmetic
            ? new JpegArithmeticSequentialScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken)
            : new JpegSequentialScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken, deferTransforms: _reconstruction is not null);
        PrepareFastAc(scanComponents);
    }

    /// <summary>
    /// Starts a scan of a lossless frame (T.81 H.1.2; the walker validated the predictor and the point transform). A
    /// component is coded by one scan, with the DC table it selects and no quantization table.
    /// </summary>
    private void StartLosslessScan(ReadOnlySpan<byte> payload)
    {
        var count = payload[0];
        var parameters = payload[(1 + (2 * count))..];
        var scanComponents = new JpegScanDecoder.ScanComponent[count];
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            if (component.IsDecoded)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} is coded in several lossless scans."));

            var dcId = payload[2 + (2 * i)] >> 4;
            if (dcId > 3)
                throw Invalid("A JPEG scan refers to a Huffman table identifier greater than 3.");

            var dc = _dcTables[dcId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined DC Huffman table {dcId}."));
            scanComponents[i] = new JpegScanDecoder.ScanComponent(component, dc, acTable: null, quantization: []);
        }

        if (_scanCount == 0)
        {
            AllocateDecoderState(streaming: count == _components.Length);
            foreach (var component in _components)
            {
                component.AllocateDifferences(_context.Scope, _context.Limits);
            }
        }

        _scanCount++;
        var reader = _reader ?? throw new InvalidOperationException("The JPEG decoder state is not allocated.");
        _scanComponents = scanComponents;
        _scan = new JpegLosslessScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, predictor: parameters[0], pointTransform: parameters[2] & 0x0F, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken);
    }

    /// <summary>
    /// Starts a progressive scan (T.81 G.1.1.1; the walker validated <c>Ss</c>, <c>Se</c>, <c>Ah</c>, <c>Al</c> and the
    /// coefficient progression). A DC first scan needs the DC tables of its components, a DC refinement none, an AC scan the
    /// AC table of its only component; the quantization table of a component is latched at its first scan. An
    /// arithmetic-coded scan needs the conditioning of the same tables instead.
    /// </summary>
    private void StartProgressiveScan(ReadOnlySpan<byte> payload)
    {
        var count = payload[0];
        var parameters = payload[(1 + (2 * count))..];
        var start = parameters[0];
        var end = parameters[1];
        var high = parameters[2] >> 4;
        var low = parameters[2] & 0x0F;
        var scanComponents = new JpegScanDecoder.ScanComponent[count];
        Span<byte> selectors = stackalloc byte[count];
        for (var i = 0; i < count; i++)
        {
            selectors[i] = payload[2 + (2 * i)];
        }

        var conditioning = _arithmetic ? CreateArithmeticConditioning(selectors, dc: start == 0 && high == 0, ac: start > 0) : null;
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var tables = payload[2 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            JpegHuffmanTable? dc = null;
            JpegHuffmanTable? ac = null;
            if (conditioning is not null)
            {
                // No Huffman table
            }
            else if (start == 0 && high == 0)
            {
                var dcId = tables >> 4;
                if (dcId > 3)
                    throw Invalid("A JPEG scan refers to a Huffman table identifier greater than 3.");

                dc = _dcTables[dcId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined DC Huffman table {dcId}."));
            }
            else if (start > 0)
            {
                var acId = tables & 0x0F;
                if (acId > 3)
                    throw Invalid("A JPEG scan refers to a Huffman table identifier greater than 3.");

                ac = _acTables[acId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined AC Huffman table {acId}."));
            }

            component.LatchedQuantization ??= _quantizationTables[component.QuantizationTable] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} refers to the undefined quantization table {component.QuantizationTable}."));
            scanComponents[i] = new JpegScanDecoder.ScanComponent(component, dc, ac, component.LatchedQuantization) { Arithmetic = conditioning?[i] };
        }

        if (_scanCount == 0)
        {
            AllocateProgressiveState();
        }

        _scanCount++;
        _scanHasAc = start > 0;
        var reader = _reader ?? throw new InvalidOperationException("The JPEG decoder state is not allocated.");

        // Only a DC first scan makes a component decodable: its other coefficients may legitimately stay zero
        _scanComponents = start == 0 && high == 0 ? scanComponents : [];
        _scan = _arithmetic
            ? new JpegArithmeticProgressiveScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, start, end, high, low, _context.CancellationToken)
            : new JpegProgressiveScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, start, end, high, low, _context.CancellationToken);
        PrepareFastAc(scanComponents);
    }

    /// <summary>
    /// Builds the table of the AC coefficients that one lookup decodes (<see cref="JpegHuffmanTable.FastAc"/>) for the AC
    /// tables of a scan. It costs more than it saves on small images, whose coefficients all take the regular path.
    /// </summary>
    private void PrepareFastAc(JpegScanDecoder.ScanComponent[] scanComponents)
    {
        if (_arithmetic || (long)_structure!.Width * _structure.Height < FastAcMinimumPixels)
            return;

        foreach (var scanComponent in scanComponents)
        {
            scanComponent.AcTable?.PrepareFastAc();
        }
    }

    private void AllocateProgressiveState()
    {
        var scope = _context.Scope;
        foreach (var component in _components)
        {
            component.AllocateCoefficients(scope, _mcusX, _mcusY, _context.Limits);
        }

        _reader = new JpegEntropyReader(scope, Math.Max(EntropyBufferCapacity, 2 * JpegScanDecoder.GetRequiredBufferCapacity(10, _precision, _arithmetic)));
    }

    /// <summary>
    /// Reconstructs a progressive frame at EOI: the coefficients of each MCU row are dequantized and transformed into a ring
    /// of three MCU rows of samples (the same numerical contract as sequential decoding), and each band is written once its lower context row
    /// exists, as for a streamed sequential frame. The entropy buffer is released first, the coefficients last.
    /// </summary>
    private void WriteProgressiveFrame()
    {
        _reader?.Dispose();
        _reader = null;
        WriteCoefficients();
        ReleaseDecoderState();
    }

    /// <summary>The frame as far as it was decoded, when the input ends before it.</summary>
    public bool TryRenderPartialFrame()
    {
        if (_sink is not { } sink || _scanCount == 0)
            return false;

        if (_progressive)
        {
            // A component that no scan coded yet has no quantization table to transform its coefficients with
            if (_components.Any(component => component.LatchedQuantization is null))
                return false;

            RenderCoefficients(sink);
            return true;
        }

        var structure = _structure!;
        int writtenRows;
        if (_writer is { } writer && _streaming)
        {
            // The rows that one worker would have written: every band but the last one decoded
            _reconstruction?.Reconstruct(_decodedRows);
            writtenRows = Math.Min(structure.Height, writer.BandsWritten * _dataUnitSize * _maxVertical);
        }
        else if (_writer is null && _components.All(component => component.IsDecoded))
        {
            // Every scan was decoded and every row written
            writtenRows = structure.Height;
        }
        else
        {
            // The scans of the components are decoded one after the other: no row is written before the last one
            return false;
        }

        using (var lease = sink.LeaseCurrentFrame())
        {
            for (var y = writtenRows; y < structure.Height; y++)
            {
                lease.GetRowBytes(y).Clear();
            }
        }

        sink.EndRefinement(downsampling: 1, passCount: 0);
        return true;
    }

    /// <summary>Writes a rendering of the coefficients of a progressive frame decoded so far.</summary>
    private void RenderCoefficients(DecodedFrameSink sink)
    {
        sink.BeginRefinement(FrameDuration.Zero);
        WriteCoefficients();

        // Only the DC coefficients: one value per block of 8x8 samples
        sink.EndRefinement(_hasAcCoefficients ? 1 : 8, _completedScans);
    }

    /// <summary>
    /// Transforms the coefficients of a progressive frame one MCU row at a time into a ring of three MCU rows of samples
    /// and writes every row; the coefficients are kept.
    /// </summary>
    private void WriteCoefficients()
    {
        var scope = _context.Scope;
        var batchRows = JpegReconstruction.GetBatchRows(_workers, _mcusY);
        var bandCount = _workers > 1 ? JpegReconstruction.GetPlaneBands(batchRows, _mcusY) : Math.Min(3, _mcusY);
        foreach (var component in _components)
        {
            component.AllocatePlane(scope, _mcusX, bandCount, _context.Limits);
        }

        CreateRowWriters(scope, _workers);
        var writer = _writer!;
        if (_workers > 1)
        {
            var quantization = Array.ConvertAll(_components, component => component.LatchedQuantization!);
            var reconstruction = new JpegReconstruction(_components, _writers, quantization, _mcusY, JpegReconstruction.GetBatchRows(_workers, _mcusY), _context.CancellationToken);
            for (var row = 0; row < _mcusY; row = reconstruction.TransformedRows)
            {
                reconstruction.Reconstruct(Math.Min(_mcusY, row + batchRows));
            }
        }
        else
        {
            var block = new double[64];
            var scales = new double[64];
            for (var row = 0; row < _mcusY; row++)
            {
                foreach (var component in _components)
                {
                    JpegReconstruction.TransformMcuRow(component, row, component.LatchedQuantization!, block, scales);
                }

                if (row > 0)
                {
                    writer.WriteNextBand(_context.CancellationToken);
                }
            }

            writer.WriteNextBand(_context.CancellationToken);
        }

        DisposeWriters();
        foreach (var component in _components)
        {
            component.ReleasePlane();
        }
    }

    private void DisposeWriters()
    {
        // The first writer of the workers is _writer
        for (var i = 1; i < _writers.Length; i++)
        {
            _writers[i].Dispose();
        }

        _writer?.Dispose();
        _writer = null;
        _writers = [];
    }

    private void AllocateDecoderState(bool streaming)
    {
        var scope = _context.Scope;
        _streaming = streaming;

        // Several workers transform the blocks of a streamed Huffman-coded frame from a ring of coefficients, and write the
        // rows of the frames coded in several scans
        var deferred = streaming && _workers > 1 && !_arithmetic;
        var batchRows = JpegReconstruction.GetBatchRows(_workers, _mcusY);
        var bandCount = !streaming ? _mcusY : deferred ? JpegReconstruction.GetPlaneBands(batchRows, _mcusY) : Math.Min(3, _mcusY);
        foreach (var component in _components)
        {
            component.AllocatePlane(scope, _mcusX, bandCount, _context.Limits);
            if (deferred)
            {
                component.AllocateCoefficients(scope, _mcusX, batchRows, _context.Limits, clear: false);
            }
        }

        _reader = new JpegEntropyReader(scope, Math.Max(EntropyBufferCapacity, 2 * JpegScanDecoder.GetRequiredBufferCapacity(10, _precision, _arithmetic, _lossless)));
        CreateRowWriters(scope, !streaming || deferred ? _workers : 1);
    }

    /// <summary>Creates the writer of the rows, and the ones of the other workers when several write them.</summary>
    private void CreateRowWriters(AllocationScope scope, int count)
    {
        _writer = CreateRowWriter(scope);
        if (count == 1)
            return;

        var writers = new JpegBandWriter[count];
        writers[0] = _writer;
        try
        {
            for (var i = 1; i < count; i++)
            {
                writers[i] = CreateRowWriter(scope);
            }
        }
        catch
        {
            foreach (var writer in writers)
            {
                writer?.Dispose();
            }

            _writer = null;
            throw;
        }

        _writers = writers;
    }

    private JpegBandWriter CreateRowWriter(AllocationScope scope)
    {
        var structure = _structure!;
        var bandRows = _dataUnitSize * _maxVertical;
        if (_precision != 8)
        {
            return _receiver is not null
                ? new JpegDeepRowWriter(_components, _receiver, _colorModel, structure.Width, structure.Height, bandRows, _precision, scope)
                : new JpegDeepRowWriter(_components, _sink!, _colorModel, _inkConversion, structure.Width, structure.Height, bandRows, _precision, scope);
        }

        return _receiver is not null
            ? new JpegRowWriter(_components, _receiver, _colorModel, structure.Width, structure.Height, bandRows, scope)
            : new JpegRowWriter(_components, _sink!, _colorModel, _inkConversion, structure.Width, structure.Height, bandRows, scope);
    }

    /// <summary>Streaming: once MCU row <paramref name="row"/> is decoded, the previous one has its lower context row and is written.</summary>
    private void OnMcuRowDecoded(int row)
    {
        if (_reconstruction is { } reconstruction)
        {
            // The ring of coefficients is full: its rows are transformed, and the bands that have their lower context row are written
            _decodedRows = row + 1;
            if (_decodedRows - reconstruction.TransformedRows == reconstruction.BatchRows)
            {
                reconstruction.Reconstruct(_decodedRows);
            }

            return;
        }

        if (row > 0)
        {
            _writer!.WriteNextBand(_context.CancellationToken);
        }
    }
}
