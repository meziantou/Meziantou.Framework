using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
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
    private HeifPictureEncoder? _color;
    private HeifPictureEncoder? _alpha;
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
        _converter = new HeifRgbConverter(_layout, bitDepth, subsamplingX, subsamplingY, matrix, fullRange, settings.ClearTransparentColors, binding.HasCositedChromaColumns);
        _icc = metadata.IccProfile?.Data.ToArray();
        _exif = metadata.Exif;
        _xmp = metadata.XmpProfile?.Data.ToArray();
        _orientation = metadata.Orientation;
        if (_isSequence && _orientation is not ExifOrientation.TopLeft)
        {
            // The transformations are properties of an item: a track has none
            if (settings.MetadataHandling is MetadataHandling.Strict)
                throw binding.Unsupported($"The {binding.Format} format cannot store the orientation of an image sequence. Apply it with AutoOrient, remove it, or set the encoder's MetadataHandling to DiscardUnsupported or Strip.", "Metadata: orientation of an image sequence");

            _orientation = ExifOrientation.TopLeft;
        }

        // Every buffer whose size is known is rented before any output, so that a limit failure leaves no output
        try
        {
            var scope = options.Scope;
            var colorSettings = new HeifPictureSettings(_width, _height, bitDepth, monochrome, subsamplingX, subsamplingY, primaries, transfer, matrix, fullRange, StillPicture: !_isSequence, lossless, settings.Quality, settings.Effort, settings.KeyFrameInterval);
            _color = binding.CreatePictureEncoder(scope, colorSettings);

            // The pictures of the stream may be larger than the image, which is then their clean aperture: the alpha plane
            // has the size of the pictures, since the same clean aperture describes both
            _codedSize = _color.Description.OutputSize ?? options.CanvasSize;

            if (PixelFormats.HasAlpha(_sourceFormat))
            {
                // The alpha plane: a monochrome image in full range, always lossless. Its key frames are the ones of
                // the color planes
                var alphaSettings = new HeifPictureSettings(_codedSize.Width, _codedSize.Height, bitDepth, Monochrome: true, SubsamplingX: 1, SubsamplingY: 1, Av1Constants.CpUnspecified, Av1Constants.TcUnspecified, Av1Constants.McUnspecified, FullRange: true, StillPicture: !_isSequence, Lossless: true, Quality: 100, settings.Effort, settings.KeyFrameInterval);
                _alpha = binding.CreatePictureEncoder(scope, alphaSettings);
            }

            // Two rows of pixels in the layout of the converter: a chroma sample covers two rows when it is subsampled
            var rowBytes = 2L * _width * PixelFormats.GetBytesPerPixel(_layout);
            if (rowBytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _rows = scope.Rent((int)rowBytes, AllocationKind.Temporary, clear: false);
            _prefix = new PooledPayloadWriter(scope);
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

    private enum Step
    {
        None,
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
        _step = Step.Convert;
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
            case Step.Convert:
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
                        Assemble();
                    }
                }

                return false;
            case Step.EncodeAlpha:
                if (_alpha!.Encode(CancellationToken))
                    Assemble();

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
}
