using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A test-only codec registered through <see cref="ImageCodecRegistry.Override"/>: it plugs a real incremental decoder into
/// the public eager entry points, exercising the shared infrastructure exactly as the PNG/GIF/JPEG decoders will
/// (detection, the push-model parser contract, <see cref="DecodedImageBuilder"/>, limits, cancellation, ownership).
/// </summary>
/// <remarks>
/// Format (<see cref="TestRawImage.Encode"/>): an 8-byte signature, a 20-byte little-endian header (width, height, source
/// and default pixel formats, frame count, flags, total plays, ICC color space), then the optional poster and the frames,
/// each as an 8-byte duration (numerator, denominator) followed by raw rows of the source format. It reports itself as PNG
/// because <see cref="ImageInfo"/> requires a known format.
/// </remarks>
internal sealed class TestRawCodec : ImageCodec
{
    public static ReadOnlySpan<byte> Signature => [0x8A, (byte)'M', (byte)'Z', (byte)'T', (byte)'E', (byte)'S', (byte)'T', 0x0A];

    /// <summary>Gets the context of the last operation (tests check that every buffer was released).</summary>
    public ImageCodecContext? LastContext { get; private set; }

    /// <summary>Gets the number of rows decoded so far by every decoder of this codec.</summary>
    public int RowsDecoded { get; set; }

    /// <summary>Gets or sets an action run before each decoded row (tests cancel or fail mid-decode).</summary>
    public Action<int>? BeforeRow { get; set; }

    public override ImageFormat Format => ImageFormat.Png;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => prefix.StartsWith(Signature);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
    {
        LastContext = context;
        return new Parser(this, context, request: null, mode);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The decode parser owns and disposes the structure parser.")]
    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        LastContext = context;
        return new DecodeParser(new Parser(this, context, request, ImageIdentifyMode.FullScan));
    }

    private sealed class DecodeParser(Parser parser) : ImageParser<Image>(ImageFormat.Png)
    {
        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed) => parser.Parse(buffer, isEndOfInput, out consumed);

        public override Image GetResult() => parser.TakeImage();

        protected override void Dispose(bool disposing)
        {
            parser.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class Parser : ImageParser<ImageInfo>
    {
        private readonly TestRawCodec _codec;
        private readonly ImageCodecContext _context;
        private readonly ImageDecodeRequest? _request;
        private readonly ImageIdentifyMode _mode;
        private readonly ImageMetadata _metadata = new() { SourceFormat = ImageFormat.Png };
        private DecodedFrameSink? _builder;
        private State _state;
        private Size _size;
        private PixelFormat _source;
        private PixelFormat _default;
        private int _frameCount;
        private bool _hasPoster;
        private int? _totalPlays;
        private bool _animated;
        private int _framesDone;
        private bool _posterDone;
        private int _row;
        private int _rowBytes;
        private ImageInfo? _info;

        public Parser(TestRawCodec codec, ImageCodecContext context, ImageDecodeRequest? request, ImageIdentifyMode mode)
            : base(ImageFormat.Png)
        {
            _codec = codec;
            _context = context;
            _request = request;
            _mode = mode;
        }

        private enum State
        {
            Header,
            FrameHeader,
            Rows,
            Done,
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
                        if (remaining.Length < 28)
                            return ParseStatus.NeedMoreData(28);

                        ParseHeader(remaining[..28]);
                        consumed += 28;
                        if (_request is null && _mode == ImageIdentifyMode.Header)
                        {
                            _info = CreateInfo();
                            _state = State.Done;
                            return ParseStatus.Complete;
                        }

                        _state = State.FrameHeader;
                        if (_context.TryConsumeYield())
                            return ParseStatus.Complete;

                        break;

                    case State.FrameHeader:
                    {
                        if (IsComplete())
                        {
                            _state = State.Done;
                            _info = CreateInfo();
                            return ParseStatus.Complete;
                        }

                        if (remaining.Length < 8)
                            return ParseStatus.NeedMoreData(8);

                        var numerator = BinaryPrimitives.ReadUInt32LittleEndian(remaining);
                        var denominator = BinaryPrimitives.ReadUInt32LittleEndian(remaining[4..]);
                        if (denominator == 0)
                            throw new InvalidImageContentException("Zero duration denominator.", ImageFormat.Png);

                        var isPoster = _hasPoster && !_posterDone;
                        if (_builder is not null)
                        {
                            if (isPoster)
                            {
                                _builder.BeginPoster();
                            }
                            else
                            {
                                _builder.BeginFrame(new FrameDuration(numerator, denominator));
                            }
                        }
                        else
                        {
                            _context.Tracker.ChargeScannedFrame();
                        }

                        consumed += 8;
                        _row = 0;
                        _state = State.Rows;
                        break;
                    }

                    case State.Rows:
                        if (_row == _size.Height)
                        {
                            if (_hasPoster && !_posterDone)
                            {
                                _posterDone = true;
                            }
                            else
                            {
                                _framesDone++;
                            }

                            _state = State.FrameHeader;
                            if (_builder is not null && !_builder.EndImage())
                            {
                                _state = State.Done;
                                return ParseStatus.Complete;
                            }

                            if (_context.TryConsumeYield())
                                return ParseStatus.Complete;

                            break;
                        }

                        if (remaining.Length < _rowBytes)
                            return ParseStatus.NeedMoreData(_rowBytes);

                        _context.CancellationToken.ThrowIfCancellationRequested();
                        _codec.BeforeRow?.Invoke(_codec.RowsDecoded);
                        _builder?.WriteRow(_row, remaining[.._rowBytes]);
                        _codec.RowsDecoded++;
                        consumed += _rowBytes;
                        _row++;
                        break;

                    default:
                        return ParseStatus.Complete;
                }
            }
        }

        public override ImageInfo GetResult() => _info ?? throw new InvalidOperationException("Not parsed.");

        public Image TakeImage()
        {
            var builder = _builder ?? throw new InvalidOperationException("Not a decoder.");
            return builder.Build(_metadata, _animated ? new AnimationMetadata { TotalPlays = _totalPlays } : null);
        }

        protected override void Dispose(bool disposing)
        {
            _builder?.Dispose();
            base.Dispose(disposing);
        }

        private bool IsComplete()
        {
            if (_hasPoster && !_posterDone)
                return false;

            return _framesDone == _frameCount || (_builder?.IsFrameLimitReached ?? false);
        }

        private void ParseHeader(ReadOnlySpan<byte> header)
        {
            if (!header.StartsWith(Signature))
                throw new InvalidImageContentException("Bad signature.", ImageFormat.Png);

            var width = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
            if (width <= 0 || height <= 0)
                throw new InvalidImageContentException("Empty canvas.", ImageFormat.Png);

            _size = new Size(width, height);
            _source = (PixelFormat)header[16];
            _default = (PixelFormat)header[17];
            _frameCount = header[18];
            _hasPoster = (header[19] & 1) != 0;
            _animated = (header[19] & 2) != 0 || _frameCount > 1 || _hasPoster;
            var plays = BinaryPrimitives.ReadUInt16LittleEndian(header[20..]);
            _totalPlays = plays == 0 ? null : plays;
            var iccColorSpace = header[22];
            if (header[23] != 0)
                throw new UnsupportedImageFeatureException("Unsupported test feature.", ImageFormat.Png, "Test feature");

            _context.Limits.EnsureCanvasWithinLimits(width, height);
            _rowBytes = checked(width * PixelFormats.GetBytesPerPixel(_source));
            if (iccColorSpace != 0)
            {
                _metadata.IccProfile = new IccProfile(new MetadataBlob(TestRawImage.CreateIccHeader(iccColorSpace == 1 ? "GRAY"u8 : "RGB "u8)));
            }

            _metadata.TextEntries.Add(new ImageTextEntry("Comment", "test codec"));
            if (_request is not null)
            {
                // Sequential readers: report the header snapshot, then the sink binds frames to the reader requests
                _context.Sequential?.OnHeader(new ImageInfo(ImageFormat.Png, _size, _default, ImageColorModel.Rgba, PixelFormats.GetBitsPerComponent(_source), _frameCount, _animated, _hasPoster, PixelFormats.HasAlpha(_source), _animated ? new AnimationMetadata { TotalPlays = _totalPlays } : null, _metadata, ImageIdentifyMode.Header));
                _builder = DecodedFrameSink.Create(_context, _request, ImageFormat.Png, _size, _default, _source, _metadata.IccProfile);
            }
        }

        private ImageInfo CreateInfo()
        {
            int? frameCount = _mode == ImageIdentifyMode.FullScan ? _frameCount : null;
            return new ImageInfo(ImageFormat.Png, _size, _default, ImageColorModel.Rgba, PixelFormats.GetBitsPerComponent(_source), frameCount, _animated, _hasPoster, PixelFormats.HasAlpha(_source), _animated ? new AnimationMetadata { TotalPlays = _totalPlays } : null, _metadata, _mode);
        }
    }
}
