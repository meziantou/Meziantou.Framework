using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The QOI encoder registration: one still image per output, written by one streaming session.</summary>
internal sealed class QoiEncoderCodec : ImageEncoderCodec
{
    public static QoiEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Qoi;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one QOI output: the header, then the pixels in bounded bands (at most
    /// <see cref="PixelsPerCall"/> pixels per <see cref="Encode"/> call, converted to 8-bit RGBA one band at a time), then the
    /// end marker at completion. The private state is the 64-entry running index, the previous pixel, the pending run and one
    /// band of converted pixels: memory does not depend on the image size beyond one row band.
    /// </summary>
    /// <remarks>
    /// Chunk selection, for each pixel: a run of the previous pixel (<c>QOI_OP_RUN</c>, up to 62, flushed at the last pixel),
    /// else the running index entry (<c>QOI_OP_INDEX</c>), else, when the alpha is unchanged, the smallest difference chunk
    /// (<c>QOI_OP_DIFF</c>, <c>QOI_OP_LUMA</c>) or <c>QOI_OP_RGB</c>, else <c>QOI_OP_RGBA</c>. Every chunk except runs and index
    /// hits stores the pixel in the index, so that the index always matches the decoder's (which stores every pixel it
    /// produces; a run or an index hit stores a pixel the index already holds, except for the initial pixel, whose slot can
    /// only be read back by an index chunk for a pixel equal to it).
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        /// <summary>The largest number of pixels encoded by one <see cref="Encode"/> call.</summary>
        internal const int PixelsPerCall = 16 * 1024;

        private readonly PixelFormat _sourceFormat;
        private readonly int _width;
        private readonly int _height;
        private readonly byte _channels;
        private readonly byte _colorSpace;
        private readonly uint[] _index = new uint[QoiFormat.IndexLength];
        private PooledBuffer? _band;
        private ImageFrame? _frame;
        private Step _step;
        private uint _previous = QoiFormat.InitialPixel;
        private int _run;
        private int _x;
        private int _y;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _sourceFormat = options.PixelFormat;
            var encoder = (QoiEncoder)options.Encoder;
            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"QOI stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set QoiEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Qoi, "Bit depth reduction");

            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _channels = PixelFormats.HasAlpha(_sourceFormat) ? (byte)4 : (byte)3;
            _colorSpace = options.Metadata.TransferFunction == ColorTransferFunction.Linear ? QoiFormat.ColorSpaceLinear : QoiFormat.ColorSpaceSrgb;

            // One band of 8-bit RGBA pixels (Rgba32 sources are read in place), allocated before any output
            if (_sourceFormat != PixelFormat.Rgba32)
            {
                _band = options.Scope.Rent((int)Math.Min(_width, PixelsPerCall) * 4, AllocationKind.Temporary, clear: false);
            }
        }

        private enum Step
        {
            None,
            Header,
            Pixels,
            EndMarker,
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("QOI output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A QOI image stores a single frame.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.EndMarker;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                {
                    var header = output.GetSpan(QoiFormat.HeaderLength);
                    QoiFormat.Magic.CopyTo(header);
                    BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)_width);
                    BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)_height);
                    header[12] = _channels;
                    header[13] = _colorSpace;
                    output.Advance(QoiFormat.HeaderLength);
                    _step = Step.Pixels;
                    return false;
                }

                case Step.Pixels:
                    EncodeBand(output);
                    if (_y < _height)
                        return false;

                    _frame = null;
                    _step = Step.None;
                    return true;

                case Step.EndMarker:
                    output.Write(QoiFormat.EndMarker);
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No QOI operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _band?.Dispose();
                _band = null;
                _frame = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Encodes up to <see cref="PixelsPerCall"/> pixels, row segment by row segment.</summary>
        private void EncodeBand(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_sourceFormat);
            var budget = PixelsPerCall;
            using var lease = frame.GetStorage().AcquireLease();
            while (budget > 0 && _y < _height)
            {
                var count = Math.Min(budget, _width - _x);
                var source = lease.GetRowBytes(_y).Slice(_x * bytesPerPixel, count * bytesPerPixel);
                ReadOnlySpan<byte> rgba;
                if (_band is null)
                {
                    rgba = source;
                }
                else
                {
                    var band = _band.RawBuffer.AsSpan(0, count * 4);
                    PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Rgba32, band, background: null, ImageFormat.Qoi);
                    rgba = band;
                }

                _x += count;
                var isLast = _x == _width && _y == _height - 1;

                // At most 5 bytes per pixel, plus the run flushed after the last pixel
                var destination = output.GetSpan((count * QoiFormat.MaxChunkLength) + 1);
                var written = EncodePixels(unsafe(MemoryMarshal.Cast<byte, uint>(rgba)), destination, isLast);
                output.Advance(written);
                budget -= count;
                if (_x == _width)
                {
                    _x = 0;
                    _y++;
                }
            }
        }

        private int EncodePixels(ReadOnlySpan<uint> pixels, Span<byte> destination, bool flushRun)
        {
            var index = _index.AsSpan();
            var previous = _previous;
            var run = _run;
            var o = 0;
            foreach (var pixel in pixels)
            {
                if (pixel == previous)
                {
                    run++;
                    if (run == QoiFormat.MaxRun)
                    {
                        destination[o++] = QoiFormat.OpRun | (QoiFormat.MaxRun - 1);
                        run = 0;
                    }

                    continue;
                }

                if (run > 0)
                {
                    destination[o++] = (byte)(QoiFormat.OpRun | (run - 1));
                    run = 0;
                }

                var hash = QoiFormat.Hash(pixel);
                if (index[hash] == pixel)
                {
                    destination[o++] = (byte)(QoiFormat.OpIndex | hash);
                }
                else
                {
                    index[hash] = pixel;
                    if ((pixel >> 24) == (previous >> 24))
                    {
                        // Differences wrap around 256 (signed 8-bit)
                        var dr = (sbyte)(byte)(pixel - previous);
                        var dg = (sbyte)(byte)((pixel >> 8) - (previous >> 8));
                        var db = (sbyte)(byte)((pixel >> 16) - (previous >> 16));
                        var drDg = dr - dg;
                        var dbDg = db - dg;
                        if (dr is >= -2 and <= 1 && dg is >= -2 and <= 1 && db is >= -2 and <= 1)
                        {
                            destination[o++] = (byte)(QoiFormat.OpDiff | ((dr + 2) << 4) | ((dg + 2) << 2) | (db + 2));
                        }
                        else if (dg is >= -32 and <= 31 && drDg is >= -8 and <= 7 && dbDg is >= -8 and <= 7)
                        {
                            destination[o++] = (byte)(QoiFormat.OpLuma | (dg + 32));
                            destination[o++] = (byte)(((drDg + 8) << 4) | (dbDg + 8));
                        }
                        else
                        {
                            destination[o++] = QoiFormat.OpRgb;
                            destination[o++] = (byte)pixel;
                            destination[o++] = (byte)(pixel >> 8);
                            destination[o++] = (byte)(pixel >> 16);
                        }
                    }
                    else
                    {
                        destination[o++] = QoiFormat.OpRgba;
                        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(o, 4), pixel);
                        o += 4;
                    }
                }

                previous = pixel;
            }

            if (flushRun && run > 0)
            {
                destination[o++] = (byte)(QoiFormat.OpRun | (run - 1));
                run = 0;
            }

            _previous = previous;
            _run = run;
            return o;
        }
    }
}
