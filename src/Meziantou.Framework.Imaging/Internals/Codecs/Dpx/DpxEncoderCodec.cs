using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The DPX encoder registration: one image element per output, uncompressed.</summary>
internal sealed class DpxEncoderCodec : ImageEncoderCodec
{
    /// <summary>The creator the encoder writes in the file information header (field 12).</summary>
    public const string Creator = "Meziantou.Framework.Imaging";

    public static DpxEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Dpx;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>Gets the descriptor of the element the encoder writes for a pixel format.</summary>
    internal static DpxDescriptor GetDescriptor(PixelFormat format, bool isOpaque)
        => PixelFormats.IsGrayscale(format) && !PixelFormats.HasAlpha(format) ? DpxDescriptor.Luma
            : PixelFormats.HasAlpha(format) && !isOpaque ? DpxDescriptor.Rgba
            : DpxDescriptor.Rgb;

    /// <summary>
    /// The encoding state of one DPX output: the headers, then one line (a row, from the top) per <see cref="Encode"/> call.
    /// </summary>
    /// <remarks>
    /// <see cref="ValidateFrame"/> finds whether the image is opaque (an opaque image is written as RGB) before anything is
    /// written. The private state is one line of components and one stored line: memory does not depend on the height.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _bigEndian;
        private readonly PixelFormat _source;
        private readonly int _bitDepth;
        private readonly int _sourceMax;
        private readonly DpxTransferCharacteristic _transfer;
        private readonly DpxColorimetricSpecification _colorimetric;
        private readonly ushort _orientation;
        private readonly PixelAspectRatio? _pixelAspectRatio;
        private PooledBuffer? _components;
        private PooledBuffer? _line;
        private ImageFrame? _frame;
        private DpxDescriptor _descriptor;
        private int _componentsPerPixel;
        private long _lineLength;
        private bool _isOpaque = true;
        private Step _step;
        private int _y;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            var encoder = (DpxEncoder)options.Encoder;
            _source = options.PixelFormat;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _bigEndian = encoder.ByteOrder == DpxByteOrder.BigEndian;
            var isFloat = PixelFormats.IsFloatingPoint(_source);
            var is16Bit = !isFloat && PixelFormats.GetBitsPerComponent(_source) == 16;
            _sourceMax = isFloat ? 0 : is16Bit ? ushort.MaxValue : byte.MaxValue;
            _bitDepth = encoder.BitDepth ?? (isFloat ? 32 : is16Bit ? 16 : 8);
            if (isFloat && _bitDepth != 32)
                throw new InvalidOperationException("Floating-point pixels reach the DPX encoder only for floating-point components.");

            if (is16Bit && _bitDepth < 16 && !encoder.AllowBitDepthReduction)
                throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"Writing {_source} pixels with {_bitDepth}-bit components loses precision. Set DpxEncoder.AllowBitDepthReduction to round them, or write 16-bit components."), "Bit depth reduction");

            var plan = options.Metadata;
            (_transfer, _colorimetric) = plan.TransferFunction == ColorTransferFunction.Linear
                ? (DpxTransferCharacteristic.Linear, DpxColorimetricSpecification.Bt709)
                : plan.Cicp is { } cicp && DpxFormat.FromCicp(cicp) is { } codes
                    ? codes
                    : (DpxTransferCharacteristic.UserDefined, DpxColorimetricSpecification.Bt709);
            _orientation = DpxFormat.FromExifOrientation(plan.Orientation);
            _pixelAspectRatio = plan.PixelAspectRatio;

            // The largest line: four components per pixel
            var maxLine = DpxRowReader.GetStoredLineLength(_bitDepth, DpxPacking.FilledMethodA, (long)_width * 4);
            if (maxLine > CheckedSizes.MaxBufferLength || (long)_width * 16 > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            // Every buffer is rented before any output
            var scope = options.Scope;
            try
            {
                _components = scope.Rent(_width * 16, AllocationKind.Temporary, clear: false);
                _line = scope.Rent((int)maxLine, AllocationKind.Temporary, clear: true);
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
            if (!PixelFormats.HasAlpha(_source))
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height && _isOpaque; y++)
            {
                _isOpaque = IsOpaque(lease.GetRowBytes(y));
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("DPX output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            if (index != 0)
                throw new InvalidOperationException("A DPX file stores a single frame.");

            _frame = frame;
            _descriptor = GetDescriptor(_source, _isOpaque);
            _componentsPerPixel = DpxFormat.GetComponentsPerPixel(_descriptor);
            _lineLength = DpxRowReader.GetStoredLineLength(_bitDepth, DpxPacking.FilledMethodA, (long)_width * _componentsPerPixel);
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.None;

        public override bool Encode(ImageOutputBuffer output)
        {
            switch (_step)
            {
                case Step.Header:
                    output.Write(CreateHeader());
                    _step = Step.Lines;
                    return false;

                case Step.Lines:
                {
                    var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
                    var line = _line!.RawBuffer.AsSpan(0, (int)_lineLength);
                    using (var lease = frame.GetStorage().AcquireLease())
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
                    throw new InvalidOperationException("No DPX operation was begun.");
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

        private bool IsOpaque(ReadOnlySpan<byte> row)
        {
            switch (_source)
            {
                case PixelFormat.Rgba32 or PixelFormat.Bgra32:
                    for (var x = 0; x < _width; x++)
                    {
                        if (row[(x * 4) + 3] != byte.MaxValue)
                            return false;
                    }

                    return true;

                case PixelFormat.Rgba64:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row[..(_width * 8)]));
                    for (var x = 0; x < _width; x++)
                    {
                        if (samples[(x * 4) + 3] != ushort.MaxValue)
                            return false;
                    }

                    return true;
                }

                case PixelFormat.RgbaFloat:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, float>(row[..(_width * 16)]));
                    for (var x = 0; x < _width; x++)
                    {
                        if (samples[(x * 4) + 3] != 1f)
                            return false;
                    }

                    return true;
                }

                case PixelFormat.RgbaHalf:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, Half>(row[..(_width * 8)]));
                    for (var x = 0; x < _width; x++)
                    {
                        if (samples[(x * 4) + 3] != Half.One)
                            return false;
                    }

                    return true;
                }

                default:
                    throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The DPX encoder does not write {_source} pixels."));
            }
        }

        /// <summary>Gets the components of a row, in the order of the descriptor: integers of <see cref="_sourceMax"/>, or the bits of single-precision numbers.</summary>
        private Span<uint> GetComponents(ReadOnlySpan<byte> row)
        {
            var count = _width * _componentsPerPixel;
            var components = unsafe(MemoryMarshal.Cast<byte, uint>(_components!.RawBuffer.AsSpan(0, count * 4)));
            var n = _componentsPerPixel;
            switch (_source)
            {
                case PixelFormat.Gray8:
                    for (var x = 0; x < _width; x++)
                    {
                        components[x] = row[x];
                    }

                    break;

                case PixelFormat.Gray16:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row[..(_width * 2)]));
                    for (var x = 0; x < _width; x++)
                    {
                        components[x] = samples[x];
                    }

                    break;
                }

                case PixelFormat.Rgb24 or PixelFormat.Rgba32:
                {
                    var stride = _source == PixelFormat.Rgb24 ? 3 : 4;
                    for (var x = 0; x < _width; x++)
                    {
                        for (var c = 0; c < n; c++)
                        {
                            components[(x * n) + c] = row[(x * stride) + c];
                        }
                    }

                    break;
                }

                case PixelFormat.Bgra32:
                    for (var x = 0; x < _width; x++)
                    {
                        // Blue, green, red and alpha in memory: the components are red, green, blue (and alpha)
                        components[x * n] = row[(x * 4) + 2];
                        components[(x * n) + 1] = row[(x * 4) + 1];
                        components[(x * n) + 2] = row[x * 4];
                        if (n == 4)
                        {
                            components[(x * n) + 3] = row[(x * 4) + 3];
                        }
                    }

                    break;

                case PixelFormat.Rgb48 or PixelFormat.Rgba64:
                {
                    var stride = _source == PixelFormat.Rgb48 ? 3 : 4;
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row[..(_width * stride * 2)]));
                    for (var x = 0; x < _width; x++)
                    {
                        for (var c = 0; c < n; c++)
                        {
                            components[(x * n) + c] = samples[(x * stride) + c];
                        }
                    }

                    break;
                }

                case PixelFormat.RgbaFloat:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, float>(row[..(_width * 16)]));
                    for (var x = 0; x < _width; x++)
                    {
                        for (var c = 0; c < n; c++)
                        {
                            components[(x * n) + c] = BitConverter.SingleToUInt32Bits(samples[(x * 4) + c]);
                        }
                    }

                    break;
                }

                case PixelFormat.RgbaHalf:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, Half>(row[..(_width * 8)]));
                    for (var x = 0; x < _width; x++)
                    {
                        for (var c = 0; c < n; c++)
                        {
                            components[(x * n) + c] = BitConverter.SingleToUInt32Bits((float)samples[(x * 4) + c]);
                        }
                    }

                    break;
                }

                case PixelFormat.GrayFloat:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, uint>(row[..(_width * 4)]));
                    samples.CopyTo(components);
                    break;
                }

                case PixelFormat.GrayHalf:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, Half>(row[..(_width * 2)]));
                    for (var x = 0; x < _width; x++)
                    {
                        components[x] = BitConverter.SingleToUInt32Bits((float)samples[x]);
                    }

                    break;
                }

                default:
                    throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The DPX encoder does not write {_source} pixels."));
            }

            return components;
        }

        private void WriteLine(ReadOnlySpan<byte> row, Span<byte> line)
        {
            var components = GetComponents(row);
            line.Clear();
            if (_bitDepth == 32)
            {
                var isFloat = _sourceMax == 0;
                for (var i = 0; i < components.Length; i++)
                {
                    var bits = isFloat ? components[i] : BitConverter.SingleToUInt32Bits((float)((double)components[i] / _sourceMax));
                    WriteWord(line, i, bits);
                }

                return;
            }

            var max = (1u << _bitDepth) - 1;
            for (var i = 0; i < components.Length; i++)
            {
                components[i] = Quantize(components[i], max);
            }

            switch (_bitDepth)
            {
                case 8:
                    for (var i = 0; i < components.Length; i++)
                    {
                        line[i] = (byte)components[i];
                    }

                    break;

                case 10:
                {
                    // Method A: the first component of a word of a multi-component element at the top, of a luma element at the bottom
                    var single = _descriptor == DpxDescriptor.Luma;
                    for (var i = 0; i < components.Length; i += 3)
                    {
                        uint word = 0;
                        for (var j = 0; j < 3 && i + j < components.Length; j++)
                        {
                            var shift = single ? 2 + (10 * j) : 22 - (10 * j);
                            word |= components[i + j] << shift;
                        }

                        WriteWord(line, i / 3, word);
                    }

                    break;
                }

                case 12:
                    for (var i = 0; i < components.Length; i++)
                    {
                        WriteHalfWord(line, i, (ushort)(components[i] << 4));
                    }

                    break;

                case 16:
                    for (var i = 0; i < components.Length; i++)
                    {
                        WriteHalfWord(line, i, (ushort)components[i]);
                    }

                    break;

                default:
                    throw new InvalidOperationException("The DPX bit depth is not valid.");
            }
        }

        /// <summary>Scales a sample of the source to the nearest code value of <paramref name="max"/>.</summary>
        private uint Quantize(uint value, uint max)
        {
            var sourceMax = (uint)_sourceMax;
            if (sourceMax == max)
                return value;

            return (uint)(((2UL * value * max) + sourceMax) / (2UL * sourceMax));
        }

        private void WriteWord(Span<byte> destination, int index, uint value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(destination[(index * 4)..], value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination[(index * 4)..], value);
            }
        }

        private void WriteHalfWord(Span<byte> destination, int index, ushort value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination[(index * 2)..], value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(index * 2)..], value);
            }
        }

        private byte[] CreateHeader()
        {
            // Every field the encoder does not know is undefined (clause 4.2): all bits set for numbers, NUL for text
            var header = new byte[DpxFormat.EncodedDataOffset];
            header.AsSpan(0, DpxFormat.FullHeaderLength).Fill(0xFF);
            ClearText(header, 8, 8);
            ClearText(header, 36, 100);
            ClearText(header, 136, 24);
            ClearText(header, 160, 100);
            ClearText(header, 260, 200);
            ClearText(header, 460, 200);
            ClearText(header, 664, 104);
            ClearText(header, 1356, 52);
            ClearText(header, 1432, 100);
            ClearText(header, 1532, 24);
            ClearText(header, 1556, 32);
            ClearText(header, 1588, 32);
            ClearText(header, 1644, 20);
            ClearText(header, 1664, 48);
            ClearText(header, 1732, 32);
            ClearText(header, 1764, 100);
            ClearText(header, 1864, 56);
            header[1931] = 0;
            ClearText(header, 1972, 76);

            var span = header.AsSpan();
            var fileSize = DpxFormat.EncodedDataOffset + (_lineLength * _height);
            if (fileSize > uint.MaxValue)
                throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DPX file would be {fileSize} bytes: the total file size field holds at most 4 GiB."), "Files larger than 4 GiB");

            WriteWord(span, 0, DpxFormat.Magic);
            WriteWord(span, 1, DpxFormat.EncodedDataOffset);
            Encoding.ASCII.GetBytes("V2.0", span[8..]);
            WriteWord(span, 4, (uint)fileSize);
            WriteWord(span, 5, 1);
            WriteWord(span, 6, DpxFormat.GenericHeaderLength);
            WriteWord(span, 7, DpxFormat.FullHeaderLength - DpxFormat.GenericHeaderLength);
            WriteWord(span, 8, 0);
            Encoding.ASCII.GetBytes(Creator, span[160..]);

            WriteHalfWord(span, 768 / 2, _orientation);
            WriteHalfWord(span, 770 / 2, 1);
            WriteWord(span, 772 / 4, (uint)_width);
            WriteWord(span, 776 / 4, (uint)_height);

            // The first image element: unsigned components, the full range of the integer code values
            const int Element = DpxFormat.FirstElementOffset;
            WriteWord(span, Element / 4, 0);
            WriteWord(span, (Element + 4) / 4, 0);
            if (_bitDepth != 32)
            {
                WriteWord(span, (Element + 12) / 4, (1u << _bitDepth) - 1);
            }

            span[Element + 20] = (byte)_descriptor;
            span[Element + 21] = (byte)_transfer;
            span[Element + 22] = (byte)_colorimetric;
            span[Element + 23] = (byte)_bitDepth;
            WriteHalfWord(span, (Element + 24) / 2, (ushort)(_bitDepth is 10 or 12 ? DpxPacking.FilledMethodA : DpxPacking.Packed));
            WriteHalfWord(span, (Element + 26) / 2, 0);
            WriteWord(span, (Element + 28) / 4, DpxFormat.EncodedDataOffset);
            WriteWord(span, (Element + 32) / 4, 0);
            WriteWord(span, (Element + 36) / 4, 0);
            ClearText(header, Element + 40, 32);

            // The image source header: no offset in the original image, the pixel aspect ratio
            WriteWord(span, 1408 / 4, 0);
            WriteWord(span, 1412 / 4, 0);
            var aspect = _pixelAspectRatio ?? new PixelAspectRatio(1, 1);
            WriteWord(span, 1628 / 4, aspect.HorizontalSpacing);
            WriteWord(span, 1632 / 4, aspect.VerticalSpacing);
            return header;
        }

        private static void ClearText(byte[] header, int offset, int length) => header.AsSpan(offset, length).Clear();
    }
}
