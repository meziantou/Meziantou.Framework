using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The BMP encoder registration: one still image per output, written by one streaming session.</summary>
internal sealed class BmpEncoderCodec : ImageEncoderCodec
{
    public static BmpEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Bmp;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one BMP output: the file header and the DIB header, then the padded rows from the bottom up in
    /// bands of at most <see cref="RowsPerCall"/> rows. Private state is one converted row band; memory never depends on the
    /// image height.
    /// </summary>
    /// <remarks>
    /// Rows are read from the frame in reverse order, so bottom-up output needs neither seeking nor a copy of the image.
    /// Every byte of padding is written as zero and the declared <c>bfSize</c>/<c>biSizeImage</c> match the bytes produced.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        /// <summary>The largest number of rows encoded by one <see cref="Encode"/> call.</summary>
        internal const int RowsPerCall = 64;

        private readonly PixelFormat _sourceFormat;
        private readonly Rgba64? _background;
        private readonly int _width;
        private readonly int _height;
        private readonly int _bitsPerPixel;
        private readonly int _headerLength;
        private readonly int _rowLength;
        private readonly int _paddingLength;
        private PooledBuffer? _band;
        private ImageFrame? _frame;
        private Step _step;
        private int _rowsWritten;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _sourceFormat = options.PixelFormat;
            var encoder = (BmpEncoder)options.Encoder;
            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"BMP output stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set BmpEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Bmp, "Bit depth reduction");

            var hasAlpha = PixelFormats.HasAlpha(_sourceFormat);
            var layout = encoder.PixelLayout == BmpPixelLayout.Auto ? (hasAlpha ? BmpPixelLayout.Bgra32 : BmpPixelLayout.Bgr24) : encoder.PixelLayout;
            _background = encoder.BackgroundColor;
            PixelConverter.ValidateBackground(_background);
            _bitsPerPixel = layout == BmpPixelLayout.Bgra32 ? 32 : 24;
            _headerLength = BmpFormat.FileHeaderLength + (layout == BmpPixelLayout.Bgra32 ? BmpFormat.V4HeaderLength : BmpFormat.InfoHeaderLength);
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;

            var rowLength = BmpFormat.GetRowLength(_width, _bitsPerPixel);
            var totalLength = _headerLength + (rowLength * _height);
            if (totalLength > uint.MaxValue)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The BMP file would be {totalLength} bytes, more than the 4,294,967,295 bytes the bfSize field can declare; the canvas is {_width}x{_height}."), ImageFormat.Bmp, "Canvas size");

            _rowLength = (int)rowLength;
            _paddingLength = _rowLength - (_width * (_bitsPerPixel / 8));

            // One band of RGB pixels for the 24-bit layout (32-bit rows are converted straight into the output buffer)
            if (_bitsPerPixel == 24)
            {
                _band = options.Scope.Rent(_width * 3, AllocationKind.Temporary, clear: false);
            }
        }

        private enum Step
        {
            None,
            Header,
            Pixels,
            Complete,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // Alpha is never discarded silently: without a background every pixel must be opaque
            if (_bitsPerPixel == 32 || !PixelFormats.HasAlpha(_sourceFormat) || _background is not null)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
                if (x >= 0)
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The selected BMP layout Bgr24 has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Set BmpEncoder.BackgroundColor to composite the pixels over an opaque color, select BmpPixelLayout.Bgra32, or flatten the image explicitly."), ImageFormat.Bmp, "Alpha removal");
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("BMP output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A BMP image stores a single frame.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.Complete;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    WriteHeaders(output);
                    _step = Step.Pixels;
                    return false;

                case Step.Pixels:
                    EncodeBand(output);
                    if (_rowsWritten < _height)
                        return false;

                    _frame = null;
                    _step = Step.None;
                    return true;

                case Step.Complete:
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No BMP operation was begun.");
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

        private void WriteHeaders(ImageOutputBuffer output)
        {
            var span = output.GetSpan(_headerLength);
            span[.._headerLength].Clear();
            var fileLength = (uint)(_headerLength + ((long)_rowLength * _height));
            BmpFormat.Magic.CopyTo(span);
            BinaryPrimitives.WriteUInt32LittleEndian(span[2..], fileLength);
            BinaryPrimitives.WriteUInt32LittleEndian(span[10..], (uint)_headerLength);

            var dib = span[BmpFormat.FileHeaderLength..];
            var dibLength = _headerLength - BmpFormat.FileHeaderLength;
            BinaryPrimitives.WriteUInt32LittleEndian(dib, (uint)dibLength);
            BinaryPrimitives.WriteInt32LittleEndian(dib[4..], _width);
            BinaryPrimitives.WriteInt32LittleEndian(dib[8..], _height);
            BinaryPrimitives.WriteUInt16LittleEndian(dib[12..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(dib[14..], (ushort)_bitsPerPixel);
            BinaryPrimitives.WriteUInt32LittleEndian(dib[16..], (uint)(_bitsPerPixel == 32 ? BmpCompression.BitFields : BmpCompression.Rgb));
            BinaryPrimitives.WriteUInt32LittleEndian(dib[20..], (uint)((long)_rowLength * _height));
            if (Options.Metadata.Resolution is { } resolution)
            {
                var (x, y) = ResolutionConversion.ToBmpPixelsPerMeter(resolution);
                BinaryPrimitives.WriteInt32LittleEndian(dib[24..], x);
                BinaryPrimitives.WriteInt32LittleEndian(dib[28..], y);
            }

            if (_bitsPerPixel == 32)
            {
                // BITMAPV4HEADER: explicit BGRA masks and the sRGB color space; the endpoints and gamma fields stay zero
                BinaryPrimitives.WriteUInt32LittleEndian(dib[40..], 0x00FF_0000);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[44..], 0x0000_FF00);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[48..], 0x0000_00FF);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[52..], 0xFF00_0000);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[56..], BmpFormat.ColorSpaceSrgb);
            }

            output.Advance(_headerLength);
        }

        private void EncodeBand(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_sourceFormat);
            var samples = _width * (_bitsPerPixel / 8);
            using var lease = frame.GetStorage().AcquireLease();
            var rows = Math.Min(RowsPerCall, _height - _rowsWritten);
            for (var i = 0; i < rows; i++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var source = lease.GetRowBytes(_height - 1 - _rowsWritten)[..(_width * bytesPerPixel)];
                var destination = output.GetSpan(_rowLength);
                if (_bitsPerPixel == 32)
                {
                    PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Bgra32, destination[..samples], background: null, ImageFormat.Bmp);
                }
                else
                {
                    var band = _band!.Span[..(_width * 3)];
                    PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Rgb24, band, _background, ImageFormat.Bmp);
                    for (var x = 0; x < _width; x++)
                    {
                        var offset = x * 3;
                        destination[offset] = band[offset + 2];
                        destination[offset + 1] = band[offset + 1];
                        destination[offset + 2] = band[offset];
                    }
                }

                destination.Slice(samples, _paddingLength).Clear();
                output.Advance(_rowLength);
                _rowsWritten++;
            }
        }
    }
}
