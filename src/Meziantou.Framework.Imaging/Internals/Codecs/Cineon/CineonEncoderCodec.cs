using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The Kodak Cineon encoder registration: one big-endian file of pixel-interleaved channels, packing 5.</summary>
internal sealed class CineonEncoderCodec : ImageEncoderCodec
{
    /// <summary>The version the encoder writes in the generic section (field 1.7).</summary>
    public const string Version = "V4.5";

    /// <summary>The packing the encoder writes (field 1.29): 32-bit cells, left-justified.</summary>
    public const byte Packing = 5;

    /// <summary>The largest printing density of the default quantities: 0.002 per 10-bit code value.</summary>
    public const float DefaultMaximumDensity = 2.048f;

    public static CineonEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Cineon;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>Gets the number of bytes of a stored line: its samples packed in 32-bit words.</summary>
    internal static long GetLineLength(int width, int channels, int bits) => bits switch
    {
        // One word per pixel of three channels; three samples per word for one channel
        10 => channels == 1 ? ((long)width + 2) / 3 * 4 : (long)width * 4,
        _ => (((long)width * channels * bits / 8) + 3) / 4 * 4,
    };

    /// <summary>The encoding state of one Cineon output: the header, then one line (a row, from the top) per <see cref="Encode"/> call.</summary>
    private sealed class Session : ImageEncoderSession
    {
        private readonly ComponentRowReader _reader;
        private readonly int _width;
        private readonly int _height;
        private readonly int _channels;
        private readonly int _bits;
        private readonly long _lineLength;
        private readonly byte[] _header;
        private PooledBuffer? _components;
        private PooledBuffer? _line;
        private ImageFrame? _frame;
        private Step _step;
        private int _y;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            var encoder = (CineonEncoder)options.Encoder;
            _reader = new ComponentRowReader(options.PixelFormat);
            if (_reader.ColorComponents == 4)
                throw CineonFormat.Unsupported("Cineon stores no CMYK samples.", "CMYK samples");

            if (_reader.IsFloatingPoint)
                throw new InvalidOperationException("Floating-point pixels reach the Cineon encoder reduced to integers.");

            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _channels = _reader.ColorComponents;
            _bits = encoder.BitDepth ?? (_reader.Depth == 8 ? 10 : 16);
            if (_reader.Depth == 16 && _bits < 16 && !encoder.AllowBitDepthReduction)
                throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"Writing {options.PixelFormat} pixels with {_bits}-bit samples loses precision. Set CineonEncoder.AllowBitDepthReduction to round them, or write 16-bit samples."), "Bit depth reduction");

            _lineLength = GetLineLength(_width, _channels, _bits);
            if (_lineLength > CheckedSizes.MaxBufferLength || (long)_width * _channels * 2 > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            // The header is built first: a field of the source header that does not fit is rejected before any output
            _header = CreateHeader();
            var scope = options.Scope;
            try
            {
                _components = scope.Rent(_width * _channels * 2, AllocationKind.Temporary, clear: false);
                _line = scope.Rent((int)_lineLength, AllocationKind.Temporary, clear: true);
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
            Lines,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_reader.HasAlpha)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < _height; y++)
            {
                if (!_reader.IsOpaque(lease.GetRowBytes(y), _width))
                    throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The pixels of the row {y} are not all fully opaque, and a Cineon file has no alpha channel. Flatten the image first."), "Transparency");
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("Cineon output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A Cineon file stores a single frame.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.None;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    output.Write(_header);
                    _step = Step.Lines;
                    return false;

                case Step.Lines:
                {
                    var line = _line!.RawBuffer.AsSpan(0, (int)_lineLength);
                    using (var lease = _frame!.GetStorage().AcquireLease())
                    {
                        WriteLine(lease.GetRowBytes(_y), line);
                    }

                    output.Write(line);
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
                    throw new InvalidOperationException("No Cineon operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _components?.Dispose();
                _components = null;
                _line?.Dispose();
                _line = null;
                _frame = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Packs the samples of a row: red, green and blue (or gray) of each pixel, scaled to the code values.</summary>
        private void WriteLine(ReadOnlySpan<byte> row, Span<byte> line)
        {
            var count = _width * _channels;
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(_components!.RawBuffer.AsSpan(0, count * 2)));
            for (var c = 0; c < _channels; c++)
            {
                // The components of one channel, then interleaved by pixel
                var values = samples[(c * _width)..((c + 1) * _width)];
                _reader.ReadComponent(row, _width, c, values);
            }

            var sourceMax = (uint)_reader.MaxValue;
            var max = (1u << _bits) - 1;
            line.Clear();
            for (var x = 0; x < _width; x++)
            {
                for (var c = 0; c < _channels; c++)
                {
                    var value = (uint)samples[(c * _width) + x];
                    var code = sourceMax == max ? value : (uint)(((2UL * value * max) + sourceMax) / (2UL * sourceMax));
                    var index = (x * _channels) + c;
                    switch (_bits)
                    {
                        case 8:
                            line[index] = (byte)code;
                            break;

                        case 16:
                            BinaryPrimitives.WriteUInt16BigEndian(line[(index * 2)..], (ushort)code);
                            break;

                        default:
                        {
                            // 10 bits: a pixel per word for three channels, three samples per word for one channel, from the top
                            var (word, slot) = _channels == 1 ? (index / 3, index % 3) : (x, c);
                            var shift = 22 - (10 * slot);
                            var position = word * 4;
                            var current = BinaryPrimitives.ReadUInt32BigEndian(line[position..]);
                            BinaryPrimitives.WriteUInt32BigEndian(line[position..], current | (code << shift));
                            break;
                        }
                    }
                }
            }
        }

        private byte[] CreateHeader()
        {
            var header = new byte[CineonFormat.FullHeaderLength];
            var span = header.AsSpan();
            var source = Options.Metadata.CineonHeader;
            var fileSize = CineonFormat.FullHeaderLength + (_lineLength * _height);
            if (fileSize > uint.MaxValue)
                throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The Cineon file would be {fileSize} bytes: the total file size field holds at most 4 GiB."), "Files larger than 4 GiB");

            // Every field the encoder does not know is undefined: all bits set for unsigned numbers and bytes, 0x80000000
            // for signed ones, positive infinity for reals, NUL for text
            span.Fill(0xFF);

            // Section 1: the file information
            WriteUInt32(span, 0, CineonFormat.Magic);
            WriteUInt32(span, 4, CineonFormat.FullHeaderLength);
            WriteUInt32(span, 8, CineonFormat.GenericHeaderLength);
            WriteUInt32(span, 12, CineonFormat.FullHeaderLength - CineonFormat.GenericHeaderLength);
            WriteUInt32(span, 16, 0);
            WriteUInt32(span, 20, (uint)fileSize);
            WriteText(span, 24, 8, Version);
            WriteText(span, 32, 100, source?.FileName);
            WriteText(span, 132, 12, source?.CreationDate);
            WriteText(span, 144, 12, source?.CreationTime);
            WriteText(span, 156, 36, value: null);

            // The image information
            span[192] = CineonFormat.FromExifOrientation(Options.Metadata.Orientation);
            span[193] = (byte)_channels;
            span[194] = 0;
            span[195] = 0;
            var sameDepth = source is not null && source.BitsPerSample == _bits;

            // The samples are written red, green, blue: the channel c takes the designator and the ranges of the channel of
            // the source file that held that color (the order the decoder placed them in)
            int[]? sourceChannels = null;
            if (source is not null && source.ChannelDesignators.Count == _channels)
            {
                var designators = new ushort[_channels];
                for (var c = 0; c < _channels; c++)
                {
                    designators[c] = source.ChannelDesignators[c];
                }

                sourceChannels = CineonStructureParser.GetChannelOrder(designators);
            }

            for (var c = 0; c < 8; c++)
            {
                var offset = CineonFormat.FirstChannelOffset + (c * CineonFormat.ChannelLength);
                if (c >= _channels)
                    continue;

                var from = sourceChannels is null ? c : sourceChannels[c];
                ushort designator = _channels == 1 ? (ushort)0 : (ushort)(c + 1);
                if (sourceChannels is not null)
                {
                    designator = source!.ChannelDesignators[from];
                }

                BinaryPrimitives.WriteUInt16BigEndian(span[offset..], designator);
                span[offset + 2] = (byte)_bits;
                span[offset + 3] = 0;
                WriteUInt32(span, offset + 4, (uint)_width);
                WriteUInt32(span, offset + 8, (uint)_height);
                WriteReal(span, offset + 12, Get(source?.MinimumDataValues, from, sameDepth) ?? 0);
                WriteReal(span, offset + 16, Get(source?.MinimumQuantities, from, sameDepth) ?? 0);
                WriteReal(span, offset + 20, Get(source?.MaximumDataValues, from, sameDepth) ?? (1 << _bits) - 1);
                WriteReal(span, offset + 24, Get(source?.MaximumQuantities, from, sameDepth) ?? DefaultMaximumDensity);
            }

            for (var i = 0; i < 8; i++)
            {
                WriteReal(span, 420 + (i * 4), source?.Chromaticities is { Count: 8 } chromaticities ? chromaticities[i] : null);
            }

            WriteText(span, 452, 200, source?.Label);
            WriteText(span, 652, 28, value: null);

            // The image data format: pixel interleave, packing 5, unsigned, no padding
            span[680] = 0;
            span[681] = Packing;
            span[682] = 0;
            span[683] = source?.IsNegative == true ? (byte)1 : (byte)0;
            WriteUInt32(span, 684, 0);
            WriteUInt32(span, 688, 0);
            WriteText(span, 692, 20, value: null);

            // The image origination
            WriteInt32(span, 712, source?.XOffset);
            WriteInt32(span, 716, source?.YOffset);
            WriteText(span, 720, 100, source?.SourceFileName);
            WriteText(span, 820, 12, source?.SourceCreationDate);
            WriteText(span, 832, 12, source?.SourceCreationTime);
            WriteText(span, 844, 64, source?.InputDevice);
            WriteText(span, 908, 32, source?.InputDeviceModel);
            WriteText(span, 940, 32, source?.InputDeviceSerialNumber);
            WriteReal(span, 972, source?.XDevicePitch);
            WriteReal(span, 976, source?.YDevicePitch);
            WriteReal(span, 980, source?.Gamma);
            WriteText(span, 984, 40, value: null);

            // Section 2: the motion-picture film information. The edge code is the five numbers the decoder joins
            span[1027] = 0;
            if (source?.FilmEdgeCode is { Length: 16 } edgeCode && edgeCode.All(char.IsAsciiDigit))
            {
                span[1024] = byte.Parse(edgeCode.AsSpan(0, 2), CultureInfo.InvariantCulture);
                span[1025] = byte.Parse(edgeCode.AsSpan(2, 2), CultureInfo.InvariantCulture);
                span[1026] = byte.Parse(edgeCode.AsSpan(4, 2), CultureInfo.InvariantCulture);
                WriteUInt32(span, 1028, uint.Parse(edgeCode.AsSpan(6, 6), CultureInfo.InvariantCulture));
                WriteUInt32(span, 1032, uint.Parse(edgeCode.AsSpan(12, 4), CultureInfo.InvariantCulture));
            }

            WriteText(span, 1036, 32, source?.FilmFormat);
            if (source?.FramePosition is { } position)
            {
                WriteUInt32(span, 1068, position);
            }

            WriteReal(span, 1072, source?.FrameRate);
            WriteText(span, 1076, 32, source?.FrameAttribute);
            WriteText(span, 1108, 200, source?.SlateInformation);
            WriteText(span, 1308, 740, value: null);
            return header;
        }

        /// <summary>Gets a value of a channel of the source header, when it describes code values of the same depth.</summary>
        private float? Get(IReadOnlyList<float?>? values, int channel, bool sameDepth)
            => sameDepth && values is not null && values.Count == _channels ? values[channel] : null;

        private static void WriteUInt32(Span<byte> header, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(header[offset..], value);

        private static void WriteInt32(Span<byte> header, int offset, int? value) => BinaryPrimitives.WriteInt32BigEndian(header[offset..], value ?? int.MinValue);

        private static void WriteReal(Span<byte> header, int offset, float? value) => BinaryPrimitives.WriteSingleBigEndian(header[offset..], value ?? float.PositiveInfinity);

        /// <summary>Writes a text field: Latin-1, the rest of the field NUL (an undefined field is all NUL).</summary>
        /// <exception cref="UnsupportedImageFeatureException">The text is longer than the field, or is not Latin-1.</exception>
        private static void WriteText(Span<byte> header, int offset, int length, string? value)
        {
            var field = header.Slice(offset, length);
            field.Clear();
            if (string.IsNullOrEmpty(value))
                return;

            if (value.Length > length || value.AsSpan().ContainsAnyExceptInRange('\u0001', 'ÿ'))
                throw CineonFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The text '{value}' of the Cineon header does not fit its field of {length} Latin-1 characters (offset {offset})."), "Metadata: Cineon header text");

            for (var i = 0; i < value.Length; i++)
            {
                field[i] = (byte)value[i];
            }
        }
    }
}
