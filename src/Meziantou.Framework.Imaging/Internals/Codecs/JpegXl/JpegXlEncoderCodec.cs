using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG XL encoder: a lossless Modular codestream (ISO/IEC 18181-1), bare or, when EXIF or XMP metadata are written
/// or when the codestream exceeds level 5, in the container (ISO/IEC 18181-2).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Layout: grayscale pixel formats are one gray channel, the others three color channels; a pixel format with alpha adds
/// an alpha channel of the same depth. Every frame is a regular Modular frame that covers the image and replaces it, in
/// groups of 256 samples: a frame of one group is a single section, otherwise each group is a section that holds its own
/// tree and prefix codes, so that a frame is compressed group by group.
/// </description></item>
/// <item><description>
/// Container: the signature and file type boxes, the level box when the codestream needs level 10 (samples of more than
/// 8 bits, which do not fit the 16-bit buffers of level 5; more than 2^18 samples on a side or 2^28 in the image; an ICC
/// profile of more than 4 MiB), a first part of the codestream that holds the image header and the ICC profile, the
/// <c>Exif</c> and <c>xml </c> boxes, then one part per frame. Every size is known when it is written: the
/// output is never patched and does not have to be seekable.
/// </description></item>
/// <item><description>
/// A frame header tells whether the frame is the last one, which a writer only knows when it is completed: a frame is
/// compressed when it is given and written when the next one or the completion arrives.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlEncoderCodec : ImageEncoderCodec
{
    /// <summary>The largest side of an image of level 10: 2^30 samples.</summary>
    public const int MaxDimension = 1 << 30;

    /// <summary>The largest side of an image of level 5: 2^18 samples.</summary>
    private const int MaxLevel5Dimension = 1 << 18;

    /// <summary>The largest number of pixels of an image of level 5.</summary>
    private const long MaxLevel5PixelCount = 1L << 28;

    /// <summary>The largest ICC profile of a codestream of level 5.</summary>
    private const int MaxLevel5IccProfileLength = 1 << 22;

    public static JpegXlEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.JpegXl;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    private sealed class Session : ImageEncoderSession
    {
        private const int EmitPieceLength = 64 * 1024;
        private const int GroupDimension = 128 << JpegXlHeaderWriter.GroupSizeShift;

        private readonly JpegXlEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _isAnimated;
        private readonly bool _isGray;
        private readonly bool _hasAlpha;
        private readonly int _bitsPerSample;
        private readonly bool _useContainer;
        private readonly bool _needsLevel10;
        private readonly JpegXlEncoderColor _color;
        private readonly byte[]? _icc;
        private readonly byte[]? _exif;
        private readonly byte[]? _xmp;
        private readonly List<ReadOnlyMemory<byte>> _pieces = [];
        private ImageFrame? _frame;
        private EncodedFrame? _pending;
        private EncodedFrame? _emitting;
        private Step _step;
        private int _pieceIndex;
        private int _pieceOffset;
        private bool _prefixWritten;
        private uint _partIndex;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (JpegXlEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _isAnimated = options.Capabilities.IsAnimated;
            _isGray = PixelFormats.IsGrayscale(_sourceFormat);
            _hasAlpha = PixelFormats.HasAlpha(_sourceFormat);
            _bitsPerSample = PixelFormats.GetBitsPerComponent(_sourceFormat);
            var metadata = options.Metadata;
            _icc = metadata.IccProfile?.Data.ToArray();
            _exif = metadata.Exif;
            _xmp = metadata.XmpProfile?.Data.ToArray();
            _needsLevel10 = _bitsPerSample > 8
                || _width > MaxLevel5Dimension
                || _height > MaxLevel5Dimension
                || (long)_width * _height > MaxLevel5PixelCount
                || _icc is { Length: > MaxLevel5IccProfileLength };
            _useContainer = _needsLevel10 || _exif is not null || _xmp is not null;
            if (_icc is not null)
            {
                _color = JpegXlEncoderColor.IccProfile;
            }
            else if (metadata.Cicp is { } cicp && JpegXlEncoderColor.FromCicp(cicp) is { } enumerated)
            {
                _color = enumerated;
            }
            else
            {
                _color = metadata.TransferFunction == Metadata.ColorTransferFunction.Linear ? JpegXlEncoderColor.LinearSrgb : JpegXlEncoderColor.Srgb;
            }
        }

        private enum Step
        {
            None,
            Compress,
            Complete,
            Emit,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_isAnimated)
                return;

            // Throws UnsupportedImageFeatureException for durations that cannot be stored (strict mode, or above 2^32 - 1 ms)
            var ticks = AnimationTiming.ToJpegXlDuration(frame.Metadata.Duration, _encoder.DurationRounding);
            if (ticks == 0 && Options.ExpectedFrameCount != 1)
                throw new UnsupportedImageFeatureException("A frame of a JPEG XL animation must have a duration: the format defines a frame without duration as a layer of the next frame, which would not be shown as an image.", ImageFormat.JpegXl, "JPEG XL frame without duration");
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("JPEG XL output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_isAnimated && index != 0)
                throw new InvalidOperationException("A still JPEG XL image stores a single frame.");

            _frame = frame;
            _step = Step.Compress;
        }

        public override void BeginComplete(int frameCount) => _step = Step.Complete;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Compress:
                {
                    // The previous frame is written now that it is known not to be the last one; this one waits for the next step
                    _pieces.Clear();
                    AddPrefix();
                    AddPendingFrame(isLast: false);
                    _pending = CompressFrame(_frame!);
                    _frame = null;
                    StartEmit();
                    return false;
                }

                case Step.Complete:
                    _pieces.Clear();
                    AddPrefix();
                    AddPendingFrame(isLast: true);
                    StartEmit();
                    return false;

                case Step.Emit:
                    break;

                default:
                    throw new InvalidOperationException("No JPEG XL operation was begun.");
            }

            // Bounded pieces: the writer flushes between calls
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

            _step = Step.None;
            _pieces.Clear();
            _emitting?.Dispose();
            _emitting = null;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pending?.Dispose();
                _pending = null;
                _emitting?.Dispose();
                _emitting = null;
                _pieces.Clear();
                _frame = null;
            }

            base.Dispose(disposing);
        }

        private void StartEmit()
        {
            _step = Step.Emit;
            _pieceIndex = 0;
            _pieceOffset = 0;
        }

        /// <summary>Queues what precedes the frames, once: the boxes that start a container, the image header with the ICC profile, and the metadata boxes.</summary>
        private void AddPrefix()
        {
            if (_prefixWritten)
                return;

            _prefixWritten = true;
            using var writer = new LsbBitWriter(Options.Scope);
            JpegXlHeaderWriter.WriteImageHeader(writer, Options.CanvasSize, _isGray, _bitsPerSample, _hasAlpha, Options.Metadata.Orientation, _isAnimated ? Options.Animation ?? new Metadata.AnimationMetadata() : null, _color);
            if (_icc is not null)
            {
                JpegXlIccEncoder.Write(writer, _icc, Options.Scope);
            }

            writer.ZeroPadToByte();
            var header = writer.WrittenSpan.ToArray();
            if (!_useContainer)
            {
                _pieces.Add(header);
                return;
            }

            _pieces.Add(JpegXlFormat.ContainerSignature.ToArray());
            _pieces.Add(CreateBoxHeader(JpegXlFormat.FileTypeBox, 12));
            _pieces.Add("jxl \0\0\0\0jxl "u8.ToArray());
            if (_needsLevel10)
            {
                _pieces.Add(CreateBoxHeader(JpegXlFormat.LevelBox, 1));
                _pieces.Add(new byte[] { 10 });
            }

            AddCodestreamPart(header, header.Length, isLast: false);
            if (_exif is not null)
            {
                // The payload starts with the offset of the TIFF header: none
                _pieces.Add(CreateBoxHeader(JpegXlFormat.ExifBox, 4L + _exif.Length));
                _pieces.Add(new byte[4]);
                _pieces.Add(_exif);
            }

            if (_xmp is not null)
            {
                _pieces.Add(CreateBoxHeader(JpegXlFormat.XmlBox, _xmp.Length));
                _pieces.Add(_xmp);
            }
        }

        private void AddPendingFrame(bool isLast)
        {
            if (_pending is not { } frame)
            {
                if (isLast)
                    throw new InvalidOperationException("A JPEG XL image has at least one frame.");

                return;
            }

            _pending = null;
            _emitting?.Dispose();
            _emitting = frame;

            using var writer = new LsbBitWriter(Options.Scope);
            JpegXlHeaderWriter.WriteFrameHeader(writer, _hasAlpha, _isAnimated ? frame.Duration : null, isLast);
            JpegXlHeaderWriter.WriteToc(writer, CollectionsMarshal.AsSpan(frame.SectionSizes));
            var header = writer.WrittenSpan.ToArray();
            var sections = frame.Sections.WrittenMemory;
            if (_useContainer)
            {
                AddCodestreamPart(header, (long)header.Length + sections.Length, isLast);
                _pieces.Add(sections);
            }
            else
            {
                _pieces.Add(header);
                _pieces.Add(sections);
            }
        }

        /// <summary>Queues the header of a <c>jxlp</c> box and the first bytes of its payload.</summary>
        private void AddCodestreamPart(byte[] firstBytes, long payloadLength, bool isLast)
        {
            var index = _partIndex++ | (isLast ? JpegXlFormat.LastPartFlag : 0);
            var header = new byte[JpegXlFormat.BoxHeaderLength + 4];
            WriteBoxHeader(header, JpegXlFormat.PartialCodestreamBox, 4 + payloadLength);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(JpegXlFormat.BoxHeaderLength), index);
            _pieces.Add(header);
            _pieces.Add(firstBytes);
        }

        private static byte[] CreateBoxHeader(uint type, long payloadLength)
        {
            var header = new byte[JpegXlFormat.BoxHeaderLength];
            WriteBoxHeader(header, type, payloadLength);
            return header;
        }

        private static void WriteBoxHeader(Span<byte> header, uint type, long payloadLength)
        {
            var size = JpegXlFormat.BoxHeaderLength + payloadLength;
            if (size > uint.MaxValue)
                throw new UnsupportedImageFeatureException("A box of the JPEG XL output is larger than 4 GiB.", ImageFormat.JpegXl, "JPEG XL box size");

            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
        }

        /// <summary>Compresses a frame: its sections, one after the other, and their sizes.</summary>
        private EncodedFrame CompressFrame(ImageFrame frame)
        {
            uint duration = 0;
            if (_isAnimated)
            {
                duration = AnimationTiming.ToJpegXlDuration(frame.Metadata.Duration, _encoder.DurationRounding);
            }

            var scope = Options.Scope;
            var columns = (_width + GroupDimension - 1) / GroupDimension;
            var rows = (_height + GroupDimension - 1) / GroupDimension;
            var lfDimension = GroupDimension * 8;
            var lfGroupCount = ((_width + lfDimension - 1) / lfDimension) * ((_height + lfDimension - 1) / lfDimension);
            var colorChannelCount = _isGray ? 1 : 3;
            LsbBitWriter? owned = null;
            try
            {
                var writer = owned = new LsbBitWriter(scope);
                var sizes = new List<int>();
                var total = 0;
                using var lease = frame.GetStorage().AcquireLease();

                // LfGlobal starts with the default LF dequantization
                writer.WriteBool(true);
                if (columns * rows == 1)
                {
                    // A single group: no global tree, the global Modular data are the whole image, and the frame is one section
                    writer.WriteBool(false);
                    EncodeGroup(writer, lease, 0, 0, colorChannelCount);
                    EndSection(writer, sizes, ref total);
                }
                else
                {
                    // The global Modular data have no channel: every channel is larger than a group, and each group codes its part
                    // with its own tree. They use a global tree of one leaf, so that every decoder agrees on what they hold
                    writer.WriteBool(true);
                    JpegXlModularEncoder.WriteUnusedGlobalTree(writer, scope);
                    writer.WriteBool(true);
                    writer.WriteBool(true);
                    writer.WriteU32(0, 0, 0, 1, 0, 2, 4, 18, 8);
                    EndSection(writer, sizes, ref total);

                    // The LF groups and HfGlobal are empty
                    for (var i = 0; i <= lfGroupCount; i++)
                    {
                        sizes.Add(0);
                    }

                    for (var row = 0; row < rows; row++)
                    {
                        for (var column = 0; column < columns; column++)
                        {
                            EncodeGroup(writer, lease, column * GroupDimension, row * GroupDimension, colorChannelCount);
                            EndSection(writer, sizes, ref total);
                        }
                    }
                }

                owned = null;
                return new EncodedFrame(writer, sizes, duration);
            }
            finally
            {
                owned?.Dispose();
            }
        }

        private static void EndSection(LsbBitWriter writer, List<int> sizes, ref int total)
        {
            writer.ZeroPadToByte();
            sizes.Add(writer.Length - total);
            total = writer.Length;
        }

        /// <summary>Compresses the samples of one group as a Modular sub-bitstream.</summary>
        private void EncodeGroup(LsbBitWriter writer, scoped in PixelLease lease, int x0, int y0, int colorChannelCount)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var width = Math.Min(GroupDimension, _width - x0);
            var height = Math.Min(GroupDimension, _height - y0);
            var scope = Options.Scope;
            using var image = new JpegXlModularImage(_bitsPerSample);
            var channelCount = colorChannelCount + (_hasAlpha ? 1 : 0);
            for (var i = 0; i < channelCount; i++)
            {
                image.AddChannel(width, height, 0, 0);
                image.Channels[i].Allocate(scope);
            }

            for (var y = 0; y < height; y++)
            {
                var source = lease.GetRowBytes(y0 + y);
                var first = image.Channels[0].GetRow(y);
                switch (_sourceFormat)
                {
                    case PixelFormat.Gray8:
                        for (var x = 0; x < width; x++)
                        {
                            first[x] = source[x0 + x];
                        }

                        break;

                    case PixelFormat.Gray16:
                    {
                        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(source)).Slice(x0, width);
                        for (var x = 0; x < width; x++)
                        {
                            first[x] = samples[x];
                        }

                        break;
                    }

                    case PixelFormat.Rgb24:
                    {
                        var second = image.Channels[1].GetRow(y);
                        var third = image.Channels[2].GetRow(y);
                        var pixels = source.Slice(x0 * 3, width * 3);
                        for (var x = 0; x < width; x++)
                        {
                            first[x] = pixels[x * 3];
                            second[x] = pixels[(x * 3) + 1];
                            third[x] = pixels[(x * 3) + 2];
                        }

                        break;
                    }

                    case PixelFormat.Rgba32 or PixelFormat.Bgra32:
                    {
                        var second = image.Channels[1].GetRow(y);
                        var third = image.Channels[2].GetRow(y);
                        var alpha = image.Channels[3].GetRow(y);
                        var pixels = source.Slice(x0 * 4, width * 4);
                        var redOffset = _sourceFormat == PixelFormat.Bgra32 ? 2 : 0;
                        for (var x = 0; x < width; x++)
                        {
                            first[x] = pixels[(x * 4) + redOffset];
                            second[x] = pixels[(x * 4) + 1];
                            third[x] = pixels[(x * 4) + 2 - redOffset];
                            alpha[x] = pixels[(x * 4) + 3];
                        }

                        break;
                    }

                    default:
                    {
                        var second = image.Channels[1].GetRow(y);
                        var third = image.Channels[2].GetRow(y);
                        var alpha = image.Channels[3].GetRow(y);
                        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(source)).Slice(x0 * 4, width * 4);
                        for (var x = 0; x < width; x++)
                        {
                            first[x] = samples[x * 4];
                            second[x] = samples[(x * 4) + 1];
                            third[x] = samples[(x * 4) + 2];
                            alpha[x] = samples[(x * 4) + 3];
                        }

                        break;
                    }
                }
            }

            JpegXlModularEncoder.Encode(writer, image, colorChannelCount, _encoder.Effort, scope, CancellationToken);
        }

        /// <summary>A compressed frame that waits to be written: its sections and their sizes.</summary>
        private sealed class EncodedFrame : IDisposable
        {
            public EncodedFrame(LsbBitWriter sections, List<int> sectionSizes, uint duration)
            {
                Sections = sections;
                SectionSizes = sectionSizes;
                Duration = duration;
            }

            public LsbBitWriter Sections { get; }

            public List<int> SectionSizes { get; }

            public uint Duration { get; }

            public void Dispose() => Sections.Dispose();
        }
    }
}
