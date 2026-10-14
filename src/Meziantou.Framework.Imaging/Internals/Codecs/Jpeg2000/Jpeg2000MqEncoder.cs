using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    /// <remarks>
    /// The state, its more probable symbol and their transitions are one entry of
    /// <see cref="Jpeg2000MqTables.ContextTransitions"/>, and RENORME shifts the registers by as many bits as the interval
    /// needs at once, up to the bits left before the next byte is written, instead of one bit per iteration.
    /// </remarks>
    public void Encode(int decision, ref byte context)
    {
        var current = context;
        var transitions = unsafe(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Jpeg2000MqTables.ContextTransitions), current));
        var probability = transitions & 0xFFFF;
        var a = _a - probability;
        if (decision == (current & 1))
        {
            // CODEMPS
            if ((a & 0x8000) != 0)
            {
                _a = a;
                _c += probability;
                return;
            }

            if (a < probability)
            {
                a = probability;
            }
            else
            {
                _c += probability;
            }

            context = (byte)(transitions >> 16);
        }
        else
        {
            // CODELPS
            if (a < probability)
            {
                _c += probability;
            }
            else
            {
                a = probability;
            }

            context = (byte)(transitions >> 24);
        }

        // RENORME: a shift of n bits equals n shifts of one bit, a byte being written whenever the count reaches zero
        var shift = BitOperations.LeadingZeroCount(a) - 16;
        if (shift < _ct)
        {
            _a = a << shift;
            _c <<= shift;
            _ct -= shift;
            return;
        }

        while (true)
        {
            var count = Math.Min(shift, _ct);
            a <<= count;
            _c <<= count;
            _ct -= count;
            shift -= count;
            if (_ct == 0)
            {
                ByteOut();
            }

            if (shift == 0)
                break;
        }

        _a = a;
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
