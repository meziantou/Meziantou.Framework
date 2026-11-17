using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental Kodak Cineon reader (Image File Format Proposal for Digital Pictures, draft 4.5): the header, then the lines
/// of the image, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the generic section (1,024 bytes) and, when the image data start after it, the motion-picture section (to byte 2,048).</description></item>
/// <item><description>Full scan: reads every line, charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: the same walk; each line goes to the <see cref="RawRowDecoder"/>.</description></item>
/// </list>
/// <para>
/// The magic number gives the byte order of every field and of the image data. Images of one channel (gray) or three
/// (RGB) whose channels have the same size and depth (1 to 16 bits), interleaved by pixel, unsigned, are decoded. The image
/// data start at the offset of field 1.2, after the generic section; the bytes before it (the user-defined section) are
/// skipped. Each line is followed by its end-of-line padding; the bytes after the last line are not read.
/// </para>
/// <para>
/// An orientation code above 7, a number of channels outside 1 to 8, an empty image, a depth of 0, data that start inside
/// the generic section, a cell smaller than a sample and truncation are <see cref="InvalidImageContentException"/>. Other
/// numbers of channels, channels of different sizes or depths, depths above 16 bits, line or channel interleaving, signed
/// samples and bit-field packing of depths other than 8 and 16 bits are <see cref="UnsupportedImageFeatureException"/>,
/// reported by the header walk.
/// </para>
/// </remarks>
internal sealed class CineonStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly RawRowDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private int _headerLength;
    private long _dataOffset;
    private long _eolPadding;
    private int _width;
    private int _height;
    private int _channels;
    private int _bits;
    private CineonRowReader? _reader;
    private CineonHeader? _header;
    private PixelFormat _defaultPixelFormat;

    private PooledBuffer? _storedRow;
    private PooledBuffer? _samples;
    private PooledBuffer? _sourceRow;
    private int _storedFilled;
    private long _skipRemaining;
    private int _rowIndex;
    private ImageInfo? _result;

    public CineonStructureParser(ImageCodecContext context, StructureWalk walk, RawRowDecoder? decoder = null)
        : base(ImageFormat.Cineon)
    {
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Cineon);
    }

    private enum State
    {
        GenericHeader,
        IndustryHeader,
        Skip,
        Lines,
        Done,
    }

    public Size Size => new(_width, _height);

    /// <summary>Gets the metadata: the orientation and <see cref="ImageMetadata.CineonHeader"/>.</summary>
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
                    if (buffer.Length < CineonFormat.GenericHeaderLength)
                        return ParseStatus.NeedMoreData(CineonFormat.GenericHeaderLength);

                    ReadGenericHeader(buffer[..CineonFormat.GenericHeaderLength]);
                    if (_dataOffset >= CineonFormat.FullHeaderLength)
                    {
                        _state = State.IndustryHeader;
                        break;
                    }

                    _headerLength = CineonFormat.GenericHeaderLength;
                    if (CompleteHeader(buffer[.._headerLength], ref consumed))
                        return ParseStatus.Complete;

                    break;
                }

                case State.IndustryHeader:
                {
                    if (buffer.Length < CineonFormat.FullHeaderLength)
                        return ParseStatus.NeedMoreData(CineonFormat.FullHeaderLength);

                    _headerLength = CineonFormat.FullHeaderLength;
                    if (CompleteHeader(buffer[.._headerLength], ref consumed))
                        return ParseStatus.Complete;

                    break;
                }

                case State.Skip:
                {
                    var remaining = buffer.Length - consumed;
                    if (remaining == 0)
                        return ParseStatus.NeedMoreData(1);

                    var take = (int)Math.Min(remaining, _skipRemaining);
                    consumed += take;
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

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The Cineon data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _storedRow?.Dispose();
            _storedRow = null;
            _samples?.Dispose();
            _samples = null;
            _sourceRow?.Dispose();
            _sourceRow = null;
        }

        base.Dispose(disposing);
    }

    private void ReadGenericHeader(ReadOnlySpan<byte> header)
    {
        var bigEndian = BinaryPrimitives.ReadUInt32BigEndian(header) == CineonFormat.Magic;
        if (!bigEndian && BinaryPrimitives.ReadUInt32LittleEndian(header) != CineonFormat.Magic)
            throw CineonFormat.Invalid("the magic number is not 802A5FD7.");

        var fields = new CineonFields(header, bigEndian);
        var orientation = header[192];
        if (orientation > 7)
            throw CineonFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the orientation code is {orientation}; the format defines 0 to 7."));

        var channels = header[193];
        if (channels is < 1 or > 8)
            throw CineonFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the file declares {channels} channels; a file has 1 to 8."));

        var designators = new ushort[channels];
        var minimumData = new float?[channels];
        var minimumQuantities = new float?[channels];
        var maximumData = new float?[channels];
        var maximumQuantities = new float?[channels];
        uint width = 0;
        uint height = 0;
        var bits = 0;
        for (var c = 0; c < channels; c++)
        {
            var offset = CineonFormat.FirstChannelOffset + (c * CineonFormat.ChannelLength);
            designators[c] = (ushort)((header[offset] << 8) | header[offset + 1]);
            var channelBits = header[offset + 2];
            var channelWidth = fields.U32(offset + 4);
            var channelHeight = fields.U32(offset + 8);
            minimumData[c] = fields.OptionalR32(offset + 12);
            minimumQuantities[c] = fields.OptionalR32(offset + 16);
            maximumData[c] = fields.OptionalR32(offset + 20);
            maximumQuantities[c] = fields.OptionalR32(offset + 24);
            if (c == 0)
            {
                width = channelWidth;
                height = channelHeight;
                bits = channelBits;
            }
            else if (channelWidth != width || channelHeight != height || channelBits != bits)
            {
                throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The channel {c + 1} of the Cineon image is {channelWidth} × {channelHeight} pixels of {channelBits} bits, and the first one {width} × {height} pixels of {bits} bits: only channels of the same size and depth are decoded."), "Channels of different sizes or depths");
            }
        }

        if (width is 0 or uint.MaxValue || height is 0 or uint.MaxValue)
            throw CineonFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image is {width} pixels per line and {height} lines."));

        if (bits == 0)
            throw CineonFormat.Invalid("the channels have 0 bits per sample.");

        if (bits > 16)
            throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The Cineon image has {bits}-bit samples: only 1 to 16-bit samples are decoded."), "Samples of more than 16 bits");

        if (channels is not (1 or 3))
            throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The Cineon image has {channels} channels: only gray (one channel) and RGB (three channels) images are decoded."), "Images of " + channels.ToString(CultureInfo.InvariantCulture) + " channels");

        var interleave = header[680];
        var packing = header[681];
        if (channels > 1 && interleave != 0)
            throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The Cineon image has the data interleave {interleave}: only pixel interleave (0) is decoded."), "Data interleave " + interleave.ToString(CultureInfo.InvariantCulture));

        if (header[682] == 1)
            throw CineonFormat.Unsupported("The Cineon image has signed samples: only unsigned samples are decoded.", "Signed samples");

        if (!CineonRowReader.IsSupported(packing, bits))
        {
            if ((packing & 0x7F) is >= 1 and <= 6)
                throw CineonFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the packing {packing} has cells smaller than the {bits}-bit samples."));

            throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The Cineon image has the packing {packing} for {bits}-bit samples: bit fields are only decoded for 8 and 16-bit samples, and packings 7 to 127 are user defined."), "Packing " + packing.ToString(CultureInfo.InvariantCulture));
        }

        var limits = _context.Limits;
        if (width > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

        if (height > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, height);

        limits.EnsureCanvasWithinLimits((int)width, (int)height);
        if ((long)width * channels * 4 > CheckedSizes.MaxBufferLength || (long)width * 16 > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _dataOffset = fields.U32(4);
        if (_dataOffset < CineonFormat.GenericHeaderLength || _dataOffset == uint.MaxValue)
            throw CineonFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image data start at offset {_dataOffset}, inside the generic section ({CineonFormat.GenericHeaderLength} bytes)."));

        var eolPadding = fields.U32(684);
        _eolPadding = eolPadding == uint.MaxValue ? 0 : eolPadding;
        _width = (int)width;
        _height = (int)height;
        _channels = channels;
        _bits = bits;
        _reader = new CineonRowReader(bigEndian, channels, bits, _width, packing, GetChannelOrder(designators));
        if (_reader.StoredLineLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _defaultPixelFormat = DefaultPixelFormats.ForCineon(channels == 1, bits);
        var chromaticities = new float[8];
        var hasChromaticities = true;
        for (var i = 0; i < 8; i++)
        {
            if (fields.OptionalR32(420 + (i * 4)) is { } value)
            {
                chromaticities[i] = value;
            }
            else
            {
                hasChromaticities = false;
            }
        }

        _header = new CineonHeader
        {
            Version = fields.Ascii(24, 8) ?? "",
            IsBigEndian = bigEndian,
            ChannelDesignators = Array.AsReadOnly(designators),
            BitsPerSample = bits,
            MinimumDataValues = Array.AsReadOnly(minimumData),
            MinimumQuantities = Array.AsReadOnly(minimumQuantities),
            MaximumDataValues = Array.AsReadOnly(maximumData),
            MaximumQuantities = Array.AsReadOnly(maximumQuantities),
            Chromaticities = hasChromaticities ? Array.AsReadOnly(chromaticities) : null,
            Packing = packing,
            IsNegative = header[683] == 1,
            Label = fields.Ascii(452, 200),
            FileName = fields.Ascii(32, 100),
            CreationDate = fields.Ascii(132, 12),
            CreationTime = fields.Ascii(144, 12),
            XOffset = fields.OptionalS32(712),
            YOffset = fields.OptionalS32(716),
            SourceFileName = fields.Ascii(720, 100),
            SourceCreationDate = fields.Ascii(820, 12),
            SourceCreationTime = fields.Ascii(832, 12),
            InputDevice = fields.Ascii(844, 64),
            InputDeviceModel = fields.Ascii(908, 32),
            InputDeviceSerialNumber = fields.Ascii(940, 32),
            XDevicePitch = fields.OptionalR32(972),
            YDevicePitch = fields.OptionalR32(976),
            Gamma = fields.OptionalR32(980),
        };

        _metadata.Metadata.Orientation = CineonFormat.ToExifOrientation(orientation);
    }

    /// <summary>
    /// Gets the channel of the file that holds red, green and blue: the order of their designators when they are the
    /// printing densities (1, 2, 3) or the CCIR XA/11 colors (4, 5, 6) of the universal metric, the order of the file otherwise.
    /// </summary>
    internal static int[] GetChannelOrder(ReadOnlySpan<ushort> designators)
    {
        if (designators.Length != 3)
            return [0];

        foreach (var first in (ReadOnlySpan<int>)[1, 4])
        {
            var order = new int[3];
            var found = 0;
            for (var c = 0; c < 3; c++)
            {
                var code = designators[c] - first;
                if (code is >= 0 and < 3 && (found & (1 << code)) == 0)
                {
                    order[code] = c;
                    found |= 1 << code;
                }
            }

            if (found == 7)
                return order;
        }

        return [0, 1, 2];
    }

    private bool CompleteHeader(ReadOnlySpan<byte> header, ref int consumed)
    {
        if (_headerLength == CineonFormat.FullHeaderLength)
        {
            var fields = new CineonFields(header, _header!.IsBigEndian);
            var manufacturer = fields.Byte(1024);
            var filmType = fields.Byte(1025);
            var perforations = fields.Byte(1026);
            var prefix = fields.OptionalU32(1028);
            var count = fields.OptionalU32(1032);
            var edgeCode = manufacturer is { } m && filmType is { } t && perforations is { } p && prefix is { } x && count is { } n
                ? string.Create(CultureInfo.InvariantCulture, $"{m:D2}{t:D2}{p:D2}{x:D6}{n:D4}")
                : null;
            var header0 = _header;
            _header = new CineonHeader
            {
                Version = header0.Version,
                IsBigEndian = header0.IsBigEndian,
                ChannelDesignators = header0.ChannelDesignators,
                BitsPerSample = header0.BitsPerSample,
                MinimumDataValues = header0.MinimumDataValues,
                MinimumQuantities = header0.MinimumQuantities,
                MaximumDataValues = header0.MaximumDataValues,
                MaximumQuantities = header0.MaximumQuantities,
                Chromaticities = header0.Chromaticities,
                Packing = header0.Packing,
                IsNegative = header0.IsNegative,
                Label = header0.Label,
                FileName = header0.FileName,
                CreationDate = header0.CreationDate,
                CreationTime = header0.CreationTime,
                XOffset = header0.XOffset,
                YOffset = header0.YOffset,
                SourceFileName = header0.SourceFileName,
                SourceCreationDate = header0.SourceCreationDate,
                SourceCreationTime = header0.SourceCreationTime,
                InputDevice = header0.InputDevice,
                InputDeviceModel = header0.InputDeviceModel,
                InputDeviceSerialNumber = header0.InputDeviceSerialNumber,
                XDevicePitch = header0.XDevicePitch,
                YDevicePitch = header0.YDevicePitch,
                Gamma = header0.Gamma,
                FilmEdgeCode = edgeCode,
                FilmFormat = fields.Ascii(1036, 32),
                FramePosition = fields.OptionalU32(1068),
                FrameRate = fields.OptionalR32(1072),
                FrameAttribute = fields.Ascii(1076, 32),
                SlateInformation = fields.Ascii(1108, 200),
            };
        }

        consumed += _headerLength;
        _metadata.Metadata.CineonHeader = _header;
        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            _state = State.Done;
            return true;
        }

        var reader = _reader!;
        _storedRow = _context.Scope.Rent((int)reader.StoredLineLength, AllocationKind.DecoderState, clear: false);
        _skipRemaining = _dataOffset - _headerLength;
        _state = _skipRemaining > 0 ? State.Skip : State.Lines;
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
            return false;
        }

        _samples = _context.Scope.Rent(reader.SamplesPerLine * 4, AllocationKind.DecoderState, clear: false);
        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _decoder!.OnHeaderComplete(Size, _defaultPixelFormat, reader.SourcePixelFormat);
        _sourceRow = _context.Scope.Rent(_width * PixelFormats.GetBytesPerPixel(reader.SourcePixelFormat), AllocationKind.DecoderState, clear: false);
        return _context.TryConsumeYield();
    }

    private ParseStatus ReadLines(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var lineLength = (int)_reader!.StoredLineLength;
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
                    consumed += lineLength;
                }
                else
                {
                    var take = Math.Min(remaining.Length, lineLength - _storedFilled);
                    remaining[..take].CopyTo(_storedRow!.Span[_storedFilled..]);
                    _storedFilled += take;
                    consumed += take;
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
                    _state = State.Skip;
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
        var samples = unsafe(MemoryMarshal.Cast<byte, uint>(_samples!.Span[..(reader.SamplesPerLine * 4)]));
        if (_rowIndex > 0)
        {
            if (!lease.IsActive)
            {
                lease = decoder.LeaseRows();
            }

            var destination = decoder.GetDirectRow(lease, _rowIndex);
            if (!destination.IsEmpty)
            {
                reader.ReadRow(line, destination, samples);
                return;
            }
        }

        var source = _sourceRow!.Span[..(_width * PixelFormats.GetBytesPerPixel(reader.SourcePixelFormat))];
        reader.ReadRow(line, source, samples);
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
            ImageFormat.Cineon,
            Size,
            _defaultPixelFormat,
            _channels == 1 ? ImageColorModel.Grayscale : ImageColorModel.Rgb,
            _bits,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: false,
            animation: null,
            _metadata.Metadata,
            mode);

    /// <summary>Reads the fields of the header in the byte order of the file.</summary>
    private readonly ref struct CineonFields
    {
        private readonly ReadOnlySpan<byte> _header;
        private readonly bool _bigEndian;

        public CineonFields(ReadOnlySpan<byte> header, bool bigEndian)
        {
            _header = header;
            _bigEndian = bigEndian;
        }

        public uint U32(int offset) => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(_header[offset..]) : BinaryPrimitives.ReadUInt32LittleEndian(_header[offset..]);

        public uint? OptionalU32(int offset)
        {
            var value = U32(offset);
            return value == uint.MaxValue ? null : value;
        }

        public int? OptionalS32(int offset)
        {
            var value = (int)U32(offset);
            return value == int.MinValue ? null : value;
        }

        /// <summary>Reads a real number; the undefined value (positive infinity) and NaN are <see langword="null"/>.</summary>
        public float? OptionalR32(int offset)
        {
            var value = BitConverter.UInt32BitsToSingle(U32(offset));
            return float.IsNaN(value) || float.IsPositiveInfinity(value) ? null : value;
        }

        public byte? Byte(int offset) => _header[offset] == 0xFF ? null : _header[offset];

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
