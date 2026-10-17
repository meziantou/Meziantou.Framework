using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Netpbm encoder registration: one still image per output, written by one streaming session.</summary>
internal sealed class PnmEncoderCodec : ImageEncoderCodec
{
    public static PnmEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Pnm;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one Netpbm output: the header, then the rows top to bottom in bands of at most
    /// <see cref="RowsPerCall"/> rows. Private state is one converted row; memory never depends on the image height.
    /// </summary>
    private sealed class Session : ImageEncoderSession
    {
        /// <summary>The largest number of rows encoded by one <see cref="Encode"/> call.</summary>
        internal const int RowsPerCall = 64;

        /// <summary>The largest length of a line of a plain raster, as the Netpbm formats require.</summary>
        internal const int MaxPlainLineLength = 70;

        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _workFormat;
        private readonly Rgba64? _background;
        private readonly bool _plain;
        private readonly bool _bitmap;
        private readonly bool _is16Bit;
        private readonly bool _grayscale;
        private readonly bool _writeAlpha;
        private readonly bool _flattenFromRgba64;
        private readonly int _channels;
        private readonly int _width;
        private readonly int _height;
        private PooledBuffer? _band;
        private ImageFrame? _frame;
        private Step _step;
        private int _rowsWritten;
        private int _lineLength;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _sourceFormat = options.PixelFormat;
            var encoder = (PnmEncoder)options.Encoder;
            _plain = encoder.Encoding == PnmEncoding.Plain;
            _bitmap = encoder.PixelLayout == PnmPixelLayout.Bitmap;
            _background = encoder.BackgroundColor;
            PixelConverter.ValidateBackground(_background);
            _is16Bit = PixelFormats.GetBitsPerComponent(_sourceFormat) > 8;
            _grayscale = PixelFormats.IsGrayscale(_sourceFormat);
            _writeAlpha = PixelFormats.HasAlpha(_sourceFormat) && !_plain && !_bitmap;
            _channels = _grayscale ? 1 : (_writeAlpha ? 4 : 3);
            _flattenFromRgba64 = (_plain || _bitmap) && _is16Bit && !_grayscale;
            _workFormat = (_grayscale, _is16Bit, _writeAlpha) switch
            {
                (true, false, _) => PixelFormat.Gray8,
                (true, true, _) => PixelFormat.Gray16,
                (false, false, true) => PixelFormat.Rgba32,
                (false, false, false) => PixelFormat.Rgb24,
                _ => PixelFormat.Rgba64,
            };

            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;

            // One converted row, allocated before any output
            _band = options.Scope.Rent(_width * PixelFormats.GetBytesPerPixel(_workFormat), AllocationKind.Temporary, clear: false);
        }

        private enum Step
        {
            None,
            Header,
            Pixels,
            Complete,
        }

        private int MaxValue => _is16Bit ? 65535 : 255;

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // Alpha is never discarded silently: without a background every pixel must be opaque
            var rejectsAlpha = !_writeAlpha && PixelFormats.HasAlpha(_sourceFormat) && _background is null;
            if (!rejectsAlpha && !_bitmap)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            if (rejectsAlpha)
            {
                for (var y = 0; y < lease.Height; y++)
                {
                    var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
                    if (x < 0)
                        continue;

                    throw new UnsupportedImageFeatureException(_bitmap
                        ? string.Create(CultureInfo.InvariantCulture, $"A portable bitmap has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Set PnmEncoder.BackgroundColor to composite the pixels over an opaque color, keep PnmPixelLayout.Auto, or flatten the image explicitly.")
                        : string.Create(CultureInfo.InvariantCulture, $"Plain Netpbm output has no alpha channel (PAM has no plain form) and the pixel ({x}, {y}) is not fully opaque. Set PnmEncoder.BackgroundColor to composite the pixels over an opaque color, keep PnmEncoding.Binary, or flatten the image explicitly."), ImageFormat.Pnm, "Alpha removal");
                }
            }

            if (!_bitmap)
                return;

            // Gray levels and colors are never thresholded silently: every pixel must be exactly black or white
            for (var y = 0; y < lease.Height; y++)
            {
                var band = ConvertRow(lease.GetRowBytes(y));
                for (var x = 0; x < _width; x++)
                {
                    if (!IsBlackOrWhite(band, x))
                        throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"A portable bitmap stores black and white pixels only and the pixel ({x}, {y}) is neither. Keep PnmPixelLayout.Auto, or reduce the image to black and white explicitly."), ImageFormat.Pnm, "Bilevel reduction");
                }
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("Netpbm output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A Netpbm image stores a single frame.");

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
                    WriteHeader(output);
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
                    throw new InvalidOperationException("No Netpbm operation was begun.");
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

        private void WriteHeader(ImageOutputBuffer output)
        {
            // A portable bitmap is the only variant without MAXVAL
            var header = (_writeAlpha, _bitmap) switch
            {
                (true, _) => string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {_width}\nHEIGHT {_height}\nDEPTH 4\nMAXVAL {MaxValue}\nTUPLTYPE RGB_ALPHA\nENDHDR\n"),
                (false, true) => string.Create(CultureInfo.InvariantCulture, $"P{GetMagicDigit()}\n{_width} {_height}\n"),
                _ => string.Create(CultureInfo.InvariantCulture, $"P{GetMagicDigit()}\n{_width} {_height}\n{MaxValue}\n"),
            };

            var span = output.GetSpan(Encoding.ASCII.GetByteCount(header));
            output.Advance(Encoding.ASCII.GetBytes(header, span));
        }

        private int GetMagicDigit() => (_bitmap, _grayscale, _plain) switch
        {
            (true, _, false) => 4,
            (true, _, true) => 1,
            (false, true, false) => 5,
            (false, true, true) => 2,
            (false, false, false) => 6,
            _ => 3,
        };

        private void EncodeBand(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            using var lease = frame.GetStorage().AcquireLease();
            var rows = Math.Min(RowsPerCall, _height - _rowsWritten);
            for (var i = 0; i < rows; i++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var band = ConvertRow(lease.GetRowBytes(_rowsWritten));
                switch (_bitmap, _plain)
                {
                    case (true, false):
                        WriteBinaryBitmapRow(output, band);
                        break;
                    case (true, true):
                        WritePlainBitmapRow(output, band);
                        break;
                    case (false, true):
                        WritePlainRow(output, band);
                        break;
                    default:
                        WriteBinaryRow(output, band);
                        break;
                }

                _rowsWritten++;
            }
        }

        /// <summary>Converts one source row to the work format, in the row buffer of the session.</summary>
        private ReadOnlySpan<byte> ConvertRow(ReadOnlySpan<byte> row)
        {
            var source = row[..(_width * PixelFormats.GetBytesPerPixel(_sourceFormat))];
            var band = _band!.Span[..(_width * PixelFormats.GetBytesPerPixel(_workFormat))];
            PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, band, _flattenFromRgba64 ? null : _background, ImageFormat.Pnm);
            return band;
        }

        /// <summary>
        /// Gets a value indicating whether a pixel is exactly black or exactly white once alpha is removed. The test runs on
        /// the samples at their own precision, never on a luma: rounding would accept colors that are only nearly black.
        /// </summary>
        private bool IsBlackOrWhite(ReadOnlySpan<byte> band, int x)
        {
            var value = ReadPlainSample(band, x, channel: 0);
            if (value != 0 && value != MaxValue)
                return false;

            return _grayscale || (ReadPlainSample(band, x, channel: 1) == value && ReadPlainSample(band, x, channel: 2) == value);
        }

        /// <summary>Writes one row of a binary portable bitmap: one bit per pixel, most significant bit first, a set bit is black.</summary>
        private void WriteBinaryBitmapRow(ImageOutputBuffer output, ReadOnlySpan<byte> band)
        {
            var length = (_width + 7) / 8;
            var destination = output.GetSpan(length);
            for (var i = 0; i < length; i++)
            {
                // The bits past the width are padding and stay clear
                var bits = 0;
                var end = Math.Min(_width, (i + 1) * 8);
                for (var x = i * 8; x < end; x++)
                {
                    if (ReadPlainSample(band, x, channel: 0) == 0)
                    {
                        bits |= 0x80 >> (x & 7);
                    }
                }

                destination[i] = (byte)bits;
            }

            output.Advance(length);
        }

        /// <summary>Writes one row of a plain portable bitmap: one digit per pixel (1 is black), in lines of at most <see cref="MaxPlainLineLength"/> characters.</summary>
        private void WritePlainBitmapRow(ImageOutputBuffer output, ReadOnlySpan<byte> band)
        {
            for (var start = 0; start < _width; start += MaxPlainLineLength)
            {
                var count = Math.Min(MaxPlainLineLength, _width - start);
                var destination = output.GetSpan(count + 1);
                for (var i = 0; i < count; i++)
                {
                    destination[i] = ReadPlainSample(band, start + i, channel: 0) == 0 ? (byte)'1' : (byte)'0';
                }

                destination[count] = (byte)'\n';
                output.Advance(count + 1);
            }
        }

        private void WriteBinaryRow(ImageOutputBuffer output, ReadOnlySpan<byte> band)
        {
            var sampleCount = _width * _channels;
            if (!_is16Bit)
            {
                // Rgba32 and Rgb24 bands already hold the samples in RGB(A) order
                output.Write(band[..sampleCount]);
                return;
            }

            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(band));
            var destination = output.GetSpan(sampleCount * 2);
            for (var i = 0; i < sampleCount; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination[(i * 2)..], samples[i]);
            }

            output.Advance(sampleCount * 2);
        }

        private void WritePlainRow(ImageOutputBuffer output, ReadOnlySpan<byte> band)
        {
            // At most 5 digits and one separator per sample, plus the final line feed
            var destination = output.GetSpan((_width * _channels * 6) + 1);
            var written = 0;
            for (var x = 0; x < _width; x++)
            {
                for (var channel = 0; channel < _channels; channel++)
                {
                    var value = ReadPlainSample(band, x, channel);
                    var digits = value switch
                    {
                        < 10 => 1,
                        < 100 => 2,
                        < 1000 => 3,
                        < 10000 => 4,
                        _ => 5,
                    };

                    if (_lineLength > 0 && _lineLength + 1 + digits > MaxPlainLineLength)
                    {
                        destination[written++] = (byte)'\n';
                        _lineLength = 0;
                    }
                    else if (_lineLength > 0)
                    {
                        destination[written++] = (byte)' ';
                        _lineLength++;
                    }

                    value.TryFormat(destination[written..], out var count, provider: CultureInfo.InvariantCulture);
                    written += count;
                    _lineLength += digits;
                }
            }

            destination[written++] = (byte)'\n';
            _lineLength = 0;
            output.Advance(written);
        }

        /// <summary>Reads one sample of an output without alpha, flattening 16-bit alpha over the background.</summary>
        private int ReadPlainSample(ReadOnlySpan<byte> band, int x, int channel)
        {
            if (!_is16Bit)
                return band[(x * _channels) + channel];

            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(band));
            if (!_flattenFromRgba64)
                return samples[(x * _channels) + channel];

            var offset = x * 4;
            var alpha = samples[offset + 3];
            if (alpha == ushort.MaxValue)
                return samples[offset + channel];

            var background = _background!.Value;
            var backgroundSample = channel switch
            {
                0 => background.R,
                1 => background.G,
                _ => background.B,
            };

            return (int)PixelConverter.Flatten(samples[offset + channel], backgroundSample, alpha, ushort.MaxValue);
        }
    }
}
