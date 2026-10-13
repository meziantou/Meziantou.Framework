namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constant tables of intra prediction, transcribed from Rec. ITU-T H.265 (01/2026), clauses 8.4.3 and 8.4.4.2.
/// </summary>
internal static class HevcIntraTables
{
    /// <summary>
    /// <c>intraPredAngle</c> for <c>predModeIntra</c> 0 to 34 (Table 8-5); the entries of the planar and DC modes, which
    /// have no angle, are 0.
    /// </summary>
    public static ReadOnlySpan<sbyte> IntraPredAngle =>
    [
        0, 0,
        32, 26, 21, 17, 13, 9, 5, 2, 0, -2, -5, -9, -13, -17, -21, -26,
        -32, -26, -21, -17, -13, -9, -5, -2, 0, 2, 5, 9, 13, 17, 21, 26, 32,
    ];

    /// <summary><c>invAngle</c> for <c>predModeIntra</c> 11 to 25 (Table 8-6): the entry 0 is the mode 11.</summary>
    public static ReadOnlySpan<short> InvAngle =>
    [
        -4096, -1638, -910, -630, -482, -390, -315, -256,
        -315, -390, -482, -630, -910, -1638, -4096,
    ];

    /// <summary>
    /// <c>IntraPredModeC</c> for <c>modeIdx</c> 0 to 34 when <c>ChromaArrayType</c> is equal to 2 (Table 8-3).
    /// </summary>
    public static ReadOnlySpan<byte> Mode422 =>
    [
        0, 1, 2, 2, 2, 2, 3, 5, 7, 8, 10, 12, 13, 15, 17, 18, 19, 20,
        21, 22, 23, 23, 24, 24, 25, 25, 26, 27, 27, 28, 28, 29, 29, 30, 31,
    ];
}
