// A port of the match finders of the LZMA SDK (LzFind.c) by Igor Pavlov, which is in the public domain.
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// The HC4 and BT4 match finders of the LZMA SDK. The input is appended to a buffer that keeps the dictionary before the
/// current position; positions in the hash tables are absolute, and are normalized before they overflow.
/// </summary>
internal sealed class LzmaMatchFinder : IDisposable
{
    private const int Hash2Size = 1 << 10;
    private const int Hash3Size = 1 << 16;
    private const int Fix3HashSize = Hash2Size;
    private const int Fix4HashSize = Hash2Size + Hash3Size;
    private const uint NormalizeLimit = 0xFFFF_FFF0u;
    private const uint NormalizeMask = ~((1u << 10) - 1);

    private static readonly uint[] CrcTable = CreateCrcTable();

    private readonly uint[] _hash;
    private readonly int _hashLength;
    private readonly uint[] _son;
    private readonly int _sonLength;
    private readonly uint _hashMask;
    private readonly uint _cyclicBufferSize;
    private readonly uint _cutValue;
    private readonly int _matchMaxLength;
    private readonly bool _binaryTree;
    private readonly int _keepSizeBefore;
    private byte[] _buffer;
    private int _bufferPosition;
    private int _streamEnd;
    private uint _position;
    private uint _cyclicBufferPosition;

    /// <param name="dictionarySize">The size of the history.</param>
    /// <param name="keepBefore">The number of bytes to keep before the current position in addition to the dictionary.</param>
    /// <param name="matchMaxLength">The longest match to look for (the nice length).</param>
    /// <param name="cutValue">The maximum number of candidates to check per position.</param>
    /// <param name="binaryTree">Whether to use the binary tree (BT4) instead of the hash chain (HC4).</param>
    public LzmaMatchFinder(int dictionarySize, int keepBefore, int matchMaxLength, int cutValue, bool binaryTree)
    {
        _binaryTree = binaryTree;
        _matchMaxLength = matchMaxLength;
        _cutValue = (uint)cutValue;

        // The binary tree stores two links per position of the dictionary, and arrays are limited to 2 GB
        var maxDictionary = (binaryTree ? Array.MaxLength / 2 : Array.MaxLength) - 1;
        dictionarySize = Math.Min(dictionarySize, maxDictionary);
        _cyclicBufferSize = (uint)dictionarySize + 1;

        var hs = (uint)dictionarySize - 1;
        hs |= hs >> 1;
        hs |= hs >> 2;
        hs |= hs >> 4;
        hs |= hs >> 8;
        hs >>= 1;
        hs |= 0xFFFF;
        if (hs > (1 << 24))
        {
            hs >>= 1;
        }

        _hashMask = hs;
        _hashLength = (int)hs + 1 + Fix4HashSize;
        _hash = ArrayPool<uint>.Shared.Rent(_hashLength);
        Array.Clear(_hash, 0, _hashLength);

        _sonLength = (int)(binaryTree ? _cyclicBufferSize * 2 : _cyclicBufferSize);
        _son = ArrayPool<uint>.Shared.Rent(_sonLength);

        _keepSizeBefore = dictionarySize + keepBefore + 1;
        var keepSizeAfter = LzmaEncoder.LookAhead;
        var reserve = (dictionarySize / 2) + (1 << 19);
        _buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min((long)_keepSizeBefore + keepSizeAfter + reserve, Array.MaxLength));

        _position = _cyclicBufferSize;
        _cyclicBufferPosition = 0;
    }

    /// <summary>Gets the buffer that contains the input.</summary>
    public byte[] Buffer => _buffer;

    /// <summary>Gets the position of the current byte in <see cref="Buffer"/>.</summary>
    public int BufferPosition => _bufferPosition;

    /// <summary>Gets the number of input bytes after the current position.</summary>
    public int Available => _streamEnd - _bufferPosition;

    /// <summary>Gets the dictionary size of the match finder.</summary>
    public int DictionarySize => (int)_cyclicBufferSize - 1;

    /// <summary>Appends input after the data of the buffer, discarding the data that is no longer needed when the buffer is full.</summary>
    /// <returns>The number of bytes appended: 0 when the buffer is full of data that is still needed.</returns>
    public int Append(ReadOnlySpan<byte> data)
    {
        if (_streamEnd == _buffer.Length)
        {
            var offset = _bufferPosition - _keepSizeBefore;
            if (offset > 0)
            {
                _buffer.AsSpan(offset, _streamEnd - offset).CopyTo(_buffer);
                _bufferPosition -= offset;
                _streamEnd -= offset;
            }
        }

        var count = Math.Min(data.Length, _buffer.Length - _streamEnd);
        data[..count].CopyTo(_buffer.AsSpan(_streamEnd));
        _streamEnd += count;
        return count;
    }

    /// <summary>Finds the matches at the current position, then moves to the next position.</summary>
    /// <param name="distances">Receives the pairs (length, distance - 1), by increasing length.</param>
    /// <returns>The number of values written to <paramref name="distances"/> (twice the number of matches).</returns>
    public int GetMatches(uint[] distances)
    {
        var lenLimit = (uint)Math.Min(_streamEnd - _bufferPosition, _matchMaxLength);
        if (lenLimit < 4)
        {
            MovePosition();
            return 0;
        }

        var buffer = _buffer;
        var cur = _bufferPosition;
        var temp = CrcTable[buffer[cur]] ^ buffer[cur + 1];
        var hash2Value = temp & (Hash2Size - 1);
        var hash3Value = (temp ^ ((uint)buffer[cur + 2] << 8)) & (Hash3Size - 1);
        var hashValue = (temp ^ ((uint)buffer[cur + 2] << 8) ^ (CrcTable[buffer[cur + 3]] << 5)) & _hashMask;

        var hash = _hash;
        var pos = _position;
        var delta2 = pos - hash[hash2Value];
        var delta3 = pos - hash[Fix3HashSize + hash3Value];
        var curMatch = hash[Fix4HashSize + hashValue];
        hash[hash2Value] = pos;
        hash[Fix3HashSize + hash3Value] = pos;
        hash[Fix4HashSize + hashValue] = pos;

        uint maxLen = 1;
        var offset = 0;
        if (delta2 < _cyclicBufferSize && buffer[cur - (int)delta2] == buffer[cur])
        {
            distances[0] = maxLen = 2;
            distances[1] = delta2 - 1;
            offset = 2;
        }

        if (delta2 != delta3 && delta3 < _cyclicBufferSize && buffer[cur - (int)delta3] == buffer[cur])
        {
            maxLen = 3;
            distances[offset + 1] = delta3 - 1;
            offset += 2;
            delta2 = delta3;
        }

        if (offset != 0)
        {
            var back = cur - (int)delta2;
            for (; maxLen != lenLimit; maxLen++)
            {
                if (buffer[back + (int)maxLen] != buffer[cur + (int)maxLen])
                    break;
            }

            distances[offset - 2] = maxLen;
            if (maxLen == lenLimit)
            {
                if (_binaryTree)
                {
                    SkipMatchesSpec(lenLimit, curMatch);
                }
                else
                {
                    _son[_cyclicBufferPosition] = curMatch;
                }

                MovePosition();
                return offset;
            }
        }

        if (maxLen < 3)
        {
            maxLen = 3;
        }

        offset = _binaryTree ? GetMatchesSpecBinaryTree(lenLimit, curMatch, distances, offset, maxLen) : GetMatchesSpecHashChain(lenLimit, curMatch, distances, offset, maxLen);
        MovePosition();
        return offset;
    }

    /// <summary>Inserts the next <paramref name="count"/> positions in the hash tables without returning their matches.</summary>
    public void Skip(uint count)
    {
        var buffer = _buffer;
        var hash = _hash;
        do
        {
            var lenLimit = (uint)Math.Min(_streamEnd - _bufferPosition, _matchMaxLength);
            if (lenLimit < 4)
            {
                MovePosition();
                continue;
            }

            var cur = _bufferPosition;
            var temp = CrcTable[buffer[cur]] ^ buffer[cur + 1];
            var hash2Value = temp & (Hash2Size - 1);
            var hash3Value = (temp ^ ((uint)buffer[cur + 2] << 8)) & (Hash3Size - 1);
            var hashValue = (temp ^ ((uint)buffer[cur + 2] << 8) ^ (CrcTable[buffer[cur + 3]] << 5)) & _hashMask;
            var curMatch = hash[Fix4HashSize + hashValue];
            hash[hash2Value] = _position;
            hash[Fix3HashSize + hash3Value] = _position;
            hash[Fix4HashSize + hashValue] = _position;
            if (_binaryTree)
            {
                SkipMatchesSpec(lenLimit, curMatch);
            }
            else
            {
                _son[_cyclicBufferPosition] = curMatch;
            }

            MovePosition();
        }
        while (--count != 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MovePosition()
    {
        _bufferPosition++;
        if (++_cyclicBufferPosition == _cyclicBufferSize)
        {
            _cyclicBufferPosition = 0;
        }

        if (++_position == NormalizeLimit)
        {
            Normalize();
        }
    }

    private void Normalize()
    {
        var subValue = (_position - _cyclicBufferSize) & NormalizeMask;
        NormalizeItems(_hash.AsSpan(0, _hashLength), subValue);
        NormalizeItems(_son.AsSpan(0, _sonLength), subValue);
        _position -= subValue;

        static void NormalizeItems(Span<uint> items, uint subValue)
        {
            for (var i = 0; i < items.Length; i++)
            {
                var value = items[i];
                items[i] = value <= subValue ? 0 : value - subValue;
            }
        }
    }

    private int GetMatchesSpecHashChain(uint lenLimit, uint curMatch, uint[] distances, int offset, uint maxLen)
    {
        var buffer = _buffer;
        var son = _son;
        var cur = _bufferPosition;
        var pos = _position;
        var cyclicBufferPos = _cyclicBufferPosition;
        var cyclicBufferSize = _cyclicBufferSize;
        var cutValue = _cutValue;
        son[cyclicBufferPos] = curMatch;
        while (true)
        {
            var delta = pos - curMatch;
            if (cutValue-- == 0 || delta >= cyclicBufferSize)
                return offset;

            var pb = cur - (int)delta;
            curMatch = son[cyclicBufferPos - delta + (delta > cyclicBufferPos ? cyclicBufferSize : 0)];
            if (buffer[pb + (int)maxLen] == buffer[cur + (int)maxLen] && buffer[pb] == buffer[cur])
            {
                var len = CommonLength(buffer, pb, cur, 1, lenLimit);
                if (maxLen < len)
                {
                    distances[offset++] = maxLen = len;
                    distances[offset++] = delta - 1;
                    if (len == lenLimit)
                        return offset;
                }
            }
        }
    }

    private int GetMatchesSpecBinaryTree(uint lenLimit, uint curMatch, uint[] distances, int offset, uint maxLen)
    {
        var buffer = _buffer;
        var son = _son;
        var cur = _bufferPosition;
        var pos = _position;
        var cyclicBufferPos = _cyclicBufferPosition;
        var cyclicBufferSize = _cyclicBufferSize;
        var cutValue = _cutValue;
        var ptr0 = (int)(cyclicBufferPos << 1) + 1;
        var ptr1 = (int)(cyclicBufferPos << 1);
        uint len0 = 0;
        uint len1 = 0;
        while (true)
        {
            var delta = pos - curMatch;
            if (cutValue-- == 0 || delta >= cyclicBufferSize)
            {
                son[ptr0] = son[ptr1] = 0;
                return offset;
            }

            var pair = (int)((cyclicBufferPos - delta + (delta > cyclicBufferPos ? cyclicBufferSize : 0)) << 1);
            var pb = cur - (int)delta;
            var len = Math.Min(len0, len1);
            if (buffer[pb + (int)len] == buffer[cur + (int)len])
            {
                len = CommonLength(buffer, pb, cur, len + 1, lenLimit);
                if (maxLen < len)
                {
                    distances[offset++] = maxLen = len;
                    distances[offset++] = delta - 1;
                    if (len == lenLimit)
                    {
                        son[ptr1] = son[pair];
                        son[ptr0] = son[pair + 1];
                        return offset;
                    }
                }
            }

            if (buffer[pb + (int)len] < buffer[cur + (int)len])
            {
                son[ptr1] = curMatch;
                ptr1 = pair + 1;
                curMatch = son[ptr1];
                len1 = len;
            }
            else
            {
                son[ptr0] = curMatch;
                ptr0 = pair;
                curMatch = son[ptr0];
                len0 = len;
            }
        }
    }

    private void SkipMatchesSpec(uint lenLimit, uint curMatch)
    {
        var buffer = _buffer;
        var son = _son;
        var cur = _bufferPosition;
        var pos = _position;
        var cyclicBufferPos = _cyclicBufferPosition;
        var cyclicBufferSize = _cyclicBufferSize;
        var cutValue = _cutValue;
        var ptr0 = (int)(cyclicBufferPos << 1) + 1;
        var ptr1 = (int)(cyclicBufferPos << 1);
        uint len0 = 0;
        uint len1 = 0;
        while (true)
        {
            var delta = pos - curMatch;
            if (cutValue-- == 0 || delta >= cyclicBufferSize)
            {
                son[ptr0] = son[ptr1] = 0;
                return;
            }

            var pair = (int)((cyclicBufferPos - delta + (delta > cyclicBufferPos ? cyclicBufferSize : 0)) << 1);
            var pb = cur - (int)delta;
            var len = Math.Min(len0, len1);
            if (buffer[pb + (int)len] == buffer[cur + (int)len])
            {
                len = CommonLength(buffer, pb, cur, len + 1, lenLimit);
                if (len == lenLimit)
                {
                    son[ptr1] = son[pair];
                    son[ptr0] = son[pair + 1];
                    return;
                }
            }

            if (buffer[pb + (int)len] < buffer[cur + (int)len])
            {
                son[ptr1] = curMatch;
                ptr1 = pair + 1;
                curMatch = son[ptr1];
                len1 = len;
            }
            else
            {
                son[ptr0] = curMatch;
                ptr0 = pair;
                curMatch = son[ptr0];
                len0 = len;
            }
        }
    }

    /// <summary>Returns the length of the common prefix of two positions, from <paramref name="start"/> up to <paramref name="limit"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint CommonLength(byte[] buffer, int a, int b, uint start, uint limit)
    {
        if (start >= limit)
            return limit;

        var length = (int)limit - (int)start;
        var mismatch = buffer.AsSpan(a + (int)start, length).CommonPrefixLength(buffer.AsSpan(b + (int)start, length));
        return start + (uint)mismatch;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var r = i;
            for (var j = 0; j < 8; j++)
            {
                r = (r >> 1) ^ (0xEDB88320u & (0 - (r & 1)));
            }

            table[i] = r;
        }

        return table;
    }

    public void Dispose()
    {
        ArrayPool<uint>.Shared.Return(_hash);
        ArrayPool<uint>.Shared.Return(_son);
        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }
    }
}
