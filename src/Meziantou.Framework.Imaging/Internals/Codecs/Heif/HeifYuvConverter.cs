using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts the rows of the decoded planes of an image of the HEIF family to the pixel layout that the decoder hands to the
/// frame sink: chroma upsampling, the matrix coefficients and the sample range of ITU-T H.273, and the scaling of 10 and
/// 12-bit samples to 16 bits.
/// </summary>
/// <remarks>
/// <para>
/// The codec specifications define the decoded planes exactly, not their conversion to R'G'B'. The conversion of the library
/// is integer only, so that it is identical on every platform:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Subsampled chroma is upsampled at the precision of the planes with a triangle filter: centered samples (9, 3, 3, 1 over 16)
/// unless the bitstream signals vertically or co-located samples; the samples of the edges are replicated. The chroma
/// samples of a 4:2:2 image are centered between two luma samples, or at the position of the first one when the bitstream
/// says so (HEVC).
/// </description></item>
/// <item><description>
/// A matrix is applied with coefficients rounded to 16 fractional bits, which include the scaling of the limited or full range
/// to the full range of the output: 0 to 255 for 8-bit planes without deeper alpha, otherwise 0 to 65535. Each component is
/// rounded once. The coefficients of the chromaticity-derived matrix (12) are the exact KR and KB of the color
/// primaries, computed as fractions from the chromaticities of ITU-T H.273, and the SMPTE ST 2085 matrix (11) uses its
/// two constants as the decimal numbers that the recommendation gives.
/// </description></item>
/// <item><description>
/// The constant luminance coefficients (10 and 13) and ICtCp (14) are not a matrix of the coded samples: they go through
/// the transfer characteristics that the file declares (<see cref="CicpTransfer"/>), in double precision. The result
/// is the nearest sample to the exact value, except within about a billionth of a rounding boundary, where the last
/// bit of the power functions of the platform decides: unlike the other conversions, it is not guaranteed to be
/// identical on every platform. The R'G'B' samples that are produced stay encoded with those transfer characteristics.
/// </description></item>
/// <item><description>
/// Samples that are not matrixed (monochrome, identity matrix and alpha in full range) are scaled by
/// <c>(v * outputMax + max / 2) / max</c>, which is exactly reversible.
/// </description></item>
/// <item><description>
/// Colors that are multiplied by the alpha of their pixel (premultiplied alpha) are divided by it once they are
/// converted: <c>min(outputMax, (c * outputMax + a / 2) / a)</c>, a division rounded to nearest whose result is clipped
/// when the color is above its alpha. A fully transparent pixel is transparent black (its color was multiplied by
/// zero), and an opaque pixel is unchanged.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class HeifYuvConverter
{
    private const long One = 1L << 16;
    private const long Half = 1L << 15;

    private readonly PixelFormat _layout;
    private readonly int _outputMax;
    private readonly bool _monochrome;
    private readonly int _matrix;
    private readonly int _subX;
    private readonly int _subY;
    private readonly int _chromaSamplePosition;
    private readonly int _max;
    private readonly bool _fullRange;
    private readonly int _yOffset;
    private readonly int _cOffset;
    private readonly long _cY;
    private readonly long _cRV;
    private readonly long _cGU;
    private readonly long _cGV;
    private readonly long _cBU;
    private readonly long _cC;
    private readonly long _cYR;
    private readonly long _cYB;
    private readonly int _alphaMax;
    private readonly bool _alphaFullRange;
    private readonly int _alphaOffset;
    private readonly long _cAlpha;
    private readonly bool _isPremultiplied;

    // The conversions that go through the transfer characteristics: the code point, the weights of the luminance, the
    // extents of the chroma of constant luminance, and the matrices of ICtCp
    private readonly bool _usesTransfer;
    private readonly byte _transfer;
    private readonly double _yScale;
    private readonly double _cScale;
    private readonly double _kr;
    private readonly double _kg;
    private readonly double _kb;
    private readonly double _nb;
    private readonly double _pb;
    private readonly double _nr;
    private readonly double _pr;
    private readonly double[]? _ictcpToLms;
    private readonly double[]? _lmsToRgb;

    /// <summary>Creates the converter of an image.</summary>
    /// <param name="binding">The format of the file, which errors report.</param>
    /// <param name="color">The sample format of the color image.</param>
    /// <param name="matrixCoefficients">The matrix coefficients code point that applies to the image.</param>
    /// <param name="fullRange">Whether the samples of the color image use the full range.</param>
    /// <param name="alpha">The sample format of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="layout">The pixel layout of the rows to produce.</param>
    /// <param name="isPremultiplied">Whether the color samples are multiplied by the alpha of their pixel: the pixels that are produced are not.</param>
    /// <param name="colorPrimaries">The color primaries code point that applies to the image, which the chromaticity-derived matrix coefficients need.</param>
    /// <param name="transferCharacteristics">The transfer characteristics code point that applies to the image, which the constant luminance and ICtCp matrix coefficients need.</param>
    /// <exception cref="UnsupportedImageFeatureException">The matrix coefficients are not supported.</exception>
    /// <exception cref="InvalidImageContentException">The identity matrix is used with subsampled chroma.</exception>
    public HeifYuvConverter(HeifCodecBinding binding, in HeifBitstreamFormat color, int matrixCoefficients, bool fullRange, HeifBitstreamFormat? alpha, PixelFormat layout, bool isPremultiplied = false, int colorPrimaries = Av1Constants.CpUnspecified, int transferCharacteristics = Av1Constants.TcUnspecified)
    {
        _layout = layout;
        _isPremultiplied = isPremultiplied && alpha is not null;
        _outputMax = layout is PixelFormat.Rgba64 or PixelFormat.Gray16 ? 65535 : 255;
        _monochrome = color.IsMonochrome;
        _matrix = matrixCoefficients;
        _subX = color.SubsamplingX;
        _subY = color.SubsamplingY;
        _chromaSamplePosition = color.ChromaSamplePosition;
        _fullRange = fullRange;
        var bitDepth = color.BitDepth;
        _max = (1 << bitDepth) - 1;
        _cOffset = 1 << (bitDepth - 1);
        long yRange;
        long cRange;
        if (fullRange)
        {
            yRange = _max;
            cRange = _max;
        }
        else
        {
            _yOffset = 16 << (bitDepth - 8);
            yRange = 219L << (bitDepth - 8);
            cRange = 224L << (bitDepth - 8);
        }

        EnsureSupported(binding, matrixCoefficients, _monochrome, _subX, _subY, colorPrimaries, transferCharacteristics);
        _cY = RoundedDivide(_outputMax * One, yRange);
        _cC = RoundedDivide(_outputMax * One, cRange);
        if (!_monochrome && matrixCoefficients is Av1Constants.McBt2020Cl or Av1Constants.McChromatCl or Av1Constants.McIctcp)
        {
            _usesTransfer = true;
            _transfer = (byte)transferCharacteristics;
            _yScale = 1.0 / yRange;
            _cScale = 1.0 / cRange;
            if (matrixCoefficients is Av1Constants.McIctcp)
            {
                // ITU-T H.273, MatrixCoefficients 14 (Rec. ITU-R BT.2100): L, M and S from linear R, G and B, then I, CT and
                // CP from L', M' and S', with the coefficients of the transfer characteristics (PQ or HLG), over 4096
                ReadOnlySpan<double> rgbToLms = [1688, 2146, 262, 683, 2951, 462, 99, 309, 3688];
                ReadOnlySpan<double> lmsToIctcp = transferCharacteristics is CicpTransfer.Hlg
                    ? [2048, 2048, 0, 3625, -7465, 3840, 9500, -9212, -288]
                    : [2048, 2048, 0, 6610, -13613, 7003, 17933, -17390, -543];
                _lmsToRgb = Invert(rgbToLms);
                _ictcpToLms = Invert(lmsToIctcp);
            }
            else
            {
                // ITU-T H.273, MatrixCoefficients 10 and 13: E'Y is the transfer characteristics function of the luminance
                // KR R + KG G + KB B, and the chroma is E'B - E'Y and E'R - E'Y over twice its extent, which is not the
                // same on both sides of zero: NB = (1 - KB)', PB = 1 - KB', NR = (1 - KR)' and PR = 1 - KR'
                var (kr, kb, d) = matrixCoefficients is Av1Constants.McBt2020Cl ? ((BigInteger)2627, (BigInteger)593, (BigInteger)10000) : GetChromaticityDerivedWeights(binding, colorPrimaries);
                _kr = (double)kr / (double)d;
                _kb = (double)kb / (double)d;
                _kg = 1 - _kr - _kb;
                _nb = CicpTransfer.Encode(_transfer, 1 - _kb);
                _pb = 1 - CicpTransfer.Encode(_transfer, _kb);
                _nr = CicpTransfer.Encode(_transfer, 1 - _kr);
                _pr = 1 - CicpTransfer.Encode(_transfer, _kr);
            }
        }
        else if (!_monochrome && matrixCoefficients is Av1Constants.McSmpte2085)
        {
            // ITU-T H.273, MatrixCoefficients 11: E'Y = E'G, E'PB = (0.986566 E'B - E'Y) / 2 and
            // E'PR = (E'R - 0.991902 E'Y) / 2, so G = Y, B = (Y + 2 PB) / 0.986566 and R = 0.991902 Y + 2 PR
            const long Million = 1_000_000;
            const long C1 = 986_566;
            const long C2 = 991_902;
            _cYB = RoundedDivide((Int128)_outputMax * One * Million, (Int128)C1 * yRange);
            _cBU = RoundedDivide((Int128)_outputMax * One * 2 * Million, (Int128)C1 * cRange);
            _cYR = RoundedDivide((Int128)_outputMax * One * C2, (Int128)Million * yRange);
            _cRV = RoundedDivide((Int128)_outputMax * One * 2, cRange);
        }
        else if (!_monochrome)
        {
            // KR and KB of the matrix coefficients, as fractions: over 10000 for the ones that ITU-T H.273 gives as decimal
            // numbers (table 4), and the exact values that the chromaticities of the color primaries give for the
            // chromaticity-derived matrix
            (BigInteger Kr, BigInteger Kb, BigInteger Denominator)? weights = matrixCoefficients switch
            {
                Av1Constants.McBt709 => (2126, 722, 10000),
                Av1Constants.McFcc => (3000, 1100, 10000),
                Av1Constants.McBt470BG or Av1Constants.McBt601 or Av1Constants.McUnspecified => (2990, 1140, 10000),
                Av1Constants.McSmpte240 => (2120, 870, 10000),
                Av1Constants.McBt2020Ncl => (2627, 593, 10000),
                Av1Constants.McChromatNcl => GetChromaticityDerivedWeights(binding, colorPrimaries),
                Av1Constants.McIdentity or Av1Constants.McSmpteYcgco => null,
                _ => throw binding.Unsupported($"The matrix coefficients {matrixCoefficients} of the {binding.Name} image are not supported.", "matrix coefficients"),
            };

            if (weights is var (kr, kb, d))
            {
                var kg = d - kr - kb;
                BigInteger scale = (BigInteger)_outputMax * One * 2;
                _cRV = RoundedDivide(scale * (d - kr), d * cRange);
                _cBU = RoundedDivide(scale * (d - kb), d * cRange);
                _cGU = RoundedDivide(scale * kb * (d - kb), d * kg * cRange);
                _cGV = RoundedDivide(scale * kr * (d - kr), d * kg * cRange);
            }
        }

        if (alpha is { } alphaFormat)
        {
            _alphaMax = (1 << alphaFormat.BitDepth) - 1;

            // An alpha plane uses the full range (ISO/IEC 23000-22, section 7.3.5.2): only a plane that states the
            // limited range is expanded, not one whose codec infers that range from the absence of a description
            _alphaFullRange = alphaFormat.FullRange || !alphaFormat.IsRangeSignaled;
            if (!_alphaFullRange)
            {
                _alphaOffset = 16 << (alphaFormat.BitDepth - 8);
                _cAlpha = RoundedDivide(_outputMax * One, 219L << (alphaFormat.BitDepth - 8));
            }
        }
    }

    /// <summary>Verifies that samples that use the matrix coefficients can be converted.</summary>
    /// <param name="binding">The format of the file, which errors report.</param>
    /// <param name="matrixCoefficients">The matrix coefficients code point.</param>
    /// <param name="isMonochrome">Whether the image has no chroma: the matrix does not apply to it.</param>
    /// <param name="subsamplingX">The horizontal subsampling of the chroma planes.</param>
    /// <param name="subsamplingY">The vertical subsampling of the chroma planes.</param>
    /// <param name="colorPrimaries">The color primaries code point that applies to the image.</param>
    /// <param name="transferCharacteristics">The transfer characteristics code point that applies to the image.</param>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The matrix coefficients are not supported, they are derived from color primaries that have no chromaticities, or
    /// they need transfer characteristics that the image does not declare.
    /// </exception>
    /// <exception cref="InvalidImageContentException">The identity matrix is used with subsampled chroma.</exception>
    public static void EnsureSupported(HeifCodecBinding binding, int matrixCoefficients, bool isMonochrome, int subsamplingX, int subsamplingY, int colorPrimaries = Av1Constants.CpUnspecified, int transferCharacteristics = Av1Constants.TcUnspecified)
    {
        if (isMonochrome)
            return;

        if (matrixCoefficients is not (Av1Constants.McIdentity or Av1Constants.McBt709 or Av1Constants.McUnspecified or Av1Constants.McFcc or Av1Constants.McBt470BG
            or Av1Constants.McBt601 or Av1Constants.McSmpte240 or Av1Constants.McSmpteYcgco or Av1Constants.McBt2020Ncl or Av1Constants.McBt2020Cl or Av1Constants.McSmpte2085
            or Av1Constants.McChromatNcl or Av1Constants.McChromatCl or Av1Constants.McIctcp))
        {
            throw binding.Unsupported($"The matrix coefficients {matrixCoefficients} of the {binding.Name} image are not supported.", "matrix coefficients");
        }

        if (matrixCoefficients is Av1Constants.McBt2020Cl or Av1Constants.McSmpte2085 or Av1Constants.McChromatNcl or Av1Constants.McChromatCl or Av1Constants.McIctcp && !binding.Supports(HeifContainerFeatures.ExtendedMatrices))
            throw binding.Unsupported($"The matrix coefficients {matrixCoefficients} of the {binding.Name} image are not supported.", "matrix coefficients");

        // The chromaticity-derived coefficients are the ones of the color primaries: unspecified or reserved primaries give none
        if (matrixCoefficients is Av1Constants.McChromatNcl or Av1Constants.McChromatCl && (colorPrimaries is < 0 or > byte.MaxValue || !CicpPrimaries.IsSupported((byte)colorPrimaries)))
            throw binding.Unsupported($"The matrix coefficients of the {binding.Name} image are derived from its color primaries ({colorPrimaries}), which have no chromaticities.", "matrix coefficients");

        // The constant luminance coefficients apply the transfer characteristics function to the luminance, and ICtCp is
        // defined for PQ and for HLG: unspecified or reserved transfer characteristics give no conversion
        var hasTransfer = transferCharacteristics is >= 0 and <= byte.MaxValue && CicpTransfer.IsSupported((byte)transferCharacteristics);
        if (matrixCoefficients is Av1Constants.McBt2020Cl or Av1Constants.McChromatCl && !hasTransfer)
            throw binding.Unsupported($"The constant luminance matrix coefficients of the {binding.Name} image need its transfer characteristics ({transferCharacteristics}), which have no function.", "matrix coefficients");

        if (matrixCoefficients is Av1Constants.McIctcp && transferCharacteristics is not (CicpTransfer.Pq or CicpTransfer.Hlg))
            throw binding.Unsupported($"The ICtCp matrix coefficients of the {binding.Name} image are defined for the PQ and HLG transfer characteristics, not for {transferCharacteristics}.", "matrix coefficients");

        if (matrixCoefficients is Av1Constants.McIdentity && (subsamplingX is not 0 || subsamplingY is not 0))
            throw binding.Invalid("the identity matrix requires 4:4:4 samples.");
    }

    private static long RoundedDivide(Int128 numerator, Int128 denominator) => (long)(((2 * numerator) + denominator) / (2 * denominator));

    private static long RoundedDivide(BigInteger numerator, BigInteger denominator) => (long)(((2 * numerator) + denominator) / (2 * denominator));

    // ITU-T H.273, section 8.3, MatrixCoefficients 12: KR and KB from the chromaticities (x, y) of the primaries and of the
    // white point, with z = 1 - x - y. Both are fractions of one denominator; the chromaticities are integers over a
    // common denominator, which the two equations cancel
    private static (BigInteger Kr, BigInteger Kb, BigInteger Denominator) GetChromaticityDerivedWeights(HeifCodecBinding binding, int colorPrimaries)
    {
        Span<int> values = stackalloc int[8];
        if (colorPrimaries is < 0 or > byte.MaxValue || !CicpPrimaries.TryGetChromaticities((byte)colorPrimaries, values))
            throw binding.Unsupported($"The matrix coefficients of the {binding.Name} image are derived from its color primaries ({colorPrimaries}), which have no chromaticities.", "matrix coefficients");

        BigInteger one = CicpPrimaries.GetDenominator((byte)colorPrimaries);
        BigInteger xR = values[0], yR = values[1], xG = values[2], yG = values[3], xB = values[4], yB = values[5], xW = values[6], yW = values[7];
        var (zR, zG, zB, zW) = (one - xR - yR, one - xG - yG, one - xB - yB, one - xW - yW);
        var denominator = yW * ((xR * ((yG * zB) - (yB * zG))) + (xG * ((yB * zR) - (yR * zB))) + (xB * ((yR * zG) - (yG * zR))));
        var kr = yR * ((xW * ((yG * zB) - (yB * zG))) + (yW * ((xB * zG) - (xG * zB))) + (zW * ((xG * yB) - (xB * yG))));
        var kb = yB * ((xW * ((yR * zG) - (yG * zR))) + (yW * ((xG * zR) - (xR * zG))) + (zW * ((xR * yG) - (xG * yR))));
        return denominator.Sign < 0 ? (-kr, -kb, -denominator) : (kr, kb, denominator);
    }

    /// <summary>Gets the number of bytes of a pixel of the layout.</summary>
    public int BytesPerPixel => PixelFormats.GetBytesPerPixel(_layout);

    /// <summary>
    /// Produces the chroma samples of a luma row at the resolution of the luma plane.
    /// </summary>
    /// <param name="image">The planes.</param>
    /// <param name="plane">The chroma plane: 1 or 2.</param>
    /// <param name="y">The luma row.</param>
    /// <param name="width">The width of the image: the chroma samples beyond it are not read.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="destination">The upsampled row: the width of the image.</param>
    public void UpsampleChromaRow(HeifPlanarImage image, int plane, int y, int width, int height, Span<ushort> destination)
    {
        var chroma = image.GetPlane(plane);
        var chromaWidth = (width + _subX) >> _subX;
        var chromaHeight = (height + _subY) >> _subY;
        if (_subX is 0)
        {
            chroma.GetRow(y)[..width].CopyTo(destination);
            return;
        }

        if (_subY is 0)
        {
            var row = chroma.GetRow(y);
            if (_chromaSamplePosition is not Av1Constants.CspUnknown)
            {
                // 4:2:2 with chroma samples at the position of the luma samples of even columns (HEVC): a luma sample
                // between two of them takes their mean
                for (var x = 0; x < width; x++)
                {
                    var i = x >> 1;
                    destination[x] = (x & 1) is 0 ? row[i] : (ushort)((row[i] + row[Math.Min(i + 1, chromaWidth - 1)] + 1) >> 1);
                }

                return;
            }

            // 4:2:2: the chroma samples are centered between two luma samples
            for (var x = 0; x < width; x++)
            {
                var i = x >> 1;
                var other = (x & 1) is 0 ? Math.Max(i - 1, 0) : Math.Min(i + 1, chromaWidth - 1);
                destination[x] = (ushort)(((3 * row[i]) + row[other] + 2) >> 2);
            }

            return;
        }

        var nearIndex = y >> 1;
        var near = chroma.GetRow(nearIndex);
        if (_chromaSamplePosition is Av1Constants.CspColocated)
        {
            var below = chroma.GetRow(Math.Min(nearIndex + 1, chromaHeight - 1));
            for (var x = 0; x < width; x++)
            {
                var i = x >> 1;
                var next = Math.Min(i + 1, chromaWidth - 1);
                if ((y & 1) is 0)
                {
                    destination[x] = (x & 1) is 0 ? near[i] : (ushort)((near[i] + near[next] + 1) >> 1);
                }
                else
                {
                    destination[x] = (x & 1) is 0 ? (ushort)((near[i] + below[i] + 1) >> 1) : (ushort)((near[i] + near[next] + below[i] + below[next] + 2) >> 2);
                }
            }

            return;
        }

        var far = chroma.GetRow(Math.Clamp((y & 1) is 0 ? nearIndex - 1 : nearIndex + 1, 0, chromaHeight - 1));
        var vertical = _chromaSamplePosition is Av1Constants.CspVertical;
        for (var x = 0; x < width; x++)
        {
            var i = x >> 1;
            var current = (3 * near[i]) + far[i];
            if (vertical)
            {
                if ((x & 1) is 0)
                {
                    destination[x] = (ushort)((current + 2) >> 2);
                }
                else
                {
                    var next = Math.Min(i + 1, chromaWidth - 1);
                    destination[x] = (ushort)((current + (3 * near[next]) + far[next] + 4) >> 3);
                }
            }
            else
            {
                var other = (x & 1) is 0 ? Math.Max(i - 1, 0) : Math.Min(i + 1, chromaWidth - 1);
                destination[x] = (ushort)(((3 * current) + (3 * near[other]) + far[other] + 8) >> 4);
            }
        }
    }

    /// <summary>Converts the samples of a row to pixels of the layout.</summary>
    /// <param name="y">The luma samples.</param>
    /// <param name="u">The U samples at the resolution of the luma row; ignored for a monochrome image.</param>
    /// <param name="v">The V samples at the resolution of the luma row.</param>
    /// <param name="alpha">The alpha samples, or an empty span for an opaque image.</param>
    /// <param name="destination">The pixels: one per luma sample.</param>
    public void ConvertRow(ReadOnlySpan<ushort> y, ReadOnlySpan<ushort> u, ReadOnlySpan<ushort> v, ReadOnlySpan<ushort> alpha, Span<byte> destination)
    {
        var deep = _outputMax is 65535;
        var wide = deep ? unsafe(MemoryMarshal.Cast<byte, ushort>(destination)) : default;
        for (var x = 0; x < y.Length; x++)
        {
            int r;
            int g;
            int b;
            if (_monochrome)
            {
                r = g = b = ConvertUnmatrixed(y[x]);
            }
            else if (_matrix is Av1Constants.McIdentity)
            {
                g = ConvertUnmatrixed(y[x]);
                b = ConvertUnmatrixed(u[x]);
                r = ConvertUnmatrixed(v[x]);
            }
            else if (_usesTransfer)
            {
                ConvertThroughTransfer((y[x] - _yOffset) * _yScale, (u[x] - _cOffset) * _cScale, (v[x] - _cOffset) * _cScale, out r, out g, out b);
            }
            else
            {
                var luma = (_cY * (y[x] - _yOffset)) + Half;
                long cb = u[x] - _cOffset;
                long cr = v[x] - _cOffset;
                if (_matrix is Av1Constants.McSmpte2085)
                {
                    // The green is the luma; the blue and the red have a luma coefficient of their own
                    long centered = y[x] - _yOffset;
                    g = Clip(luma >> 16);
                    b = Clip(((_cYB * centered) + (_cBU * cb) + Half) >> 16);
                    r = Clip(((_cYR * centered) + (_cRV * cr) + Half) >> 16);
                }
                else if (_matrix is Av1Constants.McSmpteYcgco)
                {
                    // U is Cg and V is Co
                    g = Clip((luma + (_cC * cb)) >> 16);
                    b = Clip((luma - (_cC * cb) - (_cC * cr)) >> 16);
                    r = Clip((luma - (_cC * cb) + (_cC * cr)) >> 16);
                }
                else
                {
                    r = Clip((luma + (_cRV * cr)) >> 16);
                    g = Clip((luma - (_cGU * cb) - (_cGV * cr)) >> 16);
                    b = Clip((luma + (_cBU * cb)) >> 16);
                }
            }

            var a = _outputMax;
            if (!alpha.IsEmpty)
                a = _alphaFullRange ? (int)((((long)alpha[x] * _outputMax) + (_alphaMax / 2)) / _alphaMax) : Clip(((_cAlpha * (alpha[x] - _alphaOffset)) + Half) >> 16);

            if (_isPremultiplied && a != _outputMax)
            {
                r = Unpremultiply(r, a);
                g = Unpremultiply(g, a);
                b = Unpremultiply(b, a);
            }

            switch (_layout)
            {
                case PixelFormat.Gray8:
                    destination[x] = (byte)g;
                    break;
                case PixelFormat.Gray16:
                    wide[x] = (ushort)g;
                    break;
                case PixelFormat.Rgb24:
                    destination[3 * x] = (byte)r;
                    destination[(3 * x) + 1] = (byte)g;
                    destination[(3 * x) + 2] = (byte)b;
                    break;
                case PixelFormat.Rgba32:
                    destination[4 * x] = (byte)r;
                    destination[(4 * x) + 1] = (byte)g;
                    destination[(4 * x) + 2] = (byte)b;
                    destination[(4 * x) + 3] = (byte)a;
                    break;
                default:
                    wide[4 * x] = (ushort)r;
                    wide[(4 * x) + 1] = (ushort)g;
                    wide[(4 * x) + 2] = (ushort)b;
                    wide[(4 * x) + 3] = (ushort)a;
                    break;
            }
        }
    }

    // The constant luminance and ICtCp conversions (ITU-T H.273, section 8.3), from the normalized E'Y, E'PB and E'PR
    private void ConvertThroughTransfer(double ey, double epb, double epr, out int r, out int g, out int b)
    {
        double er;
        double eg;
        double eb;
        if (_ictcpToLms is { } ictcpToLms)
        {
            // L', M' and S' from I, CT and CP, then the linear L, M and S, then the linear R, G and B
            var lmsToRgb = _lmsToRgb!;
            var l = CicpTransfer.Decode(_transfer, (ictcpToLms[0] * ey) + (ictcpToLms[1] * epb) + (ictcpToLms[2] * epr));
            var m = CicpTransfer.Decode(_transfer, (ictcpToLms[3] * ey) + (ictcpToLms[4] * epb) + (ictcpToLms[5] * epr));
            var s = CicpTransfer.Decode(_transfer, (ictcpToLms[6] * ey) + (ictcpToLms[7] * epb) + (ictcpToLms[8] * epr));
            er = CicpTransfer.Encode(_transfer, (lmsToRgb[0] * l) + (lmsToRgb[1] * m) + (lmsToRgb[2] * s));
            eg = CicpTransfer.Encode(_transfer, (lmsToRgb[3] * l) + (lmsToRgb[4] * m) + (lmsToRgb[5] * s));
            eb = CicpTransfer.Encode(_transfer, (lmsToRgb[6] * l) + (lmsToRgb[7] * m) + (lmsToRgb[8] * s));
        }
        else
        {
            // E'B and E'R from the chroma, whose extent depends on its sign; the green from the luminance, which is linear
            eb = ey + (2 * (epb < 0 ? _nb : _pb) * epb);
            er = ey + (2 * (epr < 0 ? _nr : _pr) * epr);
            var luminance = CicpTransfer.Decode(_transfer, ey);
            var green = (luminance - (_kr * CicpTransfer.Decode(_transfer, er)) - (_kb * CicpTransfer.Decode(_transfer, eb))) / _kg;
            eg = CicpTransfer.Encode(_transfer, green);
        }

        r = ToSample(er);
        g = ToSample(eg);
        b = ToSample(eb);
    }

    // The nearest sample of the output to a normalized value, which is clipped
    private int ToSample(double value) => !(value > 0) ? 0 : value >= 1 ? _outputMax : (int)Math.Floor((value * _outputMax) + 0.5);

    private static double[] Invert(ReadOnlySpan<double> matrixOver4096)
    {
        Span<double> matrix = stackalloc double[9];
        for (var i = 0; i < 9; i++)
        {
            matrix[i] = matrixOver4096[i] / 4096;
        }

        var inverse = new double[9];
        if (!ChromaticityMath.TryInvert(matrix, inverse))
            throw new InvalidOperationException("The matrix has no inverse.");

        return inverse;
    }

    // A sample that no matrix applies to: the range is scaled to the range of the output
    private int ConvertUnmatrixed(int sample)
    {
        if (_fullRange)
            return (int)((((long)sample * _outputMax) + (_max / 2)) / _max);

        return Clip(((_cY * (sample - _yOffset)) + Half) >> 16);
    }

    // The color of a pixel from its color multiplied by its alpha: nothing is left of the color of a transparent pixel
    private int Unpremultiply(int color, int alpha)
        => alpha is 0 ? 0 : (int)Math.Min(_outputMax, (((long)color * _outputMax) + (alpha / 2)) / alpha);

    private int Clip(long value) => (int)Math.Clamp(value, 0, _outputMax);
}
