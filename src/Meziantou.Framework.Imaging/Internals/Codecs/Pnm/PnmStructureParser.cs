using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of one Netpbm image (PBM, PGM, PPM and PAM) in the three walks of the codec infrastructure: the
/// magic number, the header (white space, comments and tokens, or PAM header lines up to <c>ENDHDR</c>) and the raster.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: stops once the header is complete, without consuming any raster byte.</description></item>
/// <item><description>Full scan: also consumes the raster of the first image and charges it to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: expands each row to <see cref="PnmImageHeader.PixelFormat"/> and writes it to the decoder.</description></item>
/// </list>
/// <para>
/// The header ends at exactly one white-space byte after the last token (<c>ENDHDR</c> and its line feed for PAM), so no
/// byte of a binary raster is ever consumed as header white space. Header state is a handful of integers and, for PAM, one
/// bounded line buffer; the whole header may not exceed <see cref="PnmFormat.MaxHeaderLength"/> bytes, so adversarial
/// comments and white space cannot make the walk unbounded.
/// </para>
/// <para>
/// Netpbm files may concatenate several images. This version reads the first one and stops: the bytes after its raster are
/// never read, and a concatenation is never reported as an animation.
/// </para>
/// </remarks>
internal sealed class PnmStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly PnmDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;
    private readonly byte[] _line = new byte[PnmFormat.MaxHeaderLineLength];

    private State _state;
    private PnmVariant _variant;
    private PnmImageHeader _header;
    private PnmRowExpander? _expander;
    private long _headerBytes;
    private int _lineLength;
    private int _tokenIndex;
    private int _tokenValue;
    private bool _inNumber;
    private bool _inComment;
    private int _width;
    private int _height;
    private int _maxValue = -1;
    private int _depth = -1;
    private PnmTupleType? _tupleType;
    private int _rowIndex;
    private int _storedFilled;
    private int _sampleIndex;
    private int _plainValue;
    private bool _plainInNumber;
    private bool _plainInComment;
    private bool _plainTerminated = true;
    private PooledBuffer? _storedRow;
    private PooledBuffer? _sourceRow;
    private ImageInfo? _result;

    public PnmStructureParser(ImageCodecContext context, StructureWalk walk, PnmDecoder? decoder = null)
        : base(ImageFormat.Pnm)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Pnm);
    }

    private enum State
    {
        Magic,
        Tokens,
        PamLines,
        Raster,
        PlainTerminator,
        Done,
    }

    /// <summary>Gets the resolved header, available once the header walk completed.</summary>
    public PnmImageHeader Header => _header;

    /// <summary>Gets the canvas size.</summary>
    public Size Size => new(_header.Width, _header.Height);

    /// <summary>Gets the metadata: Netpbm files store none.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the decoded representation, which is also the layout of the rows handed to the decoder.</summary>
    public PixelFormat SourcePixelFormat => _header.PixelFormat;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.Magic:
                {
                    if (buffer.Length < 3)
                        return ParseStatus.NeedMoreData(3);

                    if (!PnmCodec.MatchesSignature(buffer))
                        throw PnmFormat.Invalid("The Netpbm magic number is missing: 'P1' to 'P7' followed by white space is expected.");

                    _variant = (PnmVariant)(buffer[1] - '0');

                    // The white space after the magic number is consumed with it; the header tokens follow
                    consumed += 3;
                    _headerBytes += 3;
                    _state = _variant == PnmVariant.ArbitraryMap ? State.PamLines : State.Tokens;
                    break;
                }

                case State.Tokens:
                {
                    var status = ReadTokens(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    var tupleType = _variant switch
                    {
                        PnmVariant.PlainBitmap or PnmVariant.BinaryBitmap => PnmTupleType.BlackAndWhite,
                        PnmVariant.PlainGrayMap or PnmVariant.BinaryGrayMap => PnmTupleType.Grayscale,
                        _ => PnmTupleType.Rgb,
                    };

                    if (_variant is PnmVariant.PlainBitmap or PnmVariant.BinaryBitmap)
                    {
                        _maxValue = 1;
                    }

                    CompleteHeader(tupleType);
                    if (_walk == StructureWalk.Header)
                        return CompleteHeaderWalk();

                    _state = State.Raster;
                    if (!StartRaster())
                        return ParseStatus.Complete;

                    break;
                }

                case State.PamLines:
                {
                    var status = ReadPamLines(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    CompleteHeader(ResolvePamTupleType());
                    if (_walk == StructureWalk.Header)
                        return CompleteHeaderWalk();

                    _state = State.Raster;
                    if (!StartRaster())
                        return ParseStatus.Complete;

                    break;
                }

                case State.Raster:
                {
                    var status = _header.IsPlain ? ReadPlainRaster(buffer, isEndOfInput, ref consumed) : ReadBinaryRaster(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    if (_plainTerminated)
                        return CompleteImage();

                    _state = State.PlainTerminator;
                    break;
                }

                case State.PlainTerminator:
                {
                    // Every character of a plain PBM raster is one pixel, so the last sample is not followed by a consumed
                    // separator: the single white-space byte that ends the raster is consumed here (one byte of lookahead,
                    // like the white space that ends a binary header), so that reading an image consumes the same bytes
                    // whatever the input is.
                    if (consumed >= buffer.Length)
                    {
                        if (!isEndOfInput)
                            return ParseStatus.NeedMoreData(1);
                    }
                    else if (PnmFormat.IsWhiteSpace(buffer[consumed]))
                    {
                        consumed++;
                    }

                    return CompleteImage();
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The PNM data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _storedRow?.Dispose();
            _storedRow = null;
            _sourceRow?.Dispose();
            _sourceRow = null;
        }

        base.Dispose(disposing);
    }

    private ParseStatus CompleteImage()
    {
        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _decoder?.OnEnd(this);
        return ParseStatus.Complete;
    }

    private ParseStatus CompleteHeaderWalk()
    {
        _result = CreateInfo(ImageIdentifyMode.Header);
        _state = State.Done;
        return ParseStatus.Complete;
    }

    private void ChargeHeaderByte()
    {
        if (++_headerBytes > PnmFormat.MaxHeaderLength)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Netpbm header is longer than {PnmFormat.MaxHeaderLength} bytes (white space and comments included)."));
    }

    /// <summary>Reads the white-space separated header tokens of a PBM, PGM or PPM file, comments included.</summary>
    private ParseStatus ReadTokens(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var needed = _variant is PnmVariant.PlainBitmap or PnmVariant.BinaryBitmap ? 2 : 3;
        while (_tokenIndex < needed)
        {
            if (consumed >= buffer.Length)
                return ParseStatus.NeedMoreData(1);

            var value = buffer[consumed];
            ChargeHeaderByte();
            consumed++;
            if (_inComment)
            {
                if (value is (byte)'\n' or (byte)'\r')
                {
                    _inComment = false;
                }

                continue;
            }

            if (value == (byte)'#')
            {
                if (_inNumber)
                    throw PnmFormat.Invalid("A Netpbm comment interrupts a header token; a token must be followed by white space.");

                _inComment = true;
                continue;
            }

            if (PnmFormat.IsWhiteSpace(value))
            {
                if (_inNumber)
                {
                    StoreToken();
                }

                continue;
            }

            if (value is < (byte)'0' or > (byte)'9')
                throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Netpbm header contains the byte 0x{value:X2}, which is neither a digit, white space nor a comment."));

            _inNumber = true;
            _tokenValue = (_tokenValue * 10) + (value - '0');
            if (_tokenValue > int.MaxValue / 10)
                throw PnmFormat.Invalid("A Netpbm header token is larger than 2147483647.");
        }

        return ParseStatus.Complete;
    }

    private void StoreToken()
    {
        switch (_tokenIndex)
        {
            case 0:
                _width = _tokenValue;
                break;

            case 1:
                _height = _tokenValue;
                break;

            default:
                _maxValue = _tokenValue;
                break;
        }

        _tokenValue = 0;
        _inNumber = false;
        _tokenIndex++;
    }

    /// <summary>Reads the PAM header lines up to <c>ENDHDR</c>.</summary>
    private ParseStatus ReadPamLines(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        while (true)
        {
            if (consumed >= buffer.Length)
                return ParseStatus.NeedMoreData(1);

            var value = buffer[consumed];
            ChargeHeaderByte();
            consumed++;
            if (value != (byte)'\n')
            {
                if (_lineLength == _line.Length)
                    throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"A PAM header line is longer than {PnmFormat.MaxHeaderLineLength} bytes."));

                _line[_lineLength++] = value;
                continue;
            }

            var line = _line.AsSpan(0, _lineLength);
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            _lineLength = 0;
            if (ProcessPamLine(line))
                return ParseStatus.Complete;
        }
    }

    /// <summary>Processes one PAM header line; returns true for <c>ENDHDR</c>.</summary>
    private bool ProcessPamLine(ReadOnlySpan<byte> line)
    {
        line = line.Trim((byte)' ').Trim((byte)'\t');
        if (line.IsEmpty || line[0] == (byte)'#')
            return false;

        var space = line.IndexOfAny((byte)' ', (byte)'\t');
        var keyword = space < 0 ? line : line[..space];
        var value = space < 0 ? default : line[(space + 1)..].Trim((byte)' ').Trim((byte)'\t');
        if (keyword.SequenceEqual("ENDHDR"u8))
            return true;

        if (keyword.SequenceEqual("TUPLTYPE"u8))
        {
            if (_tupleType is not null)
                throw PnmFormat.Invalid("The PAM header declares TUPLTYPE more than once.");

            _tupleType = ParseTupleType(value);
            return false;
        }

        var number = ParsePamNumber(keyword, value);
        if (keyword.SequenceEqual("WIDTH"u8))
        {
            EnsureNotSet(_width, "WIDTH");
            _width = number;
        }
        else if (keyword.SequenceEqual("HEIGHT"u8))
        {
            EnsureNotSet(_height, "HEIGHT");
            _height = number;
        }
        else if (keyword.SequenceEqual("DEPTH"u8))
        {
            EnsureNotSet(_depth + 1, "DEPTH");
            _depth = number;
        }
        else if (keyword.SequenceEqual("MAXVAL"u8))
        {
            EnsureNotSet(_maxValue + 1, "MAXVAL");
            _maxValue = number;
        }
        else
        {
            throw PnmFormat.Invalid($"The PAM header keyword '{System.Text.Encoding.ASCII.GetString(keyword)}' is not defined.");
        }

        return false;

        static void EnsureNotSet(int current, string keyword)
        {
            if (current != 0)
                throw PnmFormat.Invalid($"The PAM header declares {keyword} more than once.");
        }
    }

    private static int ParsePamNumber(ReadOnlySpan<byte> keyword, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            throw PnmFormat.Invalid($"The PAM header keyword '{System.Text.Encoding.ASCII.GetString(keyword)}' has no value.");

        var result = 0L;
        foreach (var digit in value)
        {
            if (digit is < (byte)'0' or > (byte)'9')
                throw PnmFormat.Invalid($"The PAM header value of '{System.Text.Encoding.ASCII.GetString(keyword)}' is not a decimal number.");

            result = (result * 10) + (digit - '0');
            if (result > int.MaxValue)
                throw PnmFormat.Invalid($"The PAM header value of '{System.Text.Encoding.ASCII.GetString(keyword)}' is larger than 2147483647.");
        }

        return (int)result;
    }

    private static PnmTupleType ParseTupleType(ReadOnlySpan<byte> value)
    {
        if (value.SequenceEqual("BLACKANDWHITE"u8))
            return PnmTupleType.BlackAndWhite;

        if (value.SequenceEqual("GRAYSCALE"u8))
            return PnmTupleType.Grayscale;

        if (value.SequenceEqual("RGB"u8))
            return PnmTupleType.Rgb;

        if (value.SequenceEqual("BLACKANDWHITE_ALPHA"u8))
            return PnmTupleType.BlackAndWhiteAlpha;

        if (value.SequenceEqual("GRAYSCALE_ALPHA"u8))
            return PnmTupleType.GrayscaleAlpha;

        if (value.SequenceEqual("RGB_ALPHA"u8))
            return PnmTupleType.RgbAlpha;

        throw PnmFormat.Unsupported($"The PAM tuple type '{System.Text.Encoding.ASCII.GetString(value)}' is not one of the standard grayscale, RGB and alpha tuples this version decodes.", "PAM tuple type");
    }

    private PnmTupleType ResolvePamTupleType()
    {
        if (_width == 0 || _height == 0 || _depth < 0 || _maxValue < 0)
            throw PnmFormat.Invalid("The PAM header must declare WIDTH, HEIGHT, DEPTH and MAXVAL before ENDHDR.");

        var tupleType = _tupleType ?? _depth switch
        {
            1 => PnmTupleType.Grayscale,
            2 => PnmTupleType.GrayscaleAlpha,
            3 => PnmTupleType.Rgb,
            4 => PnmTupleType.RgbAlpha,
            _ => throw PnmFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The PAM header declares DEPTH {_depth} without a TUPLTYPE; this version decodes the standard grayscale, RGB and alpha tuples (depth 1 to 4)."), "PAM tuple type"),
        };

        var expectedDepth = tupleType switch
        {
            PnmTupleType.BlackAndWhite or PnmTupleType.Grayscale => 1,
            PnmTupleType.BlackAndWhiteAlpha or PnmTupleType.GrayscaleAlpha => 2,
            PnmTupleType.Rgb => 3,
            _ => 4,
        };

        if (_depth != expectedDepth)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The PAM header declares DEPTH {_depth} for the tuple type {tupleType}, which has {expectedDepth} sample(s)."));

        if (tupleType is PnmTupleType.BlackAndWhite or PnmTupleType.BlackAndWhiteAlpha && _maxValue != 1)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The PAM tuple type {tupleType} requires MAXVAL 1, but the header declares {_maxValue}."));

        return tupleType;
    }

    private void CompleteHeader(PnmTupleType tupleType)
    {
        if (_width <= 0 || _height <= 0)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Netpbm image is {_width}x{_height}: both dimensions must be positive."));

        if (_maxValue is < 1 or > PnmFormat.MaxSampleValue)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Netpbm MAXVAL is {_maxValue}; a value between 1 and {PnmFormat.MaxSampleValue} is expected."));

        var limits = _context.Limits;
        limits.EnsureCanvasWithinLimits(_width, _height);
        _header = new PnmImageHeader(_variant, _width, _height, _maxValue, tupleType);
        if (CheckedSizes.GetRowLength(_width, _header.Channels * _header.BytesPerSample) > CheckedSizes.MaxBufferLength
            || CheckedSizes.GetRowLength(_width, PixelFormats.GetBytesPerPixel(_header.PixelFormat)) > CheckedSizes.MaxBufferLength)
        {
            throw CheckedSizes.CreateOverflowException(limits);
        }

        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }
    }

    /// <summary>Prepares the raster walk; returns false when the sequential reader asked to yield after the header.</summary>
    private bool StartRaster()
    {
        _storedRow = _context.Scope.Rent(_header.StoredRowLength, AllocationKind.DecoderState, clear: true);
        if (_walk != StructureWalk.Decode)
            return true;

        _expander = new PnmRowExpander(_header);
        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _decoder!.OnHeaderComplete(this);
        _sourceRow = _context.Scope.Rent(_header.Width * PixelFormats.GetBytesPerPixel(_header.PixelFormat), AllocationKind.DecoderState, clear: false);
        return !_context.TryConsumeYield();
    }

    private ParseStatus ReadBinaryRaster(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var storedRowLength = _header.StoredRowLength;
        while (_rowIndex < _header.Height)
        {
            var remaining = buffer[consumed..];
            if (remaining.IsEmpty)
                return ParseStatus.NeedMoreData(1);

            var take = Math.Min(remaining.Length, storedRowLength - _storedFilled);
            remaining[..take].CopyTo(_storedRow!.Span[_storedFilled..]);
            _storedFilled += take;
            consumed += take;
            if (_storedFilled < storedRowLength)
                continue;

            _storedFilled = 0;
            EmitRow();
        }

        return ParseStatus.Complete;
    }

    private ParseStatus ReadPlainRaster(ReadOnlySpan<byte> buffer, bool isEndOfInput, ref int consumed)
    {
        var header = _header;
        var samplesPerRow = header.SamplesPerRow;
        while (_rowIndex < header.Height)
        {
            while (_sampleIndex < samplesPerRow)
            {
                while (true)
                {
                    if (consumed >= buffer.Length)
                    {
                        if (isEndOfInput && _plainInNumber)
                        {
                            CommitPlainSample();
                            _plainTerminated = true;
                            break;
                        }

                        return ParseStatus.NeedMoreData(1);
                    }

                    var value = buffer[consumed++];
                    if (_plainInComment)
                    {
                        if (value is (byte)'\n' or (byte)'\r')
                        {
                            _plainInComment = false;
                        }

                        continue;
                    }

                    if (value == (byte)'#')
                    {
                        _plainInComment = true;
                        if (_plainInNumber)
                        {
                            CommitPlainSample();
                            _plainTerminated = true;
                            break;
                        }

                        continue;
                    }

                    if (PnmFormat.IsWhiteSpace(value))
                    {
                        if (_plainInNumber)
                        {
                            CommitPlainSample();
                            _plainTerminated = true;
                            break;
                        }

                        continue;
                    }

                    if (value is < (byte)'0' or > (byte)'9')
                        throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The plain Netpbm raster contains the byte 0x{value:X2}, which is neither a digit, white space nor a comment."));

                    if (header.IsBitmap)
                    {
                        // Every character of a plain PBM raster is one pixel
                        if (value is > (byte)'1')
                            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The plain PBM raster contains the digit {(char)value}; only 0 and 1 are defined."));

                        _plainValue = value - '0';
                        _plainInNumber = true;
                        CommitPlainSample();
                        _plainTerminated = false;
                        break;
                    }

                    _plainValue = (_plainValue * 10) + (value - '0');
                    _plainInNumber = true;
                    if (_plainValue > PnmFormat.MaxSampleValue)
                        throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The plain Netpbm raster contains a sample larger than {PnmFormat.MaxSampleValue}."));
                }
            }

            _sampleIndex = 0;
            EmitRow();
        }

        return ParseStatus.Complete;
    }

    private void CommitPlainSample()
    {
        var header = _header;
        if (_plainValue > header.MaxValue)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNM sample {_plainValue} is greater than MAXVAL ({header.MaxValue})."));

        var stored = _storedRow!.Span;
        if (header.IsBitmap)
        {
            var mask = (byte)(0x80 >> (_sampleIndex & 7));
            if (_plainValue != 0)
            {
                stored[_sampleIndex >> 3] |= mask;
            }
            else
            {
                stored[_sampleIndex >> 3] &= (byte)~mask;
            }
        }
        else if (header.BytesPerSample == 1)
        {
            stored[_sampleIndex] = (byte)_plainValue;
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(stored[(_sampleIndex * 2)..], (ushort)_plainValue);
        }

        _sampleIndex++;
        _plainValue = 0;
        _plainInNumber = false;
    }

    private void EmitRow()
    {
        if (_walk == StructureWalk.Decode)
        {
            var source = _sourceRow!.Span[..(_header.Width * PixelFormats.GetBytesPerPixel(_header.PixelFormat))];
            _expander!.ExpandRow(_storedRow!.Span[.._header.StoredRowLength], source);
            _decoder!.WriteRow(_rowIndex, source);
        }

        _rowIndex++;
        _context.CancellationToken.ThrowIfCancellationRequested();
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
        => new(
            ImageFormat.Pnm,
            Size,
            _header.PixelFormat,
            _header.ColorModel,
            _header.BitsPerComponent,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: _header.HasAlpha,
            animation: null,
            _metadata.Metadata,
            mode);
}
