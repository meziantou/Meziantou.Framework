namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tables of the baseline JPEG encoder: the example quantization tables of ITU-T T.81
/// Annex K.1 (tables K.1 and K.2) scaled by the quality setting, and the typical Huffman tables of Annex K.3 (tables
/// K.3 to K.6, given as the <c>BITS</c>/<c>HUFFVAL</c> lists of a DHT segment).
/// </summary>
/// <remarks>
/// <para>
/// Quality scaling (the widely used linear scaling of the Annex K tables): <c>s = 5000 / q</c> for <c>q &lt; 50</c>, else
/// <c>s = 200 - 2q</c> (integer division), and each entry is <c>clamp((base * s + 50) / 100, 1, 255)</c>. Quality 50 is
/// the Annex K table itself, quality 100 is all ones, and quality 1 clamps most entries to 255. The clamp keeps every value
/// within the 8-bit precision of baseline DQT segments.
/// </para>
/// <para>
/// The Huffman tables are fixed (the Annex K typical tables, which cover every symbol a baseline 8-bit scan can produce:
/// DC categories 0-11, AC run/size symbols with sizes 1-10, ZRL and EOB). Optimized (image-specific) tables are not
/// produced: they require the statistics of the whole image before its first entropy-coded byte, which contradicts the
/// streamed, single-pass MCU-row encoding (an extra pass or a buffered image).
/// </para>
/// </remarks>
internal static class JpegEncodingTables
{
    /// <summary>Gets the luminance quantization table of T.81 table K.1, in natural (row-major) order.</summary>
    public static ReadOnlySpan<byte> LuminanceQuantization =>
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    /// <summary>Gets the chrominance quantization table of T.81 table K.2, in natural (row-major) order.</summary>
    public static ReadOnlySpan<byte> ChrominanceQuantization =>
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    /// <summary>Gets the number of luminance DC codes of each length 1-16 (T.81 table K.3).</summary>
    public static ReadOnlySpan<byte> LuminanceDcBits => [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Gets the luminance DC symbols (categories) in code order (T.81 table K.3).</summary>
    public static ReadOnlySpan<byte> LuminanceDcValues => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    /// <summary>Gets the number of chrominance DC codes of each length 1-16 (T.81 table K.4).</summary>
    public static ReadOnlySpan<byte> ChrominanceDcBits => [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];

    /// <summary>Gets the chrominance DC symbols (categories) in code order (T.81 table K.4).</summary>
    public static ReadOnlySpan<byte> ChrominanceDcValues => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    /// <summary>Gets the number of luminance AC codes of each length 1-16 (T.81 table K.5).</summary>
    public static ReadOnlySpan<byte> LuminanceAcBits => [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    /// <summary>Gets the luminance AC run/size symbols in code order (T.81 table K.5).</summary>
    public static ReadOnlySpan<byte> LuminanceAcValues =>
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>Gets the number of chrominance AC codes of each length 1-16 (T.81 table K.6).</summary>
    public static ReadOnlySpan<byte> ChrominanceAcBits => [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];

    /// <summary>Gets the chrominance AC run/size symbols in code order (T.81 table K.6).</summary>
    public static ReadOnlySpan<byte> ChrominanceAcValues =>
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>Gets the scaling factor of a quality setting: <c>5000 / q</c> below 50, else <c>200 - 2q</c>.</summary>
    /// <param name="quality">The quality, 1 to 100.</param>
    /// <returns>The scaling factor, in percent.</returns>
    public static int GetQualityScale(int quality)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        return quality < 50 ? 5000 / quality : 200 - (quality * 2);
    }

    /// <summary>Scales an Annex K table for a quality setting: <c>clamp((base * s + 50) / 100, 1, 255)</c>.</summary>
    /// <param name="basis">The Annex K table (natural order).</param>
    /// <param name="quality">The quality, 1 to 100.</param>
    /// <param name="destination">The 64 scaled values (natural order).</param>
    public static void ScaleQuantizationTable(ReadOnlySpan<byte> basis, int quality, Span<ushort> destination)
    {
        var scale = GetQualityScale(quality);
        for (var i = 0; i < 64; i++)
        {
            destination[i] = (ushort)Math.Clamp(((basis[i] * scale) + 50) / 100, 1, 255);
        }
    }
}
