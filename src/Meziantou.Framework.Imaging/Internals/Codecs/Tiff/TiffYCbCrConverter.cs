namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The conversion of the YCbCr samples of a TIFF page to RGB (TIFF 6.0, section 21), for the pages whose strips or tiles
/// store the samples themselves. A page compressed with JPEG is converted by the JPEG decoder instead.
/// </summary>
/// <remarks>
/// <para>
/// With the luma coefficients <c>LumaRed</c>, <c>LumaGreen</c> and <c>LumaBlue</c> of <c>YCbCrCoefficients</c> (ITU-R
/// BT.601 by default: 0.299, 0.587, 0.114) and the three black and white pairs of <c>ReferenceBlackWhite</c>:
/// </para>
/// <list type="bullet">
/// <item><description><c>Y' = (Y - black) * 255 / (white - black)</c>, and for each chroma sample <c>C' = (C - black) * 127 / (white - black)</c>;</description></item>
/// <item><description><c>R = Y' + Cr' * (2 - 2 * LumaRed)</c> and <c>B = Y' + Cb' * (2 - 2 * LumaBlue)</c>;</description></item>
/// <item><description><c>G = (Y' - LumaBlue * B - LumaRed * R) / LumaGreen</c>;</description></item>
/// <item><description>each result is rounded to the nearest integer (ties upward) and clamped to 0..255.</description></item>
/// </list>
/// <para>
/// The arithmetic is IEEE double precision, one color at a time, with the operations in the order written above (no fused
/// multiply-add), so the result does not depend on the platform.
/// </para>
/// <para>
/// <c>ReferenceBlackWhite</c> defaults to 0 and 255 for luma and 128 and 255 for chroma (full range, chroma centered on
/// 128): the default of TIFF 6.0 (0 and 255 for every sample) describes RGB data and would shift every color of a YCbCr
/// page, so writers and readers of YCbCr data use this one.
/// </para>
/// <para>
/// Chroma is stored once for <c>h</c> x <c>v</c> pixels (<c>YCbCrSubSampling</c>) and is replicated, not interpolated: the
/// specification defines no reconstruction filter, and replication is exact for the data units the page stores.
/// </para>
/// </remarks>
internal sealed class TiffYCbCrConverter
{
    private static readonly double[] DefaultCoefficients = [0.299, 0.587, 0.114];
    private static readonly double[] DefaultReferenceBlackWhite = [0, 255, 128, 255, 128, 255];

    private readonly double[] _luma = new double[256];
    private readonly double[] _chromaBlue = new double[256];
    private readonly double[] _chromaRed = new double[256];
    private readonly double _lumaRed;
    private readonly double _lumaGreen;
    private readonly double _lumaBlue;

    private TiffYCbCrConverter(double[] coefficients, double[] reference, int horizontal, int vertical)
    {
        _lumaRed = coefficients[0];
        _lumaGreen = coefficients[1];
        _lumaBlue = coefficients[2];
        HorizontalSubsampling = horizontal;
        VerticalSubsampling = vertical;
        IsJfifCompatible = IsClose(coefficients, DefaultCoefficients) && IsClose(reference, DefaultReferenceBlackWhite);
        var redFactor = 2 - (2 * _lumaRed);
        var blueFactor = 2 - (2 * _lumaBlue);
        for (var i = 0; i < 256; i++)
        {
            _luma[i] = (i - reference[0]) * 255 / (reference[1] - reference[0]);
            _chromaBlue[i] = (i - reference[2]) * 127 / (reference[3] - reference[2]) * blueFactor;
            _chromaRed[i] = (i - reference[4]) * 127 / (reference[5] - reference[4]) * redFactor;
        }
    }

    /// <summary>Gets the number of pixels one chroma sample covers horizontally: 1, 2 or 4.</summary>
    public int HorizontalSubsampling { get; }

    /// <summary>Gets the number of pixels one chroma sample covers vertically: 1, 2 or 4.</summary>
    public int VerticalSubsampling { get; }

    /// <summary>Gets the number of bytes of one data unit: the luma samples of its pixels, then one Cb and one Cr sample.</summary>
    public int UnitLength => (HorizontalSubsampling * VerticalSubsampling) + 2;

    /// <summary>
    /// Gets a value indicating whether the samples are the full-range BT.601 YCbCr of JFIF, the only YCbCr data a JPEG
    /// datastream is converted from.
    /// </summary>
    public bool IsJfifCompatible { get; }

    /// <summary>Creates the conversion of a page.</summary>
    /// <param name="coefficients">The three values of <c>YCbCrCoefficients</c>, or <see langword="null"/> for the default.</param>
    /// <param name="reference">The six values of <c>ReferenceBlackWhite</c>, or <see langword="null"/> for the default.</param>
    /// <param name="horizontal">The horizontal subsampling factor.</param>
    /// <param name="vertical">The vertical subsampling factor.</param>
    /// <returns>The conversion, or <see langword="null"/> when a value makes it undefined (a zero or non-finite coefficient, a white equal to its black).</returns>
    public static TiffYCbCrConverter? Create(double[]? coefficients, double[]? reference, int horizontal, int vertical)
    {
        coefficients ??= DefaultCoefficients;
        reference ??= DefaultReferenceBlackWhite;
        foreach (var coefficient in coefficients)
        {
            if (!double.IsFinite(coefficient) || coefficient <= 0 || coefficient >= 1)
                return null;
        }

        for (var i = 0; i < 6; i += 2)
        {
            if (!double.IsFinite(reference[i]) || !double.IsFinite(reference[i + 1]) || reference[i] == reference[i + 1])
                return null;
        }

        return new TiffYCbCrConverter(coefficients, reference, horizontal, vertical);
    }

    /// <summary>Gets the number of bytes of the data units of a block.</summary>
    /// <param name="width">The number of pixels of a row of the block.</param>
    /// <param name="rowCount">The number of rows of the block.</param>
    /// <returns>The length: a block stores whole data units, so its width and its height are rounded up to them.</returns>
    public long GetStoredLength(int width, int rowCount)
    {
        var unitsAcross = ((long)width + HorizontalSubsampling - 1) / HorizontalSubsampling;
        var unitRows = ((long)rowCount + VerticalSubsampling - 1) / VerticalSubsampling;
        return unitsAcross * unitRows * UnitLength;
    }

    /// <summary>Converts the data units of a block to rows of interleaved RGB samples.</summary>
    /// <param name="units">The data units, as <see cref="GetStoredLength"/> describes them.</param>
    /// <param name="destination">Exactly <paramref name="rowCount"/> rows of <paramref name="width"/> RGB pixels.</param>
    /// <param name="width">The number of pixels of a row of the block.</param>
    /// <param name="rowCount">The number of rows of the block.</param>
    public void Convert(ReadOnlySpan<byte> units, Span<byte> destination, int width, int rowCount)
    {
        var horizontal = HorizontalSubsampling;
        var vertical = VerticalSubsampling;
        var unitLength = UnitLength;
        var unitsAcross = (width + horizontal - 1) / horizontal;
        var rowLength = width * 3;
        for (var unitRow = 0; unitRow * vertical < rowCount; unitRow++)
        {
            var unitsOfRow = units.Slice(unitRow * unitsAcross * unitLength, unitsAcross * unitLength);
            for (var unitColumn = 0; unitColumn < unitsAcross; unitColumn++)
            {
                var unit = unitsOfRow.Slice(unitColumn * unitLength, unitLength);
                var blue = _chromaBlue[unit[unitLength - 2]];
                var red = _chromaRed[unit[unitLength - 1]];
                for (var dy = 0; dy < vertical; dy++)
                {
                    var y = (unitRow * vertical) + dy;
                    if (y >= rowCount)
                        break;

                    for (var dx = 0; dx < horizontal; dx++)
                    {
                        var x = (unitColumn * horizontal) + dx;
                        if (x >= width)
                            break;

                        var luma = _luma[unit[(dy * horizontal) + dx]];
                        var r = luma + red;
                        var b = luma + blue;
                        var g = (luma - (_lumaBlue * b) - (_lumaRed * r)) / _lumaGreen;
                        var pixel = destination.Slice((y * rowLength) + (x * 3), 3);
                        pixel[0] = Round(r);
                        pixel[1] = Round(g);
                        pixel[2] = Round(b);
                    }
                }
            }
        }
    }

    private static byte Round(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0, 255);

    private static bool IsClose(double[] values, double[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (Math.Abs(values[i] - expected[i]) > 1e-6)
                return false;
        }

        return true;
    }
}
