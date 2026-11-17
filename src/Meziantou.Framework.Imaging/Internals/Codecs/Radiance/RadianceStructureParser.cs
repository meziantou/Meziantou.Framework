using System.Text;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental Radiance HDR reader (Radiance File Formats, "Picture File Format"): the information header, the resolution
/// string and the scanline records, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the information header and the resolution string.</description></item>
/// <item><description>Full scan: reads every scanline (the end of the data is only known once every pixel is read), charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: the same walk; each complete scanline goes to the <see cref="RadianceDecoder"/>.</description></item>
/// </list>
/// <para>
/// The header is a sequence of lines ending with a line feed, the first one an identifier (<c>#?RADIANCE</c> or
/// <c>#?RGBE</c>) and the last one empty. Every byte of it is charged to <see cref="ImageResourceLimits.MaxMetadataBytes"/>
/// as it is read. The variables (<c>NAME=value</c> at the start of a line) the decoder reads are <c>FORMAT</c> (at most
/// once: <c>32-bit_rle_rgbe</c>, the default, or <c>32-bit_rle_xyze</c>; any other value is
/// <see cref="UnsupportedImageFeatureException"/>), <c>EXPOSURE</c>, <c>COLORCORR</c> and <c>PIXASPECT</c> (cumulative:
/// their product), <c>PRIMARIES</c> and <c>GAMMA</c> (the last one), <c>VIEW</c> (joined) and <c>SOFTWARE</c> (the last
/// one). A variable whose value is not what it should be (positive finite decimals, any finite ones for <c>PRIMARIES</c>,
/// which must also describe valid primaries) is skipped, like a metadata payload that cannot be read: FFmpeg writes
/// <c>PIXASPECT=inf</c>. The other lines (the commands that made the picture, comments, other variables) are skipped too.
/// </para>
/// <para>
/// The resolution string gives the major axis first: <c>-Y height +X width</c> for rows stored from the top, left to right,
/// and the seven other orders of the two axes and their directions. Each scanline is either flat (four bytes per pixel),
/// "old" run-length encoded (a pixel 1, 1, 1, n repeats the previous pixel n times, and the next such pixels add the higher
/// bytes of the count), which may be mixed with flat pixels, or "new" run-length encoded (a pixel 2, 2 followed by the
/// 15-bit length of the scanline, then each of the four components apart, in runs: a code above 128 repeats the next byte
/// code - 128 times, a code of 1 to 128 is followed by as many bytes). Only scanlines of 8 to 32,767 pixels can use the new
/// encoding; each scanline chooses its own. A length that is not the one of the scanline, a run or a repetition past its
/// end, a repetition without a previous pixel of the scanline, a code 0 and truncation are
/// <see cref="InvalidImageContentException"/>. The bytes after the last scanline are not read.
/// </para>
/// </remarks>
internal sealed class RadianceStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly RadianceDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private long _position;
    private int _lineScanned;

    // Header variables
    private bool _hasFormat;
    private double _exposure = 1;
    private double _colorCorrectionRed = 1;
    private double _colorCorrectionGreen = 1;
    private double _colorCorrectionBlue = 1;
    private double[]? _primaries;
    private double _pixelAspectRatio = 1;
    private double? _gamma;
    private StringBuilder? _view;
    private string? _software;

    // Scanlines
    private PooledBuffer? _scanline;
    private int _scanlineIndex;
    private int _pixel;
    private int _component;
    private int _repeatShift;
    private ImageInfo? _result;

    public RadianceStructureParser(ImageCodecContext context, StructureWalk walk, RadianceDecoder? decoder = null)
        : base(ImageFormat.Radiance)
    {
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Radiance);
    }

    private enum State
    {
        Signature,
        Header,
        Resolution,
        ScanlineStart,
        NewEncoding,
        OldEncoding,
        Done,
    }

    /// <summary>Gets the image width: the size of the X axis.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the image height: the size of the Y axis.</summary>
    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    /// <summary>Gets a value indicating whether the scanlines are columns (the resolution string gives the X axis first).</summary>
    public bool IsColumnMajor { get; private set; }

    /// <summary>Gets a value indicating whether the scanlines are stored from the largest coordinate of the major axis down.</summary>
    public bool IsMajorDecreasing { get; private set; }

    /// <summary>Gets a value indicating whether the pixels of a scanline go from the largest coordinate of the minor axis down.</summary>
    public bool IsMinorDecreasing { get; private set; }

    /// <summary>Gets the number of scanlines: the size of the major axis.</summary>
    public int ScanlineCount { get; private set; }

    /// <summary>Gets the number of pixels of a scanline: the size of the minor axis.</summary>
    public int ScanlineLength { get; private set; }

    /// <summary>Gets a value indicating whether the samples are CIE XYZ (<c>32-bit_rle_xyze</c>).</summary>
    public bool IsXyz { get; private set; }

    /// <summary>Gets the metadata: the transfer function, the color description of the primaries and <see cref="ImageMetadata.RadianceHeader"/>.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.Signature:
                {
                    var status = TryReadLine(buffer, ref consumed, RadianceFormat.RadianceSignature.Length, charge: true, out var line);
                    if (!status.IsComplete)
                        return status;

                    if (!line.SequenceEqual(RadianceFormat.RadianceSignature[..^1]) && !line.SequenceEqual(RadianceFormat.RgbeSignature[..^1]))
                        throw RadianceFormat.Invalid("the first line is not '#?RADIANCE' or '#?RGBE'.");

                    _state = State.Header;
                    break;
                }

                case State.Header:
                {
                    var status = TryReadLine(buffer, ref consumed, maxLength: int.MaxValue, charge: true, out var line);
                    if (!status.IsComplete)
                        return status;

                    if (line.IsEmpty)
                    {
                        _state = State.Resolution;
                        break;
                    }

                    ReadHeaderLine(line);
                    break;
                }

                case State.Resolution:
                {
                    var status = TryReadLine(buffer, ref consumed, RadianceFormat.MaxResolutionLineLength, charge: false, out var line);
                    if (!status.IsComplete)
                        return status;

                    ReadResolution(line);
                    if (_walk == StructureWalk.Header)
                    {
                        _result = CreateInfo(ImageIdentifyMode.Header);
                        _state = State.Done;
                        return ParseStatus.Complete;
                    }

                    _scanline = _context.Scope.Rent(ScanlineLength * 4, AllocationKind.DecoderState, clear: false);
                    _state = State.ScanlineStart;
                    if (_walk == StructureWalk.FullScan)
                    {
                        _context.Tracker.ChargeScannedFrame();
                        break;
                    }

                    _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
                    _decoder!.OnHeaderComplete(this);
                    if (_context.TryConsumeYield())
                        return ParseStatus.Complete;

                    break;
                }

                case State.ScanlineStart:
                {
                    var remaining = buffer[consumed..];
                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    _pixel = 0;
                    _component = 0;
                    _repeatShift = 0;
                    var length = ScanlineLength;
                    if (length is >= RadianceFormat.MinEncodedLength and <= RadianceFormat.MaxEncodedLength && remaining[0] == 2 && remaining[1] == 2 && (remaining[2] & 0x80) == 0)
                    {
                        var stored = (remaining[2] << 8) | remaining[3];
                        if (stored != length)
                            throw RadianceFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the run-length encoded scanline {_scanlineIndex} at offset {_position} declares {stored} pixels; the scanlines have {length}."));

                        Advance(ref consumed, 4);
                        _state = State.NewEncoding;
                    }
                    else
                    {
                        // A flat or "old" encoded scanline: these four bytes are its first pixel
                        _state = State.OldEncoding;
                    }

                    break;
                }

                case State.NewEncoding:
                {
                    var status = ReadNewEncoding(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    if (EndScanline())
                        return ParseStatus.Complete;

                    break;
                }

                case State.OldEncoding:
                {
                    var status = ReadOldEncoding(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    if (EndScanline())
                        return ParseStatus.Complete;

                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The Radiance HDR data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanline?.Dispose();
            _scanline = null;
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    /// <summary>
    /// Reads a line up to its line feed (excluded). Header bytes are charged to the metadata limit as they arrive, so that a
    /// line never buffers more than the limit allows, and the search for the line feed resumes where the previous call
    /// stopped.
    /// </summary>
    private ParseStatus TryReadLine(ReadOnlySpan<byte> buffer, ref int consumed, int maxLength, bool charge, out ReadOnlySpan<byte> line)
    {
        var remaining = buffer[consumed..];
        var searched = remaining[..Math.Min(remaining.Length, maxLength)];
        var end = searched[_lineScanned..].IndexOf((byte)'\n');
        if (end >= 0)
        {
            end += _lineScanned;
        }

        if (end < 0)
        {
            line = default;
            if (searched.Length >= maxLength)
            {
                throw _state switch
                {
                    State.Signature => RadianceFormat.Invalid("the first line is not '#?RADIANCE' or '#?RGBE'."),
                    _ => RadianceFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the resolution string at offset {_position} is longer than {RadianceFormat.MaxResolutionLineLength - 1} characters or has no line feed.")),
                };
            }

            if (charge)
            {
                _metadata.Charge(searched.Length - _lineScanned);
            }

            _lineScanned = searched.Length;
            return ParseStatus.NeedMoreData(remaining.Length + 1);
        }

        if (charge)
        {
            _metadata.Charge(end + 1 - _lineScanned);
        }

        _lineScanned = 0;
        line = remaining[..end];
        Advance(ref consumed, end + 1);
        return ParseStatus.Complete;
    }

    private void ReadHeaderLine(ReadOnlySpan<byte> line)
    {
        if (TryGetValue(line, "FORMAT="u8, out var format))
        {
            if (_hasFormat)
                throw RadianceFormat.Invalid("the header has more than one FORMAT line.");

            _hasFormat = true;
            var value = Encoding.UTF8.GetString(format).Trim();
            IsXyz = value switch
            {
                RadianceFormat.RgbeFormat => false,
                RadianceFormat.XyzeFormat => true,
                _ => throw RadianceFormat.Unsupported($"The Radiance HDR file has the format '{value}': only RGBE ({RadianceFormat.RgbeFormat}) and XYZE ({RadianceFormat.XyzeFormat}) pictures are read.", "Pixel format " + value),
            };
        }
        else if (TryGetValue(line, "EXPOSURE="u8, out var exposure))
        {
            if (TryParsePositive(exposure, out var value))
            {
                _exposure *= value;
            }
        }
        else if (TryGetValue(line, "COLORCORR="u8, out var colorCorrection))
        {
            Span<double> values = stackalloc double[3];
            if (TryParseNumbers(colorCorrection, values, positive: true))
            {
                _colorCorrectionRed *= values[0];
                _colorCorrectionGreen *= values[1];
                _colorCorrectionBlue *= values[2];
            }
        }
        else if (TryGetValue(line, "PRIMARIES="u8, out var primaries))
        {
            var values = new double[8];
            if (TryParseNumbers(primaries, values, positive: false) && RadianceColorDescription.IsValid(values))
            {
                _primaries = values;
            }
        }
        else if (TryGetValue(line, "PIXASPECT="u8, out var pixelAspect))
        {
            if (TryParsePositive(pixelAspect, out var value))
            {
                _pixelAspectRatio *= value;
            }
        }
        else if (TryGetValue(line, "GAMMA="u8, out var gamma))
        {
            if (TryParsePositive(gamma, out var value))
            {
                _gamma = value;
            }
        }
        else if (TryGetValue(line, "VIEW="u8, out var view))
        {
            var text = Encoding.UTF8.GetString(view).Trim();
            if (_view is null)
            {
                _view = new StringBuilder(text);
            }
            else
            {
                _view.Append(' ').Append(text);
            }
        }
        else if (TryGetValue(line, "SOFTWARE="u8, out var software))
        {
            _software = Encoding.UTF8.GetString(software).Trim();
        }
    }

    private static bool TryGetValue(ReadOnlySpan<byte> line, ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value)
    {
        if (line.StartsWith(name))
        {
            value = line[name.Length..];
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParsePositive(ReadOnlySpan<byte> text, out double value)
    {
        Span<double> values = stackalloc double[1];
        var result = TryParseNumbers(text, values, positive: true);
        value = values[0];
        return result;
    }

    /// <summary>Reads exactly <paramref name="values"/>.Length finite decimal numbers separated by white space.</summary>
    private static bool TryParseNumbers(ReadOnlySpan<byte> text, Span<double> values, bool positive)
    {
        var count = 0;
        var i = 0;
        while (true)
        {
            while (i < text.Length && IsBlank(text[i]))
            {
                i++;
            }

            if (i == text.Length)
                break;

            var start = i;
            while (i < text.Length && !IsBlank(text[i]))
            {
                i++;
            }

            if (count == values.Length || !double.TryParse(text[start..i], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || (positive && value <= 0))
                return false;

            values[count++] = value;
        }

        return count == values.Length;
    }

    private static bool IsBlank(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\r';

    private void ReadResolution(ReadOnlySpan<byte> line)
    {
        // Two axes, each a sign, the letter of the axis and a size: "-Y 512 +X 768"
        Span<Range> tokens = stackalloc Range[5];
        var count = Split(line, tokens);
        if (count != 4 || !TryParseAxis(line[tokens[0]], out var majorAxis, out var majorDecreasing) || !TryParseAxis(line[tokens[2]], out var minorAxis, out var minorDecreasing) || majorAxis == minorAxis)
            throw RadianceFormat.Invalid($"the resolution string '{Encoding.UTF8.GetString(line)}' is not two axes (such as '-Y 512 +X 768').");

        var majorSize = ParseSize(line[tokens[1]], line);
        var minorSize = ParseSize(line[tokens[3]], line);
        IsColumnMajor = majorAxis == 'X';
        IsMajorDecreasing = majorDecreasing;
        IsMinorDecreasing = minorDecreasing;
        var width = IsColumnMajor ? majorSize : minorSize;
        var height = IsColumnMajor ? minorSize : majorSize;
        var limits = _context.Limits;
        if (width > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

        if (height > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, height);

        limits.EnsureCanvasWithinLimits((int)width, (int)height);
        if (Math.Max(width, height) * 16 > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        Width = (int)width;
        Height = (int)height;
        ScanlineCount = (int)majorSize;
        ScanlineLength = (int)minorSize;
        ApplyMetadata(line, tokens);
    }

    private static int Split(ReadOnlySpan<byte> line, Span<Range> tokens)
    {
        var count = 0;
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] is (byte)' ' or (byte)'\t')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < line.Length && line[i] is not ((byte)' ' or (byte)'\t'))
            {
                i++;
            }

            if (count == tokens.Length)
                return count + 1;

            tokens[count++] = start..i;
        }

        return count;
    }

    private static bool TryParseAxis(ReadOnlySpan<byte> token, out char axis, out bool decreasing)
    {
        axis = default;
        decreasing = false;
        if (token.Length != 2 || token[0] is not ((byte)'+' or (byte)'-') || token[1] is not ((byte)'X' or (byte)'Y'))
            return false;

        axis = (char)token[1];
        decreasing = token[0] == '-';
        return true;
    }

    private static long ParseSize(ReadOnlySpan<byte> token, ReadOnlySpan<byte> line)
    {
        long value = 0;
        foreach (var digit in token)
        {
            if (digit is < (byte)'0' or > (byte)'9')
                throw RadianceFormat.Invalid($"the resolution string '{Encoding.UTF8.GetString(line)}' has a size that is not a decimal number.");

            value = Math.Min((value * 10) + (digit - '0'), long.MaxValue / 20);
        }

        if (value == 0)
            throw RadianceFormat.Invalid($"the resolution string '{Encoding.UTF8.GetString(line)}' has a size of 0.");

        return value;
    }

    private void ApplyMetadata(ReadOnlySpan<byte> line, ReadOnlySpan<Range> tokens)
    {
        var metadata = _metadata.Metadata;
        RadianceColorDescription.Apply(_primaries, metadata);
        var scanOrder = Encoding.ASCII.GetString(line[tokens[0]]) + " " + Encoding.ASCII.GetString(line[tokens[2]]);
        metadata.RadianceHeader = new RadianceHeader(IsXyz, _exposure, [_colorCorrectionRed, _colorCorrectionGreen, _colorCorrectionBlue], _primaries, _pixelAspectRatio, _gamma, _view?.ToString(), _software, scanOrder);
    }

    /// <summary>Reads the four components of a scanline of the new encoding, each one in runs and literals.</summary>
    private ParseStatus ReadNewEncoding(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var length = ScanlineLength;
        var scanline = _scanline!.RawBuffer.AsSpan(0, length * 4);
        var i = consumed;
        try
        {
            while (_component < 4)
            {
                if (_pixel == length)
                {
                    _component++;
                    _pixel = 0;
                    continue;
                }

                if (i >= buffer.Length)
                    return ParseStatus.NeedMoreData(1);

                var code = buffer[i];
                if (code > RadianceFormat.MaxLiteral)
                {
                    var run = code - RadianceFormat.MaxLiteral;
                    if (buffer.Length - i < 2)
                        return ParseStatus.NeedMoreData(2);

                    if (run > length - _pixel)
                        throw RunPastEnd(run, i - consumed);

                    var value = buffer[i + 1];
                    for (var k = 0; k < run; k++)
                    {
                        scanline[((_pixel + k) * 4) + _component] = value;
                    }

                    _pixel += run;
                    i += 2;
                }
                else
                {
                    if (code == 0)
                        throw RadianceFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the scanline {_scanlineIndex} has a code 0 at offset {_position + (i - consumed)}."));

                    if (buffer.Length - i < 1 + code)
                        return ParseStatus.NeedMoreData(1 + code);

                    if (code > length - _pixel)
                        throw RunPastEnd(code, i - consumed);

                    var literal = buffer.Slice(i + 1, code);
                    for (var k = 0; k < literal.Length; k++)
                    {
                        scanline[((_pixel + k) * 4) + _component] = literal[k];
                    }

                    _pixel += code;
                    i += 1 + code;
                }
            }

            return ParseStatus.Complete;
        }
        finally
        {
            Advance(ref consumed, i - consumed);
        }
    }

    /// <summary>Reads the pixels of a flat or "old" encoded scanline.</summary>
    private ParseStatus ReadOldEncoding(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var length = ScanlineLength;
        var scanline = _scanline!.RawBuffer.AsSpan(0, length * 4);
        var i = consumed;
        try
        {
            while (_pixel < length)
            {
                if (buffer.Length - i < 4)
                    return ParseStatus.NeedMoreData(4);

                var pixel = buffer.Slice(i, 4);
                if (pixel[0] == 1 && pixel[1] == 1 && pixel[2] == 1)
                {
                    // A repetition of the previous pixel; consecutive ones hold the higher bytes of the count
                    if (_pixel == 0)
                        throw RadianceFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the scanline {_scanlineIndex} starts with a repetition at offset {_position + (i - consumed)}: there is no previous pixel to repeat."));

                    var count = _repeatShift >= 32 ? (pixel[3] == 0 ? 0 : long.MaxValue) : (long)pixel[3] << _repeatShift;
                    if (count > length - _pixel)
                        throw RunPastEnd(count, i - consumed);

                    var previous = scanline.Slice((_pixel - 1) * 4, 4);
                    for (var k = 0; k < count; k++)
                    {
                        previous.CopyTo(scanline[((_pixel + k) * 4)..]);
                    }

                    _pixel += (int)count;
                    _repeatShift += 8;
                }
                else
                {
                    pixel.CopyTo(scanline[(_pixel * 4)..]);
                    _pixel++;
                    _repeatShift = 0;
                }

                i += 4;
            }

            return ParseStatus.Complete;
        }
        finally
        {
            Advance(ref consumed, i - consumed);
        }
    }

    /// <returns><see langword="true"/> when the last scanline was read.</returns>
    private bool EndScanline()
    {
        _decoder?.OnScanline(this, _scanlineIndex, _scanline!.RawBuffer.AsSpan(0, ScanlineLength * 4));
        _scanlineIndex++;
        _state = State.ScanlineStart;
        if (_scanlineIndex < ScanlineCount)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _decoder?.OnEnd(this);
        return true;
    }

    private InvalidImageContentException RunPastEnd(long count, int offset)
        => RadianceFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the run of {count} pixels at offset {_position + offset} extends past the end of the scanline {_scanlineIndex} ({ScanlineLength - _pixel} pixels are left)."));

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
        => new(
            ImageFormat.Radiance,
            Size,
            DefaultPixelFormats.Radiance,
            ImageColorModel.Rgb,
            bitsPerComponent: 32,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: false,
            animation: null,
            _metadata.Metadata,
            mode);
}
