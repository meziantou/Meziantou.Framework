using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the image datastream of one PNG image (the concatenated <c>IDAT</c> data, or the <c>fdAT</c> data of an APNG
/// frame) incrementally: zlib inflation of the pushed compressed pieces, scanline reconstruction (all filters), Adam7
/// pass geometry (empty passes have no scanline), and expansion to source pixels (<see cref="PngSampleFormat"/>).
/// </summary>
/// <remarks>
/// <para>
/// Push-model use: <see cref="Feed"/> copies a bounded piece of compressed data, then <see cref="TryReadRow"/> returns the
/// reconstructed rows that piece completes, until the inflater starves; finally <see cref="Complete"/> validates the end of
/// the datastream. Memory is bounded by two scanlines, one expanded row, one compressed piece and the inflater window,
/// whatever the image or chunk sizes; every buffer is charged to the operation scope as decoder state.
/// </para>
/// <para>
/// Validation (every failure is <see cref="InvalidImageContentException"/>): invalid zlib data (header, deflate blocks,
/// Adler-32 mismatch reported by the BCL), an invalid filter type, an out-of-range palette index, decompressed data longer
/// than the image (checked as soon as the last scanline is complete, so the expansion is bounded by the image size), data
/// shorter than the image, and a datastream that does not end with its Adler-32 trailer (the BCL reports truncated zlib
/// data as a clean end, so the trailer is checked against the running checksum of the decompressed bytes; extra bytes after
/// the zlib datastream are rejected the same way).
/// </para>
/// </remarks>
internal sealed class PngImageDataDecoder : IDisposable
{
    private const int FeedCapacity = 32 * 1024;

    private readonly ImageCodecContext _context;
    private readonly PngSampleFormat _format;
    private readonly int _width;
    private readonly int _height;
    private readonly int _passCount;
    private readonly long _totalScanlines;
    private PooledBuffer? _scanlineBuffer;
    private PooledBuffer? _previousBuffer;
    private PooledBuffer? _pixelBuffer;
    private PooledBuffer? _feedBuffer;
    private InflateFeedStream? _feed;
    private ZLibStream? _zlib;

    private int _pass = -1;
    private int _passWidth;
    private int _passHeight;
    private int _passRow;
    private int _scanlineLength;
    private int _filled;
    private long _decodedScanlines;
    private bool _done;
    private uint _adler = BoundedInflater.Adler32Initial;
    private long _compressedLength;
    private uint _trailer;

    /// <summary>Creates the decoder of one image.</summary>
    /// <param name="context">The per-input context (allocation scope, cancellation).</param>
    /// <param name="format">The sample layout.</param>
    /// <param name="width">The image width (the canvas, or an APNG frame region).</param>
    /// <param name="height">The image height.</param>
    /// <param name="interlaced">Whether the datastream is Adam7-interlaced.</param>
    /// <exception cref="ImageResourceLimitException">The working buffers exceed the allocation limit.</exception>
    public PngImageDataDecoder(ImageCodecContext context, PngSampleFormat format, int width, int height, bool interlaced)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _context = context;
        _format = format;
        _width = width;
        _height = height;
        _passCount = interlaced ? PngInterlace.PassCount : 1;
        for (var pass = 0; pass < _passCount; pass++)
        {
            var (passWidth, passHeight) = GetPassSize(pass);
            if (passWidth > 0)
            {
                _totalScanlines += passHeight;
            }
        }

        // The widest scanline is the full width (non-interlaced, or Adam7 pass 7 / pass 6)
        var scanlineLength = format.GetScanlineLength(width) + 1;
        var pixelLength = (long)width * format.SourceBytesPerPixel;
        if (scanlineLength > CheckedSizes.MaxBufferLength || pixelLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        var scope = context.Scope;
        try
        {
            _scanlineBuffer = scope.Rent((int)scanlineLength, AllocationKind.DecoderState, clear: false);
            _previousBuffer = scope.Rent((int)scanlineLength, AllocationKind.DecoderState);
            _pixelBuffer = scope.Rent((int)pixelLength, AllocationKind.DecoderState, clear: false);
            _feedBuffer = scope.Rent(FeedCapacity, AllocationKind.DecoderState, clear: false);
            _feed = new InflateFeedStream(_feedBuffer.RawBuffer);
            _zlib = new ZLibStream(_feed, CompressionMode.Decompress, leaveOpen: true);
        }
        catch
        {
            Dispose();
            throw;
        }

        StartNextPass();
    }

    /// <summary>Gets the number of scanlines of the datastream (Adam7: every non-empty pass).</summary>
    public long TotalScanlines => _totalScanlines;

    /// <summary>Gets a value indicating whether every scanline was decoded.</summary>
    public bool IsComplete => _done;

    /// <summary>Copies the next piece of compressed data. Call <see cref="TryReadRow"/> until it returns <see langword="false"/> before the next call.</summary>
    /// <returns>The number of bytes taken (at most an internal bound); feed the rest afterward.</returns>
    public int Feed(ReadOnlySpan<byte> compressed)
    {
        var feed = _feed ?? throw new ObjectDisposedException(nameof(PngImageDataDecoder));

        // A finished inflater stops reading its source: unread bytes follow the end of the zlib datastream
        if (feed.Available != 0)
        {
            throw _done
                ? Invalid("The PNG image data has extra bytes after the end of its zlib datastream.")
                : Invalid(string.Create(CultureInfo.InvariantCulture, $"The zlib datastream of the PNG image data ends after {_decodedScanlines} of {_totalScanlines} scanlines."));
        }

        var count = feed.Push(compressed);
        var piece = compressed[..count];
        _compressedLength += count;

        // Keep the last four compressed bytes: the Adler-32 trailer once the datastream ends
        if (count >= 4)
        {
            _trailer = BinaryPrimitives.ReadUInt32BigEndian(piece[^4..]);
        }
        else
        {
            foreach (var value in piece)
            {
                _trailer = (_trailer << 8) | value;
            }
        }

        return count;
    }

    /// <summary>Reconstructs the next row whose scanline is complete in the data fed so far.</summary>
    /// <param name="row">The row: valid until the next call.</param>
    /// <returns><see langword="false"/> when more compressed data is needed, or when every scanline was decoded.</returns>
    /// <exception cref="InvalidImageContentException">The datastream is invalid or longer than the image.</exception>
    public bool TryReadRow(out PngDecodedRow row)
    {
        var zlib = _zlib ?? throw new ObjectDisposedException(nameof(PngImageDataDecoder));
        row = default;
        if (_done)
        {
            EnsureNoExtraData();
            return false;
        }

        var scanline = _scanlineBuffer!.RawBuffer.AsSpan(0, _scanlineLength);
        while (_filled < _scanlineLength)
        {
            var read = Inflate(zlib, scanline[_filled..]);
            if (read == 0)
                return false;

            _filled += read;
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
        var data = scanline[1..];
        PngFilters.Unfilter(scanline[0], data, _previousBuffer!.RawBuffer, _format.FilterBytesPerPixel);
        var pixels = _pixelBuffer!.RawBuffer.AsSpan(0, _passWidth * _format.SourceBytesPerPixel);
        _format.Expand(data, _passWidth, pixels);
        data.CopyTo(_previousBuffer.RawBuffer);

        var (x0, y0, dx, dy) = _passCount == 1 ? (0, 0, 1, 1) : PngInterlace.GetPass(_pass);
        row = new PngDecodedRow(y0 + (_passRow * dy), x0, dx, _passWidth, pixels);
        _filled = 0;
        _decodedScanlines++;
        _passRow++;
        if (_passRow == _passHeight)
        {
            StartNextPass();
        }

        return true;
    }

    /// <summary>Validates the end of the datastream once all its compressed data was fed and its rows read.</summary>
    /// <exception cref="InvalidImageContentException">The data is shorter or longer than the image, or the zlib datastream is truncated or followed by extra data.</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_feed is null, this);

        if (!_done)
        {
            // Rows complete in the data fed so far are read first by the caller; nothing else can be produced
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG image data ends after {_decodedScanlines} of {_totalScanlines} scanlines (truncated or too short zlib datastream)."));
        }

        EnsureNoExtraData();
        if (_compressedLength < 6 || _trailer != _adler)
            throw Invalid("The PNG image data does not end with the Adler-32 checksum of its zlib datastream (truncated datastream or extra data after it).");
    }

    public void Dispose()
    {
        _zlib?.Dispose();
        _zlib = null;
        _feed?.Dispose();
        _feed = null;
        _feedBuffer?.Dispose();
        _feedBuffer = null;
        _pixelBuffer?.Dispose();
        _pixelBuffer = null;
        _previousBuffer?.Dispose();
        _previousBuffer = null;
        _scanlineBuffer?.Dispose();
        _scanlineBuffer = null;
    }

    private static InvalidImageContentException Invalid(string message, Exception? innerException = null) => new(message, ImageFormat.Png, innerException);

    private (int Width, int Height) GetPassSize(int pass) => _passCount == 1 ? (_width, _height) : PngInterlace.GetPassSize(_width, _height, pass);

    private void StartNextPass()
    {
        while (++_pass < _passCount)
        {
            (_passWidth, _passHeight) = GetPassSize(_pass);
            if (_passWidth == 0)
                continue; // empty pass: no scanline at all, not even filter bytes

            _passRow = 0;
            _scanlineLength = (int)(_format.GetScanlineLength(_passWidth) + 1);
            _previousBuffer!.RawBuffer.AsSpan(0, _scanlineLength).Clear();
            return;
        }

        _done = true;
    }

    private void EnsureNoExtraData()
    {
        // The image is complete: the zlib datastream may only end (Adler-32 trailer), never produce more bytes
        Span<byte> probe = stackalloc byte[1];
        if (Inflate(_zlib!, probe) != 0)
            throw Invalid("The PNG image data decompresses to more bytes than the image needs.");

        // A finished inflater stops reading its source: unread pushed bytes follow the end of the zlib datastream
        if (_feed!.Available != 0)
            throw Invalid("The PNG image data has extra bytes after the end of its zlib datastream.");
    }

    private int Inflate(ZLibStream zlib, Span<byte> destination)
    {
        int read;
        try
        {
            read = zlib.Read(destination);
        }
        catch (InvalidDataException exception)
        {
            throw Invalid("The zlib datastream of the PNG image data is invalid.", exception);
        }
        catch (IOException exception)
        {
            // The feed is in memory and never fails: an IOException is the BCL's (internal) ZLibException for zlib error
            // codes that are not data errors, for example a preset dictionary (FDICT), which PNG forbids (fuzz regression)
            throw Invalid("The zlib datastream of the PNG image data is invalid.", exception);
        }

        _adler = BoundedInflater.UpdateAdler32(_adler, destination[..read]);
        return read;
    }
}
