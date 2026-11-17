// A port of the LZMA2 decoder of xz-utils (liblzma) and of the LZMA SDK (Lzma2Dec.c), which are in the public domain.
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// Decodes an LZMA2 stream: a sequence of LZMA and uncompressed chunks that ends with a 0x00 control byte. The decoder
/// reads exactly the bytes of the stream, so a container can read what follows from the same <see cref="LzmaInputBuffer"/>.
/// </summary>
internal sealed class Lzma2DecoderStream : ReadOnlyStreamBase
{
    private readonly LzmaInputBuffer _input;
    private readonly bool _ownsInput;
    private readonly LzmaDecoder _decoder;
    private int _pending;
    private int _chunkRemaining;
    private int _chunkEnd;
    private bool _chunkIsLzma;
    private bool _needDictionaryReset = true;
    private bool _needProperties = true;
    private bool _finished;
    private bool _disposed;

    /// <param name="input">The compressed data.</param>
    /// <param name="dictionaryProperty">The LZMA2 dictionary property (0 to 40).</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="input"/> open when the stream is disposed.</param>
    public Lzma2DecoderStream(Stream input, byte dictionaryProperty, bool leaveOpen)
        : this(new LzmaInputBuffer(input, leaveOpen), dictionaryProperty, ownsInput: true)
    {
    }

    /// <summary>Decodes the LZMA2 data at the position of a buffer that the caller owns (the XZ reader).</summary>
    internal Lzma2DecoderStream(LzmaInputBuffer input, byte dictionaryProperty, bool ownsInput, long maxMemoryUsage = long.MaxValue)
    {
        _input = input;
        _ownsInput = ownsInput;
        _decoder = new LzmaDecoder(LzmaConstants.GetLzma2DictionarySize(dictionaryProperty), maxOutputLength: -1, maxMemoryUsage);
    }

    /// <summary>Gets a value indicating whether the end of the LZMA2 stream was read.</summary>
    internal bool IsFinished => _finished && _pending == _decoder.WindowPosition;

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            var available = _decoder.WindowPosition - _pending;
            if (available > 0)
            {
                var count = Math.Min(available, buffer.Length);
                _decoder.Window.AsSpan(_pending, count).CopyTo(buffer);
                _pending += count;
                return count;
            }

            if (_finished)
                return 0;

            if (_chunkRemaining == 0)
            {
                ReadChunkHeader();
                continue;
            }

            if (_decoder.WindowPosition == _decoder.WindowCapacity)
            {
                _decoder.MakeRoom();
                _pending = _decoder.WindowPosition;
            }

            var room = _decoder.WindowCapacity - _decoder.WindowPosition;
            if (!_chunkIsLzma)
            {
                if (_input.Available == 0 && !_input.Fill(1))
                    throw new InvalidDataException("Unexpected end of data.");

                var count = Math.Min(Math.Min(room, _chunkRemaining), _input.Available);
                _decoder.AppendUncompressed(_input.Buffer.AsSpan(_input.Position, count));
                _input.Position += count;
                _chunkRemaining -= count;
                continue;
            }

            var before = _decoder.WindowPosition;
            var limit = before + Math.Min(Math.Min(room, _chunkRemaining), Math.Max(buffer.Length, 1 << 16));
            var inputPosition = _input.Position;
            if (inputPosition > _chunkEnd)
                throw new InvalidDataException("The LZMA2 data is corrupt.");

            if (_decoder.Decode(_input.Buffer, ref inputPosition, _chunkEnd, limit))
                throw new InvalidDataException("The LZMA2 data is corrupt: an LZMA chunk contains an end marker.");

            _input.Position = inputPosition;
            if (inputPosition > _chunkEnd)
                throw new InvalidDataException("The LZMA2 data is corrupt: a chunk is longer than its compressed size.");

            _chunkRemaining -= _decoder.WindowPosition - before;
        }
    }

    private void ReadChunkHeader()
    {
        if (_chunkIsLzma)
        {
            if (_input.Position != _chunkEnd || !_decoder.IsRangeDecoderFinished || _decoder.HasPendingMatch)
                throw new InvalidDataException("The LZMA2 data is corrupt: a chunk is shorter than its compressed size.");

            _chunkIsLzma = false;
        }

        var control = _input.ReadByteOrThrow();
        if (control == 0)
        {
            _finished = true;
            return;
        }

        if (control >= 0xE0 || control == 1)
        {
            _needProperties = true;
            _needDictionaryReset = false;

            // The decoded data was copied out of the window before the header is read
            _decoder.ResetDictionary();
            _pending = 0;
        }
        else if (_needDictionaryReset)
        {
            throw new InvalidDataException("The LZMA2 data is corrupt: the first chunk does not reset the dictionary.");
        }

        if (control >= 0x80)
        {
            var unpackedSize = ((control & 0x1F) << 16) + (_input.ReadByteOrThrow() << 8) + _input.ReadByteOrThrow() + 1;
            var packedSize = (_input.ReadByteOrThrow() << 8) + _input.ReadByteOrThrow() + 1;
            if (control >= 0xC0)
            {
                var properties = _input.ReadByteOrThrow();
                if (!LzmaDecoder.TryDecodeProperties(properties, out var lc, out var lp, out var pb) || lc + lp > 4)
                    throw new InvalidDataException("The LZMA2 data is corrupt: the LZMA properties are invalid.");

                _decoder.SetProperties(lc, lp, pb);
                _decoder.ResetState();
                _needProperties = false;
            }
            else if (_needProperties)
            {
                throw new InvalidDataException("The LZMA2 data is corrupt: an LZMA chunk does not set the properties.");
            }
            else if (control >= 0xA0)
            {
                _decoder.ResetState();
            }

            if (packedSize < 5 || !_input.Fill(packedSize))
                throw new InvalidDataException("Unexpected end of data.");

            _chunkEnd = _input.Position + packedSize;
            var position = _input.Position;
            _decoder.InitializeRangeDecoder(_input.Buffer, ref position);
            _input.Position = position;
            _chunkRemaining = unpackedSize;
            _chunkIsLzma = true;
            return;
        }

        if (control > 2)
            throw new InvalidDataException("The LZMA2 data is corrupt: the control byte is invalid.");

        _chunkRemaining = (_input.ReadByteOrThrow() << 8) + _input.ReadByteOrThrow() + 1;
        _chunkIsLzma = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _decoder.Dispose();
            if (_ownsInput)
            {
                _input.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
