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
            _background = encoder.BackgroundColor;
            PixelConverter.ValidateBackground(_background);
            _is16Bit = PixelFormats.GetBitsPerComponent(_sourceFormat) > 8;
            _grayscale = PixelFormats.IsGrayscale(_sourceFormat);
            _writeAlpha = PixelFormats.HasAlpha(_sourceFormat) && !_plain;
            _channels = _grayscale ? 1 : (_writeAlpha ? 4 : 3);
            _flattenFromRgba64 = _plain && _is16Bit && !_grayscale;
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
            if (_writeAlpha || !PixelFormats.HasAlpha(_sourceFormat) || _background is not null)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
                if (x >= 0)
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"Plain Netpbm output has no alpha channel (PAM has no plain form) and the pixel ({x}, {y}) is not fully opaque. Set PnmEncoder.BackgroundColor to composite the pixels over an opaque color, keep PnmEncoding.Binary, or flatten the image explicitly."), ImageFormat.Pnm, "Alpha removal");
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
            var header = _writeAlpha
                ? string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {_width}\nHEIGHT {_height}\nDEPTH 4\nMAXVAL {MaxValue}\nTUPLTYPE RGB_ALPHA\nENDHDR\n")
                : string.Create(CultureInfo.InvariantCulture, $"P{GetMagicDigit()}\n{_width} {_height}\n{MaxValue}\n");
            var span = output.GetSpan(Encoding.ASCII.GetByteCount(header));
            output.Advance(Encoding.ASCII.GetBytes(header, span));
        }

        private int GetMagicDigit() => (_grayscale, _plain) switch
        {
            (true, false) => 5,
            (true, true) => 2,
            (false, false) => 6,
            _ => 3,
        };

        private void EncodeBand(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_sourceFormat);
            using var lease = frame.GetStorage().AcquireLease();
            var rows = Math.Min(RowsPerCall, _height - _rowsWritten);
            for (var i = 0; i < rows; i++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var source = lease.GetRowBytes(_rowsWritten)[..(_width * bytesPerPixel)];
                var band = _band!.Span[..(_width * PixelFormats.GetBytesPerPixel(_workFormat))];
                PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, band, _flattenFromRgba64 ? null : _background, ImageFormat.Pnm);
                if (_plain)
                {
                    WritePlainRow(output, band);
                }
                else
                {
                    WriteBinaryRow(output, band);
                }

                _rowsWritten++;
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

        /// <summary>Reads one plain sample, flattening 16-bit alpha over the background when the plain form cannot store it.</summary>
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
