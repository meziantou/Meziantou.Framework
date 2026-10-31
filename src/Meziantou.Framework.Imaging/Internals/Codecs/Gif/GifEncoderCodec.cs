using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The GIF encoder registration: GIF89a still images and animations with per-frame local palettes, written by one
/// streaming session.
/// </summary>
internal sealed class GifEncoderCodec : ImageEncoderCodec
{
    /// <summary>The number of pixels processed by one <see cref="ImageEncoderSession.Encode"/> step (at least one row).</summary>
    internal const int PixelsPerStep = 64 * 1024;

    public static GifEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Gif;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>Gets the display row of the <paramref name="index"/>-th row of an image of <paramref name="height"/> rows in GIF interlaced order.</summary>
    /// <param name="index">The position of the row in the datastream.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The display row (passes: 0 + 8k, 4 + 8k, 2 + 4k, 1 + 2k).</returns>
    internal static int GetInterlacedRow(int index, int height)
    {
        var pass1 = (height + 7) / 8;
        if (index < pass1)
            return index * 8;

        index -= pass1;
        var pass2 = (height + 3) / 8;
        if (index < pass2)
            return 4 + (index * 8);

        index -= pass2;
        var pass3 = (height + 1) / 4;
        if (index < pass3)
            return 2 + (index * 4);

        index -= pass3;
        return 1 + (index * 2);
    }

    /// <summary>
    /// The encoding state of one GIF. The first frame writes the header, the logical screen
    /// descriptor (no global color table), the NETSCAPE2.0 loop extension of an animation that does not play once, and one
    /// comment extension per <see cref="Encode"/> call. Each image then runs three bounded passes over its pixels: the
    /// histogram (alpha reduced, colors keyed), the palette, and the mapping and LZW compression of the rows in datastream
    /// order (interlaced or not), preceded by its Graphic Control Extension, its image descriptor and its local color table.
    /// Completion writes the trailer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default, every frame covers the whole canvas and every animation frame is disposed with "restore to background" (2),
    /// which GIF decoders apply as a clear to transparent: each displayed frame is drawn on a transparent canvas, so pixels
    /// that become transparent never show stale content, whatever the previous frame. Nothing of a frame is kept after its
    /// operation: working memory (rows, histogram, string table, and the index plane of interlaced dithered frames) is
    /// allocated on creation and is independent of the number of frames.
    /// </para>
    /// <para>
    /// With <see cref="GifEncoder.OptimizeFrames"/>, an animation keeps two canvases of alpha-reduced pixels
    /// (<see cref="AnimationFrameDelta"/>): each frame is compared with the canvases the disposal of the previous frame can
    /// leave, which chooses that disposal and, for the frames that make visible pixels transparent, grows the region of the
    /// previous frame. The previous frame is then written from the canvases (its region, its unchanged pixels as the
    /// transparent entry), and the frame is copied into them: every image is written one operation late, the last one at
    /// completion. An image whose colors no longer fit its palette because of the transparent entry is analyzed again with
    /// its unchanged pixels in their colors. The image of a frame that has transparent pixels always has a transparent entry,
    /// as a full-canvas frame does, even when its region has none: Apple ImageIO restores to background with the background
    /// color (opaque) instead of transparent pixels when the frame drawn next has no transparent entry.
    /// </para>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const byte ExtensionIntroducer = 0x21;
        private const byte ImageSeparator = 0x2C;
        private const byte Trailer = 0x3B;
        private const byte GraphicControlLabel = 0xF9;
        private const byte CommentLabel = 0xFE;
        private const byte ApplicationLabel = 0xFF;
        private const int DisposeNone = 1;
        private const int DisposeRestoreBackground = 2;
        private const int DisposeRestorePrevious = 3;

        private readonly GifEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _workFormat;
        private readonly Rgba64? _background;
        private readonly bool _threshold;
        private readonly bool _animated;
        private readonly QuantizeOptions _quantization;
        private readonly int[]? _fixedPalette;
        private readonly int _width;
        private readonly int _height;
        private readonly ushort? _loopCount;
        private readonly byte[][] _comments;
        private readonly byte[] _colorTable = new byte[256 * 3];
        private PaletteQuantizer? _quantizer;
        private GifLzwEncoder? _lzw;
        private PooledBuffer? _converted;
        private PooledBuffer? _keys;
        private PooledBuffer? _indices;
        private PaletteRowMapper? _mapper;
        private PooledBuffer? _plane;
        private AnimationFrameDelta? _delta;
        private PooledBuffer? _canonical;
        private ImageFrame? _frame;
        private Step _step;
        private int _commentIndex;
        private int _row;
        private bool _headerWritten;
        private ushort _delay;
        private int _transparentIndex;
        private int _colorTableEntries;

        // The image being written: the frame of the operation, or (OptimizeFrames) the previous frame, from the canvases
        private Rectangle _image;
        private ushort _imageDelay;
        private int _imageDisposal;
        private bool _keepUnchanged;
        private bool _keptUnchanged;
        private bool _imageTransparent;
        private AnimationFramePlan _plan;
        private ushort _pendingDelay;
        private bool _pendingTransparent;
        private bool _frameTransparent;
        private bool _hasPending;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (GifEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            if (_encoder.AlphaMode == GifAlphaMode.Flatten && _encoder.BackgroundColor is null)
                throw new ArgumentException("GifAlphaMode.Flatten requires GifEncoder.BackgroundColor: set the opaque color the pixels are composited over, or use GifAlphaMode.Threshold.", nameof(options));

            _animated = options.Capabilities.IsAnimated;
            _threshold = _encoder.AlphaMode == GifAlphaMode.Threshold && PixelFormats.HasAlpha(_sourceFormat);
            _background = _encoder.AlphaMode == GifAlphaMode.Flatten && PixelFormats.HasAlpha(_sourceFormat) ? _encoder.BackgroundColor : null;
            _workFormat = _threshold ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
            _quantization = _encoder.Quantization;
            _fixedPalette = _quantization.Palette is { } palette ? GetFixedPalette(palette, nameof(options)) : null;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _image = new Rectangle(0, 0, _width, _height);
            _loopCount = _animated ? AnimationTiming.ToGifLoopCount(options.Animation?.TotalPlays) : null;

            // Comments (already filtered by the metadata policy: bare Latin-1 comments), one extension each
            _comments = [.. options.Metadata.TextEntries.Select(entry => Encoding.Latin1.GetBytes(entry.Value))];

            var scope = options.Scope;
            var planeBytes = PaletteRowMapper.IsErrorDiffusion(_quantization.Dithering) && _quantization.DitherStrength > 0 && _encoder.Interlaced ? (long)_width * _height : 0;
            if (planeBytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            try
            {
                _quantizer = new PaletteQuantizer(scope);
                _lzw = new GifLzwEncoder(scope);
                if (_sourceFormat != _workFormat)
                {
                    _converted = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
                }

                _keys = scope.Rent(_width * sizeof(int), AllocationKind.Temporary, clear: false);
                _indices = scope.Rent(_width, AllocationKind.Temporary, clear: false);
                _mapper = new PaletteRowMapper(scope, _width, _quantization.Dithering, _quantization.DitherStrength);

                if (planeBytes > 0)
                {
                    _plane = scope.Rent((int)planeBytes, AllocationKind.Temporary, clear: false);
                }

                if (_animated && _encoder.OptimizeFrames)
                {
                    // GIF draws every pixel that is not the transparent entry, and only a disposal makes a pixel transparent
                    _delta = new AnimationFrameDelta(scope, _width, _height, new AnimationDeltaRules { BytesPerPixel = 4, OverBlend = true, RestorePrevious = true, EnlargePreviousRegion = true });
                    _canonical = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
                }
            }
            catch
            {
                ReleaseBuffers();
                throw;
            }
        }

        private enum Step
        {
            None,
            Header,
            Comments,
            Compare,
            Histogram,
            Map,
            ImageHeader,
            ImageData,
            Commit,
            Trailer,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // An unrepresentable duration fails before anything of the frame is written (the writer stays usable)
            _ = AnimationTiming.ToGifDelay(frame.Metadata.Duration, _encoder.DurationRounding);
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("GIF output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (_quantizer is null || (!_animated && index > 0))
                throw new InvalidOperationException("A still GIF has a single frame.");

            _delay = AnimationTiming.ToGifDelay(frame.Metadata.Duration, _encoder.DurationRounding);
            _frame = frame;
            _row = 0;
            _step = _headerWritten ? GetFirstFrameStep() : Step.Header;
        }

        public override void BeginComplete(int frameCount)
        {
            if (frameCount < 1 || (!_animated && frameCount != 1))
                throw new InvalidOperationException("A still GIF has exactly one frame.");

            _row = 0;
            if (_hasPending)
            {
                // The last frame, written from the canvases; its disposal is that of a full-canvas frame
                StartCanvasImage(_delta!.LastRegion, _pendingDelay, DisposeRestoreBackground, _pendingTransparent);
                return;
            }

            _step = Step.Trailer;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    WriteHeader(output);
                    _headerWritten = true;
                    _step = Step.Comments;
                    return false;

                case Step.Comments:
                    if (_commentIndex < _comments.Length)
                    {
                        WriteComment(output, _comments[_commentIndex++]);
                        return false;
                    }

                    _row = 0;
                    _step = GetFirstFrameStep();
                    return false;

                case Step.Compare:
                    if (!CompareRows())
                        return false;

                    if (!_delta!.TryChoose(out _plan))
                    {
                        // The region of the previous frame grew: the frame is compared again
                        _row = 0;
                        return false;
                    }

                    StartCanvasImage(_plan.PreviousRegion, _pendingDelay, _plan.PreviousDisposal switch
                    {
                        AnimationDisposal.None => DisposeNone,
                        AnimationDisposal.ClearToTransparent => DisposeRestoreBackground,
                        _ => DisposeRestorePrevious,
                    }, _pendingTransparent);
                    return false;

                case Step.Histogram:
                    if (!AnalyzeRows())
                        return false;

                    // The transparent entry takes one of the MaxColors slots when the frame has transparent pixels
                    if (_fixedPalette is not null)
                    {
                        _quantizer!.SetPalette(_fixedPalette, reservedTransparent: true);
                    }
                    else
                    {
                        _quantizer!.BuildPalette(_quantizer.HasTransparency ? _quantization.MaxColors - 1 : _quantization.MaxColors);
                        if (!_quantizer.IsExact && _keptUnchanged && !_imageTransparent)
                        {
                            // The transparent entry of the unchanged pixels made the colors exceed the palette: they are
                            // written in their colors, which the image has anyway
                            _keepUnchanged = false;
                            _keptUnchanged = false;
                            _row = 0;
                            return false;
                        }
                    }

                    PreparePalette();
                    _row = 0;
                    ResetErrors();
                    _step = _plane is not null && !_quantizer.IsExact ? Step.Map : Step.ImageHeader;
                    return false;

                case Step.Map:
                    if (!MapPlaneRows())
                        return false;

                    _step = Step.ImageHeader;
                    goto case Step.ImageHeader;

                case Step.ImageHeader:
                    WriteImageHeader(output);
                    _row = 0;
                    ResetErrors();
                    _step = Step.ImageData;
                    return false;

                case Step.ImageData:
                    if (!EncodeRows(output))
                        return false;

                    if (_delta is not null)
                    {
                        // The previous frame is written: the frame of the operation is copied into the canvases, or the output ends
                        _row = 0;
                        _step = _frame is not null ? Step.Commit : Step.Trailer;
                        return false;
                    }

                    // The frame is no longer referenced once its operation completes; buffers are kept for the next frame
                    _frame = null;
                    _step = Step.None;
                    if (!_animated)
                    {
                        ReleaseBuffers();
                    }

                    return true;

                case Step.Commit:
                    if (!CommitRows())
                        return false;

                    _pendingDelay = _delay;
                    _pendingTransparent = _frameTransparent;
                    _hasPending = true;
                    _frame = null;
                    _step = Step.None;
                    return true;

                case Step.Trailer:
                    output.Write([Trailer]);
                    _step = Step.None;
                    ReleaseBuffers();
                    return true;

                default:
                    throw new InvalidOperationException("No GIF encoding operation is in progress.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            _frame = null;
            ReleaseBuffers();
            base.Dispose(disposing);
        }

        private static void WriteComment(ImageOutputBuffer output, ReadOnlySpan<byte> comment)
        {
            output.Write([ExtensionIntroducer, CommentLabel]);
            WriteSubBlocks(output, comment);
        }

        private static void WriteSubBlocks(ImageOutputBuffer output, ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var length = Math.Min(data.Length, GifLzwEncoder.MaxSubBlockLength);
                var span = output.GetSpan(length + 1);
                span[0] = (byte)length;
                data[..length].CopyTo(span[1..]);
                output.Advance(length + 1);
                data = data[length..];
            }

            output.Write([0]);
        }

        private void ReleaseBuffers()
        {
            _quantizer?.Dispose();
            _quantizer = null;
            _lzw?.Dispose();
            _lzw = null;
            _converted?.Dispose();
            _converted = null;
            _keys?.Dispose();
            _keys = null;
            _indices?.Dispose();
            _indices = null;
            _mapper?.Dispose();
            _mapper = null;
            _plane?.Dispose();
            _plane = null;
            _delta?.Dispose();
            _delta = null;
            _canonical?.Dispose();
            _canonical = null;
        }

        /// <summary>
        /// Gets the first step of a frame once the header is written: the image of the frame, or with
        /// <see cref="GifEncoder.OptimizeFrames"/> its comparison with the canvases (the first frame is copied into them).
        /// </summary>
        private Step GetFirstFrameStep()
        {
            if (_delta is null)
            {
                _image = new Rectangle(0, 0, _width, _height);
                _imageDelay = _delay;
                _imageDisposal = _animated ? DisposeRestoreBackground : 0;
                _imageTransparent = false;
                _keepUnchanged = false;
                _keptUnchanged = false;
                return Step.Histogram;
            }

            if (_delta.CommittedFrames == 0)
                return Step.Commit;

            _delta.BeginCompare();
            return Step.Compare;
        }

        /// <summary>Starts the image of the previous frame, written from the canvases, and its passes.</summary>
        private void StartCanvasImage(Rectangle region, ushort delay, int disposal, bool transparent)
        {
            _image = region;
            _imageDelay = delay;
            _imageDisposal = disposal;
            _imageTransparent = transparent;
            _keepUnchanged = true;
            _keptUnchanged = false;
            _row = 0;
            _step = Step.Histogram;
        }

        private void WriteHeader(ImageOutputBuffer output)
        {
            // Header and logical screen descriptor: no global color table (every frame has its local table), 8-bit color
            // resolution, background color index 0 (never painted by modern decoders), no aspect ratio
            Span<byte> header = stackalloc byte[13];
            "GIF89a"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)_width);
            BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)_height);
            header[10] = 0x70;
            header[11] = 0;
            header[12] = 0;
            output.Write(header);
            if (_loopCount is { } loop)
            {
                // NETSCAPE2.0 loop sub-block: the number of repetitions after the first play (0 = infinite)
                Span<byte> extension = stackalloc byte[19];
                extension[0] = ExtensionIntroducer;
                extension[1] = ApplicationLabel;
                extension[2] = 11;
                "NETSCAPE2.0"u8.CopyTo(extension[3..]);
                extension[14] = 3;
                extension[15] = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(extension[16..], loop);
                extension[18] = 0;
                output.Write(extension);
            }
        }

        /// <summary>Compares the alpha-reduced rows of the frame with the canvases, a band per call.</summary>
        /// <returns><see langword="true"/> when every row was compared.</returns>
        private bool CompareRows()
        {
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span));
            var row = _canonical!.Span;
            using var lease = _frame!.GetStorage().AcquireLease();
            var processed = 0;
            while (_row < _height && (processed == 0 || processed < PixelsPerStep))
            {
                LoadKeys(in lease, _row, keys);
                ToCanonical(keys, row);
                _delta!.CompareRow(_row, row);
                _row++;
                processed += _width;
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _height;
        }

        /// <summary>Copies the alpha-reduced rows of the frame into the canvases, a band per call.</summary>
        /// <returns><see langword="true"/> when every row was copied.</returns>
        private bool CommitRows()
        {
            var delta = _delta!;
            if (_row == 0)
            {
                delta.BeginCommit(delta.CommittedFrames == 0 ? new Rectangle(0, 0, _width, _height) : _plan.Region);
                _frameTransparent = false;
            }

            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span));
            var row = _canonical!.Span;
            using var lease = _frame!.GetStorage().AcquireLease();
            var processed = 0;
            while (_row < _height && (processed == 0 || processed < PixelsPerStep))
            {
                LoadKeys(in lease, _row, keys);
                _frameTransparent |= keys.Contains(PaletteQuantizer.TransparentKey);
                ToCanonical(keys, row);
                delta.CommitRow(_row, row);
                _row++;
                processed += _width;
            }

            CancellationToken.ThrowIfCancellationRequested();
            if (_row < _height)
                return false;

            delta.EndCommit();
            return true;
        }

        /// <summary>Reads the rows of the image into the histogram, a band per call.</summary>
        /// <returns><see langword="true"/> when every row was added.</returns>
        private bool AnalyzeRows()
        {
            var quantizer = _quantizer!;
            if (_row == 0)
            {
                quantizer.Begin(_fixedPalette is null ? _quantization.MaxColors : ImagePalette.MaxCount);
                if (_imageTransparent)
                {
                    // The frame has transparent pixels outside the region: the image has the transparent entry anyway
                    quantizer.Add([PaletteQuantizer.TransparentKey]);
                }
            }

            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span))[.._image.Width];
            var processed = 0;
            while (_row < _image.Height && (processed == 0 || processed < PixelsPerStep))
            {
                LoadImageKeys(_row, keys);
                quantizer.Add(keys);
                _row++;
                processed += _image.Width;
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _image.Height;
        }

        private void PreparePalette()
        {
            var quantizer = _quantizer!;
            var colors = quantizer.PaletteCount;
            _transparentIndex = quantizer.HasTransparency ? colors : -1;
            var entries = colors + (quantizer.HasTransparency ? 1 : 0);
            _colorTableEntries = 2;
            while (_colorTableEntries < entries)
            {
                _colorTableEntries *= 2;
            }

            var table = _colorTable.AsSpan(0, _colorTableEntries * 3);
            table.Clear();
            var palette = quantizer.Palette;
            for (var i = 0; i < palette.Length; i++)
            {
                table[3 * i] = (byte)(palette[i] >> 16);
                table[(3 * i) + 1] = (byte)(palette[i] >> 8);
                table[(3 * i) + 2] = (byte)palette[i];
            }
        }

        /// <summary>Maps the rows of an interlaced dithered image into the index plane, in display order, a band per call.</summary>
        private bool MapPlaneRows()
        {
            var width = _image.Width;
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span))[..width];
            var plane = _plane!.Span;
            var processed = 0;
            while (_row < _image.Height && (processed == 0 || processed < PixelsPerStep))
            {
                LoadImageKeys(_row, keys);
                MapRow(keys, plane.Slice(_row * width, width), _row);
                _row++;
                processed += width;
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _image.Height;
        }

        private void WriteImageHeader(ImageOutputBuffer output)
        {
            var hasTransparency = _transparentIndex >= 0;
            if (_animated || hasTransparency || _imageDelay != 0)
            {
                // Graphic Control Extension: the disposal of the frame (2 for full-canvas animation frames: cleared to
                // transparent before the next frame; not specified for a still image); the transparent flag and index; the
                // delay in hundredths
                Span<byte> control = stackalloc byte[8];
                control[0] = ExtensionIntroducer;
                control[1] = GraphicControlLabel;
                control[2] = 4;
                control[3] = (byte)((_imageDisposal << 2) | (hasTransparency ? 1 : 0));
                BinaryPrimitives.WriteUInt16LittleEndian(control[4..], _imageDelay);
                control[6] = hasTransparency ? (byte)_transparentIndex : (byte)0;
                control[7] = 0;
                output.Write(control);
            }

            // Image descriptor of the region of the canvas, with a local color table of 2^(n + 1) entries
            var sizeField = 0;
            while ((2 << sizeField) < _colorTableEntries)
            {
                sizeField++;
            }

            Span<byte> descriptor = stackalloc byte[10];
            descriptor[0] = ImageSeparator;
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[1..], (ushort)_image.X);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[3..], (ushort)_image.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[5..], (ushort)_image.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[7..], (ushort)_image.Height);
            descriptor[9] = (byte)(0x80 | (_encoder.Interlaced ? 0x40 : 0) | sizeField);
            output.Write(descriptor);
            output.Write(_colorTable.AsSpan(0, _colorTableEntries * 3));
            var codeSize = GifLzwEncoder.GetMinimumCodeSize(_colorTableEntries);
            output.Write([(byte)codeSize]);
            _lzw!.Reset(codeSize);
        }

        /// <summary>Maps and compresses rows in datastream order until the output should be flushed or a band is done.</summary>
        /// <returns><see langword="true"/> when the datastream is complete.</returns>
        private bool EncodeRows(ImageOutputBuffer output)
        {
            var lzw = _lzw!;
            var width = _image.Width;
            var height = _image.Height;
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span))[..width];
            var indices = _indices!.Span[..width];
            var processed = 0;
            while (_row < height)
            {
                if (processed > 0 && (processed >= PixelsPerStep || output.ShouldFlush))
                    return false;

                var y = _encoder.Interlaced ? GetInterlacedRow(_row, height) : _row;
                if (_plane is not null && !_quantizer!.IsExact)
                {
                    lzw.Write(_plane.Span.Slice(y * width, width), output);
                }
                else
                {
                    LoadImageKeys(y, keys);
                    MapRow(keys, indices, y);
                    lzw.Write(indices, output);
                }

                _row++;
                processed += width;
            }

            lzw.Finish(output);
            return true;
        }

        /// <summary>
        /// Gets the keys of a row of the image: a row of the frame, or (<see cref="GifEncoder.OptimizeFrames"/>) the row of the
        /// region of the displayed canvas, its unchanged pixels transparent when <see cref="_keepUnchanged"/> is set.
        /// </summary>
        /// <param name="y">The row of the image, from the top of its region.</param>
        /// <param name="keys">The keys, one per pixel of the region.</param>
        private void LoadImageKeys(int y, Span<int> keys)
        {
            if (_delta is null)
            {
                using var lease = _frame!.GetStorage().AcquireLease();
                LoadKeys(in lease, y, keys);
                return;
            }

            // The displayed frame of the image, and the canvas it is drawn on
            var bytes = _image.X * 4;
            var length = _image.Width * 4;
            var displayed = _delta.Displayed.GetRow(_image.Y + y).Slice(bytes, length);
            var canvas = _delta.Canvas!.GetRow(_image.Y + y).Slice(bytes, length);
            for (var x = 0; x < keys.Length; x++)
            {
                var pixel = displayed.Slice(4 * x, 4);
                if (pixel[3] == 0)
                {
                    keys[x] = PaletteQuantizer.TransparentKey;
                }
                else if (_keepUnchanged && pixel.SequenceEqual(canvas.Slice(4 * x, 4)))
                {
                    keys[x] = PaletteQuantizer.TransparentKey;
                    _keptUnchanged = true;
                }
                else
                {
                    keys[x] = (pixel[0] << 16) | (pixel[1] << 8) | pixel[2];
                }
            }
        }

        /// <summary>
        /// Converts a source row to color keys: alpha reduced by the threshold (transparent below it) or flattened onto the
        /// background by the shared converter, 16-bit samples reduced to 8 bits (nearest), gray
        /// replicated.
        /// </summary>
        private void LoadKeys(scoped in PixelLease lease, int y, Span<int> keys)
        {
            ReadOnlySpan<byte> source = lease.GetRowBytes(y);
            if (_converted is not null)
            {
                var converted = _converted.Span;
                PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, converted, _background, ImageFormat.Gif);
                source = converted;
            }

            if (_threshold)
            {
                var threshold = _encoder.AlphaThreshold;
                for (var x = 0; x < _width; x++)
                {
                    var pixel = source.Slice(4 * x, 4);
                    keys[x] = pixel[3] < threshold ? PaletteQuantizer.TransparentKey : (pixel[0] << 16) | (pixel[1] << 8) | pixel[2];
                }
            }
            else
            {
                for (var x = 0; x < _width; x++)
                {
                    var pixel = source.Slice(3 * x, 3);
                    keys[x] = (pixel[0] << 16) | (pixel[1] << 8) | pixel[2];
                }
            }
        }

        /// <summary>Converts keys to the pixels of the canvases: opaque 8-bit RGBA, or transparent black.</summary>
        private static void ToCanonical(ReadOnlySpan<int> keys, Span<byte> row)
        {
            for (var x = 0; x < keys.Length; x++)
            {
                var key = keys[x];
                var pixel = row.Slice(4 * x, 4);
                if (key == PaletteQuantizer.TransparentKey)
                {
                    pixel.Clear();
                }
                else
                {
                    pixel[0] = (byte)(key >> 16);
                    pixel[1] = (byte)(key >> 8);
                    pixel[2] = (byte)key;
                    pixel[3] = byte.MaxValue;
                }
            }
        }

        private void ResetErrors() => _mapper?.Reset();

        /// <summary>Maps a row of keys (rows in display order for error diffusion) to palette indices; see <see cref="PaletteRowMapper.MapRow"/>.</summary>
        private void MapRow(ReadOnlySpan<int> keys, Span<byte> indices, int y) => _mapper!.MapRow(_quantizer!, keys, indices, _transparentIndex, y);

        /// <summary>
        /// Reduces the entries of a fixed palette as the pixels are reduced: below the alpha threshold an entry is dropped (the
        /// transparent entry stands for it) and above it its alpha is ignored, or it is composited over the background.
        /// </summary>
        /// <exception cref="ArgumentException">No entry is left, or no room is left for the transparent entry.</exception>
        private int[] GetFixedPalette(ImagePalette palette, string paramName)
        {
            var keys = new List<int>(palette.Count);
            Span<byte> color = stackalloc byte[4];
            Span<byte> opaque = stackalloc byte[3];
            foreach (var entry in palette.Colors)
            {
                int key;
                if (_encoder.AlphaMode == GifAlphaMode.Flatten)
                {
                    color[0] = entry.R;
                    color[1] = entry.G;
                    color[2] = entry.B;
                    color[3] = entry.A;
                    PixelConverter.ConvertRow(PixelFormat.Rgba32, color, PixelFormat.Rgb24, opaque, _encoder.BackgroundColor, ImageFormat.Gif);
                    key = (opaque[0] << 16) | (opaque[1] << 8) | opaque[2];
                }
                else if (entry.A < _encoder.AlphaThreshold)
                {
                    continue;
                }
                else
                {
                    key = (entry.R << 16) | (entry.G << 8) | entry.B;
                }

                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }

            if (keys.Count == 0)
                throw new ArgumentException("GifEncoder.Quantization.Palette has no color a GIF frame can store: every entry is below GifEncoder.AlphaThreshold.", paramName);

            if (keys.Count == ImagePalette.MaxCount && _threshold)
                throw new ArgumentException("GifEncoder.Quantization.Palette has 256 opaque colors and the pixels have alpha: a GIF color table has no room left for the transparent entry. Use a palette of at most 255 opaque colors.", paramName);

            return [.. keys];
        }
    }
}
