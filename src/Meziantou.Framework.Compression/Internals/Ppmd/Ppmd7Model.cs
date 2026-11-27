// PPMd var.H model, a port of 7-Zip's Ppmd7.c, Ppmd7Dec.c and Ppmd7Enc.c (Igor Pavlov, public domain), which are based on
// PPMd var.H (2001) by Dmitry Shkarin (public domain). The model and its sub-allocator behave exactly as 7-Zip's, so that the
// streams are compatible: the 7z PPMd coder (03 04 01) and the PPMd blocks of RAR 2.9/4.x use it, with different range coders.
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The PPMd var.H model (CPpmd7 of 7-Zip), with its sub-allocator over one byte array.</summary>
internal sealed class Ppmd7Model
{
    public const int MinOrder = 2;
    public const int MaxOrder = 64;
    public const uint MinMemorySize = 1u << 11;
    public const uint MaxMemorySize = 0xFFFFFFFFu - (12 * 3);

    private const int MaxFreq = 124;
    private const uint UnitSize = PpmdMemory.UnitSize;
    private const int NumIndexes = PpmdMemory.NumIndexes;
    private const int IntBits = 7;
    private const int PeriodBits = 7;
    private const uint BinScale = 1 << (IntBits + PeriodBits);
    private const int DummySeeIndex = 25 * 16;
    private const uint StateSize = 6;
    private const ushort EmptyNodeStamp = 0xFFFF;

    private static readonly ushort[] InitBinEsc = [0x3CDD, 0x1F3F, 0x59BF, 0x48F3, 0x64A1, 0x5ABC, 0x6632, 0x6051];
    private static readonly byte[] ExpEscape = [25, 14, 9, 7, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2];
    private static readonly byte[] NS2Indx = CreateNS2Indx();
    private static readonly byte[] NS2BSIndx = CreateNS2BSIndx();
    private static readonly byte[] HB2Flag = CreateHB2Flag();

    private readonly byte[] _memory;
    private readonly uint _size;
    private readonly uint _alignOffset;
    private readonly uint[] _freeList = new uint[NumIndexes];
    private readonly PpmdSee[] _see = new PpmdSee[DummySeeIndex + 1];
    private readonly ushort[] _binSumm = new ushort[256 * 64]; // 128 rows are used, the others make every byte index valid
    // 0xFF for the symbols that are not masked and 0 for the masked ones, so that the loops sum the frequencies of the symbols
    // that are not masked without a branch (Freq & mask), as 7-Zip does
    private readonly byte[] _charMask = new byte[256];
    private readonly uint[] _successorStates = new uint[MaxOrder + 1];
    private int _maxOrder;
    private uint _minContext;
    private uint _maxContext;
    private uint _foundState;
    private int _orderFall;
    private uint _initEsc;
    private uint _prevSuccess;
    private uint _hiBitsFlag;
    private int _runLength;
    private int _initRL;
    private uint _glueCount;
    private uint _text;
    private uint _unitsStart;
    private uint _loUnit;
    private uint _hiUnit;

    /// <summary>Allocates the memory of the model. The caller validates <paramref name="memorySize"/> first.</summary>
    public Ppmd7Model(uint memorySize)
    {
        _memory = PpmdMemory.Allocate(memorySize, out _alignOffset);
        _size = memorySize;
    }

    /// <summary>Gets the number of times the model restarted because its memory was full, since <see cref="Init"/>.</summary>
    public int RestartCount { get; private set; }

    /// <summary>Validates the parameters of a model, as 7-Zip does.</summary>
    public static void ValidateParameters(int order, uint memorySize, long maxMemoryUsage)
    {
        if (order is < MinOrder or > MaxOrder)
            throw new InvalidDataException($"The PPMd order {order} is not supported (expected {MinOrder} to {MaxOrder}).");

        if (memorySize is < MinMemorySize or > MaxMemorySize)
            throw new InvalidDataException($"The PPMd memory size {memorySize} is not supported.");

        if (memorySize > maxMemoryUsage)
            throw new InvalidDataException($"The PPMd memory size ({memorySize} bytes) exceeds the maximum memory usage ({maxMemoryUsage} bytes).");
    }

    /// <summary>Starts a new model (Ppmd7_Init).</summary>
    public void Init(int maxOrder)
    {
        _maxOrder = maxOrder;
        RestartModel();
        RestartCount = 0;
        ref var dummySee = ref _see[DummySeeIndex];
        dummySee.Shift = PeriodBits;
        dummySee.Summ = 0;
        dummySee.Count = 64;
    }

    /// <summary>Decodes a symbol: a byte, -1 for the end marker (an escape from the order-0 context), or -2 for a data error.</summary>
    public int DecodeSymbol<TDecoder>(ref TDecoder rc)
        where TDecoder : struct, IPpmd7RangeDecoder
    {
        ref var mc = ref Ctx(_minContext);
        if (mc.NumStats != 1)
        {
            var s = mc.Stats;
            ref var state = ref St(s);
            var count = rc.GetThreshold(mc.SummFreq);
            uint hiCnt = state.Freq;
            if (count < hiCnt)
            {
                rc.Decode(0, hiCnt);
                _foundState = s;
                var symbol = state.Symbol;
                Update1_0();
                return symbol;
            }

            _prevSuccess = 0;
            var i = mc.NumStats - 1;
            do
            {
                s += StateSize;
                state = ref St(s);
                if ((hiCnt += state.Freq) > count)
                {
                    rc.Decode(hiCnt - state.Freq, state.Freq);
                    _foundState = s;
                    var symbol = state.Symbol;
                    Update1();
                    return symbol;
                }
            }
            while (--i > 0);

            if (count >= mc.SummFreq)
                return -2;

            _hiBitsFlag = HB2Flag[St(_foundState).Symbol];
            rc.Decode(hiCnt, mc.SummFreq - hiCnt);
            ResetMask();
            MaskStates(mc.Stats, mc.NumStats);
        }
        else
        {
            ref var prob = ref GetBinSumm();
            if (rc.DecodeBit(prob) == 0)
            {
                prob = (ushort)(prob + (1 << IntBits) - GetMean(prob));
                _foundState = _minContext + 2;
                var symbol = St(_foundState).Symbol;
                UpdateBin();
                return symbol;
            }

            prob = (ushort)(prob - GetMean(prob));
            _initEsc = ExpEscape[(prob >> 10) & 15];
            ResetMask();
            Mask(St(_minContext + 2).Symbol);
            _prevSuccess = 0;
        }

        var charMask = _charMask;
        while (true)
        {
            uint numMasked = Ctx(_minContext).NumStats;
            do
            {
                _orderFall++;
                var suffix = Ctx(_minContext).Suffix;
                if (suffix == 0)
                    return -1;

                _minContext = suffix;
            }
            while (Ctx(_minContext).NumStats == numMasked);

            mc = ref Ctx(_minContext);
            var num = (int)mc.NumStats - (int)numMasked;
            if (num is <= 0 or > 255)
                return -2;

            // The symbols of the child contexts are masked: the mask of a masked symbol is 0, so that its frequency is not added
            var states = PpmdMemory.States(_memory, mc.Stats, mc.NumStats);
            uint hiCnt = 0;
            foreach (ref readonly var state in states)
            {
                hiCnt += state.Freq & (uint)charMask[state.Symbol];
            }

            ref var see = ref MakeEscFreq(numMasked, out var freqSum);
            freqSum += hiCnt;
            var count = rc.GetThreshold(freqSum);
            if (count < hiCnt)
            {
                // The first state where the sum exceeds the count is not masked, as a masked state does not change the sum
                var k = 0;
                hiCnt = 0;
                while (true)
                {
                    ref readonly var state = ref states[k];
                    hiCnt += state.Freq & (uint)charMask[state.Symbol];
                    if (hiCnt > count)
                        break;

                    k++;
                }

                ref var found = ref states[k];
                rc.Decode(hiCnt - found.Freq, found.Freq);
                see.Update();
                _foundState = mc.Stats + ((uint)k * StateSize);
                var symbol = found.Symbol;
                Update2();
                return symbol;
            }

            if (count >= freqSum)
                return -2;

            rc.Decode(hiCnt, freqSum - hiCnt);
            see.Summ = (ushort)(see.Summ + freqSum);
            foreach (ref readonly var state in states)
            {
                charMask[state.Symbol] = 0;
            }
        }
    }

    /// <summary>Encodes a byte, or the end marker when <paramref name="symbol"/> is -1 (Ppmd7_EncodeSymbol).</summary>
    public void EncodeSymbol<TEncoder>(ref TEncoder rc, int symbol)
        where TEncoder : struct, IPpmd7RangeEncoder
    {
        ref var mc = ref Ctx(_minContext);
        if (mc.NumStats != 1)
        {
            var s = mc.Stats;
            ref var state = ref St(s);
            if (state.Symbol == symbol)
            {
                rc.Encode(0, state.Freq, mc.SummFreq);
                _foundState = s;
                Update1_0();
                return;
            }

            _prevSuccess = 0;
            uint sum = state.Freq;
            var i = mc.NumStats - 1;
            do
            {
                s += StateSize;
                state = ref St(s);
                if (state.Symbol == symbol)
                {
                    rc.Encode(sum, state.Freq, mc.SummFreq);
                    _foundState = s;
                    Update1();
                    return;
                }

                sum += state.Freq;
            }
            while (--i > 0);

            _hiBitsFlag = HB2Flag[St(_foundState).Symbol];
            ResetMask();
            MaskStates(mc.Stats, mc.NumStats);
            rc.Encode(sum, mc.SummFreq - sum, mc.SummFreq);
        }
        else
        {
            ref var prob = ref GetBinSumm();
            var s = _minContext + 2;
            ref var state = ref St(s);
            if (state.Symbol == symbol)
            {
                rc.EncodeBit0(prob);
                prob = (ushort)(prob + (1 << IntBits) - GetMean(prob));
                _foundState = s;
                UpdateBin();
                return;
            }

            rc.EncodeBit1(prob);
            prob = (ushort)(prob - GetMean(prob));
            _initEsc = ExpEscape[(prob >> 10) & 15];
            ResetMask();
            Mask(state.Symbol);
            _prevSuccess = 0;
        }

        while (true)
        {
            uint numMasked = Ctx(_minContext).NumStats;
            do
            {
                _orderFall++;
                var suffix = Ctx(_minContext).Suffix;
                if (suffix == 0)
                    return; // End marker (symbol -1)

                _minContext = suffix;
            }
            while (Ctx(_minContext).NumStats == numMasked);

            ref var see = ref MakeEscFreq(numMasked, out var escFreq);
            mc = ref Ctx(_minContext);
            var states = PpmdMemory.States(_memory, mc.Stats, mc.NumStats);
            var charMask = _charMask;
            uint sum = 0;
            for (var i = 0; i < states.Length; i++)
            {
                ref var state = ref states[i];
                var current = state.Symbol;
                if (current == symbol)
                {
                    var low = sum;
                    foreach (ref readonly var other in states[i..])
                    {
                        sum += other.Freq & (uint)charMask[other.Symbol];
                    }

                    rc.Encode(low, state.Freq, sum + escFreq);
                    see.Update();
                    _foundState = mc.Stats + ((uint)i * StateSize);
                    Update2();
                    return;
                }

                // The mask of a masked symbol is 0: its frequency is not added
                sum += state.Freq & (uint)charMask[current];
                charMask[current] = 0;
            }

            rc.Encode(sum, escFreq, sum + escFreq);
            see.Summ = (ushort)(see.Summ + sum + escFreq);
        }
    }

    private static uint GetMean(uint prob) => (prob + (1 << (PeriodBits - 2))) >> PeriodBits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Ppmd7Context Ctx(uint offset) => ref PpmdMemory.At<Ppmd7Context>(_memory, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref PpmdState St(uint offset) => ref PpmdMemory.At<PpmdState>(_memory, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Ppmd7Node Node(uint offset) => ref PpmdMemory.At<Ppmd7Node>(_memory, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetMask() => _charMask.AsSpan().Fill(0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Mask(byte symbol) => _charMask[symbol] = 0;

    private void MaskStates(uint stats, uint numStats)
    {
        for (uint i = 0; i < numStats; i++)
        {
            Mask(St(stats + (i * StateSize)).Symbol);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ushort GetBinSumm()
    {
        ref var mc = ref Ctx(_minContext);
        ref var oneState = ref St(_minContext + 2);
        _hiBitsFlag = HB2Flag[St(_foundState).Symbol];
        var index = ((byte)(oneState.Freq - 1) * 64)
            + (int)_prevSuccess
            + NS2BSIndx[(byte)(Ctx(mc.Suffix).NumStats - 1)]
            + (int)_hiBitsFlag
            + (2 * HB2Flag[oneState.Symbol])
            + ((_runLength >> 26) & 0x20);
        return ref _binSumm[index];
    }

    private ref PpmdSee MakeEscFreq(uint numMasked, out uint escFreq)
    {
        ref var mc = ref Ctx(_minContext);
        uint numStats = mc.NumStats;
        if (numStats != 256)
        {
            var nonMasked = numStats - numMasked;
            var index = (NS2Indx[(byte)(nonMasked - 1)] * 16)
                + (nonMasked < (uint)Ctx(mc.Suffix).NumStats - numStats ? 1 : 0)
                + (mc.SummFreq < 11 * numStats ? 2 : 0)
                + (numMasked > nonMasked ? 4 : 0)
                + (int)_hiBitsFlag;
            ref var see = ref _see[index];
            escFreq = see.GetMean();
            return ref see;
        }

        escFreq = 1;
        return ref _see[DummySeeIndex];
    }

    private void Update1()
    {
        var s = _foundState;
        ref var state = ref St(s);
        state.Freq += 4;
        Ctx(_minContext).SummFreq += 4;
        ref var previous = ref St(s - StateSize);
        if (state.Freq > previous.Freq)
        {
            PpmdMemory.SwapStates(ref state, ref previous);
            _foundState = s - StateSize;
            if (previous.Freq > MaxFreq)
            {
                Rescale();
            }
        }

        NextContext();
    }

    private void Update1_0()
    {
        ref var state = ref St(_foundState);
        ref var mc = ref Ctx(_minContext);
        _prevSuccess = 2u * state.Freq > mc.SummFreq ? 1u : 0u;
        _runLength += (int)_prevSuccess;
        mc.SummFreq += 4;
        if ((state.Freq += 4) > MaxFreq)
        {
            Rescale();
        }

        NextContext();
    }

    private void UpdateBin()
    {
        ref var state = ref St(_foundState);
        state.Freq = (byte)(state.Freq + (state.Freq < 128 ? 1 : 0));
        _prevSuccess = 1;
        _runLength++;
        NextContext();
    }

    private void Update2()
    {
        ref var state = ref St(_foundState);
        state.Freq += 4;
        Ctx(_minContext).SummFreq += 4;
        if (state.Freq > MaxFreq)
        {
            Rescale();
        }

        _runLength = _initRL;
        UpdateModel();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextContext()
    {
        var c = St(_foundState).Successor;
        if (_orderFall == 0 && c > _text)
        {
            _minContext = _maxContext = c;
        }
        else
        {
            UpdateModel();
        }
    }

    private void Rescale()
    {
        ref var mc = ref Ctx(_minContext);
        var stats = mc.Stats;
        var s = _foundState;

        // Move the found state to the front
        if (s != stats)
        {
            var tmp = St(s);
            for (; s != stats; s -= StateSize)
            {
                St(s) = St(s - StateSize);
            }

            St(s) = tmp;
        }

        ref var first = ref St(s);
        var escFreq = (uint)mc.SummFreq - first.Freq;
        first.Freq += 4;
        var adder = _orderFall != 0 ? 1u : 0u;
        first.Freq = (byte)((first.Freq + adder) >> 1);
        uint sumFreq = first.Freq;

        var i = mc.NumStats - 1;
        do
        {
            s += StateSize;
            ref var state = ref St(s);
            escFreq -= state.Freq;
            state.Freq = (byte)((state.Freq + adder) >> 1);
            sumFreq += state.Freq;
            if (state.Freq > St(s - StateSize).Freq)
            {
                var s1 = s;
                var tmp = state;
                do
                {
                    St(s1) = St(s1 - StateSize);
                    s1 -= StateSize;
                }
                while (s1 != stats && tmp.Freq > St(s1 - StateSize).Freq);

                St(s1) = tmp;
            }
        }
        while (--i > 0);

        if (St(s).Freq == 0)
        {
            uint numStats = mc.NumStats;
            uint removed = 0;
            do
            {
                removed++;
                s -= StateSize;
            }
            while (St(s).Freq == 0);

            escFreq += removed;
            mc.NumStats = (ushort)(mc.NumStats - removed);
            if (mc.NumStats == 1)
            {
                var tmp = St(stats);
                do
                {
                    tmp.Freq = (byte)(tmp.Freq - (tmp.Freq >> 1));
                    escFreq >>= 1;
                }
                while (escFreq > 1);

                InsertNode(stats, U2I((numStats + 1) >> 1));
                _foundState = _minContext + 2;
                St(_foundState) = tmp;
                return;
            }

            var n0 = (numStats + 1) >> 1;
            var n1 = (mc.NumStats + 1u) >> 1;
            if (n0 != n1)
            {
                mc.Stats = ShrinkUnits(stats, n0, n1);
            }
        }

        mc.SummFreq = (ushort)(sumFreq + escFreq - (escFreq >> 1));
        _foundState = mc.Stats;
    }

    private uint CreateSuccessors(bool skip)
    {
        var c = _minContext;
        ref var foundState = ref St(_foundState);
        var upBranch = foundState.Successor;
        var foundSymbol = foundState.Symbol;
        var ps = _successorStates;
        var numPs = 0;
        if (!skip)
        {
            ps[numPs++] = _foundState;
        }

        while (Ctx(c).Suffix != 0)
        {
            c = Ctx(c).Suffix;
            ref var ctx = ref Ctx(c);
            uint s;
            if (ctx.NumStats != 1)
            {
                s = ctx.Stats;
                while (St(s).Symbol != foundSymbol)
                {
                    s += StateSize;
                }
            }
            else
            {
                s = c + 2;
            }

            var successor = St(s).Successor;
            if (successor != upBranch)
            {
                c = successor;
                if (numPs == 0)
                    return c;

                break;
            }

            if (numPs >= ps.Length)
                PpmdMemory.ThrowCorrupted();

            ps[numPs++] = s;
        }

        if (numPs == 0)
            return c;

        var upSymbol = PpmdMemory.At<byte>(_memory, upBranch);
        var upSuccessor = upBranch + 1;
        byte upFreq;
        ref var context = ref Ctx(c);
        if (context.NumStats == 1)
        {
            upFreq = St(c + 2).Freq;
        }
        else
        {
            var s = context.Stats;
            while (St(s).Symbol != upSymbol)
            {
                s += StateSize;
            }

            var cf = (uint)St(s).Freq - 1;
            var s0 = (uint)context.SummFreq - context.NumStats - cf;
            uint value;
            if (2 * cf <= s0)
            {
                value = 5 * cf > s0 ? 1u : 0u;
            }
            else
            {
                if (s0 == 0)
                    PpmdMemory.ThrowCorrupted();

                value = ((2 * cf) + (3 * s0) - 1) / (2 * s0);
            }

            upFreq = (byte)(1 + value);
        }

        do
        {
            uint c1;
            if (_hiUnit != _loUnit)
            {
                c1 = _hiUnit -= UnitSize;
            }
            else if (_freeList[0] != 0)
            {
                c1 = RemoveNode(0);
            }
            else
            {
                c1 = AllocUnitsRare(0);
                if (c1 == 0)
                    return 0;
            }

            ref var child = ref Ctx(c1);
            child.NumStats = 1;
            ref var oneState = ref St(c1 + 2);
            oneState.Symbol = upSymbol;
            oneState.Freq = upFreq;
            oneState.Successor = upSuccessor;
            child.Suffix = c;
            St(ps[--numPs]).Successor = c1;
            c = c1;
        }
        while (numPs != 0);

        return c;
    }

    private void UpdateModel()
    {
        ref var foundState = ref St(_foundState);
        var foundSymbol = foundState.Symbol;
        uint foundFreq = foundState.Freq;
        var fSuccessor = foundState.Successor;

        ref var minContext = ref Ctx(_minContext);
        if (foundFreq < MaxFreq / 4 && minContext.Suffix != 0)
        {
            var c = minContext.Suffix;
            ref var ctx = ref Ctx(c);
            if (ctx.NumStats == 1)
            {
                ref var state = ref St(c + 2);
                if (state.Freq < 32)
                {
                    state.Freq++;
                }
            }
            else
            {
                var s = ctx.Stats;
                if (St(s).Symbol != foundSymbol)
                {
                    do
                    {
                        s += StateSize;
                    }
                    while (St(s).Symbol != foundSymbol);

                    if (St(s).Freq >= St(s - StateSize).Freq)
                    {
                        PpmdMemory.SwapStates(ref St(s), ref St(s - StateSize));
                        s -= StateSize;
                    }
                }

                ref var state = ref St(s);
                if (state.Freq < MaxFreq - 9)
                {
                    state.Freq += 2;
                    ctx.SummFreq += 2;
                }
            }
        }

        if (_orderFall == 0)
        {
            _minContext = _maxContext = CreateSuccessors(skip: true);
            if (_minContext == 0)
            {
                RestartAfterMemoryFull();
                return;
            }

            St(_foundState).Successor = _minContext;
            return;
        }

        PpmdMemory.At<byte>(_memory, _text) = foundSymbol;
        _text++;
        var successor = _text;
        if (_text >= _unitsStart)
        {
            RestartAfterMemoryFull();
            return;
        }

        if (fSuccessor != 0)
        {
            if (fSuccessor <= successor)
            {
                var cs = CreateSuccessors(skip: false);
                if (cs == 0)
                {
                    RestartAfterMemoryFull();
                    return;
                }

                fSuccessor = cs;
            }

            if (--_orderFall == 0)
            {
                successor = fSuccessor;
                if (_maxContext != _minContext)
                {
                    _text--;
                }
            }
        }
        else
        {
            St(_foundState).Successor = successor;
            fSuccessor = _minContext;
        }

        uint ns = Ctx(_minContext).NumStats;
        var s0 = (uint)Ctx(_minContext).SummFreq - ns - (foundFreq - 1);
        for (var c = _maxContext; c != _minContext; c = Ctx(c).Suffix)
        {
            ref var ctx = ref Ctx(c);
            uint ns1 = ctx.NumStats;
            if (ns1 != 1)
            {
                if ((ns1 & 1) == 0)
                {
                    // Expand for one unit
                    var oldNU = ns1 >> 1;
                    var i = U2I(oldNU);
                    if (i != U2I(oldNU + 1))
                    {
                        var ptr = AllocUnits(i + 1);
                        if (ptr == 0)
                        {
                            RestartAfterMemoryFull();
                            return;
                        }

                        var oldPtr = ctx.Stats;
                        PpmdMemory.CopyUnits(_memory, ptr, oldPtr, oldNU);
                        InsertNode(oldPtr, i);
                        ctx.Stats = ptr;
                    }
                }

                ctx.SummFreq = (ushort)(ctx.SummFreq + (2 * ns1 < ns ? 1 : 0) + (2 * ((4 * ns1 <= ns ? 1 : 0) & (ctx.SummFreq <= 8 * ns1 ? 1 : 0))));
            }
            else
            {
                var s = AllocUnits(0);
                if (s == 0)
                {
                    RestartAfterMemoryFull();
                    return;
                }

                ref var state = ref St(s);
                state = St(c + 2);
                ctx.Stats = s;
                if (state.Freq < (MaxFreq / 4) - 1)
                {
                    state.Freq <<= 1;
                }
                else
                {
                    state.Freq = MaxFreq - 4;
                }

                ctx.SummFreq = (ushort)(state.Freq + _initEsc + (ns > 3 ? 1u : 0u));
            }

            var cf = 2 * foundFreq * (ctx.SummFreq + 6u);
            var sf = s0 + ctx.SummFreq;
            if (cf < 6 * sf)
            {
                cf = 1 + (cf > sf ? 1u : 0u) + (cf >= 4 * sf ? 1u : 0u);
                ctx.SummFreq += 3;
            }
            else
            {
                cf = 4 + (cf >= 9 * sf ? 1u : 0u) + (cf >= 12 * sf ? 1u : 0u) + (cf >= 15 * sf ? 1u : 0u);
                ctx.SummFreq = (ushort)(ctx.SummFreq + cf);
            }

            ref var newState = ref St(ctx.Stats + (ns1 * StateSize));
            newState.Successor = successor;
            newState.Symbol = foundSymbol;
            newState.Freq = (byte)cf;
            ctx.NumStats = (ushort)(ns1 + 1);
        }

        _maxContext = _minContext = fSuccessor;
    }

    private void RestartAfterMemoryFull()
    {
        RestartModel();
        RestartCount++;
    }

    private void RestartModel()
    {
        Array.Clear(_freeList);
        _text = _alignOffset;
        _hiUnit = _text + _size;
        _loUnit = _unitsStart = _hiUnit - (_size / 8 / UnitSize * 7 * UnitSize);
        _glueCount = 0;

        _orderFall = _maxOrder;
        _runLength = _initRL = -(_maxOrder < 12 ? _maxOrder : 12) - 1;
        _prevSuccess = 0;

        _hiUnit -= UnitSize;
        _minContext = _maxContext = _hiUnit;
        ref var mc = ref Ctx(_minContext);
        mc.Suffix = 0;
        mc.NumStats = 256;
        mc.SummFreq = 256 + 1;
        _foundState = _loUnit;
        mc.Stats = _loUnit;
        for (var i = 0u; i < 256; i++)
        {
            ref var state = ref St(_loUnit + (i * StateSize));
            state.Symbol = (byte)i;
            state.Freq = 1;
            state.Successor = 0;
        }

        _loUnit += 256 / 2 * UnitSize;

        for (var i = 0; i < 128; i++)
        {
            for (var k = 0; k < 8; k++)
            {
                var value = (ushort)(BinScale - (InitBinEsc[k] / (i + 2)));
                for (var m = 0; m < 64; m += 8)
                {
                    _binSumm[(i * 64) + k + m] = value;
                }
            }
        }

        for (var i = 0; i < 25; i++)
        {
            for (var k = 0; k < 16; k++)
            {
                ref var see = ref _see[(i * 16) + k];
                see.Shift = PeriodBits - 4;
                see.Summ = (ushort)(((5 * i) + 10) << see.Shift);
                see.Count = 4;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int U2I(uint nu)
    {
        if (nu - 1 >= 128)
            PpmdMemory.ThrowCorrupted();

        return PpmdMemory.UnitsToIndex[(int)nu - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint I2U(int index) => PpmdMemory.IndexToUnits[index];

    private void InsertNode(uint node, int index)
    {
        PpmdMemory.At<uint>(_memory, node) = _freeList[index];
        _freeList[index] = node;
    }

    private uint RemoveNode(int index)
    {
        var node = _freeList[index];
        _freeList[index] = PpmdMemory.At<uint>(_memory, node);
        return node;
    }

    private void SplitBlock(uint ptr, int oldIndex, int newIndex)
    {
        var nu = I2U(oldIndex) - I2U(newIndex);
        ptr += I2U(newIndex) * UnitSize;
        var i = U2I(nu);
        if (I2U(i) != nu)
        {
            var k = I2U(--i);
            InsertNode(ptr + (k * UnitSize), (int)(nu - k - 1));
        }

        InsertNode(ptr, i);
    }

    private void GlueFreeBlocks()
    {
        // The doubly-linked list of the free blocks has its head in the spare unit at the end of the memory
        var head = _alignOffset + _size;
        ref var headNode = ref Node(head);
        headNode.Stamp = 0;
        headNode.NU = 0;
        headNode.Next = head;
        headNode.Prev = head;

        _glueCount = 255;
        if (_loUnit != _hiUnit)
        {
            PpmdMemory.At<ushort>(_memory, _loUnit) = 0;
        }

        for (var i = 0; i < NumIndexes; i++)
        {
            while (_freeList[i] != 0)
            {
                var p = RemoveNode(i);
                ref var node = ref Node(p);
                var next = Node(head).Next;
                node.Prev = head;
                node.Next = next;
                Node(next).Prev = p;
                Node(head).Next = p;
                node.Stamp = EmptyNodeStamp;
                node.NU = (ushort)I2U(i);
            }
        }

        for (var p = Node(head).Next; p != head; p = Node(p).Next)
        {
            ref var node = ref Node(p);
            while (true)
            {
                var p1 = p + (node.NU * UnitSize);
                ref var node1 = ref Node(p1);
                if (node1.Stamp != EmptyNodeStamp || node.NU + node1.NU >= 0x10000)
                    break;

                Node(node1.Prev).Next = node1.Next;
                Node(node1.Next).Prev = node1.Prev;
                node.NU += node1.NU;
            }
        }

        while (true)
        {
            var p = Node(head).Next;
            if (p == head)
                break;

            var next = Node(p).Next;
            Node(head).Next = next;
            Node(next).Prev = head;

            uint nu = Node(p).NU;
            for (; nu > 128; nu -= 128, p += 128 * UnitSize)
            {
                InsertNode(p, NumIndexes - 1);
            }

            var i = U2I(nu);
            if (I2U(i) != nu)
            {
                var k = nu - I2U(--i);
                InsertNode(p + ((nu - k) * UnitSize), (int)k - 1);
            }

            InsertNode(p, i);
        }
    }

    private uint AllocUnitsRare(int index)
    {
        if (_glueCount == 0)
        {
            GlueFreeBlocks();
            if (_freeList[index] != 0)
                return RemoveNode(index);
        }

        var i = index;
        do
        {
            if (++i == NumIndexes)
            {
                var numBytes = I2U(index) * UnitSize;
                _glueCount--;
                if (_unitsStart - _text > numBytes)
                {
                    _unitsStart -= numBytes;
                    return _unitsStart;
                }

                return 0;
            }
        }
        while (_freeList[i] == 0);

        var result = RemoveNode(i);
        SplitBlock(result, i, index);
        return result;
    }

    private uint AllocUnits(int index)
    {
        if (_freeList[index] != 0)
            return RemoveNode(index);

        var numBytes = I2U(index) * UnitSize;
        if (numBytes <= _hiUnit - _loUnit)
        {
            var result = _loUnit;
            _loUnit += numBytes;
            return result;
        }

        return AllocUnitsRare(index);
    }

    private uint ShrinkUnits(uint oldPtr, uint oldNU, uint newNU)
    {
        var i0 = U2I(oldNU);
        var i1 = U2I(newNU);
        if (i0 == i1)
            return oldPtr;

        if (_freeList[i1] != 0)
        {
            var ptr = RemoveNode(i1);
            PpmdMemory.CopyUnits(_memory, ptr, oldPtr, newNU);
            InsertNode(oldPtr, i0);
            return ptr;
        }

        SplitBlock(oldPtr, i0, i1);
        return oldPtr;
    }

    private static byte[] CreateNS2Indx()
    {
        var result = new byte[256];
        int i;
        for (i = 0; i < 3; i++)
        {
            result[i] = (byte)i;
        }

        for (int m = i, k = 1; i < 256; i++)
        {
            result[i] = (byte)m;
            if (--k == 0)
            {
                k = ++m - 2;
            }
        }

        return result;
    }

    private static byte[] CreateNS2BSIndx()
    {
        var result = new byte[256];
        result[0] = 0 << 1;
        result[1] = 1 << 1;
        result.AsSpan(2, 9).Fill(2 << 1);
        result.AsSpan(11).Fill(3 << 1);
        return result;
    }

    private static byte[] CreateHB2Flag()
    {
        var result = new byte[256];
        result.AsSpan(0x40).Fill(8);
        return result;
    }
}
