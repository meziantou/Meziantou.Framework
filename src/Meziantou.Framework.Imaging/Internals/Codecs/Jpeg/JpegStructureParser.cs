using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental JPEG marker walker (ITU-T T.81, JFIF, EXIF, ICC.1 Annex B): bounded marker segments (at most 65,535 bytes
/// are buffered), the frame header, scan headers, and the boundaries of entropy-coded segments (byte stuffing and restart
/// markers). It never decodes entropy-coded data or allocates coefficients.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: stops before the first SOS marker (not consumed); a frame header must precede it.</description></item>
/// <item><description>Full scan: walks every scan to EOI; metadata after the first scan (for example COM) is included.</description></item>
/// <item><description>Decode: like a full scan, with every segment and entropy-coded byte delivered to a <see cref="JpegDecodeObserver"/>.</description></item>
/// </list>
/// Supported: 8-bit baseline (SOF0), extended sequential Huffman (SOF1) and progressive Huffman (SOF2) frames with 1 or 3
/// components. Recognized and rejected with <see cref="UnsupportedImageFeatureException"/>: lossless (SOF3), hierarchical
/// (SOF5-7, SOF13-15, DHP, EXP), arithmetic coding (SOF9-11, DAC), JPEG-LS, 12/16-bit precision, DNL-defined heights,
/// CMYK/YCCK (4 components), other component counts and sampling factors that do not divide the largest factors.
/// Malformed or misplaced markers and segments, a missing frame header or scan, interleaved scans of more than 10 blocks
/// per MCU, progressive scans with invalid spectral selection, successive approximation or coefficient progression (see
/// <see cref="ValidateProgressiveScan"/>), and truncation (including a missing EOI) are
/// <see cref="InvalidImageContentException"/>. Table contents (DQT/DHT) are validated by the decoder only.
/// </remarks>
internal sealed class JpegStructureParser : ImageParser<ImageInfo>
{
    private const byte MarkerSof0 = 0xC0;
    private const byte MarkerSof1 = 0xC1;
    private const byte MarkerSof2 = 0xC2;
    private const byte MarkerDht = 0xC4;
    private const byte MarkerJpg = 0xC8;
    private const byte MarkerDac = 0xCC;
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerDqt = 0xDB;
    private const byte MarkerDnl = 0xDC;
    private const byte MarkerDri = 0xDD;
    private const byte MarkerDhp = 0xDE;
    private const byte MarkerExp = 0xDF;
    private const byte MarkerApp0 = 0xE0;
    private const byte MarkerApp1 = 0xE1;
    private const byte MarkerApp2 = 0xE2;
    private const byte MarkerApp14 = 0xEE;
    private const byte MarkerJpegLsFrame = 0xF7;
    private const byte MarkerJpegLsExtension = 0xF8;
    private const byte MarkerCom = 0xFE;

    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;

    private static ReadOnlySpan<byte> IccSignature => "ICC_PROFILE\0"u8;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly JpegDecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private int _scanCount;
    private bool _firstScanReached;
    private SortedDictionary<int, byte[]>? _iccChunks;
    private int _iccChunkCount;
    private bool _iccInvalid;
    private ImageInfo? _result;

    // Progressive frames: the successive approximation state of each coefficient of each component (64 per component, in
    // zig-zag order): -1 before its first scan, then the Al of the last scan that coded it
    private sbyte[]? _coefficientBits;

    public JpegStructureParser(ImageCodecContext context, StructureWalk walk, JpegDecodeObserver? observer = null)
        : base(ImageFormat.Jpeg)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(observer);
        }

        _context = context;
        _walk = walk;
        _observer = walk == StructureWalk.Decode ? observer : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Jpeg);
    }

    private enum State
    {
        StartOfImage,
        Marker,
        EntropyCodedData,
        Done,
    }

    /// <summary>Gets the SOF marker of the frame (0xC0 baseline, 0xC1 extended sequential, 0xC2 progressive), or 0 before the frame header.</summary>
    public byte FrameMarker { get; private set; }

    public bool IsProgressive => FrameMarker == MarkerSof2;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    /// <summary>Gets the frame components, in frame-header order.</summary>
    public IReadOnlyList<JpegComponent> Components { get; private set; } = [];

    /// <summary>Gets a value indicating whether a JFIF APP0 segment was found.</summary>
    public bool HasJfif { get; private set; }

    /// <summary>Gets the Adobe APP14 color transform (0: none/RGB, 1: YCbCr, 2: YCCK), or <see langword="null"/>.</summary>
    public byte? AdobeTransform { get; private set; }

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel
    {
        get
        {
            if (Components.Count == 1)
                return ImageColorModel.Grayscale;

            if (AdobeTransform is { } transform)
                return transform == 0 ? ImageColorModel.Rgb : ImageColorModel.YCbCr;

            if (!HasJfif && Components.Count == 3 && Components[0].Id == 'R' && Components[1].Id == 'G' && Components[2].Id == 'B')
                return ImageColorModel.Rgb;

            return ImageColorModel.YCbCr;
        }
    }

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the default working representation.</summary>
    public PixelFormat DefaultPixelFormat => DefaultPixelFormats.ForJpeg(Components.Count);

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.StartOfImage:
                    if (remaining.Length < 2)
                        return ParseStatus.NeedMoreData(2);

                    if (remaining[0] != 0xFF || remaining[1] != MarkerSoi)
                        throw Invalid("The JPEG data does not start with an SOI marker.");

                    consumed += 2;
                    _state = State.Marker;
                    break;

                case State.Marker:
                {
                    if (remaining.Length < 2)
                        return ParseStatus.NeedMoreData(2);

                    if (remaining[0] != 0xFF)
                        throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A JPEG marker was expected, found 0x{remaining[0]:X2}."));

                    var marker = remaining[1];
                    if (marker == 0xFF)
                    {
                        // Fill byte before a marker
                        consumed++;
                        break;
                    }

                    if (marker == MarkerEoi)
                    {
                        consumed += 2;
                        OnEndOfImage();
                        return ParseStatus.Complete;
                    }

                    if (marker == 0x01)
                    {
                        // TEM: no length
                        consumed += 2;
                        break;
                    }

                    if (marker is 0x00 or MarkerSoi or (>= 0xD0 and <= 0xD7))
                        throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG marker 0xFF{marker:X2} is not valid here."));

                    if (marker == MarkerSos && !_firstScanReached)
                    {
                        if (!OnFirstScan())
                            return ParseStatus.Complete;

                        // Sequential readers: yield before the first SOS segment, which the next call processes
                        if (TryYield())
                            return ParseStatus.Complete;
                    }

                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    var length = BinaryPrimitives.ReadUInt16BigEndian(remaining[2..]);
                    if (length < 2)
                        throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG segment 0xFF{marker:X2} has an invalid length {length}."));

                    var total = 2 + length;
                    if (remaining.Length < total)
                        return ParseStatus.NeedMoreData(total);

                    var payload = remaining.Slice(4, length - 2);
                    ProcessSegment(marker, payload);
                    _observer?.OnSegment(marker, payload);
                    consumed += total;
                    if (marker == MarkerSos)
                    {
                        _scanCount++;
                        _state = State.EntropyCodedData;
                    }

                    break;
                }

                case State.EntropyCodedData:
                {
                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var index = remaining.IndexOf((byte)0xFF);
                    if (index != 0)
                    {
                        var data = index < 0 ? remaining : remaining[..index];
                        _observer?.OnEntropyData(data);
                        consumed += data.Length;
                        break;
                    }

                    if (remaining.Length < 2)
                        return ParseStatus.NeedMoreData(2);

                    var next = remaining[1];
                    if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                    {
                        // Stuffed 0xFF or restart marker: part of the entropy-coded segment
                        _observer?.OnEntropyData(remaining[..2]);
                        consumed += 2;
                        break;
                    }

                    if (next == 0xFF)
                    {
                        consumed++;
                        break;
                    }

                    _state = State.Marker;
                    if (_observer?.OnScanEnd() == false)
                    {
                        _state = State.Done;
                        return ParseStatus.Complete;
                    }

                    if (TryYield())
                        return ParseStatus.Complete;

                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The JPEG structure was not parsed.");

    private bool OnFirstScan()
    {
        if (FrameMarker == 0)
            throw Invalid("The JPEG scan is not preceded by a frame header (SOF).");

        _firstScanReached = true;

        // The ICC chunks found before the first scan form the profile that labels the decoded pixels
        CompleteIccProfile();
        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            _state = State.Done;
            return false;
        }

        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }

        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _observer?.OnHeaderComplete(this);
        return true;
    }

    private bool TryYield() => _walk == StructureWalk.Decode && _context.TryConsumeYield();

    private void OnEndOfImage()
    {
        if (_scanCount == 0)
            throw Invalid("The JPEG data has no scan.");

        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _observer?.OnEnd(this);
    }

    private void ProcessSegment(byte marker, ReadOnlySpan<byte> payload)
    {
        switch (marker)
        {
            case MarkerSof0:
            case MarkerSof1:
            case MarkerSof2:
                ProcessFrameHeader(marker, payload);
                break;

            case 0xC3:
                throw Unsupported("Lossless JPEG (SOF3) is not supported.", "JPEG lossless");

            case 0xC5 or 0xC6 or 0xC7 or MarkerDhp or MarkerExp:
                throw Unsupported("Hierarchical JPEG is not supported.", "JPEG hierarchical");

            case 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF or MarkerDac:
                throw Unsupported("Arithmetic-coded JPEG is not supported.", "JPEG arithmetic coding");

            case MarkerJpg:
                throw Unsupported("The JPEG extension marker JPG (0xFFC8) is not supported.", "JPEG extensions");

            case MarkerJpegLsFrame or MarkerJpegLsExtension:
                throw Unsupported("JPEG-LS is not supported.", "JPEG-LS");

            case MarkerSos:
                ProcessScanHeader(payload);
                break;

            case MarkerDri:
                if (payload.Length != 2)
                    throw Invalid("The JPEG DRI segment must be 4 bytes long.");

                break;

            case MarkerDht or MarkerDqt or MarkerDnl:
                // Tables are parsed by the decoder; they are bounded segments
                break;

            case MarkerApp0:
                if (payload.Length >= 12 && payload.StartsWith("JFIF\0"u8))
                {
                    HasJfif = true;
                    _metadata.TrySetResolution(ResolutionConversion.FromJfifDensity(payload[7], BinaryPrimitives.ReadUInt16BigEndian(payload[8..]), BinaryPrimitives.ReadUInt16BigEndian(payload[10..])));
                }

                break;

            case MarkerApp1:
                if (payload.StartsWith("Exif\0\0"u8))
                {
                    _metadata.TryAdoptExif(payload[6..]);
                }
                else if (payload.StartsWith(XmpSignature))
                {
                    _metadata.TryAdoptXmp(payload[XmpSignature.Length..]);
                }

                break;

            case MarkerApp2:
                if (payload.StartsWith(IccSignature))
                {
                    AddIccChunk(payload[IccSignature.Length..]);
                }

                break;

            case MarkerApp14:
                if (payload.Length >= 12 && payload.StartsWith("Adobe"u8))
                {
                    AdobeTransform ??= payload[11];
                }

                break;

            case MarkerCom:
                _metadata.Charge(payload.Length);
                _metadata.AddText(ImageTextEntry.CommentKeyword, Encoding.Latin1.GetString(payload));
                break;

            case <= 0xBF:
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG marker 0xFF{marker:X2} is reserved."));
        }
    }

    private void ProcessFrameHeader(byte marker, ReadOnlySpan<byte> payload)
    {
        if (FrameMarker != 0)
            throw Invalid("The JPEG data has several frame headers.");

        if (payload.Length < 6)
            throw Invalid("The JPEG frame header is truncated.");

        var precision = payload[0];
        var height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
        var width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
        var count = payload[5];
        if (count == 0 || payload.Length != 6 + (3 * count))
            throw Invalid("The JPEG frame header length does not match its component count.");

        var components = new JpegComponent[count];
        for (var i = 0; i < count; i++)
        {
            var entry = payload.Slice(6 + (3 * i), 3);
            var component = new JpegComponent(entry[0], (byte)(entry[1] >> 4), (byte)(entry[1] & 0x0F), entry[2]);
            if (component.HorizontalSampling is < 1 or > 4 || component.VerticalSampling is < 1 or > 4)
                throw Invalid("A JPEG sampling factor is not between 1 and 4.");

            if (component.QuantizationTable > 3)
                throw Invalid("A JPEG quantization table selector is greater than 3.");

            for (var j = 0; j < i; j++)
            {
                if (components[j].Id == component.Id)
                    throw Invalid("The JPEG frame header has duplicate component identifiers.");
            }

            components[i] = component;
        }

        if (width == 0)
            throw Invalid("The JPEG frame width is zero.");

        if (precision != 8)
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"{precision}-bit JPEG precision is not supported (only 8-bit samples)."), "JPEG " + precision.ToString(CultureInfo.InvariantCulture) + "-bit precision");

        if (height == 0)
            throw Unsupported("JPEG images whose height is defined by a DNL marker are not supported.", "JPEG DNL height");

        if (count == 4)
            throw Unsupported("CMYK and YCCK JPEG images are not supported.", "JPEG CMYK/YCCK");

        if (count != 1 && count != 3)
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"JPEG images with {count} components are not supported."), "JPEG " + count.ToString(CultureInfo.InvariantCulture) + " components");

        if (count > 1)
        {
            // Chroma upsampling by integral factors only (T.81 allows any factors; fractional ratios are a recognized unsupported mode)
            var maxHorizontal = components.Max(component => component.HorizontalSampling);
            var maxVertical = components.Max(component => component.VerticalSampling);
            if (components.Any(component => maxHorizontal % component.HorizontalSampling != 0 || maxVertical % component.VerticalSampling != 0))
                throw Unsupported("JPEG images whose sampling factors do not divide the largest factors (non-integral sampling ratios) are not supported.", "JPEG non-integral sampling ratios");
        }

        FrameMarker = marker;
        Width = width;
        Height = height;
        Components = components;
        if (marker == MarkerSof2)
        {
            _coefficientBits = new sbyte[64 * count];
            _coefficientBits.AsSpan().Fill(-1);
        }

        _context.Limits.EnsureCanvasWithinLimits(Width, Height);
    }

    private void ProcessScanHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw Invalid("The JPEG scan header is empty.");

        var count = payload[0];
        if (count is < 1 or > 4 || payload.Length != 1 + (2 * count) + 3)
            throw Invalid("The JPEG scan header length does not match its component count.");

        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var found = false;
            foreach (var component in Components)
            {
                found |= component.Id == id;
            }

            if (!found)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG scan refers to the unknown component {id}."));

            for (var j = 0; j < i; j++)
            {
                if (payload[1 + (2 * j)] == id)
                    throw Invalid("The JPEG scan header has duplicate components.");
            }
        }

        if (count > 1)
        {
            // T.81 B.2.3: an interleaved MCU has at most 10 blocks
            var blocks = 0;
            for (var i = 0; i < count; i++)
            {
                var id = payload[1 + (2 * i)];
                var component = Components.First(component => component.Id == id);
                blocks += component.HorizontalSampling * component.VerticalSampling;
            }

            if (blocks > 10)
                throw Invalid("The JPEG interleaved scan has more than 10 blocks per MCU.");
        }

        if (_coefficientBits is not null)
        {
            ValidateProgressiveScan(payload, count);
        }
    }

    /// <summary>
    /// Validates the spectral selection and successive approximation of a progressive scan (ITU-T T.81 G.1.1.1) and tracks
    /// the progression of every coefficient: a DC scan codes only the DC coefficient (<c>Ss = Se = 0</c>, possibly
    /// interleaved), an AC scan one component and a band <c>1 &lt;= Ss &lt;= Se &lt;= 63</c>; <c>Ah</c> and <c>Al</c> are at most
    /// 13 and a refinement (<c>Ah &gt; 0</c>) lowers the bit position by one (<c>Al = Ah - 1</c>). A first scan (<c>Ah = 0</c>)
    /// codes only coefficients no scan coded yet, a refinement only coefficients whose last scan had <c>Al = Ah</c>, and AC
    /// coefficients follow the component's first DC scan. Every accepted scan advances the state of each coefficient of its
    /// band, which can advance at most 14 times: the scan work is bounded (at most 64 x 14 passes per component).
    /// </summary>
    private void ValidateProgressiveScan(ReadOnlySpan<byte> payload, int count)
    {
        var parameters = payload[(1 + (2 * count))..];
        var start = parameters[0];
        var end = parameters[1];
        var high = parameters[2] >> 4;
        var low = parameters[2] & 0x0F;
        if (start > end || end > 63)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG progressive scan has an invalid spectral selection (Ss = {start}, Se = {end})."));

        if (start == 0 && end != 0)
            throw Invalid("A JPEG progressive scan codes the DC and AC coefficients together (Ss = 0, Se > 0).");

        if (start > 0 && count != 1)
            throw Invalid("A JPEG progressive AC scan codes several components.");

        if (high > 13 || low > 13)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG successive approximation bit position is greater than 13 (Ah = {high}, Al = {low})."));

        if (high != 0 && low != high - 1)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A JPEG refinement scan must refine one bit (Ah = {high}, Al = {low})."));

        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var index = 0;
            while (Components[index].Id != id)
            {
                index++;
            }

            var bits = _coefficientBits.AsSpan(64 * index, 64);
            if (start > 0 && bits[0] < 0)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG progressive scan codes AC coefficients of the component {id} before its DC coefficient."));

            for (var k = start; k <= end; k++)
            {
                if (high == 0 && bits[k] >= 0)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The coefficient {k} of the JPEG component {id} is coded by several first scans (Ah = 0)."));

                if (high != 0 && bits[k] != high)
                    throw Invalid(bits[k] < 0
                        ? string.Create(CultureInfo.InvariantCulture, $"The JPEG refinement scan of the coefficient {k} of the component {id} precedes its first scan.")
                        : string.Create(CultureInfo.InvariantCulture, $"The JPEG refinement scan of the coefficient {k} of the component {id} expects Al = {high} from the previous scan, which had Al = {bits[k]}."));

                bits[k] = (sbyte)low;
            }
        }
    }

    private void AddIccChunk(ReadOnlySpan<byte> data)
    {
        // ICC.1 Annex B: 1-based sequence number, total count, then the chunk bytes
        if (_iccInvalid || _metadata.Metadata.IccProfile is not null)
            return;

        if (data.Length < 2 || data[0] == 0 || data[1] == 0 || data[0] > data[1] || (_iccChunkCount != 0 && _iccChunkCount != data[1]))
        {
            _iccInvalid = true;
            _iccChunks = null;
            return;
        }

        _iccChunkCount = data[1];
        _iccChunks ??= [];
        if (!_iccChunks.TryAdd(data[0], []))
        {
            _iccInvalid = true;
            _iccChunks = null;
            return;
        }

        _metadata.Charge(data.Length - 2);
        _iccChunks[data[0]] = data[2..].ToArray();
    }

    private void CompleteIccProfile()
    {
        if (_iccChunks is null || _iccChunks.Count != _iccChunkCount)
            return;

        var length = 0;
        foreach (var chunk in _iccChunks.Values)
        {
            length += chunk.Length;
        }

        var profile = new byte[length];
        var offset = 0;
        foreach (var chunk in _iccChunks.Values)
        {
            chunk.CopyTo(profile, offset);
            offset += chunk.Length;
        }

        _iccChunks = null;
        _metadata.TryAdoptIccProfile(profile, Components.Count == 1);
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        CompleteIccProfile();
        return new ImageInfo(
            ImageFormat.Jpeg,
            Size,
            DefaultPixelFormat,
            ColorModel,
            bitsPerComponent: 8,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: false,
            animation: null,
            _metadata.Metadata,
            mode);
    }

    private static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Jpeg, feature);

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Jpeg);
}
