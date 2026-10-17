using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    private uint[] _transitions;
    private int _position;
    private int _end;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Starts the decoding of a codeword segment (INITDEC).</summary>
    public void Initialize(byte[] data, int offset, int length)
    {
        _data = data;
        _transitions = Jpeg2000MqTables.ContextTransitions;
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
    /// <remarks>
    /// The state, its more probable symbol and their transitions are one entry of
    /// <see cref="Jpeg2000MqTables.ContextTransitions"/>, and RENORMD shifts the registers by as many bits as the interval
    /// needs at once, up to the bits left in the code register, instead of one bit per iteration.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ref byte context)
    {
        var current = context;
        var transitions = unsafe(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_transitions), current));
        var probability = transitions & 0xFFFF;
        var moreProbable = current & 1;
        int decision;
        var a = _a - probability;
        if ((_c >> 16) < probability)
        {
            // The less probable sub-interval is the lower one: LPS_EXCHANGE
            if (a < probability)
            {
                decision = moreProbable;
                context = (byte)(transitions >> 16);
            }
            else
            {
                decision = moreProbable ^ 1;
                context = (byte)(transitions >> 24);
            }

            a = probability;
        }
        else
        {
            _c -= probability << 16;
            if ((a & 0x8000) != 0)
            {
                _a = a;
                return moreProbable;
            }

            // MPS_EXCHANGE
            if (a < probability)
            {
                decision = moreProbable ^ 1;
                context = (byte)(transitions >> 24);
            }
            else
            {
                decision = moreProbable;
                context = (byte)(transitions >> 16);
            }
        }

        _a = a;
        Renormalize();
        return decision;
    }

    // RENORMD: the interval is shifted until its most significant bit is set, a byte being read whenever the code register
    // has no bit left. A shift of n bits equals n shifts of one bit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Renormalize()
    {
        var a = _a;
        var shift = BitOperations.LeadingZeroCount(a) - 16;
        if (shift <= _ct)
        {
            _a = a << shift;
            _c <<= shift;
            _ct -= shift;
            return;
        }

        while (true)
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            var count = Math.Min(shift, _ct);
            a <<= count;
            _c <<= count;
            _ct -= count;
            shift -= count;
            if (shift == 0)
                break;
        }

        _a = a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int ByteAt(int position) => position < _end ? _data[position] : 0xFF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
