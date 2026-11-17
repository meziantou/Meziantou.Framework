using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.ZipLegacy;

/// <summary>
/// Decodes the shrink method of zip (method 1, PKWARE APPNOTE section 5.1): LZW codes, least significant bit first, from 9
/// to 13 bits wide. The code 256 is followed by a control code: 1 widens the codes, 2 frees the codes that are not the
/// prefix of another code (a partial clearing). The new strings take the lowest free code after the last one assigned.
/// </summary>
internal sealed class ZipShrinkDecoderStream : ReadOnlyStreamBase
{
    private const int MinBits = 9;
    private const int MaxBits = 13;
    private const int TableSize = 1 << MaxBits;
    private const int ControlCode = 256;
    private const int FirstCode = 257;
    private const short Free = -1;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly LsbBitReader _reader;
    private readonly short[] _parents = new short[TableSize];
    private readonly byte[] _values = new byte[TableSize];
    private readonly byte[] _stack = new byte[TableSize + 1];
    private int _stackTop;
    private long _remaining;
    private int _bits = MinBits;
    private int _lastCode = ControlCode;
    private int _oldCode = -1;
    private byte _finalByte;
    private bool _disposed;

    public ZipShrinkDecoderStream(Stream input, long outputLength, bool leaveOpen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        _input = input;
        _leaveOpen = leaveOpen;
        _remaining = outputLength;
        _reader = new LsbBitReader(input, 4096);
        _stackTop = _stack.Length;
        _parents.AsSpan(FirstCode).Fill(Free);
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var written = 0;
        while (written < buffer.Length && _remaining > 0)
        {
            if (_stackTop == _stack.Length)
            {
                DecodeCode();
            }

            var count = (int)Math.Min(Math.Min(_stack.Length - _stackTop, buffer.Length - written), _remaining);
            _stack.AsSpan(_stackTop, count).CopyTo(buffer[written..]);
            _stackTop += count;
            written += count;
            _remaining -= count;
        }

        return written;
    }

    private void DecodeCode()
    {
        while (true)
        {
            var code = (int)_reader.Read(_bits);
            if (code is ControlCode)
            {
                switch (_reader.Read(_bits))
                {
                    case 1:
                        if (_bits >= MaxBits)
                            throw new InvalidDataException("The shrunk data use codes wider than 13 bits.");

                        _bits++;
                        break;

                    case 2:
                        PartialClear();
                        break;

                    default:
                        throw new InvalidDataException("The shrunk data contain an invalid control code.");
                }

                continue;
            }

            if (_oldCode < 0)
            {
                if (code > 255)
                    throw new InvalidDataException("The shrunk data start with an invalid code.");

                _oldCode = code;
                _finalByte = (byte)code;
                _stack[--_stackTop] = (byte)code;
                return;
            }

            var top = _stack.Length;
            var current = code;
            if (current >= FirstCode && _parents[current] is Free)
            {
                // The code being added: the previous string and its first byte
                if (current != NextFreeCode())
                    throw new InvalidDataException("The shrunk data contain an undefined code.");

                _stack[--top] = _finalByte;
                current = _oldCode;
            }

            while (current >= FirstCode)
            {
                var parent = _parents[current];
                if (parent is Free || top <= 1)
                    throw new InvalidDataException("The shrunk data contain an invalid code.");

                _stack[--top] = _values[current];
                current = parent;
            }

            if (current is ControlCode)
                throw new InvalidDataException("The shrunk data contain an invalid code.");

            _finalByte = (byte)current;
            _stack[--top] = _finalByte;

            var free = NextFreeCode();
            if (free < TableSize)
            {
                _parents[free] = (short)_oldCode;
                _values[free] = _finalByte;
                _lastCode = free;
            }

            _oldCode = code;
            _stackTop = top;
            return;
        }
    }

    private int NextFreeCode()
    {
        var code = _lastCode + 1;
        while (code < TableSize && _parents[code] is not Free)
        {
            code++;
        }

        return code;
    }

    /// <summary>Frees the codes that are not the prefix of another code (the leaves of the tree of the strings).</summary>
    private void PartialClear()
    {
        var hasChild = new bool[TableSize];
        for (var code = FirstCode; code <= _lastCode; code++)
        {
            var parent = _parents[code];
            if (parent >= FirstCode)
            {
                hasChild[parent] = true;
            }
        }

        for (var code = FirstCode; code <= _lastCode; code++)
        {
            if (!hasChild[code])
            {
                _parents[code] = Free;
            }
        }

        _lastCode = ControlCode;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed && !_leaveOpen)
        {
            _input.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
