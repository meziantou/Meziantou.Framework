using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The TGA encoder registration: one still image per output, written by one streaming session.</summary>
internal sealed class TgaEncoderCodec : ImageEncoderCodec
{
    public static TgaEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Tga;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one TGA output: the 18-byte header, then the rows from the bottom up (the origin the header
    /// declares), raw or run-length encoded, then the TGA 2.0 footer. Private state is one converted row and one row of
    /// stored samples; memory never depends on the image height.
    /// </summary>
    private sealed class Session : ImageEncoderSession
    {
        /// <summary>The largest number of rows encoded by one <see cref="Encode"/> call.</summary>
        internal const int RowsPerCall = 64;

        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _workFormat;
        private readonly bool _runLength;
        private readonly bool _grayscale;
        private readonly int _width;
        private readonly int _height;
        private readonly int _bytesPerStoredPixel;
        private PooledBuffer? _band;
        private ImageFrame? _frame;
        private Step _step;
        private int _rowsWritten;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _sourceFormat = options.PixelFormat;
            var encoder = (TgaEncoder)options.Encoder;
            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"TGA output stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set TgaEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Tga, "Bit depth reduction");

            _runLength = encoder.Compression == TgaCompression.RunLength;
            _grayscale = PixelFormats.IsGrayscale(_sourceFormat);
            _workFormat = _grayscale ? PixelFormat.Gray8 : (PixelFormats.HasAlpha(_sourceFormat) ? PixelFormat.Bgra32 : PixelFormat.Rgb24);
            _bytesPerStoredPixel = PixelFormats.GetBytesPerPixel(_workFormat);
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;

            // One converted row, allocated before any output (the 24-bit layout swaps red and blue in place)
            _band = options.Scope.Rent(_width * _bytesPerStoredPixel, AllocationKind.Temporary, clear: false);
        }

        private enum Step
        {
            None,
            Header,
            Pixels,
            Footer,
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("TGA output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A TGA image stores a single frame.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.Footer;

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

                case Step.Footer:
                    WriteFooter(output);
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No TGA operation was begun.");
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
            var header = output.GetSpan(TgaFormat.HeaderLength);
            header[..TgaFormat.HeaderLength].Clear();
            var imageType = _grayscale
                ? (_runLength ? TgaImageType.RleGrayscale : TgaImageType.Grayscale)
                : (_runLength ? TgaImageType.RleTrueColor : TgaImageType.TrueColor);
            header[2] = (byte)imageType;
            BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)_width);
            BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)_height);
            header[16] = (byte)(_bytesPerStoredPixel * 8);

            // Origin at the bottom left (descriptor bit 5 clear), with the declared alpha-bit count of a 32-bit payload
            header[17] = _workFormat == PixelFormat.Bgra32 ? (byte)8 : (byte)0;
            output.Advance(TgaFormat.HeaderLength);
        }

        private static void WriteFooter(ImageOutputBuffer output)
        {
            var footer = output.GetSpan(TgaFormat.FooterLength);
            BinaryPrimitives.WriteUInt32LittleEndian(footer, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], 0);
            TgaFormat.FooterSignature.CopyTo(footer[8..]);
            output.Advance(TgaFormat.FooterLength);
        }

        private void EncodeBand(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_sourceFormat);
            var storedRowLength = _width * _bytesPerStoredPixel;
            using var lease = frame.GetStorage().AcquireLease();
            var rows = Math.Min(RowsPerCall, _height - _rowsWritten);
            if (!_runLength)
            {
                // The output grows once for the whole band, instead of doubling (and copying) as the rows are appended
                output.GetSpan(rows * storedRowLength);
            }

            for (var i = 0; i < rows; i++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var source = lease.GetRowBytes(_height - 1 - _rowsWritten)[..(_width * bytesPerPixel)];
                if (_runLength)
                {
                    // At most one packet header per 128 pixels, plus the samples
                    var stored = ConvertRow(source, _band!.Span[..storedRowLength]);
                    var destination = output.GetSpan(storedRowLength + (((_width + TgaFormat.MaxPacketLength - 1) / TgaFormat.MaxPacketLength) * 2));
                    output.Advance(EncodeRunLengthRow(stored, destination));
                }
                else
                {
                    // Raw rows are stored straight into the output
                    ConvertRow(source, output.GetSpan(storedRowLength)[..storedRowLength]);
                    output.Advance(storedRowLength);
                }

                _rowsWritten++;
            }
        }

        /// <summary>Converts one source row to the stored samples (blue, green and red for 24-bit pixels) in <paramref name="stored"/>.</summary>
        private ReadOnlySpan<byte> ConvertRow(ReadOnlySpan<byte> source, Span<byte> stored)
        {
            if (_workFormat == PixelFormat.Bgra32 && _sourceFormat == PixelFormat.Rgba32)
            {
                PixelConverter.SwapRedAndBlue32(source, stored);
                return stored;
            }

            if (_workFormat != PixelFormat.Rgb24)
            {
                PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, stored, background: null, ImageFormat.Tga);
                return stored;
            }

            // TGA stores blue, green and red: RGB rows are swapped while they are copied (never in place, which stalls the
            // overlapping vector loads on the stores before them)
            if (_sourceFormat != PixelFormat.Rgb24)
            {
                var band = _band!.Span[..stored.Length];
                PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Rgb24, band, background: null, ImageFormat.Tga);
                source = band;
                if (source.Overlaps(stored))
                {
                    PixelConverter.SwapRedAndBlue24(band, band);
                    return band;
                }
            }

            PixelConverter.SwapRedAndBlue24(source, stored);
            return stored;
        }

        /// <summary>Encodes one row as run-length and raw packets; packets never cross the scan line.</summary>
        private int EncodeRunLengthRow(ReadOnlySpan<byte> stored, Span<byte> destination) => _bytesPerStoredPixel switch
        {
            1 => EncodeRunLengthRow<byte>(stored, destination),
            3 => EncodeRunLengthRow(unsafe(MemoryMarshal.Cast<byte, StoredPixel24>(stored)), destination),
            _ => EncodeRunLengthRow(unsafe(MemoryMarshal.Cast<byte, uint>(stored)), destination),
        };

        /// <summary>Encodes one row of pixels (compared as values) as run-length and raw packets.</summary>
        private static int EncodeRunLengthRow<TPixel>(ReadOnlySpan<TPixel> pixels, Span<byte> destination)
            where TPixel : unmanaged, IEquatable<TPixel>
        {
            var bytes = Unsafe.SizeOf<TPixel>();
            var width = pixels.Length;
            var written = 0;
            var x = 0;
            while (x < width)
            {
                var pixel = pixels[x];
                var run = 1;
                while (x + run < width && run < TgaFormat.MaxPacketLength && pixels[x + run].Equals(pixel))
                {
                    run++;
                }

                if (run >= 2)
                {
                    destination[written++] = (byte)(TgaFormat.RunLengthPacketFlag | (run - 1));
                    unsafe { MemoryMarshal.AsBytes(pixels.Slice(x, 1)).CopyTo(destination[written..]); }
                    written += bytes;
                    x += run;
                    continue;
                }

                // Raw packet: stop before the next run of two or more identical pixels
                var count = 1;
                while (x + count < width && count < TgaFormat.MaxPacketLength)
                {
                    if (x + count + 1 < width && pixels[x + count].Equals(pixels[x + count + 1]))
                        break;

                    count++;
                }

                destination[written++] = (byte)(count - 1);
                unsafe { MemoryMarshal.AsBytes(pixels.Slice(x, count)).CopyTo(destination[written..]); }
                written += count * bytes;
                x += count;
            }

            return written;
        }

        /// <summary>A stored 24-bit pixel, compared as a value.</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct StoredPixel24 : IEquatable<StoredPixel24>
        {
            private readonly byte _first;
            private readonly byte _second;
            private readonly byte _third;

            public bool Equals(StoredPixel24 other) => _first == other._first && _second == other._second && _third == other._third;

            public override bool Equals([NotNullWhen(true)] object? obj) => obj is StoredPixel24 other && Equals(other);

            public override int GetHashCode() => _first | (_second << 8) | (_third << 16);
        }
    }
}
