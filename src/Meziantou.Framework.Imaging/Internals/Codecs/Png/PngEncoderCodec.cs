using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The PNG encoder registration: static PNG output and animated PNG output (APNG, selected by
/// <see cref="ImageOutputCapabilities.IsAnimated"/>), both written by one streaming session.
/// </summary>
internal sealed class PngEncoderCodec : ImageEncoderCodec
{
    /// <summary>The largest APNG sequence number (the PNG four-byte integer limit, 2^31 - 1).</summary>
    public const uint MaxSequenceNumber = int.MaxValue;

    private const int FrameControlLength = 26;

    public static PngEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Png;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Encoder is PngEncoder { Optimization: PngOptimization.MinimumSize } ? new PngOptimizedEncoderSession(options) : new Session(options);
    }

    /// <summary>Throws when an APNG sequence number exceeds the PNG integer limit.</summary>
    /// <param name="sequenceNumber">The sequence number about to be written.</param>
    /// <exception cref="UnsupportedImageFeatureException">The number exceeds 2^31 - 1.</exception>
    internal static void EnsureSequenceNumber(uint sequenceNumber)
    {
        if (sequenceNumber > MaxSequenceNumber)
            throw new UnsupportedImageFeatureException("The APNG output needs more than 2^31 fcTL and fdAT chunks, beyond the largest sequence number of the format.", ImageFormat.Png, "APNG sequence number");
    }

    /// <summary>Writes <c>IHDR</c>.</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="size">The image size.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <param name="colorType">The color type.</param>
    /// <param name="interlaced">Whether Adam7 interlacing is used.</param>
    internal static void WriteHeader(ImageOutputBuffer output, Size size, byte bitDepth, byte colorType, bool interlaced)
    {
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)size.Height);
        header[8] = bitDepth;
        header[9] = colorType;
        header[10] = 0; // compression method 0 (zlib)
        header[11] = 0; // filter method 0
        header[12] = interlaced ? (byte)1 : (byte)0;
        PngChunkWriter.Write(output, PngChunkWriter.Ihdr, header);
    }

    /// <summary>
    /// Writes <c>PLTE</c> and, when an entry is not opaque, <c>tRNS</c>. The entries are sorted by alpha first, so the
    /// entries that are not opaque are the first ones and <c>tRNS</c> only stores their alpha values.
    /// </summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="colors">The entries as <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c> keys, in palette order.</param>
    internal static void WritePalette(ImageOutputBuffer output, ReadOnlySpan<uint> colors)
    {
        Span<byte> palette = stackalloc byte[3 * ExactPaletteCollector.MaxColors];
        Span<byte> transparency = stackalloc byte[ExactPaletteCollector.MaxColors];
        var transparencyCount = 0;
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            palette[3 * i] = (byte)(color >> 16);
            palette[(3 * i) + 1] = (byte)(color >> 8);
            palette[(3 * i) + 2] = (byte)color;
            var alpha = (byte)(color >> 24);
            if (alpha != byte.MaxValue)
            {
                transparency[transparencyCount++] = alpha;
            }
        }

        PngChunkWriter.Write(output, PngChunkWriter.Plte, palette[..(3 * colors.Length)]);
        if (transparencyCount > 0)
        {
            PngChunkWriter.Write(output, PngChunkWriter.Trns, transparency[..transparencyCount]);
        }
    }

    /// <summary>Writes <c>acTL</c>.</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="frameCount">The number of displayed frames (a separate poster is not counted).</param>
    /// <param name="totalPlays">The total plays of the animation settings.</param>
    internal static void WriteAnimationControl(ImageOutputBuffer output, int frameCount, int? totalPlays)
    {
        // acTL: num_frames, num_plays (0 = infinite)
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(data[4..], AnimationTiming.ToApngNumPlays(totalPlays));
        PngChunkWriter.Write(output, PngChunkWriter.Actl, data);
    }

    /// <summary>Writes <c>fcTL</c>.</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="sequenceNumber">The sequence number of the chunk.</param>
    /// <param name="region">The frame region, inside the canvas.</param>
    /// <param name="delay">The frame delay.</param>
    /// <param name="disposeOp">The <c>dispose_op</c> of the frame.</param>
    /// <param name="blendOp">The <c>blend_op</c> of the frame.</param>
    internal static void WriteFrameControl(ImageOutputBuffer output, uint sequenceNumber, Rectangle region, (ushort Numerator, ushort Denominator) delay, byte disposeOp, byte blendOp)
    {
        EnsureSequenceNumber(sequenceNumber);
        Span<byte> data = stackalloc byte[FrameControlLength];
        BinaryPrimitives.WriteUInt32BigEndian(data, sequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(data[4..], (uint)region.Width);
        BinaryPrimitives.WriteUInt32BigEndian(data[8..], (uint)region.Height);
        BinaryPrimitives.WriteUInt32BigEndian(data[12..], (uint)region.X);
        BinaryPrimitives.WriteUInt32BigEndian(data[16..], (uint)region.Y);
        BinaryPrimitives.WriteUInt16BigEndian(data[20..], delay.Numerator);
        BinaryPrimitives.WriteUInt16BigEndian(data[22..], delay.Denominator); // never 0 (0/1 for a zero delay)
        data[24] = disposeOp;
        data[25] = blendOp;
        PngChunkWriter.Write(output, PngChunkWriter.Fctl, data);
    }

    /// <summary>
    /// The encoding state of a static PNG or an APNG. The first operation (the poster, or
    /// frame zero) writes the signature, <c>IHDR</c>, <c>acTL</c> (animations only) and one metadata chunk per
    /// <see cref="Encode"/> call (<see cref="PngMetadataWriter"/>); each animation frame then writes its <c>fcTL</c> and bands
    /// of image data (<see cref="PngImageDataEncoder"/>, one independent datastream per image): <c>IDAT</c> for the separate
    /// poster or for frame zero without a poster, <c>fdAT</c> otherwise. Completion writes <c>IEND</c>. Everything that can
    /// fail without pixels (encoder settings, metadata sizes, working buffers) fails on creation, before any output; frame
    /// durations fail in <see cref="ValidateFrame"/>, before anything of the frame is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Indexed output (<see cref="PngPixelLayout.Indexed"/>) has one palette for the whole file, written after the metadata
    /// chunks (<c>PLTE</c>, then <c>tRNS</c> when an entry is not opaque) and before the first image, so its colors must be
    /// known before the header: <see cref="ValidateFrame"/> collects the colors of every image it is given until the first
    /// one is begun (every frame and the poster for an eager save, which validates them all before it writes; the first
    /// image for a sequential writer), and afterward only checks that a frame uses colors of the palette. A frame that
    /// cannot be stored exactly (too many colors, a color missing from the written palette, a 16-bit sample that 8 bits
    /// cannot represent) is rejected there and leaves the palette as it was. The bit depth is the smallest one that
    /// addresses the palette.
    /// </para>
    /// <para>
    /// Animation frames are written as full-canvas <c>SOURCE</c> frames (offset 0, canvas size, <c>APNG_DISPOSE_OP_NONE</c>,
    /// <c>APNG_BLEND_OP_SOURCE</c>): every displayed frame replaces the whole canvas, so the output never depends on the
    /// delta, disposal or blending choices of a decoded source. The session keeps no pixels between frames: memory is the
    /// datastream encoder's (a few scanlines, one chunk buffer, the deflater) and the palette of indexed output (256 colors
    /// and one row of color keys), whatever the number of frames.
    /// </para>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private readonly PngEncoder _encoder;
        private readonly bool _animated;
        private readonly int _expectedFrameCount;
        private readonly PngMetadataWriter _metadata;
        private PngEncodedLayout _layout;
        private PngIndexedRows? _indexedRows;
        private PngImageDataEncoder? _imageData;
        private ImageFrame? _frame;
        private Step _step;
        private int _metadataIndex;
        private bool _headerWritten;
        private bool _posterWritten;
        private bool _frameData;
        private bool _lastImage;
        private (ushort Numerator, ushort Denominator)? _delay;
        private uint _sequenceNumber;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (PngEncoder)options.Encoder;
            _animated = options.Capabilities.IsAnimated;

            // The writer guarantees a known count for PNG outputs (acTL precedes the image data; seeking is never required)
            _expectedFrameCount = options.ExpectedFrameCount ?? throw new ArgumentException("PNG output requires an expected frame count.", nameof(options));
            if (!_animated && _expectedFrameCount != 1)
                throw new ArgumentException("A static PNG has a single frame.", nameof(options));

            // The indexed layout is replaced once the palette is final: its bit depth follows the number of colors
            _layout = PngEncodedLayout.Get(options.PixelFormat);
            _metadata = new PngMetadataWriter(options.Metadata, _encoder.CompressionLevel, options.Scope);
            try
            {
                if (_encoder.PixelLayout == PngPixelLayout.Indexed)
                {
                    _indexedRows = new PngIndexedRows(options.Scope, options.PixelFormat, options.CanvasSize.Width);
                }

                _imageData = new PngImageDataEncoder(options.Scope, options.PixelFormat, options.CanvasSize.Width, options.CanvasSize.Height, _encoder.Interlaced, _encoder.Filter, _encoder.CompressionLevel, _indexedRows);
            }
            catch
            {
                _indexedRows?.Dispose();
                throw;
            }
        }

        private enum Step
        {
            None,
            Header,
            Metadata,
            FrameControl,
            ImageData,
            Trailer,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // An unrepresentable duration fails before anything of the frame is written (the writer stays usable)
            if (_animated && !isPoster)
            {
                _ = AnimationTiming.ToApngDelay(frame.Metadata.Duration, _encoder.DurationRounding);
            }

            if (_indexedRows is { } indexedRows)
            {
                ValidateColors(frame, indexedRows);
            }
        }

        public override void BeginPosterFrame(ImageFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_animated)
                throw new InvalidOperationException("A static PNG has no poster frame.");

            if (_posterWritten || _headerWritten)
                throw new InvalidOperationException("The APNG poster frame is written once, before frame zero.");

            // The separate poster is the IDAT image without a preceding fcTL: shown by non-APNG decoders, not part of the animation
            _posterWritten = true;
            Begin(frame, delay: null, frameData: false, lastImage: false);
        }

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index < 0 || index >= _expectedFrameCount)
                throw new InvalidOperationException(_animated ? "The frame index exceeds the expected frame count." : "A static PNG has a single frame.");

            var lastImage = index == _expectedFrameCount - 1;
            if (!_animated)
            {
                Begin(frame, delay: null, frameData: false, lastImage);
                return;
            }

            // Without a separate poster, frame zero is the IDAT image (its fcTL precedes IDAT); later frames use fdAT
            var delay = AnimationTiming.ToApngDelay(frame.Metadata.Duration, _encoder.DurationRounding);
            Begin(frame, delay, frameData: index > 0 || _posterWritten, lastImage);
        }

        public override void BeginComplete(int frameCount)
        {
            if (frameCount != _expectedFrameCount)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{frameCount} frame(s) were written but the output declares {_expectedFrameCount}."));

            _step = Step.Trailer;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    if (_indexedRows is { } indexedRows)
                    {
                        // Every color known at this point is in the palette; later frames must use them
                        indexedRows.Freeze();
                        _layout = PngEncodedLayout.GetIndexed(Options.PixelFormat, PngIndexedRows.GetBitDepth(indexedRows.Count));
                        _imageData!.SetIndexedLayout(_layout);
                    }

                    PngChunkWriter.WriteSignature(output);
                    WriteHeader(output);
                    if (_animated)
                    {
                        WriteAnimationControl(output);
                    }

                    _headerWritten = true;
                    _step = Step.Metadata;
                    return false;

                case Step.Metadata:
                    if (_metadataIndex < _metadata.Count)
                    {
                        _metadata.Write(_metadataIndex++, output);
                        return false;
                    }

                    // The palette follows every metadata chunk (iCCP must precede PLTE) and precedes the first image
                    if (_indexedRows is not null)
                    {
                        PngEncoderCodec.WritePalette(output, _indexedRows.Colors);
                    }

                    _step = Step.FrameControl;
                    goto case Step.FrameControl;

                case Step.FrameControl:
                    if (_delay is { } delay)
                    {
                        WriteFrameControl(output, delay);
                    }

                    if (_frameData)
                    {
                        EnsureSequenceNumber(_sequenceNumber);
                        _imageData!.StartFrameData(_sequenceNumber);
                    }
                    else
                    {
                        _imageData!.Start();
                    }

                    _step = Step.ImageData;
                    goto case Step.ImageData;

                case Step.ImageData:
                    if (!_imageData!.Encode(_frame!.GetStorage(), output))
                        return false;

                    if (_frameData)
                    {
                        _sequenceNumber = _imageData.NextSequenceNumber;
                    }

                    // The frame is no longer referenced once its operation completes; the buffers go with the last image
                    _frame = null;
                    _step = Step.None;
                    if (_lastImage)
                    {
                        _imageData.Dispose();
                        _imageData = null;
                    }

                    return true;

                case Step.Trailer:
                    PngChunkWriter.Write(output, PngChunkWriter.Iend, []);
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No PNG encoding operation is in progress.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            _frame = null;
            _imageData?.Dispose();
            _imageData = null;
            _indexedRows?.Dispose();
            _indexedRows = null;
            base.Dispose(disposing);
        }

        private void Begin(ImageFrame frame, (ushort Numerator, ushort Denominator)? delay, bool frameData, bool lastImage)
        {
            if (_imageData is null)
                throw new InvalidOperationException("Every image of the PNG output was already written.");

            _frame = frame;
            _delay = delay;
            _frameData = frameData;
            _lastImage = lastImage;
            _step = _headerWritten ? Step.FrameControl : Step.Header;
        }

        private void WriteHeader(ImageOutputBuffer output)
            => PngEncoderCodec.WriteHeader(output, Options.CanvasSize, _layout.BitDepth, _layout.ColorType, _encoder.Interlaced);

        /// <summary>
        /// Adds the colors of a frame to the palette, or checks them against it once the palette is written. A frame that
        /// is rejected leaves the palette unchanged; a frame may be validated more than once.
        /// </summary>
        private void ValidateColors(ImageFrame frame, PngIndexedRows indexedRows)
        {
            var isFinal = indexedRows.IsFrozen;
            var count = indexedRows.Count;
            try
            {
                using var lease = frame.GetStorage().AcquireLease();
                for (var y = 0; y < lease.Height; y++)
                {
                    var row = lease.GetRowBytes(y);
                    var status = isFinal ? indexedRows.Check(row, out var x) : indexedRows.Collect(row, out x);
                    if (status != PngIndexedRowStatus.Ok)
                        throw CreateColorException(status, x, y);
                }
            }
            catch
            {
                indexedRows.Rollback(count);
                throw;
            }
        }

        private UnsupportedImageFeatureException CreateColorException(PngIndexedRowStatus status, int x, int y) => status switch
        {
            PngIndexedRowStatus.NotEightBit => new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"A PNG palette stores 8-bit samples, and the {Options.PixelFormat} pixel ({x}, {y}) has a 16-bit sample that 8 bits cannot represent exactly. PngPixelLayout.Indexed never reduces precision: convert the image explicitly with CloneAs, or select PngPixelLayout.Auto."), ImageFormat.Png, "Bit depth reduction"),
            PngIndexedRowStatus.MissingColor => new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The pixel ({x}, {y}) has a color that is not in the palette of the PNG file. A PNG file has one palette, written before its first image with the colors known at that point: the colors of every frame for an eager save, the colors of the first image for a sequential writer. PngPixelLayout.Indexed never reduces colors: write a first image that holds every color of the animation, save the whole image at once, or select PngPixelLayout.Auto."), ImageFormat.Png, "Color reduction"),
            _ => new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The image has more than {ExactPaletteCollector.MaxColors} distinct colors, the most a PNG palette can hold; the pixel ({x}, {y}) has one more. A color is a combination of red, green, blue and alpha samples (fully transparent pixels of different colors are different entries), and the frames and the poster of an animation share one palette. PngPixelLayout.Indexed never reduces colors: reduce the colors of the image first, or select PngPixelLayout.Auto."), ImageFormat.Png, "Color reduction"),
        };

        private void WriteAnimationControl(ImageOutputBuffer output)
            => PngEncoderCodec.WriteAnimationControl(output, _expectedFrameCount, Options.Animation?.TotalPlays);

        private void WriteFrameControl(ImageOutputBuffer output, (ushort Numerator, ushort Denominator) delay)
        {
            // Full-canvas SOURCE frames: APNG_DISPOSE_OP_NONE (the next frame replaces the whole canvas anyway) and
            // APNG_BLEND_OP_SOURCE (the region, the canvas, is replaced, alpha included)
            var region = new Rectangle(0, 0, Options.CanvasSize.Width, Options.CanvasSize.Height);
            PngEncoderCodec.WriteFrameControl(output, _sequenceNumber++, region, delay, PngFrameControl.DisposeNone, PngFrameControl.BlendSource);
        }
    }
}
