// A port of the LZMA encoder of the LZMA SDK (LzmaEnc.c) by Igor Pavlov, which is in the public domain: the optimal parser
// (normal mode) and the greedy parser (fast mode), with the price tables of the SDK.
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// The LZMA encoder. The input is appended with <see cref="Append"/>; <see cref="CodeOneBlock"/> encodes while enough input
/// is buffered (<see cref="LookAhead"/> bytes, so the parser sees what it would see with the whole input), and the encoded
/// bytes accumulate in <see cref="RangeEncoder"/>. The match finder is created once the dictionary is full or the input
/// ends, so a small input does not allocate the tables of a large dictionary.
/// </summary>
internal sealed class LzmaEncoder : IDisposable
{
    /// <summary>The number of bytes of input the encoder needs after the current position, except at the end of the input.</summary>
    public const int LookAhead = NumOpts + (2 * LzmaConstants.MatchLenMax) + 16;

    private const int NumOpts = 1 << 12;
    private const int NumMoveReducingBits = 4;
    private const int NumBitPriceShiftBits = 4;
    private const uint BitModelTotal = 1 << 11;
    private const uint InfinityPrice = 1 << 30;
    private const uint NotALiteral = uint.MaxValue;
    private const int DistTableSizeMax = 64;
    private const int NumLenPrices = LzmaConstants.LenNumSymbolsTotal;

    // The layout of the probabilities, the one of the decoder
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

    private const int LenChoice = 0;
    private const int LenChoice2 = 1;
    private const int LenLow = 2;
    private const int LenMid = LenLow + (LzmaConstants.NumPosStatesMax << LzmaConstants.LenNumLowBits);
    private const int LenHigh = LenMid + (LzmaConstants.NumPosStatesMax << LzmaConstants.LenNumMidBits);

    private static readonly uint[] ProbPrices = CreateProbPrices();
    private static readonly byte[] LiteralNextStates = [0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 4, 5];
    private static readonly byte[] MatchNextStates = [7, 7, 7, 7, 7, 7, 7, 10, 10, 10, 10, 10];
    private static readonly byte[] RepNextStates = [8, 8, 8, 8, 8, 8, 8, 11, 11, 11, 11, 11];
    private static readonly byte[] ShortRepNextStates = [9, 9, 9, 9, 9, 9, 9, 11, 11, 11, 11, 11];

    private readonly LzmaEncoderSettings _settings;
    private readonly int _lc;
    private readonly uint _lpMask;
    private readonly uint _pbMask;
    private readonly int _numFastBytes;
    private readonly bool _fastMode;
    private readonly int _keepBefore;
    private readonly ushort[] _probs;
    private ushort[]? _savedProbs;
    private uint _savedState;
    private readonly uint[] _savedReps = new uint[LzmaConstants.NumReps];

    private LzmaMatchFinder? _matchFinder;
    private byte[]? _pendingInput;
    private int _pendingLength;

    private uint _state;
    private readonly uint[] _reps = new uint[LzmaConstants.NumReps];

    private readonly Optimal[] _opt = new Optimal[NumOpts];
    private uint _optimumEndIndex;
    private uint _optimumCurrentIndex;
    private uint _longestMatchLength;
    private uint _numPairs;
    private uint _numAvail;
    private uint _additionalOffset;
    private readonly uint[] _matches = new uint[(LzmaConstants.MatchLenMax * 2) + 2 + 1];

    private uint _distTableSize;
    private readonly uint[] _posSlotPrices = new uint[LzmaConstants.NumLenToPosStates * DistTableSizeMax];
    private readonly uint[] _distancesPrices = new uint[LzmaConstants.NumLenToPosStates * LzmaConstants.NumFullDistances];
    private readonly uint[] _alignPrices = new uint[LzmaConstants.AlignTableSize];
    private uint _alignPriceCount;
    private uint _matchPriceCount;

    private readonly uint[] _lenPrices = new uint[LzmaConstants.NumPosStatesMax * NumLenPrices];
    private readonly uint[] _lenCounters = new uint[LzmaConstants.NumPosStatesMax];
    private readonly uint[] _repLenPrices = new uint[LzmaConstants.NumPosStatesMax * NumLenPrices];
    private readonly uint[] _repLenCounters = new uint[LzmaConstants.NumPosStatesMax];
    private uint _lenTableSize;

    private long _nowPos64;
    private long _blockStart;

    /// <param name="settings">The settings, which are validated.</param>
    /// <param name="keepBefore">The number of encoded bytes the caller reads back from <see cref="InputBuffer"/> (the uncompressed chunks of LZMA2).</param>
    public LzmaEncoder(LzmaEncoderSettings settings, int keepBefore = 0)
    {
        settings.Validate();
        _settings = settings.Clone();
        _lc = settings.LiteralContextBits;
        _lpMask = (1u << settings.LiteralPositionBits) - 1;
        _pbMask = (1u << settings.PositionBits) - 1;
        _numFastBytes = settings.NiceLength;
        _fastMode = settings.Mode is LzmaEncoderMode.Fast;
        _keepBefore = keepBefore;
        _probs = new ushort[Literal + (0x300 << (settings.LiteralContextBits + settings.LiteralPositionBits))];
        ResetState();
    }

    /// <summary>Gets the range encoder that holds the encoded bytes.</summary>
    public LzmaRangeEncoder RangeEncoder { get; } = new();

    /// <summary>Gets the number of bytes encoded since the creation of the encoder.</summary>
    public long Position => _nowPos64;

    /// <summary>Gets the input buffer: the encoded bytes end at <see cref="InputPosition"/> when <see cref="CodeOneBlock"/> returns at the end of a block.</summary>
    public byte[] InputBuffer => _matchFinder!.Buffer;

    /// <summary>Gets the position, in <see cref="InputBuffer"/>, of the first byte that is not encoded.</summary>
    public int InputPosition => _matchFinder!.BufferPosition - (int)_additionalOffset;

    /// <summary>Gets a value indicating whether the match finder exists: the encoder buffers the input until then.</summary>
    public bool HasMatchFinder => _matchFinder is not null;

    /// <summary>Resets the probabilities, the state and the repeated distances (LzmaEnc_Init).</summary>
    public void ResetState()
    {
        _probs.AsSpan().Fill((ushort)(BitModelTotal >> 1));
        _state = 0;
        Array.Clear(_reps);
        _optimumEndIndex = 0;
        _optimumCurrentIndex = 0;
        _additionalOffset = 0;
    }

    /// <summary>Saves the probabilities and the state, before an LZMA2 chunk that may be stored uncompressed.</summary>
    public void SaveState()
    {
        _savedProbs ??= new ushort[_probs.Length];
        _probs.AsSpan().CopyTo(_savedProbs);
        _savedState = _state;
        _reps.AsSpan().CopyTo(_savedReps);
    }

    /// <summary>Restores the state of <see cref="SaveState"/>: the decoder does not see the chunk that was stored uncompressed.</summary>
    public void RestoreState()
    {
        _savedProbs.AsSpan().CopyTo(_probs);
        _state = _savedState;
        _savedReps.AsSpan().CopyTo(_reps);
    }

    /// <summary>Appends input.</summary>
    /// <returns>The number of bytes appended: the caller encodes (<see cref="CodeOneBlock"/>) and appends the rest.</returns>
    public int Append(ReadOnlySpan<byte> data)
    {
        if (_matchFinder is not null)
            return _matchFinder.Append(data);

        var dictionarySize = _settings.DictionarySize;
        var count = Math.Min(data.Length, dictionarySize - _pendingLength);
        if (_pendingInput is null || _pendingInput.Length < _pendingLength + count)
        {
            var newLength = Math.Max(Math.Max(_pendingLength + count, 4096), (int)Math.Min((_pendingInput?.Length ?? 0) * 2L, dictionarySize));
            Array.Resize(ref _pendingInput, newLength);
        }

        data[..count].CopyTo(_pendingInput.AsSpan(_pendingLength));
        _pendingLength += count;
        if (_pendingLength == dictionarySize)
        {
            CreateMatchFinder(dictionarySize);
        }

        return count;
    }

    /// <summary>Creates the match finder when the input ends before the dictionary is full, with a dictionary of the size of the input.</summary>
    public void EndInput()
    {
        if (_matchFinder is null)
        {
            CreateMatchFinder(Math.Max(_pendingLength, LzmaConstants.MinDictionarySize));
        }
    }

    /// <summary>Creates the match finder with the dictionary size of the settings, before a flush when more input may follow.</summary>
    public void EnsureMatchFinder()
    {
        if (_matchFinder is null)
        {
            CreateMatchFinder(_settings.DictionarySize);
        }
    }

    private void CreateMatchFinder(int dictionarySize)
    {
        var depth = _settings.Depth;
        var binaryTree = _settings.MatchFinder is LzmaMatchFinderKind.BinaryTree4;
        if (depth == 0)
        {
            depth = binaryTree ? 16 + (_numFastBytes / 2) : 4 + (_numFastBytes / 4);
        }

        _matchFinder = new LzmaMatchFinder(dictionarySize, _keepBefore + NumOpts + 16, _numFastBytes, depth, binaryTree);
        uint i;
        for (i = 0; i < 31; i++)
        {
            if ((uint)_matchFinder.DictionarySize <= (1u << (int)i))
                break;
        }

        _distTableSize = i * 2;

        var pending = _pendingInput.AsSpan(0, _pendingLength);
        while (!pending.IsEmpty)
        {
            pending = pending[_matchFinder.Append(pending)..];
        }

        _pendingInput = null;
        _pendingLength = 0;
    }

    /// <summary>Prepares the price tables for a new block (LzmaEnc_InitPrices).</summary>
    public void BeginBlock()
    {
        if (!_fastMode)
        {
            FillDistancesPrices();
            FillAlignPrices();
        }

        _lenTableSize = (uint)(_numFastBytes + 1 - LzmaConstants.MatchLenMin);
        var numPosStates = _pbMask + 1;
        for (uint posState = 0; posState < numPosStates; posState++)
        {
            UpdateLenPrices(LenCoder, _lenPrices, _lenCounters, posState);
            UpdateLenPrices(RepLenCoder, _repLenPrices, _repLenCounters, posState);
        }

        _blockStart = _nowPos64;
    }

    /// <summary>
    /// Encodes the buffered input (LzmaEnc_CodeOneBlock). Without <paramref name="finishing"/>, it stops when less than
    /// <see cref="LookAhead"/> bytes of input are buffered.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the block ends: all the input is encoded (with <paramref name="finishing"/>), or the
    /// limits are reached; <see langword="false"/> when more input is needed.
    /// </returns>
    public bool CodeOneBlock(bool finishing, bool useLimits, uint maxPackSize, uint maxUnpackSize)
    {
        var mf = _matchFinder;
        if (mf is null)
            return false;

        var rc = RangeEncoder;
        if (_nowPos64 == 0)
        {
            if (!finishing && mf.Available < LookAhead)
                return false;

            if (mf.Available == 0)
                return true;

            ReadMatchDistances(out _);
            rc.EncodeBit(_probs, IsMatch + (int)(_state << LzmaConstants.NumPosBitsMax), 0);
            _state = LiteralNextStates[_state];
            var curByte = mf.Buffer[mf.BufferPosition - (int)_additionalOffset];
            EncodeLiteral(rc, Literal, curByte);
            _additionalOffset--;
            _nowPos64++;
        }

        while (true)
        {
            if (_additionalOffset == 0)
            {
                if (mf.Available == 0)
                    return finishing;

                if (useLimits)
                {
                    var processed = (uint)(_nowPos64 - _blockStart);
                    if (processed + NumOpts + 300 >= maxUnpackSize || rc.Processed + (NumOpts * 2) >= maxPackSize)
                        return true;
                }
            }

            if (!finishing && (_fastMode || _optimumEndIndex == _optimumCurrentIndex) && mf.Available < LookAhead)
                return false;

            var nowPos32 = (uint)_nowPos64;
            uint pos;
            var len = _fastMode ? GetOptimumFast(out pos) : GetOptimum(nowPos32, out pos);
            var posState = nowPos32 & _pbMask;
            if (len == 1 && pos == NotALiteral)
            {
                rc.EncodeBit(_probs, IsMatch + (int)((_state << LzmaConstants.NumPosBitsMax) + posState), 0);
                var data = mf.BufferPosition - (int)_additionalOffset;
                var buffer = mf.Buffer;
                var curByte = buffer[data];
                var probs = LiteralProbs(nowPos32, buffer[data - 1]);
                if (_state < 7)
                {
                    EncodeLiteral(rc, probs, curByte);
                }
                else
                {
                    EncodeMatchedLiteral(rc, probs, curByte, buffer[data - (int)_reps[0] - 1]);
                }

                _state = LiteralNextStates[_state];
            }
            else
            {
                rc.EncodeBit(_probs, IsMatch + (int)((_state << LzmaConstants.NumPosBitsMax) + posState), 1);
                if (pos < LzmaConstants.NumReps)
                {
                    rc.EncodeBit(_probs, IsRep + (int)_state, 1);
                    if (pos == 0)
                    {
                        rc.EncodeBit(_probs, IsRepG0 + (int)_state, 0);
                        rc.EncodeBit(_probs, IsRep0Long + (int)((_state << LzmaConstants.NumPosBitsMax) + posState), len == 1 ? 0u : 1u);
                    }
                    else
                    {
                        var distance = _reps[pos];
                        rc.EncodeBit(_probs, IsRepG0 + (int)_state, 1);
                        if (pos == 1)
                        {
                            rc.EncodeBit(_probs, IsRepG1 + (int)_state, 0);
                        }
                        else
                        {
                            rc.EncodeBit(_probs, IsRepG1 + (int)_state, 1);
                            rc.EncodeBit(_probs, IsRepG2 + (int)_state, pos - 2);
                            if (pos == 3)
                            {
                                _reps[3] = _reps[2];
                            }

                            _reps[2] = _reps[1];
                        }

                        _reps[1] = _reps[0];
                        _reps[0] = distance;
                    }

                    if (len == 1)
                    {
                        _state = ShortRepNextStates[_state];
                    }
                    else
                    {
                        EncodeLength(rc, RepLenCoder, _repLenPrices, _repLenCounters, len - LzmaConstants.MatchLenMin, posState);
                        _state = RepNextStates[_state];
                    }
                }
                else
                {
                    rc.EncodeBit(_probs, IsRep + (int)_state, 0);
                    _state = MatchNextStates[_state];
                    EncodeLength(rc, LenCoder, _lenPrices, _lenCounters, len - LzmaConstants.MatchLenMin, posState);
                    pos -= LzmaConstants.NumReps;
                    EncodeDistance(rc, pos, len);
                    _reps[3] = _reps[2];
                    _reps[2] = _reps[1];
                    _reps[1] = _reps[0];
                    _reps[0] = pos;
                    _matchPriceCount++;
                }
            }

            _additionalOffset -= len;
            _nowPos64 += len;
            if (_additionalOffset == 0 && !_fastMode)
            {
                if (_matchPriceCount >= (1 << 7))
                {
                    FillDistancesPrices();
                }

                if (_alignPriceCount >= LzmaConstants.AlignTableSize)
                {
                    FillAlignPrices();
                }
            }
        }
    }

    /// <summary>Writes the end marker (a match with the distance 0xFFFFFFFF).</summary>
    public void WriteEndMarker()
    {
        var rc = RangeEncoder;
        var posState = (uint)_nowPos64 & _pbMask;
        rc.EncodeBit(_probs, IsMatch + (int)((_state << LzmaConstants.NumPosBitsMax) + posState), 1);
        rc.EncodeBit(_probs, IsRep + (int)_state, 0);
        _state = MatchNextStates[_state];
        const uint Length = LzmaConstants.MatchLenMin;
        EncodeLength(rc, LenCoder, _lenPrices, _lenCounters, Length - LzmaConstants.MatchLenMin, posState);
        rc.EncodeTree(_probs, PosSlot + (int)(GetLenToPosState(Length) << LzmaConstants.NumPosSlotBits), LzmaConstants.NumPosSlotBits, (1 << LzmaConstants.NumPosSlotBits) - 1);
        rc.EncodeDirectBits(((1u << 30) - 1) >> LzmaConstants.NumAlignBits, 30 - LzmaConstants.NumAlignBits);
        rc.EncodeReverseTree(_probs, Align, LzmaConstants.NumAlignBits, LzmaConstants.AlignMask);
    }

    private void EncodeDistance(LzmaRangeEncoder rc, uint distance, uint len)
    {
        var posSlot = GetPosSlot(distance);
        rc.EncodeTree(_probs, PosSlot + (int)(GetLenToPosState(len) << LzmaConstants.NumPosSlotBits), LzmaConstants.NumPosSlotBits, posSlot);
        if (posSlot >= LzmaConstants.StartPosModelIndex)
        {
            var footerBits = (int)((posSlot >> 1) - 1);
            var baseValue = (2 | (posSlot & 1)) << footerBits;
            var posReduced = distance - baseValue;
            if (posSlot < LzmaConstants.EndPosModelIndex)
            {
                rc.EncodeReverseTree(_probs, SpecPos + (int)baseValue - (int)posSlot - 1, footerBits, posReduced);
            }
            else
            {
                rc.EncodeDirectBits(posReduced >> LzmaConstants.NumAlignBits, footerBits - LzmaConstants.NumAlignBits);
                rc.EncodeReverseTree(_probs, Align, LzmaConstants.NumAlignBits, posReduced & LzmaConstants.AlignMask);
                _alignPriceCount++;
            }
        }
    }

    private void EncodeLength(LzmaRangeEncoder rc, int coder, uint[] prices, uint[] counters, uint symbol, uint posState)
    {
        if (symbol < LzmaConstants.LenNumLowSymbols)
        {
            rc.EncodeBit(_probs, coder + LenChoice, 0);
            rc.EncodeTree(_probs, coder + LenLow + (int)(posState << LzmaConstants.LenNumLowBits), LzmaConstants.LenNumLowBits, symbol);
        }
        else
        {
            rc.EncodeBit(_probs, coder + LenChoice, 1);
            if (symbol < LzmaConstants.LenNumLowSymbols + LzmaConstants.LenNumMidSymbols)
            {
                rc.EncodeBit(_probs, coder + LenChoice2, 0);
                rc.EncodeTree(_probs, coder + LenMid + (int)(posState << LzmaConstants.LenNumMidBits), LzmaConstants.LenNumMidBits, symbol - LzmaConstants.LenNumLowSymbols);
            }
            else
            {
                rc.EncodeBit(_probs, coder + LenChoice2, 1);
                rc.EncodeTree(_probs, coder + LenHigh, LzmaConstants.LenNumHighBits, symbol - LzmaConstants.LenNumLowSymbols - LzmaConstants.LenNumMidSymbols);
            }
        }

        if (!_fastMode && --counters[posState] == 0)
        {
            UpdateLenPrices(coder, prices, counters, posState);
        }
    }

    private void EncodeLiteral(LzmaRangeEncoder rc, int probs, uint symbol)
    {
        symbol |= 0x100;
        do
        {
            rc.EncodeBit(_probs, probs + (int)(symbol >> 8), (symbol >> 7) & 1);
            symbol <<= 1;
        }
        while (symbol < 0x10000);
    }

    private void EncodeMatchedLiteral(LzmaRangeEncoder rc, int probs, uint symbol, uint matchByte)
    {
        uint offs = 0x100;
        symbol |= 0x100;
        do
        {
            matchByte <<= 1;
            rc.EncodeBit(_probs, probs + (int)(offs + (matchByte & offs) + (symbol >> 8)), (symbol >> 7) & 1);
            symbol <<= 1;
            offs &= ~(matchByte ^ symbol);
        }
        while (symbol < 0x10000);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int LiteralProbs(uint position, uint previousByte) => Literal + (0x300 * (int)(((position & _lpMask) << _lc) + (previousByte >> (8 - _lc))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetLenToPosState(uint len) => len < LzmaConstants.NumLenToPosStates + 1 ? len - 2 : LzmaConstants.NumLenToPosStates - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetPosSlot(uint distance)
    {
        if (distance < 4)
            return distance;

        var n = 31 - BitOperations.LeadingZeroCount(distance);
        return (uint)(2 * n) + ((distance >> (n - 1)) & 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetPrice(uint prob, uint bit) => ProbPrices[(prob ^ ((0 - bit) & (BitModelTotal - 1))) >> NumMoveReducingBits];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetPrice0(uint prob) => ProbPrices[prob >> NumMoveReducingBits];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetPrice1(uint prob) => ProbPrices[(prob ^ (BitModelTotal - 1)) >> NumMoveReducingBits];

    private uint GetLiteralPrice(int probs, uint symbol)
    {
        uint price = 0;
        symbol |= 0x100;
        do
        {
            price += GetPrice(_probs[probs + (int)(symbol >> 8)], (symbol >> 7) & 1);
            symbol <<= 1;
        }
        while (symbol < 0x10000);

        return price;
    }

    private uint GetMatchedLiteralPrice(int probs, uint symbol, uint matchByte)
    {
        uint price = 0;
        uint offs = 0x100;
        symbol |= 0x100;
        do
        {
            matchByte <<= 1;
            price += GetPrice(_probs[probs + (int)(offs + (matchByte & offs) + (symbol >> 8))], (symbol >> 7) & 1);
            symbol <<= 1;
            offs &= ~(matchByte ^ symbol);
        }
        while (symbol < 0x10000);

        return price;
    }

    private uint GetTreePrice(int probs, int numBits, uint symbol)
    {
        uint price = 0;
        symbol |= 1u << numBits;
        while (symbol != 1)
        {
            price += GetPrice(_probs[probs + (int)(symbol >> 1)], symbol & 1);
            symbol >>= 1;
        }

        return price;
    }

    private uint GetReverseTreePrice(int probs, int numBits, uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = numBits; i != 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += GetPrice(_probs[probs + (int)m], bit);
            m = (m << 1) | bit;
        }

        return price;
    }

    private void UpdateLenPrices(int coder, uint[] prices, uint[] counters, uint posState)
    {
        var numSymbols = _lenTableSize;
        var offset = (int)posState * NumLenPrices;
        var a0 = GetPrice0(_probs[coder + LenChoice]);
        var a1 = GetPrice1(_probs[coder + LenChoice]);
        var b0 = a1 + GetPrice0(_probs[coder + LenChoice2]);
        var b1 = a1 + GetPrice1(_probs[coder + LenChoice2]);
        uint i = 0;
        for (; i < LzmaConstants.LenNumLowSymbols && i < numSymbols; i++)
        {
            prices[offset + (int)i] = a0 + GetTreePrice(coder + LenLow + (int)(posState << LzmaConstants.LenNumLowBits), LzmaConstants.LenNumLowBits, i);
        }

        for (; i < LzmaConstants.LenNumLowSymbols + LzmaConstants.LenNumMidSymbols && i < numSymbols; i++)
        {
            prices[offset + (int)i] = b0 + GetTreePrice(coder + LenMid + (int)(posState << LzmaConstants.LenNumMidBits), LzmaConstants.LenNumMidBits, i - LzmaConstants.LenNumLowSymbols);
        }

        for (; i < numSymbols; i++)
        {
            prices[offset + (int)i] = b1 + GetTreePrice(coder + LenHigh, LzmaConstants.LenNumHighBits, i - LzmaConstants.LenNumLowSymbols - LzmaConstants.LenNumMidSymbols);
        }

        counters[posState] = numSymbols;
    }

    private void FillAlignPrices()
    {
        for (uint i = 0; i < LzmaConstants.AlignTableSize; i++)
        {
            _alignPrices[i] = GetReverseTreePrice(Align, LzmaConstants.NumAlignBits, i);
        }

        _alignPriceCount = 0;
    }

    private void FillDistancesPrices()
    {
        Span<uint> tempPrices = stackalloc uint[LzmaConstants.NumFullDistances];
        for (uint i = LzmaConstants.StartPosModelIndex; i < LzmaConstants.NumFullDistances; i++)
        {
            var posSlot = GetPosSlot(i);
            var footerBits = (int)((posSlot >> 1) - 1);
            var baseValue = (2 | (posSlot & 1)) << footerBits;
            tempPrices[(int)i] = GetReverseTreePrice(SpecPos + (int)baseValue - (int)posSlot - 1, footerBits, i - baseValue);
        }

        for (var lenToPosState = 0; lenToPosState < LzmaConstants.NumLenToPosStates; lenToPosState++)
        {
            var encoder = PosSlot + (lenToPosState << LzmaConstants.NumPosSlotBits);
            var slotPrices = lenToPosState * DistTableSizeMax;
            for (uint posSlot = 0; posSlot < _distTableSize; posSlot++)
            {
                _posSlotPrices[slotPrices + (int)posSlot] = GetTreePrice(encoder, LzmaConstants.NumPosSlotBits, posSlot);
            }

            for (uint posSlot = LzmaConstants.EndPosModelIndex; posSlot < _distTableSize; posSlot++)
            {
                _posSlotPrices[slotPrices + (int)posSlot] += ((posSlot >> 1) - 1 - LzmaConstants.NumAlignBits) << NumBitPriceShiftBits;
            }

            var distancesPrices = lenToPosState * LzmaConstants.NumFullDistances;
            uint i = 0;
            for (; i < LzmaConstants.StartPosModelIndex; i++)
            {
                _distancesPrices[distancesPrices + (int)i] = _posSlotPrices[slotPrices + (int)i];
            }

            for (; i < LzmaConstants.NumFullDistances; i++)
            {
                _distancesPrices[distancesPrices + (int)i] = _posSlotPrices[slotPrices + (int)GetPosSlot(i)] + tempPrices[(int)i];
            }
        }

        _matchPriceCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint GetRepLen1Price(uint state, uint posState) => GetPrice0(_probs[IsRepG0 + (int)state]) + GetPrice0(_probs[IsRep0Long + (int)((state << LzmaConstants.NumPosBitsMax) + posState)]);

    private uint GetPureRepPrice(uint repIndex, uint state, uint posState)
    {
        uint price;
        if (repIndex == 0)
        {
            price = GetPrice0(_probs[IsRepG0 + (int)state]);
            price += GetPrice1(_probs[IsRep0Long + (int)((state << LzmaConstants.NumPosBitsMax) + posState)]);
        }
        else
        {
            price = GetPrice1(_probs[IsRepG0 + (int)state]);
            if (repIndex == 1)
            {
                price += GetPrice0(_probs[IsRepG1 + (int)state]);
            }
            else
            {
                price += GetPrice1(_probs[IsRepG1 + (int)state]);
                price += GetPrice(_probs[IsRepG2 + (int)state], repIndex - 2);
            }
        }

        return price;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint GetRepPrice(uint repIndex, uint len, uint state, uint posState) => _repLenPrices[((int)posState * NumLenPrices) + (int)len - LzmaConstants.MatchLenMin] + GetPureRepPrice(repIndex, state, posState);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint GetMatchPrice(uint distance, uint len, uint posState)
    {
        var price = _lenPrices[((int)posState * NumLenPrices) + (int)len - LzmaConstants.MatchLenMin];
        var lenToPosState = GetLenToPosState(len);
        if (distance < LzmaConstants.NumFullDistances)
        {
            price += _distancesPrices[((int)lenToPosState * LzmaConstants.NumFullDistances) + (int)distance];
        }
        else
        {
            price += _alignPrices[distance & LzmaConstants.AlignMask] + _posSlotPrices[((int)lenToPosState * DistTableSizeMax) + (int)GetPosSlot(distance)];
        }

        return price;
    }

    private uint ReadMatchDistances(out uint numPairs)
    {
        var mf = _matchFinder!;
        uint lenRes = 0;
        _numAvail = (uint)mf.Available;
        numPairs = (uint)mf.GetMatches(_matches);
        if (numPairs > 0)
        {
            lenRes = _matches[numPairs - 2];
            if (lenRes == _numFastBytes)
            {
                var buffer = mf.Buffer;
                var pby = mf.BufferPosition - 1;
                var distance = _matches[numPairs - 1] + 1;
                var numAvail = Math.Min(_numAvail, LzmaConstants.MatchLenMax);
                var pby2 = pby - (int)distance;
                while (lenRes < numAvail && buffer[pby + (int)lenRes] == buffer[pby2 + (int)lenRes])
                {
                    lenRes++;
                }
            }
        }

        _additionalOffset++;
        return lenRes;
    }

    private void MovePos(uint count)
    {
        if (count != 0)
        {
            _additionalOffset += count;
            _matchFinder!.Skip(count);
        }
    }

    private static bool ChangePair(uint smallDistance, uint bigDistance) => (bigDistance >> 7) > smallDistance;

    private uint GetOptimumFast(out uint backRes)
    {
        uint mainLen;
        uint numPairs;
        if (_additionalOffset == 0)
        {
            mainLen = ReadMatchDistances(out numPairs);
        }
        else
        {
            mainLen = _longestMatchLength;
            numPairs = _numPairs;
        }

        var numAvail = _numAvail;
        backRes = NotALiteral;
        if (numAvail < 2)
            return 1;

        numAvail = Math.Min(numAvail, LzmaConstants.MatchLenMax);
        var mf = _matchFinder!;
        var buffer = mf.Buffer;
        var data = mf.BufferPosition - 1;

        uint repLen = 0;
        uint repIndex = 0;
        for (uint i = 0; i < LzmaConstants.NumReps; i++)
        {
            var data2 = data - (int)_reps[i] - 1;
            if (buffer[data] != buffer[data2] || buffer[data + 1] != buffer[data2 + 1])
                continue;

            uint len = 2;
            while (len < numAvail && buffer[data + (int)len] == buffer[data2 + (int)len])
            {
                len++;
            }

            if (len >= _numFastBytes)
            {
                backRes = i;
                MovePos(len - 1);
                return len;
            }

            if (len > repLen)
            {
                repIndex = i;
                repLen = len;
            }
        }

        var matches = _matches;
        if (mainLen >= _numFastBytes)
        {
            backRes = matches[numPairs - 1] + LzmaConstants.NumReps;
            MovePos(mainLen - 1);
            return mainLen;
        }

        uint mainDist = 0;
        if (mainLen >= 2)
        {
            mainDist = matches[numPairs - 1];
            while (numPairs > 2 && mainLen == matches[numPairs - 4] + 1)
            {
                if (!ChangePair(matches[numPairs - 3], mainDist))
                    break;

                numPairs -= 2;
                mainLen = matches[numPairs - 2];
                mainDist = matches[numPairs - 1];
            }

            if (mainLen == 2 && mainDist >= 0x80)
            {
                mainLen = 1;
            }
        }

        if (repLen >= 2 && (repLen + 1 >= mainLen || (repLen + 2 >= mainLen && mainDist >= (1 << 9)) || (repLen + 3 >= mainLen && mainDist >= (1 << 15))))
        {
            backRes = repIndex;
            MovePos(repLen - 1);
            return repLen;
        }

        if (mainLen < 2 || numAvail <= 2)
            return 1;

        _longestMatchLength = ReadMatchDistances(out _numPairs);
        if (_longestMatchLength >= 2)
        {
            var newDistance = matches[_numPairs - 1];
            if ((_longestMatchLength >= mainLen && newDistance < mainDist) ||
                (_longestMatchLength == mainLen + 1 && !ChangePair(mainDist, newDistance)) ||
                _longestMatchLength > mainLen + 1 ||
                (_longestMatchLength + 1 >= mainLen && mainLen >= 3 && ChangePair(newDistance, mainDist)))
            {
                return 1;
            }
        }

        data = mf.BufferPosition - 1;
        for (uint i = 0; i < LzmaConstants.NumReps; i++)
        {
            var data2 = data - (int)_reps[i] - 1;
            if (buffer[data] != buffer[data2] || buffer[data + 1] != buffer[data2 + 1])
                continue;

            var limit = mainLen - 1;
            uint len = 2;
            while (len < limit && buffer[data + (int)len] == buffer[data2 + (int)len])
            {
                len++;
            }

            if (len >= limit)
                return 1;
        }

        backRes = mainDist + LzmaConstants.NumReps;
        MovePos(mainLen - 2);
        return mainLen;
    }

    private uint Backward(out uint backRes, uint cur)
    {
        var opt = _opt;
        var posMem = opt[cur].PosPrev;
        var backMem = opt[cur].BackPrev;
        _optimumEndIndex = cur;
        do
        {
            if (opt[cur].Prev1IsChar)
            {
                opt[posMem].MakeAsChar();
                opt[posMem].PosPrev = posMem - 1;
                if (opt[cur].Prev2)
                {
                    opt[posMem - 1].Prev1IsChar = false;
                    opt[posMem - 1].PosPrev = opt[cur].PosPrev2;
                    opt[posMem - 1].BackPrev = opt[cur].BackPrev2;
                }
            }

            var posPrev = posMem;
            var backCur = backMem;
            backMem = opt[posPrev].BackPrev;
            posMem = opt[posPrev].PosPrev;
            opt[posPrev].BackPrev = backCur;
            opt[posPrev].PosPrev = cur;
            cur = posPrev;
        }
        while (cur != 0);

        backRes = opt[0].BackPrev;
        _optimumCurrentIndex = opt[0].PosPrev;
        return _optimumCurrentIndex;
    }

    private uint GetOptimum(uint position, out uint backRes)
    {
        var opt = _opt;
        if (_optimumEndIndex != _optimumCurrentIndex)
        {
            ref var current = ref opt[_optimumCurrentIndex];
            var lenRes = current.PosPrev - _optimumCurrentIndex;
            backRes = current.BackPrev;
            _optimumCurrentIndex = current.PosPrev;
            return lenRes;
        }

        _optimumCurrentIndex = _optimumEndIndex = 0;

        uint mainLen;
        uint numPairs;
        if (_additionalOffset == 0)
        {
            mainLen = ReadMatchDistances(out numPairs);
        }
        else
        {
            mainLen = _longestMatchLength;
            numPairs = _numPairs;
        }

        var numAvail = _numAvail;
        if (numAvail < 2)
        {
            backRes = NotALiteral;
            return 1;
        }

        numAvail = Math.Min(numAvail, LzmaConstants.MatchLenMax);

        var mf = _matchFinder!;
        var buffer = mf.Buffer;
        var data = mf.BufferPosition - 1;
        Span<uint> reps = stackalloc uint[LzmaConstants.NumReps];
        Span<uint> repLens = stackalloc uint[LzmaConstants.NumReps];
        uint repMaxIndex = 0;
        for (var i = 0; i < LzmaConstants.NumReps; i++)
        {
            reps[i] = _reps[i];
            var data2 = data - (int)reps[i] - 1;
            if (buffer[data] != buffer[data2] || buffer[data + 1] != buffer[data2 + 1])
            {
                repLens[i] = 0;
                continue;
            }

            uint lenTest = 2;
            while (lenTest < numAvail && buffer[data + (int)lenTest] == buffer[data2 + (int)lenTest])
            {
                lenTest++;
            }

            repLens[i] = lenTest;
            if (lenTest > repLens[(int)repMaxIndex])
            {
                repMaxIndex = (uint)i;
            }
        }

        if (repLens[(int)repMaxIndex] >= _numFastBytes)
        {
            backRes = repMaxIndex;
            var lenRes = repLens[(int)repMaxIndex];
            MovePos(lenRes - 1);
            return lenRes;
        }

        var matches = _matches;
        if (mainLen >= _numFastBytes)
        {
            backRes = matches[numPairs - 1] + LzmaConstants.NumReps;
            MovePos(mainLen - 1);
            return mainLen;
        }

        uint curByte = buffer[data];
        uint matchByte = buffer[data - (int)reps[0] - 1];
        if (mainLen < 2 && curByte != matchByte && repLens[(int)repMaxIndex] < 2)
        {
            backRes = NotALiteral;
            return 1;
        }

        opt[0].State = _state;
        var posState = position & _pbMask;
        {
            var probs = LiteralProbs(position, buffer[data - 1]);
            opt[1].Price = GetPrice0(_probs[IsMatch + (int)((_state << LzmaConstants.NumPosBitsMax) + posState)]) +
                (_state >= 7 ? GetMatchedLiteralPrice(probs, curByte, matchByte) : GetLiteralPrice(probs, curByte));
        }

        opt[1].MakeAsChar();

        var matchPrice = GetPrice1(_probs[IsMatch + (int)((_state << LzmaConstants.NumPosBitsMax) + posState)]);
        var repMatchPrice = matchPrice + GetPrice1(_probs[IsRep + (int)_state]);
        if (matchByte == curByte)
        {
            var shortRepPrice = repMatchPrice + GetRepLen1Price(_state, posState);
            if (shortRepPrice < opt[1].Price)
            {
                opt[1].Price = shortRepPrice;
                opt[1].MakeAsShortRep();
            }
        }

        var lenEnd = mainLen >= repLens[(int)repMaxIndex] ? mainLen : repLens[(int)repMaxIndex];
        if (lenEnd < 2)
        {
            backRes = opt[1].BackPrev;
            return 1;
        }

        opt[1].PosPrev = 0;
        for (var i = 0; i < LzmaConstants.NumReps; i++)
        {
            opt[0].Backs[i] = reps[i];
        }

        var len = lenEnd;
        do
        {
            opt[len--].Price = InfinityPrice;
        }
        while (len >= 2);

        for (uint i = 0; i < LzmaConstants.NumReps; i++)
        {
            var repLen = repLens[(int)i];
            if (repLen < 2)
                continue;

            var price = repMatchPrice + GetPureRepPrice(i, _state, posState);
            do
            {
                var curAndLenPrice = price + _repLenPrices[((int)posState * NumLenPrices) + (int)repLen - 2];
                ref var o = ref opt[repLen];
                if (curAndLenPrice < o.Price)
                {
                    o.Price = curAndLenPrice;
                    o.PosPrev = 0;
                    o.BackPrev = i;
                    o.Prev1IsChar = false;
                }
            }
            while (--repLen >= 2);
        }

        var normalMatchPrice = matchPrice + GetPrice0(_probs[IsRep + (int)_state]);
        len = repLens[0] >= 2 ? repLens[0] + 1 : 2;
        if (len <= mainLen)
        {
            uint offs = 0;
            while (len > matches[offs])
            {
                offs += 2;
            }

            for (; ; len++)
            {
                var distance = matches[offs + 1];
                var curAndLenPrice = normalMatchPrice + GetMatchPrice(distance, len, posState);
                ref var o = ref opt[len];
                if (curAndLenPrice < o.Price)
                {
                    o.Price = curAndLenPrice;
                    o.PosPrev = 0;
                    o.BackPrev = distance + LzmaConstants.NumReps;
                    o.Prev1IsChar = false;
                }

                if (len == matches[offs])
                {
                    offs += 2;
                    if (offs == numPairs)
                        break;
                }
            }
        }

        uint cur = 0;
        while (true)
        {
            cur++;
            if (cur == lenEnd)
                return Backward(out backRes, cur);

            var newLen = ReadMatchDistances(out numPairs);
            if (newLen >= _numFastBytes)
            {
                _numPairs = numPairs;
                _longestMatchLength = newLen;
                return Backward(out backRes, cur);
            }

            position++;
            ref var curOpt = ref opt[cur];
            var posPrev = curOpt.PosPrev;
            uint state;
            if (curOpt.Prev1IsChar)
            {
                posPrev--;
                if (curOpt.Prev2)
                {
                    state = opt[curOpt.PosPrev2].State;
                    state = curOpt.BackPrev2 < LzmaConstants.NumReps ? RepNextStates[state] : MatchNextStates[state];
                }
                else
                {
                    state = opt[posPrev].State;
                }

                state = LiteralNextStates[state];
            }
            else
            {
                state = opt[posPrev].State;
            }

            if (posPrev == cur - 1)
            {
                state = curOpt.IsShortRep ? ShortRepNextStates[state] : LiteralNextStates[state];
            }
            else
            {
                uint pos;
                if (curOpt.Prev1IsChar && curOpt.Prev2)
                {
                    posPrev = curOpt.PosPrev2;
                    pos = curOpt.BackPrev2;
                    state = RepNextStates[state];
                }
                else
                {
                    pos = curOpt.BackPrev;
                    state = pos < LzmaConstants.NumReps ? RepNextStates[state] : MatchNextStates[state];
                }

                ref var prevOpt = ref opt[posPrev];
                if (pos < LzmaConstants.NumReps)
                {
                    reps[0] = prevOpt.Backs[(int)pos];
                    int i;
                    for (i = 1; i <= pos; i++)
                    {
                        reps[i] = prevOpt.Backs[i - 1];
                    }

                    for (; i < LzmaConstants.NumReps; i++)
                    {
                        reps[i] = prevOpt.Backs[i];
                    }
                }
                else
                {
                    reps[0] = pos - LzmaConstants.NumReps;
                    for (var i = 1; i < LzmaConstants.NumReps; i++)
                    {
                        reps[i] = prevOpt.Backs[i - 1];
                    }
                }
            }

            curOpt.State = state;
            curOpt.Backs[0] = reps[0];
            curOpt.Backs[1] = reps[1];
            curOpt.Backs[2] = reps[2];
            curOpt.Backs[3] = reps[3];

            var curPrice = curOpt.Price;
            var nextIsChar = false;
            data = mf.BufferPosition - 1;
            curByte = buffer[data];
            matchByte = buffer[data - (int)reps[0] - 1];
            posState = position & _pbMask;

            var curAnd1Price = curPrice + GetPrice0(_probs[IsMatch + (int)((state << LzmaConstants.NumPosBitsMax) + posState)]);
            {
                var probs = LiteralProbs(position, buffer[data - 1]);
                curAnd1Price += state >= 7 ? GetMatchedLiteralPrice(probs, curByte, matchByte) : GetLiteralPrice(probs, curByte);
            }

            ref var nextOpt = ref opt[cur + 1];
            if (curAnd1Price < nextOpt.Price)
            {
                nextOpt.Price = curAnd1Price;
                nextOpt.PosPrev = cur;
                nextOpt.MakeAsChar();
                nextIsChar = true;
            }

            matchPrice = curPrice + GetPrice1(_probs[IsMatch + (int)((state << LzmaConstants.NumPosBitsMax) + posState)]);
            repMatchPrice = matchPrice + GetPrice1(_probs[IsRep + (int)state]);

            if (matchByte == curByte && !(nextOpt.PosPrev < cur && nextOpt.BackPrev == 0))
            {
                var shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
                if (shortRepPrice <= nextOpt.Price)
                {
                    nextOpt.Price = shortRepPrice;
                    nextOpt.PosPrev = cur;
                    nextOpt.MakeAsShortRep();
                    nextIsChar = true;
                }
            }

            var numAvailFull = Math.Min(_numAvail, NumOpts - 1 - cur);
            if (numAvailFull < 2)
                continue;

            numAvail = numAvailFull <= _numFastBytes ? numAvailFull : (uint)_numFastBytes;

            if (!nextIsChar && matchByte != curByte)
            {
                // Try a literal then a rep0
                var data2 = data - (int)reps[0] - 1;
                var limit = Math.Min((uint)_numFastBytes + 1, numAvailFull);
                uint temp = 1;
                while (temp < limit && buffer[data + (int)temp] == buffer[data2 + (int)temp])
                {
                    temp++;
                }

                var lenTest2 = temp - 1;
                if (lenTest2 >= 2)
                {
                    uint state2 = LiteralNextStates[state];
                    var posStateNext = (position + 1) & _pbMask;
                    var nextRepMatchPrice = curAnd1Price + GetPrice1(_probs[IsMatch + (int)((state2 << LzmaConstants.NumPosBitsMax) + posStateNext)]) + GetPrice1(_probs[IsRep + (int)state2]);
                    var offset = cur + 1 + lenTest2;
                    while (lenEnd < offset)
                    {
                        opt[++lenEnd].Price = InfinityPrice;
                    }

                    var curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                    ref var o = ref opt[offset];
                    if (curAndLenPrice < o.Price)
                    {
                        o.Price = curAndLenPrice;
                        o.PosPrev = cur + 1;
                        o.BackPrev = 0;
                        o.Prev1IsChar = true;
                        o.Prev2 = false;
                    }
                }
            }

            uint startLen = 2;
            for (uint repIndex = 0; repIndex < LzmaConstants.NumReps; repIndex++)
            {
                var data2 = data - (int)reps[(int)repIndex] - 1;
                if (buffer[data] != buffer[data2] || buffer[data + 1] != buffer[data2 + 1])
                    continue;

                uint lenTest = 2;
                while (lenTest < numAvail && buffer[data + (int)lenTest] == buffer[data2 + (int)lenTest])
                {
                    lenTest++;
                }

                while (lenEnd < cur + lenTest)
                {
                    opt[++lenEnd].Price = InfinityPrice;
                }

                var lenTestTemp = lenTest;
                var price = repMatchPrice + GetPureRepPrice(repIndex, state, posState);
                do
                {
                    var curAndLenPrice = price + _repLenPrices[((int)posState * NumLenPrices) + (int)lenTest - 2];
                    ref var o = ref opt[cur + lenTest];
                    if (curAndLenPrice < o.Price)
                    {
                        o.Price = curAndLenPrice;
                        o.PosPrev = cur;
                        o.BackPrev = repIndex;
                        o.Prev1IsChar = false;
                    }
                }
                while (--lenTest >= 2);

                lenTest = lenTestTemp;
                if (repIndex == 0)
                {
                    startLen = lenTest + 1;
                }

                // Try a rep, a literal, then a rep0
                var lenTest2 = lenTest + 1;
                var limit = Math.Min(lenTest2 + (uint)_numFastBytes, numAvailFull);
                while (lenTest2 < limit && buffer[data + (int)lenTest2] == buffer[data2 + (int)lenTest2])
                {
                    lenTest2++;
                }

                lenTest2 -= lenTest + 1;
                if (lenTest2 >= 2)
                {
                    uint state2 = RepNextStates[state];
                    var posStateNext = (position + lenTest) & _pbMask;
                    var curAndLenCharPrice = price + _repLenPrices[((int)posState * NumLenPrices) + (int)lenTest - 2] +
                        GetPrice0(_probs[IsMatch + (int)((state2 << LzmaConstants.NumPosBitsMax) + posStateNext)]) +
                        GetMatchedLiteralPrice(LiteralProbs(position + lenTest, buffer[data + (int)lenTest - 1]), buffer[data + (int)lenTest], buffer[data2 + (int)lenTest]);
                    state2 = LiteralNextStates[state2];
                    posStateNext = (position + lenTest + 1) & _pbMask;
                    var nextRepMatchPrice = curAndLenCharPrice + GetPrice1(_probs[IsMatch + (int)((state2 << LzmaConstants.NumPosBitsMax) + posStateNext)]) + GetPrice1(_probs[IsRep + (int)state2]);
                    var offset = cur + lenTest + 1 + lenTest2;
                    while (lenEnd < offset)
                    {
                        opt[++lenEnd].Price = InfinityPrice;
                    }

                    var curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                    ref var o = ref opt[offset];
                    if (curAndLenPrice < o.Price)
                    {
                        o.Price = curAndLenPrice;
                        o.PosPrev = cur + lenTest + 1;
                        o.BackPrev = 0;
                        o.Prev1IsChar = true;
                        o.Prev2 = true;
                        o.PosPrev2 = cur;
                        o.BackPrev2 = repIndex;
                    }
                }
            }

            if (newLen > numAvail)
            {
                newLen = numAvail;
                for (numPairs = 0; newLen > matches[numPairs]; numPairs += 2)
                {
                }

                matches[numPairs] = newLen;
                numPairs += 2;
            }

            if (newLen >= startLen)
            {
                var normalMatchPrice2 = matchPrice + GetPrice0(_probs[IsRep + (int)state]);
                while (lenEnd < cur + newLen)
                {
                    opt[++lenEnd].Price = InfinityPrice;
                }

                uint offs = 0;
                while (startLen > matches[offs])
                {
                    offs += 2;
                }

                var curBack = matches[offs + 1];
                for (var lenTest = startLen; ; lenTest++)
                {
                    var curAndLenPrice = normalMatchPrice2 + GetMatchPrice(curBack, lenTest, posState);
                    ref var o = ref opt[cur + lenTest];
                    if (curAndLenPrice < o.Price)
                    {
                        o.Price = curAndLenPrice;
                        o.PosPrev = cur;
                        o.BackPrev = curBack + LzmaConstants.NumReps;
                        o.Prev1IsChar = false;
                    }

                    if (lenTest == matches[offs])
                    {
                        // Try a match, a literal, then a rep0
                        var data2 = data - (int)curBack - 1;
                        var lenTest2 = lenTest + 1;
                        var limit = Math.Min(lenTest2 + (uint)_numFastBytes, numAvailFull);
                        while (lenTest2 < limit && buffer[data + (int)lenTest2] == buffer[data2 + (int)lenTest2])
                        {
                            lenTest2++;
                        }

                        lenTest2 -= lenTest + 1;
                        if (lenTest2 >= 2)
                        {
                            uint state2 = MatchNextStates[state];
                            var posStateNext = (position + lenTest) & _pbMask;
                            var curAndLenCharPrice = curAndLenPrice +
                                GetPrice0(_probs[IsMatch + (int)((state2 << LzmaConstants.NumPosBitsMax) + posStateNext)]) +
                                GetMatchedLiteralPrice(LiteralProbs(position + lenTest, buffer[data + (int)lenTest - 1]), buffer[data + (int)lenTest], buffer[data2 + (int)lenTest]);
                            state2 = LiteralNextStates[state2];
                            posStateNext = (posStateNext + 1) & _pbMask;
                            var nextRepMatchPrice = curAndLenCharPrice + GetPrice1(_probs[IsMatch + (int)((state2 << LzmaConstants.NumPosBitsMax) + posStateNext)]) + GetPrice1(_probs[IsRep + (int)state2]);
                            var offset = cur + lenTest + 1 + lenTest2;
                            while (lenEnd < offset)
                            {
                                opt[++lenEnd].Price = InfinityPrice;
                            }

                            var curAndLenPrice2 = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                            ref var o2 = ref opt[offset];
                            if (curAndLenPrice2 < o2.Price)
                            {
                                o2.Price = curAndLenPrice2;
                                o2.PosPrev = cur + lenTest + 1;
                                o2.BackPrev = 0;
                                o2.Prev1IsChar = true;
                                o2.Prev2 = true;
                                o2.PosPrev2 = cur;
                                o2.BackPrev2 = curBack + LzmaConstants.NumReps;
                            }
                        }

                        offs += 2;
                        if (offs == numPairs)
                            break;

                        curBack = matches[offs + 1];
                    }
                }
            }
        }
    }

    private static uint[] CreateProbPrices()
    {
        var prices = new uint[BitModelTotal >> NumMoveReducingBits];
        for (var i = (1u << NumMoveReducingBits) / 2; i < BitModelTotal; i += 1u << NumMoveReducingBits)
        {
            const int CyclesBits = NumBitPriceShiftBits;
            var w = i;
            uint bitCount = 0;
            for (var j = 0; j < CyclesBits; j++)
            {
                w *= w;
                bitCount <<= 1;
                while (w >= 1u << 16)
                {
                    w >>= 1;
                    bitCount++;
                }
            }

            prices[i >> NumMoveReducingBits] = (11u << CyclesBits) - 15 - bitCount;
        }

        return prices;
    }

    public void Dispose()
    {
        _matchFinder?.Dispose();
        _matchFinder = null;
    }

    [InlineArray(LzmaConstants.NumReps)]
    private struct RepArray
    {
        private uint _element;
    }

    private struct Optimal
    {
        public uint Price;
        public uint State;
        public bool Prev1IsChar;
        public bool Prev2;
        public uint PosPrev2;
        public uint BackPrev2;
        public uint PosPrev;
        public uint BackPrev;
        public RepArray Backs;

        public readonly bool IsShortRep => BackPrev == 0;

        public void MakeAsChar()
        {
            BackPrev = NotALiteral;
            Prev1IsChar = false;
        }

        public void MakeAsShortRep()
        {
            BackPrev = 0;
            Prev1IsChar = false;
        }
    }
}
