using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The AVIF encoder registration: still images and image sequences, coded by the AV1 encoder of the library.</summary>
internal sealed class AvifEncoderCodec : ImageEncoderCodec
{
    public static AvifEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Avif;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one AVIF output. The rows of the frame are converted to the planes of the AV1 encoders (the
    /// color image, and the alpha plane of a pixel format that has alpha), which encode them a row of superblocks at a time.
    /// The file is written once both are coded, because the boxes that describe the image hold the size of what follows
    /// them: a still image goes to any stream, without seeking.
    /// <para>
    /// An image sequence writes the same boxes for its first image, room for its <c>moov</c> box, then the coded images as
    /// they are encoded. The <c>moov</c> box, which holds the size and the duration of every image, and the size of the media
    /// data are written at completion (<see cref="ImageOutputBuffer.AddPatch"/>; the destination is seekable). The session
    /// keeps 24 bytes per image for them, rented before any output.
    /// </para>
    /// </summary>
    private sealed class Session : ImageEncoderSession
    {
        private const int RowsPerCall = 32;
        private const int EmitPieceLength = 64 * 1024;

        // The time scale of the durations that have no common time scale of 32 bits: microseconds
        private const long RoundedTimescale = 1_000_000;

        private readonly AvifEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly PixelFormat _layout;
        private readonly int _width;
        private readonly int _height;
        private readonly CicpColorDescription _colorDescription;
        private readonly AvifRgbConverter _converter;
        private readonly byte[]? _icc;
        private readonly byte[]? _exif;
        private readonly byte[]? _xmp;
        private readonly ExifOrientation _orientation;
        private readonly Rectangle? _cleanAperture;
        private readonly bool _premultiplyAlpha;

        // The tiles of a grid: their size, and their numbers of columns and of rows (0 for an image that is one item)
        private readonly int _tileWidth;
        private readonly int _tileHeight;
        private readonly int _gridColumns;
        private readonly int _gridRows;
        private readonly PixelAspectRatio? _pixelAspectRatio;
        private readonly ContentLightLevel? _contentLightLevel;
        private readonly MasteringDisplayColorVolume? _masteringDisplay;
        private readonly bool _isSequence;
        private readonly int _frameCount;
        private readonly int _movieReservation;
        private readonly List<ReadOnlyMemory<byte>> _pieces = [];
        private Av1PooledArray<int>? _sizes;
        private Av1PooledArray<long>? _durations;
        private Av1PooledArray<byte>? _keyFrames;
        private PooledPayloadWriter? _movie;
        private long _timescale = 1;
        private int _frameIndex;
        private int _movieOffset;
        private int _mediaSizeOffset;
        private long _firstSampleOffset;
        private bool _completing;
        private Av1Encoder? _color;
        private Av1Encoder? _alpha;
        private PooledPayloadWriter? _colorTiles;
        private PooledPayloadWriter? _alphaTiles;
        private Av1PooledArray<int>? _tileLengths;
        private int _tileIndex;
        private PooledBuffer? _rows;
        private PooledPayloadWriter? _prefix;
        private ImageFrame? _frame;
        private Step _step;
        private int _row;
        private bool _isTransparent;
        private int _pieceIndex;
        private int _pieceOffset;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (AvifEncoder)options.Encoder;
            _sourceFormat = options.PixelFormat;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _isSequence = options.Capabilities.IsAnimated;
            _frameCount = _isSequence ? options.ExpectedFrameCount ?? throw new ArgumentException("An AVIF image sequence requires the number of its images.", nameof(options)) : 1;
            var bitDepth = ResolveBitDepth(_encoder, _sourceFormat);
            var monochrome = PixelFormats.IsGrayscale(_sourceFormat);
            var deep = PixelFormats.GetBitsPerComponent(_sourceFormat) > 8;
            _layout = monochrome ? (deep ? PixelFormat.Gray16 : PixelFormat.Gray8) : (deep ? PixelFormat.Rgba64 : PixelFormat.Rgba32);

            // The lossless coding has no color conversion: the identity matrix, in full range and without subsampling. The
            // lossy coding keeps the matrix and the range of the color description of the image when the library converts
            // them, and uses BT.601 in full range otherwise. No matrix applies to a monochrome image
            var lossless = _encoder.Compression is AvifCompression.Lossless;
            var metadata = options.Metadata;
            var cicp = metadata.Cicp;
            var primaries = cicp?.ColorPrimaries ?? Av1Constants.CpUnspecified;
            var transfer = cicp?.TransferCharacteristics ?? Av1Constants.TcUnspecified;
            var hasMatrix = cicp is { MatrixCoefficients: Av1Constants.McBt709 or Av1Constants.McFcc or Av1Constants.McBt470BG or Av1Constants.McBt601 or Av1Constants.McSmpte240 or Av1Constants.McSmpteYcgco or Av1Constants.McBt2020Ncl };
            int matrix;
            var fullRange = true;
            var subsamplingX = 0;
            var subsamplingY = 0;
            if (monochrome)
            {
                matrix = hasMatrix ? cicp!.MatrixCoefficients : Av1Constants.McUnspecified;
                fullRange = lossless || (cicp?.FullRange ?? true);
            }
            else if (lossless)
            {
                matrix = Av1Constants.McIdentity;
            }
            else
            {
                matrix = hasMatrix ? cicp!.MatrixCoefficients : Av1Constants.McBt601;
                fullRange = !hasMatrix || cicp!.FullRange;
                subsamplingX = _encoder.ChromaSubsampling is AvifChromaSubsampling.Ratio444 ? 0 : 1;
                subsamplingY = _encoder.ChromaSubsampling is AvifChromaSubsampling.Auto or AvifChromaSubsampling.Ratio420 ? 1 : 0;
            }

            _colorDescription = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
            _premultiplyAlpha = _encoder.PremultiplyAlpha && PixelFormats.HasAlpha(_sourceFormat);
            _converter = new AvifRgbConverter(_layout, bitDepth, subsamplingX, subsamplingY, matrix, fullRange, _encoder.ClearTransparentColors, _premultiplyAlpha);
            _icc = metadata.IccProfile?.Data.ToArray();
            _exif = metadata.Exif;
            _xmp = metadata.XmpProfile?.Data.ToArray();
            _pixelAspectRatio = metadata.PixelAspectRatio;
            _contentLightLevel = metadata.ContentLightLevel;
            _masteringDisplay = metadata.MasteringDisplay;
            _orientation = metadata.Orientation;

            _cleanAperture = ResolveCleanAperture(_encoder, _width, _height, subsamplingX, subsamplingY);
            (_tileWidth, _tileHeight, _gridColumns, _gridRows) = ResolveGrid(_encoder, _width, _height, subsamplingX, subsamplingY, _isSequence);

            // The encoders code the image, or one tile of its grid at a time
            var codedWidth = IsGrid ? _tileWidth : _width;
            var codedHeight = IsGrid ? _tileHeight : _height;

            // Every buffer whose size is known is rented before any output, so that a limit failure leaves no output
            try
            {
                var scope = options.Scope;
                var settings = new Av1EncoderSettings(codedWidth, codedHeight, bitDepth, monochrome, subsamplingX, subsamplingY, primaries, transfer, matrix, fullRange, ChromaSamplePosition: 0, StillPicture: !_isSequence, lossless, _encoder.Quality, _encoder.Effort, _encoder.KeyFrameInterval);
                _color = new Av1Encoder(scope, settings);
                if (PixelFormats.HasAlpha(_sourceFormat))
                {
                    // The alpha plane: a monochrome image in full range, always lossless. Its key frames are the ones of
                    // the color planes
                    var alphaSettings = new Av1EncoderSettings(codedWidth, codedHeight, bitDepth, Monochrome: true, SubsamplingX: 1, SubsamplingY: 1, Av1Constants.CpUnspecified, Av1Constants.TcUnspecified, Av1Constants.McUnspecified, FullRange: true, ChromaSamplePosition: 0, StillPicture: !_isSequence, Lossless: true, Quality: 100, _encoder.Effort, _encoder.KeyFrameInterval);
                    _alpha = new Av1Encoder(scope, alphaSettings);
                }

                // Two rows of pixels in the layout of the converter: a chroma sample covers two rows when it is subsampled.
                // A grid also has two rows of a tile, for the tiles that extend past the image
                var rowBytes = 2L * (_width + (IsGrid ? _tileWidth : 0)) * PixelFormats.GetBytesPerPixel(_layout);
                if (rowBytes > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(scope.Limits);

                _rows = scope.Rent((int)rowBytes, AllocationKind.Temporary, clear: false);
                _prefix = new PooledPayloadWriter(scope);
                if (IsGrid)
                {
                    // The coded tiles, and the number of bytes of each of them: the color tiles, then the alpha tiles
                    _colorTiles = new PooledPayloadWriter(scope);
                    _alphaTiles = _alpha is null ? null : new PooledPayloadWriter(scope);
                    _tileLengths = Av1PooledArray<int>.Rent(scope, 2L * _gridColumns * _gridRows, clear: true, AllocationKind.Temporary);
                }
                if (_isSequence)
                {
                    // The size of the color image and of the alpha plane, the duration and the kind of each image; then
                    // the room of the largest moov box that this number of images can need
                    _sizes = Av1PooledArray<int>.Rent(scope, 2L * _frameCount, clear: true, AllocationKind.Temporary);
                    _durations = Av1PooledArray<long>.Rent(scope, 2L * _frameCount, clear: true, AllocationKind.Temporary);
                    _keyFrames = Av1PooledArray<byte>.Rent(scope, _frameCount, clear: true, AllocationKind.Temporary);
                    _movie = new PooledPayloadWriter(scope);
                    WriteMovie(reservation: 0, worstCase: true);
                    _movieReservation = checked(_movie.Length + 8);
                    _movie.Reset();
                }
            }
            catch
            {
                Dispose(disposing: true);
                throw;
            }
        }

        private bool IsGrid => _gridColumns is not 0;

        private enum Step
        {
            None,
            Analyze,
            Convert,
            EncodeColor,
            EncodeAlpha,
            Emit,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_isSequence)
                return;

            // The time scale of the sequence is the smallest one that holds every duration seen so far exactly. A frame
            // may be validated more than once: the time scale only grows, and only with a frame that is accepted
            var duration = frame.Metadata.Duration;
            var timescale = LeastCommonMultiple(_timescale, duration.Denominator);
            if (timescale > uint.MaxValue)
            {
                if (_encoder.DurationRounding is FrameDurationRounding.RequireExact)
                    throw new UnsupportedImageFeatureException($"The duration {duration} cannot be stored exactly with the other durations of the AVIF image sequence: their common time scale exceeds 32 bits. Set AvifEncoder.DurationRounding to RoundToNearest to round the durations to microseconds.", ImageFormat.Avif, "AVIF frame duration precision");

                timescale = RoundedTimescale;
            }

            if ((Int128)duration.Numerator * timescale > (Int128)uint.MaxValue * duration.Denominator)
                throw new UnsupportedImageFeatureException($"The duration {duration} exceeds what an AVIF image sequence stores for an image with the time scale of its durations.", ImageFormat.Avif, "AVIF frame duration range");

            _timescale = timescale;
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("AVIF output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_isSequence && index is not 0)
                throw new InvalidOperationException("A still AVIF image stores a single frame.");

            if (_isSequence)
            {
                if (index != _frameIndex || index >= _frameCount)
                    throw new InvalidOperationException("The images of an AVIF image sequence are written in order, up to the announced number of images.");

                var duration = frame.Metadata.Duration;
                _durations!.Span[2 * index] = duration.Numerator;
                _durations.Span[(2 * index) + 1] = duration.Denominator;
            }

            _frame = frame;
            _row = 0;
            _isTransparent = false;
            _tileIndex = 0;

            // The tiles of a grid are coded one after the other: whether the image has an alpha plane is known first
            _step = IsGrid && _alpha is not null ? Step.Analyze : Step.Convert;
        }

        public override void BeginComplete(int frameCount)
        {
            // A still image is complete once its frame is written; an image sequence still has its moov box to write
            if (_isSequence && _frameIndex != _frameCount)
                throw new InvalidOperationException("The AVIF image sequence does not have the announced number of images.");

            _completing = true;
            _pieces.Clear();
            _pieceIndex = 0;
            _pieceOffset = 0;
            _step = Step.Emit;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Analyze:
                    AnalyzeRows();
                    return false;
                case Step.Convert:
                    if (IsGrid)
                        ConvertTileRows();
                    else
                        ConvertRows();

                    return false;
                case Step.EncodeColor:
                    if (_color!.Encode(CancellationToken))
                    {
                        // The alpha track of a sequence has every image, whether it is opaque or not
                        if (_alpha is not null && (_isTransparent || _isSequence))
                        {
                            _alpha.BeginFrame();
                            _step = Step.EncodeAlpha;
                        }
                        else
                        {
                            CompleteImage();
                        }
                    }

                    return false;
                case Step.EncodeAlpha:
                    if (_alpha!.Encode(CancellationToken))
                        CompleteImage();

                    return false;
                case Step.Emit:
                    if (!Emit(output))
                        return false;

                    if (_completing && _isSequence)
                        CompleteSequence(output);

                    return true;
                default:
                    throw new InvalidOperationException("No AVIF operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _color?.Dispose();
                _color = null;
                _alpha?.Dispose();
                _alpha = null;
                _rows?.Dispose();
                _rows = null;
                _prefix?.Dispose();
                _prefix = null;
                _colorTiles?.Dispose();
                _colorTiles = null;
                _alphaTiles?.Dispose();
                _alphaTiles = null;
                _tileLengths?.Dispose();
                _tileLengths = null;
                _movie?.Dispose();
                _movie = null;
                _sizes?.Dispose();
                _sizes = null;
                _durations?.Dispose();
                _durations = null;
                _keyFrames?.Dispose();
                _keyFrames = null;
                _pieces.Clear();
                _frame = null;
            }

            base.Dispose(disposing);
        }

        // 8-bit pixels keep their depth unless more is asked for; 16-bit pixels lose precision, which needs a consent
        private static int ResolveBitDepth(AvifEncoder encoder, PixelFormat format)
        {
            if (encoder.BitDepth is { } bitDepth)
                return bitDepth;

            if (PixelFormats.GetBitsPerComponent(format) <= 8)
                return 8;

            if (!encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"AVIF stores at most 12 bits per sample, so encoding {format} pixels would discard precision. Set AvifEncoder.BitDepth to 10 or 12, set AvifEncoder.AllowBitDepthReduction to reduce the samples to 12 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Avif, "Bit depth reduction");

            return 12;
        }

        // The clean aperture selects whole samples of every plane (ISO/IEC 23000-22, section 7.3.6.7): inside the image,
        // and on a chroma sample when the chroma planes are subsampled
        private static Rectangle? ResolveCleanAperture(AvifEncoder encoder, int width, int height, int subsamplingX, int subsamplingY)
        {
            if (encoder.CleanAperture is not { } aperture)
                return null;

            if (aperture.Right > width || aperture.Bottom > height)
                throw new ArgumentException($"The clean aperture ({aperture}) is not inside the image ({width}x{height}).", nameof(encoder));

            if ((subsamplingX is not 0 && (aperture.X & 1) is not 0) || (subsamplingY is not 0 && (aperture.Y & 1) is not 0))
                throw new UnsupportedImageFeatureException($"The clean aperture ({aperture}) of an AVIF image with subsampled chroma starts on a chroma sample: its left coordinate must be even{(subsamplingY is not 0 ? ", and its top coordinate too" : "")}. Move the aperture, or select AvifChromaSubsampling.Ratio444.", ImageFormat.Avif, "Clean aperture between chroma samples");

            // The whole image is no aperture
            return aperture.X is 0 && aperture.Y is 0 && aperture.Width == width && aperture.Height == height ? null : aperture;
        }

        // The grid of a still image (ISO/IEC 23000-22, section 7.3.11.4.2): tiles of one size, of at least 64 samples per
        // side, that cover the image; with subsampled chroma, the tiles and the image have an even number of columns, and
        // of rows for 4:2:0, so that every tile starts on a chroma sample. An image that one tile holds is one item
        private static (int TileWidth, int TileHeight, int Columns, int Rows) ResolveGrid(AvifEncoder encoder, int width, int height, int subsamplingX, int subsamplingY, bool isSequence)
        {
            if (isSequence || encoder.GridTileSize is not { } tile || (tile.Width >= width && tile.Height >= height))
                return default;

            if ((subsamplingX is not 0 && ((tile.Width | width) & 1) is not 0) || (subsamplingY is not 0 && ((tile.Height | height) & 1) is not 0))
                throw new UnsupportedImageFeatureException($"A grid of an AVIF image with subsampled chroma needs an even width of the tiles ({tile.Width}) and of the image ({width}){(subsamplingY is not 0 ? $", and an even height ({tile.Height} and {height})" : "")}. Change the size of the tiles, select AvifChromaSubsampling.Ratio444, or remove AvifEncoder.GridTileSize.", ImageFormat.Avif, "AVIF grid between chroma samples");

            var columns = ((width - 1) / tile.Width) + 1;
            var rows = ((height - 1) / tile.Height) + 1;
            if (columns > 256 || rows > 256)
                throw new UnsupportedImageFeatureException($"A grid of an AVIF image has at most 256 columns and 256 rows: tiles of {tile.Width}x{tile.Height} need {columns}x{rows} of them for an image of {width}x{height}. Select larger tiles.", ImageFormat.Avif, "AVIF grid size");

            return (tile.Width, tile.Height, columns, rows);
        }

        // Looks for a pixel that is not opaque, a band of rows at a time: an opaque image has no alpha grid
        private void AnalyzeRows()
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            CancellationToken.ThrowIfCancellationRequested();
            var rowLength = _width * PixelFormats.GetBytesPerPixel(_layout);
            var scratch = _rows!.RawBuffer.AsSpan(0, rowLength);
            var end = Math.Min(_height, _row + RowsPerCall);
            using (var lease = frame.GetStorage().AcquireLease())
            {
                for (; _row < end && !_isTransparent; _row++)
                {
                    var row = Normalize(lease.GetRowBytes(_row), scratch);
                    if (_layout is PixelFormat.Rgba32)
                    {
                        for (var x = 3; x < row.Length; x += 4)
                        {
                            if (row[x] is not byte.MaxValue)
                            {
                                _isTransparent = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                        for (var x = 3; x < samples.Length; x += 4)
                        {
                            if (samples[x] is not ushort.MaxValue)
                            {
                                _isTransparent = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (_row < _height && !_isTransparent)
                return;

            _row = 0;
            _step = Step.Convert;
        }

        // Converts a band of rows of the current tile to the planes of the encoders. The tiles of the right column and of
        // the bottom row may extend past the image: its last column and its last row are repeated there, which is what
        // costs the least to code and what the grid then crops
        private void ConvertTileRows()
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            CancellationToken.ThrowIfCancellationRequested();
            var color = _color!;
            var luma = color.GetSourcePlane(0);
            var monochrome = color.Sequence.Monochrome;
            var u = monochrome ? null : color.GetSourcePlane(1);
            var v = monochrome ? null : color.GetSourcePlane(2);
            var alpha = _isTransparent ? _alpha?.GetSourcePlane(0) : null;
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_layout);
            var rowLength = _width * bytesPerPixel;
            var tileLength = _tileWidth * bytesPerPixel;
            var buffer = _rows!.RawBuffer.AsSpan();
            var scratch = buffer[..(2 * rowLength)];
            var tileScratch = buffer.Slice(2 * rowLength, 2 * tileLength);
            var left = _tileIndex % _gridColumns * _tileWidth;
            var top = _tileIndex / _gridColumns * _tileHeight;
            var step = _converter.RowsPerCall;
            var subY = step - 1;
            var end = Math.Min(_tileHeight, _row + RowsPerCall);
            using (var lease = frame.GetStorage().AcquireLease())
            {
                for (; _row < end; _row += step)
                {
                    // The height of a tile is even when the chroma planes have half the rows
                    var hasSecondRow = step is 2;
                    var row0 = GetTileRow(lease.GetRowBytes(Math.Min(top + _row, _height - 1)), left, scratch[..rowLength], tileScratch[..tileLength]);
                    var row1 = hasSecondRow ? GetTileRow(lease.GetRowBytes(Math.Min(top + _row + 1, _height - 1)), left, scratch[rowLength..], tileScratch[tileLength..]) : row0;
                    _converter.ConvertRows(
                        row0,
                        row1,
                        luma.GetRow(_row),
                        hasSecondRow ? luma.GetRow(_row + 1) : default,
                        u is null ? default : u.GetRow(_row >> subY),
                        v is null ? default : v.GetRow(_row >> subY),
                        alpha is null ? default : alpha.GetRow(_row),
                        alpha is null || !hasSecondRow ? default : alpha.GetRow(_row + 1),
                        hasSecondRow);
                }
            }

            if (_row < _tileHeight)
                return;

            color.BeginFrame();
            _step = Step.EncodeColor;
        }

        // The pixels of a row of the frame that a tile covers, in the layout of the converter
        private ReadOnlySpan<byte> GetTileRow(ReadOnlySpan<byte> row, int left, Span<byte> scratch, Span<byte> tileScratch)
        {
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_layout);
            var pixels = Normalize(row, scratch)[(left * bytesPerPixel)..];
            if (pixels.Length >= tileScratch.Length)
                return pixels[..tileScratch.Length];

            // The last pixel of the row is repeated to the right edge of the tile
            pixels.CopyTo(tileScratch);
            var last = pixels[^bytesPerPixel..];
            for (var offset = pixels.Length; offset < tileScratch.Length; offset += bytesPerPixel)
            {
                last.CopyTo(tileScratch[offset..]);
            }

            return tileScratch;
        }

        // The image is coded, or one more tile of its grid, which is kept until every tile is coded
        private void CompleteImage()
        {
            if (!IsGrid)
            {
                Assemble();
                return;
            }

            var count = _gridColumns * _gridRows;
            var lengths = _tileLengths!.Span;
            var colorPayload = _color!.Payload.Span;
            _colorTiles!.Write(colorPayload);
            lengths[_tileIndex] = colorPayload.Length;
            _color.Reset();
            if (_isTransparent)
            {
                var alphaPayload = _alpha!.Payload.Span;
                _alphaTiles!.Write(alphaPayload);
                lengths[count + _tileIndex] = alphaPayload.Length;
                _alpha.Reset();
            }

            _tileIndex++;
            if (_tileIndex < count)
            {
                _row = 0;
                _step = Step.Convert;
                return;
            }

            _frame = null;
            AssembleGrid();
        }

        // Writes the boxes that describe the grids and their tiles, now that the size of every tile is known
        private void AssembleGrid()
        {
            var count = _gridColumns * _gridRows;
            var lengths = _tileLengths!.Span;
            var prefix = _prefix!;
            prefix.Reset();
            AvifFileWriter.WritePrefix(
                prefix,
                new AvifFileWriter.StillImage
                {
                    Width = _width,
                    Height = _height,
                    Color = _color!.Sequence,
                    ColorLength = 0,
                    Alpha = _isTransparent ? _alpha!.Sequence : null,
                    ColorDescription = _colorDescription,
                    IccProfile = _icc,
                    Exif = _exif,
                    Xmp = _xmp,
                    Orientation = _orientation,
                    PixelAspectRatio = _pixelAspectRatio,
                    ContentLightLevel = _contentLightLevel,
                    MasteringDisplay = _masteringDisplay,
                    CleanAperture = _cleanAperture,
                    IsPremultiplied = _premultiplyAlpha,
                    Grid = new AvifFileWriter.GridLayout
                    {
                        TileWidth = _tileWidth,
                        TileHeight = _tileHeight,
                        Columns = _gridColumns,
                        Rows = _gridRows,
                    },
                },
                colorTileLengths: lengths[..count],
                alphaTileLengths: _isTransparent ? lengths.Slice(count, count) : default);

            _pieces.Clear();
            _pieces.Add(prefix.WrittenMemory);
            if (_isTransparent)
                _pieces.Add(_alphaTiles!.WrittenMemory);

            _pieces.Add(_colorTiles!.WrittenMemory);
            _frameIndex++;
            _pieceIndex = 0;
            _pieceOffset = 0;
            _step = Step.Emit;
        }

        // Converts a band of rows of the frame to the planes of the encoders
        private void ConvertRows()
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            CancellationToken.ThrowIfCancellationRequested();
            var color = _color!;
            var luma = color.GetSourcePlane(0);
            var monochrome = color.Sequence.Monochrome;
            var u = monochrome ? null : color.GetSourcePlane(1);
            var v = monochrome ? null : color.GetSourcePlane(2);
            var alpha = _alpha?.GetSourcePlane(0);
            var rowLength = _width * PixelFormats.GetBytesPerPixel(_layout);
            var scratch = _rows!.RawBuffer.AsSpan(0, 2 * rowLength);
            var step = _converter.RowsPerCall;
            var subY = step - 1;
            var end = Math.Min(_height, _row + RowsPerCall);
            using (var lease = frame.GetStorage().AcquireLease())
            {
                for (; _row < end; _row += step)
                {
                    var hasSecondRow = step is 2 && _row + 1 < _height;
                    var row0 = Normalize(lease.GetRowBytes(_row), scratch[..rowLength]);
                    var row1 = hasSecondRow ? Normalize(lease.GetRowBytes(_row + 1), scratch[rowLength..]) : row0;
                    _isTransparent |= _converter.ConvertRows(
                        row0,
                        row1,
                        luma.GetRow(_row),
                        hasSecondRow ? luma.GetRow(_row + 1) : default,
                        u is null ? default : u.GetRow(_row >> subY),
                        v is null ? default : v.GetRow(_row >> subY),
                        alpha is null ? default : alpha.GetRow(_row),
                        alpha is null || !hasSecondRow ? default : alpha.GetRow(_row + 1),
                        hasSecondRow);
                }
            }

            if (_row < _height)
                return;

            _frame = null;
            color.BeginFrame();
            _step = Step.EncodeColor;
        }

        // A row of the frame in the layout of the converter
        private ReadOnlySpan<byte> Normalize(ReadOnlySpan<byte> row, Span<byte> scratch)
        {
            if (_sourceFormat == _layout)
                return row;

            PixelConverter.ConvertRow(_sourceFormat, row, _layout, scratch, background: null, ImageFormat.Avif);
            return scratch;
        }

        // Writes the boxes that precede the coded images, now that their sizes are known; the following images of a
        // sequence are only their samples
        private void Assemble()
        {
            var color = _color!;
            var alpha = _isTransparent || _isSequence ? _alpha : null;
            var colorPayload = color.Payload;
            var alphaPayload = alpha?.Payload ?? default;
            _pieces.Clear();
            if (_frameIndex is 0)
            {
                var prefix = _prefix!;
                prefix.Reset();
                (_movieOffset, _mediaSizeOffset) = AvifFileWriter.WritePrefix(
                    prefix,
                    new AvifFileWriter.StillImage
                    {
                        Width = _width,
                        Height = _height,
                        Color = color.Sequence,
                        ColorLength = colorPayload.Length,
                        Alpha = alpha?.Sequence,
                        AlphaLength = alphaPayload.Length,
                        ColorDescription = _colorDescription,
                        IccProfile = _icc,
                        Exif = _exif,
                        Xmp = _xmp,
                        Orientation = _orientation,
                        PixelAspectRatio = _pixelAspectRatio,
                        ContentLightLevel = _contentLightLevel,
                        MasteringDisplay = _masteringDisplay,
                        CleanAperture = _cleanAperture,
                        IsPremultiplied = _premultiplyAlpha,
                    },
                    _isSequence ? _movieReservation : -1);

                _firstSampleOffset = prefix.Length;
                _pieces.Add(prefix.WrittenMemory);
            }

            if (_isSequence)
            {
                _sizes!.Span[2 * _frameIndex] = colorPayload.Length;
                _sizes.Span[(2 * _frameIndex) + 1] = alphaPayload.Length;
                _keyFrames!.Span[_frameIndex] = (byte)(color.IsKeyFrame ? 1 : 0);
            }

            if (alpha is not null)
                _pieces.Add(alphaPayload);

            _pieces.Add(colorPayload);
            _frameIndex++;
            _pieceIndex = 0;
            _pieceOffset = 0;
            _step = Step.Emit;
        }

        // The moov box takes the place of the bytes that were reserved for it, and the media data get their size
        private void CompleteSequence(ImageOutputBuffer output)
        {
            var movie = _movie!;
            movie.Reset();
            WriteMovie(_movieReservation, worstCase: false);
            output.AddPatch(_movieOffset, movie.WrittenSpan);
            Span<byte> size = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)(output.TotalBytes - (_mediaSizeOffset - 8)));
            output.AddPatch(_mediaSizeOffset, size);
            _completing = false;
        }

        private void WriteMovie(int reservation, bool worstCase)
        {
            // The sizes and the durations in the layout of the tables, for the images written so far
            var count = worstCase ? _frameCount : _frameIndex;
            var scope = Options.Scope;
            using var tables = Av1PooledArray<int>.Rent(scope, 2L * count, clear: true, AllocationKind.Temporary);
            using var deltaTable = Av1PooledArray<uint>.Rent(scope, count, clear: true, AllocationKind.Temporary);
            var sizes = _sizes!.Span;
            var durations = _durations!.Span;
            var colorSizes = tables.Span[..count];
            var alphaSizes = _alpha is null ? default : tables.Span[count..];
            var deltas = deltaTable.Span;
            var timescale = worstCase ? 1 : GetTimescale();
            for (var i = 0; i < count; i++)
            {
                colorSizes[i] = sizes[2 * i];
                if (_alpha is not null)
                    alphaSizes[i] = sizes[(2 * i) + 1];

                if (!worstCase)
                    deltas[i] = GetSampleDelta(durations[2 * i], durations[(2 * i) + 1], timescale);
            }

            AvifFileWriter.WriteMovie(
                _movie!,
                new AvifFileWriter.Movie
                {
                    Width = _width,
                    Height = _height,
                    Color = _color!.Sequence,
                    Alpha = _alpha?.Sequence,
                    ColorDescription = _colorDescription,
                    IccProfile = _icc,
                    Timescale = (uint)timescale,
                    TotalPlays = worstCase ? null : Options.Animation?.TotalPlays,
                    PixelAspectRatio = _pixelAspectRatio,
                    ContentLightLevel = _contentLightLevel,
                    MasteringDisplay = _masteringDisplay,
                    IsPremultiplied = _premultiplyAlpha,
                    Orientation = _orientation,
                    CleanAperture = _cleanAperture,
                },
                colorSizes,
                alphaSizes,
                deltas,
                _keyFrames!.Span[..count],
                _firstSampleOffset,
                reservation,
                worstCase);
        }

        // The smallest time scale that holds the duration of every image exactly, or microseconds when it has more than
        // 32 bits (which ValidateFrame only accepts when the durations may be rounded)
        private long GetTimescale()
        {
            long timescale = 1;
            var durations = _durations!.Span;
            for (var i = 0; i < _frameIndex; i++)
            {
                timescale = LeastCommonMultiple(timescale, durations[(2 * i) + 1]);
                if (timescale > uint.MaxValue)
                    return RoundedTimescale;
            }

            return timescale;
        }

        private static uint GetSampleDelta(long numerator, long denominator, long timescale)
        {
            var delta = (((Int128)numerator * timescale * 2) + denominator) / ((Int128)denominator * 2);
            if (delta > uint.MaxValue)
                throw new UnsupportedImageFeatureException("A duration exceeds what an AVIF image sequence stores for an image with the time scale of its durations.", ImageFormat.Avif, "AVIF frame duration range");

            return (uint)delta;
        }

        // Returns a value above 32 bits when the multiple does not fit in 32 bits
        private static long LeastCommonMultiple(long a, long b)
        {
            var gcd = (long)BigInteger.GreatestCommonDivisor(a, b);
            var multiple = (Int128)a / gcd * b;
            return multiple > uint.MaxValue ? long.MaxValue : (long)multiple;
        }

        // Bounded pieces: the writer flushes between calls
        private bool Emit(ImageOutputBuffer output)
        {
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

            _pieces.Clear();
            _step = Step.None;
            return true;
        }
    }
}
