using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Huffman JPEG pixel decoder plugged into the <see cref="JpegStructureParser"/> marker walk, shared by eager loads
/// and sequential readers: sequential frames (baseline SOF0 and extended
/// sequential SOF1) and progressive frames (SOF2), of 8-bit or 12-bit samples. The walker validates the container (markers, segment lengths,
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
/// The image (one frame, duration zero) ends at EOI after <see cref="DecodedFrameSink.UpdateMetadata"/>, so reader frames
/// carry the metadata found after the scans like eager loads. EXIF orientation is metadata only: pixels are never rotated.
/// </description></item>
/// <item><description>
/// Four-component frames (CMYK, YCCK) are inks: no pixel format holds them, so they are decoded only when the request
/// carries a <see cref="CmykConversionOptions"/>. The rows are converted to RGB by an ICC conversion from the CMYK profile
/// of the file (or the assumed one) to the destination profile, which labels the image: the pixels and their profile
/// change together, and the CMYK profile is not kept. Without the options, or without any source profile, the frame is
/// an <see cref="UnsupportedImageFeatureException"/> raised before any allocation.
/// </description></item>
/// <item><description>
/// Samples of 12 bits are kept in 16-bit planes and written scaled to 16 bits (<see cref="JpegDeepRowWriter"/>):
/// <see cref="PixelFormat.Gray16"/> for one component, opaque <see cref="PixelFormat.Rgba64"/> otherwise. A datastream
/// embedded in another format only has 8-bit samples: 12-bit ones are an <see cref="UnsupportedImageFeatureException"/>.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegDecoder : JpegDecodeObserver
{
    private const byte MarkerDht = 0xC4;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerDqt = 0xDB;
    private const byte MarkerDri = 0xDD;
    private const int EntropyBufferCapacity = 64 * 1024;

    private readonly ImageDecodeRequest? _request;
    private readonly ImageCodecContext _context;

    // The container of an embedded datastream and the meaning it gives to the components; null for an image
    private readonly JpegSampleReceiver? _receiver;
    private readonly ImageColorModel _embeddedColorModel;
    private readonly JpegHuffmanTable?[] _dcTables = new JpegHuffmanTable?[4];
    private readonly JpegHuffmanTable?[] _acTables = new JpegHuffmanTable?[4];
    private readonly ushort[]?[] _quantizationTables = new ushort[]?[4];
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
    private int _scanCount;
    private JpegEntropyReader? _reader;
    private JpegBandWriter? _writer;
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
        _precision = structure.Precision;
        _colorModel = structure.ColorModel;

        // The container of an embedded datastream is handed rows of bytes
        if (_receiver is not null && _precision != 8)
            throw Unsupported("An embedded JPEG datastream with 12-bit samples is not supported.", "JPEG 12-bit precision");

        var frameComponents = structure.Components;
        var components = new JpegFrameComponent[frameComponents.Count];
        for (var i = 0; i < components.Length; i++)
        {
            var component = frameComponents[i];

            // The only component of a grayscale frame is coded in a non-interleaved scan: its sampling factors play no role
            components[i] = components.Length == 1
                ? new JpegFrameComponent(i, component.Id, 1, 1, component.QuantizationTable, _precision)
                : new JpegFrameComponent(i, component.Id, component.HorizontalSampling, component.VerticalSampling, component.QuantizationTable, _precision);
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
        _mcusX = (structure.Width + (8 * maxHorizontal) - 1) / (8 * maxHorizontal);
        _mcusY = (structure.Height + (8 * maxVertical) - 1) / (8 * maxVertical);
        if (_receiver is not null)
        {
            _colorModel = _embeddedColorModel;
            _receiver.OnFrame(structure.Width, structure.Height, components.Length);
            return;
        }

        var request = _request!;
        // The rows of the writer: 8-bit samples as they are, deeper ones scaled to 16 bits (no 16-bit RGB format: opaque RGBA)
        var source = DefaultPixelFormats.ForJpeg(components.Length, _precision);
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
            return true; // The coefficients are complete at EOI only

        if (_components.All(component => component.IsDecoded))
        {
            // Streaming: the last MCU row (no lower neighbor) is still pending; several scans: every row is written now
            var writer = _writer!;
            while (writer.BandsWritten < _mcusY)
            {
                writer.WriteNextBand(_context.CancellationToken);
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
        _writer?.Dispose();
        _writer = null;
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

            var table = JpegHuffmanTable.TryCreate(counts, payload.Slice(17, total), out var error) ?? throw Invalid(error!);
            (tableClass == 0 ? _dcTables : _acTables)[id] = table;
            payload = payload[(17 + total)..];
        }
    }

    private void StartScan(ReadOnlySpan<byte> payload)
    {
        if (_progressive)
        {
            StartProgressiveScan(payload);
            return;
        }

        // The walker validated the length, the component count and that every component exists once
        var count = payload[0];
        var scanComponents = new JpegScanDecoder.ScanComponent[count];
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var tables = payload[2 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            if (component.IsDecoded)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} is coded in several sequential scans."));

            var dcId = tables >> 4;
            var acId = tables & 0x0F;
            if (dcId > 3 || acId > 3)
                throw Invalid("A JPEG scan refers to a Huffman table identifier greater than 3.");

            var dc = _dcTables[dcId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined DC Huffman table {dcId}."));
            var ac = _acTables[acId] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the undefined AC Huffman table {acId}."));
            var quantization = _quantizationTables[component.QuantizationTable] ?? throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} refers to the undefined quantization table {component.QuantizationTable}."));
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
        _scan = new JpegSequentialScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken);
    }

    /// <summary>
    /// Starts a progressive scan (T.81 G.1.1.1; the walker validated <c>Ss</c>, <c>Se</c>, <c>Ah</c>, <c>Al</c> and the
    /// coefficient progression). A DC first scan needs the DC tables of its components, a DC refinement none, an AC scan the
    /// AC table of its only component; the quantization table of a component is latched at its first scan.
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
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var tables = payload[2 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            JpegHuffmanTable? dc = null;
            JpegHuffmanTable? ac = null;
            if (start == 0 && high == 0)
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
            scanComponents[i] = new JpegScanDecoder.ScanComponent(component, dc, ac, component.LatchedQuantization);
        }

        if (_scanCount == 0)
        {
            AllocateProgressiveState();
        }

        _scanCount++;
        var reader = _reader ?? throw new InvalidOperationException("The JPEG decoder state is not allocated.");

        // Only a DC first scan makes a component decodable: its other coefficients may legitimately stay zero
        _scanComponents = start == 0 && high == 0 ? scanComponents : [];
        _scan = new JpegProgressiveScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, start, end, high, low, _context.CancellationToken);
    }

    private void AllocateProgressiveState()
    {
        var scope = _context.Scope;
        foreach (var component in _components)
        {
            component.AllocateCoefficients(scope, _mcusX, _mcusY, _context.Limits);
        }

        _reader = new JpegEntropyReader(scope, Math.Max(EntropyBufferCapacity, 2 * JpegScanDecoder.GetRequiredBufferCapacity(10, _precision)));
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
        var scope = _context.Scope;
        var bandCount = Math.Min(3, _mcusY);
        foreach (var component in _components)
        {
            component.AllocatePlane(scope, _mcusX, bandCount, _context.Limits);
        }

        _writer = CreateRowWriter(scope);
        var block = new double[64];
        for (var row = 0; row < _mcusY; row++)
        {
            foreach (var component in _components)
            {
                TransformBlockRows(component, row, block);
            }

            if (row > 0)
            {
                _writer.WriteNextBand(_context.CancellationToken);
            }
        }

        _writer.WriteNextBand(_context.CancellationToken);
        ReleaseDecoderState();
    }

    /// <summary>Dequantizes and transforms the blocks of one MCU row of a component into its sample plane.</summary>
    private static void TransformBlockRows(JpegFrameComponent component, int mcuRow, double[] block)
    {
        var quantization = component.LatchedQuantization!;
        var stride = component.Stride;
        var precision = component.Precision;
        for (var blockRow = mcuRow * component.VerticalSampling; blockRow < (mcuRow + 1) * component.VerticalSampling; blockRow++)
        {
            var rowOffset = component.GetRowOffset(blockRow * 8);
            for (var blockColumn = 0; blockColumn < component.CoefficientBlocksPerLine; blockColumn++)
            {
                var coefficients = component.GetCoefficients(blockRow, blockColumn);
                var offset = rowOffset + (blockColumn * 8);
                if (coefficients[1..].IndexOfAnyExcept((short)0) < 0)
                {
                    var dc = (long)coefficients[0] * quantization[0];
                    if (precision > 8)
                    {
                        JpegIdct.TransformDeepDcOnly(dc, component.DeepSamples[offset..], stride, precision);
                    }
                    else
                    {
                        JpegIdct.TransformDcOnly(dc, component.Samples.AsSpan(offset), stride);
                    }

                    continue;
                }

                for (var i = 0; i < 64; i++)
                {
                    block[i] = coefficients[i] * (double)quantization[i];
                }

                if (precision > 8)
                {
                    JpegIdct.TransformDeep(block, component.DeepSamples[offset..], stride, precision);
                }
                else
                {
                    JpegIdct.Transform(block, component.Samples.AsSpan(offset), stride);
                }
            }
        }
    }

    private void AllocateDecoderState(bool streaming)
    {
        var scope = _context.Scope;
        _streaming = streaming;
        var bandCount = streaming ? Math.Min(3, _mcusY) : _mcusY;
        foreach (var component in _components)
        {
            component.AllocatePlane(scope, _mcusX, bandCount, _context.Limits);
        }

        _reader = new JpegEntropyReader(scope, Math.Max(EntropyBufferCapacity, 2 * JpegScanDecoder.GetRequiredBufferCapacity(10, _precision)));
        _writer = CreateRowWriter(scope);
    }

    private JpegBandWriter CreateRowWriter(AllocationScope scope)
    {
        var structure = _structure!;
        if (_precision > 8)
            return new JpegDeepRowWriter(_components, _sink!, _colorModel, _inkConversion, structure.Width, structure.Height, _maxVertical, _precision, scope);

        return _receiver is not null
            ? new JpegRowWriter(_components, _receiver, _colorModel, structure.Width, structure.Height, _maxVertical, scope)
            : new JpegRowWriter(_components, _sink!, _colorModel, _inkConversion, structure.Width, structure.Height, _maxVertical, scope);
    }

    /// <summary>Streaming: once MCU row <paramref name="row"/> is decoded, the previous one has its lower context row and is written.</summary>
    private void OnMcuRowDecoded(int row)
    {
        if (row > 0)
        {
            _writer!.WriteNextBand(_context.CancellationToken);
        }
    }
}
