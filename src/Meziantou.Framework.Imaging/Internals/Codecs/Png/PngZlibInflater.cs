#if NET11_0_OR_GREATER
using System.Buffers;
#else
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
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
/// On .NET 10, a raw <see cref="DeflateStream"/> reads an <see cref="InflateFeedStream"/>; the zlib header and the Adler-32
/// trailer are read here too (a <see cref="ZLibStream"/> would compute the checksum a second time, with scalar code on
/// Arm64). The inflater reads ahead and reports neither where the deflate data ends nor a truncated datastream, so the
/// last bytes pushed are held back and given to it one at a time once it needs them: it never reads past the end of the
/// deflate data, except for the 4 bytes that can only be the trailer. Then one more byte is read only by an inflater that
/// did not reach the end of the deflate data.
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
    // The bytes after the end of the deflate data: the Adler-32 trailer of a zlib datastream, none for raw deflate data
    private readonly int _trailerLength;

    // The last pushed bytes, not given to the inflater yet: those that may follow the end of the deflate data
    private HeldBytes _held;
    private int _heldLength;
    private InflateFeedStream? _feed;
    private DeflateStream? _deflate;
    private int _headerLength;
    private uint _header;
    private uint _adler = Adler32.Compute([]);

    /// <param name="buffer">The storage of one pushed piece (its capacity bounds each push).</param>
    /// <param name="rawDeflate">Whether the datastream is raw deflate data (Apple CgBI) instead of a zlib datastream.</param>
    public PngZlibInflater(byte[] buffer, bool rawDeflate)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _rawDeflate = rawDeflate;
        _trailerLength = rawDeflate ? 0 : 4;
        _headerLength = rawDeflate ? -1 : 0;
        _feed = new InflateFeedStream(buffer);
        _deflate = new DeflateStream(_feed, CompressionMode.Decompress, leaveOpen: true);
    }

    /// <summary>Gets the number of pushed bytes not read by the inflater yet (a finished inflater stops reading).</summary>
    public int Available => _feed?.Available ?? 0;

    /// <summary>Copies the next piece of compressed data; the previous piece must have been read completely.</summary>
    /// <returns>The number of bytes copied (at most the buffer capacity).</returns>
    public int Push(ReadOnlySpan<byte> compressed)
    {
        var feed = _feed ?? throw new ObjectDisposedException(nameof(PngZlibInflater));
        var header = 0;
        while (_headerLength is >= 0 and < 2 && header < compressed.Length)
        {
            _header = (_header << 8) | compressed[header++];
            _headerLength++;
        }

        // The bytes held back from the previous piece come first, and the last bytes of the result are held back in turn
        Span<byte> held = _held;
        var count = feed.Push(held[.._heldLength], compressed[header..]);
        _heldLength = feed.Withhold(held[..(_trailerLength + 1)]);
        return header + count;
    }

    /// <summary>Decompresses the next bytes of the datastream.</summary>
    /// <returns>The number of bytes written; 0 when more compressed data is needed or the datastream ended.</returns>
    /// <exception cref="InvalidImageContentException">The datastream is invalid (header or deflate data).</exception>
    public int Inflate(Span<byte> destination)
    {
        var feed = _feed ?? throw new ObjectDisposedException(nameof(PngZlibInflater));
        if (_headerLength >= 0)
        {
            if (_headerLength < 2)
                return 0;

            ValidateHeader(_header);
            _headerLength = -1;
        }

        var read = Read(destination);

        // The inflater reads ahead and a finished inflater ignores the bytes it read after the end of the deflate data. It
        // starves before the bytes held back: they are given one at a time, so that it reads none after the end, except
        // those that can only be the trailer. A datastream that ends before them is followed by more than its trailer:
        // the bytes held back that remain unread (Complete) or the byte the finished inflater does not read (Available)
        while (read == 0 && !destination.IsEmpty && feed.Available == 0 && _heldLength > _trailerLength)
        {
            Span<byte> held = _held;
            feed.Push(held[..1]);
            held[1.._heldLength].CopyTo(held);
            _heldLength--;
            read = Read(destination);
        }

        if (!_rawDeflate)
        {
            _adler = Adler32.Append(_adler, destination[..read]);
        }

        return read;
    }

    /// <summary>Validates that the datastream ended, once all its compressed data was pushed and inflated.</summary>
    /// <exception cref="InvalidImageContentException">The datastream does not end (truncated) or is followed by extra data.</exception>
    public void Complete()
    {
        var feed = _feed;
        ObjectDisposedException.ThrowIf(feed is null, this);
        if (feed.Available != 0 || _heldLength > _trailerLength)
        {
            throw Invalid(_rawDeflate
                ? "The Apple CgBI image data has extra bytes after the end of its deflate datastream."
                : "The PNG image data has extra bytes after the end of its zlib datastream.");
        }

        // Every byte before the trailer was read: one more byte is read only by an inflater that did not reach the end of
        // the deflate data
        var ended = false;
        if (_heldLength == _trailerLength && _headerLength < 0)
        {
            feed.Push([0]);
            Span<byte> probe = stackalloc byte[1];
            try
            {
                ended = _deflate!.Read(probe) == 0 && feed.Available == 1;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                // Inflating the probe failed: the deflate data goes on
            }
        }

        if (!ended)
        {
            throw Invalid(_rawDeflate
                ? "The deflate datastream of the Apple CgBI image data does not end with its final block (truncated datastream)."
                : "The PNG image data does not end with the Adler-32 checksum of its zlib datastream (truncated datastream).");
        }

        if (!_rawDeflate && BinaryPrimitives.ReadUInt32BigEndian(_held) != _adler)
            throw Invalid("The Adler-32 checksum of the zlib datastream of the PNG image data does not match its decompressed bytes.");
    }

    public void Dispose()
    {
        _deflate?.Dispose();
        _deflate = null;
        _feed?.Dispose();
        _feed = null;
    }

    private int Read(Span<byte> destination)
    {
        try
        {
            return _deflate!.Read(destination);
        }
        catch (InvalidDataException exception)
        {
            throw Invalid(_rawDeflate ? "The deflate datastream of the Apple CgBI image data is invalid." : "The zlib datastream of the PNG image data is invalid.", exception);
        }
        catch (IOException exception)
        {
            // The feed is in memory and never fails: an IOException is the BCL's (internal) ZLibException for zlib error
            // codes that are not data errors
            throw Invalid(_rawDeflate ? "The deflate datastream of the Apple CgBI image data is invalid." : "The zlib datastream of the PNG image data is invalid.", exception);
        }
    }

    // The Adler-32 trailer and one byte, inline, so that they need no buffer of the allocation scope
    [InlineArray(5)]
    private struct HeldBytes
    {
        private byte _element;
    }
#endif

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

    private static InvalidImageContentException Invalid(string message, Exception? innerException = null) => new(message, ImageFormat.Png, innerException);
}
