using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The symbol encoder of AV1: the range encoder whose output <see cref="Av1SymbolDecoder"/> decodes (AV1 specification,
/// section 8.2). The specification only describes the decoder; the encoder is its inverse.
/// </summary>
/// <remarks>
/// <para>
/// The decoder keeps a value <c>SymbolValue</c> below a range <c>SymbolRange</c>, splits the range into one interval per
/// symbol and decodes the symbol whose interval holds the value. Its value is made of the inverted bits of the stream minus
/// the lower bounds of the intervals of the symbols decoded so far. The encoder therefore accumulates these lower bounds
/// (<c>low</c>): after the last symbol, any number in <c>[low, low + range)</c> decodes to the symbols that were coded, and
/// the stream is the inverted bits of one of them.
/// </para>
/// <para>
/// <c>low</c> gains the bits that every renormalization shifts in. Its high bytes are written as soon as they are complete,
/// and an addition that overflows the bits still held carries into the bytes already written. The interval never grows, so
/// a carry never goes past the first byte of the tile.
/// </para>
/// <para>
/// The exit process (section 8.2.4) requires the 15 bits that the decoder holds at the end to be a one followed by zeros,
/// and zeros up to the end of the tile data: the encoder ends with the only number of the final interval whose 15 low bits,
/// inverted, have this form.
/// </para>
/// </remarks>
internal sealed class Av1SymbolEncoder
{
    // The bits of low that are held: at least the 16 bits that an addition changes, flushed by bytes above that
    private const int FlushThreshold = 24;

    private PooledPayloadWriter? _output;
    private int _start;
    private ulong _low;
    private int _lowBits;
    private uint _range;
    private bool _disableCdfUpdate;

    /// <summary>
    /// Starts the data of a tile, which are appended to <paramref name="output"/> (the counterpart of <c>init_symbol</c>,
    /// section 8.2.2).
    /// </summary>
    public void Initialize(PooledPayloadWriter output, bool disableCdfUpdate)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _start = output.Length;
        _disableCdfUpdate = disableCdfUpdate;
        _low = 0;
        _lowBits = 15;
        _range = 1 << 15;
    }

    /// <summary>
    /// Codes <paramref name="symbol"/> with <paramref name="cdf"/>, the cumulative values of N symbols followed by the
    /// adaptation counter, and adapts the distribution (the counterpart of <c>read_symbol( cdf )</c>, section 8.2.6).
    /// </summary>
    public void WriteSymbol(Span<ushort> cdf, int symbol)
    {
        var n = cdf.Length - 1;
        Debug.Assert((uint)symbol < (uint)n);
        var range = _range;
        var prev = symbol is 0 ? range : GetLowerBound(range, cdf[symbol - 1], n - symbol);
        var cur = GetLowerBound(range, cdf[symbol], n - symbol - 1);
        Encode(cur, prev);
        if (!_disableCdfUpdate)
            Av1Cdf.Update(cdf, symbol);
    }

    /// <summary>Codes a bit with equal probabilities (the counterpart of <c>read_bool()</c>, section 8.2.3).</summary>
    public void WriteBool(int value)
    {
        // read_symbol with the distribution { 1 << 14, 1 << 15, 0 }, which is not adapted
        var range = _range;
        var split = GetLowerBound(range, 1 << 14, 1);
        if (value is not 0)
        {
            Encode(0, split);
        }
        else
        {
            Encode(split, range);
        }
    }

    /// <summary>Codes an unsigned number of <paramref name="count"/> equiprobable bits, most significant first (<c>L(n)</c>, section 8.2.5).</summary>
    public void WriteLiteral(int value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            WriteBool((value >> i) & 1);
        }
    }

    /// <summary>Codes a number in the range 0 to <paramref name="valueCount"/> - 1 with the non-symmetric encoding (<c>NS(n)</c>, section 4.10.10).</summary>
    public void WriteNonSymmetric(int value, int valueCount)
    {
        Debug.Assert((uint)value < (uint)valueCount);
        var w = BitOperations.Log2((uint)valueCount) + 1;
        var m = (1 << w) - valueCount;
        if (value < m)
        {
            WriteLiteral(value, w - 1);
            return;
        }

        var coded = value + m;
        WriteLiteral(coded >> 1, w - 1);
        WriteLiteral(coded & 1, 1);
    }

    /// <summary>
    /// Ends the data of the tile (the counterpart of <c>exit_symbol</c>, section 8.2.4): writes the bits that are still held,
    /// the trailing one bit and the zero bits up to the next byte.
    /// </summary>
    /// <returns>The number of bytes of the tile data.</returns>
    public int Finish()
    {
        var output = _output ?? throw new InvalidOperationException("The symbol encoder is not initialized.");

        // The smallest number of the interval whose 15 low bits are 011111111111111: inverted, a one bit then zero bits
        var end = (_low & ~0x7FFFUL) | 0x3FFF;
        if (end < _low)
            end += 0x8000;

        if ((end >> _lowBits) is not 0)
        {
            output.PropagateCarry(output.Length - 1);
            end &= (1UL << _lowBits) - 1;
        }

        // The 14 low bits are zero bits of the stream, like the bits that follow the end of the data: they are not written
        var bits = _lowBits - 14;
        var value = end >> 14;
        var padding = (8 - (bits & 7)) & 7;
        value = (value << padding) | ((1UL << padding) - 1);
        bits += padding;
        while (bits > 0)
        {
            bits -= 8;
            output.Write((byte)(value >> bits));
        }

        var data = output.WrittenSpan[_start..];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)~data[i];
        }

        _output = null;
        return data.Length;
    }

    // The lower bound of the interval of a symbol, from the cumulative value that ends it and the number of symbols after it
    private static uint GetLowerBound(uint range, int cumulative, int followingSymbols)
    {
        var f = (uint)((1 << 15) - cumulative);
        return (((range >> 8) * (f >> Av1Constants.EcProbShift)) >> (7 - Av1Constants.EcProbShift)) + (uint)(Av1Constants.EcMinProb * followingSymbols);
    }

    // The value of the decoder is in [cur, prev): SymbolValue -= cur and SymbolRange = prev - cur, then both are scaled
    // until the range has 16 bits again
    private void Encode(uint cur, uint prev)
    {
        var output = _output!;
        _low += cur;
        if ((_low >> _lowBits) is not 0)
        {
            output.PropagateCarry(output.Length - 1);
            _low &= (1UL << _lowBits) - 1;
        }

        var range = prev - cur;
        var bits = 15 - BitOperations.Log2(range);
        _range = range << bits;
        _low <<= bits;
        _lowBits += bits;
        while (_lowBits >= FlushThreshold)
        {
            _lowBits -= 8;
            output.Write((byte)(_low >> _lowBits));
            _low &= (1UL << _lowBits) - 1;
        }
    }
}
