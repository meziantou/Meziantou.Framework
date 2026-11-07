namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// The arithmetic encoder of one entropy-coded segment, written from ITU-T T.81 section D.1 for
/// <see cref="JpegTestImage"/>: the coding of a binary decision with the estimate of a statistics bin (figures D.3 to
/// D.6), the renormalization and the output of bytes with carry propagation and byte stuffing (figures D.7 to D.11), and
/// the termination of the segment (figure D.15). Nothing here uses the library.
/// </summary>
/// <remarks>
/// A statistics bin is the index of its state in table D.3 and the sense of its more probable symbol; a new bin is the
/// state 0 with the more probable symbol 0.
/// </remarks>
internal sealed class JpegTestArithmeticCoder
{
    // Table D.3, one row per state: Qe, next state after an LPS, next state after an MPS, exchange of the MPS sense
    private static readonly (int Qe, int NextLps, int NextMps, bool Switch)[] States =
    [
        (0x5A1D, 1, 1, true), (0x2586, 14, 2, false), (0x1114, 16, 3, false), (0x080B, 18, 4, false),
        (0x03D8, 20, 5, false), (0x01DA, 23, 6, false), (0x00E5, 25, 7, false), (0x006F, 28, 8, false),
        (0x0036, 30, 9, false), (0x001A, 33, 10, false), (0x000D, 35, 11, false), (0x0006, 9, 12, false),
        (0x0003, 10, 13, false), (0x0001, 12, 13, false), (0x5A7F, 15, 15, true), (0x3F25, 36, 16, false),
        (0x2CF2, 38, 17, false), (0x207C, 39, 18, false), (0x17B9, 40, 19, false), (0x1182, 42, 20, false),
        (0x0CEF, 43, 21, false), (0x09A1, 45, 22, false), (0x072F, 46, 23, false), (0x055C, 48, 24, false),
        (0x0406, 49, 25, false), (0x0303, 51, 26, false), (0x0240, 52, 27, false), (0x01B1, 54, 28, false),
        (0x0144, 56, 29, false), (0x00F5, 57, 30, false), (0x00B7, 59, 31, false), (0x008A, 60, 32, false),
        (0x0068, 62, 33, false), (0x004E, 63, 34, false), (0x003B, 32, 35, false), (0x002C, 33, 9, false),
        (0x5AE1, 37, 37, true), (0x484C, 64, 38, false), (0x3A0D, 65, 39, false), (0x2EF1, 67, 40, false),
        (0x261F, 68, 41, false), (0x1F33, 69, 42, false), (0x19A8, 70, 43, false), (0x1518, 72, 44, false),
        (0x1177, 73, 45, false), (0x0E74, 74, 46, false), (0x0BFB, 75, 47, false), (0x09F8, 77, 48, false),
        (0x0861, 78, 49, false), (0x0706, 79, 50, false), (0x05CD, 48, 51, false), (0x04DE, 50, 52, false),
        (0x040F, 50, 53, false), (0x0363, 51, 54, false), (0x02D4, 52, 55, false), (0x025C, 53, 56, false),
        (0x01F8, 54, 57, false), (0x01A4, 55, 58, false), (0x0160, 56, 59, false), (0x0125, 57, 60, false),
        (0x00F6, 58, 61, false), (0x00CB, 59, 62, false), (0x00AB, 61, 63, false), (0x008F, 61, 32, false),
        (0x5B12, 65, 65, true), (0x4D04, 80, 66, false), (0x412C, 81, 67, false), (0x37D8, 82, 68, false),
        (0x2FE8, 83, 69, false), (0x293C, 84, 70, false), (0x2379, 86, 71, false), (0x1EDF, 87, 72, false),
        (0x1AA9, 87, 73, false), (0x174E, 72, 74, false), (0x1424, 72, 75, false), (0x119C, 74, 76, false),
        (0x0F6B, 74, 77, false), (0x0D51, 75, 78, false), (0x0BB6, 77, 79, false), (0x0A40, 77, 48, false),
        (0x5832, 80, 81, true), (0x4D1C, 88, 82, false), (0x438E, 89, 83, false), (0x3BDD, 90, 84, false),
        (0x34EE, 91, 85, false), (0x2EAE, 92, 86, false), (0x299A, 93, 87, false), (0x2516, 86, 71, false),
        (0x5570, 88, 89, true), (0x4CA9, 95, 90, false), (0x44D9, 96, 91, false), (0x3E22, 97, 92, false),
        (0x3824, 99, 93, false), (0x32B4, 99, 94, false), (0x2E17, 93, 86, false), (0x56A8, 95, 96, true),
        (0x4F46, 101, 97, false), (0x47E5, 102, 98, false), (0x41CF, 103, 99, false), (0x3C3D, 104, 100, false),
        (0x375E, 99, 93, false), (0x5231, 105, 102, false), (0x4C0F, 106, 103, false), (0x4639, 107, 104, false),
        (0x415E, 103, 99, false), (0x5627, 105, 106, true), (0x50E7, 108, 107, false), (0x4B85, 109, 103, false),
        (0x5597, 110, 109, false), (0x504F, 111, 107, false), (0x5A10, 110, 111, true), (0x5522, 112, 109, false),
        (0x59EB, 112, 111, true),
    ];

    private readonly List<byte> _output;
    private readonly int _start;

    // The interval register and the code register: the bits 19 to 26 of the latter are the next byte, the bit 27 a carry
    private uint _interval = 0x10000;
    private uint _code;
    private int _shifts = 11;

    // The last byte, which a carry may still change (-1 before the first one), and the 0xFF bytes that follow it
    private int _pending = -1;
    private int _stacked;

    public JpegTestArithmeticCoder(List<byte> output)
    {
        _output = output;
        _start = output.Count;
    }

    /// <summary>Codes a binary decision with the estimate of a statistics bin, and adapts the bin.</summary>
    public void Code(ref (int State, int Mps) bin, int decision)
    {
        var (qe, nextLps, nextMps, exchange) = States[bin.State];
        var estimate = (uint)qe;
        _interval -= estimate;
        if (decision == bin.Mps)
        {
            // Figure D.4: the estimate is only revised when the interval is renormalized
            if (_interval >= 0x8000)
                return;

            if (_interval < estimate)
            {
                _code += _interval;
                _interval = estimate;
            }

            bin.State = nextMps;
        }
        else
        {
            // Figure D.3: the less probable symbol takes the upper sub-interval, unless it is the larger one
            if (_interval >= estimate)
            {
                _code += _interval;
                _interval = estimate;
            }

            if (exchange)
            {
                bin.Mps ^= 1;
            }

            bin.State = nextLps;
        }

        Renormalize();
    }

    /// <summary>Codes a binary decision with the fixed estimate of one half (Qe = 0x5A1D, more probable symbol 0, never adapted).</summary>
    public void CodeFixed(int decision)
    {
        const uint Estimate = 0x5A1D;
        _interval -= Estimate;
        if (decision == 0)
        {
            if (_interval >= 0x8000)
                return;

            if (_interval < Estimate)
            {
                _code += _interval;
                _interval = Estimate;
            }
        }
        else if (_interval >= Estimate)
        {
            _code += _interval;
            _interval = Estimate;
        }

        Renormalize();
    }

    /// <summary>Ends the segment (figure D.15): the code register is set to the value of the interval with the most trailing zero bits, and its bytes are written.</summary>
    /// <param name="discardFinalZeros">Whether the zero bytes at the end of the segment are omitted, which the decoder replaces.</param>
    public void Flush(bool discardFinalZeros)
    {
        var value = (_code + _interval - 1) & 0xFFFF0000;
        if (value < _code)
        {
            value += 0x8000;
        }

        _code = value << _shifts;
        WriteByte();
        _code <<= 8;
        WriteByte();

        // The byte a carry could still have changed, and the 0xFF bytes after it
        Emit(_pending);
        for (; _stacked > 0; _stacked--)
        {
            Emit(0xFF);
        }

        _pending = -1;
        if (discardFinalZeros)
        {
            while (_output.Count > _start && _output[^1] == 0)
            {
                _output.RemoveAt(_output.Count - 1);
            }

            // A stuffed zero is not a final zero: it belongs to its 0xFF byte
            if (_output.Count > _start && _output[^1] == 0xFF)
            {
                _output.Add(0);
            }
        }
    }

    /// <summary>Figure D.7: the interval is doubled until it is at least 0x8000; a byte leaves the code register every eight shifts.</summary>
    private void Renormalize()
    {
        do
        {
            _interval <<= 1;
            _code <<= 1;
            _shifts--;
            if (_shifts == 0)
            {
                WriteByte();
                _shifts = 8;
            }
        }
        while (_interval < 0x8000);
    }

    /// <summary>
    /// Figure D.9: takes the next byte out of the code register. A carry is added to the previous byte and turns the 0xFF
    /// bytes after it into zeros; a 0xFF byte is kept back, as a later carry may still change it.
    /// </summary>
    private void WriteByte()
    {
        var value = (int)(_code >> 19);
        if (value > 0xFF)
        {
            Emit(_pending + 1);
            for (; _stacked > 0; _stacked--)
            {
                Emit(0x00);
            }

            _pending = value & 0xFF;
        }
        else if (value == 0xFF)
        {
            _stacked++;
        }
        else
        {
            Emit(_pending);
            for (; _stacked > 0; _stacked--)
            {
                Emit(0xFF);
            }

            _pending = value;
        }

        _code &= 0x7FFFF;
    }

    /// <summary>Writes a byte, with a stuffed zero byte after 0xFF (figure D.10); nothing for the byte before the first one.</summary>
    private void Emit(int value)
    {
        if (value < 0)
            return;

        _output.Add((byte)value);
        if (value == 0xFF)
        {
            _output.Add(0);
        }
    }
}
