#if NET11_0_OR_GREATER
using System.Buffers;
#else
using System.Buffers.Binary;
#endif
using System.IO.Compression;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Inflates the image datastream of a PNG image for <see cref="PngImageDataDecoder"/>: a zlib datastream, or the raw
/// deflate datastream of an Apple CgBI file (no header, no Adler-32 trailer), pushed in bounded pieces.
/// </summary>
/// <remarks>
/// <para>
/// Push-model use: <see cref="Push"/> copies a piece of compressed data, then <see cref="Inflate"/> decompresses until it
/// returns 0 (more data is needed, or the datastream ended); a piece is pushed only once the previous one was read
/// (<see cref="Available"/> is 0). Once the datastream ended, its bytes after the end stay available, which the decoder
/// reports as extra data, and <see cref="Complete"/> checks that it did end.
/// </para>
/// <para>
/// On .NET 11, a raw <c>DeflateDecoder</c> inflates the deflate data; the zlib header (compression method 8, a
/// window of at most 32 KiB, no preset dictionary, check bits) and the Adler-32 trailer are read here. The decoder reports
/// where the deflate data ends, so the trailer is the 4 bytes that follow it, and the checksum is computed once, with the
/// vectorized <see cref="Adler32"/> (the BCL zlib computes it with scalar code on Arm64).
/// </para>
/// <para>
/// On .NET 10, a <see cref="ZLibStream"/> reads an <see cref="InflateFeedStream"/>. It checks the Adler-32 trailer itself
/// but reports a truncated datastream as a clean end and does not report where the datastream ends (it reads ahead), so the
/// last 4 bytes pushed must be the Adler-32 checksum of the decompressed bytes, computed here too. A raw deflate datastream
/// is given a zlib header before it and, at the end, the checksum of the decompressed bytes after it, then one byte, which
/// only an inflater that did not reach the end of the datastream reads.
/// </para>
/// </remarks>
internal sealed class PngZlibInflater : IDisposable
{
    private readonly bool _rawDeflate;

#if NET11_0_OR_GREATER
    private readonly byte[] _buffer;
    private DeflateDecoder? _decoder = new();
    private int _offset;
    private int _length;
    private Stage _stage;

    // The bytes of the zlib header or of the Adler-32 trailer read so far
    private int _fieldLength;
    private uint _field;
    private uint _adler = Adler32.Compute([]);

    /// <param name="buffer">The storage of one pushed piece (its capacity bounds each push).</param>
    /// <param name="rawDeflate">Whether the datastream is raw deflate data (Apple CgBI) instead of a zlib datastream.</param>
    public PngZlibInflater(byte[] buffer, bool rawDeflate)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _rawDeflate = rawDeflate;
        _stage = rawDeflate ? Stage.Deflate : Stage.Header;
    }

    private enum Stage
    {
        Header,
        Deflate,
        Trailer,
        End,
    }

    /// <summary>Gets the number of pushed bytes not read yet (after the end of the datastream: the bytes that follow it).</summary>
    public int Available => _length - _offset;

    /// <summary>Copies the next piece of compressed data; the previous piece must have been read completely.</summary>
    /// <returns>The number of bytes copied (at most the buffer capacity).</returns>
    public int Push(ReadOnlySpan<byte> compressed)
    {
        if (Available != 0)
            throw new InvalidOperationException("The previous compressed piece was not consumed by the inflater.");

        var count = Math.Min(compressed.Length, _buffer.Length);
        compressed[..count].CopyTo(_buffer);
        _offset = 0;
        _length = count;
        return count;
    }

    /// <summary>Decompresses the next bytes of the datastream.</summary>
    /// <returns>The number of bytes written; 0 when more compressed data is needed or the datastream ended.</returns>
    /// <exception cref="InvalidImageContentException">The datastream is invalid (header, deflate data or checksum).</exception>
    public int Inflate(Span<byte> destination)
    {
        var decoder = _decoder ?? throw new ObjectDisposedException(nameof(PngZlibInflater));
        var written = 0;
        while (true)
        {
            switch (_stage)
            {
                case Stage.Header:
                    if (!TryReadField(2))
                        return written;

                    ValidateHeader(_field);
                    (_field, _fieldLength) = (0, 0);
                    _stage = Stage.Deflate;
                    break;

                case Stage.Deflate:
                {
                    var target = destination[written..];
                    if (target.IsEmpty)
                        return written;

                    var status = decoder.Decompress(_buffer.AsSpan(_offset, _length - _offset), target, out var consumed, out var produced);
                    _offset += consumed;
                    written += produced;
                    if (!_rawDeflate)
                    {
                        _adler = Adler32.Append(_adler, target[..produced]);
                    }

                    switch (status)
                    {
                        case OperationStatus.Done:
                            _stage = _rawDeflate ? Stage.End : Stage.Trailer;
                            break;

                        case OperationStatus.InvalidData:
                            throw Invalid(_rawDeflate ? "The deflate datastream of the Apple CgBI image data is invalid." : "The zlib datastream of the PNG image data is invalid.");

                        default:
                            // NeedMoreData (every pushed byte was read) or DestinationTooSmall
                            return written;
                    }

                    break;
                }

                case Stage.Trailer:
                    if (!TryReadField(4))
                        return written;

                    if (_field != _adler)
                        throw Invalid("The Adler-32 checksum of the zlib datastream of the PNG image data does not match its decompressed bytes.");

                    _stage = Stage.End;
                    break;

                default:
                    return written;
            }
        }
    }

    /// <summary>Validates that the datastream ended, once all its compressed data was pushed and inflated.</summary>
    /// <exception cref="InvalidImageContentException">The datastream does not end (truncated).</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_decoder is null, this);
        if (_stage != Stage.End)
        {
            throw Invalid(_rawDeflate
                ? "The deflate datastream of the Apple CgBI image data does not end with its final block (truncated datastream)."
                : "The PNG image data does not end with the Adler-32 checksum of its zlib datastream (truncated datastream).");
        }
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _decoder = null;
    }

    /// <summary>
    /// Validates a zlib header (RFC 1950, section 2.2) as zlib does for a 32 KiB window: compression method 8, a window of
    /// at most 32 KiB, check bits, and no preset dictionary, which PNG forbids.
    /// </summary>
    private static void ValidateHeader(uint header)
    {
        var method = header >> 8;
        if ((method & 0x0F) != 8 || (method >> 4) > 7 || header % 31 != 0 || (header & 0x20) != 0)
            throw Invalid("The zlib header of the PNG image data is invalid.");
    }

    /// <summary>Reads the bytes of the header or the trailer (big-endian) until it has <paramref name="length"/> of them.</summary>
    /// <returns><see langword="false"/> when every pushed byte was read before.</returns>
    private bool TryReadField(int length)
    {
        while (_fieldLength < length)
        {
            if (_offset == _length)
                return false;

            _field = (_field << 8) | _buffer[_offset++];
            _fieldLength++;
        }

        return true;
    }
#else
    private InflateFeedStream? _feed;
    private ZLibStream? _zlib;
    private bool _pendingSyntheticHeader;
    private uint _adler = Adler32.Compute([]);
    private long _compressedLength;
    private uint _trailer;

    /// <param name="buffer">The storage of one pushed piece (its capacity bounds each push).</param>
    /// <param name="rawDeflate">Whether the datastream is raw deflate data (Apple CgBI) instead of a zlib datastream.</param>
    public PngZlibInflater(byte[] buffer, bool rawDeflate)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _rawDeflate = rawDeflate;
        _feed = new InflateFeedStream(buffer);
        _zlib = new ZLibStream(_feed, CompressionMode.Decompress, leaveOpen: true);
        _pendingSyntheticHeader = rawDeflate;
    }

    /// <summary>Gets the number of pushed bytes not read by the inflater yet (a finished inflater stops reading).</summary>
    public int Available => _feed?.Available ?? 0;

    /// <summary>Copies the next piece of compressed data; the previous piece must have been read completely.</summary>
    /// <returns>The number of bytes copied (at most the buffer capacity).</returns>
    public int Push(ReadOnlySpan<byte> compressed)
    {
        var feed = _feed ?? throw new ObjectDisposedException(nameof(PngZlibInflater));
        var count = _pendingSyntheticHeader ? feed.Push(BoundedInflater.SyntheticZlibHeader, compressed) : feed.Push(compressed);
        _pendingSyntheticHeader = false;
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

    /// <summary>Decompresses the next bytes of the datastream.</summary>
    /// <returns>The number of bytes written; 0 when more compressed data is needed or the datastream ended.</returns>
    /// <exception cref="InvalidImageContentException">The datastream is invalid (header, deflate data or checksum).</exception>
    public int Inflate(Span<byte> destination)
    {
        var zlib = _zlib ?? throw new ObjectDisposedException(nameof(PngZlibInflater));
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

        _adler = Adler32.Append(_adler, destination[..read]);
        return read;
    }

    /// <summary>Validates that the datastream ended, once all its compressed data was pushed and inflated.</summary>
    /// <exception cref="InvalidImageContentException">The datastream does not end (truncated) or is followed by extra data.</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_feed is null, this);
        if (_rawDeflate)
        {
            CompleteRawDeflate();
            return;
        }

        if (_compressedLength < 6 || _trailer != _adler)
            throw Invalid("The PNG image data does not end with the Adler-32 checksum of its zlib datastream (truncated datastream or extra data after it).");
    }

    public void Dispose()
    {
        _zlib?.Dispose();
        _zlib = null;
        _feed?.Dispose();
        _feed = null;
    }

    /// <summary>
    /// Ends the raw deflate datastream of an Apple CgBI file: the inflater is given the checksum of the decompressed bytes,
    /// which ends a complete datastream, then one byte, which only an inflater that did not reach the end reads.
    /// </summary>
    private void CompleteRawDeflate()
    {
        var feed = _feed!;
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, _adler);
        feed.Push(checksum);
        Span<byte> probe = stackalloc byte[1];

        // The image is complete (more data was rejected before): a datastream that goes on after the checksum is truncated
        if (Inflate(probe) != 0)
            throw Truncated();

        if (feed.Available != 0)
            throw Invalid("The Apple CgBI image data has extra bytes after the end of its deflate datastream.");

        feed.Push([0]);
        if (Inflate(probe) != 0 || feed.Available == 0)
            throw Truncated();

        static InvalidImageContentException Truncated() => Invalid("The deflate datastream of the Apple CgBI image data does not end with its final block (truncated datastream).");
    }
#endif

    private static InvalidImageContentException Invalid(string message, Exception? innerException = null) => new(message, ImageFormat.Png, innerException);
}
