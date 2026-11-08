using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG pixel decoder plugged into the <see cref="JpegStructureParser"/> marker walk, shared by eager loads
/// and sequential readers: sequential frames (baseline SOF0 and extended
/// sequential SOF1) and progressive frames (SOF2), of 8-bit or 12-bit samples, Huffman coded or arithmetic coded (SOF9
/// and SOF10), and lossless frames (SOF3, or SOF11 with arithmetic coding) of 2-bit to 16-bit samples. The walker validates the container (markers, segment lengths,
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
/// Lenient decoding (<see cref="ImageCodecContext.IsLenient"/>, JPEG files only): a restart marker of a sequential or a
/// lossless scan whose number is 3 to 5 after the expected one is read as the expected marker
/// (<see cref="JpegScanDecoder.AcceptsGarbledRestartNumbers"/>); Apple ImageIO rejects such a marker in a progressive
/// scan, so it stays an error there. The structure parser also ends a complete frame of Huffman-coded scans that has no
/// EOI marker (<see cref="JpegDecodeObserver.OnScanEndAtEndOfInput"/>).
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
/// <item><description>
/// JPEG-LS frames (ITU-T T.87, SOF55) have the geometry of the lossless frames, and full planes: the entropy-coded data of
/// each scan is buffered and decoded at its end (<see cref="JpegLsScanDecoder"/>), with the coding parameters of the last
/// LSE segment of type 1 before it, and the rows are written once every component is decoded. The HP color transforms
/// (APP8 <c>mrfx</c>, from HP's LOCO-I implementation, which CharLS writes) of three 8-bit components of an interleaved
/// scan are undone before.
/// </description></item>
/// <item><description>
/// Hierarchical images (T.81 Annex J): each frame is decoded with the procedures of its process into full planes (no row
/// is streamed, and no rendering is given), then reconstructed into a reference plane per component (16-bit samples,
/// charged as decoder state): the samples of the first frame, then for each differential frame its differences added to
/// the reference of the previous frame, expanded by 2 horizontally or vertically (the new samples are the floored means
/// of their two neighbors, the last one repeated, horizontally first; J.1.1.2), and clamped to the samples. The blocks
/// of a differential DCT-based frame have no DC prediction and an inverse DCT without level shift or clamping (planes of
/// <c>P + 1</c> bits offset by <c>2^P</c>); the differences of a differential lossless frame are not predicted, and are
/// added modulo 2^16. The frames before the last one count as pixels that are not shown. At EOI, the reference of the last
/// frame, which has the size of the image, is written like the samples of a lossless frame.
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
    private const byte MarkerJpegLsExtension = 0xF8;
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
    private bool _jpegLs;

    // Hierarchical images: whether the image is one, whether the current frame is a differential frame, its size and the
    // expansion of its reference, and the reconstruction of the previous frames (one 16-bit plane per component)
    private bool _hierarchical;
    private bool _differential;
    private int _frameWidth;
    private int _frameHeight;
    private bool _expandsHorizontally;
    private bool _expandsVertically;
    private PooledBuffer?[] _references = [];
    private (int Width, int Height)[] _referenceSizes = [];

    // JPEG-LS: the preset coding parameters of the last LSE segment (zero for the defaults), the scan being decoded and its components
    private (int MaxValue, int T1, int T2, int T3, int Reset) _jpegLsPreset;
    private JpegLsScanDecoder? _jpegLsScan;
    private JpegFrameComponent[] _jpegLsComponents = [];

    // The number of samples of a side of a data unit: 8, or 1 for a lossless frame
    private int _dataUnitSize = 8;

    // Scaled decoding: the factor by which the image is reduced, the image rows of a data unit of the largest sampling
    // factor (8, 4, 2 or 1; 1 for a lossless frame), and the size of the image
    private int _scaleDenominator = 1;
    private int _blockSize = 8;
    private Size _outputSize;
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

            case MarkerJpegLsExtension:
                // The walker accepts the preset coding parameters only (type 1, T.87 C.2.4.1.1)
                _jpegLsPreset = (BinaryPrimitives.ReadUInt16BigEndian(payload[1..]), BinaryPrimitives.ReadUInt16BigEndian(payload[3..]), BinaryPrimitives.ReadUInt16BigEndian(payload[5..]), BinaryPrimitives.ReadUInt16BigEndian(payload[7..]), BinaryPrimitives.ReadUInt16BigEndian(payload[9..]));
                break;

            case MarkerSos:
                StartScan(payload);
                break;

            case 0xC5 or 0xC6 or 0xC7 or 0xCD or 0xCE or 0xCF:
                // A differential frame of a hierarchical image (the walker validated its header): the previous frame ends
                if (_structure is { } structure)
                {
                    EndHierarchicalFrame(isLast: false);
                    ConfigureFrame(structure);
                }

                break;
        }
    }

    public override void OnHeaderComplete(JpegStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _hierarchical = structure.IsHierarchical;
        if (_hierarchical && _receiver is not null)
            throw Unsupported("Hierarchical JPEG datastreams embedded in another format are not supported.", "JPEG hierarchical");

        _precision = structure.Precision;
        _colorModel = structure.ColorModel;

        // Scaled decoding (before any allocation): the blocks of a DCT-based frame give 4, 2 or 1 samples a side instead of 8
        // (fewer reductions for the subsampled components, which then need less upsampling)
        if (_request is { IsScaled: true } scaledRequest)
        {
            var dctFrame = !structure.IsLossless && !structure.IsJpegLs;
            _scaleDenominator = scaledRequest.GetScaleDenominator(structure.Size, dctFrame && !_hierarchical ? 8 : 1, ImageFormat.Jpeg, _hierarchical ? "the frames of a hierarchical JPEG image are not scaled" : dctFrame ? "a JPEG frame is reduced to 1/8 of its size at most" : "a lossless JPEG frame has no DCT blocks to scale");
        }

        _outputSize = new Size(ImageDecodeRequest.GetScaledLength(structure.Width, _scaleDenominator), ImageDecodeRequest.GetScaledLength(structure.Height, _scaleDenominator));
        ConfigureFrame(structure);
        var components = _components;

        // The container of an embedded datastream receives the rows on the calling thread, one at a time
        _workers = _receiver is null && _dataUnitSize == 8 && !_hierarchical ? JpegReconstruction.GetWorkerCount(_outputSize, _mcusY, _context.Configuration.MaxDegreeOfParallelism) : 1;
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
        else if (source == PixelFormat.Rgb24 && request.PixelFormat == PixelFormat.Rgba32)
        {
            // The writer produces the requested opaque RGBA pixels itself: no conversion of the rows by the sink
            source = PixelFormat.Rgba32;
        }

        if (_scaleDenominator > 1)
        {
            // A reader describes the frames it gives
            _context.Sequential?.ScaleHeader(_outputSize);
        }

        _sink = DecodedFrameSink.Create(_context, request, ImageFormat.Jpeg, _outputSize, structure.DefaultPixelFormat, source, profile);
        _sink.BeginFrame(FrameDuration.Zero);
        _sink.SetPartialFrameRenderer(this);
    }

    /// <summary>
    /// Sets the process and the geometry of the frame whose header the walker just read: the first one, or the next frame
    /// of a hierarchical image. The samples of a differential frame are differences: those of a DCT-based frame are kept on
    /// <c>P + 1</c> bits (offset by <c>2^P</c>), those of a lossless frame on 16 bits (modulo 2^16).
    /// </summary>
    private void ConfigureFrame(JpegStructureParser structure)
    {
        _progressive = structure.IsProgressive;
        _arithmetic = structure.IsArithmetic;
        _lossless = structure.IsLossless;
        _jpegLs = structure.IsJpegLs;
        _differential = structure.IsDifferential;
        _frameWidth = structure.FrameWidth;
        _frameHeight = structure.FrameHeight;
        _expandsHorizontally = structure.ExpandsHorizontally;
        _expandsVertically = structure.ExpandsVertically;
        _scanCount = 0;
        _completedScans = 0;
        _hasAcCoefficients = false;

        // The data unit of a lossless or a JPEG-LS frame is a sample
        _dataUnitSize = _lossless || _jpegLs ? 1 : 8;
        _blockSize = _dataUnitSize / _scaleDenominator;
        var precision = !_differential ? _precision : _lossless ? 16 : _precision + 1;
        var frameComponents = structure.Components;
        var components = new JpegFrameComponent[frameComponents.Count];
        for (var i = 0; i < components.Length; i++)
        {
            var component = frameComponents[i];

            // The only component of a grayscale frame is coded in a non-interleaved scan: its sampling factors play no role
            components[i] = components.Length == 1
                ? new JpegFrameComponent(i, component.Id, 1, 1, component.QuantizationTable, precision, _dataUnitSize)
                : new JpegFrameComponent(i, component.Id, component.HorizontalSampling, component.VerticalSampling, component.QuantizationTable, precision, _dataUnitSize);
        }

        var maxHorizontal = components.Max(component => component.HorizontalSampling);
        var maxVertical = components.Max(component => component.VerticalSampling);
        foreach (var component in components)
        {
            // The walker rejects sampling factors that do not divide the maximum factors
            component.SetGeometry(_frameWidth, _frameHeight, maxHorizontal, maxVertical, _scaleDenominator);
        }

        _components = components;
        _maxVertical = maxVertical;
        _mcusX = (_frameWidth + (_dataUnitSize * maxHorizontal) - 1) / (_dataUnitSize * maxHorizontal);
        _mcusY = (_frameHeight + (_dataUnitSize * maxVertical) - 1) / (_dataUnitSize * maxVertical);
    }

    /// <summary>
    /// Ends a frame of a hierarchical image: every component must be decoded; the coefficients of a progressive frame are
    /// transformed into full planes, and the samples become the reference of the next frame (see the remarks of the class).
    /// </summary>
    /// <param name="isLast">Whether the frame is the last one, whose reference is the image (the other ones are not shown).</param>
    private void EndHierarchicalFrame(bool isLast)
    {
        foreach (var component in _components)
        {
            if (!component.IsDecoded)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A frame of the hierarchical JPEG image has no scan for the component {component.Id}."));
        }

        if (_progressive)
        {
            _reader?.Dispose();
            _reader = null;
            var block = new double[64];
            var scales = new double[64];
            foreach (var component in _components)
            {
                component.AllocatePlane(_context.Scope, _mcusX, _mcusY, _context.Limits);
            }

            for (var row = 0; row < _mcusY; row++)
            {
                foreach (var component in _components)
                {
                    JpegReconstruction.TransformMcuRow(component, row, component.LatchedQuantization!, block, scales);
                }
            }
        }

        var maxValue = (1 << _precision) - 1;
        var offset = 1 << _precision;
        var references = new PooledBuffer?[_components.Length];
        var sizes = new (int Width, int Height)[_components.Length];
        try
        {
            for (var i = 0; i < _components.Length; i++)
            {
                var component = _components[i];
                var (width, height) = (component.CodedWidth, component.CodedHeight);
                var buffer = references[i] = _context.Scope.Rent(checked(width * height * sizeof(ushort)), AllocationKind.DecoderState, clear: false);
                sizes[i] = (width, height);
                var reference = unsafe(MemoryMarshal.Cast<byte, ushort>(buffer.RawBuffer.AsSpan(0, width * height * sizeof(ushort))));
                var previous = _references.Length == 0 ? default : unsafe(MemoryMarshal.Cast<byte, ushort>(_references[i]!.RawBuffer.AsSpan()));
                var (previousWidth, previousHeight) = _referenceSizes.Length == 0 ? default : _referenceSizes[i];
                for (var y = 0; y < height; y++)
                {
                    var row = component.GetRowOffset(y);
                    var line = reference.Slice(y * width, width);
                    for (var x = 0; x < width; x++)
                    {
                        int sample = component.HasDeepSamples ? component.DeepSamples[row + x] : component.Samples[row + x];
                        if (_differential)
                        {
                            // Lossless differences are added modulo 2^16 (16-bit samples) or as signed 16-bit values
                            var expanded = GetExpandedReference(previous, previousWidth, previousHeight, x, y);
                            sample = _lossless && _precision == 16
                                ? (expanded + sample) & 0xFFFF
                                : Math.Clamp(expanded + (_lossless ? (short)sample : sample - offset), 0, maxValue);
                        }

                        line[x] = (ushort)sample;
                    }
                }
            }
        }
        catch
        {
            foreach (var reference in references)
            {
                reference?.Dispose();
            }

            throw;
        }

        ReleaseReferences();
        _references = references;
        _referenceSizes = sizes;
        ReleaseDecoderState();
        if (!isLast)
        {
            // A frame that is not shown, like the renderings of the frames of other formats
            _context.Tracker.ChargeHiddenPixels((long)_frameWidth * _frameHeight);
        }
    }

    /// <summary>
    /// Gets a sample of the reference of a differential frame (T.81 J.1.1.2, as the reference decoder computes it): the
    /// reference expanded by 2 horizontally, then vertically, as the EXP segment of the frame says. An expanded sample of an
    /// even position is the reference sample, one of an odd position the floored mean of its two neighbors, the last
    /// reference sample being repeated after the end.
    /// </summary>
    private int GetExpandedReference(ReadOnlySpan<ushort> reference, int width, int height, int x, int y)
    {
        if (!_expandsVertically)
            return GetHorizontallyExpandedReference(reference, width, Math.Min(y, height - 1), x);

        var line = y >> 1;
        var top = GetHorizontallyExpandedReference(reference, width, Math.Min(line, height - 1), x);
        if ((y & 1) == 0)
            return top;

        return (top + GetHorizontallyExpandedReference(reference, width, Math.Min(line + 1, height - 1), x)) >> 1;
    }

    private int GetHorizontallyExpandedReference(ReadOnlySpan<ushort> reference, int width, int y, int x)
    {
        var row = reference.Slice(y * width, width);
        if (!_expandsHorizontally)
            return row[Math.Min(x, width - 1)];

        var column = x >> 1;
        var left = row[Math.Min(column, width - 1)];
        if ((x & 1) == 0)
            return left;

        return (left + row[Math.Min(column + 1, width - 1)]) >> 1;
    }

    /// <summary>
    /// Writes the image of a hierarchical file: the reference of its last frame, which has the size of the image, in planes
    /// of the geometry of a lossless frame (one sample per data unit, the samples that complete the MCUs repeating the last
    /// ones), through the row writers of the other frames.
    /// </summary>
    private void WriteHierarchicalImage()
    {
        var structure = _structure!;
        var frameComponents = structure.Components;
        var components = new JpegFrameComponent[frameComponents.Count];
        for (var i = 0; i < components.Length; i++)
        {
            var component = frameComponents[i];
            components[i] = components.Length == 1
                ? new JpegFrameComponent(i, component.Id, 1, 1, 0, _precision, dataUnitSize: 1)
                : new JpegFrameComponent(i, component.Id, component.HorizontalSampling, component.VerticalSampling, 0, _precision, dataUnitSize: 1);
        }

        var maxHorizontal = components.Max(component => component.HorizontalSampling);
        var maxVertical = components.Max(component => component.VerticalSampling);
        _dataUnitSize = 1;
        _blockSize = 1;
        _maxVertical = maxVertical;
        _mcusX = (structure.Width + maxHorizontal - 1) / maxHorizontal;
        _mcusY = (structure.Height + maxVertical - 1) / maxVertical;
        _components = components;
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            component.SetGeometry(structure.Width, structure.Height, maxHorizontal, maxVertical);
            component.AllocatePlane(_context.Scope, _mcusX, _mcusY, _context.Limits);
            var (width, height) = _referenceSizes[i];
            var reference = unsafe(MemoryMarshal.Cast<byte, ushort>(_references[i]!.RawBuffer.AsSpan(0, width * height * sizeof(ushort))));
            for (var y = 0; y < component.BandRows * _mcusY; y++)
            {
                var line = reference.Slice(Math.Min(y, height - 1) * width, width);
                var offset = component.GetRowOffset(y);
                if (component.HasDeepSamples)
                {
                    var samples = component.DeepSamples.Slice(offset, component.Stride);
                    line.CopyTo(samples);
                    samples[width..].Fill(line[width - 1]);
                }
                else
                {
                    var samples = component.Samples.AsSpan(offset, component.Stride);
                    for (var x = 0; x < width; x++)
                    {
                        samples[x] = (byte)line[x];
                    }

                    samples[width..].Fill((byte)line[width - 1]);
                }
            }
        }

        ReleaseReferences();
        CreateRowWriters(_context.Scope, 1);
        var writer = _writer!;
        while (writer.BandsWritten < _mcusY)
        {
            writer.WriteNextBand(_context.CancellationToken);
        }

        ReleaseDecoderState();
    }

    private void ReleaseReferences()
    {
        foreach (var reference in _references)
        {
            reference?.Dispose();
        }

        _references = [];
        _referenceSizes = [];
    }

    public override void OnEntropyData(ReadOnlySpan<byte> data)
    {
        if (_jpegLsScan is { } jpegLsScan)
        {
            jpegLsScan.Feed(data);
            return;
        }

        var scan = _scan ?? throw new InvalidOperationException("No JPEG scan was started.");
        scan.Feed(data);
    }

    public override bool IsEmbedded => _receiver is not null;

    // Lenient decoding applies to JPEG files, not to the datastreams of another format
    private bool IsLenient => _context.IsLenient && _receiver is null;

    public override bool OnScanEnd() => EndScan(endOfInput: false);

    public override bool OnScanEndAtEndOfInput() => EndScan(endOfInput: true);

    private bool EndScan(bool endOfInput)
    {
        if (_jpegLsScan is { } jpegLsScan)
        {
            EndJpegLsScan(jpegLsScan);
            return true;
        }

        var scan = _scan ?? throw new InvalidOperationException("No JPEG scan was started.");
        scan.Finish(endOfInput);
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
            if (_sink is { WantsRefinement: true } sink && !_hierarchical && _components.All(component => component.IsDecoded))
            {
                // A rendering counts like the pixels of a frame that is not shown
                _context.Tracker.ChargeHiddenPixels(_outputSize.Area);
                RenderCoefficients(sink);
            }

            return true;
        }

        if (!_hierarchical && _components.All(component => component.IsDecoded))
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
        if (_hierarchical)
        {
            EndHierarchicalFrame(isLast: true);
            WriteHierarchicalImage();
            var hierarchicalSink = _sink ?? throw new InvalidOperationException("The JPEG header was not parsed.");
            hierarchicalSink.UpdateMetadata(structure.Metadata);
            hierarchicalSink.EndImage();
            return;
        }

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
            ReleaseReferences();
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
        _jpegLsScan?.Dispose();
        _jpegLsScan = null;
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
    /// <param name="dcBinCount">The number of bins of a DC statistics area: more for the differences of a lossless frame.</param>
    private JpegArithmeticConditioning[] CreateArithmeticConditioning(ReadOnlySpan<byte> selectors, bool dc, bool ac, int dcBinCount = JpegArithmeticConditioning.DcBinCount)
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
                dc ? dcStatistics[dcId] ??= new byte[dcBinCount] : [],
                ac ? acStatistics[acId] ??= new byte[JpegArithmeticConditioning.AcBinCount] : [],
                lower,
                upper,
                ac ? _acConditioning[acId] : 0);
        }

        return result;
    }

    private void StartScan(ReadOnlySpan<byte> payload)
    {
        if (_jpegLs)
        {
            StartJpegLsScan(payload);
            return;
        }

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
            ? new JpegArithmeticSequentialScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken) { AcceptsGarbledRestartNumbers = IsLenient, Differential = _differential }
            : new JpegSequentialScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken, deferTransforms: _reconstruction is not null) { AcceptsGarbledRestartNumbers = IsLenient, Differential = _differential };

        // The blocks are decoded with FastLookup but the last MCUs of each segment (most of them when the segments are short)
        // and the stored coefficients of the workers
        if (_reconstruction is not null || _restartInterval > 0)
        {
            PrepareFastAc(scanComponents);
        }
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
        JpegArithmeticConditioning[]? conditioning = null;
        if (_arithmetic)
        {
            Span<byte> selectors = stackalloc byte[count];
            for (var i = 0; i < count; i++)
            {
                selectors[i] = payload[2 + (2 * i)];
            }

            conditioning = CreateArithmeticConditioning(selectors, dc: true, ac: false, JpegArithmeticConditioning.LosslessBinCount);
        }

        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            if (component.IsDecoded)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} is coded in several lossless scans."));

            if (conditioning is not null)
            {
                scanComponents[i] = new JpegScanDecoder.ScanComponent(component, dcTable: null, acTable: null, quantization: []) { Arithmetic = conditioning[i] };
                continue;
            }

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
        _scan = new JpegLosslessScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, predictor: parameters[0], pointTransform: parameters[2] & 0x0F, _streaming ? OnMcuRowDecoded : null, _context.CancellationToken, _arithmetic) { AcceptsGarbledRestartNumbers = IsLenient };
    }

    /// <summary>
    /// Starts a scan of a JPEG-LS frame (T.87 C.2.3; the walker validated the interleave mode, the mapping table selectors
    /// and the point transform). Its data is buffered until the scan ends; a scan of one component is never interleaved.
    /// </summary>
    private void StartJpegLsScan(ReadOnlySpan<byte> payload)
    {
        var count = payload[0];
        var parameters = payload[(1 + (2 * count))..];
        var components = new JpegFrameComponent[count];
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var component = _components.First(component => component.Id == id);
            if (component.IsDecoded)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG component {id} is coded in several JPEG-LS scans."));

            components[i] = component;
        }

        // CharLS and FFmpeg apply the HP color transforms to the components of an interleaved scan only
        if (count == 1 && _structure!.JpegLsColorTransform != 0)
            throw Unsupported("The HP color transforms of JPEG-LS components coded in separate scans are not supported.", "JPEG-LS HP color transform");

        var coding = JpegLsParameters.Create(_precision, near: parameters[0], _jpegLsPreset);
        if (_scanCount == 0)
        {
            AllocateDecoderState(streaming: false);
        }

        _scanCount++;
        _jpegLsComponents = components;
        _jpegLsScan = new JpegLsScanDecoder(components, coding, count == 1 ? 0 : parameters[1], _restartInterval, _context.Scope, _context.Limits, _context.CancellationToken);
    }

    /// <summary>Decodes a JPEG-LS scan at its end, and writes every row once each component is decoded.</summary>
    private void EndJpegLsScan(JpegLsScanDecoder scan)
    {
        _jpegLsScan = null;
        scan.Finish();
        foreach (var component in _jpegLsComponents)
        {
            component.IsDecoded = true;
        }

        _jpegLsComponents = [];
        if (!_components.All(component => component.IsDecoded))
            return;

        if (_structure!.JpegLsColorTransform != 0)
        {
            UndoJpegLsColorTransform(_structure.JpegLsColorTransform);
        }

        if (_writers.Length > 1)
        {
            JpegBandWriter.WriteBands(_writers, _mcusY, _context.CancellationToken);
        }
        else
        {
            var writer = _writer!;
            while (writer.BandsWritten < _mcusY)
            {
                writer.WriteNextBand(_context.CancellationToken);
            }
        }

        ReleaseDecoderState();
    }

    /// <summary>
    /// Undoes the HP color transform of the three 8-bit components of a JPEG-LS frame, modulo 2^8 (as CharLS does): the
    /// transform 1 codes <c>R - G</c>, <c>G</c> and <c>B - G</c> (offset by half the range), the transform 2 <c>R - G</c>,
    /// <c>G</c> and <c>B - (R + G) / 2</c>, and the transform 3 the reversible transform <c>G + (R - G + B - G) / 4</c>,
    /// <c>B - G</c> and <c>R - G</c>.
    /// </summary>
    private void UndoJpegLsColorTransform(int transform)
    {
        // The walker checked that the three planes have the same geometry
        var first = _components[0].Samples;
        var second = _components[1].Samples;
        var third = _components[2].Samples;
        var length = _components[0].Stride * _components[0].BandRows * _mcusY;
        for (var i = 0; i < length; i++)
        {
            int v1 = first[i];
            int v2 = second[i];
            int v3 = third[i];
            switch (transform)
            {
                case 1:
                    first[i] = (byte)(v1 + v2 - 128);
                    third[i] = (byte)(v3 + v2 - 128);
                    break;

                case 2:
                    var red = (byte)(v1 + v2 - 128);
                    first[i] = red;
                    third[i] = (byte)(v3 + ((red + v2) >> 1) - 128);
                    break;

                default:
                    var green = v1 - ((v3 + v2) >> 2) + 64;
                    first[i] = (byte)(v3 + green - 128);
                    second[i] = (byte)green;
                    third[i] = (byte)(v2 + green - 128);
                    break;
            }
        }
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
            ? new JpegArithmeticProgressiveScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, start, end, high, low, _context.CancellationToken) { Differential = _differential }
            : new JpegProgressiveScanDecoder(scanComponents, reader, _mcusX, _mcusY, _restartInterval, start, end, high, low, _context.CancellationToken) { Differential = _differential };
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

        _reader = new JpegEntropyReader(scope, Math.Max(EntropyBufferCapacity, 2 * JpegScanDecoder.GetRequiredBufferCapacity(10, _components[0].Precision, _arithmetic)));
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
        if (_sink is not { } sink || _scanCount == 0 || _hierarchical)
            return false;

        if (_progressive)
        {
            // A component that no scan coded yet has no quantization table to transform its coefficients with
            if (_components.Any(component => component.LatchedQuantization is null))
                return false;

            RenderCoefficients(sink);
            return true;
        }

        var height = _outputSize.Height;
        int writtenRows;
        if (_writer is { } writer && _streaming)
        {
            // The rows that one worker would have written: every band but the last one decoded
            _reconstruction?.Reconstruct(_decodedRows);
            writtenRows = Math.Min(height, writer.BandsWritten * _blockSize * _maxVertical);
        }
        else if (_writer is null && _components.All(component => component.IsDecoded))
        {
            // Every scan was decoded and every row written
            writtenRows = height;
        }
        else
        {
            // The scans of the components are decoded one after the other: no row is written before the last one
            return false;
        }

        using (var lease = sink.LeaseCurrentFrame())
        {
            for (var y = writtenRows; y < height; y++)
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

        // Only the DC coefficients: one value per block of 8x8 samples (fewer when the frame is scaled)
        sink.EndRefinement(_hasAcCoefficients ? 1 : _blockSize, _completedScans);
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
        // The frames of a hierarchical image keep full planes, which become the reference of the next frame
        streaming &= !_hierarchical;
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

        _reader = new JpegEntropyReader(scope, Math.Max(EntropyBufferCapacity, 2 * JpegScanDecoder.GetRequiredBufferCapacity(10, _components[0].Precision, _arithmetic, _lossless)));
        if (!_hierarchical)
        {
            CreateRowWriters(scope, !streaming || deferred ? _workers : 1);
        }
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
        var (width, height) = _outputSize;
        var bandRows = _blockSize * _maxVertical;
        if (_precision != 8)
        {
            return _receiver is not null
                ? new JpegDeepRowWriter(_components, _receiver, _colorModel, width, height, bandRows, _precision, scope)
                : new JpegDeepRowWriter(_components, _sink!, _colorModel, _inkConversion, width, height, bandRows, _precision, scope);
        }

        return _receiver is not null
            ? new JpegRowWriter(_components, _receiver, _colorModel, width, height, bandRows, scope)
            : new JpegRowWriter(_components, _sink!, _colorModel, _inkConversion, width, height, bandRows, scope);
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
