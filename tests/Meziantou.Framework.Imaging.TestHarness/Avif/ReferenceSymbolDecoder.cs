namespace Meziantou.Framework.Imaging.TestHarness.Avif;

/// <summary>
/// The symbol decoder of AV1, transcribed step by step from the specification (section 8.2 "Parsing process for symbol
/// decoder") independently of the library under test: it reads the stream one bit at a time, keeps the 15-bit
/// <c>SymbolValue</c> of the specification, and checks the two conformance requirements of the exit process that a decoder
/// does not need to decode (the trailing one bit and the zero padding). It is what the output of the symbol encoder is
/// decoded with.
/// </summary>
public sealed class ReferenceSymbolDecoder
{
    private const int EcProbShift = 6;
    private const int EcMinProb = 4;

    private readonly byte[] _data;
    private readonly bool _disableCdfUpdate;
    private long _position;
    private int _symbolValue;
    private int _symbolRange;
    private long _symbolMaxBits;

    /// <summary>Starts decoding the data of a tile (<c>init_symbol( sz )</c>, section 8.2.2).</summary>
    /// <param name="data">The data of the tile.</param>
    /// <param name="disableCdfUpdate">The value of <c>disable_cdf_update</c>.</param>
    public ReferenceSymbolDecoder(ReadOnlySpan<byte> data, bool disableCdfUpdate)
    {
        _data = data.ToArray();
        _disableCdfUpdate = disableCdfUpdate;
        var numBits = (int)Math.Min(8L * _data.Length, 15);
        var buf = ReadBits(numBits);
        var paddedBuf = buf << (15 - numBits);
        _symbolValue = ((1 << 15) - 1) ^ paddedBuf;
        _symbolRange = 1 << 15;
        _symbolMaxBits = (8L * _data.Length) - 15;
    }

    /// <summary>Decodes a symbol (<c>read_symbol( cdf )</c>, section 8.2.6).</summary>
    /// <param name="cdf">The cumulative values of the N symbols followed by the adaptation counter.</param>
    /// <returns>The symbol.</returns>
    public int ReadSymbol(ushort[] cdf)
    {
        ArgumentNullException.ThrowIfNull(cdf);
        var n = cdf.Length - 1;
        var cur = _symbolRange;
        var symbol = -1;
        int prev;
        do
        {
            symbol++;
            prev = cur;
            var f = (1 << 15) - cdf[symbol];
            cur = ((_symbolRange >> 8) * (f >> EcProbShift)) >> (7 - EcProbShift);
            cur += EcMinProb * (n - symbol - 1);
        }
        while (_symbolValue < cur);

        _symbolRange = prev - cur;
        _symbolValue -= cur;
        var bits = 15 - FloorLog2(_symbolRange);
        _symbolRange <<= bits;
        var numBits = (int)Math.Min(bits, Math.Max(0, _symbolMaxBits));
        var newData = ReadBits(numBits);
        var paddedData = newData << (bits - numBits);
        _symbolValue = paddedData ^ (((_symbolValue + 1) << bits) - 1);
        _symbolMaxBits -= bits;

        if (!_disableCdfUpdate)
        {
            var rate = 3 + (cdf[n] > 15 ? 1 : 0) + (cdf[n] > 31 ? 1 : 0) + Math.Min(FloorLog2(n), 2);
            var tmp = 0;
            for (var i = 0; i < n - 1; i++)
            {
                tmp = i == symbol ? 1 << 15 : tmp;
                if (tmp < cdf[i])
                {
                    cdf[i] -= (ushort)((cdf[i] - tmp) >> rate);
                }
                else
                {
                    cdf[i] += (ushort)((tmp - cdf[i]) >> rate);
                }
            }

            cdf[n] += (ushort)(cdf[n] < 32 ? 1 : 0);
        }

        return symbol;
    }

    /// <summary>Decodes a bit with equal probabilities (<c>read_bool()</c>, section 8.2.3).</summary>
    /// <returns>The bit.</returns>
    public int ReadBool()
    {
        // The array is constructed each time, and disable_cdf_update does not matter: its modified values are never used
        ushort[] cdf = [1 << 14, 1 << 15, 0];
        return ReadSymbol(cdf);
    }

    /// <summary>Decodes an unsigned number of equiprobable bits (<c>read_literal( n )</c>, section 8.2.5).</summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The number.</returns>
    public int ReadLiteral(int count)
    {
        var x = 0;
        for (var i = 0; i < count; i++)
        {
            x = (2 * x) + ReadBool();
        }

        return x;
    }

    /// <summary>
    /// Ends the decoding of the tile (<c>exit_symbol()</c>, section 8.2.4) and checks the requirements of bitstream
    /// conformance of this process.
    /// </summary>
    /// <exception cref="InvalidDataException">The tile does not meet a requirement of bitstream conformance.</exception>
    public void Exit()
    {
        if (_symbolMaxBits < -14)
            throw new InvalidDataException($"SymbolMaxBits is {_symbolMaxBits} at the exit of the symbol decoder: it must be at least -14.");

        var trailingBitPosition = _position - Math.Min(15, _symbolMaxBits + 15);
        _position += Math.Max(0, _symbolMaxBits);
        var paddingEndPosition = _position;
        if (paddingEndPosition != 8L * _data.Length)
            throw new InvalidDataException("The padding of the tile data does not end with the data.");

        if (GetBit(trailingBitPosition) is not 1)
            throw new InvalidDataException("The trailing bit of the tile data is not 1.");

        for (var x = trailingBitPosition + 1; x < paddingEndPosition; x++)
        {
            if (GetBit(x) is not 0)
                throw new InvalidDataException($"The padding bit at position {x} of the tile data is not 0.");
        }
    }

    private static int FloorLog2(int x)
    {
        var s = 0;
        while (x > 1)
        {
            x >>= 1;
            s++;
        }

        return s;
    }

    private int GetBit(long position) => (_data[position >> 3] >> (7 - (int)(position & 7))) & 1;

    private int ReadBits(int count)
    {
        var x = 0;
        for (var i = 0; i < count; i++)
        {
            x = (2 * x) + GetBit(_position);
            _position++;
        }

        return x;
    }
}
