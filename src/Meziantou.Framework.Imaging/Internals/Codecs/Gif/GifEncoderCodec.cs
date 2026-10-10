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
    /// comment extension per <see cref="Encode"/> call. Each frame then runs three bounded passes over the borrowed frame:
    /// the histogram (alpha reduced, colors keyed), the palette, and the mapping and LZW compression of the rows in datastream
    /// order (interlaced or not), preceded by its Graphic Control Extension, a full-canvas image descriptor and its local
    /// color table. Completion writes the trailer.
    /// </summary>
    /// <remarks>
    /// Every frame covers the whole canvas and every animation frame is disposed with "restore to background" (2), which
    /// GIF decoders apply as a clear to transparent: each displayed frame is drawn on a transparent
    /// canvas, so pixels that become transparent never show stale content, whatever the previous frame. Nothing of a frame
    /// is kept after its operation: working memory (rows, histogram, string table, and the index plane of interlaced dithered
    /// frames) is allocated on creation and is independent of the number of frames.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const byte ExtensionIntroducer = 0x21;
        private const byte ImageSeparator = 0x2C;
        private const byte Trailer = 0x3B;
        private const byte GraphicControlLabel = 0xF9;
        private const byte CommentLabel = 0xFE;
        private const byte ApplicationLabel = 0xFF;
        private const int DisposeRestoreBackground = 2;

        private readonly GifEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _workFormat;
        private readonly Rgba64? _background;
        private readonly bool _threshold;
        private readonly bool _animated;
        private readonly bool _dither;
        private readonly int _width;
        private readonly int _height;
        private readonly ushort? _loopCount;
        private readonly byte[][] _comments;
        private readonly byte[] _colorTable = new byte[256 * 3];
        private GifQuantizer? _quantizer;
        private GifLzwEncoder? _lzw;
        private PooledBuffer? _converted;
        private PooledBuffer? _keys;
        private PooledBuffer? _indices;
        private PooledBuffer? _errors;
        private PooledBuffer? _plane;
        private ImageFrame? _frame;
        private Step _step;
        private int _commentIndex;
        private int _row;
        private bool _headerWritten;
        private ushort _delay;
        private int _transparentIndex;
        private int _colorTableEntries;

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
            _dither = _encoder.Dithering == GifDithering.FloydSteinberg;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _loopCount = _animated ? AnimationTiming.ToGifLoopCount(options.Animation?.TotalPlays) : null;

            // Comments (already filtered by the metadata policy: bare Latin-1 comments), one extension each
            _comments = [.. options.Metadata.TextEntries.Select(entry => Encoding.Latin1.GetBytes(entry.Value))];

            var scope = options.Scope;
            var planeBytes = _dither && _encoder.Interlaced ? (long)_width * _height : 0;
            if (planeBytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            try
            {
                _quantizer = new GifQuantizer(scope);
                _lzw = new GifLzwEncoder(scope);
                if (_sourceFormat != _workFormat)
                {
                    _converted = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
                }

                _keys = scope.Rent(_width * sizeof(int), AllocationKind.Temporary, clear: false);
                _indices = scope.Rent(_width, AllocationKind.Temporary, clear: false);
                if (_dither)
                {
                    // Two rows of error terms (current and next), one pixel of margin on each side, three channels
                    _errors = scope.Rent(2 * (_width + 2) * 3 * sizeof(int), AllocationKind.Temporary, clear: true);
                }

                if (planeBytes > 0)
                {
                    _plane = scope.Rent((int)planeBytes, AllocationKind.Temporary, clear: false);
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
            Histogram,
            Map,
            ImageHeader,
            ImageData,
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
            _step = _headerWritten ? Step.Histogram : Step.Header;
        }

        public override void BeginComplete(int frameCount)
        {
            if (frameCount < 1 || (!_animated && frameCount != 1))
                throw new InvalidOperationException("A still GIF has exactly one frame.");

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

                    _step = Step.Histogram;
                    _row = 0;
                    goto case Step.Histogram;

                case Step.Histogram:
                    if (!AnalyzeRows())
                        return false;

                    // The transparent entry takes one of the MaxColors slots when the frame has transparent pixels
                    _quantizer!.BuildPalette(_quantizer.HasTransparency ? _encoder.MaxColors - 1 : _encoder.MaxColors);
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

                    // The frame is no longer referenced once its operation completes; buffers are kept for the next frame
                    _frame = null;
                    _step = Step.None;
                    if (!_animated)
                    {
                        ReleaseBuffers();
                    }

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
            _errors?.Dispose();
            _errors = null;
            _plane?.Dispose();
            _plane = null;
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

        /// <summary>Reads source rows into the histogram, a band per call.</summary>
        /// <returns><see langword="true"/> when every row was added.</returns>
        private bool AnalyzeRows()
        {
            var quantizer = _quantizer!;
            if (_row == 0)
            {
                quantizer.Begin(_encoder.MaxColors);
            }

            using var lease = _frame!.GetStorage().AcquireLease();
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span));
            var processed = 0;
            while (_row < _height && (processed == 0 || processed < PixelsPerStep))
            {
                LoadKeys(in lease, _row, keys);
                quantizer.Add(keys);
                _row++;
                processed += _width;
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _height;
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

        /// <summary>Maps the rows of an interlaced dithered frame into the index plane, in display order, a band per call.</summary>
        private bool MapPlaneRows()
        {
            using var lease = _frame!.GetStorage().AcquireLease();
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span));
            var plane = _plane!.Span;
            var processed = 0;
            while (_row < _height && (processed == 0 || processed < PixelsPerStep))
            {
                LoadKeys(in lease, _row, keys);
                MapRow(keys, plane.Slice(_row * _width, _width));
                _row++;
                processed += _width;
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _height;
        }

        private void WriteImageHeader(ImageOutputBuffer output)
        {
            var hasTransparency = _transparentIndex >= 0;
            if (_animated || hasTransparency || _delay != 0)
            {
                // Graphic Control Extension: disposal 2 for animation frames (cleared to transparent before the next frame),
                // not specified for a still image; the transparent flag and index; the delay in hundredths
                Span<byte> control = stackalloc byte[8];
                control[0] = ExtensionIntroducer;
                control[1] = GraphicControlLabel;
                control[2] = 4;
                control[3] = (byte)(((_animated ? DisposeRestoreBackground : 0) << 2) | (hasTransparency ? 1 : 0));
                BinaryPrimitives.WriteUInt16LittleEndian(control[4..], _delay);
                control[6] = hasTransparency ? (byte)_transparentIndex : (byte)0;
                control[7] = 0;
                output.Write(control);
            }

            // Image descriptor covering the canvas, with a local color table of 2^(n + 1) entries
            var sizeField = 0;
            while ((2 << sizeField) < _colorTableEntries)
            {
                sizeField++;
            }

            Span<byte> descriptor = stackalloc byte[10];
            descriptor[0] = ImageSeparator;
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[1..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[3..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[5..], (ushort)_width);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[7..], (ushort)_height);
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
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span));
            var indices = _indices!.Span[.._width];
            using (var lease = _frame!.GetStorage().AcquireLease())
            {
                var processed = 0;
                while (_row < _height)
                {
                    if (processed > 0 && (processed >= PixelsPerStep || output.ShouldFlush))
                        return false;

                    var y = _encoder.Interlaced ? GetInterlacedRow(_row, _height) : _row;
                    if (_plane is not null && !_quantizer!.IsExact)
                    {
                        lzw.Write(_plane.Span.Slice(y * _width, _width), output);
                    }
                    else
                    {
                        LoadKeys(in lease, y, keys);
                        MapRow(keys, indices);
                        lzw.Write(indices, output);
                    }

                    _row++;
                    processed += _width;
                }
            }

            lzw.Finish(output);
            return true;
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
                    keys[x] = pixel[3] < threshold ? GifQuantizer.TransparentKey : (pixel[0] << 16) | (pixel[1] << 8) | pixel[2];
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

        private void ResetErrors() => _errors?.Span.Clear();

        /// <summary>
        /// Maps a row of keys (rows in display order when dithering) to palette indices: transparent pixels to the transparent
        /// index, colors to their palette entry, or with Floyd–Steinberg dithering (when the palette is not exact) the nearest
        /// entry of the color plus the diffused error, the error of each pixel being spread 7/16 right, 3/16 below left,
        /// 5/16 below and 1/16 below right (left to right on every row; integer sixteenths, the sum rounded with
        /// <c>(e + 8) &gt;&gt; 4</c>; transparent pixels neither receive nor spread error).
        /// </summary>
        private void MapRow(ReadOnlySpan<int> keys, Span<byte> indices)
        {
            var quantizer = _quantizer!;
            if (!_dither || quantizer.IsExact)
            {
                for (var x = 0; x < _width; x++)
                {
                    var key = keys[x];
                    indices[x] = key == GifQuantizer.TransparentKey ? (byte)_transparentIndex : (byte)quantizer.Map(key);
                }

                return;
            }

            var errors = unsafe(MemoryMarshal.Cast<byte, int>(_errors!.Span));
            var stride = (_width + 2) * 3;
            var current = errors[..stride];
            var next = errors.Slice(stride, stride);
            var palette = quantizer.Palette;
            for (var x = 0; x < _width; x++)
            {
                var key = keys[x];
                if (key == GifQuantizer.TransparentKey)
                {
                    indices[x] = (byte)_transparentIndex;
                    continue;
                }

                var offset = (x + 1) * 3;
                var r = Math.Clamp(((key >> 16) & 0xFF) + ((current[offset] + 8) >> 4), 0, 255);
                var g = Math.Clamp(((key >> 8) & 0xFF) + ((current[offset + 1] + 8) >> 4), 0, 255);
                var b = Math.Clamp((key & 0xFF) + ((current[offset + 2] + 8) >> 4), 0, 255);
                var index = quantizer.Map((r << 16) | (g << 8) | b);
                indices[x] = (byte)index;
                var color = palette[index];
                Spread(current, next, offset, 0, r - ((color >> 16) & 0xFF));
                Spread(current, next, offset, 1, g - ((color >> 8) & 0xFF));
                Spread(current, next, offset, 2, b - (color & 0xFF));
            }

            // The next row's errors become the current ones; the following row starts without error
            next.CopyTo(current);
            next.Clear();

            static void Spread(Span<int> current, Span<int> next, int offset, int channel, int error)
            {
                current[offset + 3 + channel] += error * 7;
                next[offset - 3 + channel] += error * 3;
                next[offset + channel] += error * 5;
                next[offset + 3 + channel] += error;
            }
        }
    }
}
