using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.ZipLegacy;

/// <summary>
/// Decodes the implode method of zip (method 6, PKWARE APPNOTE section 5.3): a sliding window of 4 or 8 KiB and 2 or 3
/// Shannon-Fano trees. The data start with the trees (the literal tree when there is one, the length tree and the distance
/// tree), each written as runs of bit lengths. Then each item starts with a bit: 1 for a literal (8 bits, or a code of the
/// literal tree), 0 for a copy: the low bits of the distance (6 or 7), its high bits from the distance tree, and the length
/// from the length tree (the code 63 is followed by 8 more bits), plus 2, or 3 with a literal tree.
/// </summary>
/// <remarks>
/// The trees are canonical Huffman codes (the codes of each length follow those of the shorter lengths, in the order of the
/// values) whose bits are complemented. The bytes before the start of the data read as zeros, as Info-ZIP does.
/// </remarks>
internal sealed class ZipImplodeDecoderStream : ReadOnlyStreamBase
{
    private const int WindowSize = 1 << 13;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly LsbBitReader _reader;
    private readonly bool _literalTree;
    private readonly int _distanceLowBits;
    private readonly int _minimumLength;
    private readonly LsbHuffmanDecoder _literalCode = new(256);
    private readonly LsbHuffmanDecoder _lengthCode = new(64);
    private readonly LsbHuffmanDecoder _distanceCode = new(64);
    private readonly byte[] _window = new byte[WindowSize];
    private long _position;
    private long _remaining;
    private bool _treesRead;
    private int _copyRemaining;
    private int _copyDistance;
    private bool _disposed;

    public ZipImplodeDecoderStream(Stream input, bool largeWindow, bool literalTree, long outputLength, bool leaveOpen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        _input = input;
        _leaveOpen = leaveOpen;
        _remaining = outputLength;
        _literalTree = literalTree;
        _distanceLowBits = largeWindow ? 7 : 6;
        _minimumLength = literalTree ? 3 : 2;
        _reader = new LsbBitReader(input, 4096);
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_treesRead && _remaining > 0)
        {
            ReadTrees();
        }

        var written = 0;
        while (written < buffer.Length && _remaining > 0)
        {
            if (_copyRemaining > 0)
            {
                var value = _position >= _copyDistance ? _window[(int)(_position - _copyDistance) & (WindowSize - 1)] : (byte)0;
                _copyRemaining--;
                buffer[written++] = Output(value);
                continue;
            }

            if (_reader.Read(1) is not 0)
            {
                var literal = _literalTree ? _literalCode.Decode(_reader) : (int)_reader.Read(8);
                buffer[written++] = Output((byte)literal);
                continue;
            }

            var low = (int)_reader.Read(_distanceLowBits);
            var high = _distanceCode.Decode(_reader);
            _copyDistance = ((high << _distanceLowBits) | low) + 1;
            var length = _lengthCode.Decode(_reader);
            if (length is 63)
            {
                length += (int)_reader.Read(8);
            }

            _copyRemaining = length + _minimumLength;
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

    private void ReadTrees()
    {
        _treesRead = true;
        Span<byte> lengths = stackalloc byte[256];
        if (_literalTree)
        {
            ReadTree(lengths);
            _literalCode.Build(lengths, inverted: true);
        }

        ReadTree(lengths[..64]);
        _lengthCode.Build(lengths[..64], inverted: true);
        ReadTree(lengths[..64]);
        _distanceCode.Build(lengths[..64], inverted: true);
    }

    /// <summary>Reads the bit lengths of a tree: a byte with the number of bytes minus 1, then bytes with a count minus 1 (high 4 bits) and a bit length minus 1 (low 4 bits).</summary>
    private void ReadTree(Span<byte> lengths)
    {
        var byteCount = (int)_reader.Read(8) + 1;
        var index = 0;
        for (var i = 0; i < byteCount; i++)
        {
            var value = (int)_reader.Read(8);
            var length = (value & 0x0F) + 1;
            var count = (value >> 4) + 1;
            if (index + count > lengths.Length)
                throw new InvalidDataException("The imploded data have a tree with too many values.");

            lengths.Slice(index, count).Fill((byte)length);
            index += count;
        }

        if (index != lengths.Length)
            throw new InvalidDataException("The imploded data have a tree with too few values.");
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
