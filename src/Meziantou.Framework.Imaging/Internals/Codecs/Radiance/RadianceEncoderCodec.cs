using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Radiance HDR encoder registration: one RGBE picture per output, in the standard orientation.</summary>
internal sealed class RadianceEncoderCodec : ImageEncoderCodec
{
    public static RadianceEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Radiance;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>
    /// The encoding state of one Radiance HDR output: the information header and the resolution string, then one scanline
    /// (a row, from the top) per <see cref="Encode"/> call.
    /// </summary>
    /// <remarks>
    /// The rows are converted to <see cref="PixelFormat.RgbaFloat"/> (read in place for <see cref="RgbaFloat"/> frames),
    /// composited over the background when they are not opaque, converted to RGBE pixels
    /// (<see cref="RadianceFormat.FromColor"/>), then written flat or with the new run-length encoding
    /// (<see cref="WriteEncoded"/>). <see cref="ValidateFrame"/> checks every pixel first, so an error is reported before
    /// anything is written. The private state is one row of floats, one row of RGBE pixels and the encoded scanline: memory
    /// does not depend on the height.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _encoded;
        private readonly byte _primaries;
        private readonly Rgba64? _background;
        private readonly PixelConversionPlan? _plan;
        private PooledBuffer? _floats;
        private PooledBuffer? _pixels;
        private ImageFrame? _frame;
        private Step _step;
        private int _y;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            var encoder = (RadianceEncoder)options.Encoder;
            var source = options.PixelFormat;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _encoded = encoder.Compression == RadianceCompression.RunLength && _width is >= RadianceFormat.MinEncodedLength and <= RadianceFormat.MaxEncodedLength;
            _primaries = options.Metadata.Cicp?.ColorPrimaries ?? 1;
            _background = PixelFormats.HasAlpha(source) ? encoder.BackgroundColor : null;
            if ((long)_width * 16 > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            if (source != PixelFormat.RgbaFloat)
            {
                _plan = PixelConversionPlan.Create(source, PixelFormat.RgbaFloat, format: ImageFormat.Radiance);
            }

            // Every buffer is rented before any output
            var scope = options.Scope;
            try
            {
                _floats = scope.Rent(_width * 16, AllocationKind.Temporary, clear: false);
                _pixels = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
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
            Header,
            Scanlines,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                GetColors(lease.GetRowBytes(y), y);
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("Radiance HDR output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            if (index != 0)
                throw new InvalidOperationException("A Radiance HDR picture stores a single frame.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.None;

        public override bool Encode(ImageOutputBuffer output)
        {
            switch (_step)
            {
                case Step.Header:
                    output.Write(CreateHeader());
                    _step = Step.Scanlines;
                    return false;

                case Step.Scanlines:
                {
                    var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
                    var pixels = _pixels!.RawBuffer.AsSpan(0, _width * 4);
                    using (var lease = frame.GetStorage().AcquireLease())
                    {
                        var colors = GetColors(lease.GetRowBytes(_y), _y);
                        for (var x = 0; x < _width; x++)
                        {
                            var pixel = RadianceFormat.FromColor(colors[x * 4], colors[(x * 4) + 1], colors[(x * 4) + 2]);
                            pixels[x * 4] = (byte)pixel;
                            pixels[(x * 4) + 1] = (byte)(pixel >> 8);
                            pixels[(x * 4) + 2] = (byte)(pixel >> 16);
                            pixels[(x * 4) + 3] = (byte)(pixel >> 24);
                        }
                    }

                    if (_encoded)
                    {
                        var destination = output.GetSpan(GetMaxEncodedLength(_width));
                        output.Advance(WriteEncoded(pixels, destination));
                    }
                    else
                    {
                        output.Write(pixels);
                    }

                    _y++;
                    if (_y < _height)
                        return false;

                    _frame = null;
                    _step = Step.None;
                    return true;
                }

                case Step.None:
                    return true;

                default:
                    throw new InvalidOperationException("No Radiance HDR operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _floats?.Dispose();
                _floats = null;
                _pixels?.Dispose();
                _pixels = null;
                _frame = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Gets the length of the longest encoded scanline: its marker, then for each component one code per literal of
        /// at most 128 bytes, and the bytes.
        /// </summary>
        internal static int GetMaxEncodedLength(int width) => 4 + (4 * (width + ((width + RadianceFormat.MaxLiteral - 1) / RadianceFormat.MaxLiteral)));

        /// <summary>
        /// Writes a scanline with the new run-length encoding: the marker (2, 2 and the 15-bit length), then the red, green,
        /// blue and exponent bytes of the pixels, each component as runs of at least <see cref="RadianceFormat.MinRun"/>
        /// equal bytes (codes 129 to 255) and literals of up to 128 bytes between them (codes 1 to 128).
        /// </summary>
        /// <returns>The number of bytes written.</returns>
        internal static int WriteEncoded(ReadOnlySpan<byte> pixels, Span<byte> destination)
        {
            var width = pixels.Length / 4;
            destination[0] = 2;
            destination[1] = 2;
            destination[2] = (byte)(width >> 8);
            destination[3] = (byte)width;
            var o = 4;
            for (var component = 0; component < 4; component++)
            {
                var i = 0;
                while (i < width)
                {
                    var run = GetRunLength(pixels, component, i, width);
                    if (run >= RadianceFormat.MinRun)
                    {
                        // A run longer than one code continues with the next codes; their tail may be short, any run is valid
                        while (run > 0)
                        {
                            var count = Math.Min(run, RadianceFormat.MaxRun);
                            destination[o] = (byte)(RadianceFormat.MaxLiteral + count);
                            destination[o + 1] = pixels[(i * 4) + component];
                            o += 2;
                            i += count;
                            run -= count;
                        }

                        continue;
                    }

                    // A literal up to the next run that is worth a code, or of 128 bytes
                    var start = i;
                    while (i < width && i - start < RadianceFormat.MaxLiteral && GetRunLength(pixels, component, i, Math.Min(width, i + RadianceFormat.MinRun)) < RadianceFormat.MinRun)
                    {
                        i++;
                    }

                    destination[o++] = (byte)(i - start);
                    for (var k = start; k < i; k++)
                    {
                        destination[o++] = pixels[(k * 4) + component];
                    }
                }
            }

            return o;
        }

        /// <summary>Gets the number of pixels from <paramref name="start"/> to <paramref name="end"/> (excluded) whose component equals the one of the first.</summary>
        private static int GetRunLength(ReadOnlySpan<byte> pixels, int component, int start, int end)
        {
            var value = pixels[(start * 4) + component];
            var i = start + 1;
            while (i < end && pixels[(i * 4) + component] == value)
            {
                i++;
            }

            return i - start;
        }

        private byte[] CreateHeader()
        {
            var chromaticities = RadianceColorDescription.GetChromaticities(_primaries);
            var builder = new StringBuilder();
            builder.Append("#?RADIANCE\n");
            builder.Append("FORMAT=").Append(RadianceFormat.RgbeFormat).Append('\n');
            builder.Append("PRIMARIES=");
            foreach (var value in chromaticities)
            {
                builder.Append(' ').Append(value.ToString("0.0000", CultureInfo.InvariantCulture));
            }

            builder.Append("\n\n");
            builder.Append(CultureInfo.InvariantCulture, $"-Y {_height} +X {_width}\n");
            return Encoding.ASCII.GetBytes(builder.ToString());
        }

        /// <summary>
        /// Gets the colors of a row as <see cref="PixelFormat.RgbaFloat"/> samples, composited over the background when a
        /// pixel is not opaque, and checks that each color can be stored.
        /// </summary>
        /// <exception cref="UnsupportedImageFeatureException">A pixel is not opaque and there is no background, or a color cannot be stored.</exception>
        private Span<float> GetColors(Span<byte> source, int y)
        {
            Span<float> colors;
            if (_plan is null)
            {
                colors = unsafe(MemoryMarshal.Cast<byte, float>(_floats!.RawBuffer.AsSpan(0, _width * 16)));
                var stored = unsafe(MemoryMarshal.Cast<byte, float>(source[..(_width * 16)]));
                stored.CopyTo(colors);
            }
            else
            {
                var row = _floats!.RawBuffer.AsSpan(0, _width * 16);
                _plan.ConvertRow(source[..(_width * PixelFormats.GetBytesPerPixel(_plan.SourceFormat))], row);
                colors = unsafe(MemoryMarshal.Cast<byte, float>(row));
            }

            for (var x = 0; x < _width; x++)
            {
                var pixel = colors.Slice(x * 4, 4);
                var alpha = pixel[3];
                if (alpha != 1f)
                {
                    if (_background is not { } background)
                        throw RadianceFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"Radiance HDR has no alpha channel and the pixel ({x}, {y}) is not fully opaque (alpha {alpha}). Set RadianceEncoder.BackgroundColor to composite the pixels over an opaque color, or flatten the image explicitly."), "Alpha removal");

                    var a = Math.Clamp((double)alpha, 0, 1);
                    pixel[0] = (float)((a * pixel[0]) + ((1 - a) * (background.R / 65535.0)));
                    pixel[1] = (float)((a * pixel[1]) + ((1 - a) * (background.G / 65535.0)));
                    pixel[2] = (float)((a * pixel[2]) + ((1 - a) * (background.B / 65535.0)));
                    pixel[3] = 1f;
                }

                for (var c = 0; c < 3; c++)
                {
                    if (!RadianceFormat.CanStore(pixel[c]))
                        throw RadianceFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The color {pixel[c]} of the pixel ({x}, {y}) cannot be stored: Radiance HDR stores colors from 0 to 2^127, finite and not negative. Clamp the colors of the image first."), "Colors outside the range of RGBE");
                }
            }

            return colors;
        }
    }
}
