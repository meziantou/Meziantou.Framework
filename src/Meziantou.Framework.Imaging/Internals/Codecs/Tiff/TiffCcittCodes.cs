namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The code words of the CCITT facsimile compressions of TIFF (compressions 2, 3 and 4), transcribed from ITU-T
/// Recommendation T.4 (tables 2 and 3: the terminating and make-up codes of white and black runs) and ITU-T Recommendation
/// T.6 (table 1: the modes of two-dimensional coding).
/// </summary>
/// <remarks>
/// A run of 0 to 63 pixels is one terminating code. A longer run is one or more make-up codes (multiples of 64, up to
/// 2560, the largest one being repeated as often as needed) followed by the terminating code of the remainder. White and
/// black runs have their own codes, except the make-up codes from 1792, which are shared.
/// </remarks>
internal static class TiffCcittCodes
{
    /// <summary>The number of bits of the longest run code (the black codes have at most 13 bits).</summary>
    public const int MaxRunCodeLength = 13;

    /// <summary>The end-of-line code: eleven zeros and a one.</summary>
    public const uint EndOfLine = 0b000000000001;

    /// <summary>The number of bits of <see cref="EndOfLine"/>.</summary>
    public const int EndOfLineLength = 12;

    /// <summary>The longest run one make-up code stores.</summary>
    public const int MaxMakeUpRun = 2560;

    // T.6 table 1
    public const uint PassCode = 0b0001;
    public const int PassLength = 4;
    public const uint HorizontalCode = 0b001;
    public const int HorizontalLength = 3;

    /// <summary>The terminating codes of white runs of 0 to 63 pixels.</summary>
    private static readonly TiffCcittCode[] WhiteTerminating =
    [
        new(0b00110101, 8), new(0b000111, 6), new(0b0111, 4), new(0b1000, 4),
        new(0b1011, 4), new(0b1100, 4), new(0b1110, 4), new(0b1111, 4),
        new(0b10011, 5), new(0b10100, 5), new(0b00111, 5), new(0b01000, 5),
        new(0b001000, 6), new(0b000011, 6), new(0b110100, 6), new(0b110101, 6),
        new(0b101010, 6), new(0b101011, 6), new(0b0100111, 7), new(0b0001100, 7),
        new(0b0001000, 7), new(0b0010111, 7), new(0b0000011, 7), new(0b0000100, 7),
        new(0b0101000, 7), new(0b0101011, 7), new(0b0010011, 7), new(0b0100100, 7),
        new(0b0011000, 7), new(0b00000010, 8), new(0b00000011, 8), new(0b00011010, 8),
        new(0b00011011, 8), new(0b00010010, 8), new(0b00010011, 8), new(0b00010100, 8),
        new(0b00010101, 8), new(0b00010110, 8), new(0b00010111, 8), new(0b00101000, 8),
        new(0b00101001, 8), new(0b00101010, 8), new(0b00101011, 8), new(0b00101100, 8),
        new(0b00101101, 8), new(0b00000100, 8), new(0b00000101, 8), new(0b00001010, 8),
        new(0b00001011, 8), new(0b01010010, 8), new(0b01010011, 8), new(0b01010100, 8),
        new(0b01010101, 8), new(0b00100100, 8), new(0b00100101, 8), new(0b01011000, 8),
        new(0b01011001, 8), new(0b01011010, 8), new(0b01011011, 8), new(0b01001010, 8),
        new(0b01001011, 8), new(0b00110010, 8), new(0b00110011, 8), new(0b00110100, 8),
    ];

    /// <summary>The make-up codes of white runs of 64, 128, ... 1728 pixels.</summary>
    private static readonly TiffCcittCode[] WhiteMakeUp =
    [
        new(0b11011, 5), new(0b10010, 5), new(0b010111, 6), new(0b0110111, 7),
        new(0b00110110, 8), new(0b00110111, 8), new(0b01100100, 8), new(0b01100101, 8),
        new(0b01101000, 8), new(0b01100111, 8), new(0b011001100, 9), new(0b011001101, 9),
        new(0b011010010, 9), new(0b011010011, 9), new(0b011010100, 9), new(0b011010101, 9),
        new(0b011010110, 9), new(0b011010111, 9), new(0b011011000, 9), new(0b011011001, 9),
        new(0b011011010, 9), new(0b011011011, 9), new(0b010011000, 9), new(0b010011001, 9),
        new(0b010011010, 9), new(0b011000, 6), new(0b010011011, 9),
    ];

    /// <summary>The terminating codes of black runs of 0 to 63 pixels.</summary>
    private static readonly TiffCcittCode[] BlackTerminating =
    [
        new(0b0000110111, 10), new(0b010, 3), new(0b11, 2), new(0b10, 2),
        new(0b011, 3), new(0b0011, 4), new(0b0010, 4), new(0b00011, 5),
        new(0b000101, 6), new(0b000100, 6), new(0b0000100, 7), new(0b0000101, 7),
        new(0b0000111, 7), new(0b00000100, 8), new(0b00000111, 8), new(0b000011000, 9),
        new(0b0000010111, 10), new(0b0000011000, 10), new(0b0000001000, 10), new(0b00001100111, 11),
        new(0b00001101000, 11), new(0b00001101100, 11), new(0b00000110111, 11), new(0b00000101000, 11),
        new(0b00000010111, 11), new(0b00000011000, 11), new(0b000011001010, 12), new(0b000011001011, 12),
        new(0b000011001100, 12), new(0b000011001101, 12), new(0b000001101000, 12), new(0b000001101001, 12),
        new(0b000001101010, 12), new(0b000001101011, 12), new(0b000011010010, 12), new(0b000011010011, 12),
        new(0b000011010100, 12), new(0b000011010101, 12), new(0b000011010110, 12), new(0b000011010111, 12),
        new(0b000001101100, 12), new(0b000001101101, 12), new(0b000011011010, 12), new(0b000011011011, 12),
        new(0b000001010100, 12), new(0b000001010101, 12), new(0b000001010110, 12), new(0b000001010111, 12),
        new(0b000001100100, 12), new(0b000001100101, 12), new(0b000001010010, 12), new(0b000001010011, 12),
        new(0b000000100100, 12), new(0b000000110111, 12), new(0b000000111000, 12), new(0b000000100111, 12),
        new(0b000000101000, 12), new(0b000001011000, 12), new(0b000001011001, 12), new(0b000000101011, 12),
        new(0b000000101100, 12), new(0b000001011010, 12), new(0b000001100110, 12), new(0b000001100111, 12),
    ];

    /// <summary>The make-up codes of black runs of 64, 128, ... 1728 pixels.</summary>
    private static readonly TiffCcittCode[] BlackMakeUp =
    [
        new(0b0000001111, 10), new(0b000011001000, 12), new(0b000011001001, 12), new(0b000001011011, 12),
        new(0b000000110011, 12), new(0b000000110100, 12), new(0b000000110101, 12), new(0b0000001101100, 13),
        new(0b0000001101101, 13), new(0b0000001001010, 13), new(0b0000001001011, 13), new(0b0000001001100, 13),
        new(0b0000001001101, 13), new(0b0000001110010, 13), new(0b0000001110011, 13), new(0b0000001110100, 13),
        new(0b0000001110101, 13), new(0b0000001110110, 13), new(0b0000001110111, 13), new(0b0000001010010, 13),
        new(0b0000001010011, 13), new(0b0000001010100, 13), new(0b0000001010101, 13), new(0b0000001011010, 13),
        new(0b0000001011011, 13), new(0b0000001100100, 13), new(0b0000001100101, 13),
    ];

    /// <summary>The make-up codes of runs of 1792, 1856, ... 2560 pixels, shared by both colors.</summary>
    private static readonly TiffCcittCode[] ExtendedMakeUp =
    [
        new(0b00000001000, 11), new(0b00000001100, 11), new(0b00000001101, 11), new(0b000000010010, 12),
        new(0b000000010011, 12), new(0b000000010100, 12), new(0b000000010101, 12), new(0b000000010110, 12),
        new(0b000000010111, 12), new(0b000000011100, 12), new(0b000000011101, 12), new(0b000000011110, 12),
        new(0b000000011111, 12),
    ];

    /// <summary>The codes of the vertical modes, from <c>VL3</c> (the change is three pixels to the left of the reference one) to <c>VR3</c>.</summary>
    private static readonly TiffCcittCode[] Vertical =
    [
        new(0b0000010, 7), new(0b000010, 6), new(0b010, 3), new(0b1, 1), new(0b011, 3), new(0b000011, 6), new(0b0000011, 7),
    ];

    private static readonly ushort[] WhiteLookup = BuildLookup(WhiteTerminating, WhiteMakeUp);
    private static readonly ushort[] BlackLookup = BuildLookup(BlackTerminating, BlackMakeUp);

    /// <summary>Gets the code of a vertical mode.</summary>
    /// <param name="offset">The position of the change relative to the reference change, from -3 to 3.</param>
    /// <returns>The code.</returns>
    public static TiffCcittCode GetVertical(int offset) => Vertical[offset + 3];

    /// <summary>Gets the terminating code of a run.</summary>
    /// <param name="isBlack">Whether the run is black.</param>
    /// <param name="run">The length of the run, from 0 to 63.</param>
    /// <returns>The code.</returns>
    public static TiffCcittCode GetTerminating(bool isBlack, int run) => (isBlack ? BlackTerminating : WhiteTerminating)[run];

    /// <summary>Gets the make-up code of a multiple of 64 pixels.</summary>
    /// <param name="isBlack">Whether the run is black.</param>
    /// <param name="run">A multiple of 64, from 64 to 2560.</param>
    /// <returns>The code.</returns>
    public static TiffCcittCode GetMakeUp(bool isBlack, int run)
    {
        var index = (run / 64) - 1;
        if (index >= WhiteMakeUp.Length)
            return ExtendedMakeUp[index - WhiteMakeUp.Length];

        return (isBlack ? BlackMakeUp : WhiteMakeUp)[index];
    }

    /// <summary>
    /// Looks up the run code that starts a window of <see cref="MaxRunCodeLength"/> bits.
    /// </summary>
    /// <param name="isBlack">Whether the run is black.</param>
    /// <param name="window">The next <see cref="MaxRunCodeLength"/> bits, the first one in the most significant position.</param>
    /// <param name="run">The pixels of the code: less than 64 for a terminating code, a multiple of 64 for a make-up code.</param>
    /// <returns>The number of bits of the code, or 0 when the window starts with no run code of that color.</returns>
    public static int Lookup(bool isBlack, uint window, out int run)
    {
        var entry = (isBlack ? BlackLookup : WhiteLookup)[window];
        run = entry & 0x0FFF;
        return entry >> 12;
    }

    /// <summary>Builds the table that maps every window of 13 bits to the code it starts with: its length in the 4 high bits, its run in the 12 low bits.</summary>
    private static ushort[] BuildLookup(TiffCcittCode[] terminating, TiffCcittCode[] makeUp)
    {
        var table = new ushort[1 << MaxRunCodeLength];
        for (var i = 0; i < terminating.Length; i++)
        {
            Add(table, terminating[i], i);
        }

        for (var i = 0; i < makeUp.Length; i++)
        {
            Add(table, makeUp[i], (i + 1) * 64);
        }

        for (var i = 0; i < ExtendedMakeUp.Length; i++)
        {
            Add(table, ExtendedMakeUp[i], (makeUp.Length + i + 1) * 64);
        }

        return table;

        static void Add(ushort[] table, TiffCcittCode code, int run)
        {
            var shift = MaxRunCodeLength - code.Length;
            var first = (int)code.Bits << shift;
            var entry = (ushort)((code.Length << 12) | run);
            for (var i = 0; i < 1 << shift; i++)
            {
                if (table[first + i] != 0)
                    throw new InvalidOperationException("The CCITT run codes are not a prefix code.");

                table[first + i] = entry;
            }
        }
    }
}
