using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Adobe Photoshop encoder registration (Adobe Photoshop File Formats Specification, "Photoshop File Format"): a flat
/// document whose composite image is the image, with one layer holding the image when it has transparency.
/// </summary>
internal sealed class PsdEncoderCodec : ImageEncoderCodec
{
    /// <summary>The name the encoder writes in the version information resource (1057).</summary>
    public const string WriterName = "Meziantou.Framework.Imaging";

    /// <summary>The name of the layer the encoder writes for an image that has transparency.</summary>
    public const string LayerName = "Layer 0";

    public static PsdEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Psd;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>Determines whether an image of this size is written as a PSB file.</summary>
    internal static bool IsLargeDocument(PsdEncoder encoder, Size size) => encoder.LargeDocument || size.Width > PsdFormat.MaxPsdDimension || size.Height > PsdFormat.MaxPsdDimension;

    /// <summary>
    /// The encoding state of one PSD output. <see cref="ValidateFrame"/> reads the whole frame before anything is written: it
    /// finds whether the image is opaque and, for PackBits rows, the compressed length of every row of every channel, which
    /// the tables of the file give before the rows.
    /// </summary>
    /// <remarks>
    /// The channels the file stores are the planes of the session: the colors of the composite (blended over white when
    /// the image has transparency), the transparency, and the colors of the layer (as they are). The private state is the
    /// table of the compressed lengths (4 bytes per row of each plane) and a few rows.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private readonly ComponentRowReader _reader;
        private readonly int _width;
        private readonly int _height;
        private readonly int _depth;
        private readonly int _mode;
        private readonly bool _runLength;
        private readonly bool _largeDocument;
        private readonly int _rowLength;
        private PooledBuffer? _values;
        private PooledBuffer? _alpha;
        private PooledBuffer? _row;
        private PooledBuffer? _packed;
        private PooledBuffer? _counts;
        private long[] _planeLengths = [];
        private ImageFrame? _measured;
        private ImageFrame? _frame;
        private bool _hasAlpha;
        private List<Segment>? _segments;
        private int _segment;
        private int _position;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            var encoder = (PsdEncoder)options.Encoder;
            _reader = new ComponentRowReader(options.PixelFormat);
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _depth = _reader.Depth;
            _mode = _reader.ColorComponents switch
            {
                1 => PsdFormat.ModeGrayscale,
                3 => PsdFormat.ModeRgb,
                _ => PsdFormat.ModeCmyk,
            };
            _runLength = encoder.Compression == PsdCompression.RunLength;
            _largeDocument = IsLargeDocument(encoder, options.CanvasSize);
            var rowLength = (long)_width * _depth / 8;
            var packedLength = (long)TiffPackBits.GetMaxEncodedLength((int)Math.Min(rowLength, int.MaxValue / 2));
            if (packedLength > CheckedSizes.MaxBufferLength || (long)_width * 4 > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            _rowLength = (int)rowLength;
            var scope = options.Scope;
            try
            {
                _values = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
                _alpha = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
                _row = scope.Rent(_rowLength, AllocationKind.Temporary, clear: false);
                _packed = _runLength ? scope.Rent((int)packedLength, AllocationKind.Temporary, clear: false) : null;
            }
            catch
            {
                Dispose(disposing: true);
                throw;
            }
        }

        private enum SegmentKind
        {
            Bytes,
            Counts,
            Rows,
        }

        /// <summary>The number of color channels.</summary>
        private int ColorChannels => _reader.ColorComponents;

        /// <summary>The number of planes the file stores: the colors of the composite, then the transparency and the colors of the layer.</summary>
        private int PlaneCount => _hasAlpha ? (2 * ColorChannels) + 1 : ColorChannels;

        /// <summary>The number of bytes of a length of the layer and mask information (and of the lengths it holds).</summary>
        private int LengthSize => _largeDocument ? 8 : 4;

        /// <summary>The number of bytes of the compressed length of a PackBits row.</summary>
        private int CountSize => _largeDocument ? 4 : 2;

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            _hasAlpha = false;
            using (var lease = frame.GetStorage().AcquireLease())
            {
                for (var y = 0; y < _height && !_hasAlpha; y++)
                {
                    _hasAlpha = !_reader.IsOpaque(lease.GetRowBytes(y), _width);
                }
            }

            if (_runLength)
            {
                Measure(frame);
            }

            _measured = frame;
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("PSD output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A PSD file stores a single image.");

            if (!ReferenceEquals(frame, _measured))
            {
                ValidateFrame(frame, isPoster: false);
            }

            _frame = frame;
            _segments = CreateSegments();
            _segment = 0;
            _position = 0;
        }

        public override void BeginComplete(int frameCount) => _segments = null;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            var segments = _segments;
            if (segments is null)
                return true;

            using var lease = _frame!.GetStorage().AcquireLease();
            while (_segment < segments.Count)
            {
                var segment = segments[_segment];
                switch (segment.Kind)
                {
                    case SegmentKind.Bytes:
                        output.Write(segment.Data.Span);
                        _segment++;
                        break;

                    case SegmentKind.Counts:
                    {
                        // A band of the table of the compressed lengths of the rows of a plane
                        var counts = GetCounts(segment.Plane);
                        var end = Math.Min(_height, _position + 4096);
                        var span = output.GetSpan((end - _position) * CountSize);
                        for (var y = _position; y < end; y++)
                        {
                            if (_largeDocument)
                            {
                                BinaryPrimitives.WriteUInt32BigEndian(span[((y - _position) * 4)..], (uint)counts[y]);
                            }
                            else
                            {
                                BinaryPrimitives.WriteUInt16BigEndian(span[((y - _position) * 2)..], (ushort)counts[y]);
                            }
                        }

                        output.Advance((end - _position) * CountSize);
                        _position = end;
                        if (_position == _height)
                        {
                            _position = 0;
                            _segment++;
                        }

                        break;
                    }

                    case SegmentKind.Rows:
                    {
                        var row = _row!.RawBuffer.AsSpan(0, _rowLength);
                        StoreRow(lease.GetRowBytes(_position), segment.Plane, row);
                        if (_runLength)
                        {
                            var packed = _packed!.RawBuffer.AsSpan();
                            var length = TiffPackBits.EncodeRow(row, packed);
                            output.Write(packed[..length]);
                        }
                        else
                        {
                            output.Write(row);
                        }

                        _position++;
                        if (_position == _height)
                        {
                            _position = 0;
                            _segment++;
                        }

                        break;
                    }

                    default:
                        throw new InvalidOperationException("The segment of the PSD file is not valid.");
                }

                if (output.ShouldFlush)
                    return _segment == segments.Count && Finish();
            }

            return Finish();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _values?.Dispose();
                _values = null;
                _alpha?.Dispose();
                _alpha = null;
                _row?.Dispose();
                _row = null;
                _packed?.Dispose();
                _packed = null;
                _counts?.Dispose();
                _counts = null;
                _frame = null;
                _measured = null;
                _segments = null;
            }

            base.Dispose(disposing);
        }

        private bool Finish()
        {
            _segments = null;
            _frame = null;
            return true;
        }

        private Span<int> GetCounts(int plane) => unsafe(MemoryMarshal.Cast<byte, int>(_counts!.RawBuffer.AsSpan(plane * _height * 4, _height * 4)));

        /// <summary>Compresses every row of every plane once, to know the lengths the tables and the layer record give before the rows.</summary>
        private void Measure(ImageFrame frame)
        {
            var planes = PlaneCount;
            var size = (long)planes * _height * 4;
            if (size > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(Options.Configuration.Limits);

            if (_counts is null || _counts.RawBuffer.Length < size)
            {
                _counts?.Dispose();
                _counts = null;
                _counts = Options.Scope.Rent((int)size, AllocationKind.Temporary, clear: false);
            }

            _planeLengths = new long[planes];
            var row = _row!.RawBuffer.AsSpan(0, _rowLength);
            var packed = _packed!.RawBuffer.AsSpan();
            using var lease = frame.GetStorage().AcquireLease();
            for (var plane = 0; plane < planes; plane++)
            {
                var counts = GetCounts(plane);
                for (var y = 0; y < _height; y++)
                {
                    StoreRow(lease.GetRowBytes(y), plane, row);
                    var length = TiffPackBits.EncodeRow(row, packed);
                    if (!_largeDocument && length > ushort.MaxValue)
                        throw PsdFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"A row of the image compresses to {length} bytes, and the PackBits rows of a PSD file record their length in 16 bits. Set PsdEncoder.Compression to None, or PsdEncoder.LargeDocument to write a PSB file."), "PackBits rows longer than 65,535 bytes");

                    counts[y] = length;
                    _planeLengths[plane] += length;
                }
            }
        }

        /// <summary>Gets the number of bytes of the data of a plane, without its compression field.</summary>
        private long GetPlaneDataLength(int plane) => _runLength ? ((long)_height * CountSize) + _planeLengths[plane] : (long)_height * _rowLength;

        /// <summary>Writes the stored samples of one row of a plane (big-endian; CMYK inverted; the colors of the composite blended over white).</summary>
        private void StoreRow(ReadOnlySpan<byte> pixels, int plane, Span<byte> destination)
        {
            var colors = ColorChannels;
            var isAlpha = plane == colors;
            var component = plane < colors ? plane : isAlpha ? _reader.AlphaComponent : plane - colors - 1;
            var matted = _hasAlpha && plane < colors;
            if (_reader.IsFloatingPoint)
            {
                var values = unsafe(MemoryMarshal.Cast<byte, float>(_values!.RawBuffer.AsSpan(0, _width * 4)));
                _reader.ReadComponent(pixels, _width, component, values);
                if (matted)
                {
                    var alpha = unsafe(MemoryMarshal.Cast<byte, float>(_alpha!.RawBuffer.AsSpan(0, _width * 4)));
                    _reader.ReadComponent(pixels, _width, _reader.AlphaComponent, alpha);
                    for (var x = 0; x < _width; x++)
                    {
                        values[x] = (values[x] * alpha[x]) + (1f - alpha[x]);
                    }
                }

                for (var x = 0; x < _width; x++)
                {
                    BinaryPrimitives.WriteSingleBigEndian(destination[(x * 4)..], values[x]);
                }

                return;
            }

            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(_values!.RawBuffer.AsSpan(0, _width * 2)));
            _reader.ReadComponent(pixels, _width, component, samples);
            var max = (uint)_reader.MaxValue;
            if (matted)
            {
                var alpha = unsafe(MemoryMarshal.Cast<byte, ushort>(_alpha!.RawBuffer.AsSpan(0, _width * 2)));
                _reader.ReadComponent(pixels, _width, _reader.AlphaComponent, alpha);
                for (var x = 0; x < _width; x++)
                {
                    // c × α + white × (1 − α), rounded to the nearest sample
                    uint a = alpha[x];
                    samples[x] = (ushort)((((ulong)samples[x] * a) + ((ulong)max * (max - a)) + (max / 2)) / max);
                }
            }

            if (_mode == PsdFormat.ModeCmyk && !isAlpha)
            {
                for (var x = 0; x < _width; x++)
                {
                    samples[x] = (ushort)(max - samples[x]);
                }
            }

            if (_depth == 8)
            {
                for (var x = 0; x < _width; x++)
                {
                    destination[x] = (byte)samples[x];
                }
            }
            else
            {
                for (var x = 0; x < _width; x++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(destination[(x * 2)..], samples[x]);
                }
            }
        }

        /// <summary>Lays out the file: the sections before the image data, the layer, then the composite image.</summary>
        private List<Segment> CreateSegments()
        {
            var colors = ColorChannels;
            var segments = new List<Segment>();
            var prologue = new ArrayBufferWriter<byte>();
            WriteHeader(prologue);
            WriteResources(prologue, segments);
            if (_hasAlpha)
            {
                WriteLayerSection(prologue, segments);
            }
            else
            {
                // No layer and mask information
                WriteLength(prologue, 0);
                segments.Add(Segment.Bytes(prologue.WrittenSpan.ToArray()));
            }

            // The composite image: the colors, then the transparency, compressed alike
            var composite = new ArrayBufferWriter<byte>();
            WriteUInt16(composite, (ushort)(_runLength ? PsdFormat.CompressionRle : PsdFormat.CompressionRaw));
            segments.Add(Segment.Bytes(composite.WrittenSpan.ToArray()));
            var planes = _hasAlpha ? colors + 1 : colors;
            if (_runLength)
            {
                for (var plane = 0; plane < planes; plane++)
                {
                    segments.Add(new Segment(SegmentKind.Counts, default, plane));
                }
            }

            for (var plane = 0; plane < planes; plane++)
            {
                segments.Add(new Segment(SegmentKind.Rows, default, plane));
            }

            return segments;
        }

        private void WriteHeader(ArrayBufferWriter<byte> writer)
        {
            writer.Write(PsdFormat.Signature);
            WriteUInt16(writer, (ushort)(_largeDocument ? PsdFormat.PsbVersion : PsdFormat.PsdVersion));
            writer.Write((ReadOnlySpan<byte>)[0, 0, 0, 0, 0, 0]);
            WriteUInt16(writer, (ushort)(ColorChannels + (_hasAlpha ? 1 : 0)));
            WriteUInt32(writer, (uint)_height);
            WriteUInt32(writer, (uint)_width);
            WriteUInt16(writer, (ushort)_depth);
            WriteUInt16(writer, (ushort)_mode);

            // No color mode data: the data of the indexed and duotone modes only
            WriteUInt32(writer, 0);
        }

        /// <summary>
        /// Writes the image resources. The payloads of the ICC profile, the EXIF data and the XMP packet are segments of their
        /// own, written from the metadata of the image: they are never copied.
        /// </summary>
        private void WriteResources(ArrayBufferWriter<byte> writer, List<Segment> segments)
        {
            var metadata = Options.Metadata;
            var resources = new List<(int Id, ReadOnlyMemory<byte> Data)>();
            if (metadata.Resolution is { } resolution)
            {
                // Fixed-point pixels per inch, displayed in pixels per inch, the width and height displayed in inches
                var data = new byte[16];
                BinaryPrimitives.WriteInt32BigEndian(data, ToFixed(resolution.HorizontalDpi));
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1);
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 1);
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), ToFixed(resolution.VerticalDpi));
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 1);
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(14), 1);
                resources.Add((PsdFormat.ResourceResolutionInfo, data));
            }

            if (metadata.IccProfile is { } icc)
            {
                resources.Add((PsdFormat.ResourceIccProfile, icc.Data.Memory));
            }

            resources.Add((PsdFormat.ResourceVersionInfo, CreateVersionInfo()));
            if (metadata.Exif is { } exif)
            {
                resources.Add((PsdFormat.ResourceExif, exif));
            }

            if (metadata.XmpProfile is { } xmp)
            {
                resources.Add((PsdFormat.ResourceXmp, xmp.Data.Memory));
            }

            // Each resource: the signature, the id, an empty name padded to an even length, the length, the data padded to an
            // even length
            var length = 0L;
            foreach (var (_, data) in resources)
            {
                length += 4 + 2 + 2 + 4 + data.Length + (data.Length % 2);
            }

            WriteUInt32(writer, checked((uint)length));
            foreach (var (id, data) in resources)
            {
                writer.Write(PsdFormat.ResourceSignature);
                WriteUInt16(writer, (ushort)id);
                writer.Write((ReadOnlySpan<byte>)[0, 0]);
                WriteUInt32(writer, (uint)data.Length);
                segments.Add(Segment.Bytes(writer.WrittenSpan.ToArray()));
                writer.Clear();
                segments.Add(Segment.Bytes(data));
                if (data.Length % 2 != 0)
                {
                    writer.Write((ReadOnlySpan<byte>)[0]);
                }
            }
        }

        /// <summary>Converts a resolution to a fixed-point number of 16.16 bits.</summary>
        private static int ToFixed(double dpi)
        {
            var value = Math.Round(dpi * 65536, MidpointRounding.AwayFromZero);
            return value is >= 1 and <= int.MaxValue ? (int)value : throw new InvalidOperationException("The resolution was validated by the metadata plan.");
        }

        /// <summary>The version information (1057): the file has a real composite image, written by this library.</summary>
        private static byte[] CreateVersionInfo()
        {
            var writer = new ArrayBufferWriter<byte>();
            WriteUInt32(writer, 1);
            writer.Write((ReadOnlySpan<byte>)[1]);
            WriteUnicodeString(writer, WriterName);
            WriteUnicodeString(writer, WriterName);
            WriteUInt32(writer, 1);
            return writer.WrittenSpan.ToArray();
        }

        private static void WriteUnicodeString(ArrayBufferWriter<byte> writer, string value)
        {
            WriteUInt32(writer, (uint)value.Length);
            writer.Write(Encoding.BigEndianUnicode.GetBytes(value));
        }

        /// <summary>
        /// Writes the layer and mask information of an image that has transparency: one layer that holds the image, and a
        /// negative layer count, which says that the channel after the colors is the transparency of the composite.
        /// </summary>
        /// <remarks>
        /// As Photoshop does, the layers of 16 and 32-bit documents are written in an <c>Lr16</c> or <c>Lr32</c> tagged
        /// block after the global layer mask information, the layer information itself being empty.
        /// </remarks>
        private void WriteLayerSection(ArrayBufferWriter<byte> prologue, List<Segment> segments)
        {
            var colors = ColorChannels;

            // The layer channels: the transparency (-1), then the colors as they are
            Span<int> planes = stackalloc int[colors + 1];
            planes[0] = colors;
            for (var c = 0; c < colors; c++)
            {
                planes[c + 1] = colors + 1 + c;
            }

            var name = Encoding.ASCII.GetBytes(LayerName);
            var nameLength = Align(1 + name.Length, 4);
            var record = new ArrayBufferWriter<byte>();
            WriteUInt16(record, ushort.MaxValue);
            WriteUInt32(record, 0);
            WriteUInt32(record, 0);
            WriteUInt32(record, (uint)_height);
            WriteUInt32(record, (uint)_width);
            WriteUInt16(record, (ushort)(colors + 1));
            var dataLength = 0L;
            for (var i = 0; i < planes.Length; i++)
            {
                var channelLength = 2 + GetPlaneDataLength(planes[i]);
                WriteUInt16(record, (ushort)(short)(i - 1));
                WriteLength(record, channelLength);
                dataLength += channelLength;
            }

            // Normal blending, opaque, not clipped, visible
            record.Write(PsdFormat.ResourceSignature);
            record.Write("norm"u8);
            record.Write((ReadOnlySpan<byte>)[255, 0, 0, 0]);
            WriteUInt32(record, (uint)(4 + 4 + nameLength));
            WriteUInt32(record, 0);
            WriteUInt32(record, 0);
            var paddedName = new byte[nameLength];
            paddedName[0] = (byte)name.Length;
            name.CopyTo(paddedName, 1);
            record.Write(paddedName);

            var layerInfoLength = record.WrittenCount + dataLength;
            var padding = (int)(Align(layerInfoLength, 4) - layerInfoLength);
            var paddedLayerInfo = layerInfoLength + padding;
            var inTaggedBlock = _depth != 8;
            var sectionLength = inTaggedBlock
                ? LengthSize + 4 + 4 + 4 + LengthSize + paddedLayerInfo
                : LengthSize + paddedLayerInfo + 4;
            if (!_largeDocument && sectionLength > uint.MaxValue)
                throw PsdFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The layer of the image takes {sectionLength} bytes, more than the 4 GiB the lengths of a PSD file can declare. Set PsdEncoder.LargeDocument to write a PSB file."), "Layers larger than 4 GiB");

            WriteLength(prologue, sectionLength);
            if (inTaggedBlock)
            {
                WriteLength(prologue, 0);
                WriteUInt32(prologue, 0);
                prologue.Write(PsdFormat.ResourceSignature);
                prologue.Write(_depth == 16 ? "Lr16"u8 : "Lr32"u8);
                WriteLength(prologue, paddedLayerInfo);
            }
            else
            {
                WriteLength(prologue, paddedLayerInfo);
            }

            prologue.Write(record.WrittenSpan);
            segments.Add(Segment.Bytes(prologue.WrittenSpan.ToArray()));
            foreach (var plane in planes)
            {
                var compression = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(compression, (ushort)(_runLength ? PsdFormat.CompressionRle : PsdFormat.CompressionRaw));
                segments.Add(Segment.Bytes(compression));
                if (_runLength)
                {
                    segments.Add(new Segment(SegmentKind.Counts, default, plane));
                }

                segments.Add(new Segment(SegmentKind.Rows, default, plane));
            }

            // The padding of the layer information, then (for 8-bit documents) an empty global layer mask
            var epilogue = new byte[padding + (inTaggedBlock ? 0 : 4)];
            segments.Add(Segment.Bytes(epilogue));
        }

        private void WriteLength(ArrayBufferWriter<byte> writer, long value)
        {
            if (_largeDocument)
            {
                BinaryPrimitives.WriteUInt64BigEndian(writer.GetSpan(8), (ulong)value);
                writer.Advance(8);
            }
            else
            {
                WriteUInt32(writer, (uint)value);
            }
        }

        private static long Align(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

        private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

        private static void WriteUInt16(ArrayBufferWriter<byte> writer, ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(writer.GetSpan(2), value);
            writer.Advance(2);
        }

        private static void WriteUInt32(ArrayBufferWriter<byte> writer, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(writer.GetSpan(4), value);
            writer.Advance(4);
        }

        /// <summary>A part of the file: bytes, the table of the compressed lengths of a plane, or the rows of a plane.</summary>
        [StructLayout(LayoutKind.Auto)]
        private readonly record struct Segment(SegmentKind Kind, ReadOnlyMemory<byte> Data, int Plane)
        {
            public static Segment Bytes(ReadOnlyMemory<byte> data) => new(SegmentKind.Bytes, data, -1);
        }
    }
}
