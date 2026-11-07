namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic decoder of one entropy-coded segment (ITU-T T.81 section D.2): binary decisions decoded from the code
/// register with an adaptive probability estimate per context (a statistics bin), the estimation state machine of table
/// D.3 and the fixed estimate of one half (section F.1.4.4.2).
/// </summary>
/// <remarks>
/// <para>
/// A statistics bin is one byte: the index of its state in table D.3 (bits 0-6) and the sense of its more probable symbol
/// (bit 7). A new bin is zero: the state 0 and the more probable symbol 0.
/// </para>
/// <para>
/// The bytes come from the <see cref="JpegEntropyReader"/> of the scan (byte stuffing removed). After the end of the
/// segment, the decoder is fed zero bytes (section D.2.6): an encoder may omit the final zero bytes of a segment, so
/// reading past the end is not an error, and a segment that is too short cannot be told from a valid one.
/// </para>
/// </remarks>
internal sealed class JpegArithmeticDecoder
{
    /// <summary>The fixed estimate of one half: Qe = 0x5A1D, never adapted (T.81 F.1.4.4.2).</summary>
    private const int FixedEstimate = 0x5A1D;

    // Table D.3: the LPS probability estimate of each state
    private static readonly ushort[] Estimates =
    [
        0x5A1D, 0x2586, 0x1114, 0x080B, 0x03D8, 0x01DA, 0x00E5, 0x006F, 0x0036, 0x001A, 0x000D, 0x0006, 0x0003, 0x0001,
        0x5A7F, 0x3F25, 0x2CF2, 0x207C, 0x17B9, 0x1182, 0x0CEF, 0x09A1, 0x072F, 0x055C, 0x0406, 0x0303, 0x0240, 0x01B1,
        0x0144, 0x00F5, 0x00B7, 0x008A, 0x0068, 0x004E, 0x003B, 0x002C, 0x5AE1, 0x484C, 0x3A0D, 0x2EF1, 0x261F, 0x1F33,
        0x19A8, 0x1518, 0x1177, 0x0E74, 0x0BFB, 0x09F8, 0x0861, 0x0706, 0x05CD, 0x04DE, 0x040F, 0x0363, 0x02D4, 0x025C,
        0x01F8, 0x01A4, 0x0160, 0x0125, 0x00F6, 0x00CB, 0x00AB, 0x008F, 0x5B12, 0x4D04, 0x412C, 0x37D8, 0x2FE8, 0x293C,
        0x2379, 0x1EDF, 0x1AA9, 0x174E, 0x1424, 0x119C, 0x0F6B, 0x0D51, 0x0BB6, 0x0A40, 0x5832, 0x4D1C, 0x438E, 0x3BDD,
        0x34EE, 0x2EAE, 0x299A, 0x2516, 0x5570, 0x4CA9, 0x44D9, 0x3E22, 0x3824, 0x32B4, 0x2E17, 0x56A8, 0x4F46, 0x47E5,
        0x41CF, 0x3C3D, 0x375E, 0x5231, 0x4C0F, 0x4639, 0x415E, 0x5627, 0x50E7, 0x4B85, 0x5597, 0x504F, 0x5A10, 0x5522,
        0x59EB,
    ];

    // Table D.3: the next state after a less probable symbol
    private static ReadOnlySpan<byte> NextAfterLps =>
    [
        1, 14, 16, 18, 20, 23, 25, 28, 30, 33, 35, 9, 10, 12,
        15, 36, 38, 39, 40, 42, 43, 45, 46, 48, 49, 51, 52, 54,
        56, 57, 59, 60, 62, 63, 32, 33, 37, 64, 65, 67, 68, 69,
        70, 72, 73, 74, 75, 77, 78, 79, 48, 50, 50, 51, 52, 53,
        54, 55, 56, 57, 58, 59, 61, 61, 65, 80, 81, 82, 83, 84,
        86, 87, 87, 72, 72, 74, 74, 75, 77, 77, 80, 88, 89, 90,
        91, 92, 93, 86, 88, 95, 96, 97, 99, 99, 93, 95, 101, 102,
        103, 104, 99, 105, 106, 107, 103, 105, 108, 109, 110, 111, 110, 112,
        112,
    ];

    // Table D.3: the next state after a more probable symbol
    private static ReadOnlySpan<byte> NextAfterMps =>
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 13,
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
        29, 30, 31, 32, 33, 34, 35, 9, 37, 38, 39, 40, 41, 42,
        43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56,
        57, 58, 59, 60, 61, 62, 63, 32, 65, 66, 67, 68, 69, 70,
        71, 72, 73, 74, 75, 76, 77, 78, 79, 48, 81, 82, 83, 84,
        85, 86, 87, 71, 89, 90, 91, 92, 93, 94, 86, 96, 97, 98,
        99, 100, 93, 102, 103, 104, 99, 106, 107, 103, 109, 107, 111, 109,
        111,
    ];

    private readonly JpegEntropyReader _reader;

    // The code register: Cx in the 16 high bits, the bits not yet compared below
    private uint _code;

    // The interval register, 0x8000 to 0x10000 between two decisions
    private int _interval;
    private int _bitsLeft;
    private bool _initialized;

    public JpegArithmeticDecoder(JpegEntropyReader reader) => _reader = reader;

    /// <summary>Starts a new entropy-coded segment: the registers are initialized from its first two bytes at the next decision.</summary>
    public void Reset() => _initialized = false;

    /// <summary>Decodes one binary decision with the estimate of a statistics bin, and adapts the bin (T.81 figure D.9).</summary>
    public int Decode(ref byte bin)
    {
        var state = bin & 0x7F;
        var mps = bin >> 7;
        switch (DecodeDecision(Estimates[state]))
        {
            case Symbol.MoreProbableWithoutRenormalization:
                // The estimate is only revised when the interval is renormalized
                return mps;

            case Symbol.MoreProbable:
                bin = (byte)(NextAfterMps[state] | (mps << 7));
                return mps;

            default:
                // In these states of table D.3, a less probable symbol exchanges the sense of the more probable one
                var next = state is 0 or 14 or 36 or 64 or 80 or 88 or 95 or 105 or 110 or 112 ? mps ^ 1 : mps;
                bin = (byte)(NextAfterLps[state] | (next << 7));
                return mps ^ 1;
        }
    }

    /// <summary>Decodes one binary decision with the fixed estimate of one half, whose more probable symbol is 0.</summary>
    public int DecodeFixed() => DecodeDecision(FixedEstimate) == Symbol.LessProbable ? 1 : 0;

    /// <summary>The interval subdivision of figure D.9, with the conditional exchange of figures D.11 and D.12.</summary>
    private Symbol DecodeDecision(int estimate)
    {
        if (!_initialized)
        {
            Initialize();
        }

        Symbol symbol;
        _interval -= estimate;
        if ((_code >> 16) < (uint)_interval)
        {
            if (_interval >= 0x8000)
                return Symbol.MoreProbableWithoutRenormalization;

            // The MPS sub-interval became the smaller one: the symbols are exchanged
            symbol = _interval < estimate ? Symbol.LessProbable : Symbol.MoreProbable;
        }
        else
        {
            // The LPS sub-interval, at the top of the interval; the symbols are exchanged when it is the larger one
            _code -= (uint)_interval << 16;
            symbol = _interval < estimate ? Symbol.MoreProbable : Symbol.LessProbable;
            _interval = estimate;
        }

        // Renormalization (figure D.13): a byte is read every eight shifts
        do
        {
            if (_bitsLeft == 0)
            {
                _code += (uint)_reader.ReadByteOrZero() << 8;
                _bitsLeft = 8;
            }

            _interval <<= 1;
            _code <<= 1;
            _bitsLeft--;
        }
        while (_interval < 0x8000);

        return symbol;
    }

    /// <summary>The initialization of figure D.14: the code register holds the first two bytes of the segment.</summary>
    private void Initialize()
    {
        _code = ((uint)_reader.ReadByteOrZero() << 24) | ((uint)_reader.ReadByteOrZero() << 16);
        _interval = 0x10000;
        _bitsLeft = 0;
        _initialized = true;
    }

    private enum Symbol
    {
        MoreProbableWithoutRenormalization,
        MoreProbable,
        LessProbable,
    }
}
