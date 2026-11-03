using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The symbol decoder of AV1 (AV1 specification, section 8.2): a range decoder that reads symbols coded with adaptive
/// cumulative distributions, and equiprobable literal bits. Each tile has its own coded data and is decoded from an initialized
/// decoder (<see cref="Initialize"/>) to its exit (<see cref="Exit"/>).
/// </summary>
/// <remarks>
/// <para>
/// A cumulative distribution of N symbols is an array of N + 1 values in the form of the specification: N increasing values
/// ending with 32768, then the counter that controls the adaptation rate.
/// </para>
/// <para>
/// The specification keeps a 15-bit <c>SymbolValue</c> and reads the stream one bit at a time. This implementation keeps the same
/// value in the upper bits of a wider window refilled one byte at a time, which is an exact equivalent: the bits of the window
/// are the inverted bits of the stream, and the bytes after the end of the tile data are zero bits.
/// </para>
/// </remarks>
internal sealed class Av1SymbolDecoder
{
    // The window holds SymbolValue in bits [_count + 14 .. _count] and the next _count bits of the stream below it
    private const int WindowRefillThreshold = 16;

    private byte[] _data = [];
    private int _position;
    private int _end;
    private ulong _window;
    private int _count;
    private uint _range;
    private bool _disableCdfUpdate;

    /// <summary>
    /// Starts decoding the <paramref name="length"/> bytes of tile data at <paramref name="data"/>[<paramref name="offset"/>]
    /// (<c>init_symbol( sz )</c>, section 8.2.2).
    /// </summary>
    public void Initialize(byte[] data, int offset, int length, bool disableCdfUpdate)
    {
        _data = data;
        _position = offset;
        _end = offset + length;
        _disableCdfUpdate = disableCdfUpdate;
        _window = 0;
        _count = -15;
        Refill();
        _range = 1 << 15;
    }

    /// <summary>
    /// Ends the decoding of a tile (<c>exit_symbol()</c>, section 8.2.4).
    /// </summary>
    /// <exception cref="InvalidImageContentException">The decoder consumed more padding bits than a conforming tile needs.</exception>
    public void Exit()
    {
        EnsureNotOverrun();
    }

    /// <summary>
    /// Verifies that the decoder did not read past the padding that a conforming tile allows: <c>SymbolMaxBits</c> only
    /// decreases, so a tile that is past this limit can never become valid again.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The tile data ends before the symbols it codes.</exception>
    public void EnsureNotOverrun()
    {
        // SymbolMaxBits starts at 8 * sz - 15 and loses the bits of every renormalization: the window holds count bits after
        // the 15 bits of SymbolValue, and position - offset bytes were read into it, so SymbolMaxBits is the bits after the
        // position plus the count
        var maxBits = (8L * (_end - _position)) + _count;
        if (maxBits < -14)
            throw Av1Errors.Invalid("the data of a tile ends before the symbols it codes.");
    }

    /// <summary>
    /// Decodes a symbol and adapts its distribution (<c>read_symbol( cdf )</c>, section 8.2.6). <paramref name="cdf"/> holds the
    /// cumulative values of the N symbols followed by the adaptation counter.
    /// </summary>
    public int ReadSymbol(Span<ushort> cdf)
    {
        var n = cdf.Length - 1;
        var value = (uint)(_window >> _count);
        var range = _range;
        var cur = range;
        uint prev;
        var symbol = -1;
        do
        {
            symbol++;
            prev = cur;
            var f = (uint)((1 << 15) - cdf[symbol]);
            cur = ((range >> 8) * (f >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift);
            cur += (uint)(Av1Constants.EcMinProb * (n - symbol - 1));
        }
        while (value < cur);

        Renormalize(prev - cur, cur);
        if (!_disableCdfUpdate)
            Av1Cdf.Update(cdf, symbol);

        return symbol;
    }

    /// <summary>
    /// Copies the state of the range decoder, for a loop that decodes many symbols with <see cref="ReadSymbol4Branchless"/> and keeps
    /// the state in locals; <see cref="Restore"/> gives it back to the decoder before its other methods are called.
    /// </summary>
    public State Save() => new() { Window = _window, Count = _count, Range = _range };

    /// <summary>Sets the state of the range decoder that <see cref="Save"/> copied and <see cref="ReadSymbol4Branchless"/> advanced.</summary>
    public void Restore(State state)
    {
        _window = state.Window;
        _count = state.Count;
        _range = state.Range;
    }

    /// <summary>
    /// <see cref="ReadSymbol"/> for a distribution of 4 symbols (5 values with the counter), on a state copied by
    /// <see cref="Save"/>, without branches on the decoded symbol: the coefficient levels, most of the symbols of a tile, which
    /// a branch predictor cannot guess.
    /// </summary>
    // The values cur( 0 ) > cur( 1 ) > cur( 2 ) > cur( 3 ) = 0 decrease: the symbol is the number of them above the value,
    // and symbol i is coded when value < cur( j ) for every j < i. prev and cur are read at the index of the symbol in the
    // sequence range, cur( 0 ) to cur( 3 ), and value i of the distribution decreases when i < symbol
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbol4Branchless(ref State state, Span<ushort> cdf)
    {
        cdf = cdf[..5];
        var value = (uint)(state.Window >> state.Count);
        var range = state.Range;
        var scale = range >> 8;
        uint c0 = cdf[0];
        uint c1 = cdf[1];
        uint c2 = cdf[2];
        var cur0 = ((scale * (((1u << 15) - c0) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + (3 * Av1Constants.EcMinProb);
        var cur1 = ((scale * (((1u << 15) - c1) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + (2 * Av1Constants.EcMinProb);
        var cur2 = ((scale * (((1u << 15) - c2) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + Av1Constants.EcMinProb;
        var symbol = (value < cur0 ? 1 : 0) + (value < cur1 ? 1 : 0) + (value < cur2 ? 1 : 0);
        var bounds = default(SymbolBounds);
        bounds[0] = range;
        bounds[1] = cur0;
        bounds[2] = cur1;
        bounds[3] = cur2;
        bounds[4] = 0;
        var subtracted = bounds[symbol + 1];
        var newRange = bounds[symbol] - subtracted;

        // The adaptation of Av1Cdf.Update with the rate of 4 symbols, before the renormalization: the values of the
        // distribution are not live across the refill
        if (!_disableCdfUpdate)
        {
            int counter = cdf[4];
            var rate = 5 + (counter > 15 ? 1 : 0) + (counter > 31 ? 1 : 0);
            if (Vector128.IsHardwareAccelerated)
            {
                // The 4 values in the lanes of a vector (the last one is 32768, which neither formula changes), lane i
                // decreased when i < symbol
                ref var values = ref unsafe(Unsafe.As<ushort, byte>(ref cdf[0]));
                var current = Vector128.CreateScalar(unsafe(Unsafe.ReadUnaligned<ulong>(ref values))).AsUInt16();
                var decreased = Vector128.LessThan(Vector128.Create((short)0, 1, 2, 3, 4, 5, 6, 7), Vector128.Create((short)symbol)).AsUInt16();
                var up = current + Vector128.ShiftRightLogical(Vector128.Create((ushort)(1 << 15)) - current, rate);
                var down = current - Vector128.ShiftRightLogical(current, rate);
                var updated = Vector128.ConditionalSelect(decreased, down, up).AsUInt64().ToScalar();
                unsafe
                {
                    Unsafe.WriteUnaligned(ref values, updated);
                }
            }
            else
            {
                var mask0 = (uint)-(symbol > 0 ? 1 : 0);
                var mask1 = (uint)-(symbol > 1 ? 1 : 0);
                var mask2 = (uint)-(symbol > 2 ? 1 : 0);
                cdf[0] = (ushort)(c0 + (((((1u << 15) - c0) >> rate) & ~mask0) - ((c0 >> rate) & mask0)));
                cdf[1] = (ushort)(c1 + (((((1u << 15) - c1) >> rate) & ~mask1) - ((c1 >> rate) & mask1)));
                cdf[2] = (ushort)(c2 + (((((1u << 15) - c2) >> rate) & ~mask2) - ((c2 >> rate) & mask2)));
            }

            cdf[4] = (ushort)(counter + (counter < 32 ? 1 : 0));
        }

        Renormalize(ref state, newRange, subtracted);
        return symbol;
    }

    /// <summary><see cref="ReadBool"/> on a state copied by <see cref="Save"/>, without a branch on the decoded bit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBoolBranchless(ref State state)
    {
        var value = (uint)(state.Window >> state.Count);
        var range = state.Range;
        var cur = (((range >> 8) * ((1 << 14) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + Av1Constants.EcMinProb;
        var bit = value < cur ? 1 : 0;
        var mask = (uint)-bit;

        // 1: the range becomes cur and nothing is subtracted; 0: range - cur, and cur is subtracted
        var newRange = (range - cur) + (mask & ((2 * cur) - range));
        Renormalize(ref state, newRange, cur & ~mask);
        return bit;
    }

    /// <summary>Decodes a bit with equal probabilities (<c>read_bool()</c>, section 8.2.3).</summary>
    public int ReadBool()
    {
        // read_symbol with the distribution { 1 << 14, 1 << 15, 0 }, which is not adapted
        var value = (uint)(_window >> _count);
        var range = _range;
        var cur = (((range >> 8) * ((1 << 14) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + Av1Constants.EcMinProb;
        if (value < cur)
        {
            Renormalize(cur, 0);
            return 1;
        }

        Renormalize(range - cur, cur);
        return 0;
    }

    /// <summary>Decodes an unsigned number of <paramref name="count"/> equiprobable bits, most significant first (<c>L(n)</c>, section 8.2.5).</summary>
    public int ReadLiteral(int count)
    {
        var x = 0;
        for (var i = 0; i < count; i++)
        {
            x = (2 * x) + ReadBool();
        }

        return x;
    }

    /// <summary>Decodes a number in the range 0 to <paramref name="valueCount"/> - 1 with the non-symmetric encoding (<c>NS(n)</c>, section 4.10.10).</summary>
    public int ReadNonSymmetric(int valueCount)
    {
        var w = BitOperations.Log2((uint)valueCount) + 1;
        var m = (1 << w) - valueCount;
        var v = ReadLiteral(w - 1);
        if (v < m)
            return v;

        var extraBit = ReadLiteral(1);
        return (v << 1) - m + extraBit;
    }

    // SymbolRange = newRange and SymbolValue -= subtracted, then both are scaled until the range has 16 bits again
    private void Renormalize(uint newRange, uint subtracted)
    {
        _window -= (ulong)subtracted << _count;
        var bits = 15 - BitOperations.Log2(newRange);
        _range = newRange << bits;
        _count -= bits;
        if (_count < WindowRefillThreshold)
            Refill();
    }

    // Renormalize on a state copied by Save
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Renormalize(ref State state, uint newRange, uint subtracted)
    {
        state.Window -= (ulong)subtracted << state.Count;

        // newRange is 1 to 65535: 15 - Log2( newRange )
        var bits = BitOperations.LeadingZeroCount(newRange) - 16;
        state.Range = newRange << bits;
        state.Count -= bits;
        if (state.Count < WindowRefillThreshold)
        {
            // The bytes of Refill: until the count is above 40
            state.Window = RefillWindow(state.Window, state.Count);
            state.Count += (((40 - state.Count) >> 3) + 1) << 3;
        }
    }

    // The window of Refill for a state copied by Save, whose count is not changed: the window and the count of a state stay in
    // registers when they are not given by reference
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong RefillWindow(ulong window, int count)
    {
        while (count <= 40)
        {
            var next = _position < _end ? _data[_position] : 0;
            _position++;
            window = (window << 8) | (byte)~next;
            count += 8;
        }

        return window;
    }

    private void Refill()
    {
        // A byte after the end of the data is a byte of zero bits, which are one bits in the inverted window
        while (_count <= 40)
        {
            var next = _position < _end ? _data[_position] : 0;
            _position++;
            _window = (_window << 8) | (byte)~next;
            _count += 8;
        }
    }

    // The range and the values cur( 0 ) to cur( 3 ) of a symbol of 4
    [InlineArray(5)]
    private struct SymbolBounds
    {
        private uint _element;
    }

    /// <summary>The state of the range decoder: the window of bits, its count and the range.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct State
    {
        public ulong Window;
        public int Count;
        public uint Range;
    }
}
