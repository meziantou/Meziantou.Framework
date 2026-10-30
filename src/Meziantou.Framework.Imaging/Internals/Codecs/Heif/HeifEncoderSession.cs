using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoding state of one output of a format of the HEIF family, whose codec is the one of a
/// <see cref="HeifCodecBinding"/>. The rows of the frame are converted to the planes of the picture encoders (the color
/// image, and the alpha plane of a pixel format that has alpha), which encode them a bounded part at a time. The file is
/// written once both are coded, because the boxes that describe the image hold the size of what follows them: a still
/// image goes to any stream, without seeking.
/// <para>
/// An image sequence writes the same boxes for its first image, room for its <c>moov</c> box, then the coded images as
/// they are encoded. The <c>moov</c> box, which holds the size and the duration of every image, and the size of the media
/// data are written at completion (<see cref="ImageOutputBuffer.AddPatch"/>; the destination is seekable). The session
/// keeps 24 bytes per image for them, rented before any output.
/// </para>
/// <para>
/// The tiles of a grid are coded one after the other by the same encoders, or, with several workers
/// (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>, at least <see cref="RowBands.MinPixelsForParallelism"/>
/// samples), up to that many at once, each worker with encoders and rows of its own, rented before any output. The coded
/// tiles are appended in the order of the tiles: the file and the errors are those of one worker.
/// </para>
/// </summary>
internal sealed class HeifEncoderSession : ImageEncoderSession
{
    private const int RowsPerCall = 32;
    private const int EmitPieceLength = 64 * 1024;

    // The time scale of the durations that have no common time scale of 32 bits: microseconds
    private const long RoundedTimescale = 1_000_000;

    private readonly HeifCodecBinding _binding;
    private readonly HeifEncoderSettings _settings;
    private readonly PixelFormat _sourceFormat;
    private readonly PixelFormat _layout;
    private readonly int _width;
    private readonly Size _codedSize;
    private readonly int _height;
    private readonly CicpColorDescription _colorDescription;
    private readonly HeifRgbConverter _converter;
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
    private readonly ExceptionDispatchInfo?[]? _tileErrors;
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
    private HeifPictureEncoder? _color;
    private HeifPictureEncoder? _alpha;
    private PooledPayloadWriter? _colorTiles;
    private PooledPayloadWriter? _alphaTiles;
    private Av1PooledArray<int>? _tileLengths;
    private TileEncoder[]? _tileEncoders;
    private int _tileIndex;
    private PooledBuffer? _rows;
    private PooledPayloadWriter? _prefix;
    private ImageFrame? _frame;
    private Step _step;
    private int _row;
    private bool _isTransparent;
    private int _pieceIndex;
    private int _pieceOffset;

    public HeifEncoderSession(ImageEncoderSessionOptions options, HeifCodecBinding binding, in HeifEncoderSettings settings)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _binding = binding;
        _settings = settings;
        _sourceFormat = options.PixelFormat;
        _width = options.CanvasSize.Width;
        _height = options.CanvasSize.Height;
        _isSequence = options.Capabilities.IsAnimated;
        _frameCount = _isSequence ? options.ExpectedFrameCount ?? throw new ArgumentException($"An {binding.Name} image sequence requires the number of its images.", nameof(options)) : 1;
        var bitDepth = ResolveBitDepth();
        var monochrome = PixelFormats.IsGrayscale(_sourceFormat);
        var deep = PixelFormats.GetBitsPerComponent(_sourceFormat) > 8;
        _layout = monochrome ? (deep ? PixelFormat.Gray16 : PixelFormat.Gray8) : (deep ? PixelFormat.Rgba64 : PixelFormat.Rgba32);

        // The lossless coding has no color conversion: the identity matrix, in full range and without subsampling. The
        // lossy coding keeps the matrix and the range of the color description of the image when the library converts
        // them, and uses BT.601 in full range otherwise. No matrix applies to a monochrome image
        var lossless = settings.Lossless;
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
            subsamplingX = settings.SubsamplingX;
            subsamplingY = settings.SubsamplingY;
        }

        _colorDescription = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
        _premultiplyAlpha = settings.PremultiplyAlpha && PixelFormats.HasAlpha(_sourceFormat);
        _converter = new HeifRgbConverter(_layout, bitDepth, subsamplingX, subsamplingY, matrix, fullRange, settings.ClearTransparentColors, binding.HasCositedChromaColumns, _premultiplyAlpha);
        _icc = metadata.IccProfile?.Data.ToArray();
        _exif = metadata.Exif;
        _xmp = metadata.XmpProfile?.Data.ToArray();
        if (binding.Supports(HeifContainerFeatures.DisplayMetadata))
        {
            _pixelAspectRatio = metadata.PixelAspectRatio;
            _contentLightLevel = metadata.ContentLightLevel;
            _masteringDisplay = metadata.MasteringDisplay;
        }

        _orientation = metadata.Orientation;
        if (_isSequence && _orientation is not ExifOrientation.TopLeft && !binding.Supports(HeifContainerFeatures.SequenceOrientation))
        {
            // The transformations are properties of an item: the format does not write the matrix of a track
            if (settings.MetadataHandling is MetadataHandling.Strict)
                throw binding.Unsupported($"The {binding.Format} format cannot store the orientation of an image sequence. Apply it with AutoOrient, remove it, or set the encoder's MetadataHandling to DiscardUnsupported or Strip.", "Metadata: orientation of an image sequence");

            _orientation = ExifOrientation.TopLeft;
        }

        _cleanAperture = ResolveCleanAperture(subsamplingX, subsamplingY, nameof(settings));
        (_tileWidth, _tileHeight, _gridColumns, _gridRows) = ResolveGrid(subsamplingX, subsamplingY);

        // The encoders code the image, or one tile of its grid at a time
        var codedWidth = IsGrid ? _tileWidth : _width;
        var codedHeight = IsGrid ? _tileHeight : _height;

        // Every buffer whose size is known is rented before any output, so that a limit failure leaves no output
        try
        {
            var scope = options.Scope;
            HeifPictureSettings? alphaSettings = null;

            // The workers of a picture: none of its own when several workers code the tiles of a grid, so that the threads
            // do not multiply
            var gridWorkers = IsGrid ? HeifGridTiles.GetWorkerCount(options.Configuration.MaxDegreeOfParallelism, new Size(_tileWidth, _tileHeight), _gridColumns * _gridRows) : 1;
            var pictureWorkers = gridWorkers > 1 ? 1 : options.Configuration.MaxDegreeOfParallelism;
            var colorSettings = new HeifPictureSettings(codedWidth, codedHeight, bitDepth, monochrome, subsamplingX, subsamplingY, primaries, transfer, matrix, fullRange, StillPicture: !_isSequence, lossless, settings.Quality, settings.Effort, settings.KeyFrameInterval, pictureWorkers, settings.AutoTiling);
            _color = binding.CreatePictureEncoder(scope, colorSettings);

            // The pictures of the stream may be larger than the image, which is then their clean aperture: the alpha plane
            // has the size of the pictures, since the same clean aperture describes both
            _codedSize = _color.Description.OutputSize ?? new Size(codedWidth, codedHeight);

            if (PixelFormats.HasAlpha(_sourceFormat))
            {
                // The alpha plane: a monochrome image in full range, always lossless. Its key frames are the ones of
                // the color planes
                alphaSettings = new HeifPictureSettings(_codedSize.Width, _codedSize.Height, bitDepth, Monochrome: true, SubsamplingX: 1, SubsamplingY: 1, Av1Constants.CpUnspecified, Av1Constants.TcUnspecified, Av1Constants.McUnspecified, FullRange: true, StillPicture: !_isSequence, Lossless: true, Quality: 100, settings.Effort, settings.KeyFrameInterval, pictureWorkers, settings.AutoTiling);
                _alpha = binding.CreatePictureEncoder(scope, alphaSettings.Value);
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

                // The encoders and the rows of the workers that code the tiles of a batch with the ones of the session
                if (gridWorkers > 1)
                {
                    _tileEncoders = new TileEncoder[gridWorkers - 1];
                    _tileErrors = new ExceptionDispatchInfo?[gridWorkers];
                    for (var k = 0; k < _tileEncoders.Length; k++)
                    {
                        _tileEncoders[k] = TileEncoder.Create(binding, scope, colorSettings, alphaSettings, (int)rowBytes);
                    }
                }
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
            if (_settings.DurationRounding is FrameDurationRounding.RequireExact)
                throw _binding.Unsupported($"The duration {duration} cannot be stored exactly with the other durations of the {_binding.Name} image sequence: their common time scale exceeds 32 bits. Set {_settings.EncoderName}.DurationRounding to RoundToNearest to round the durations to microseconds.", $"{_binding.Name} frame duration precision");

            timescale = RoundedTimescale;
        }

        if ((Int128)duration.Numerator * timescale > (Int128)uint.MaxValue * duration.Denominator)
            throw _binding.Unsupported($"The duration {duration} exceeds what an {_binding.Name} image sequence stores for an image with the time scale of its durations.", $"{_binding.Name} frame duration range");

        _timescale = timescale;
    }

    public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException($"{_binding.Name} output has no poster frame.");

    public override void BeginFrame(ImageFrame frame, int index)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!_isSequence && index is not 0)
            throw new InvalidOperationException($"A still {_binding.Name} image stores a single frame.");

        if (_isSequence)
        {
            if (index != _frameIndex || index >= _frameCount)
                throw new InvalidOperationException($"The images of an {_binding.Name} image sequence are written in order, up to the announced number of images.");

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
            throw new InvalidOperationException($"The {_binding.Name} image sequence does not have the announced number of images.");

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
                if (_tileEncoders is not null)
                    EncodeTiles();
                else if (IsGrid)
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
                throw new InvalidOperationException($"No {_binding.Name} operation was begun.");
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
            if (_tileEncoders is not null)
            {
                foreach (var encoder in _tileEncoders)
                {
                    encoder?.Dispose();
                }

                _tileEncoders = null;
            }

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
    private int ResolveBitDepth()
    {
        if (_settings.BitDepth is { } bitDepth)
            return bitDepth;

        if (PixelFormats.GetBitsPerComponent(_sourceFormat) <= 8)
            return 8;

        var maxBitDepth = _settings.MaxBitDepth;
        if (!_settings.AllowBitDepthReduction)
        {
            var depths = maxBitDepth > 10 ? string.Create(CultureInfo.InvariantCulture, $"10 or {maxBitDepth}") : "10";
            throw _binding.Unsupported(string.Create(CultureInfo.InvariantCulture, $"{_binding.Name} stores at most {maxBitDepth} bits per sample, so encoding {_sourceFormat} pixels would discard precision. Set {_settings.EncoderName}.BitDepth to {depths}, set {_settings.EncoderName}.AllowBitDepthReduction to reduce the samples to {maxBitDepth} bits (nearest rounding), or convert the image explicitly with CloneAs."), "Bit depth reduction");
        }

        return maxBitDepth;
    }

    // The clean aperture selects whole samples of every plane (ISO/IEC 23000-22, section 7.3.6.7): inside the image,
    // and on a chroma sample when the chroma planes are subsampled
    private Rectangle? ResolveCleanAperture(int subsamplingX, int subsamplingY, string parameterName)
    {
        var (width, height) = (_width, _height);
        if (_settings.CleanAperture is not { } aperture)
            return null;

        if (aperture.Right > width || aperture.Bottom > height)
            throw new ArgumentException($"The clean aperture ({aperture}) is not inside the image ({width}x{height}).", parameterName);

        if ((subsamplingX is not 0 && (aperture.X & 1) is not 0) || (subsamplingY is not 0 && (aperture.Y & 1) is not 0))
            throw _binding.Unsupported($"The clean aperture ({aperture}) of an {_binding.Name} image with subsampled chroma starts on a chroma sample: its left coordinate must be even{(subsamplingY is not 0 ? ", and its top coordinate too" : "")}. Move the aperture, or select AvifChromaSubsampling.Ratio444.", "Clean aperture between chroma samples");

        // The whole image is no aperture
        return aperture.X is 0 && aperture.Y is 0 && aperture.Width == width && aperture.Height == height ? null : aperture;
    }

    // The grid of a still image (ISO/IEC 23000-22, section 7.3.11.4.2): tiles of one size, of at least 64 samples per
    // side, that cover the image; with subsampled chroma, the tiles and the image have an even number of columns, and
    // of rows for 4:2:0, so that every tile starts on a chroma sample. An image that one tile holds is one item
    private (int TileWidth, int TileHeight, int Columns, int Rows) ResolveGrid(int subsamplingX, int subsamplingY)
    {
        var (width, height) = (_width, _height);
        if (_isSequence || _settings.GridTileSize is not { } tile || (tile.Width >= width && tile.Height >= height))
            return default;

        if ((subsamplingX is not 0 && ((tile.Width | width) & 1) is not 0) || (subsamplingY is not 0 && ((tile.Height | height) & 1) is not 0))
            throw _binding.Unsupported($"A grid of an {_binding.Name} image with subsampled chroma needs an even width of the tiles ({tile.Width}) and of the image ({width}){(subsamplingY is not 0 ? $", and an even height ({tile.Height} and {height})" : "")}. Change the size of the tiles, set {_settings.EncoderName}.ChromaSubsampling to Ratio444, or remove {_settings.EncoderName}.GridTileSize.", $"{_binding.Name} grid between chroma samples");

        var columns = ((width - 1) / tile.Width) + 1;
        var rows = ((height - 1) / tile.Height) + 1;
        if (columns > 256 || rows > 256)
            throw _binding.Unsupported($"A grid of an {_binding.Name} image has at most 256 columns and 256 rows: tiles of {tile.Width}x{tile.Height} need {columns}x{rows} of them for an image of {width}x{height}. Select larger tiles.", $"{_binding.Name} grid size");

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

    // Converts a band of rows of the current tile to the planes of the encoders
    private void ConvertTileRows()
    {
        var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
        CancellationToken.ThrowIfCancellationRequested();
        var color = _color!;
        var end = Math.Min(_tileHeight, _row + RowsPerCall);
        using (var lease = frame.GetStorage().AcquireLease())
        {
            ConvertTileRows(lease.Storage, _tileIndex, _row, end, color, _isTransparent ? _alpha : null, _rows!.RawBuffer);
        }

        _row = end;
        if (_row < _tileHeight)
            return;

        color.BeginFrame();
        _step = Step.EncodeColor;
    }

    // Converts the next tiles of the grid and codes them at once, one tile per worker, then appends them in the order of the
    // tiles. The errors are those of a sequential coding: a cancellation first, then the error of the first tile that fails
    private void EncodeTiles()
    {
        var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
        var cancellationToken = CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var errors = _tileErrors!;
        var tileCount = _gridColumns * _gridRows;
        var first = _tileIndex;
        var count = Math.Min(errors.Length, tileCount - first);
        var transparent = _isTransparent;
        try
        {
            using (var lease = frame.GetStorage().AcquireLease())
            {
                // The lease is held for the whole batch: the workers read the rows of the leased storage
                var storage = lease.Storage;
                RowBands.RunParallel(count, k =>
                {
                    var (color, alpha, rows) = GetTileEncoder(k);
                    try
                    {
                        ConvertTileRows(storage, first + k, 0, _tileHeight, color, transparent ? alpha : null, rows);
                        EncodePicture(color, cancellationToken);
                        if (transparent)
                            EncodePicture(alpha!, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        errors[k] = ExceptionDispatchInfo.Capture(ex);
                    }
                });
            }

            for (var k = 0; k < count; k++)
            {
                if (errors[k]?.SourceException is OperationCanceledException)
                    errors[k]!.Throw();
            }

            for (var k = 0; k < count; k++)
            {
                errors[k]?.Throw();
                var (color, alpha, _) = GetTileEncoder(k);
                AppendTile(color, alpha);
            }
        }
        finally
        {
            Array.Clear(errors);
        }

        if (_tileIndex < tileCount)
            return;

        _frame = null;
        AssembleGrid();
    }

    // The encoders and the rows of a worker: the ones of the session for the first one
    private (HeifPictureEncoder Color, HeifPictureEncoder? Alpha, byte[] Rows) GetTileEncoder(int worker)
    {
        if (worker is 0)
            return (_color!, _alpha, _rows!.RawBuffer);

        var encoder = _tileEncoders![worker - 1];
        return (encoder.Color, encoder.Alpha, encoder.Rows.RawBuffer);
    }

    private static void EncodePicture(HeifPictureEncoder encoder, CancellationToken cancellationToken)
    {
        encoder.BeginFrame();
        while (!encoder.Encode(cancellationToken))
        {
        }
    }

    // Converts the rows [start, end) of a tile to the planes of the encoders. The tiles of the right column and of the
    // bottom row may extend past the image: its last column and its last row are repeated there, which is what costs the
    // least to code and what the grid then crops. It uses no state of the session but its settings: the tiles of a grid are
    // converted on several threads, each with encoders and rows of its own.
    private void ConvertTileRows(PixelStorage storage, int tileIndex, int start, int end, HeifPictureEncoder color, HeifPictureEncoder? alphaEncoder, byte[] rows)
    {
        var luma = color.GetSourcePlane(0);
        var monochrome = color.Description.PlaneCount is 1;
        var u = monochrome ? null : color.GetSourcePlane(1);
        var v = monochrome ? null : color.GetSourcePlane(2);
        var alpha = alphaEncoder?.GetSourcePlane(0);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(_layout);
        var rowLength = _width * bytesPerPixel;
        var tileLength = _tileWidth * bytesPerPixel;
        var buffer = rows.AsSpan();
        var scratch = buffer[..(2 * rowLength)];
        var tileScratch = buffer.Slice(2 * rowLength, 2 * tileLength);
        var left = tileIndex % _gridColumns * _tileWidth;
        var top = tileIndex / _gridColumns * _tileHeight;
        var step = _converter.RowsPerCall;
        var subY = step - 1;
        for (var row = start; row < end; row += step)
        {
            // The height of a tile is even when the chroma planes have half the rows
            var hasSecondRow = step is 2;
            var row0 = GetTileRow(storage.GetRowSpanCore(Math.Min(top + row, _height - 1)), left, scratch[..rowLength], tileScratch[..tileLength]);
            var row1 = hasSecondRow ? GetTileRow(storage.GetRowSpanCore(Math.Min(top + row + 1, _height - 1)), left, scratch[rowLength..], tileScratch[tileLength..]) : row0;
            _converter.ConvertRows(
                row0,
                row1,
                luma.GetRow(row),
                hasSecondRow ? luma.GetRow(row + 1) : default,
                u is null ? default : u.GetRow(row >> subY),
                v is null ? default : v.GetRow(row >> subY),
                alpha is null ? default : alpha.GetRow(row),
                alpha is null || !hasSecondRow ? default : alpha.GetRow(row + 1),
                hasSecondRow);
        }
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

        AppendTile(_color!, _alpha);
        if (_tileIndex < _gridColumns * _gridRows)
        {
            _row = 0;
            _step = Step.Convert;
            return;
        }

        _frame = null;
        AssembleGrid();
    }

    // Keeps the next coded tile until every tile is coded, and makes its encoders ready for another tile
    private void AppendTile(HeifPictureEncoder color, HeifPictureEncoder? alpha)
    {
        var count = _gridColumns * _gridRows;
        var lengths = _tileLengths!.Span;
        var colorPayload = color.Payload.Span;
        _colorTiles!.Write(colorPayload);
        lengths[_tileIndex] = colorPayload.Length;
        color.Reset();
        if (_isTransparent)
        {
            var alphaPayload = alpha!.Payload.Span;
            _alphaTiles!.Write(alphaPayload);
            lengths[count + _tileIndex] = alphaPayload.Length;
            alpha.Reset();
        }

        _tileIndex++;
    }

    // Writes the boxes that describe the grids and their tiles, now that the size of every tile is known
    private void AssembleGrid()
    {
        var count = _gridColumns * _gridRows;
        var lengths = _tileLengths!.Span;
        var prefix = _prefix!;
        prefix.Reset();
        HeifFileWriter.WritePrefix(
            prefix,
            _binding,
            new HeifFileWriter.StillImage
            {
                Width = _width,
                Height = _height,
                Color = _color!.Description,
                ColorLength = 0,
                Alpha = _isTransparent ? _alpha!.Description : null,
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
                Grid = new HeifFileWriter.GridLayout
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
        var monochrome = color.Description.PlaneCount is 1;
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

        if (alpha is not null && (_codedSize.Width != _width || _codedSize.Height != _height))
            ExtendAlpha(alpha);

        _frame = null;
        color.BeginFrame();
        _step = Step.EncodeColor;
    }

    // The alpha plane of a stream whose pictures are larger than the image: the samples outside the image repeat the last
    // column and the last row, as the encoder of the color image extends its planes
    private void ExtendAlpha(Av1Plane alpha)
    {
        for (var y = 0; y < _height; y++)
        {
            var row = alpha.GetRow(y);
            row[_width..].Fill(row[_width - 1]);
        }

        var last = alpha.GetRow(_height - 1);
        for (var y = _height; y < _codedSize.Height; y++)
        {
            last.CopyTo(alpha.GetRow(y));
        }
    }

    // A row of the frame in the layout of the converter
    private ReadOnlySpan<byte> Normalize(ReadOnlySpan<byte> row, Span<byte> scratch)
    {
        if (_sourceFormat == _layout)
            return row;

        PixelConverter.ConvertRow(_sourceFormat, row, _layout, scratch, background: null, _binding.Format);
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
            (_movieOffset, _mediaSizeOffset) = HeifFileWriter.WritePrefix(
                prefix,
                _binding,
                new HeifFileWriter.StillImage
                {
                    Width = _width,
                    Height = _height,
                    Color = color.Description,
                    ColorLength = colorPayload.Length,
                    Alpha = alpha?.Description,
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

        HeifFileWriter.WriteMovie(
            _movie!,
            _binding,
            new HeifFileWriter.Movie
            {
                Width = _width,
                Height = _height,
                Color = _color!.Description,
                Alpha = _alpha?.Description,
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

    private uint GetSampleDelta(long numerator, long denominator, long timescale)
    {
        var delta = (((Int128)numerator * timescale * 2) + denominator) / ((Int128)denominator * 2);
        if (delta > uint.MaxValue)
            throw _binding.Unsupported($"A duration exceeds what an {_binding.Name} image sequence stores for an image with the time scale of its durations.", $"{_binding.Name} frame duration range");

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

    // The encoders and the rows of a worker that codes tiles of a grid with the ones of the session
    private sealed class TileEncoder : IDisposable
    {
        private TileEncoder(HeifPictureEncoder color, HeifPictureEncoder? alpha, PooledBuffer rows)
        {
            Color = color;
            Alpha = alpha;
            Rows = rows;
        }

        public HeifPictureEncoder Color { get; }

        public HeifPictureEncoder? Alpha { get; }

        public PooledBuffer Rows { get; }

        public static TileEncoder Create(HeifCodecBinding binding, AllocationScope scope, in HeifPictureSettings colorSettings, in HeifPictureSettings? alphaSettings, int rowBytes)
        {
            HeifPictureEncoder? color = null;
            HeifPictureEncoder? alpha = null;
            try
            {
                color = binding.CreatePictureEncoder(scope, colorSettings);
                alpha = alphaSettings is { } settings ? binding.CreatePictureEncoder(scope, settings) : null;
                var rows = scope.Rent(rowBytes, AllocationKind.Temporary, clear: false);
                return new TileEncoder(color, alpha, rows);
            }
            catch
            {
                alpha?.Dispose();
                color?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Color.Dispose();
            Alpha?.Dispose();
            Rows.Dispose();
        }
    }
}
