// A port of the LZMA decoder of the LZMA SDK (LzmaDec.c) by Igor Pavlov, and of xz-utils (liblzma), which are in the public domain.
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// The LZMA decoder: the range decoder, the probability models and the LZ window. The window starts small and doubles up
/// to the dictionary size, so the memory is bounded by the data actually produced, not by the declared dictionary size.
/// The containers call <see cref="Decode"/> with a limit in the input buffer and a limit in the window, then copy the
/// decoded bytes out of <see cref="Window"/>.
/// </summary>
internal sealed class LzmaDecoder : IDisposable
{
    private const int InitialWindowSize = 1 << 16;
    private const uint TopValue = 1u << 24;
    private const int NumBitModelTotalBits = 11;
    private const uint BitModelTotal = 1u << NumBitModelTotalBits;
    private const int NumMoveBits = 5;

    // The layout of the probabilities (LzmaDec.c)
    private const int IsMatch = 0;
    private const int IsRep = IsMatch + (LzmaConstants.NumStates << LzmaConstants.NumPosBitsMax);
    private const int IsRepG0 = IsRep + LzmaConstants.NumStates;
    private const int IsRepG1 = IsRepG0 + LzmaConstants.NumStates;
    private const int IsRepG2 = IsRepG1 + LzmaConstants.NumStates;
    private const int IsRep0Long = IsRepG2 + LzmaConstants.NumStates;
    private const int PosSlot = IsRep0Long + (LzmaConstants.NumStates << LzmaConstants.NumPosBitsMax);
    private const int SpecPos = PosSlot + (LzmaConstants.NumLenToPosStates << LzmaConstants.NumPosSlotBits);
    private const int Align = SpecPos + LzmaConstants.NumFullDistances - LzmaConstants.EndPosModelIndex;
    private const int LenCoder = Align + LzmaConstants.AlignTableSize;
    private const int RepLenCoder = LenCoder + LzmaConstants.NumLenProbs;
    private const int Literal = RepLenCoder + LzmaConstants.NumLenProbs;

    // The layout of a length decoder
    private const int LenChoice = 0;
    private const int LenChoice2 = 1;
    private const int LenLow = 2;
    private const int LenMid = LenLow + (LzmaConstants.NumPosStatesMax << LzmaConstants.LenNumLowBits);
    private const int LenHigh = LenMid + (LzmaConstants.NumPosStatesMax << LzmaConstants.LenNumMidBits);

    private ushort[] _probs = [];
    private int _lc;
    private uint _lpMask;
    private uint _posMask;

    private uint _state;
    private uint _rep0;
    private uint _rep1;
    private uint _rep2;
    private uint _rep3;
    private int _remainLen;

    private uint _range;
    private uint _code;

    private byte[] _window = [];
    private int _windowCapacity;
    private int _windowPosition;
    private bool _windowFull;
    private uint _processedPosition;
    private readonly int _maxWindowSize;

    /// <param name="dictionarySize">The dictionary size of the stream.</param>
    /// <param name="maxOutputLength">The length of the data when it is known (the window is never larger), or -1.</param>
    /// <param name="maxMemoryUsage">The largest window the decoder may allocate (<see cref="ArchiveReaderOptions.MaxMemoryUsage"/>).</param>
    /// <exception cref="InvalidDataException">The window the data need is larger than <paramref name="maxMemoryUsage"/>.</exception>
    public LzmaDecoder(uint dictionarySize, long maxOutputLength, long maxMemoryUsage = long.MaxValue)
    {
        var max = (long)Math.Max(dictionarySize, 4096u);
        if (maxOutputLength >= 0)
        {
            max = Math.Min(max, Math.Max(maxOutputLength, 1));
        }

        // Arrays are limited to about 2 GB: a larger dictionary is useless with a window of the data actually produced
        _maxWindowSize = (int)Math.Min(max, Array.MaxLength);
        if (_maxWindowSize > maxMemoryUsage)
            throw new InvalidDataException($"The LZMA dictionary needs {_maxWindowSize} bytes, more than the maximum memory usage ({maxMemoryUsage} bytes).");

        _windowCapacity = Math.Min(_maxWindowSize, InitialWindowSize);
        _window = ArrayPool<byte>.Shared.Rent(_windowCapacity);
    }

    /// <summary>Gets the window that contains the decoded data.</summary>
    public byte[] Window => _window;

    /// <summary>Gets the position of the next decoded byte in <see cref="Window"/>.</summary>
    public int WindowPosition => _windowPosition;

    /// <summary>Gets the size of the window: <see cref="WindowPosition"/> never exceeds it.</summary>
    public int WindowCapacity => _windowCapacity;

    /// <summary>Gets a value indicating whether a match was cut by the limit of the last call to <see cref="Decode"/>.</summary>
    public bool HasPendingMatch => _remainLen > 0;

    /// <summary>Gets a value indicating whether the range decoder ended in the state of a properly flushed encoder.</summary>
    public bool IsRangeDecoderFinished => _code == 0;

    /// <summary>Sets the lc/lp/pb properties, from the byte of the LZMA properties.</summary>
    public void SetProperties(int lc, int lp, int pb)
    {
        _lc = lc;
        _lpMask = (1u << lp) - 1;
        _posMask = (1u << pb) - 1;
        var count = Literal + (0x300 << (lc + lp));
        if (_probs.Length != count)
        {
            _probs = new ushort[count];
        }
    }

    /// <summary>Decodes the lc/lp/pb byte of the LZMA properties.</summary>
    public static bool TryDecodeProperties(byte value, out int lc, out int lp, out int pb)
    {
        if (value >= 9 * 5 * 5)
        {
            lc = lp = pb = 0;
            return false;
        }

        lc = value % 9;
        value /= 9;
        lp = value % 5;
        pb = value / 5;
        return true;
    }

    /// <summary>Resets the probabilities, the state and the repeated distances.</summary>
    public void ResetState()
    {
        _probs.AsSpan().Fill((ushort)(BitModelTotal >> 1));
        _state = 0;
        _rep0 = _rep1 = _rep2 = _rep3 = 0;
        _remainLen = 0;
    }

    /// <summary>Empties the dictionary. The caller must have copied the decoded bytes out of the window.</summary>
    public void ResetDictionary()
    {
        _windowPosition = 0;
        _windowFull = false;
        _processedPosition = 0;
        _remainLen = 0;
    }

    /// <summary>Makes room in the window when it is full: grows it up to the dictionary size, or wraps around.</summary>
    /// <remarks>The caller must have copied the decoded bytes out of the window.</remarks>
    public void MakeRoom()
    {
        if (_windowPosition < _windowCapacity)
            return;

        if (_windowCapacity < _maxWindowSize)
        {
            var newCapacity = (int)Math.Min((long)_windowCapacity * 2, _maxWindowSize);
            var newWindow = ArrayPool<byte>.Shared.Rent(newCapacity);
            _window.AsSpan(0, _windowPosition).CopyTo(newWindow);
            ArrayPool<byte>.Shared.Return(_window);
            _window = newWindow;
            _windowCapacity = newCapacity;
        }
        else
        {
            _windowPosition = 0;
            _windowFull = true;
        }
    }

    /// <summary>Initializes the range decoder from the 5 bytes at <paramref name="position"/>.</summary>
    public void InitializeRangeDecoder(byte[] input, ref int position)
    {
        if (input[position] != 0)
            throw new InvalidDataException("The LZMA data is corrupt: the range coder does not start with a zero byte.");

        _code = ((uint)input[position + 1] << 24) | ((uint)input[position + 2] << 16) | ((uint)input[position + 3] << 8) | input[position + 4];
        _range = uint.MaxValue;
        position += 5;
        if (_code == uint.MaxValue)
            throw new InvalidDataException("The LZMA data is corrupt.");
    }

    /// <summary>Copies uncompressed data (an uncompressed chunk of LZMA2) to the window.</summary>
    public void AppendUncompressed(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_window.AsSpan(_windowPosition));
        _windowPosition += data.Length;
        _processedPosition += (uint)data.Length;
    }

    /// <summary>
    /// Decodes symbols while the window position is before <paramref name="windowLimit"/> and the input position is not after
    /// <paramref name="inputLimit"/>. The input array must have <see cref="LzmaInputBuffer.Padding"/> readable bytes after
    /// <paramref name="inputLimit"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the end marker was decoded.</returns>
    [SkipLocalsInit]
    public bool Decode(byte[] input, ref int inputPosition, int inputLimit, int windowLimit)
    {
        var window = _window;
        var capacity = _windowCapacity;
        var position = _windowPosition;
        var processed = _processedPosition;
        var full = _windowFull;
        var rep0 = _rep0;

        if (_remainLen > 0)
        {
            var length = Math.Min(_remainLen, windowLimit - position);
            CopyMatch(window, capacity, ref position, rep0, length);
            processed += (uint)length;
            _remainLen -= length;
        }

        var probs = _probs;
        var lc = _lc;
        var lpMask = _lpMask;
        var posMask = _posMask;
        var state = _state;
        var rep1 = _rep1;
        var rep2 = _rep2;
        var rep3 = _rep3;
        var range = _range;
        var code = _code;
        var inPos = inputPosition;
        var endMarker = false;
        var remainLen = _remainLen;

        while (position < windowLimit && inPos <= inputLimit)
        {
            var posState = processed & posMask;
            if (DecodeBit(probs, IsMatch + (int)((state << LzmaConstants.NumPosBitsMax) + posState), ref range, ref code, input, ref inPos) == 0)
            {
                uint previousByte = position > 0 ? window[position - 1] : full ? window[capacity - 1] : 0u;
                var literal = Literal + (0x300 * (int)(((processed & lpMask) << lc) + (previousByte >> (8 - lc))));
                uint symbol = 1;
                if (state < 7)
                {
                    do
                    {
                        symbol = (symbol << 1) | DecodeBitBranchless(probs, literal + (int)symbol, ref range, ref code, input, ref inPos);
                    }
                    while (symbol < 0x100);
                }
                else
                {
                    var source = position - (int)rep0 - 1;
                    if (source < 0)
                    {
                        source += capacity;
                    }

                    uint matchByte = window[source];
                    uint offset = 0x100;
                    do
                    {
                        matchByte <<= 1;
                        var matchBit = matchByte & offset;
                        var probIndex = literal + (int)(offset + matchBit + symbol);
                        var bit = DecodeBitBranchless(probs, probIndex, ref range, ref code, input, ref inPos);
                        symbol = (symbol << 1) | bit;

                        // offset &= bit is 0 ? ~matchBit : matchBit
                        offset &= ~(matchBit ^ (0 - bit));
                    }
                    while (symbol < 0x100);
                }

                window[position++] = (byte)symbol;
                processed++;
                state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                continue;
            }

            uint length;
            if (DecodeBit(probs, IsRep + (int)state, ref range, ref code, input, ref inPos) == 0)
            {
                // A match with a new distance
                state = state < 7 ? 7u : 10u;
                length = DecodeLength(probs, LenCoder, posState, ref range, ref code, input, ref inPos);
                var lenToPosState = length < LzmaConstants.NumLenToPosStates ? length : LzmaConstants.NumLenToPosStates - 1;
                var slotProbs = PosSlot + (int)(lenToPosState << LzmaConstants.NumPosSlotBits);
                uint slot = 1;
                do
                {
                    slot = (slot << 1) | DecodeBit(probs, slotProbs + (int)slot, ref range, ref code, input, ref inPos);
                }
                while (slot < (1u << LzmaConstants.NumPosSlotBits));

                slot -= 1u << LzmaConstants.NumPosSlotBits;
                uint distance;
                if (slot < LzmaConstants.StartPosModelIndex)
                {
                    distance = slot;
                }
                else
                {
                    var numDirectBits = (int)(slot >> 1) - 1;
                    distance = (2 | (slot & 1)) << numDirectBits;
                    if (slot < LzmaConstants.EndPosModelIndex)
                    {
                        var specProbs = SpecPos + (int)distance - (int)slot - 1;
                        uint m = 1;
                        for (var i = 0; i < numDirectBits; i++)
                        {
                            var bit = DecodeBitBranchless(probs, specProbs + (int)m, ref range, ref code, input, ref inPos);
                            m = (m << 1) | bit;
                            distance |= bit << i;
                        }
                    }
                    else
                    {
                        distance = 2 | (slot & 1);
                        for (var i = numDirectBits - LzmaConstants.NumAlignBits; i > 0; i--)
                        {
                            range >>= 1;
                            code -= range;
                            var t = 0 - (code >> 31);
                            distance = (distance << 1) + (t + 1);
                            code += range & t;
                            if (range < TopValue)
                            {
                                range <<= 8;
                                code = (code << 8) | input[inPos++];
                            }
                        }

                        distance <<= LzmaConstants.NumAlignBits;
                        uint m = 1;
                        for (var i = 0; i < LzmaConstants.NumAlignBits; i++)
                        {
                            var bit = DecodeBitBranchless(probs, Align + (int)m, ref range, ref code, input, ref inPos);
                            m = (m << 1) | bit;
                            distance |= bit << i;
                        }

                        if (distance == uint.MaxValue)
                        {
                            endMarker = true;
                            break;
                        }
                    }
                }

                rep3 = rep2;
                rep2 = rep1;
                rep1 = rep0;
                rep0 = distance;
            }
            else
            {
                if (DecodeBit(probs, IsRepG0 + (int)state, ref range, ref code, input, ref inPos) == 0)
                {
                    if (DecodeBit(probs, IsRep0Long + (int)((state << LzmaConstants.NumPosBitsMax) + posState), ref range, ref code, input, ref inPos) == 0)
                    {
                        // A "short rep": one byte at the last distance
                        if (rep0 >= (full ? (uint)capacity : (uint)position))
                            throw CorruptData();

                        state = state < 7 ? 9u : 11u;
                        var source = position - (int)rep0 - 1;
                        if (source < 0)
                        {
                            source += capacity;
                        }

                        window[position++] = window[source];
                        processed++;
                        continue;
                    }
                }
                else
                {
                    uint distance;
                    if (DecodeBit(probs, IsRepG1 + (int)state, ref range, ref code, input, ref inPos) == 0)
                    {
                        distance = rep1;
                    }
                    else
                    {
                        if (DecodeBit(probs, IsRepG2 + (int)state, ref range, ref code, input, ref inPos) == 0)
                        {
                            distance = rep2;
                        }
                        else
                        {
                            distance = rep3;
                            rep3 = rep2;
                        }

                        rep2 = rep1;
                    }

                    rep1 = rep0;
                    rep0 = distance;
                }

                state = state < 7 ? 8u : 11u;
                length = DecodeLength(probs, RepLenCoder, posState, ref range, ref code, input, ref inPos);
            }

            if (rep0 >= (full ? (uint)capacity : (uint)position))
                throw CorruptData();

            var matchLength = (int)length + LzmaConstants.MatchLenMin;
            var count = Math.Min(matchLength, windowLimit - position);
            CopyMatch(window, capacity, ref position, rep0, count);
            processed += (uint)count;
            remainLen = matchLength - count;
        }

        _state = state;
        _rep0 = rep0;
        _rep1 = rep1;
        _rep2 = rep2;
        _rep3 = rep3;
        _range = range;
        _code = code;
        _remainLen = remainLen;
        _windowPosition = position;
        _processedPosition = processed;
        inputPosition = inPos;
        return endMarker;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyMatch(byte[] window, int capacity, ref int position, uint distance, int length)
    {
        var source = position - (int)distance - 1;
        if (source < 0)
        {
            source += capacity;
        }

        var destination = position;
        position += length;
        if (source < destination ? source + length <= destination : source + length <= capacity)
        {
            if (length <= 8)
            {
                for (var i = 0; i < length; i++)
                {
                    window[destination + i] = window[source + i];
                }
            }
            else
            {
                window.AsSpan(source, length).CopyTo(window.AsSpan(destination, length));
            }

            return;
        }

        if (distance == 0)
        {
            window.AsSpan(destination, length).Fill(window[source]);
            return;
        }

        for (var i = 0; i < length; i++)
        {
            window[destination + i] = window[source];
            if (++source == capacity)
            {
                source = 0;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DecodeBit(ushort[] probs, int index, ref uint range, ref uint code, byte[] input, ref int inPos)
    {
        uint prob = probs[index];
        var bound = (range >> NumBitModelTotalBits) * prob;
        uint bit;
        if (code < bound)
        {
            range = bound;
            probs[index] = (ushort)(prob + ((BitModelTotal - prob) >> NumMoveBits));
            bit = 0;
        }
        else
        {
            range -= bound;
            code -= bound;
            probs[index] = (ushort)(prob - (prob >> NumMoveBits));
            bit = 1;
        }

        if (range < TopValue)
        {
            range <<= 8;
            code = (code << 8) | input[inPos++];
        }

        return bit;
    }

    /// <summary>Decodes a bit as <see cref="DecodeBit"/> does, without a branch on its value: for the bits of the literals and of the low bits of the distances, which are hard to predict (the branches are faster for the other bits, which are more regular).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DecodeBitBranchless(ushort[] probs, int index, ref uint range, ref uint code, byte[] input, ref int inPos)
    {
        uint prob = probs[index];
        var bound = (range >> NumBitModelTotalBits) * prob;
        var bit = code >= bound ? 1u : 0u;
        var mask = 0 - bit; // All ones for a 1, zero for a 0
        code -= bound & mask;
        range = (bound & ~mask) | ((range - bound) & mask);

        // A 0 moves the probability towards BitModelTotal, and a 1 towards 0: prob - (prob >> NumMoveBits) is
        // prob + ((31 - prob) >> NumMoveBits) with an arithmetic shift
        var target = ((1u << NumMoveBits) - 1) + (~mask & (BitModelTotal - ((1u << NumMoveBits) - 1)));
        probs[index] = (ushort)(prob + (uint)((int)(target - prob) >> NumMoveBits));

        if (range < TopValue)
        {
            range <<= 8;
            code = (code << 8) | input[inPos++];
        }

        return bit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DecodeLength(ushort[] probs, int coder, uint posState, ref uint range, ref uint code, byte[] input, ref int inPos)
    {
        int treeProbs;
        int numBits;
        uint offset;
        if (DecodeBit(probs, coder + LenChoice, ref range, ref code, input, ref inPos) == 0)
        {
            treeProbs = coder + LenLow + (int)(posState << LzmaConstants.LenNumLowBits);
            numBits = LzmaConstants.LenNumLowBits;
            offset = 0;
        }
        else if (DecodeBit(probs, coder + LenChoice2, ref range, ref code, input, ref inPos) == 0)
        {
            treeProbs = coder + LenMid + (int)(posState << LzmaConstants.LenNumMidBits);
            numBits = LzmaConstants.LenNumMidBits;
            offset = LzmaConstants.LenNumLowSymbols;
        }
        else
        {
            treeProbs = coder + LenHigh;
            numBits = LzmaConstants.LenNumHighBits;
            offset = LzmaConstants.LenNumLowSymbols + LzmaConstants.LenNumMidSymbols;
        }

        uint symbol = 1;
        var limit = 1u << numBits;
        do
        {
            symbol = (symbol << 1) | DecodeBit(probs, treeProbs + (int)symbol, ref range, ref code, input, ref inPos);
        }
        while (symbol < limit);

        return symbol - limit + offset;
    }

    private static InvalidDataException CorruptData() => new("The LZMA data is corrupt: a match refers to data before the start of the dictionary.");

    public void Dispose()
    {
        if (_window.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_window);
            _window = [];
        }
    }
}
