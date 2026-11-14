using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the Radiance picture format (Radiance File Formats, "Picture File Format"; Greg Ward, "Real Pixels",
/// Graphics Gems II) shared by the decoder and the encoder: the identifiers, the variables of the information header, the
/// limits of the run-length encodings, and the conversions between RGBE pixels and floating-point colors.
/// </summary>
/// <remarks>
/// <para>
/// A pixel is four bytes: a mantissa for each of the three colors, then an exponent they share. The exponent 0 is black;
/// otherwise a mantissa <c>m</c> with the exponent <c>e</c> is the color <c>(m + 0.5) × 2^(e - 136)</c>, the middle of the
/// interval the writer truncated the color into (the reference routines of Radiance read it so; FFmpeg reads
/// <c>m × 2^(e - 136)</c>). These values are exact in single precision: 9 significant bits, and the smallest one,
/// <c>0.5 × 2^-135</c>, is a subnormal float.
/// </para>
/// <para>
/// The writer gives the largest color of a pixel the exponent <c>e</c> for which it is in [2^(e - 1), 2^e), and each color
/// the mantissa <c>floor(c × 2^(8 - e))</c>, computed exactly (a scaling by a power of two), so the largest one is in
/// [128, 255]. A pixel whose largest color is at most 1E-32 is black, as Radiance writes it.
/// </para>
/// </remarks>
internal static class RadianceFormat
{
    /// <summary>The standard identifier of the first line of the information header.</summary>
    public static ReadOnlySpan<byte> RadianceSignature => "#?RADIANCE\n"u8;

    /// <summary>The identifier other writers put on the first line.</summary>
    public static ReadOnlySpan<byte> RgbeSignature => "#?RGBE\n"u8;

    /// <summary>The <c>FORMAT</c> value of RGB pixels.</summary>
    public const string RgbeFormat = "32-bit_rle_rgbe";

    /// <summary>The <c>FORMAT</c> value of CIE XYZ pixels.</summary>
    public const string XyzeFormat = "32-bit_rle_xyze";

    /// <summary>The shortest scanline that uses the new run-length encoding.</summary>
    public const int MinEncodedLength = 8;

    /// <summary>The longest scanline that uses the new run-length encoding (its length is a 15-bit number).</summary>
    public const int MaxEncodedLength = 0x7FFF;

    /// <summary>The longest run of one run code of the new encoding (the code is the length plus 128).</summary>
    public const int MaxRun = 127;

    /// <summary>The longest literal of the new encoding: a code of 1 to 128 is followed by as many bytes.</summary>
    public const int MaxLiteral = 128;

    /// <summary>The shortest run the encoder writes with a run code: a shorter one is cheaper in a literal.</summary>
    public const int MinRun = 4;

    /// <summary>The longest resolution string the decoder reads, its line feed included.</summary>
    public const int MaxResolutionLineLength = 64;

    /// <summary>The smallest color that cannot be stored: 2^127, whose exponent byte would be 256.</summary>
    public const double MaxColorExcluded = 1.7014118346046923E+38;

    /// <summary>The largest color of a pixel at or below which the encoder writes black, as Radiance writes it.</summary>
    public const double BlackThreshold = 1e-32;

    /// <summary>The standard primaries of Radiance (<c>src/common/color.h</c>, as the file format documentation gives them): red, green, blue and white, x then y.</summary>
    public static ReadOnlySpan<double> StandardPrimaries => [0.640, 0.330, 0.290, 0.600, 0.150, 0.060, 0.333, 0.333];

    /// <summary>The factors of the exponents: <c>2^(e - 136)</c>, and 0 for the exponent 0.</summary>
    private static readonly float[] ExponentScales = CreateExponentScales();

    /// <summary>Determines whether the data starts with one of the two identifiers.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(RadianceSignature) || prefix.StartsWith(RgbeSignature);

    /// <summary>Gets the factor of an exponent byte: <c>2^(e - 136)</c>, or 0 for the exponent 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetScale(byte exponent) => ExponentScales[exponent];

    /// <summary>Converts RGBE pixels to <see cref="PixelFormat.RgbaFloat"/> pixels with an alpha of 1.</summary>
    /// <param name="rgbe">Four bytes per pixel.</param>
    /// <param name="pixels">Four floats per pixel.</param>
    public static void ToFloats(ReadOnlySpan<byte> rgbe, Span<float> pixels)
    {
        var count = rgbe.Length / 4;
        if (pixels.Length < count * 4)
            throw new ArgumentException("The destination cannot hold the pixels.", nameof(pixels));

        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            var scale = ExponentScales[rgbe[offset + 3]];
            pixels[offset] = (rgbe[offset] + 0.5f) * scale;
            pixels[offset + 1] = (rgbe[offset + 1] + 0.5f) * scale;
            pixels[offset + 2] = (rgbe[offset + 2] + 0.5f) * scale;
            pixels[offset + 3] = 1f;
        }
    }

    /// <summary>Converts one RGBE pixel to a floating-point pixel with an alpha of 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToFloats(ReadOnlySpan<byte> rgbe, int offset, Span<float> pixel)
    {
        var scale = ExponentScales[rgbe[offset + 3]];
        pixel[0] = (rgbe[offset] + 0.5f) * scale;
        pixel[1] = (rgbe[offset + 1] + 0.5f) * scale;
        pixel[2] = (rgbe[offset + 2] + 0.5f) * scale;
        pixel[3] = 1f;
    }

    /// <summary>
    /// Converts a color to an RGBE pixel. The colors must be finite, not negative, and below 2^127 (<see cref="CanStore"/>).
    /// </summary>
    /// <returns>The pixel, red in the low byte and the exponent in the high byte.</returns>
    public static uint FromColor(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        if (max <= BlackThreshold)
            return 0;

        // max is in [2^(e - 1), 2^e): the scaling by 2^(8 - e) puts it in [128, 256), and is exact
        var exponent = Math.ILogB(max) + 1;
        var shift = 8 - exponent;
        var r = (uint)Math.Floor(Math.ScaleB(red, shift));
        var g = (uint)Math.Floor(Math.ScaleB(green, shift));
        var b = (uint)Math.Floor(Math.ScaleB(blue, shift));
        return r | (g << 8) | (b << 16) | ((uint)(exponent + 128) << 24);
    }

    /// <summary>Determines whether a color can be stored: finite, not negative, and below 2^127.</summary>
    public static bool CanStore(double value) => value >= 0 && value < MaxColorExcluded;

    public static InvalidImageContentException Invalid(string message) => new("The Radiance HDR file is invalid: " + message, ImageFormat.Radiance);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Radiance, feature);

    private static float[] CreateExponentScales()
    {
        var scales = new float[256];
        for (var e = 1; e < scales.Length; e++)
        {
            scales[e] = MathF.ScaleB(1f, e - 136);
        }

        return scales;
    }
}
