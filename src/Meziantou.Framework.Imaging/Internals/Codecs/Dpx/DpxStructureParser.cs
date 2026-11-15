using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental DPX reader (SMPTE ST 268:2014, DPX 2.0, and 268M-2003, DPX 1.0): the headers, then the lines of the first
/// image element, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the generic headers (1,664 bytes) and, when the image data start after them, the industry-specific headers (to byte 2,048).</description></item>
/// <item><description>Full scan: reads every line of the first image element, charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: the same walk; each line goes to the <see cref="RawRowDecoder"/>.</description></item>
/// </list>
/// <para>
/// The magic number gives the byte order of every field and of the image data. The first image element is decoded; the
/// other ones (up to eight) are counted in <see cref="DpxHeader.ImageElementCount"/> and not read. The data of the element
/// start at its offset (field 21.12, or field 2 when it is 0 or undefined), which must be after the generic headers; the
/// bytes before it (the user-defined data) are skipped. Each line is followed by the end-of-line padding of the element;
/// the bytes after the last line are not read.
/// </para>
/// <para>
/// An orientation code above 7, a number of elements outside 1 to 8, an empty image, a bit depth that is not one of Table 3A,
/// a 4:2:2 element of an odd width, data that start inside the generic headers and truncation are
/// <see cref="InvalidImageContentException"/>. Descriptors other than R, G, B, A, Y, RGB, RGBA, ABGR and the four Cb, Y, Cr
/// ones, signed components, run-length encoded or encrypted data, reserved packing methods, and 1-bit or floating-point Cb, Y,
/// Cr components are <see cref="UnsupportedImageFeatureException"/>, reported by the header walk.
/// </para>
/// </remarks>
internal sealed class DpxStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly RawRowDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private long _position;
    private int _headerLength;
    private long _dataOffset;
    private long _eolPadding;
    private int _width;
    private int _height;
    private DpxRowReader? _reader;
    private DpxHeader? _header;
    private PixelFormat _defaultPixelFormat;
    private ImageColorModel _colorModel;
    private int _bitsPerComponent;
    private bool _hasAlpha;

    private PooledBuffer? _storedRow;
    private PooledBuffer? _components;
    private PooledBuffer? _sourceRow;
    private int _storedFilled;
    private long _skipRemaining;
    private int _rowIndex;
    private ImageInfo? _result;

    public DpxStructureParser(ImageCodecContext context, StructureWalk walk, RawRowDecoder? decoder = null)
        : base(ImageFormat.Dpx)
    {
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Dpx);
    }

    private enum State
    {
        GenericHeader,
        IndustryHeader,
        SkipToData,
        Lines,
        Padding,
        Done,
    }

    public Size Size => new(_width, _height);

    /// <summary>Gets the metadata: the orientation, the transfer function or CICP description, the pixel aspect ratio and <see cref="ImageMetadata.DpxHeader"/>.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.GenericHeader:
                {
                    if (buffer.Length < DpxFormat.GenericHeaderLength)
                        return ParseStatus.NeedMoreData(DpxFormat.GenericHeaderLength);

                    ReadGenericHeader(buffer[..DpxFormat.GenericHeaderLength]);
                    if (_dataOffset >= DpxFormat.FullHeaderLength)
                    {
                        _state = State.IndustryHeader;
                        break;
                    }

                    _headerLength = DpxFormat.GenericHeaderLength;
                    if (CompleteHeader(buffer[..DpxFormat.GenericHeaderLength], ref consumed))
                        return ParseStatus.Complete;

                    break;
                }

                case State.IndustryHeader:
                {
                    if (buffer.Length < DpxFormat.FullHeaderLength)
                        return ParseStatus.NeedMoreData(DpxFormat.FullHeaderLength);

                    _headerLength = DpxFormat.FullHeaderLength;
                    if (CompleteHeader(buffer[..DpxFormat.FullHeaderLength], ref consumed))
                        return ParseStatus.Complete;

                    break;
                }

                case State.SkipToData:
                {
                    var remaining = buffer.Length - consumed;
                    if (remaining == 0)
                        return ParseStatus.NeedMoreData(1);

                    var take = (int)Math.Min(remaining, _skipRemaining);
                    Advance(ref consumed, take);
                    _skipRemaining -= take;
                    if (_skipRemaining == 0)
                    {
                        _state = State.Lines;
                    }

                    break;
                }

                case State.Lines:
                {
                    var status = ReadLines(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    if (_state == State.Done)
                    {
                        Finish();
                        return ParseStatus.Complete;
                    }

                    break;
                }

                case State.Padding:
                {
                    var remaining = buffer.Length - consumed;
                    if (remaining == 0)
                        return ParseStatus.NeedMoreData(1);

                    var take = (int)Math.Min(remaining, _skipRemaining);
                    Advance(ref consumed, take);
                    _skipRemaining -= take;
                    if (_skipRemaining == 0)
                    {
                        _state = State.Lines;
                    }

                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The DPX data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _storedRow?.Dispose();
            _storedRow = null;
            _components?.Dispose();
            _components = null;
            _sourceRow?.Dispose();
            _sourceRow = null;
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    /// <summary>Validates the fields of the generic headers and computes the layout of the first image element.</summary>
    private void ReadGenericHeader(ReadOnlySpan<byte> header)
    {
        var bigEndian = header[..4].SequenceEqual("SDPX"u8);
        if (!bigEndian && !header[..4].SequenceEqual("XPDS"u8))
            throw DpxFormat.Invalid("the magic number is not 'SDPX' or 'XPDS'.");

        var fields = new DpxFields(header, bigEndian);
        var orientation = fields.U16(768);
        if (orientation > 7)
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the orientation code is {orientation}; Table 2 defines 0 to 7."));

        var elementCount = fields.U16(770);
        if (elementCount is < 1 or > 8)
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the file declares {elementCount} image elements; a file has 1 to 8."));

        var width = fields.U32(772);
        var height = fields.U32(776);
        if (width == 0 || height == 0 || width == DpxFormat.UndefinedU32 || height == DpxFormat.UndefinedU32)
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image is {width} pixels per line and {height} lines."));

        const int Element = DpxFormat.FirstElementOffset;
        var sign = fields.U32(Element);
        var descriptor = (DpxDescriptor)header[Element + 20];
        var transfer = (DpxTransferCharacteristic)header[Element + 21];
        var colorimetric = (DpxColorimetricSpecification)header[Element + 22];
        var bitDepth = header[Element + 23];
        var packingCode = fields.U16(Element + 24);
        var encoding = fields.U16(Element + 26);
        var elementOffset = fields.U32(Element + 28);
        var eolPadding = fields.U32(Element + 32);
        var encryption = fields.U32(660);

        if (bitDepth is not (1 or 8 or 10 or 12 or 16 or 32 or 64))
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the bit depth of the first image element is {bitDepth}; Table 3A defines 1, 8, 10, 12, 16, 32 and 64."));

        if (DpxFormat.GetComponentsPerPixel(descriptor) == 0)
            throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DPX image element has the descriptor {(int)descriptor} ({descriptor}): only R, G, B, A, Y, RGB, RGBA, ABGR and the Cb, Y, Cr descriptors (4:2:2 and 4:4:4, with or without alpha) are decoded."), "Descriptor " + ((int)descriptor).ToString(CultureInfo.InvariantCulture));

        if (sign == 1)
            throw DpxFormat.Unsupported("The DPX image element has signed components: only unsigned components are decoded.", "Signed components");

        if (sign != 0)
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data sign of the first image element is {sign}; it is 0 (unsigned) or 1 (signed)."));

        if (encryption != DpxFormat.UndefinedU32)
            throw DpxFormat.Unsupported("The DPX image data is encrypted (the encryption key is set).", "Encrypted image data");

        if (encoding == 1)
            throw DpxFormat.Unsupported("The DPX image element is run-length encoded: no reference decoder reads it, so it is not decoded.", "Run-length encoding");

        if (encoding != 0)
            throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DPX image element has the reserved encoding {encoding}."), "Encoding " + encoding.ToString(CultureInfo.InvariantCulture));

        if (packingCode > 2)
            throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DPX image element has the reserved packing method {packingCode}."), "Packing " + packingCode.ToString(CultureInfo.InvariantCulture));

        var packing = (DpxPacking)packingCode;
        var isYCbCr = DpxFormat.IsYCbCr(descriptor);
        if (isYCbCr && bitDepth is 1 or 32 or 64)
            throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DPX image element has {bitDepth}-bit Cb, Y, Cr components: only 8 to 16-bit ones are converted to RGB."), "Cb, Y, Cr components of " + bitDepth.ToString(CultureInfo.InvariantCulture) + " bits");

        if (DpxFormat.IsSubsampled(descriptor) && (width & 1) != 0)
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the 4:2:2 image element has {width} pixels per line; Table 1 requires an even number."));

        var limits = _context.Limits;
        if (width > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

        if (height > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, height);

        limits.EnsureCanvasWithinLimits((int)width, (int)height);
        var components = (long)width * DpxFormat.GetComponentsPerPixel(descriptor);
        var lineLength = DpxRowReader.GetStoredLineLength(bitDepth, packing, components);
        if (lineLength > CheckedSizes.MaxBufferLength || components * 4 > CheckedSizes.MaxBufferLength || (long)width * 16 > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _dataOffset = elementOffset is 0 or DpxFormat.UndefinedU32 ? fields.U32(4) : elementOffset;
        if (_dataOffset < DpxFormat.GenericHeaderLength || _dataOffset == DpxFormat.UndefinedU32)
            throw DpxFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image data start at offset {_dataOffset}, inside the generic headers ({DpxFormat.GenericHeaderLength} bytes)."));

        _eolPadding = eolPadding == DpxFormat.UndefinedU32 ? 0 : eolPadding;
        _width = (int)width;
        _height = (int)height;
        var referenceLow = fields.OptionalU32(Element + 4);
        var referenceHigh = fields.OptionalU32(Element + 12);
        var yCbCr = isYCbCr ? new DpxYCbCrConverter(descriptor, bitDepth, colorimetric, referenceLow, referenceHigh) : null;
        _reader = new DpxRowReader(bigEndian, descriptor, bitDepth, packing, _width, yCbCr);
        _defaultPixelFormat = DpxRowReader.GetDefaultPixelFormat(descriptor, bitDepth);
        _hasAlpha = DpxFormat.HasAlpha(descriptor);
        _bitsPerComponent = Math.Min((int)bitDepth, 32);
        _colorModel = descriptor switch
        {
            DpxDescriptor.Alpha => ImageColorModel.GrayscaleAlpha,
            DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Luma => ImageColorModel.Grayscale,
            DpxDescriptor.Rgb => ImageColorModel.Rgb,
            DpxDescriptor.Rgba or DpxDescriptor.Abgr => ImageColorModel.Rgba,
            _ => ImageColorModel.YCbCr,
        };

        _header = new DpxHeader
        {
            Version = fields.Ascii(8, 8) ?? "",
            IsBigEndian = bigEndian,
            ImageElementCount = elementCount,
            Descriptor = descriptor,
            BitDepth = bitDepth,
            Packing = packing,
            Transfer = transfer,
            Colorimetric = colorimetric,
            ReferenceLowCode = referenceLow,
            ReferenceLowQuantity = fields.OptionalR32(Element + 8),
            ReferenceHighCode = referenceHigh,
            ReferenceHighQuantity = fields.OptionalR32(Element + 16),
            ElementDescription = fields.Ascii(Element + 40, 32),
            FileName = fields.Ascii(36, 100),
            CreationTime = fields.Ascii(136, 24),
            Creator = fields.Ascii(160, 100),
            Project = fields.Ascii(260, 200),
            Copyright = fields.Ascii(460, 200),
            SourceFileName = fields.Ascii(1432, 100),
            SourceCreationTime = fields.Ascii(1532, 24),
            InputDevice = fields.Ascii(1556, 32),
            InputDeviceSerialNumber = fields.Ascii(1588, 32),
        };

        var metadata = _metadata.Metadata;
        metadata.Orientation = DpxFormat.ToExifOrientation(orientation);
        if (transfer == DpxTransferCharacteristic.Linear)
        {
            metadata.TransferFunction = ColorTransferFunction.Linear;
        }
        else if (DpxFormat.ToCicp(transfer, colorimetric) is { } cicp)
        {
            metadata.Cicp = cicp;
        }

        var horizontal = fields.U32(1628);
        var vertical = fields.U32(1632);
        if (horizontal is not (0 or DpxFormat.UndefinedU32) && vertical is not (0 or DpxFormat.UndefinedU32) && horizontal != vertical)
        {
            metadata.PixelAspectRatio = new PixelAspectRatio(horizontal, vertical);
        }
    }

    /// <summary>Reads the industry-specific headers when they are there, publishes the header and prepares the lines.</summary>
    /// <returns><see langword="true"/> when the parse call must return <see cref="ParseStatus.Complete"/> now.</returns>
    private bool CompleteHeader(ReadOnlySpan<byte> header, ref int consumed)
    {
        if (_headerLength == DpxFormat.FullHeaderLength)
        {
            var fields = new DpxFields(header, _header!.IsBigEndian);
            _header = WithIndustryHeaders(_header, fields);
        }

        Advance(ref consumed, _headerLength);
        _metadata.Metadata.DpxHeader = _header;
        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            _state = State.Done;
            return true;
        }

        var reader = _reader!;
        _storedRow = _context.Scope.Rent((int)reader.StoredLineLength, AllocationKind.DecoderState, clear: false);
        _skipRemaining = _dataOffset - _headerLength;
        _state = _skipRemaining > 0 ? State.SkipToData : State.Lines;
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
            return false;
        }

        _components = _context.Scope.Rent(reader.ComponentsPerLine * 4, AllocationKind.DecoderState, clear: false);
        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _decoder!.OnHeaderComplete(Size, _defaultPixelFormat, reader.SourcePixelFormat);
        _sourceRow = _context.Scope.Rent(_width * PixelFormats.GetBytesPerPixel(reader.SourcePixelFormat), AllocationKind.DecoderState, clear: false);
        return _context.TryConsumeYield();
    }

    private static DpxHeader WithIndustryHeaders(DpxHeader header, DpxFields fields)
    {
        var manufacturer = fields.Ascii(1664, 2);
        var filmType = fields.Ascii(1666, 2);
        var perforations = fields.Ascii(1668, 2);
        var prefix = fields.Ascii(1670, 6);
        var count = fields.Ascii(1676, 4);
        var edgeCode = manufacturer is null || filmType is null || perforations is null || prefix is null || count is null ? null : manufacturer + filmType + perforations + prefix + count;
        var interlace = fields.Byte(1928);
        return new DpxHeader
        {
            Version = header.Version,
            IsBigEndian = header.IsBigEndian,
            ImageElementCount = header.ImageElementCount,
            Descriptor = header.Descriptor,
            BitDepth = header.BitDepth,
            Packing = header.Packing,
            Transfer = header.Transfer,
            Colorimetric = header.Colorimetric,
            ReferenceLowCode = header.ReferenceLowCode,
            ReferenceLowQuantity = header.ReferenceLowQuantity,
            ReferenceHighCode = header.ReferenceHighCode,
            ReferenceHighQuantity = header.ReferenceHighQuantity,
            ElementDescription = header.ElementDescription,
            FileName = header.FileName,
            CreationTime = header.CreationTime,
            Creator = header.Creator,
            Project = header.Project,
            Copyright = header.Copyright,
            SourceFileName = header.SourceFileName,
            SourceCreationTime = header.SourceCreationTime,
            InputDevice = header.InputDevice,
            InputDeviceSerialNumber = header.InputDeviceSerialNumber,
            FilmEdgeCode = edgeCode,
            FilmFormat = fields.Ascii(1680, 32),
            FramePosition = fields.OptionalU32(1712),
            SequenceLength = fields.OptionalU32(1716),
            HeldCount = fields.OptionalU32(1720),
            FrameRate = fields.OptionalR32(1724),
            ShutterAngle = fields.OptionalR32(1728),
            FrameIdentification = fields.Ascii(1732, 32),
            SlateInformation = fields.Ascii(1764, 100),
            TimeCode = FormatTimeCode(fields.OptionalU32(1920)),
            UserBits = fields.OptionalU32(1924),
            IsInterlaced = interlace switch
            {
                0 => false,
                1 => true,
                _ => null,
            },
            FieldNumber = fields.Byte(1929),
            VideoSignalStandard = fields.Byte(1930),
            TemporalSamplingRate = fields.OptionalR32(1940),
            Gamma = fields.OptionalR32(1948),
        };
    }

    /// <summary>Formats a time code (Table 6: hours, minutes, seconds and frames, two BCD digits each, from the most significant nibble).</summary>
    internal static string? FormatTimeCode(uint? value)
    {
        if (value is not { } code)
            return null;

        Span<char> text = stackalloc char[11];
        var position = 0;
        for (var i = 0; i < 8; i++)
        {
            var digit = (code >> (28 - (4 * i))) & 0xF;
            if (digit > 9)
                return null;

            if (i is 2 or 4 or 6)
            {
                text[position++] = ':';
            }

            text[position++] = (char)('0' + digit);
        }

        return new string(text);
    }

    private ParseStatus ReadLines(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var reader = _reader!;
        var lineLength = (int)reader.StoredLineLength;
        var lease = default(PixelLease);
        try
        {
            while (_rowIndex < _height)
            {
                var remaining = buffer[consumed..];
                if (remaining.IsEmpty)
                    return ParseStatus.NeedMoreData(1);

                ReadOnlySpan<byte> line;
                if (_storedFilled == 0 && remaining.Length >= lineLength)
                {
                    line = remaining[..lineLength];
                    Advance(ref consumed, lineLength);
                }
                else
                {
                    var take = Math.Min(remaining.Length, lineLength - _storedFilled);
                    remaining[..take].CopyTo(_storedRow!.Span[_storedFilled..]);
                    _storedFilled += take;
                    Advance(ref consumed, take);
                    if (_storedFilled < lineLength)
                        continue;

                    _storedFilled = 0;
                    line = _storedRow.Span[..lineLength];
                }

                if (_walk == StructureWalk.Decode)
                {
                    DecodeLine(ref lease, line);
                }

                _rowIndex++;
                _context.CancellationToken.ThrowIfCancellationRequested();
                if (_rowIndex < _height && _eolPadding > 0)
                {
                    _skipRemaining = _eolPadding;
                    _state = State.Padding;
                    return ParseStatus.Complete;
                }
            }

            _state = State.Done;
            return ParseStatus.Complete;
        }
        finally
        {
            lease.Dispose();
        }
    }

    private void DecodeLine(scoped ref PixelLease lease, ReadOnlySpan<byte> line)
    {
        var decoder = _decoder!;
        var reader = _reader!;
        var components = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(_components!.Span[..(reader.ComponentsPerLine * 4)]));

        // The first line is read before the frame starts, so that a component that cannot be read is reported before a frame limit
        if (_rowIndex > 0)
        {
            if (!lease.IsActive)
            {
                lease = decoder.LeaseRows();
            }

            var destination = decoder.GetDirectRow(lease, _rowIndex);
            if (!destination.IsEmpty)
            {
                reader.ReadRow(line, destination, components);
                return;
            }
        }

        var source = _sourceRow!.Span[..(_width * PixelFormats.GetBytesPerPixel(reader.SourcePixelFormat))];
        reader.ReadRow(line, source, components);
        if (!lease.IsActive)
        {
            lease = decoder.LeaseRows();
        }

        decoder.WriteRow(lease, _rowIndex, source);
    }

    private void Finish()
    {
        if (_walk == StructureWalk.FullScan)
        {
            _result = CreateInfo(ImageIdentifyMode.FullScan);
        }
        else if (_walk == StructureWalk.Decode)
        {
            _decoder!.OnEnd(_metadata.Metadata);
        }
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
        => new(
            ImageFormat.Dpx,
            Size,
            _defaultPixelFormat,
            _colorModel,
            _bitsPerComponent,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: _hasAlpha,
            animation: null,
            _metadata.Metadata,
            mode);

    /// <summary>Reads the fields of the headers in the byte order of the file.</summary>
    private readonly ref struct DpxFields
    {
        private readonly ReadOnlySpan<byte> _header;
        private readonly bool _bigEndian;

        public DpxFields(ReadOnlySpan<byte> header, bool bigEndian)
        {
            _header = header;
            _bigEndian = bigEndian;
        }

        public ushort U16(int offset) => _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(_header[offset..]) : BinaryPrimitives.ReadUInt16LittleEndian(_header[offset..]);

        public uint U32(int offset) => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(_header[offset..]) : BinaryPrimitives.ReadUInt32LittleEndian(_header[offset..]);

        public uint? OptionalU32(int offset)
        {
            var value = U32(offset);
            return value == DpxFormat.UndefinedU32 ? null : value;
        }

        /// <summary>Reads a real number; the undefined value (all bits set) and any other NaN are <see langword="null"/>.</summary>
        public float? OptionalR32(int offset)
        {
            var value = BitConverter.UInt32BitsToSingle(U32(offset));
            return float.IsNaN(value) ? null : value;
        }

        public byte? Byte(int offset) => _header[offset] == 0xFF ? null : _header[offset];

        /// <summary>Reads a text field (ISO/IEC 8859-1, ended by a NUL character when it does not fill the field); an empty one is <see langword="null"/>.</summary>
        public string? Ascii(int offset, int length)
        {
            var field = _header.Slice(offset, length);
            var end = field.IndexOf((byte)0);
            if (end >= 0)
            {
                field = field[..end];
            }

            return field.IsEmpty || field[0] == 0xFF ? null : Encoding.Latin1.GetString(field);
        }
    }
}
