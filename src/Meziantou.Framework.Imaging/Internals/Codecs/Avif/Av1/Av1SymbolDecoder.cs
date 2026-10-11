using System.Numerics;

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
    private long _maxBits;
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
        _maxBits = (8L * length) - 15;
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
        if (_maxBits < -14)
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
        {
            var counter = cdf[n];
            var rate = 3 + (counter > 15 ? 1 : 0) + (counter > 31 ? 1 : 0) + Math.Min(BitOperations.Log2((uint)n), 2);
            for (var i = 0; i < n - 1; i++)
            {
                // tmp is 0 before the decoded symbol and 32768 from it
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
                cdf[n] = (ushort)(counter + 1);
        }

        return symbol;
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
        _maxBits -= bits;
        _count -= bits;
        if (_count < WindowRefillThreshold)
            Refill();
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
}
