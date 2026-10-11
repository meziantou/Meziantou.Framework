using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts rows of pixels to the planes that the AVIF encoder codes: the matrix coefficients and the sample range of
/// ITU-T H.273, the scaling to the bit depth of the planes, and the subsampling of the chroma. The counterpart of
/// <see cref="AvifYuvConverter"/>.
/// </summary>
/// <remarks>
/// <para>The conversion is integer only, and every sample is rounded once:</para>
/// <list type="bullet">
/// <item><description>
/// A sample that no matrix applies to (gray, the G, B and R of the identity matrix, alpha) is scaled by
/// <c>(v * max + inputMax / 2) / inputMax</c>. It is the exact inverse of the scaling of the decoder, so that the samples of
/// a 10 or 12-bit image that was decoded to 16 bits are coded back unchanged.
/// </description></item>
/// <item><description>
/// A matrixed sample is the value of the equations of ITU-T H.273 (section 8.3) for the exact Kr and Kb of the matrix
/// coefficients, rounded to nearest.
/// </description></item>
/// <item><description>
/// A subsampled chroma sample is the mean of the chroma of the 2 or 4 pixels it covers (the samples are centered, which is
/// <c>chroma_sample_position</c> 0), computed before the rounding. The last column and the last row are repeated when
/// the size of the image is odd.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class AvifRgbConverter
{
    private const long D = 10000;

    private readonly PixelFormat _layout;
    private readonly bool _monochrome;
    private readonly int _matrix;
    private readonly int _subX;
    private readonly int _subY;
    private readonly long _inputMax;
    private readonly long _max;
    private readonly long _yOffset;
    private readonly long _yRange;
    private readonly long _cOffset;
    private readonly long _cRange;
    private readonly long _kr;
    private readonly long _kb;
    private readonly bool _clearTransparentColors;

    /// <summary>Creates the converter of an image.</summary>
    /// <param name="layout">The layout of the rows to convert: <see cref="PixelFormat.Gray8"/>, <see cref="PixelFormat.Gray16"/>, <see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Rgba64"/>.</param>
    /// <param name="bitDepth">The bit depth of the planes.</param>
    /// <param name="subsamplingX">The horizontal subsampling of the chroma planes.</param>
    /// <param name="subsamplingY">The vertical subsampling of the chroma planes.</param>
    /// <param name="matrixCoefficients">The matrix coefficients code point; not applied to a gray layout.</param>
    /// <param name="fullRange">Whether the color planes use the full range. Alpha always does.</param>
    /// <param name="clearTransparentColors">Whether the color of the fully transparent pixels is replaced with black.</param>
    public AvifRgbConverter(PixelFormat layout, int bitDepth, int subsamplingX, int subsamplingY, int matrixCoefficients, bool fullRange, bool clearTransparentColors)
    {
        if (layout is not (PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.Rgba32 or PixelFormat.Rgba64))
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "The layout is not a layout of the converter.");

        _layout = layout;
        _monochrome = layout is PixelFormat.Gray8 or PixelFormat.Gray16;
        _matrix = matrixCoefficients;
        _subX = _monochrome ? 0 : subsamplingX;
        _subY = _monochrome ? 0 : subsamplingY;
        _inputMax = layout is PixelFormat.Gray16 or PixelFormat.Rgba64 ? 65535 : 255;
        _max = (1L << bitDepth) - 1;
        _cOffset = 1L << (bitDepth - 1);
        _clearTransparentColors = clearTransparentColors;
        if (fullRange)
        {
            _yRange = _max;
            _cRange = _max;
        }
        else
        {
            _yOffset = 16L << (bitDepth - 8);
            _yRange = 219L << (bitDepth - 8);
            _cRange = 224L << (bitDepth - 8);
        }

        if (!_monochrome)
        {
            // Kr and Kb of the matrix coefficients, over 10000 (ITU-T H.273, table 4)
            (_kr, _kb) = matrixCoefficients switch
            {
                Av1Constants.McBt709 => (2126, 722),
                Av1Constants.McFcc => (3000, 1100),
                Av1Constants.McBt470BG or Av1Constants.McBt601 or Av1Constants.McUnspecified => (2990, 1140),
                Av1Constants.McSmpte240 => (2120, 870),
                Av1Constants.McBt2020Ncl => (2627, 593),
                Av1Constants.McIdentity or Av1Constants.McSmpteYcgco => (0L, 0L),
                _ => throw new ArgumentOutOfRangeException(nameof(matrixCoefficients), matrixCoefficients, "The matrix coefficients are not supported."),
            };

            if (matrixCoefficients is Av1Constants.McIdentity && (_subX is not 0 || _subY is not 0 || !fullRange))
                throw new ArgumentException("The identity matrix requires 4:4:4 samples in full range.", nameof(matrixCoefficients));
        }
    }

    /// <summary>Gets the number of rows of pixels that <see cref="ConvertRows"/> converts: 2 when the chroma planes have half the rows.</summary>
    public int RowsPerCall => 1 + _subY;

    /// <summary>
    /// Converts <see cref="RowsPerCall"/> rows of pixels to a row of each plane (two rows of the luma and alpha planes when
    /// the chroma planes have half the rows).
    /// </summary>
    /// <param name="row0">The first row of pixels.</param>
    /// <param name="row1">The second row, when <see cref="RowsPerCall"/> is 2: <paramref name="row0"/> again for the last row of an image of odd height.</param>
    /// <param name="y0">The luma samples of the first row.</param>
    /// <param name="y1">The luma samples of the second row; not written when <paramref name="hasSecondRow"/> is false.</param>
    /// <param name="u">The U samples; ignored for a gray layout.</param>
    /// <param name="v">The V samples.</param>
    /// <param name="a0">The alpha samples of the first row, or an empty span.</param>
    /// <param name="a1">The alpha samples of the second row.</param>
    /// <param name="hasSecondRow">Whether <paramref name="row1"/> is a row of the image.</param>
    /// <returns>Whether a pixel of the rows is not opaque.</returns>
    public bool ConvertRows(ReadOnlySpan<byte> row0, ReadOnlySpan<byte> row1, Span<ushort> y0, Span<ushort> y1, Span<ushort> u, Span<ushort> v, Span<ushort> a0, Span<ushort> a1, bool hasSecondRow)
    {
        var width = y0.Length;
        var transparent = false;
        if (_monochrome)
        {
            for (var x = 0; x < width; x++)
            {
                long gray = _layout is PixelFormat.Gray8 ? row0[x] : unsafe(MemoryMarshal.Cast<byte, ushort>(row0))[x];
                y0[x] = (ushort)(_yOffset + RoundedDivide(gray * _yRange, _inputMax));
            }

            return false;
        }

        var chromaWidth = (width + _subX) >> _subX;
        for (var cx = 0; cx < chromaWidth; cx++)
        {
            long uSum = 0;
            long vSum = 0;
            for (var dy = 0; dy <= _subY; dy++)
            {
                var row = dy is 0 ? row0 : row1;
                var luma = dy is 0 ? y0 : y1;
                var alpha = dy is 0 ? a0 : a1;
                var write = dy is 0 || hasSecondRow;
                for (var dx = 0; dx <= _subX; dx++)
                {
                    // The last column is repeated when the width is odd
                    var x = Math.Min((cx << _subX) + dx, width - 1);
                    ReadPixel(row, x, out var r, out var g, out var b, out var a);
                    if (a != _inputMax)
                    {
                        transparent = true;
                        if (a is 0 && _clearTransparentColors)
                            r = g = b = 0;
                    }

                    Convert(r, g, b, out var luminance, out var uNumerator, out var vNumerator);
                    uSum += uNumerator;
                    vSum += vNumerator;
                    if (write && (dx is 0 || ((cx << _subX) + dx) < width))
                    {
                        luma[x] = (ushort)luminance;
                        if (!alpha.IsEmpty)
                            alpha[x] = (ushort)RoundedDivide(a * _max, _inputMax);
                    }
                }
            }

            var count = (1 + _subX) * (1 + _subY);
            u[cx] = FinishChroma(uSum, count, isU: true);
            v[cx] = FinishChroma(vSum, count, isU: false);
        }

        return transparent;
    }

    private void ReadPixel(ReadOnlySpan<byte> row, int x, out long r, out long g, out long b, out long a)
    {
        if (_layout is PixelFormat.Rgba32)
        {
            var pixel = row.Slice(4 * x, 4);
            (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
        }
        else
        {
            var pixel = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(4 * x, 4);
            (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
        }
    }

    // The luma sample of a pixel, and the numerators of its chroma samples: FinishChroma divides their sum
    private void Convert(long r, long g, long b, out long luma, out long uNumerator, out long vNumerator)
    {
        switch (_matrix)
        {
            case Av1Constants.McIdentity:
                // Y is G, U is B and V is R
                luma = RoundedDivide(g * _max, _inputMax);
                uNumerator = b;
                vNumerator = r;
                break;
            case Av1Constants.McSmpteYcgco:
                // Y = (R + 2G + B) / 4, Cg = (2G - R - B) / 4 in U and Co = (R - B) / 2 in V
                luma = _yOffset + RoundedDivide((r + (2 * g) + b) * _yRange, 4 * _inputMax);
                uNumerator = (2 * g) - r - b;
                vNumerator = 2 * (r - b);
                break;
            default:
                // E'Y = Kr R + Kg G + Kb B, E'PB = (B - E'Y) / (2 (1 - Kb)) and E'PR = (R - E'Y) / (2 (1 - Kr))
                var weighted = (_kr * r) + ((D - _kr - _kb) * g) + (_kb * b);
                luma = _yOffset + RoundedDivide(weighted * _yRange, D * _inputMax);
                uNumerator = (b * D) - weighted;
                vNumerator = (r * D) - weighted;
                break;
        }

        luma = Math.Clamp(luma, 0, _max);
    }

    private ushort FinishChroma(long sum, int count, bool isU)
    {
        long value;
        switch (_matrix)
        {
            case Av1Constants.McIdentity:
                value = RoundedDivide(sum * _max, count * _inputMax);
                break;
            case Av1Constants.McSmpteYcgco:
                value = _cOffset + RoundedDivide(sum * _cRange, 4L * count * _inputMax);
                break;
            default:
                value = _cOffset + RoundedDivide(sum * _cRange, 2 * (D - (isU ? _kb : _kr)) * count * _inputMax);
                break;
        }

        return (ushort)Math.Clamp(value, 0, _max);
    }

    // The quotient rounded to nearest, halves up: the denominator is positive
    private static long RoundedDivide(long numerator, long denominator)
    {
        var doubled = (2 * numerator) + denominator;
        var quotient = doubled / (2 * denominator);
        return doubled < 0 && doubled % (2 * denominator) is not 0 ? quotient - 1 : quotient;
    }
}
