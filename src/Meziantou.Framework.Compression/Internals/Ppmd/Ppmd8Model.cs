// PPMd var.I rev. 1 model, a port of 7-Zip's Ppmd8.c and Ppmd8Dec.c (Igor Pavlov, public domain), which are based on
// PPMd var.I (2002) by Dmitry Shkarin (public domain). The model and its sub-allocator behave exactly as 7-Zip's, so that the
// streams are compatible: zip method 98 uses it, with the restore methods 0 (restart) and 1 (cut off).
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The PPMd var.I rev. 1 model (CPpmd8 of 7-Zip), with its sub-allocator over one byte array, and its decoder (its encoder is in Ppmd8Model.Encoder.cs).</summary>
internal sealed partial class Ppmd8Model
{
    public const int MinOrder = 2;
    public const int MaxOrder = 16;
    public const uint MinMemorySize = 1u << 11;
    public const uint MaxMemorySize = 0xFFFFFFFFu - (12 * 3);
    public const int RestoreMethodRestart = 0;
    public const int RestoreMethodCutOff = 1;

    private const int MaxFreq = 124;
    private const uint UnitSize = PpmdMemory.UnitSize;
    private const int NumIndexes = PpmdMemory.NumIndexes;
    private const int IntBits = 7;
    private const int PeriodBits = 7;
    private const uint BinScale = 1 << (IntBits + PeriodBits);
    private const int DummySeeIndex = 24 * 32;
    private const uint StateSize = 6;
    private const uint EmptyNode = 0xFFFFFFFF;
    private const int MaxOrderBound = 9;

    private static readonly ushort[] InitBinEsc = [0x3CDD, 0x1F3F, 0x59BF, 0x48F3, 0x64A1, 0x5ABC, 0x6632, 0x6051];
    private static readonly byte[] ExpEscape = [25, 14, 9, 7, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2];
    private static readonly byte[] NS2Indx = CreateNS2Indx();
    private static readonly byte[] NS2BSIndx = CreateNS2BSIndx();

    private readonly byte[] _memory;
    private readonly uint _size;
    private readonly uint _alignOffset;
    private readonly uint[] _freeList = new uint[NumIndexes];
    private readonly uint[] _stamps = new uint[NumIndexes];
    private readonly PpmdSee[] _see = new PpmdSee[DummySeeIndex + 1];
    private readonly ushort[] _binSumm = new ushort[32 * 64]; // 25 rows are used, the others make every byte index valid
    // 0xFF for the symbols that are not masked and 0 for the masked ones, so that the loops sum the frequencies of the symbols
    // that are not masked without a branch (Freq & mask), as 7-Zip does
    private readonly byte[] _charMask = new byte[256];
    private readonly uint[] _successorStates = new uint[MaxOrder + 1];
    private readonly uint[] _expandCounts = new uint[NumIndexes];
    private PpmdCarrylessRangeDecoder _rc;
    private int _maxOrder;
    private int _restoreMethod;
    private uint _minContext;
    private uint _maxContext;
    private uint _foundState;
    private int _orderFall;
    private uint _initEsc;
    private uint _prevSuccess;
    private int _runLength;
    private int _initRL;
    private uint _glueCount;
    private uint _text;
    private uint _unitsStart;
    private uint _loUnit;
    private uint _hiUnit;

    /// <summary>Allocates the memory of the model. The caller validates <paramref name="memorySize"/> first.</summary>
    public Ppmd8Model(uint memorySize)
    {
        _memory = PpmdMemory.Allocate(memorySize, out _alignOffset);
        _size = memorySize;
    }

    /// <summary>Gets the number of times the model restored its memory (restart or cut off) because it was full, since <see cref="Init"/>.</summary>
    public int RestoreCount { get; private set; }

    /// <summary>Gets the number of restorations that cut the model off rather than restarting it, since <see cref="Init"/>.</summary>
    public int CutOffCount { get; private set; }

    /// <summary>Gets the number of bytes the range decoder read after the end of the data (it reads zeros there).</summary>
    public int ExtraBytes => _rc.ExtraBytes;

    /// <summary>Validates the parameters of a model, as 7-Zip does.</summary>
    public static void ValidateParameters(int order, uint memorySize, int restoreMethod, long maxMemoryUsage)
    {
        if (order is < MinOrder or > MaxOrder)
            throw new InvalidDataException($"The PPMd order {order} is not supported (expected {MinOrder} to {MaxOrder}).");

        if (memorySize is < MinMemorySize or > MaxMemorySize)
            throw new InvalidDataException($"The PPMd memory size {memorySize} is not supported.");

        if (restoreMethod is not RestoreMethodRestart and not RestoreMethodCutOff)
            throw new InvalidDataException($"The PPMd restore method {restoreMethod} is not supported.");

        if (memorySize > maxMemoryUsage)
            throw new InvalidDataException($"The PPMd memory size ({memorySize} bytes) exceeds the maximum memory usage ({maxMemoryUsage} bytes).");
    }

    /// <summary>Starts a new model (Ppmd8_Init).</summary>
    public void Init(int maxOrder, int restoreMethod)
    {
        _maxOrder = maxOrder;
        _restoreMethod = restoreMethod;
        RestartModel();
        RestoreCount = 0;
        CutOffCount = 0;
        ref var dummySee = ref _see[DummySeeIndex];
        dummySee.Shift = PeriodBits;
        dummySee.Summ = 0;
        dummySee.Count = 64;
    }

    /// <summary>Initializes the range decoder (Ppmd8_Init_RangeDec). Returns <see langword="false"/> when the first bytes are not valid.</summary>
    public bool InitRangeDecoder(PpmdByteSource source) => _rc.Initialize(source);

    /// <summary>Decodes a symbol: a byte, -1 for the end marker (an escape from the order-0 context), or -2 for a data error.</summary>
    public int DecodeSymbol()
    {
        ref var mc = ref Ctx(_minContext);
        if (mc.NumStats != 0)
        {
            var s = mc.Stats;
            ref var state = ref St(s);
            var count = _rc.GetThreshold(mc.SummFreq);
            uint hiCnt = state.Freq;
            if (count < hiCnt)
            {
                _rc.Decode(0, hiCnt);
                _foundState = s;
                var symbol = state.Symbol;
                Update1_0();
                return symbol;
            }

            _prevSuccess = 0;
            int i = mc.NumStats;
            do
            {
                s += StateSize;
                state = ref St(s);
                if ((hiCnt += state.Freq) > count)
                {
                    _rc.Decode(hiCnt - state.Freq, state.Freq);
                    _foundState = s;
                    var symbol = state.Symbol;
                    Update1();
                    return symbol;
                }
            }
            while (--i > 0);

            if (count >= mc.SummFreq)
                return -2;

            _rc.Decode(hiCnt, mc.SummFreq - hiCnt);
            ResetMask();
            for (uint j = 0; j <= mc.NumStats; j++)
            {
                Mask(St(mc.Stats + (j * StateSize)).Symbol);
            }
        }
        else
        {
            ref var prob = ref GetBinSumm();
            if (_rc.DecodeBit(prob) == 0)
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
            var states = PpmdMemory.States(_memory, mc.Stats, mc.NumStats + 1);
            uint hiCnt = 0;
            foreach (ref readonly var state in states)
            {
                hiCnt += state.Freq & (uint)charMask[state.Symbol];
            }

            ref var see = ref MakeEscFreq(numMasked, out var freqSum);
            freqSum += hiCnt;
            var count = _rc.GetThreshold(freqSum);
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
                _rc.Decode(hiCnt - found.Freq, found.Freq);
                see.Update();
                _foundState = mc.Stats + ((uint)k * StateSize);
                var symbol = found.Symbol;
                Update2();
                return symbol;
            }

            if (count >= freqSum)
                return -2;

            _rc.Decode(hiCnt, freqSum - hiCnt);
            see.Summ = (ushort)(see.Summ + freqSum);
            foreach (ref readonly var state in states)
            {
                charMask[state.Symbol] = 0;
            }
        }
    }

    private static uint GetMean(uint prob) => (prob + (1 << (PeriodBits - 2))) >> PeriodBits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Ppmd8Context Ctx(uint offset) => ref PpmdMemory.At<Ppmd8Context>(_memory, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref PpmdState St(uint offset) => ref PpmdMemory.At<PpmdState>(_memory, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Ppmd8Node Node(uint offset) => ref PpmdMemory.At<Ppmd8Node>(_memory, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetMask() => _charMask.AsSpan().Fill(0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Mask(byte symbol) => _charMask[symbol] = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsMasked(byte symbol) => _charMask[symbol] == 0;

    private static byte SymbolFlag(byte symbol) => symbol >= 0x40 ? (byte)0x08 : (byte)0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ushort GetBinSumm()
    {
        ref var mc = ref Ctx(_minContext);
        ref var oneState = ref St(_minContext + 2);
        var index = (NS2Indx[(byte)(oneState.Freq - 1)] * 64)
            + NS2BSIndx[Ctx(mc.Suffix).NumStats]
            + (int)_prevSuccess
            + mc.Flags
            + ((_runLength >> 26) & 0x20);
        return ref _binSumm[index & ((32 * 64) - 1)];
    }

    private ref PpmdSee MakeEscFreq(uint numMasked, out uint escFreq)
    {
        ref var mc = ref Ctx(_minContext);
        uint numStats = mc.NumStats;
        if (numStats != 0xFF)
        {
            var index = ((NS2Indx[numStats + 2] - 3) * 32)
                + (mc.SummFreq > 11 * (numStats + 1) ? 1 : 0)
                + (2 * numStats < Ctx(mc.Suffix).NumStats + numMasked ? 2 : 0)
                + mc.Flags;
            if ((uint)index >= DummySeeIndex)
                PpmdMemory.ThrowCorrupted();

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
        _prevSuccess = 2u * state.Freq >= mc.SummFreq ? 1u : 0u;
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
        state.Freq = (byte)(state.Freq + (state.Freq < 196 ? 1 : 0));
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
        _minContext = _maxContext;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextContext()
    {
        var c = St(_foundState).Successor;
        if (_orderFall == 0 && c >= _unitsStart)
        {
            _minContext = _maxContext = c;
        }
        else
        {
            UpdateModel();
            _minContext = _maxContext;
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
        uint sumFreq = first.Freq;
        var escFreq = mc.SummFreq - sumFreq;
        var adder = _orderFall != 0 ? 1u : 0u;
        sumFreq = (sumFreq + 4 + adder) >> 1;
        first.Freq = (byte)sumFreq;

        int i = mc.NumStats;
        do
        {
            s += StateSize;
            ref var state = ref St(s);
            uint freq = state.Freq;
            escFreq -= freq;
            freq = (freq + adder) >> 1;
            sumFreq += freq;
            state.Freq = (byte)freq;
            if (freq > St(s - StateSize).Freq)
            {
                var tmp = state;
                var s1 = s;
                do
                {
                    St(s1) = St(s1 - StateSize);
                    s1 -= StateSize;
                }
                while (s1 != stats && freq > St(s1 - StateSize).Freq);

                St(s1) = tmp;
            }
        }
        while (--i > 0);

        if (St(s).Freq == 0)
        {
            uint removed = 0;
            do
            {
                removed++;
                s -= StateSize;
            }
            while (St(s).Freq == 0);

            escFreq += removed;
            uint numStats = mc.NumStats;
            var numStatsNew = numStats - removed;
            mc.NumStats = (byte)numStatsNew;
            var n0 = (numStats + 2) >> 1;
            if (numStatsNew == 0)
            {
                ref var firstState = ref St(stats);
                var freq = ((2 * (uint)firstState.Freq) + escFreq - 1) / escFreq;
                if (freq > MaxFreq / 3)
                {
                    freq = MaxFreq / 3;
                }

                mc.Flags = (byte)((mc.Flags & 0x10) + SymbolFlag(firstState.Symbol));
                var oneState = firstState;
                oneState.Freq = (byte)freq;
                _foundState = _minContext + 2;
                St(_foundState) = oneState;
                InsertNode(stats, U2I(n0));
                return;
            }

            var n1 = (numStatsNew + 2) >> 1;
            if (n0 != n1)
            {
                mc.Stats = ShrinkUnits(stats, n0, n1);
            }

            var flags = (byte)(mc.Flags & ~0x08);
            for (uint j = 0; j <= numStatsNew; j++)
            {
                flags |= SymbolFlag(St(mc.Stats + (j * StateSize)).Symbol);
            }

            mc.Flags = flags;
        }

        mc.SummFreq = (ushort)(sumFreq + escFreq - (escFreq >> 1));
        mc.Flags |= 0x04;
        _foundState = mc.Stats;
    }

    private uint CreateSuccessors(bool skip, uint s1, uint c)
    {
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
            uint s;
            if (s1 != 0)
            {
                s = s1;
                s1 = 0;
            }
            else
            {
                ref var ctx = ref Ctx(c);
                if (ctx.NumStats != 0)
                {
                    s = ctx.Stats;
                    while (St(s).Symbol != foundSymbol)
                    {
                        s += StateSize;
                    }

                    ref var state = ref St(s);
                    if (state.Freq < MaxFreq - 9)
                    {
                        state.Freq++;
                        ctx.SummFreq++;
                    }
                }
                else
                {
                    s = c + 2;
                    ref var state = ref St(s);
                    state.Freq = (byte)(state.Freq + ((Ctx(ctx.Suffix).NumStats == 0 && state.Freq < 24) ? 1 : 0));
                }
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
        var flags = (byte)((foundSymbol >= 0x40 ? 0x10 : 0) + SymbolFlag(upSymbol));
        byte upFreq;
        ref var context = ref Ctx(c);
        if (context.NumStats == 0)
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

                value = (cf + (2 * s0) - 3) / s0;
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
            child.NumStats = 0;
            child.Flags = flags;
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

    private uint ReduceOrder(uint s1, uint c)
    {
        uint s;
        var c1 = c;
        var upBranch = _text;
        var foundSymbol = St(_foundState).Symbol;

        St(_foundState).Successor = upBranch;
        _orderFall++;

        while (true)
        {
            if (s1 != 0)
            {
                c = Ctx(c).Suffix;
                s = s1;
                s1 = 0;
            }
            else
            {
                if (Ctx(c).Suffix == 0)
                    return c;

                c = Ctx(c).Suffix;
                ref var ctx = ref Ctx(c);
                if (ctx.NumStats != 0)
                {
                    s = ctx.Stats;
                    while (St(s).Symbol != foundSymbol)
                    {
                        s += StateSize;
                    }

                    ref var state = ref St(s);
                    if (state.Freq < MaxFreq - 9)
                    {
                        state.Freq += 2;
                        ctx.SummFreq += 2;
                    }
                }
                else
                {
                    s = c + 2;
                    ref var state = ref St(s);
                    state.Freq = (byte)(state.Freq + (state.Freq < 32 ? 1 : 0));
                }
            }

            if (St(s).Successor != 0)
                break;

            St(s).Successor = upBranch;
            _orderFall++;
        }

        if (St(s).Successor <= upBranch)
        {
            var foundState = _foundState;
            _foundState = s;
            var successor = CreateSuccessors(skip: false, 0, c);
            St(s).Successor = successor;
            _foundState = foundState;
        }

        if (_orderFall == 1 && c1 == _maxContext)
        {
            St(_foundState).Successor = St(s).Successor;
            _text--;
        }

        return St(s).Successor;
    }

    private void UpdateModel()
    {
        ref var foundState = ref St(_foundState);
        var foundSymbol = foundState.Symbol;
        uint foundFreq = foundState.Freq;
        var fSuccessor = foundState.Successor;
        uint s = 0;

        ref var minContext = ref Ctx(_minContext);
        if (foundFreq < MaxFreq / 4 && minContext.Suffix != 0)
        {
            var c = minContext.Suffix;
            ref var ctx = ref Ctx(c);
            if (ctx.NumStats == 0)
            {
                s = c + 2;
                ref var state = ref St(s);
                if (state.Freq < 32)
                {
                    state.Freq++;
                }
            }
            else
            {
                s = ctx.Stats;
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

        var c0 = _maxContext;
        if (_orderFall == 0 && fSuccessor != 0)
        {
            var cs = CreateSuccessors(skip: true, s, _minContext);
            if (cs == 0)
            {
                St(_foundState).Successor = 0;
                RestoreModel(c0);
            }
            else
            {
                St(_foundState).Successor = cs;
                _maxContext = cs;
            }

            return;
        }

        PpmdMemory.At<byte>(_memory, _text) = foundSymbol;
        _text++;
        var successor = _text;
        if (_text >= _unitsStart)
        {
            RestoreModel(c0);
            return;
        }

        if (fSuccessor == 0)
        {
            var cs = ReduceOrder(s, _minContext);
            if (cs == 0)
            {
                RestoreModel(c0);
                return;
            }

            fSuccessor = cs;
        }
        else if (fSuccessor < _unitsStart)
        {
            var cs = CreateSuccessors(skip: false, s, _minContext);
            if (cs == 0)
            {
                RestoreModel(c0);
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

        uint ns = Ctx(_minContext).NumStats;
        var s0 = (uint)Ctx(_minContext).SummFreq - ns - foundFreq;
        var flag = SymbolFlag(foundSymbol);
        for (var c = _maxContext; c != _minContext; c = Ctx(c).Suffix)
        {
            ref var ctx = ref Ctx(c);
            uint ns1 = ctx.NumStats;
            if (ns1 != 0)
            {
                if ((ns1 & 1) != 0)
                {
                    // Expand for one unit
                    var oldNU = (ns1 + 1) >> 1;
                    var i = U2I(oldNU);
                    if (i != U2I(oldNU + 1))
                    {
                        var ptr = AllocUnits(i + 1);
                        if (ptr == 0)
                        {
                            RestoreModel(c);
                            return;
                        }

                        var oldPtr = ctx.Stats;
                        PpmdMemory.CopyUnits(_memory, ptr, oldPtr, oldNU);
                        InsertNode(oldPtr, i);
                        ctx.Stats = ptr;
                    }
                }

                ctx.SummFreq = (ushort)(ctx.SummFreq + ((3 * ns1) + 1 < ns ? 1 : 0));
            }
            else
            {
                var s2 = AllocUnits(0);
                if (s2 == 0)
                {
                    RestoreModel(c);
                    return;
                }

                ref var state = ref St(s2);
                state = St(c + 2);
                ctx.Stats = s2;
                if (state.Freq < (MaxFreq / 4) - 1)
                {
                    state.Freq <<= 1;
                }
                else
                {
                    state.Freq = MaxFreq - 4;
                }

                ctx.SummFreq = (ushort)(state.Freq + _initEsc + (ns > 2 ? 1u : 0u));
            }

            var cf = 2 * foundFreq * (ctx.SummFreq + 6u);
            var sf = s0 + ctx.SummFreq;
            if (cf < 6 * sf)
            {
                cf = 1 + (cf > sf ? 1u : 0u) + (cf >= 4 * sf ? 1u : 0u);
                ctx.SummFreq += 4;
            }
            else
            {
                cf = 4 + (cf > 9 * sf ? 1u : 0u) + (cf > 12 * sf ? 1u : 0u) + (cf > 15 * sf ? 1u : 0u);
                ctx.SummFreq = (ushort)(ctx.SummFreq + cf);
            }

            ref var newState = ref St(ctx.Stats + ((ns1 + 1) * StateSize));
            newState.Successor = successor;
            newState.Symbol = foundSymbol;
            newState.Freq = (byte)cf;
            ctx.Flags |= flag;
            ctx.NumStats = (byte)(ns1 + 1);
        }

        _maxContext = _minContext = fSuccessor;
    }

    private void RestoreModel(uint c1)
    {
        RestoreCount++;
        _text = _alignOffset;

        uint c;
        for (c = _maxContext; c != c1; c = Ctx(c).Suffix)
        {
            ref var ctx = ref Ctx(c);
            if (--ctx.NumStats == 0)
            {
                var s = ctx.Stats;
                ref var state = ref St(s);
                ctx.Flags = (byte)((ctx.Flags & 0x10) + SymbolFlag(state.Symbol));
                var oneState = state;
                oneState.Freq = (byte)((oneState.Freq + 11u) >> 3);
                St(c + 2) = oneState;
                SpecialFreeUnit(s);
            }
            else
            {
                Refresh(c, (ctx.NumStats + 3u) >> 1, 0);
            }
        }

        for (; c != _minContext; c = Ctx(c).Suffix)
        {
            ref var ctx = ref Ctx(c);
            if (ctx.NumStats == 0)
            {
                ref var state = ref St(c + 2);
                state.Freq = (byte)(state.Freq - (state.Freq >> 1));
            }
            else if ((ctx.SummFreq += 4) > 128 + (4 * ctx.NumStats))
            {
                Refresh(c, (ctx.NumStats + 2u) >> 1, 1);
            }
        }

        if (_restoreMethod == RestoreMethodRestart || GetUsedMemory() < (_size >> 1))
        {
            RestartModel();
        }
        else
        {
            CutOffCount++;
            while (Ctx(_maxContext).Suffix != 0)
            {
                _maxContext = Ctx(_maxContext).Suffix;
            }

            do
            {
                CutOff(_maxContext, 0);
                ExpandTextArea();
            }
            while (GetUsedMemory() > 3 * (_size >> 2));

            _glueCount = 0;
            _orderFall = _maxOrder;
        }
    }

    private void Refresh(uint c, uint oldNU, int scale)
    {
        ref var ctx = ref Ctx(c);
        int i = ctx.NumStats;
        var s = ShrinkUnits(ctx.Stats, oldNU, (uint)(i + 2) >> 1);
        ctx.Stats = s;
        ref var state = ref St(s);
        var flags = (ctx.Flags & (0x10 + (0x04 * scale))) + SymbolFlag(state.Symbol);
        var escFreq = (uint)ctx.SummFreq - state.Freq;
        state.Freq = (byte)((state.Freq + scale) >> scale);
        uint sumFreq = state.Freq;
        do
        {
            s += StateSize;
            state = ref St(s);
            escFreq -= state.Freq;
            state.Freq = (byte)((state.Freq + scale) >> scale);
            sumFreq += state.Freq;
            flags |= SymbolFlag(state.Symbol);
        }
        while (--i > 0);

        ctx.SummFreq = (ushort)(sumFreq + ((escFreq + (uint)scale) >> scale));
        ctx.Flags = (byte)flags;
    }

    private uint CutOff(uint c, int order)
    {
        ref var ctx = ref Ctx(c);
        if (ctx.NumStats == 0)
        {
            var s = c + 2;
            var successor = St(s).Successor;
            if (successor >= _unitsStart)
            {
                successor = order < _maxOrder ? CutOff(successor, order + 1) : 0;
                St(s).Successor = successor;
                if (successor != 0 || order <= MaxOrderBound)
                    return c;
            }

            SpecialFreeUnit(c);
            return 0;
        }

        var nu = (ctx.NumStats + 2u) >> 1;
        ctx.Stats = MoveUnitsUp(ctx.Stats, nu);
        var stats = ctx.Stats;
        int i = ctx.NumStats;
        for (var s = stats + ((uint)i * StateSize); s >= stats; s -= StateSize)
        {
            var successor = St(s).Successor;
            if (successor < _unitsStart)
            {
                var s2 = stats + ((uint)i * StateSize);
                i--;
                St(s).Successor = 0;
                PpmdMemory.SwapStates(ref St(s), ref St(s2));
            }
            else
            {
                St(s).Successor = order < _maxOrder ? CutOff(successor, order + 1) : 0;
            }

            if (s == stats)
                break;
        }

        ctx = ref Ctx(c);
        if (i != ctx.NumStats && order != 0)
        {
            ctx.NumStats = (byte)i;
            var s = stats;
            if (i < 0)
            {
                FreeUnits(s, nu);
                SpecialFreeUnit(c);
                return 0;
            }

            if (i == 0)
            {
                ref var state = ref St(s);
                ctx.Flags = (byte)((ctx.Flags & 0x10) + SymbolFlag(state.Symbol));
                var oneState = state;
                oneState.Freq = (byte)((oneState.Freq + 11u) >> 3);
                St(c + 2) = oneState;
                FreeUnits(s, nu);
            }
            else
            {
                Refresh(c, nu, ctx.SummFreq > 16 * i ? 1 : 0);
            }
        }

        return c;
    }

    private uint GetUsedMemory()
    {
        uint v = 0;
        for (var i = 0; i < NumIndexes; i++)
        {
            v += _stamps[i] * I2U(i);
        }

        return _size - (_hiUnit - _loUnit) - (_unitsStart - _text) - (v * UnitSize);
    }

    private void RestartModel()
    {
        Array.Clear(_freeList);
        Array.Clear(_stamps);
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
        mc.NumStats = 255;
        mc.Flags = 0;
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

        for (int i = 0, m = 0; m < 25; m++)
        {
            while (NS2Indx[i] == m)
            {
                i++;
            }

            for (var k = 0; k < 8; k++)
            {
                var value = (ushort)(BinScale - (InitBinEsc[k] / (i + 1)));
                for (var r = 0; r < 64; r += 8)
                {
                    _binSumm[(m * 64) + k + r] = value;
                }
            }
        }

        for (int i = 0, m = 0; m < 24; m++)
        {
            while (NS2Indx[i + 3] == m + 3)
            {
                i++;
            }

            for (var k = 0; k < 32; k++)
            {
                ref var see = ref _see[(m * 32) + k];
                see.Shift = PeriodBits - 4;
                see.Summ = (ushort)(((2 * i) + 5) << see.Shift);
                see.Count = 7;
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
        ref var n = ref Node(node);
        n.Stamp = EmptyNode;
        n.Next = _freeList[index];
        n.NU = I2U(index);
        _freeList[index] = node;
        _stamps[index]++;
    }

    private uint RemoveNode(int index)
    {
        var node = _freeList[index];
        _freeList[index] = Node(node).Next;
        _stamps[index]--;
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
        _glueCount = 1 << 13;
        Array.Clear(_stamps);

        // Order-0 context is always at the top unit, so there is no guard node at the end. All the blocks up to LoUnit can
        // be free, so there is a guard node at LoUnit.
        if (_loUnit != _hiUnit)
        {
            Node(_loUnit).Stamp = 0;
        }

        // Glue the free blocks, and chain the remaining ones through Next. prev is the offset of the field to update.
        uint head = 0;
        uint prevField = 0; // 0 means head
        for (var i = 0; i < NumIndexes; i++)
        {
            var next = _freeList[i];
            _freeList[i] = 0;
            while (next != 0)
            {
                ref var node = ref Node(next);
                if (node.NU != 0)
                {
                    if (prevField == 0)
                    {
                        head = next;
                    }
                    else
                    {
                        PpmdMemory.At<uint>(_memory, prevField) = next;
                    }

                    prevField = next + 4; // offset of Next
                    while (true)
                    {
                        ref var node2 = ref Node(next + (node.NU * UnitSize));
                        if (node2.Stamp != EmptyNode || node2.NU == 0)
                            break;

                        node.NU += node2.NU;
                        node2.NU = 0;
                    }
                }

                next = node.Next;
            }
        }

        if (prevField == 0)
        {
            head = 0;
        }
        else
        {
            PpmdMemory.At<uint>(_memory, prevField) = 0;
        }

        // Fill the lists of free blocks
        while (head != 0)
        {
            var node = head;
            ref var n = ref Node(node);
            head = n.Next;
            var nu = n.NU;
            if (nu == 0)
                continue;

            for (; nu > 128; nu -= 128, node += 128 * UnitSize)
            {
                InsertNode(node, NumIndexes - 1);
            }

            var i = U2I(nu);
            if (I2U(i) != nu)
            {
                var k = I2U(--i);
                InsertNode(node + (k * UnitSize), (int)(nu - k - 1));
            }

            InsertNode(node, i);
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

    private void FreeUnits(uint ptr, uint nu) => InsertNode(ptr, U2I(nu));

    private void SpecialFreeUnit(uint ptr)
    {
        if (ptr != _unitsStart)
        {
            InsertNode(ptr, 0);
        }
        else
        {
            _unitsStart += UnitSize;
        }
    }

    private uint MoveUnitsUp(uint oldPtr, uint nu)
    {
        var index = U2I(nu);
        if (oldPtr > _unitsStart + 16384 || oldPtr > _freeList[index])
            return oldPtr;

        var ptr = RemoveNode(index);
        PpmdMemory.CopyUnits(_memory, ptr, oldPtr, nu);
        if (oldPtr != _unitsStart)
        {
            InsertNode(oldPtr, index);
        }
        else
        {
            _unitsStart += I2U(index) * UnitSize;
        }

        return ptr;
    }

    private void ExpandTextArea()
    {
        var count = _expandCounts;
        Array.Clear(count);
        if (_loUnit != _hiUnit)
        {
            Node(_loUnit).Stamp = 0;
        }

        var node = _unitsStart;
        while (Node(node).Stamp == EmptyNode)
        {
            ref var n = ref Node(node);
            if (n.NU is 0 or > 128)
                PpmdMemory.ThrowCorrupted();

            n.Stamp = 0;
            count[U2I(n.NU)]++;
            node += n.NU * UnitSize;
        }

        _unitsStart = node;

        for (var i = 0; i < NumIndexes; i++)
        {
            // prevField is the offset of the field that holds the reference to the current node (0 for the list head)
            uint prevField = 0;
            while (count[i] != 0)
            {
                var current = prevField == 0 ? _freeList[i] : PpmdMemory.At<uint>(_memory, prevField);
                if (current == 0)
                    PpmdMemory.ThrowCorrupted();

                while (Node(current).Stamp == 0)
                {
                    current = Node(current).Next;
                    if (prevField == 0)
                    {
                        _freeList[i] = current;
                    }
                    else
                    {
                        PpmdMemory.At<uint>(_memory, prevField) = current;
                    }

                    _stamps[i]--;
                    if (--count[i] == 0)
                        break;
                }

                prevField = current + 4; // offset of Next
            }
        }
    }

    private static byte[] CreateNS2Indx()
    {
        var result = new byte[260];
        int i;
        for (i = 0; i < 5; i++)
        {
            result[i] = (byte)i;
        }

        for (int m = i, k = 1; i < 260; i++)
        {
            result[i] = (byte)m;
            if (--k == 0)
            {
                k = ++m - 4;
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
}
