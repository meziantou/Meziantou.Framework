using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The AVIF encoder registration: still images, coded by the AV1 encoder of the library.</summary>
internal sealed class AvifEncoderCodec : ImageEncoderCodec
{
    public static AvifEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Avif;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one AVIF output. The rows of the frame are converted to the planes of the AV1 encoders (the
    /// color image, and the alpha plane of a pixel format that has alpha), which encode them a row of superblocks at a time.
    /// The file is written once both are coded, because the boxes that describe the image hold the size of what follows
    /// them: a still image goes to any stream, without seeking.
    /// </summary>
    private sealed class Session : ImageEncoderSession
    {
        private const int RowsPerCall = 32;
        private const int EmitPieceLength = 64 * 1024;

        private readonly AvifEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _layout;
        private readonly int _width;
        private readonly int _height;
        private readonly CicpColorDescription _colorDescription;
        private readonly AvifRgbConverter _converter;
        private readonly byte[]? _icc;
        private readonly byte[]? _exif;
        private readonly byte[]? _xmp;
        private readonly ExifOrientation _orientation;
        private readonly List<ReadOnlyMemory<byte>> _pieces = [];
        private Av1Encoder? _color;
        private Av1Encoder? _alpha;
        private PooledBuffer? _rows;
        private PooledPayloadWriter? _prefix;
        private ImageFrame? _frame;
        private Step _step;
        private int _row;
        private bool _isTransparent;
        private int _pieceIndex;
        private int _pieceOffset;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (AvifEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            var bitDepth = ResolveBitDepth(_encoder, _sourceFormat);
            var monochrome = PixelFormats.IsGrayscale(_sourceFormat);
            var deep = PixelFormats.GetBitsPerComponent(_sourceFormat) > 8;
            _layout = monochrome ? (deep ? PixelFormat.Gray16 : PixelFormat.Gray8) : (deep ? PixelFormat.Rgba64 : PixelFormat.Rgba32);

            // The lossless coding has no color conversion: the identity matrix, in full range and without subsampling. The
            // lossy coding keeps the matrix and the range of the color description of the image when the library converts
            // them, and uses BT.601 in full range otherwise. No matrix applies to a monochrome image
            var lossless = _encoder.Compression is AvifCompression.Lossless;
            var metadata = options.Metadata;
            var cicp = metadata.Cicp;
            var primaries = cicp?.ColorPrimaries ?? Av1Constants.CpUnspecified;
            var transfer = cicp?.TransferCharacteristics ?? Av1Constants.TcUnspecified;
            var hasMatrix = cicp is { MatrixCoefficients: Av1Constants.McBt709 or Av1Constants.McFcc or Av1Constants.McBt470BG or Av1Constants.McBt601 or Av1Constants.McSmpte240 or Av1Constants.McSmpteYcgco or Av1Constants.McBt2020Ncl };
            int matrix;
            var fullRange = true;
            var subsamplingX = 0;
            var subsamplingY = 0;
            if (monochrome)
            {
                matrix = hasMatrix ? cicp!.MatrixCoefficients : Av1Constants.McUnspecified;
                fullRange = lossless || (cicp?.FullRange ?? true);
            }
            else if (lossless)
            {
                matrix = Av1Constants.McIdentity;
            }
            else
            {
                matrix = hasMatrix ? cicp!.MatrixCoefficients : Av1Constants.McBt601;
                fullRange = !hasMatrix || cicp!.FullRange;
                subsamplingX = _encoder.ChromaSubsampling is AvifChromaSubsampling.Ratio444 ? 0 : 1;
                subsamplingY = _encoder.ChromaSubsampling is AvifChromaSubsampling.Auto or AvifChromaSubsampling.Ratio420 ? 1 : 0;
            }

            _colorDescription = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
            _converter = new AvifRgbConverter(_layout, bitDepth, subsamplingX, subsamplingY, matrix, fullRange, _encoder.ClearTransparentColors);
            _icc = metadata.IccProfile?.Data.ToArray();
            _exif = metadata.Exif;
            _xmp = metadata.XmpProfile?.Data.ToArray();
            _orientation = metadata.Orientation;

            // Every buffer whose size is known is rented before any output, so that a limit failure leaves no output
            try
            {
                var scope = options.Scope;
                var settings = new Av1EncoderSettings(_width, _height, bitDepth, monochrome, subsamplingX, subsamplingY, primaries, transfer, matrix, fullRange, ChromaSamplePosition: 0, StillPicture: true, lossless, _encoder.Quality, _encoder.Effort);
                _color = new Av1Encoder(scope, settings);
                if (PixelFormats.HasAlpha(_sourceFormat))
                {
                    // The alpha plane: a monochrome image in full range, always lossless
                    var alphaSettings = new Av1EncoderSettings(_width, _height, bitDepth, Monochrome: true, SubsamplingX: 1, SubsamplingY: 1, Av1Constants.CpUnspecified, Av1Constants.TcUnspecified, Av1Constants.McUnspecified, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: true, Quality: 100, _encoder.Effort);
                    _alpha = new Av1Encoder(scope, alphaSettings);
                }

                // Two rows of pixels in the layout of the converter: a chroma sample covers two rows when it is subsampled
                var rowBytes = 2L * _width * PixelFormats.GetBytesPerPixel(_layout);
                if (rowBytes > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(scope.Limits);

                _rows = scope.Rent((int)rowBytes, AllocationKind.Temporary, clear: false);
                _prefix = new PooledPayloadWriter(scope);
            }
            catch
            {
                Dispose(disposing: true);
                throw;
            }
        }

        private enum Step
        {
            None,
            Convert,
            EncodeColor,
            EncodeAlpha,
            Emit,
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("AVIF output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index is not 0)
                throw new InvalidOperationException("A still AVIF image stores a single frame.");

            _frame = frame;
            _row = 0;
            _isTransparent = false;
            _step = Step.Convert;
        }

        public override void BeginComplete(int frameCount)
        {
            // A still image is complete once its frame is written
            _pieces.Clear();
            _pieceIndex = 0;
            _pieceOffset = 0;
            _step = Step.Emit;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Convert:
                    ConvertRows();
                    return false;
                case Step.EncodeColor:
                    if (_color!.Encode(CancellationToken))
                    {
                        if (_alpha is not null && _isTransparent)
                        {
                            _alpha.BeginFrame();
                            _step = Step.EncodeAlpha;
                        }
                        else
                        {
                            Assemble();
                        }
                    }

                    return false;
                case Step.EncodeAlpha:
                    if (_alpha!.Encode(CancellationToken))
                        Assemble();

                    return false;
                case Step.Emit:
                    return Emit(output);
                default:
                    throw new InvalidOperationException("No AVIF operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _color?.Dispose();
                _color = null;
                _alpha?.Dispose();
                _alpha = null;
                _rows?.Dispose();
                _rows = null;
                _prefix?.Dispose();
                _prefix = null;
                _pieces.Clear();
                _frame = null;
            }

            base.Dispose(disposing);
        }

        // 8-bit pixels keep their depth unless more is asked for; 16-bit pixels lose precision, which needs a consent
        private static int ResolveBitDepth(AvifEncoder encoder, PixelFormat format)
        {
            if (encoder.BitDepth is { } bitDepth)
                return bitDepth;

            if (PixelFormats.GetBitsPerComponent(format) <= 8)
                return 8;

            if (!encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"AVIF stores at most 12 bits per sample, so encoding {format} pixels would discard precision. Set AvifEncoder.BitDepth to 10 or 12, set AvifEncoder.AllowBitDepthReduction to reduce the samples to 12 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Avif, "Bit depth reduction");

            return 12;
        }

        // Converts a band of rows of the frame to the planes of the encoders
        private void ConvertRows()
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            CancellationToken.ThrowIfCancellationRequested();
            var color = _color!;
            var luma = color.GetSourcePlane(0);
            var monochrome = color.Sequence.Monochrome;
            var u = monochrome ? null : color.GetSourcePlane(1);
            var v = monochrome ? null : color.GetSourcePlane(2);
            var alpha = _alpha?.GetSourcePlane(0);
            var rowLength = _width * PixelFormats.GetBytesPerPixel(_layout);
            var scratch = _rows!.RawBuffer.AsSpan(0, 2 * rowLength);
            var step = _converter.RowsPerCall;
            var subY = step - 1;
            var end = Math.Min(_height, _row + RowsPerCall);
            using (var lease = frame.GetStorage().AcquireLease())
            {
                for (; _row < end; _row += step)
                {
                    var hasSecondRow = step is 2 && _row + 1 < _height;
                    var row0 = Normalize(lease.GetRowBytes(_row), scratch[..rowLength]);
                    var row1 = hasSecondRow ? Normalize(lease.GetRowBytes(_row + 1), scratch[rowLength..]) : row0;
                    _isTransparent |= _converter.ConvertRows(
                        row0,
                        row1,
                        luma.GetRow(_row),
                        hasSecondRow ? luma.GetRow(_row + 1) : default,
                        u is null ? default : u.GetRow(_row >> subY),
                        v is null ? default : v.GetRow(_row >> subY),
                        alpha is null ? default : alpha.GetRow(_row),
                        alpha is null || !hasSecondRow ? default : alpha.GetRow(_row + 1),
                        hasSecondRow);
                }
            }

            if (_row < _height)
                return;

            _frame = null;
            color.BeginFrame();
            _step = Step.EncodeColor;
        }

        // A row of the frame in the layout of the converter
        private ReadOnlySpan<byte> Normalize(ReadOnlySpan<byte> row, Span<byte> scratch)
        {
            if (_sourceFormat == _layout)
                return row;

            PixelConverter.ConvertRow(_sourceFormat, row, _layout, scratch, background: null, ImageFormat.Avif);
            return scratch;
        }

        // Writes the boxes that precede the coded images, now that their sizes are known
        private void Assemble()
        {
            var color = _color!;
            var alpha = _isTransparent ? _alpha : null;
            var colorPayload = color.Payload;
            var alphaPayload = alpha?.Payload ?? default;
            var prefix = _prefix!;
            prefix.Reset();
            AvifFileWriter.WriteStillImagePrefix(prefix, new AvifFileWriter.StillImage
            {
                Width = _width,
                Height = _height,
                Color = color.Sequence,
                ColorLength = colorPayload.Length,
                Alpha = alpha?.Sequence,
                AlphaLength = alphaPayload.Length,
                ColorDescription = _colorDescription,
                IccProfile = _icc,
                Exif = _exif,
                Xmp = _xmp,
                Orientation = _orientation,
            });

            _pieces.Clear();
            _pieces.Add(prefix.WrittenMemory);
            if (alpha is not null)
                _pieces.Add(alphaPayload);

            _pieces.Add(colorPayload);
            _pieceIndex = 0;
            _pieceOffset = 0;
            _step = Step.Emit;
        }

        // Bounded pieces: the writer flushes between calls
        private bool Emit(ImageOutputBuffer output)
        {
            var budget = EmitPieceLength;
            while (budget > 0 && _pieceIndex < _pieces.Count)
            {
                var piece = _pieces[_pieceIndex].Span[_pieceOffset..];
                var count = Math.Min(budget, piece.Length);
                output.Write(piece[..count]);
                budget -= count;
                _pieceOffset += count;
                if (_pieceOffset == _pieces[_pieceIndex].Length)
                {
                    _pieceIndex++;
                    _pieceOffset = 0;
                }
            }

            if (_pieceIndex < _pieces.Count)
                return false;

            _pieces.Clear();
            _step = Step.None;
            return true;
        }
    }
}
