// Compiled into Meziantou.Framework.Imaging and Meziantou.Framework.Pdf (CCITTFaxDecode, JBIG2), internal to each. The copy
// of Pdf is in its namespace, so that Pdf does not depend on the internals of Imaging.
#if MEZIANTOU_FRAMEWORK_PDF
namespace Meziantou.Framework.Pdf.Filters;
#else
namespace Meziantou.Framework.Imaging.Internals;
#endif

/// <summary>The probability estimation of the MQ coder (T.800, Table C.2): for each of the 47 states, the probability of the less probable symbol and the transitions.</summary>
internal static class Jpeg2000MqTables
{
    /// <summary>The number of states.</summary>
    public const int StateCount = 47;

    /// <summary>The initial state of the context of the uniform distribution (Table D.7).</summary>
    public const int UniformState = 46;

    /// <summary>The initial state of the run-length context (Table D.7).</summary>
    public const int RunLengthState = 3;

    /// <summary>The initial state of the context of a coefficient whose neighbors are all insignificant (Table D.7).</summary>
    public const int ZeroNeighborState = 4;

    /// <summary>
    /// The transitions of a context byte (the state in its seven high bits, the more probable symbol in the low one), for
    /// the 256 values of a byte: Qe in the 16 low bits, the context after a more probable symbol that required a
    /// renormalization in the bits 16 to 23, and the context after a less probable symbol (with the exchange of the more
    /// probable symbol of SWITCH) in the bits 24 to 31. The values above the last state are never reached.
    /// </summary>
    internal static readonly uint[] ContextTransitions = CreateContextTransitions();

    /// <summary>Qe: the probability estimate of the less probable symbol.</summary>
    public static ReadOnlySpan<ushort> Probability =>
    [
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
        0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
        0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085, 0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
    ];

    /// <summary>NMPS: the state after a more probable symbol that required a renormalization.</summary>
    public static ReadOnlySpan<byte> NextMoreProbable =>
    [
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
    ];

    /// <summary>NLPS: the state after a less probable symbol.</summary>
    public static ReadOnlySpan<byte> NextLessProbable =>
    [
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14,
        15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
    ];

    /// <summary>SWITCH: whether a less probable symbol exchanges the sense of the more probable symbol.</summary>
    public static ReadOnlySpan<byte> Switch =>
    [
        1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static uint[] CreateContextTransitions()
    {
        var table = new uint[256];
        for (var context = 0; context < 2 * StateCount; context++)
        {
            var state = context >> 1;
            var moreProbable = context & 1;
            var nextMoreProbable = (NextMoreProbable[state] << 1) | moreProbable;
            var nextLessProbable = (NextLessProbable[state] << 1) | (Switch[state] != 0 ? 1 - moreProbable : moreProbable);
            table[context] = Probability[state] | ((uint)nextMoreProbable << 16) | ((uint)nextLessProbable << 24);
        }

        return table;
    }
}
