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
    private uint[] _transitions;
    private int _start;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Starts a codeword segment at the end of <paramref name="output"/> (INITENC).</summary>
    public void Initialize(PooledPayloadWriter output)
    {
        _output = output;
        _transitions = Jpeg2000MqTables.ContextTransitions;
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
    /// needs at once, up to the bits left before the next byte is written, instead of one bit per iteration. It is
    /// inlined, and BYTEOUT is a call that does not take the address of the encoder, so that a copy of the encoder in a
    /// local keeps its registers in processor registers.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Encode(int decision, ref byte context)
    {
        var current = context;
        var transitions = unsafe(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_transitions), current));
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
                (_c, _ct) = ByteOut(_output, _start, _c);
            }

            if (shift == 0)
                break;
        }

        _a = a;
    }

    /// <summary>Gets the state of the encoder between two decisions, for <see cref="GetTruncationLength"/>.</summary>
    public readonly Jpeg2000MqState GetState()
    {
        var count = _output.Length - _start;
        return new Jpeg2000MqState(count, count == 0 ? (byte)0 : _output.WrittenSpan[^1], _c, _a, _ct);
    }

    /// <summary>
    /// Gets the smallest number of bytes of a terminated codeword segment from which a decoder decodes every decision coded
    /// before a state: the decoder supplies 1 bits past the end of the bytes it has (C.3.4).
    /// </summary>
    /// <param name="segment">The bytes of the codeword segment, once <see cref="Flush"/> ended it.</param>
    /// <param name="state">The state of the encoder, from <see cref="GetState"/>.</param>
    /// <returns>The length: it is never followed by an FF byte of the segment, and it does not end with one.</returns>
    /// <remarks>
    /// <para>
    /// The decisions coded before the state are decoded as long as the value of the bytes kept, followed by 1 bits, is
    /// below the upper end of the interval of the state (the lower end is never passed: 1 bits only raise the value). The
    /// values are computed in units of a bit of the code register: the register is shifted CT more times before its next
    /// byte is written, so its bit <c>27 - CT</c> has the weight of the least significant bit of the last byte written,
    /// which a carry may still increment. A byte that follows an FF byte holds seven bits, its most significant one being
    /// the carry of the FF byte.
    /// </para>
    /// <para>
    /// An FF byte at the end can be removed: followed by 1 bits, it has the value of 1 bits.
    /// </para>
    /// </remarks>
    public static int GetTruncationLength(ReadOnlySpan<byte> segment, in Jpeg2000MqState state)
    {
        // The upper end of the interval, relative to the bytes before the last byte written
        var upper = ((UInt128)state.Last << (27 - state.Count)) + state.C + state.A;
        var exponent = 27 - state.Count;
        var length = state.Written;

        // The value of the bytes kept from the last byte written (0 for the byte before the first one), in units of the
        // least significant bit of the last byte kept
        UInt128 value = length == 0 ? 0u : segment[length - 1];
        var previous = length == 0 ? 0 : segment[length - 1];
        while (true)
        {
            var fits = exponent >= 0 ? (value + 1) << exponent <= upper : value + 1 <= upper << -exponent;
            if (fits || length >= segment.Length || exponent < -64)
                break;

            var bits = previous == 0xFF ? 7 : 8;
            previous = segment[length];
            value = (value << bits) + (uint)previous;
            exponent -= bits;
            length++;
        }

        length = Math.Min(length, segment.Length);
        return length > 0 && segment[length - 1] == 0xFF ? length - 1 : length;
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
        (_c, _ct) = ByteOut(_output, _start, _c);
        _c <<= _ct;
        (_c, _ct) = ByteOut(_output, _start, _c);

        // A segment does not end with an FF byte: the decoder supplies 1 bits past the end
        if (_output.Length > _start && _output.WrittenSpan[^1] == 0xFF)
        {
            _output.Truncate(_output.Length - 1);
        }

        return _output.Length - _start;
    }

    // BYTEOUT: writes the next byte of the code register; returns the code register without it and the number of bits
    // before the next byte
    private static (uint C, int Count) ByteOut(PooledPayloadWriter output, int start, uint c)
    {
        var hasByte = output.Length > start;
        var last = hasByte ? output.WrittenSpan[^1] : (byte)0;
        if (last == 0xFF)
        {
            // The byte that follows an FF byte holds seven bits
            output.Write((byte)(c >> 20));
            return (c & 0xFFFFF, 7);
        }

        if (c >= 0x8000000 && hasByte)
        {
            // The carry goes into the last byte
            output.WrittenSpan[^1] = ++last;
            c &= 0x7FFFFFF;
            if (last == 0xFF)
            {
                output.Write((byte)(c >> 20));
                return (c & 0xFFFFF, 7);
            }
        }

        output.Write((byte)(c >> 19));
        return (c & 0x7FFFF, 8);
    }
}
