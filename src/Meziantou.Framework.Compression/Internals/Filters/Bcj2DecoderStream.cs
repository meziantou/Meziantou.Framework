// A port of the BCJ2 decoder of the LZMA SDK (Bcj2Dec.c of 7-Zip 9.20) by Igor Pavlov, which is in the public domain.
using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>
/// Decodes the BCJ2 filter of 7z (coder 0303011B): the x86 code without the addresses of its calls and jumps (the main
/// stream), the absolute addresses of the converted calls (the call stream) and jumps (the jump stream), both big-endian,
/// and the range-coded bits that tell which E8/E9/Jcc instructions were converted.
/// </summary>
internal sealed class Bcj2DecoderStream : ReadOnlyStreamBase
{
    private const uint TopValue = 1u << 24;
    private const int NumBitModelTotalBits = 11;
    private const uint BitModelTotal = 1u << NumBitModelTotalBits;
    private const int NumMoveBits = 5;

    private readonly LzmaInputBuffer _main;
    private readonly LzmaInputBuffer _call;
    private readonly LzmaInputBuffer _jump;
    private readonly LzmaInputBuffer _rangeCoder;
    private readonly long _outputLength;
    private readonly ushort[] _probs = new ushort[256 + 2];
    private readonly byte[] _pending = new byte[4];
    private readonly byte[] _address = new byte[4];
    private int _pendingIndex;
    private int _pendingCount;
    private long _outputPosition;
    private byte _previousByte;
    private bool _needBit;
    private int _probIndex;
    private byte _jumpByte;
    private uint _range;
    private uint _code;
    private bool _initialized;
    private bool _finished;
    private bool _disposed;

    /// <param name="main">The main stream.</param>
    /// <param name="call">The stream of the addresses of the calls (E8).</param>
    /// <param name="jump">The stream of the addresses of the jumps (E9 and Jcc).</param>
    /// <param name="rangeCoder">The range-coded stream.</param>
    /// <param name="outputLength">The length of the decoded data, or -1 to decode until the end of the main stream.</param>
    /// <param name="leaveOpen">Whether to leave the four streams open when the stream is disposed.</param>
    public Bcj2DecoderStream(Stream main, Stream call, Stream jump, Stream rangeCoder, long outputLength, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(jump);
        ArgumentNullException.ThrowIfNull(rangeCoder);

        _main = new LzmaInputBuffer(main, leaveOpen);
        _call = new LzmaInputBuffer(call, leaveOpen, capacity: 1 << 12);
        _jump = new LzmaInputBuffer(jump, leaveOpen, capacity: 1 << 12);
        _rangeCoder = new LzmaInputBuffer(rangeCoder, leaveOpen, capacity: 1 << 12);
        _outputLength = outputLength;
        _probs.AsSpan().Fill((ushort)(BitModelTotal >> 1));
    }

    public override long Length => _outputLength >= 0 ? _outputLength : throw new NotSupportedException();

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var written = 0;
        while (written < buffer.Length && !_finished)
        {
            if (_outputLength >= 0 && _outputPosition == _outputLength)
            {
                _finished = true;
                break;
            }

            if (_pendingCount > 0)
            {
                buffer[written++] = _pending[_pendingIndex++];
                _pendingCount--;
                _outputPosition++;
                continue;
            }

            if (_needBit)
            {
                _needBit = false;
                if (DecodeBit(_probIndex) == 0)
                {
                    _previousByte = _jumpByte;
                    continue;
                }

                var source = _jumpByte == 0xE8 ? _call : _jump;
                var address = _address;
                source.ReadExactly(address);
                var dest = (((uint)address[0] << 24) | ((uint)address[1] << 16) | ((uint)address[2] << 8) | address[3]) - (uint)(_outputPosition + 4);
                _pending[0] = (byte)dest;
                _pending[1] = (byte)(dest >> 8);
                _pending[2] = (byte)(dest >> 16);
                _pending[3] = (byte)(dest >> 24);
                _pendingIndex = 0;
                _pendingCount = 4;
                _previousByte = (byte)(dest >> 24);
                continue;
            }

            if (_main.Available == 0 && !_main.Fill(1))
            {
                if (_outputLength >= 0)
                    throw new InvalidDataException("The BCJ2 data is corrupt: the main stream is too short.");

                _finished = true;
                break;
            }

            // Copy the bytes of the main stream up to the next E8, E9 or Jcc instruction
            var input = _main.Buffer;
            var position = _main.Position;
            var limit = Math.Min(_main.End - position, buffer.Length - written);
            if (_outputLength >= 0)
            {
                limit = (int)Math.Min(limit, _outputLength - _outputPosition);
            }

            var previous = _previousByte;
            var count = 0;
            while (count < limit)
            {
                var b = input[position + count];
                buffer[written + count] = b;
                count++;
                if ((b & 0xFE) == 0xE8 || (previous == 0x0F && (b & 0xF0) == 0x80))
                {
                    _needBit = true;
                    _jumpByte = b;
                    _probIndex = b == 0xE8 ? previous : b == 0xE9 ? 256 : 257;
                    break;
                }

                previous = b;
            }

            _previousByte = previous;
            _main.Position = position + count;
            written += count;
            _outputPosition += count;
            if (_needBit && _outputLength >= 0 && _outputPosition == _outputLength)
            {
                _needBit = false;
            }
        }

        return written;
    }

    private uint DecodeBit(int index)
    {
        if (!_initialized)
        {
            _initialized = true;
            for (var i = 0; i < 5; i++)
            {
                _code = (_code << 8) | _rangeCoder.ReadByteOrThrow();
            }

            _range = uint.MaxValue;
        }

        uint prob = _probs[index];
        var bound = (_range >> NumBitModelTotalBits) * prob;
        uint bit;
        if (_code < bound)
        {
            _range = bound;
            _probs[index] = (ushort)(prob + ((BitModelTotal - prob) >> NumMoveBits));
            bit = 0;
        }
        else
        {
            _range -= bound;
            _code -= bound;
            _probs[index] = (ushort)(prob - (prob >> NumMoveBits));
            bit = 1;
        }

        if (_range < TopValue)
        {
            _range <<= 8;
            _code = (_code << 8) | _rangeCoder.ReadByteOrThrow();
        }

        return bit;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _main.Dispose();
            _call.Dispose();
            _jump.Dispose();
            _rangeCoder.Dispose();
        }

        base.Dispose(disposing);
    }
}
