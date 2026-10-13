namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic encoder of T.800, Annex C (the MQ encoder), written from the flow charts of C.2: INITENC (Figure C.10),
/// CODEMPS and CODELPS (Figures C.6 and C.7), RENORME (Figure C.8), BYTEOUT (Figure C.9) and FLUSH (Figures C.11 and
/// C.12). A context is one byte, as in <see cref="Jpeg2000MqDecoder"/>.
/// </summary>
/// <remarks>
/// The bytes are appended to a payload writer. The byte "before the first one" of the flow charts does not exist: no carry
/// reaches it, because the code register starts at zero.
/// </remarks>
internal struct Jpeg2000MqEncoder
{
    private PooledPayloadWriter _output;
    private int _start;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Starts a codeword segment at the end of <paramref name="output"/> (INITENC).</summary>
    public void Initialize(PooledPayloadWriter output)
    {
        _output = output;
        _start = output.Length;
        _a = 0x8000;
        _c = 0;
        _ct = 12;
    }

    /// <summary>Encodes one decision in a context (ENCODE).</summary>
    /// <param name="decision">The decision, 0 or 1.</param>
    /// <param name="context">The context; it is updated.</param>
    public void Encode(int decision, ref byte context)
    {
        var state = context >> 1;
        var moreProbable = context & 1;
        uint probability = Jpeg2000MqTables.Probability[state];
        _a -= probability;
        if (decision == moreProbable)
        {
            // CODEMPS
            if ((_a & 0x8000) != 0)
            {
                _c += probability;
                return;
            }

            if (_a < probability)
            {
                _a = probability;
            }
            else
            {
                _c += probability;
            }

            context = (byte)((Jpeg2000MqTables.NextMoreProbable[state] << 1) | moreProbable);
        }
        else
        {
            // CODELPS
            if (_a < probability)
            {
                _c += probability;
            }
            else
            {
                _a = probability;
            }

            var nextMoreProbable = Jpeg2000MqTables.Switch[state] != 0 ? 1 - moreProbable : moreProbable;
            context = (byte)((Jpeg2000MqTables.NextLessProbable[state] << 1) | nextMoreProbable);
        }

        // RENORME
        do
        {
            _a <<= 1;
            _c <<= 1;
            _ct--;
            if (_ct == 0)
            {
                ByteOut();
            }
        }
        while ((_a & 0x8000) == 0);
    }

    /// <summary>Terminates the codeword segment (FLUSH).</summary>
    /// <returns>The number of bytes of the segment.</returns>
    public int Flush()
    {
        // SETBITS: as many 1 bits as possible in the code register, within the current interval
        var limit = _c + _a;
        _c |= 0xFFFF;
        if (_c >= limit)
        {
            _c -= 0x8000;
        }

        _c <<= _ct;
        ByteOut();
        _c <<= _ct;
        ByteOut();

        // A segment does not end with an FF byte: the decoder supplies 1 bits past the end
        if (_output.Length > _start && _output.WrittenSpan[^1] == 0xFF)
        {
            _output.Truncate(_output.Length - 1);
        }

        return _output.Length - _start;
    }

    private void ByteOut()
    {
        var hasByte = _output.Length > _start;
        var last = hasByte ? _output.WrittenSpan[^1] : (byte)0;
        if (last == 0xFF)
        {
            // The byte that follows an FF byte holds seven bits
            _output.Write((byte)(_c >> 20));
            _c &= 0xFFFFF;
            _ct = 7;
            return;
        }

        if (_c >= 0x8000000 && hasByte)
        {
            // The carry goes into the last byte
            _output.WrittenSpan[^1] = ++last;
            _c &= 0x7FFFFFF;
            if (last == 0xFF)
            {
                _output.Write((byte)(_c >> 20));
                _c &= 0xFFFFF;
                _ct = 7;
                return;
            }
        }

        _output.Write((byte)(_c >> 19));
        _c &= 0x7FFFF;
        _ct = 8;
    }
}
