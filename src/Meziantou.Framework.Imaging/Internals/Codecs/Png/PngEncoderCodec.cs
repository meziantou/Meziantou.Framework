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
        return new Session(options);
    }

    /// <summary>Throws when an APNG sequence number exceeds the PNG integer limit.</summary>
    /// <param name="sequenceNumber">The sequence number about to be written.</param>
    /// <exception cref="UnsupportedImageFeatureException">The number exceeds 2^31 - 1.</exception>
    internal static void EnsureSequenceNumber(uint sequenceNumber)
    {
        if (sequenceNumber > MaxSequenceNumber)
            throw new UnsupportedImageFeatureException("The APNG output needs more than 2^31 fcTL and fdAT chunks, beyond the largest sequence number of the format.", ImageFormat.Png, "APNG sequence number");
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
    /// Animation frames are written as full-canvas <c>SOURCE</c> frames (offset 0, canvas size, <c>APNG_DISPOSE_OP_NONE</c>,
    /// <c>APNG_BLEND_OP_SOURCE</c>): every displayed frame replaces the whole canvas, so the output never depends on the
    /// delta, disposal or blending choices of a decoded source. The session keeps no pixels between frames: memory is the
    /// datastream encoder's (a few scanlines, one chunk buffer, the deflater), whatever the number of frames.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private readonly PngEncodedLayout _layout;
        private readonly PngEncoder _encoder;
        private readonly bool _animated;
        private readonly int _expectedFrameCount;
        private readonly PngMetadataWriter _metadata;
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

            _layout = PngEncodedLayout.Get(options.PixelFormat);
            _metadata = new PngMetadataWriter(options.Metadata, _encoder.CompressionLevel, options.Scope);
            _imageData = new PngImageDataEncoder(options.Scope, options.PixelFormat, options.CanvasSize.Width, options.CanvasSize.Height, _encoder.Interlaced, _encoder.Filter, _encoder.CompressionLevel);
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
        {
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)Options.CanvasSize.Width);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)Options.CanvasSize.Height);
            header[8] = _layout.BitDepth;
            header[9] = _layout.ColorType;
            header[10] = 0; // compression method 0 (zlib)
            header[11] = 0; // filter method 0
            header[12] = _encoder.Interlaced ? (byte)1 : (byte)0;
            PngChunkWriter.Write(output, PngChunkWriter.Ihdr, header);
        }

        private void WriteAnimationControl(ImageOutputBuffer output)
        {
            // acTL: num_frames (the displayed frames; a separate poster is not counted), num_plays (0 = infinite)
            Span<byte> data = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)_expectedFrameCount);
            BinaryPrimitives.WriteUInt32BigEndian(data[4..], AnimationTiming.ToApngNumPlays(Options.Animation?.TotalPlays));
            PngChunkWriter.Write(output, PngChunkWriter.Actl, data);
        }

        private void WriteFrameControl(ImageOutputBuffer output, (ushort Numerator, ushort Denominator) delay)
        {
            EnsureSequenceNumber(_sequenceNumber);
            Span<byte> data = stackalloc byte[FrameControlLength];
            BinaryPrimitives.WriteUInt32BigEndian(data, _sequenceNumber++);
            BinaryPrimitives.WriteUInt32BigEndian(data[4..], (uint)Options.CanvasSize.Width);
            BinaryPrimitives.WriteUInt32BigEndian(data[8..], (uint)Options.CanvasSize.Height);
            BinaryPrimitives.WriteUInt32BigEndian(data[12..], 0); // x_offset
            BinaryPrimitives.WriteUInt32BigEndian(data[16..], 0); // y_offset
            BinaryPrimitives.WriteUInt16BigEndian(data[20..], delay.Numerator);
            BinaryPrimitives.WriteUInt16BigEndian(data[22..], delay.Denominator); // never 0 (0/1 for a zero delay)
            data[24] = 0; // dispose_op APNG_DISPOSE_OP_NONE: the next frame replaces the whole canvas anyway
            data[25] = 0; // blend_op APNG_BLEND_OP_SOURCE: the region (the canvas) is replaced, alpha included
            PngChunkWriter.Write(output, PngChunkWriter.Fctl, data);
        }
    }
}
