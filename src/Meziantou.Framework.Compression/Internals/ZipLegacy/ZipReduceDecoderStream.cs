using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.ZipLegacy;

/// <summary>
/// Decodes the reduce methods of zip (methods 2 to 5, compression factors 1 to 4, PKWARE APPNOTE section 5.2). The data
/// start with the follower sets of the 256 bytes, from the last one: a byte is either a literal of 8 bits, or the index of
/// one of the followers of the previous byte. The bytes are then expanded: the byte 0x90 (DLE) starts a copy of 3 or more
/// bytes from a distance of at most 4096 bytes (whose bits are shared with the length depending on the factor), or is
/// followed by 0 for a literal 0x90.
/// </summary>
internal sealed class ZipReduceDecoderStream : ReadOnlyStreamBase
{
    private const int Dle = 0x90;
    private const int WindowSize = 1 << 13;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly LsbBitReader _reader;
    private readonly int _lengthMask;
    private readonly int _distanceShift;
    private readonly byte[] _followers = new byte[256 * 64];
    private readonly byte[] _followerCounts = new byte[256];
    private readonly byte[] _window = new byte[WindowSize];
    private long _position;
    private long _remaining;
    private bool _followersRead;
    private int _lastByte;
    private int _state;
    private int _lengthByte;
    private int _length;
    private int _copyRemaining;
    private int _copyDistance;
    private bool _disposed;

    public ZipReduceDecoderStream(Stream input, int compressionFactor, long outputLength, bool leaveOpen)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(compressionFactor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(compressionFactor, 4);
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        _input = input;
        _leaveOpen = leaveOpen;
        _remaining = outputLength;
        _reader = new LsbBitReader(input, 4096);

        // The factor 1 keeps 7 bits of the length byte for the length and 1 bit for the distance, the factor 4 keeps 4 and 4
        _lengthMask = 0xFF >> compressionFactor;
        _distanceShift = 8 - compressionFactor;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_followersRead && _remaining > 0)
        {
            ReadFollowerSets();
        }

        var written = 0;
        while (written < buffer.Length && _remaining > 0)
        {
            if (_copyRemaining > 0)
            {
                var value = _window[(int)(_position - _copyDistance) & (WindowSize - 1)];
                _copyRemaining--;
                buffer[written++] = Output(value);
                continue;
            }

            var next = ReadFirstStageByte();
            switch (_state)
            {
                case 0:
                    if (next is Dle)
                    {
                        _state = 1;
                    }
                    else
                    {
                        buffer[written++] = Output((byte)next);
                    }

                    break;

                case 1:
                    if (next is 0)
                    {
                        buffer[written++] = Output(Dle);
                        _state = 0;
                    }
                    else
                    {
                        _lengthByte = next;
                        _length = next & _lengthMask;
                        _state = _length == _lengthMask ? 2 : 3;
                    }

                    break;

                case 2:
                    _length += next;
                    _state = 3;
                    break;

                default:
                    _copyDistance = ((_lengthByte >> _distanceShift) << 8) + next + 1;
                    _copyRemaining = _length + 3;
                    _state = 0;
                    break;
            }
        }

        return written;
    }

    private byte Output(byte value)
    {
        _window[(int)_position & (WindowSize - 1)] = value;
        _position++;
        _remaining--;
        return value;
    }

    /// <summary>Reads the next byte of the first stage: a literal, or a follower of the previous byte.</summary>
    private int ReadFirstStageByte()
    {
        int count = _followerCounts[_lastByte];
        int value;
        if (count is 0 || _reader.Read(1) is not 0)
        {
            value = (int)_reader.Read(8);
        }
        else
        {
            var index = (int)_reader.Read(GetIndexBits(count));
            if (index >= count)
                throw new InvalidDataException("The reduced data contain an invalid follower index.");

            value = _followers[(_lastByte * 64) + index];
        }

        _lastByte = value;
        return value;
    }

    private void ReadFollowerSets()
    {
        _followersRead = true;
        for (var i = 255; i >= 0; i--)
        {
            var count = (int)_reader.Read(6);
            if (count > 32)
                throw new InvalidDataException("The reduced data have a follower set of more than 32 bytes.");

            _followerCounts[i] = (byte)count;
            for (var j = 0; j < count; j++)
            {
                _followers[(i * 64) + j] = (byte)_reader.Read(8);
            }
        }
    }

    // The number of bits of the index of a follower: the bits of count - 1, at least 1
    private static int GetIndexBits(int count) => count switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 8 => 3,
        <= 16 => 4,
        _ => 5,
    };

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
