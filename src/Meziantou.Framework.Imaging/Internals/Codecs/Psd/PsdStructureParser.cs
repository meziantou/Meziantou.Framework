using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of the sections of an Adobe Photoshop file (Adobe Photoshop File Formats Specification, "Photoshop
/// File Format"), in the three walks of the codec infrastructure: the file header, the color mode data, the image
/// resources, the layer and mask information, and the image data (the composite image Photoshop stores when "Maximize
/// Compatibility" is on).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the sections up to the layer count, which tells whether the composite has a transparency channel.</description></item>
/// <item><description>Full scan: reads every row of every channel of the image data, decompressing the run-length encoded ones; charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: the same walk; each row of a channel goes to the <see cref="PsdDecoder"/>.</description></item>
/// </list>
/// <para>
/// Version 1 is a PSD file (at most 30,000 pixels per side), version 2 a PSB file (the large document format, at most
/// 300,000 pixels per side, whose section lengths and run-length counts are wider). The image data stores the channels
/// one after the other, each one row by row: raw, or compressed row by row with PackBits after a table of the compressed
/// length of every row. A row of PackBits data must give exactly the bytes of a row and use exactly its stored length.
/// ZIP compression, which Photoshop only uses for the layers, is identified and rejected by a load.
/// </para>
/// <para>
/// The image resources the decoder reads are the resolution (1005), the ICC profile (1039), the transparency index of an
/// indexed image (1047), the version information (1057: whether the file has a real composite), EXIF data (1058) and XMP
/// metadata (1060); their bytes are charged to <see cref="ImageResourceLimits.MaxMetadataBytes"/> before they are read.
/// The other resources and the layers are skipped. The layer count is negative when the first channel after the color
/// channels is the transparency of the composite ("merged alpha"); Photoshop writes the layers of 16 and 32-bit
/// documents in an <c>Lr16</c> or <c>Lr32</c> tagged block after the global layer mask information, whose layer count
/// says the same. The bytes after the image data are not read.
/// </para>
/// </remarks>
internal sealed class PsdStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly PsdDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private State _afterSkip;
    private long _skip;
    private long _position;
    private long _sectionRemaining;

    // Image resources
    private int _resourceId;
    private long _resourceSize;
    private bool _resourceCharged;

    // Image data
    private PooledBuffer? _counts;
    private int _countsRead;
    private PooledBuffer? _row;
    private int _channel;
    private int _rowIndex;
    private ImageInfo? _result;

    public PsdStructureParser(ImageCodecContext context, StructureWalk walk, PsdDecoder? decoder = null)
        : base(ImageFormat.Psd)
    {
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Psd);
    }

    private enum State
    {
        Header,
        ColorModeLength,
        ColorTable,
        ResourcesLength,
        ResourceHeader,
        ResourceData,
        LayerSectionLength,
        LayerInfoLength,
        LayerCount,
        GlobalMaskLength,
        TaggedBlockHeader,
        Skip,
        Compression,
        RunLengthCounts,
        Rows,
        Done,
    }

    /// <summary>Gets a value indicating whether the file is a PSB file (version 2).</summary>
    public bool IsLargeDocument { get; private set; }

    /// <summary>Gets the number of channels of the image data.</summary>
    public int ChannelCount { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    /// <summary>Gets the number of bits of a sample: 1, 8, 16 or 32 (floating-point).</summary>
    public int Depth { get; private set; }

    /// <summary>Gets the color mode (<see cref="PsdFormat.ModeRgb"/>, ...).</summary>
    public int Mode { get; private set; }

    /// <summary>Gets the number of color channels of the color mode.</summary>
    public int ColorChannelCount => PsdFormat.GetColorChannelCount(Mode);

    /// <summary>Gets the color table of an indexed image: 256 red, 256 green, then 256 blue values.</summary>
    public byte[]? ColorTable { get; private set; }

    /// <summary>Gets the transparent index of an indexed image (resource 1047), or <see langword="null"/>.</summary>
    public int? TransparentIndex { get; private set; }

    /// <summary>Gets a value indicating whether the version information (resource 1057) says the file has no real composite.</summary>
    public bool HasNoRealComposite { get; private set; }

    /// <summary>Gets a value indicating whether the layer count is negative: the first channel after the color channels is the transparency of the composite.</summary>
    public bool HasMergedAlpha { get; private set; }

    /// <summary>Gets the compression of the image data.</summary>
    public int Compression { get; private set; }

    /// <summary>Gets a value indicating whether the composite has a transparency channel.</summary>
    public bool HasAlpha => Mode switch
    {
        PsdFormat.ModeIndexed => TransparentIndex is not null,
        PsdFormat.ModeBitmap => false,
        _ => HasMergedAlpha && ChannelCount > ColorChannelCount,
    };

    /// <summary>Gets the number of bytes of a row of a channel.</summary>
    public int RowLength => (int)((((long)Width * Depth) + 7) / 8);

    /// <summary>Gets the default pixel format of the image.</summary>
    public PixelFormat DefaultPixelFormat => GetDefaultPixelFormat(Mode, Depth, HasAlpha);

    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the default pixel format of an image of a color mode and depth.</summary>
    public static PixelFormat GetDefaultPixelFormat(int mode, int depth, bool hasAlpha)
    {
        if (depth == 32)
            return DefaultPixelFormats.ForPsdFloatingPoint(mode == PsdFormat.ModeGrayscale, hasAlpha);

        var isGray = mode is PsdFormat.ModeBitmap or PsdFormat.ModeGrayscale or PsdFormat.ModeDuotone;
        return DefaultPixelFormats.ForPsd(isGray, hasAlpha, depth == 16);
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.Header:
                    if (remaining.Length < PsdFormat.HeaderLength)
                        return ParseStatus.NeedMoreData(PsdFormat.HeaderLength);

                    ReadHeader(remaining[..PsdFormat.HeaderLength]);
                    Advance(ref consumed, PsdFormat.HeaderLength);
                    _state = State.ColorModeLength;
                    break;

                case State.ColorModeLength:
                {
                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    var length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
                    Advance(ref consumed, 4);
                    if (Mode == PsdFormat.ModeIndexed)
                    {
                        if (length != PsdFormat.ColorTableLength)
                            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the color mode data of an indexed image has {length} bytes; the color table has {PsdFormat.ColorTableLength}."));

                        _state = State.ColorTable;
                    }
                    else
                    {
                        // The duotone specification (and any data of the other modes) is not used: a duotone image is
                        // read as its grayscale samples
                        StartSkip(length, State.ResourcesLength);
                    }

                    break;
                }

                case State.ColorTable:
                    if (remaining.Length < PsdFormat.ColorTableLength)
                        return ParseStatus.NeedMoreData(PsdFormat.ColorTableLength);

                    ColorTable = remaining[..PsdFormat.ColorTableLength].ToArray();
                    Advance(ref consumed, PsdFormat.ColorTableLength);
                    _state = State.ResourcesLength;
                    break;

                case State.ResourcesLength:
                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    _sectionRemaining = BinaryPrimitives.ReadUInt32BigEndian(remaining);
                    Advance(ref consumed, 4);
                    _state = State.ResourceHeader;
                    break;

                case State.ResourceHeader:
                {
                    if (_sectionRemaining == 0)
                    {
                        // 32-bit documents are linear light; an ICC profile describes them otherwise
                        if (Depth == 32 && _metadata.Metadata.IccProfile is null)
                        {
                            _metadata.Metadata.TransferFunction = ColorTransferFunction.Linear;
                        }

                        _state = State.LayerSectionLength;
                        break;
                    }

                    // Signature, identifier, then a Pascal string padded to an even length, and the size of the data
                    if (remaining.Length < 7)
                        return ParseStatus.NeedMoreData(7);

                    var nameLength = (remaining[6] + 2) & ~1;
                    var headerLength = 6 + nameLength + 4;
                    if (headerLength > _sectionRemaining)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image resource at offset {_position} extends past the end of the image resources section."));

                    if (remaining.Length < headerLength)
                        return ParseStatus.NeedMoreData(headerLength);

                    var signature = remaining[..4];
                    if (!signature.SequenceEqual(PsdFormat.ResourceSignature) && !signature.SequenceEqual("MeSa"u8) && !signature.SequenceEqual("AgHg"u8) && !signature.SequenceEqual("PHUT"u8) && !signature.SequenceEqual("DCSR"u8))
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image resource at offset {_position} does not start with the signature '8BIM'."));

                    _resourceId = signature.SequenceEqual(PsdFormat.ResourceSignature) ? BinaryPrimitives.ReadUInt16BigEndian(remaining[4..]) : -1;
                    _resourceSize = BinaryPrimitives.ReadUInt32BigEndian(remaining[(headerLength - 4)..]);
                    _sectionRemaining -= headerLength;
                    var paddedSize = (_resourceSize + 1) & ~1L;
                    if (_resourceSize > _sectionRemaining)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image resource {_resourceId} at offset {_position} has {_resourceSize} bytes; {_sectionRemaining} are left in the image resources section."));

                    // The padding byte of the last resource may be missing
                    paddedSize = Math.Min(paddedSize, _sectionRemaining);
                    _sectionRemaining -= paddedSize;
                    Advance(ref consumed, headerLength);
                    if (IsReadResource(_resourceId))
                    {
                        _resourceCharged = false;
                        _skip = paddedSize - _resourceSize;
                        _state = State.ResourceData;
                    }
                    else
                    {
                        StartSkip(paddedSize, State.ResourceHeader);
                    }

                    break;
                }

                case State.ResourceData:
                {
                    if (!_resourceCharged)
                    {
                        if (_resourceSize > CheckedSizes.MaxBufferLength)
                            throw CheckedSizes.CreateOverflowException(_context.Limits);

                        _metadata.Charge(_resourceSize);
                        _resourceCharged = true;
                    }

                    var size = (int)_resourceSize;
                    if (remaining.Length < size)
                        return ParseStatus.NeedMoreData(size);

                    ReadResource(_resourceId, remaining[..size]);
                    Advance(ref consumed, size);
                    StartSkip(_skip, State.ResourceHeader);
                    break;
                }

                case State.LayerSectionLength:
                {
                    var lengthSize = IsLargeDocument ? 8 : 4;
                    if (remaining.Length < lengthSize)
                        return ParseStatus.NeedMoreData(lengthSize);

                    _sectionRemaining = ReadLength(remaining, lengthSize);
                    Advance(ref consumed, lengthSize);
                    if (_sectionRemaining == 0)
                    {
                        if (EndLayerSection())
                            return ParseStatus.Complete;

                        break;
                    }

                    if (_sectionRemaining < lengthSize)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the layer and mask information section has {_sectionRemaining} bytes, fewer than the length of its layer info."));

                    _state = State.LayerInfoLength;
                    break;
                }

                case State.LayerInfoLength:
                {
                    var lengthSize = IsLargeDocument ? 8 : 4;
                    if (remaining.Length < lengthSize)
                        return ParseStatus.NeedMoreData(lengthSize);

                    var length = ReadLength(remaining, lengthSize);
                    Advance(ref consumed, lengthSize);
                    _sectionRemaining -= lengthSize;
                    if (length > _sectionRemaining)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the layer info has {length} bytes; {_sectionRemaining} are left in the layer and mask information section."));

                    if (length > 0)
                    {
                        if (length < 2)
                            throw PsdFormat.Invalid("the layer info is shorter than its layer count.");

                        _state = State.LayerCount;
                    }
                    else
                    {
                        _state = _sectionRemaining == 0 ? State.LayerCount : State.GlobalMaskLength;
                        if (_sectionRemaining == 0 && EndLayerSection())
                            return ParseStatus.Complete;
                    }

                    break;
                }

                case State.LayerCount:
                {
                    if (remaining.Length < 2)
                        return ParseStatus.NeedMoreData(2);

                    HasMergedAlpha = BinaryPrimitives.ReadInt16BigEndian(remaining) < 0;
                    Advance(ref consumed, 2);
                    _sectionRemaining -= 2;
                    if (EndLayerSection())
                        return ParseStatus.Complete;

                    break;
                }

                case State.GlobalMaskLength:
                {
                    if (_sectionRemaining < 4)
                        throw PsdFormat.Invalid("the layer and mask information section ends inside the length of the global layer mask info.");

                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    var length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
                    Advance(ref consumed, 4);
                    _sectionRemaining -= 4;
                    if (length > _sectionRemaining)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the global layer mask info has {length} bytes; {_sectionRemaining} are left in the layer and mask information section."));

                    _sectionRemaining -= length;
                    StartSkip(length, State.TaggedBlockHeader);
                    break;
                }

                case State.TaggedBlockHeader:
                {
                    // A section shorter than the header of a block holds padding
                    var lengthSize = 4;
                    if (_sectionRemaining < 12)
                    {
                        if (EndLayerSection())
                            return ParseStatus.Complete;

                        break;
                    }

                    if (remaining.Length < 8)
                        return ParseStatus.NeedMoreData(8);

                    var signature = remaining[..4];
                    if (!signature.SequenceEqual(PsdFormat.ResourceSignature) && !signature.SequenceEqual(PsdFormat.LargeBlockSignature))
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the tagged block at offset {_position} does not start with the signature '8BIM' or '8B64'."));

                    var key = remaining.Slice(4, 4);
                    if (IsLargeDocument && PsdFormat.HasLargeLength(key))
                    {
                        lengthSize = 8;
                    }

                    if (_sectionRemaining < 8 + lengthSize)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the tagged block at offset {_position} extends past the end of the layer and mask information section."));

                    if (remaining.Length < 8 + lengthSize)
                        return ParseStatus.NeedMoreData(8 + lengthSize);

                    var length = ReadLength(remaining[8..], lengthSize);
                    var isLayerInfo = PsdFormat.IsLayerInfoBlock(key);
                    Advance(ref consumed, 8 + lengthSize);
                    _sectionRemaining -= 8 + lengthSize;
                    if (length > _sectionRemaining)
                        throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the tagged block at offset {_position - 8 - lengthSize} has {length} bytes; {_sectionRemaining} are left in the layer and mask information section."));

                    if (isLayerInfo && length >= 2)
                    {
                        _state = State.LayerCount;
                        break;
                    }

                    _sectionRemaining -= length;
                    StartSkip(length, State.TaggedBlockHeader);
                    break;
                }

                case State.Skip:
                {
                    if (_skip > 0)
                    {
                        var count = (int)Math.Min(_skip, remaining.Length);
                        Advance(ref consumed, count);
                        _skip -= count;
                        if (_skip > 0)
                        {
                            _context.CancellationToken.ThrowIfCancellationRequested();
                            return ParseStatus.NeedMoreData(1);
                        }
                    }

                    _state = _afterSkip;
                    break;
                }

                case State.Compression:
                {
                    if (remaining.Length < 2)
                        return ParseStatus.NeedMoreData(2);

                    Compression = BinaryPrimitives.ReadUInt16BigEndian(remaining);
                    Advance(ref consumed, 2);
                    if (StartImageData())
                        return ParseStatus.Complete;

                    break;
                }

                case State.RunLengthCounts:
                {
                    var table = _counts!.Span;
                    var count = Math.Min(table.Length - _countsRead, remaining.Length);
                    remaining[..count].CopyTo(table[_countsRead..]);
                    _countsRead += count;
                    Advance(ref consumed, count);
                    if (_countsRead < table.Length)
                        return ParseStatus.NeedMoreData(1);

                    _state = State.Rows;
                    break;
                }

                case State.Rows:
                {
                    ReadOnlySpan<byte> row;
                    if (Compression == PsdFormat.CompressionRaw)
                    {
                        var length = RowLength;
                        if (remaining.Length < length)
                            return ParseStatus.NeedMoreData(length);

                        row = remaining[..length];
                        Advance(ref consumed, length);
                    }
                    else
                    {
                        var length = GetCompressedRowLength(_channel, _rowIndex);
                        if (remaining.Length < length)
                            return ParseStatus.NeedMoreData(length);

                        var unpacked = _row!.Span[..RowLength];
                        UnpackBits(remaining[..length], unpacked);
                        row = unpacked;
                        Advance(ref consumed, length);
                    }

                    if (EndRow(row))
                        return ParseStatus.Complete;

                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The PSD data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _counts?.Dispose();
            _counts = null;
            _row?.Dispose();
            _row = null;
        }

        base.Dispose(disposing);
    }

    private static long ReadLength(ReadOnlySpan<byte> data, int size)
    {
        if (size == 4)
            return BinaryPrimitives.ReadUInt32BigEndian(data);

        var value = BinaryPrimitives.ReadUInt64BigEndian(data);
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    private static bool IsReadResource(int id) => id is PsdFormat.ResourceResolutionInfo or PsdFormat.ResourceIccProfile or PsdFormat.ResourceTransparencyIndex or PsdFormat.ResourceVersionInfo or PsdFormat.ResourceExif or PsdFormat.ResourceXmp;

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    private void StartSkip(long count, State next)
    {
        _skip = count;
        _afterSkip = next;
        _state = State.Skip;
    }

    private void ReadHeader(ReadOnlySpan<byte> header)
    {
        if (!header.StartsWith(PsdFormat.Signature))
            throw PsdFormat.Invalid("the file does not start with the signature '8BPS'.");

        var version = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);
        if (version is not (PsdFormat.PsdVersion or PsdFormat.PsbVersion))
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the version is {version}; it must be 1 (PSD) or 2 (PSB)."));

        if (header.Slice(6, 6).ContainsAnyExcept((byte)0))
            throw PsdFormat.Invalid("the reserved bytes of the file header are not zero.");

        IsLargeDocument = version == PsdFormat.PsbVersion;
        var channels = BinaryPrimitives.ReadUInt16BigEndian(header[12..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[14..]);
        var width = BinaryPrimitives.ReadUInt32BigEndian(header[18..]);
        var depth = BinaryPrimitives.ReadUInt16BigEndian(header[22..]);
        var mode = BinaryPrimitives.ReadUInt16BigEndian(header[24..]);
        if (channels is < 1 or > PsdFormat.MaxChannels)
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the file has {channels} channels; it must have 1 to {PsdFormat.MaxChannels}."));

        var maxDimension = IsLargeDocument ? PsdFormat.MaxPsbDimension : PsdFormat.MaxPsdDimension;
        if (width is 0 || height is 0 || width > maxDimension || height > maxDimension)
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image is {width}x{height} pixels; each side must be 1 to {maxDimension} pixels in a {(IsLargeDocument ? "PSB" : "PSD")} file."));

        if (depth is not (1 or 8 or 16 or 32))
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the depth is {depth} bits; it must be 1, 8, 16 or 32."));

        if (mode is not (PsdFormat.ModeBitmap or PsdFormat.ModeGrayscale or PsdFormat.ModeIndexed or PsdFormat.ModeRgb or PsdFormat.ModeCmyk or PsdFormat.ModeMultichannel or PsdFormat.ModeDuotone or PsdFormat.ModeLab))
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the color mode is {mode}, which is not defined."));

        // Bitmap images have 1-bit samples, and only them; indexed images 8-bit indexes
        if ((mode == PsdFormat.ModeBitmap) != (depth == 1) || (mode == PsdFormat.ModeIndexed && depth != 8))
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"a {PsdFormat.GetModeName(mode)} image cannot have a depth of {depth} bits."));

        if (mode == PsdFormat.ModeMultichannel)
            throw PsdFormat.Unsupported("The PSD file is a multichannel document, whose channels have no color interpretation: only bitmap, grayscale, duotone, indexed, RGB, CMYK and Lab documents are read.", "Multichannel color mode");

        if (mode == PsdFormat.ModeCmyk && depth == 32)
            throw PsdFormat.Unsupported("The PSD file stores 32-bit CMYK samples, which the CMYK pixel formats and conversions do not read: only 8 and 16-bit CMYK documents are read.", "32-bit CMYK samples");

        if (mode == PsdFormat.ModeLab && depth != 8)
            throw PsdFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The PSD file stores {depth}-bit Lab samples: only 8-bit Lab documents are read."), string.Create(CultureInfo.InvariantCulture, $"{depth}-bit Lab samples"));

        if (mode == PsdFormat.ModeDuotone && depth == 32)
            throw PsdFormat.Unsupported("The PSD file is a 32-bit duotone document: only 8 and 16-bit duotone documents are read.", "32-bit duotone samples");

        if (channels < PsdFormat.GetColorChannelCount(mode))
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"a {PsdFormat.GetModeName(mode)} image has {PsdFormat.GetColorChannelCount(mode)} color channels, but the file has {channels} channels."));

        _context.Limits.EnsureCanvasWithinLimits((int)width, (int)height);
        ChannelCount = channels;
        Width = (int)width;
        Height = (int)height;
        Depth = depth;
        Mode = mode;
    }

    private void ReadResource(int id, ReadOnlySpan<byte> data)
    {
        switch (id)
        {
            case PsdFormat.ResourceResolutionInfo:
                // The horizontal and vertical resolutions are fixed-point numbers of pixels per inch, whatever unit
                // Photoshop displays them in
                if (data.Length >= 16)
                {
                    var horizontal = BinaryPrimitives.ReadInt32BigEndian(data) / 65536.0;
                    var vertical = BinaryPrimitives.ReadInt32BigEndian(data[8..]) / 65536.0;
                    if (horizontal > 0 && vertical > 0)
                    {
                        _metadata.TrySetResolution(new ImageResolution(horizontal, vertical));
                    }
                }

                break;

            case PsdFormat.ResourceIccProfile:
            {
                IccProfileColorSpace? expected = Mode switch
                {
                    PsdFormat.ModeBitmap or PsdFormat.ModeGrayscale or PsdFormat.ModeDuotone => IccProfileColorSpace.Gray,
                    PsdFormat.ModeRgb or PsdFormat.ModeIndexed => IccProfileColorSpace.Rgb,
                    PsdFormat.ModeCmyk => IccProfileColorSpace.Cmyk,
                    _ => null,
                };

                if (expected is { } space)
                {
                    _metadata.TryAdoptIccProfile(data.ToArray(), space);
                }

                break;
            }

            case PsdFormat.ResourceTransparencyIndex:
                if (Mode == PsdFormat.ModeIndexed && data.Length >= 2)
                {
                    var index = BinaryPrimitives.ReadUInt16BigEndian(data);
                    if (index < 256)
                    {
                        TransparentIndex = index;
                    }
                }

                break;

            case PsdFormat.ResourceVersionInfo:
                // A version number, then whether the file has real merged data
                if (data.Length >= 5)
                {
                    HasNoRealComposite = data[4] == 0;
                }

                break;

            case PsdFormat.ResourceExif:
                _metadata.TryAdoptExif(data, alreadyCharged: true);
                break;

            case PsdFormat.ResourceXmp:
                _metadata.TryAdoptXmp(data, alreadyCharged: true);
                break;
        }
    }

    /// <returns><see langword="true"/> when the header walk is complete.</returns>
    private bool EndLayerSection()
    {
        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            _state = State.Done;
            return true;
        }

        StartSkip(_sectionRemaining, State.Compression);
        _sectionRemaining = 0;
        return false;
    }

    /// <returns><see langword="true"/> when the parser must return (a full scan of ZIP data, or a yield).</returns>
    private bool StartImageData()
    {
        if (Compression is PsdFormat.CompressionZip or PsdFormat.CompressionZipWithPrediction)
        {
            if (_walk == StructureWalk.Decode)
                throw PsdFormat.Unsupported("The image data of the PSD file is ZIP-compressed: only raw and RLE (PackBits) image data is read.", "ZIP-compressed image data");

            // The compressed data is not examined: its end is the end of the file
            _result = CreateInfo(ImageIdentifyMode.FullScan);
            _context.Tracker.ChargeScannedFrame();
            _state = State.Done;
            return true;
        }

        if (Compression is not (PsdFormat.CompressionRaw or PsdFormat.CompressionRle))
            throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the compression of the image data is {Compression}, which is not defined."));

        var scope = _context.Scope;
        if (Compression == PsdFormat.CompressionRle)
        {
            var countSize = IsLargeDocument ? 4 : 2;
            if (!CheckedSizes.TryMultiply((long)ChannelCount * Height, countSize, out var tableLength) || tableLength > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            _counts = scope.Rent((int)tableLength, AllocationKind.DecoderState, clear: false);
            _row = scope.Rent(RowLength, AllocationKind.DecoderState, clear: false);
            _state = State.RunLengthCounts;
        }
        else
        {
            _state = State.Rows;
        }

        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
            return false;
        }

        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _decoder!.OnHeaderComplete(this);
        return _context.TryConsumeYield();
    }

    private int GetCompressedRowLength(int channel, int row)
    {
        var index = ((long)channel * Height) + row;
        var table = _counts!.Span;
        if (IsLargeDocument)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(table[(int)(index * 4)..]);
            if (length > CheckedSizes.MaxBufferLength)
                throw PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the compressed row {row} of the channel {channel} has {length} bytes."));

            return (int)length;
        }

        return BinaryPrimitives.ReadUInt16BigEndian(table[(int)(index * 2)..]);
    }

    /// <summary>
    /// Decompresses a row of PackBits data (Apple Technical Note TN1023): a header byte of 0 to 127 is followed by as many
    /// bytes plus one, a header of -1 to -127 by one byte repeated one minus the header times, and -128 is skipped.
    /// </summary>
    private void UnpackBits(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var i = 0;
        var written = 0;
        while (i < source.Length)
        {
            var header = (sbyte)source[i++];
            if (header >= 0)
            {
                var count = header + 1;
                if (count > source.Length - i)
                    throw RowError("a literal extends past the stored length of the row");

                if (count > destination.Length - written)
                    throw RowError("a literal extends past the end of the row");

                source.Slice(i, count).CopyTo(destination[written..]);
                i += count;
                written += count;
            }
            else if (header != -128)
            {
                var count = 1 - header;
                if (i == source.Length)
                    throw RowError("a run has no byte to repeat");

                if (count > destination.Length - written)
                    throw RowError("a run extends past the end of the row");

                destination.Slice(written, count).Fill(source[i++]);
                written += count;
            }
        }

        if (written != destination.Length)
            throw RowError(string.Create(CultureInfo.InvariantCulture, $"the row has {written} bytes instead of {destination.Length}"));
    }

    private InvalidImageContentException RowError(string message)
        => PsdFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"in the RLE data of the row {_rowIndex} of the channel {_channel} at offset {_position}, {message}."));

    /// <returns><see langword="true"/> when the last row of the last channel was read.</returns>
    private bool EndRow(ReadOnlySpan<byte> row)
    {
        _decoder?.OnRow(this, _channel, _rowIndex, row);
        _rowIndex++;
        if (_rowIndex == Height)
        {
            _rowIndex = 0;
            _channel++;
        }

        if (_channel < ChannelCount)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _decoder?.OnEnd(this);
        return true;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var hasAlpha = HasAlpha;
        var colorModel = Mode switch
        {
            PsdFormat.ModeIndexed => ImageColorModel.Indexed,
            PsdFormat.ModeCmyk => ImageColorModel.Cmyk,
            PsdFormat.ModeLab => ImageColorModel.CieLab,
            PsdFormat.ModeRgb => hasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb,
            _ => hasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale,
        };

        return new(
            ImageFormat.Psd,
            Size,
            DefaultPixelFormat,
            colorModel,
            bitsPerComponent: Depth,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: hasAlpha,
            animation: null,
            _metadata.Metadata,
            mode);
    }
}
