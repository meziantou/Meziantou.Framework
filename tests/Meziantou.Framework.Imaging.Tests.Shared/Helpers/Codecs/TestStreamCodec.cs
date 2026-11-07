using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// The test-only decoder of <see cref="TestStreamFormat"/>, written exactly as a real codec plugs into the shared pipeline
///: one incremental push-model parser serves identification, eager loads and sequential
/// readers; it reports its header snapshot to <see cref="ImageCodecContext.Sequential"/>, creates its sink with
/// <see cref="DecodedFrameSink.Create"/>, composites delta frames on a private canvas charged to the operation scope, and
/// yields after the header and after each image.
/// </summary>
internal sealed class TestStreamCodec : ImageCodec
{
    /// <summary>Gets or sets an action run before each decoded row, with the image index (-1 for the poster) and the row (tests cancel or fail mid-frame).</summary>
    public Action<int, int>? BeforeRow { get; set; }

    /// <summary>Gets the number of rows decoded by every decoder of this codec.</summary>
    public int RowsDecoded { get; private set; }

    /// <summary>Gets the context of the last operation.</summary>
    public ImageCodecContext? LastContext { get; private set; }

    public override ImageFormat Format => ImageFormat.Gif;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => prefix.StartsWith(TestStreamFormat.Signature);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The identify parser owns and disposes the parser.")]
    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
    {
        LastContext = context;
        return new IdentifyParser(new Parser(this, context, request: null, mode));
    }

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        LastContext = context;
        return new Parser(this, context, request, ImageIdentifyMode.FullScan);
    }

    private sealed class IdentifyParser(Parser parser) : ImageParser<ImageInfo>(ImageFormat.Gif)
    {
        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed) => parser.Parse(buffer, isEndOfInput, out consumed);

        public override ImageInfo GetResult() => parser.Info ?? throw new InvalidOperationException("Not parsed.");

        protected override void Dispose(bool disposing)
        {
            parser.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class Parser : ImageParser<Image>
    {
        private readonly TestStreamCodec _codec;
        private readonly ImageCodecContext _context;
        private readonly ImageDecodeRequest? _request;
        private readonly ImageIdentifyMode _mode;
        private readonly ImageMetadata _metadata = new();
        private DecodedFrameSink? _sink;
        private PooledBuffer? _canvas;
        private State _state;
        private Size _size;
        private PixelFormat _pixelFormat;
        private ImageFormat _format;
        private byte _flags;
        private int _declared;
        private int? _totalPlays;
        private int _commentLength;
        private int _rowBytes;
        private int _row;
        private bool _isPoster;
        private bool _isDelta;
        private bool _posterDone;
        private int _frames;

        public Parser(TestStreamCodec codec, ImageCodecContext context, ImageDecodeRequest? request, ImageIdentifyMode mode)
            : base(ImageFormat.Gif)
        {
            _codec = codec;
            _context = context;
            _request = request;
            _mode = mode;
        }

        private enum State
        {
            Header,
            Comment,
            Record,
            Rows,
            Done,
        }

        public ImageInfo? Info { get; private set; }

        private bool IsAnimated => (_flags & TestStreamFormat.FlagAnimated) != 0;

        private bool HasPoster => (_flags & TestStreamFormat.FlagPoster) != 0;

        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = 0;
            while (true)
            {
                var remaining = buffer[consumed..];
                switch (_state)
                {
                    case State.Header:
                        if (remaining.Length < 8 + TestStreamFormat.HeaderLength)
                            return ParseStatus.NeedMoreData(8 + TestStreamFormat.HeaderLength);

                        ParseHeader(remaining[..(8 + TestStreamFormat.HeaderLength)]);
                        consumed += 8 + TestStreamFormat.HeaderLength;
                        _state = State.Comment;
                        break;

                    case State.Comment:
                        if (remaining.Length < _commentLength)
                            return ParseStatus.NeedMoreData(_commentLength);

                        if (_commentLength > 0)
                        {
                            _context.Tracker.ChargeMetadataBytes(_commentLength);
                            _metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, Encoding.Latin1.GetString(remaining[.._commentLength])));
                        }

                        consumed += _commentLength;
                        _state = State.Record;
                        if (OnHeaderComplete())
                            return ParseStatus.Complete;

                        if (_context.TryConsumeYield())
                            return ParseStatus.Complete;

                        break;

                    case State.Record:
                    {
                        if (remaining.IsEmpty)
                            return ParseStatus.NeedMoreData(1);

                        var tag = remaining[0];
                        if (tag == 'E')
                        {
                            if (remaining.Length < 5)
                                return ParseStatus.NeedMoreData(5);

                            OnEnd(BinaryPrimitives.ReadInt32LittleEndian(remaining[1..]));
                            consumed += 5;
                            return ParseStatus.Complete;
                        }

                        if (tag == 'P')
                        {
                            if (!HasPoster || _posterDone || _frames > 0)
                                throw Invalid("Unexpected poster record.");

                            consumed++;
                            BeginImage(isPoster: true, isDelta: false, FrameDuration.Zero);
                            break;
                        }

                        if (tag is not ((byte)'F' or (byte)'D'))
                            throw Invalid($"Unknown record 0x{tag:X2}.");

                        if (remaining.Length < 9)
                            return ParseStatus.NeedMoreData(9);

                        var numerator = BinaryPrimitives.ReadUInt32LittleEndian(remaining[1..]);
                        var denominator = BinaryPrimitives.ReadUInt32LittleEndian(remaining[5..]);
                        if (denominator == 0)
                            throw Invalid("Zero duration denominator.");

                        if (tag == 'D' && _frames == 0)
                            throw Invalid("A delta frame has no previous frame.");

                        if (HasPoster && !_posterDone)
                            throw Invalid("The poster record is missing.");

                        consumed += 9;
                        BeginImage(isPoster: false, isDelta: tag == 'D', new FrameDuration(numerator, denominator));
                        break;
                    }

                    case State.Rows:
                        if (_row == _size.Height)
                        {
                            _state = State.Record;
                            if (EndImage())
                                return ParseStatus.Complete;

                            if (_context.TryConsumeYield())
                                return ParseStatus.Complete;

                            break;
                        }

                        if (remaining.Length < _rowBytes)
                            return ParseStatus.NeedMoreData(_rowBytes);

                        DecodeRow(remaining[.._rowBytes]);
                        consumed += _rowBytes;
                        _row++;
                        break;

                    default:
                        return ParseStatus.Complete;
                }
            }
        }

        public override Image GetResult()
        {
            var sink = _sink ?? throw new InvalidOperationException("Not a decoder.");
            return sink.Build(_metadata, IsAnimated ? new AnimationMetadata { TotalPlays = _totalPlays } : null);
        }

        protected override void Dispose(bool disposing)
        {
            _sink?.Dispose();
            _sink = null;
            _canvas?.Dispose();
            _canvas = null;
            base.Dispose(disposing);
        }

        private InvalidImageContentException Invalid(string message) => new($"Invalid test stream: {message}", _format == ImageFormat.Unknown ? ImageFormat.Gif : _format);

        private void ParseHeader(ReadOnlySpan<byte> header)
        {
            if (!header.StartsWith(TestStreamFormat.Signature))
                throw Invalid("Bad signature.");

            header = header[8..];
            var width = BinaryPrimitives.ReadInt32LittleEndian(header);
            var height = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            if (width <= 0 || height <= 0)
                throw Invalid("Empty canvas.");

            _size = new Size(width, height);
            _pixelFormat = (PixelFormat)header[8];
            _format = (ImageFormat)header[9];
            _flags = header[10];
            _declared = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
            var plays = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
            _totalPlays = plays == 0 ? null : plays;
            _commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);
            if ((_flags & TestStreamFormat.FlagUnsupported) != 0)
                throw new UnsupportedImageFeatureException("Unsupported test feature.", _format, "Test feature");

            _context.Limits.EnsureCanvasWithinLimits(width, height);
            _rowBytes = checked(width * PixelFormats.GetBytesPerPixel(_pixelFormat));
            _metadata.SourceFormat = _format;
        }

        /// <returns><see langword="true"/> when the parser is complete (header identification).</returns>
        private bool OnHeaderComplete()
        {
            var info = CreateInfo(ImageIdentifyMode.Header);
            if (_request is null)
            {
                if (_mode == ImageIdentifyMode.Header)
                {
                    Info = info;
                    _state = State.Done;
                    return true;
                }

                return false;
            }

            // Sequential readers: the header snapshot is their Info; the parser then yields before the first record
            _context.Sequential?.OnHeader(info);
            _sink = DecodedFrameSink.Create(_context, _request, _format, _size, _pixelFormat, _pixelFormat, iccProfile: null);
            _canvas = _context.Scope.Rent(checked(_rowBytes * _size.Height), AllocationKind.CompositorState);
            return false;
        }

        private void BeginImage(bool isPoster, bool isDelta, FrameDuration duration)
        {
            _isPoster = isPoster;
            _isDelta = isDelta;
            _row = 0;
            _state = State.Rows;
            if (_sink is null)
            {
                _context.Tracker.ChargeScannedFrame();
            }
            else if (isPoster)
            {
                _sink.BeginPoster();
            }
            else
            {
                _sink.BeginFrame(duration);
            }
        }

        private void DecodeRow(ReadOnlySpan<byte> data)
        {
            _codec.BeforeRow?.Invoke(_isPoster ? -1 : _frames, _row);
            _context.CancellationToken.ThrowIfCancellationRequested();
            _codec.RowsDecoded++;
            if (_sink is null)
                return;

            if (_isPoster)
            {
                _sink.WriteRow(_row, data);
                return;
            }

            // The compositor canvas is private: caller edits of returned frames never affect the next frames
            var canvasRow = _canvas!.Span.Slice(_row * _rowBytes, _rowBytes);
            if (_isDelta)
            {
                for (var i = 0; i < canvasRow.Length; i++)
                {
                    canvasRow[i] ^= data[i];
                }
            }
            else
            {
                data.CopyTo(canvasRow);
            }

            _sink.WriteRow(_row, canvasRow);
        }

        /// <returns><see langword="true"/> when the walk stops (frame limit).</returns>
        private bool EndImage()
        {
            if (_isPoster)
            {
                _posterDone = true;
            }
            else
            {
                _frames++;
            }

            if (_sink is not null && !_sink.EndImage())
            {
                _state = State.Done;
                return true;
            }

            return false;
        }

        private void OnEnd(int count)
        {
            if (count != _frames || (_declared != 0 && _declared != _frames))
                throw Invalid($"The end record declares {count} frames ({_declared} in the header) but {_frames} were found.");

            if (_frames == 0)
                throw Invalid("No frame.");

            _state = State.Done;
            Info = CreateInfo(ImageIdentifyMode.FullScan);
        }

        private ImageInfo CreateInfo(ImageIdentifyMode mode)
        {
            int? frameCount = mode == ImageIdentifyMode.FullScan ? _frames : _declared == 0 ? null : _declared;
            var animation = IsAnimated ? new AnimationMetadata { TotalPlays = _totalPlays } : null;
            return new ImageInfo(_format, _size, _pixelFormat, ImageColorModel.Rgba, PixelFormats.GetBitsPerComponent(_pixelFormat), frameCount, IsAnimated, HasPoster, PixelFormats.HasAlpha(_pixelFormat), animation, _metadata, mode);
        }
    }
}
