using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    /// Copies the state of the range decoder, for a loop that decodes many symbols with <see cref="ReadSymbol4"/> and keeps
    /// the state in locals; <see cref="Restore"/> gives it back to the decoder before its other methods are called.
    /// </summary>
    public State Save() => new() { Window = _window, Count = _count, Range = _range };

    /// <summary>Sets the state of the range decoder that <see cref="Save"/> copied and <see cref="ReadSymbol4"/> advanced.</summary>
    public void Restore(State state)
    {
        _window = state.Window;
        _count = state.Count;
        _range = state.Range;
    }

    /// <summary>
    /// <see cref="ReadSymbol"/> for a distribution of 4 symbols (5 values with the counter), on a state copied by
    /// <see cref="Save"/>: the coefficient levels, most of the symbols of a tile.
    /// </summary>
    // The loop of ReadSymbol unrolled for 4 symbols (cur( 3 ) is 0, below every value), Renormalize on the state, and the
    // adaptation of Av1Cdf.Update with the rate of 4 symbols
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbol4(ref State state, Span<ushort> cdf)
    {
        cdf = cdf[..5];
        var value = (uint)(state.Window >> state.Count);
        var range = state.Range;
        var scale = range >> 8;
        uint c0 = cdf[0];
        int symbol;
        uint prev;
        uint cur;
        var cur0 = ((scale * (((1u << 15) - c0) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + (3 * Av1Constants.EcMinProb);
        if (value >= cur0)
        {
            symbol = 0;
            prev = range;
            cur = cur0;
        }
        else
        {
            uint c1 = cdf[1];
            var cur1 = ((scale * (((1u << 15) - c1) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + (2 * Av1Constants.EcMinProb);
            if (value >= cur1)
            {
                symbol = 1;
                prev = cur0;
                cur = cur1;
            }
            else
            {
                uint c2 = cdf[2];
                var cur2 = ((scale * (((1u << 15) - c2) >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + Av1Constants.EcMinProb;
                if (value >= cur2)
                {
                    symbol = 2;
                    prev = cur1;
                    cur = cur2;
                }
                else
                {
                    symbol = 3;
                    prev = cur2;
                    cur = 0;
                }
            }
        }

        Renormalize(ref state, prev - cur, cur);
        if (!_disableCdfUpdate)
        {
            var counter = cdf[4];
            var rate = 5 + (counter > 15 ? 1 : 0) + (counter > 31 ? 1 : 0);
            for (var i = 0; i < 3; i++)
            {
                if (i < symbol)
                {
                    cdf[i] -= (ushort)(cdf[i] >> rate);
                }
                else
                {
                    cdf[i] += (ushort)(((1 << 15) - cdf[i]) >> rate);
                }
            }

            if (counter < 32)
                cdf[4] = (ushort)(counter + 1);
        }

        return symbol;
    }

    /// <summary>
    /// <see cref="ReadSymbol4"/> without branches on the decoded symbol, for the symbols that a branch predictor cannot guess
    /// (the levels of the coefficients). Returns the same symbol and leaves the same state and distribution.
    /// </summary>
    // The values cur( 0 ) > cur( 1 ) > cur( 2 ) > cur( 3 ) = 0 decrease: the symbol is the number of them above the value,
    // and symbol i is coded when value < cur( j ) for every j < i. prev - cur and cur are selected with the masks of these
    // comparisons, and the adaptation of each value of the distribution with the mask of its own comparison
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
        var below0 = value < cur0 ? 1 : 0;
        var below1 = value < cur1 ? 1 : 0;
        var below2 = value < cur2 ? 1 : 0;
        var mask0 = (uint)-below0;
        var mask1 = (uint)-below1;
        var mask2 = (uint)-below2;

        // symbol 0: range - cur0 and cur0; 1: cur0 - cur1 and cur1; 2: cur1 - cur2 and cur2; 3: cur2 and 0
        var d0 = range - cur0;
        var d1 = cur0 - cur1;
        var d2 = cur1 - cur2;
        var newRange = d0 + (mask0 & (d1 - d0));
        newRange += mask1 & (d2 - d1);
        newRange += mask2 & (cur2 - d2);
        var subtracted = cur0 - (mask0 & d1) - (mask1 & d2) - (mask2 & cur2);
        Renormalize(ref state, newRange, subtracted);
        if (!_disableCdfUpdate)
        {
            int counter = cdf[4];
            var rate = 5 + (counter > 15 ? 1 : 0) + (counter > 31 ? 1 : 0);
            cdf[0] = (ushort)(c0 + (((((1u << 15) - c0) >> rate) & ~mask0) - ((c0 >> rate) & mask0)));
            cdf[1] = (ushort)(c1 + (((((1u << 15) - c1) >> rate) & ~mask1) - ((c1 >> rate) & mask1)));
            cdf[2] = (ushort)(c2 + (((((1u << 15) - c2) >> rate) & ~mask2) - ((c2 >> rate) & mask2)));
            cdf[4] = (ushort)(counter + (counter < 32 ? 1 : 0));
        }

        return below0 + below1 + below2;
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
    [MethodImpl(MethodImplOptions.NoInlining)]
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

    /// <summary>The state of the range decoder: the window of bits, its count and the range.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct State
    {
        public ulong Window;
        public int Count;
        public uint Range;
    }
}
