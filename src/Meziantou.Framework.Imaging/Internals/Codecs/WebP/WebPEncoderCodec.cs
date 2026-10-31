using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The WebP encoder registration (lossless and lossy): still images in the simple or extended layout and animations of
/// full-canvas frames (or of the regions that change, with <see cref="WebPEncoder.OptimizeFrames"/>), written by one session.
/// </summary>
internal sealed class WebPEncoderCodec : ImageEncoderCodec
{
    /// <summary>The largest RIFF size field (2^32 - 10: the whole file is at most 4 GiB - 2 bytes).</summary>
    public const long MaxRiffSize = uint.MaxValue - 9;

    public static WebPEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.WebP;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one WebP output. Each frame is converted to 8-bit RGBA and compressed in its first
    /// <see cref="Encode"/> call (a VP8L or VP8 bitstream, plus an <c>ALPH</c> chunk for lossy frames with transparency), then
    /// its chunks are emitted in bounded pieces. A still image is written at once with exact sizes (simple layout without
    /// metadata or lossy alpha, otherwise <c>VP8X</c>, <c>ICCP</c>, the image, <c>EXIF</c>, <c>XMP </c>). An animation writes
    /// the RIFF header, <c>VP8X</c>, <c>ICCP</c> and <c>ANIM</c> before its first frame, one <c>ANMF</c> per frame, and the
    /// metadata chunks at completion, then asks the writer to patch the RIFF size and the <c>VP8X</c> alpha flag
    /// (<see cref="ImageOutputBuffer.AddPatch"/>; the destination is seekable, see <see cref="ImageOutputCapabilities.RequiresSeekableOutput"/>).
    /// </summary>
    /// <remarks>
    /// With <see cref="WebPEncoder.OptimizeFrames"/>, the session keeps the displayed frame (<see cref="AnimationFrameDelta"/>):
    /// each frame after the first one is compared with it, which chooses the disposal of the previous frame (patched into its
    /// <c>ANMF</c> flags), the region (at even coordinates) and the blending; the region is copied out, its unchanged
    /// pixels transparent when it is blended, and compressed instead of the frame.
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const int EmitPieceLength = 64 * 1024;
        private const int VP8XFlagsOffset = WebPStructureParser.RiffHeaderLength + WebPStructureParser.ChunkHeaderLength;

        private readonly WebPEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _isAnimated;
        private readonly bool _lossless;
        private readonly byte[]? _icc;
        private readonly byte[]? _exif;
        private readonly byte[]? _xmp;
        private readonly List<ReadOnlyMemory<byte>> _pieces = [];
        private PooledBuffer? _pixels;
        private AnimationFrameDelta? _delta;
        private long _previousFlagsOffset;
        private byte _previousFlags;
        private EncodedFrame? _encoded;
        private ImageFrame? _frame;
        private Step _step;
        private int _pieceIndex;
        private int _pieceOffset;
        private bool _headerWritten;
        private bool _anyAlpha;
        private bool _completing;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (WebPEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !_encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"WebP stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set WebPEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.WebP, "Bit depth reduction");

            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _isAnimated = options.Capabilities.IsAnimated;
            _lossless = _encoder.Compression == WebPCompression.Lossless;
            var metadata = options.Metadata;
            _icc = metadata.IccProfile?.Data.ToArray();
            _exif = metadata.Exif;
            _xmp = metadata.XmpProfile?.Data.ToArray();

            // The RGBA image of one frame, allocated before any output so that limit failures leave no output
            var bytes = (long)_width * _height * 4;
            if (bytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Scope.Limits);

            _pixels = options.Scope.Rent((int)bytes, AllocationKind.Temporary, clear: false);
            if (_isAnimated && _encoder.OptimizeFrames)
            {
                try
                {
                    // A region replaces the canvas ("do not blend") or is blended over it, at even coordinates; the
                    // blending of the format rounds translucent pixels, so only opaque pixels are blended
                    _delta = new AnimationFrameDelta(options.Scope, _width, _height, new AnimationDeltaRules { BytesPerPixel = 4, SourceBlend = true, OverBlend = true, EvenOffsets = true });
                }
                catch
                {
                    _pixels.Dispose();
                    _pixels = null;
                    throw;
                }
            }
        }

        private enum Step
        {
            None,
            Compress,
            Emit,
        }

        private bool NeedsExtendedLayout(bool hasAlpha) => _isAnimated || _icc is not null || _exif is not null || _xmp is not null || (!_lossless && hasAlpha);

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (_isAnimated)
            {
                // Throws UnsupportedImageFeatureException for unrepresentable durations (strict mode, or above 16,777.215 s)
                _ = AnimationTiming.ToWebPDuration(frame.Metadata.Duration, _encoder.DurationRounding);
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("WebP output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_isAnimated && index != 0)
                throw new InvalidOperationException("A still WebP image stores a single frame.");

            _frame = frame;
            _step = Step.Compress;
        }

        public override void BeginComplete(int frameCount)
        {
            _completing = true;
            _pieces.Clear();
            if (_isAnimated)
            {
                // Trailing metadata, then the sizes known only now
                AddMetadataChunks(trailing: true);
            }

            StartEmit();
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (_step == Step.Compress)
            {
                CompressFrame(output);
                StartEmit();
                return false;
            }

            if (_step != Step.Emit)
                throw new InvalidOperationException("No WebP operation was begun.");

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
            _encoded?.Dispose();
            _encoded = null;
            if (_completing && _isAnimated && _headerWritten)
            {
                // Completion: the RIFF size covers everything after its field, and the VP8X alpha flag every frame
                var riffSize = output.TotalBytes - 8;
                if (riffSize > MaxRiffSize)
                    throw TooLarge(output.TotalBytes);

                Span<byte> size = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)riffSize);
                output.AddPatch(4, size);
                output.AddPatch(VP8XFlagsOffset, [GetExtendedFlags(_anyAlpha)]);
            }

            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _encoded?.Dispose();
                _encoded = null;
                _pixels?.Dispose();
                _pixels = null;
                _delta?.Dispose();
                _delta = null;
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

        private void CompressFrame(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var rgba = _pixels!.RawBuffer.AsSpan(0, _width * _height * 4);
            var hasAlpha = LoadPixels(frame, rgba);
            var duration = _isAnimated ? AnimationTiming.ToWebPDuration(frame.Metadata.Duration, _encoder.DurationRounding) : 0;
            _frame = null;

            _encoded?.Dispose();
            _encoded = null;
            var region = new Rectangle(0, 0, _width, _height);
            var blend = false;
            if (_delta is not null)
            {
                (region, blend, hasAlpha) = CompressRegion(output, rgba);
            }
            else
            {
                _encoded = Compress(rgba, _width, _height, hasAlpha);
            }

            _anyAlpha |= hasAlpha;
            _pieces.Clear();
            var imageChunks = _encoded!.GetChunks();
            long imageLength = 0;
            foreach (var chunk in imageChunks)
            {
                imageLength += chunk.Length;
            }

            if (_isAnimated)
            {
                if (!_headerWritten)
                {
                    _headerWritten = true;
                    AddChunkPrefix(riffSize: 0, hasAlpha: true);
                }

                // ANMF: the region (by default the full canvas at (0, 0), which replaces the canvas), its duration, "do not
                // blend" or alpha blending, and "do not dispose" (patched by the next frame of OptimizeFrames)
                var header = new byte[WebPStructureParser.ChunkHeaderLength + WebPStructureParser.FrameHeaderLength];
                WriteChunkHeader(header, "ANMF"u8, WebPStructureParser.FrameHeaderLength + imageLength);
                var data = header.AsSpan(WebPStructureParser.ChunkHeaderLength);
                WriteUInt24(data, region.X / 2);
                WriteUInt24(data[3..], region.Y / 2);
                WriteUInt24(data[6..], region.Width - 1);
                WriteUInt24(data[9..], region.Height - 1);
                WriteUInt24(data[12..], duration);
                data[15] = blend ? (byte)0 : (byte)0x02;
                var offset = output.TotalBytes;
                foreach (var piece in _pieces)
                {
                    offset += piece.Length;
                }

                if (offset + header.Length + imageLength + MetadataLength(trailing: true) - 8 > MaxRiffSize)
                    throw TooLarge(offset + header.Length + imageLength);

                (_previousFlagsOffset, _previousFlags) = (offset + WebPStructureParser.ChunkHeaderLength + 15, data[15]);
                _pieces.Add(header);
                _pieces.AddRange(imageChunks);
                return;
            }

            // Still image: every size is known now
            var extended = NeedsExtendedLayout(hasAlpha);
            var riffSize = 4L + imageLength + (extended ? WebPStructureParser.ChunkHeaderLength + WebPStructureParser.ExtendedHeaderLength + MetadataLength(trailing: false) + MetadataLength(trailing: true) : 0);
            if (riffSize > MaxRiffSize)
                throw TooLarge(riffSize + 8);

            if (extended)
            {
                AddChunkPrefix(riffSize, hasAlpha);
            }
            else
            {
                var riff = new byte[WebPStructureParser.RiffHeaderLength];
                WriteRiffHeader(riff, riffSize);
                _pieces.Add(riff);
            }

            _pieces.AddRange(imageChunks);
            if (extended)
            {
                AddMetadataChunks(trailing: true);
            }
        }

        private EncodedFrame Compress(Span<byte> rgba, int width, int height, bool hasAlpha) => _lossless
            ? EncodedFrame.EncodeLossless(Options.Scope, rgba, width, height, _encoder.Effort, _encoder.NearLossless, CancellationToken)
            : EncodedFrame.EncodeLossy(Options.Scope, rgba, width, height, hasAlpha, _encoder.Quality, _encoder.AlphaQuality, _encoder.Effort, CancellationToken);

        /// <summary>
        /// Plans a frame of <see cref="WebPEncoder.OptimizeFrames"/> (the first one covers the canvas), patches the disposal of
        /// the previous frame, copies the region out of the frame (its unchanged pixels transparent when it is blended),
        /// commits the frame as the displayed frame, and compresses the region.
        /// </summary>
        /// <returns>The region, whether it is blended, and whether it has pixels that are not opaque.</returns>
        private (Rectangle Region, bool Blend, bool HasAlpha) CompressRegion(ImageOutputBuffer output, ReadOnlySpan<byte> rgba)
        {
            var delta = _delta!;
            var rowLength = _width * 4;
            var region = new Rectangle(0, 0, _width, _height);
            var blend = false;
            if (delta.CommittedFrames > 0)
            {
                delta.BeginCompare();
                for (var y = 0; y < _height; y++)
                {
                    if ((y & 63) == 0)
                    {
                        CancellationToken.ThrowIfCancellationRequested();
                    }

                    delta.CompareRow(y, rgba.Slice(y * rowLength, rowLength));
                }

                // Always planned: a region can replace the canvas
                _ = delta.TryChoose(out var plan);
                region = plan.Region;
                blend = plan.Blend == AnimationBlend.Over;
                if (plan.PreviousDisposal == AnimationDisposal.ClearToTransparent)
                {
                    output.AddPatch(_previousFlagsOffset, [(byte)(_previousFlags | 0x01)]);
                }
            }

            var bytes = (long)region.Width * region.Height * 4;
            if (bytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(Options.Scope.Limits);

            using var pixels = Options.Scope.Rent((int)bytes, AllocationKind.Temporary, clear: false);
            var regionPixels = pixels.RawBuffer.AsSpan(0, (int)bytes);
            var hasAlpha = false;
            for (var y = 0; y < region.Height; y++)
            {
                var source = rgba.Slice(((region.Y + y) * rowLength) + (region.X * 4), region.Width * 4);
                var destination = regionPixels.Slice(y * region.Width * 4, region.Width * 4);
                source.CopyTo(destination);
                if (blend)
                {
                    // Blending leaves the canvas under a fully transparent pixel: the unchanged pixels become transparent
                    // (lossy output keeps their color, so that the compression of their neighbors does not see an edge)
                    var row = rgba.Slice((region.Y + y) * rowLength, rowLength);
                    var canvas = delta.GetPlannedCanvasRow(region.Y + y, out var clearedStart, out var clearedEnd);
                    for (var x = 0; x < region.Width; x++)
                    {
                        if (delta.IsUnchanged(row, region.X + x, canvas, clearedStart, clearedEnd))
                        {
                            var pixel = destination.Slice(x * 4, 4);
                            if (_lossless)
                            {
                                pixel.Clear();
                            }
                            else
                            {
                                pixel[3] = 0;
                            }
                        }
                    }
                }

                for (var x = 3; x < destination.Length && !hasAlpha; x += 4)
                {
                    hasAlpha = destination[x] != byte.MaxValue;
                }
            }

            delta.BeginCommit(region);
            for (var y = 0; y < _height; y++)
            {
                delta.CommitRow(y, rgba.Slice(y * rowLength, rowLength));
            }

            delta.EndCommit();
            _encoded = Compress(regionPixels, region.Width, region.Height, hasAlpha);
            return (region, blend, hasAlpha);
        }

        /// <summary>Converts the frame to 8-bit RGBA (gray replicated, 16-bit reduced to the nearest 8-bit value), optionally clearing the color of transparent pixels.</summary>
        /// <returns>Whether at least one pixel is not fully opaque.</returns>
        private bool LoadPixels(ImageFrame frame, Span<byte> rgba)
        {
            var hasAlpha = false;
            var rowLength = _width * 4;
            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < _height; y++)
            {
                if ((y & 63) == 0)
                {
                    CancellationToken.ThrowIfCancellationRequested();
                }

                var row = rgba.Slice(y * rowLength, rowLength);
                var source = lease.GetRowBytes(y);
                if (_sourceFormat == PixelFormat.Rgba32)
                {
                    source.CopyTo(row);
                }
                else
                {
                    PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Rgba32, row, background: null, ImageFormat.WebP);
                }

                if (!PixelFormats.HasAlpha(_sourceFormat))
                    continue;

                for (var x = 3; x < row.Length; x += 4)
                {
                    if (row[x] != byte.MaxValue)
                    {
                        hasAlpha = true;
                        if (row[x] == 0 && _encoder.ClearTransparentColors)
                        {
                            row.Slice(x - 3, 3).Clear();
                        }
                    }
                }
            }

            return hasAlpha;
        }

        /// <summary>Adds the RIFF header, <c>VP8X</c>, <c>ICCP</c> and (animations) <c>ANIM</c>.</summary>
        private void AddChunkPrefix(long riffSize, bool hasAlpha)
        {
            var prefix = new byte[WebPStructureParser.RiffHeaderLength + WebPStructureParser.ChunkHeaderLength + WebPStructureParser.ExtendedHeaderLength];
            WriteRiffHeader(prefix, riffSize);
            var vp8x = prefix.AsSpan(WebPStructureParser.RiffHeaderLength);
            WriteChunkHeader(vp8x, "VP8X"u8, WebPStructureParser.ExtendedHeaderLength);
            var data = vp8x[WebPStructureParser.ChunkHeaderLength..];
            data[0] = GetExtendedFlags(hasAlpha);
            WriteUInt24(data[4..], _width - 1);
            WriteUInt24(data[7..], _height - 1);
            _pieces.Add(prefix);
            AddMetadataChunks(trailing: false);
            if (_isAnimated)
            {
                // ANIM: transparent black background hint (never painted by this library's decoder), loop count
                var anim = new byte[WebPStructureParser.ChunkHeaderLength + 6];
                WriteChunkHeader(anim, "ANIM"u8, 6);
                BinaryPrimitives.WriteUInt16LittleEndian(anim.AsSpan(12), AnimationTiming.ToWebPLoopCount(Options.Animation?.TotalPlays));
                _pieces.Add(anim);
            }
        }

        private byte GetExtendedFlags(bool hasAlpha)
        {
            byte flags = 0;
            if (_isAnimated)
            {
                flags |= WebPStructureParser.AnimationFlag;
            }

            if (hasAlpha)
            {
                flags |= WebPStructureParser.AlphaFlag;
            }

            if (_icc is not null)
            {
                flags |= WebPStructureParser.IccFlag;
            }

            if (_exif is not null)
            {
                flags |= WebPStructureParser.ExifFlag;
            }

            if (_xmp is not null)
            {
                flags |= WebPStructureParser.XmpFlag;
            }

            return flags;
        }

        /// <summary>The leading metadata chunk (<c>ICCP</c>) or the trailing ones (<c>EXIF</c>, <c>XMP </c>).</summary>
        private void AddMetadataChunks(bool trailing)
        {
            if (!trailing)
            {
                AddChunk("ICCP"u8, _icc);
                return;
            }

            AddChunk("EXIF"u8, _exif);
            AddChunk("XMP "u8, _xmp);
        }

        private long MetadataLength(bool trailing)
        {
            return trailing ? ChunkLength(_exif) + ChunkLength(_xmp) : ChunkLength(_icc);

            static long ChunkLength(byte[]? payload) => payload is null ? 0 : WebPStructureParser.ChunkHeaderLength + payload.Length + (payload.Length & 1);
        }

        private void AddChunk(ReadOnlySpan<byte> fourCC, byte[]? payload)
        {
            if (payload is null)
                return;

            var header = new byte[WebPStructureParser.ChunkHeaderLength];
            WriteChunkHeader(header, fourCC, payload.Length);
            _pieces.Add(header);
            _pieces.Add(payload);
            if ((payload.Length & 1) != 0)
            {
                _pieces.Add(new byte[1]);
            }
        }

        private static void WriteRiffHeader(Span<byte> destination, long riffSize)
        {
            "RIFF"u8.CopyTo(destination);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], (uint)riffSize);
            "WEBP"u8.CopyTo(destination[8..]);
        }

        internal static void WriteChunkHeader(Span<byte> destination, ReadOnlySpan<byte> fourCC, long size)
        {
            fourCC.CopyTo(destination);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], checked((uint)size));
        }

        private static void WriteUInt24(Span<byte> destination, int value)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
        }

        private static UnsupportedImageFeatureException TooLarge(long size)
            => new(string.Create(CultureInfo.InvariantCulture, $"The WebP output would be {size} bytes; a WebP file is at most 4 GiB - 2 bytes (RIFF size field)."), ImageFormat.WebP, "WebP file size");
    }

    /// <summary>The compressed chunks of one frame: the bitstream (and the alpha data of a lossy frame).</summary>
    private sealed class EncodedFrame : IDisposable
    {
        private LsbBitWriter? _alpha;
        private byte _alphaHeader;
        private LsbBitWriter? _lossless;
        private PooledPayloadWriter? _lossy;

        public static EncodedFrame EncodeLossless(AllocationScope scope, Span<byte> rgba, int width, int height, int effort, int nearLossless, CancellationToken cancellationToken)
        {
            WebPNearLossless.Apply(scope, rgba, width, height, nearLossless, cancellationToken);

            // RGBA bytes become 0xAARRGGBB values in place
            var argb = unsafe(MemoryMarshal.Cast<byte, uint>(rgba));
            for (var i = 0; i < argb.Length; i++)
            {
                var pixel = rgba.Slice(i * 4, 4);
                argb[i] = ((uint)pixel[3] << 24) | ((uint)pixel[0] << 16) | ((uint)pixel[1] << 8) | pixel[2];
            }

            var result = new EncodedFrame();
            try
            {
                result._lossless = effort < 9 ? EncodeStream(scope, argb, width, height, effort, configuration: null, cancellationToken) : EncodeSmallestStream(scope, argb, width, height, cancellationToken);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Effort 9: the size estimates of the configurations are not exact, so the two estimated smallest are both encoded
        /// and the smaller stream is kept.
        /// </summary>
        private static LsbBitWriter EncodeSmallestStream(AllocationScope scope, Span<uint> argb, int width, int height, CancellationToken cancellationToken)
        {
            var ranked = Vp8LEncoder.RankConfigurations(scope, argb, width, height, effort: 9, cancellationToken);
            if (ranked.Length == 1)
                return EncodeStream(scope, argb, width, height, effort: 9, ranked[0], cancellationToken);

            using var copy = scope.Rent(argb.Length * sizeof(uint), AllocationKind.Temporary, clear: false);
            var second = unsafe(MemoryMarshal.Cast<byte, uint>(copy.RawBuffer.AsSpan(0, argb.Length * sizeof(uint))));
            argb.CopyTo(second);
            var best = EncodeStream(scope, argb, width, height, effort: 9, ranked[0], cancellationToken);
            LsbBitWriter other;
            try
            {
                other = EncodeStream(scope, second, width, height, effort: 9, ranked[1], cancellationToken);
            }
            catch
            {
                best.Dispose();
                throw;
            }

            if (other.Length < best.Length)
            {
                best.Dispose();
                return other;
            }

            other.Dispose();
            return best;
        }

        private static LsbBitWriter EncodeStream(AllocationScope scope, Span<uint> argb, int width, int height, int effort, Vp8LConfiguration? configuration, CancellationToken cancellationToken)
        {
            var writer = new LsbBitWriter(scope);
            try
            {
                Vp8LEncoder.Encode(scope, writer, argb, width, height, effort, cancellationToken, configuration);
                writer.Finish();
                return writer;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        public static EncodedFrame EncodeLossy(AllocationScope scope, Span<byte> rgba, int width, int height, bool hasAlpha, int quality, int alphaQuality, int effort, CancellationToken cancellationToken)
        {
            var result = new EncodedFrame();
            try
            {
                // The alpha comes first: the color encoder then sees the alpha the decoder will see
                if (hasAlpha)
                {
                    (result._alpha, result._alphaHeader) = EncodeAlpha(scope, rgba, width, height, alphaQuality, effort, cancellationToken);
                }

                result._lossy = Vp8Encoder.Encode(scope, rgba, width, height, quality, effort, cancellationToken);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Encodes the alpha values; below an alpha quality of 100, also with reduced levels, kept (and left in
        /// <paramref name="rgba"/>) only when smaller: fewer levels can break ramps that compress well.
        /// </summary>
        private static (LsbBitWriter Bitstream, byte Header) EncodeAlpha(AllocationScope scope, Span<byte> rgba, int width, int height, int alphaQuality, int effort, CancellationToken cancellationToken)
        {
            var exact = WebPAlphaEncoder.Encode(scope, rgba, width, height, effort, levelsReduced: false, cancellationToken);
            if (alphaQuality >= 100)
                return exact;

            var count = width * height;
            using var saved = scope.Rent(count, AllocationKind.Temporary, clear: false);
            var original = saved.RawBuffer.AsSpan(0, count);
            for (var i = 0; i < count; i++)
            {
                original[i] = rgba[(i * 4) + 3];
            }

            (LsbBitWriter Bitstream, byte Header) reduced;
            try
            {
                if (!WebPAlphaEncoder.QuantizeLevels(rgba, alphaQuality))
                    return exact;

                reduced = WebPAlphaEncoder.Encode(scope, rgba, width, height, effort, levelsReduced: true, cancellationToken);
            }
            catch
            {
                exact.Bitstream.Dispose();
                throw;
            }

            if (reduced.Bitstream.Length < exact.Bitstream.Length)
            {
                exact.Bitstream.Dispose();
                return reduced;
            }

            reduced.Bitstream.Dispose();
            for (var i = 0; i < count; i++)
            {
                rgba[(i * 4) + 3] = original[i];
            }

            return exact;
        }

        /// <summary>Gets the chunks (headers, payloads and padding) in file order.</summary>
        public List<ReadOnlyMemory<byte>> GetChunks()
        {
            var chunks = new List<ReadOnlyMemory<byte>>();
            if (_alpha is not null)
            {
                var payload = _alpha.WrittenMemory;
                var header = new byte[WebPStructureParser.ChunkHeaderLength + 1];
                Session.WriteChunkHeader(header, "ALPH"u8, payload.Length + 1);
                header[^1] = _alphaHeader;
                chunks.Add(header);
                chunks.Add(payload);
                AddPadding(chunks, payload.Length + 1);
            }

            if (_lossless is not null)
            {
                var payload = _lossless.WrittenMemory;
                var header = new byte[WebPStructureParser.ChunkHeaderLength];
                Session.WriteChunkHeader(header, "VP8L"u8, payload.Length);
                chunks.Add(header);
                chunks.Add(payload);
                AddPadding(chunks, payload.Length);
            }
            else
            {
                var payload = _lossy!.WrittenMemory;
                var header = new byte[WebPStructureParser.ChunkHeaderLength];
                Session.WriteChunkHeader(header, "VP8 "u8, payload.Length);
                chunks.Add(header);
                chunks.Add(payload);
                AddPadding(chunks, payload.Length);
            }

            return chunks;
        }

        public void Dispose()
        {
            _alpha?.Dispose();
            _alpha = null;
            _lossless?.Dispose();
            _lossless = null;
            _lossy?.Dispose();
            _lossy = null;
        }

        private static void AddPadding(List<ReadOnlyMemory<byte>> chunks, int length)
        {
            if ((length & 1) != 0)
            {
                chunks.Add(new byte[1]);
            }
        }
    }
}
