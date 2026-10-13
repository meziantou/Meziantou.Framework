namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic decoder of T.800, Annex C (the MQ decoder), written from the flow charts of C.3: INITDEC (Figure C.20),
/// DECODE (Figure C.15), the two conditional exchanges (Figures C.16 and C.17), RENORMD (Figure C.18) and BYTEIN (Figure
/// C.19). A context is one byte: the state of Table C.2 in its seven high bits and the more probable symbol in the low one.
/// </summary>
/// <remarks>
/// A codeword segment may be shorter than what the decoder reads: once its bytes are consumed, the decoder is fed 1 bits
/// (D.4.1), which is what BYTEIN does when it meets a marker.
/// </remarks>
internal struct Jpeg2000MqDecoder
{
    private byte[] _data;
    private int _position;
    private int _end;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Starts the decoding of a codeword segment (INITDEC).</summary>
    public void Initialize(byte[] data, int offset, int length)
    {
        _data = data;
        _position = offset;
        _end = offset + length;
        _c = (uint)ByteAt(_position) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>Decodes one decision in a context (DECODE).</summary>
    /// <param name="context">The context; it is updated.</param>
    /// <returns>The decision, 0 or 1.</returns>
    public int Decode(ref byte context)
    {
        var state = context >> 1;
        var moreProbable = context & 1;
        uint probability = Jpeg2000MqTables.Probability[state];
        int decision;
        _a -= probability;
        if ((_c >> 16) < probability)
        {
            // The less probable sub-interval is the lower one: LPS_EXCHANGE
            if (_a < probability)
            {
                decision = moreProbable;
                context = (byte)((Jpeg2000MqTables.NextMoreProbable[state] << 1) | moreProbable);
            }
            else
            {
                decision = 1 - moreProbable;
                context = Jpeg2000MqTables.Switch[state] != 0
                    ? (byte)((Jpeg2000MqTables.NextLessProbable[state] << 1) | decision)
                    : (byte)((Jpeg2000MqTables.NextLessProbable[state] << 1) | moreProbable);
            }

            _a = probability;
        }
        else
        {
            _c -= probability << 16;
            if ((_a & 0x8000) != 0)
                return moreProbable;

            // MPS_EXCHANGE
            if (_a < probability)
            {
                decision = 1 - moreProbable;
                context = Jpeg2000MqTables.Switch[state] != 0
                    ? (byte)((Jpeg2000MqTables.NextLessProbable[state] << 1) | decision)
                    : (byte)((Jpeg2000MqTables.NextLessProbable[state] << 1) | moreProbable);
            }
            else
            {
                decision = moreProbable;
                context = (byte)((Jpeg2000MqTables.NextMoreProbable[state] << 1) | moreProbable);
            }
        }

        // RENORMD
        do
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);

        return decision;
    }

    private readonly int ByteAt(int position) => position < _end ? _data[position] : 0xFF;

    private void ByteIn()
    {
        if (ByteAt(_position) == 0xFF)
        {
            var next = ByteAt(_position + 1);
            if (next > 0x8F)
            {
                // A marker: the segment is over, 1 bits are fed
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                // The bit stuffed after an FF byte
                _position++;
                _c += (uint)next << 9;
                _ct = 7;
            }
        }
        else
        {
            _position++;
            _c += (uint)ByteAt(_position) << 8;
            _ct = 8;
        }
    }
}
