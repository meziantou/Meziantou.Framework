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
/// rounded once.
/// </description></item>
/// <item><description>
/// Samples that are not matrixed (monochrome, identity matrix and alpha in full range) are scaled by
/// <c>(v * outputMax + max / 2) / max</c>, which is exactly reversible.
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
    private readonly int _alphaMax;
    private readonly bool _alphaFullRange;
    private readonly int _alphaOffset;
    private readonly long _cAlpha;

    /// <summary>Creates the converter of an image.</summary>
    /// <param name="binding">The format of the file, which errors report.</param>
    /// <param name="color">The sample format of the color image.</param>
    /// <param name="matrixCoefficients">The matrix coefficients code point that applies to the image.</param>
    /// <param name="fullRange">Whether the samples of the color image use the full range.</param>
    /// <param name="alpha">The sample format of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="layout">The pixel layout of the rows to produce.</param>
    /// <exception cref="UnsupportedImageFeatureException">The matrix coefficients are not supported.</exception>
    /// <exception cref="InvalidImageContentException">The identity matrix is used with subsampled chroma.</exception>
    public HeifYuvConverter(HeifCodecBinding binding, in HeifBitstreamFormat color, int matrixCoefficients, bool fullRange, HeifBitstreamFormat? alpha, PixelFormat layout)
    {
        _layout = layout;
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

        EnsureSupported(binding, matrixCoefficients, _monochrome, _subX, _subY);
        _cY = RoundedDivide(_outputMax * One, yRange);
        _cC = RoundedDivide(_outputMax * One, cRange);
        if (!_monochrome)
        {
            // Kr and Kb of the matrix coefficients, over 10000 (ITU-T H.273, table 4)
            const long D = 10000;
            (long Kr, long Kb)? weights = matrixCoefficients switch
            {
                Av1Constants.McBt709 => (2126, 722),
                Av1Constants.McFcc => (3000, 1100),
                Av1Constants.McBt470BG or Av1Constants.McBt601 or Av1Constants.McUnspecified => (2990, 1140),
                Av1Constants.McSmpte240 => (2120, 870),
                Av1Constants.McBt2020Ncl => (2627, 593),
                Av1Constants.McIdentity or Av1Constants.McSmpteYcgco => null,
                _ => throw binding.Unsupported($"The matrix coefficients {matrixCoefficients} of the {binding.Name} image are not supported.", "matrix coefficients"),
            };


            if (weights is var (kr, kb))
            {
                _cRV = RoundedDivide((Int128)_outputMax * One * 2 * (D - kr), D * cRange);
                _cBU = RoundedDivide((Int128)_outputMax * One * 2 * (D - kb), D * cRange);
                _cGU = RoundedDivide((Int128)_outputMax * One * 2 * kb * (D - kb), (Int128)D * (D - kr - kb) * cRange);
                _cGV = RoundedDivide((Int128)_outputMax * One * 2 * kr * (D - kr), (Int128)D * (D - kr - kb) * cRange);
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
    /// <exception cref="UnsupportedImageFeatureException">The matrix coefficients are not supported.</exception>
    /// <exception cref="InvalidImageContentException">The identity matrix is used with subsampled chroma.</exception>
    public static void EnsureSupported(HeifCodecBinding binding, int matrixCoefficients, bool isMonochrome, int subsamplingX, int subsamplingY)
    {
        if (isMonochrome)
            return;

        if (matrixCoefficients is not (Av1Constants.McIdentity or Av1Constants.McBt709 or Av1Constants.McUnspecified or Av1Constants.McFcc or Av1Constants.McBt470BG
            or Av1Constants.McBt601 or Av1Constants.McSmpte240 or Av1Constants.McSmpteYcgco or Av1Constants.McBt2020Ncl))
        {
            throw binding.Unsupported($"The matrix coefficients {matrixCoefficients} of the {binding.Name} image are not supported.", "matrix coefficients");
        }

        if (matrixCoefficients is Av1Constants.McIdentity && (subsamplingX is not 0 || subsamplingY is not 0))
            throw binding.Invalid("the identity matrix requires 4:4:4 samples.");
    }

    private static long RoundedDivide(Int128 numerator, Int128 denominator) => (long)(((2 * numerator) + denominator) / (2 * denominator));

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
            else
            {
                var luma = (_cY * (y[x] - _yOffset)) + Half;
                long cb = u[x] - _cOffset;
                long cr = v[x] - _cOffset;
                if (_matrix is Av1Constants.McSmpteYcgco)
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

    // A sample that no matrix applies to: the range is scaled to the range of the output
    private int ConvertUnmatrixed(int sample)
    {
        if (_fullRange)
            return (int)((((long)sample * _outputMax) + (_max / 2)) / _max);

        return Clip(((_cY * (sample - _yOffset)) + Half) >> 16);
    }

    private int Clip(long value) => (int)Math.Clamp(value, 0, _outputMax);
}
