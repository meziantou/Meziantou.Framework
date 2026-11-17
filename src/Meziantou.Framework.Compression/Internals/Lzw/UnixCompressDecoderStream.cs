using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzw;

/// <summary>
/// Decodes the LZW data of the Unix compress command (.Z): the header <c>1F 9D</c>, a byte with the maximum width of the
/// codes (9 to 16 bits) and the block mode flag, and the codes, least significant bit first, from 9 bits wide.
/// </summary>
/// <remarks>
/// compress writes the codes in groups of 8 codes (as many bytes as the width of the codes), and pads the last group when
/// the width of the codes changes or when the table is cleared (the code 256 of the block mode): the decoder reads the
/// codes in the same groups, as ncompress and 7-Zip do. The width grows when the next free code no longer fits.
/// </remarks>
internal sealed class UnixCompressDecoderStream : ReadOnlyStreamBase
{
    private const int MinBits = 9;
    private const int MaxBits = 16;
    private const int ClearCode = 256;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly byte[] _inputBuffer = new byte[16 * 1024];
    private int _inputPosition;
    private int _inputLength;
    private bool _inputEnded;

    // The current group of codes, with 2 more bytes so that a code can be read with 3 bytes
    private readonly byte[] _group = new byte[MaxBits + 2];
    private int _groupBits;
    private int _groupPosition;

    private readonly ushort[] _prefixes = new ushort[1 << MaxBits];
    private readonly byte[] _suffixes = new byte[1 << MaxBits];
    private readonly byte[] _stack = new byte[(1 << MaxBits) + 1];
    private int _stackTop;

    private bool _headerRead;
    private bool _ended;
    private bool _disposed;
    private int _maxBits;
    private bool _blockMode;
    private int _maxMaxCode;
    private int _bits;
    private int _maxCode;
    private int _freeCode;
    private int _oldCode = -1;
    private byte _finalByte;

    public UnixCompressDecoderStream(Stream input, bool leaveOpen)
    {
        _input = input;
        _leaveOpen = leaveOpen;
        _stackTop = _stack.Length;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_headerRead)
        {
            ReadHeader();
        }

        var written = 0;
        while (written < buffer.Length)
        {
            if (_stackTop < _stack.Length)
            {
                var count = Math.Min(_stack.Length - _stackTop, buffer.Length - written);
                _stack.AsSpan(_stackTop, count).CopyTo(buffer[written..]);
                _stackTop += count;
                written += count;
                continue;
            }

            if (_ended || !DecodeCode())
            {
                _ended = true;
                break;
            }
        }

        return written;
    }

    private void ReadHeader()
    {
        _headerRead = true;
        if (!TryReadByte(out var b0) || !TryReadByte(out var b1) || b0 is not 0x1F || b1 is not 0x9D)
            throw new InvalidDataException("The data do not start with the header of the compress format.");

        if (!TryReadByte(out var flags))
            throw new InvalidDataException("The header of the compress data is truncated.");

        _maxBits = flags & 0x1F;
        if (_maxBits is < MinBits or > MaxBits)
            throw new InvalidDataException($"The compress data use codes of {_maxBits} bits, but the maximum is {MaxBits}.");

        _blockMode = (flags & 0x80) is not 0;
        _maxMaxCode = 1 << _maxBits;
        _freeCode = _blockMode ? ClearCode + 1 : ClearCode;
        SetBits(MinBits);
    }

    private void SetBits(int bits)
    {
        // As compress, the codes start at 9 bits even when the maximum is 9, and then grow to 10 bits when the table is
        // full: the maximum is checked only when the width grows
        _bits = bits;
        _maxCode = bits > MinBits && bits == _maxBits ? _maxMaxCode : (1 << bits) - 1;

        // The rest of the group is padding
        _groupPosition = _groupBits;
    }

    /// <summary>Decodes the next code: its string is on the stack.</summary>
    /// <returns><see langword="false"/> at the end of the data.</returns>
    private bool DecodeCode()
    {
        while (true)
        {
            if (_freeCode > _maxCode)
            {
                SetBits(_bits + 1);
            }

            if (!TryReadCode(out var code))
                return false;

            if (_oldCode < 0)
            {
                if (code >= 256)
                    throw new InvalidDataException("The compress data start with an invalid code.");

                _finalByte = (byte)code;
                _oldCode = code;
                _stack[--_stackTop] = (byte)code;
                return true;
            }

            if (code is ClearCode && _blockMode)
            {
                // The next code adds an unused entry at 256: the free codes start at 257 again
                _freeCode = ClearCode;
                SetBits(MinBits);
                continue;
            }

            var top = _stack.Length;
            var current = code;
            if (current >= _freeCode)
            {
                if (current > _freeCode)
                    throw new InvalidDataException("The compress data contain an undefined code.");

                _stack[--top] = _finalByte;
                current = _oldCode;
            }

            while (current >= 256)
            {
                if (top <= 1)
                    throw new InvalidDataException("The compress data contain an invalid code.");

                _stack[--top] = _suffixes[current];
                current = _prefixes[current];
            }

            _finalByte = (byte)current;
            _stack[--top] = _finalByte;
            if (_freeCode < _maxMaxCode)
            {
                _prefixes[_freeCode] = (ushort)_oldCode;
                _suffixes[_freeCode] = _finalByte;
                _freeCode++;
            }

            _oldCode = code;
            _stackTop = top;
            return true;
        }
    }

    private bool TryReadCode(out int code)
    {
        if (_groupPosition + _bits > _groupBits)
        {
            // The next group: 8 codes, or fewer at the end of the data
            var length = 0;
            while (length < _bits && TryReadByte(out var value))
            {
                _group[length++] = value;
            }

            _group.AsSpan(length).Clear();
            _groupBits = length * 8;
            _groupPosition = 0;
            if (_bits > _groupBits)
            {
                code = 0;
                return false;
            }
        }

        var index = _groupPosition >> 3;
        var bits = _group[index] | (_group[index + 1] << 8) | (_group[index + 2] << 16);
        code = (bits >> (_groupPosition & 7)) & ((1 << _bits) - 1);
        _groupPosition += _bits;
        return true;
    }

    private bool TryReadByte(out byte value)
    {
        if (_inputPosition == _inputLength)
        {
            if (_inputEnded)
            {
                value = 0;
                return false;
            }

            _inputPosition = 0;
            _inputLength = _input.Read(_inputBuffer);
            if (_inputLength is 0)
            {
                _inputEnded = true;
                value = 0;
                return false;
            }
        }

        value = _inputBuffer[_inputPosition++];
        return true;
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
