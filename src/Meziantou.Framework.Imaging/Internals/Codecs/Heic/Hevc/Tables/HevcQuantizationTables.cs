namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constant tables of the quantization parameters, of the scaling process and of the deblocking filter, transcribed
/// from Rec. ITU-T H.265 (01/2026).
/// </summary>
internal static class HevcQuantizationTables
{
    /// <summary><c>levelScale[ k ]</c> (clause 8.6.3).</summary>
    public static ReadOnlySpan<byte> LevelScale => [40, 45, 51, 57, 64, 72];

    /// <summary>
    /// <c>QpC</c> as a function of <c>qPi</c> from 30 to 43 for <c>ChromaArrayType</c> equal to 1 (Table 8-10): below 30
    /// <c>QpC</c> is <c>qPi</c>, above 43 it is <c>qPi - 6</c>.
    /// </summary>
    public static ReadOnlySpan<byte> ChromaQp420 => [29, 30, 31, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37];

    /// <summary><c>beta'</c> as a function of <c>Q</c> from 0 to 51 (Table 8-12).</summary>
    public static ReadOnlySpan<byte> BetaPrime =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6, 7, 8,
        9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36,
        38, 40, 42, 44, 46, 48, 50, 52, 54, 56, 58, 60, 62, 64,
    ];

    /// <summary><c>tC'</c> as a function of <c>Q</c> from 0 to 53 (Table 8-12).</summary>
    public static ReadOnlySpan<byte> TcPrime =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4,
        5, 5, 6, 6, 7, 8, 9, 10, 11, 13, 14, 16, 18, 20, 22, 24,
    ];

    /// <summary>
    /// The default values of <c>ScalingList[ 1..3 ][ 0..2 ][ i ]</c>, the lists of the intra prediction mode (Table 7-6).
    /// The default values of <c>ScalingList[ 0 ][ matrixId ][ i ]</c> are all 16 (Table 7-5).
    /// </summary>
    public static ReadOnlySpan<byte> DefaultScalingListIntra =>
    [
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 17, 16, 17, 16, 17, 18,
        17, 18, 18, 17, 18, 21, 19, 20, 21, 20, 19, 21, 24, 22, 22, 24,
        24, 22, 22, 24, 25, 25, 27, 30, 27, 25, 25, 29, 31, 35, 35, 31,
        29, 36, 41, 44, 41, 36, 47, 54, 54, 47, 65, 70, 65, 88, 88, 115,
    ];

    /// <summary>
    /// The default values of <c>ScalingList[ 1..3 ][ 3..5 ][ i ]</c>, the lists of the inter prediction mode (Table 7-6).
    /// </summary>
    public static ReadOnlySpan<byte> DefaultScalingListInter =>
    [
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 17, 17, 17, 17, 17, 18,
        18, 18, 18, 18, 18, 20, 20, 20, 20, 20, 20, 20, 24, 24, 24, 24,
        24, 24, 24, 24, 25, 25, 25, 25, 25, 25, 25, 28, 28, 28, 28, 28,
        28, 33, 33, 33, 33, 33, 41, 41, 41, 41, 54, 54, 54, 71, 71, 91,
    ];
}
