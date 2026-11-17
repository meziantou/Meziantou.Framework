using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts the Cb, Y, Cr (and alpha) components of a line of a DPX image element to R'G'B' pixels: Rgb24 or Rgba32 for
/// 8-bit components, Rgba64 for deeper ones.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The matrix is the one of BT.709 when the colorimetric specification is SMPTE ST 274 or BT.709, and the one of BT.601
/// otherwise (SMPTE ST 125, on which the 4:2:2 descriptor is based, is a BT.601 signal).
/// </description></item>
/// <item><description>
/// The range is full when the reference low and high code values are 0 and <c>2^n - 1</c>; otherwise it is the nominal
/// range of BT.601 and BT.709 at the depth of the components (Y from 16 to 235 and Cb, Cr from 16 to 240 at 8 bits, times
/// <c>2^(n - 8)</c>).
/// </description></item>
/// <item><description>
/// The chroma components of a 4:2:2 element are at the position of the luma component of even columns (SMPTE ST 125): an
/// odd column takes the mean of its two neighbors, rounded up, and the last one of a line repeats its left neighbor.
/// </description></item>
/// <item><description>
/// Each color is computed in double precision from the exact matrix, clamped, and rounded once to the nearest sample of the
/// output. Alpha is scaled like the components of an RGB element.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class DpxYCbCrConverter
{
    private readonly DpxDescriptor _descriptor;
    private readonly int _bitDepth;
    private readonly double _kr;
    private readonly double _kb;
    private readonly double _yOffset;
    private readonly double _yRange;
    private readonly double _cOffset;
    private readonly double _cRange;
    private readonly bool _wide;

    public DpxYCbCrConverter(DpxDescriptor descriptor, int bitDepth, DpxColorimetricSpecification colorimetric, uint? referenceLow, uint? referenceHigh)
    {
        _descriptor = descriptor;
        _bitDepth = bitDepth;
        _wide = bitDepth > 8;
        (_kr, _kb) = colorimetric is DpxColorimetricSpecification.Smpte274 or DpxColorimetricSpecification.Bt709 ? (0.2126, 0.0722) : (0.299, 0.114);
        var max = (1L << bitDepth) - 1;
        IsFullRange = referenceLow == 0 && referenceHigh == (ulong)max;
        if (IsFullRange)
        {
            _yOffset = 0;
            _yRange = max;
            _cOffset = 1L << (bitDepth - 1);
            _cRange = max;
        }
        else
        {
            var scale = Math.Pow(2, bitDepth - 8);
            _yOffset = 16 * scale;
            _yRange = 219 * scale;
            _cOffset = 128 * scale;
            _cRange = 224 * scale;
        }
    }

    /// <summary>Gets whether the components are in full range rather than in the nominal video range.</summary>
    public bool IsFullRange { get; }

    /// <summary>Writes the pixels of a line.</summary>
    public void WriteRow(ReadOnlySpan<uint> components, int width, Span<byte> destination)
    {
        var hasAlpha = DpxFormat.HasAlpha(_descriptor);
        var channels = _wide || hasAlpha ? 4 : 3;
        Span<ushort> wide = _wide ? unsafe(MemoryMarshal.Cast<byte, ushort>(destination)) : default;
        var outputMax = _wide ? 65535.0 : 255.0;
        var alphaMax = (uint)((1L << _bitDepth) - 1);
        for (var x = 0; x < width; x++)
        {
            GetComponents(components, x, width, out var y, out var cb, out var cr, out var alpha);
            var luma = (y - _yOffset) / _yRange;
            var blueDifference = (cb - _cOffset) / _cRange;
            var redDifference = (cr - _cOffset) / _cRange;
            var red = luma + (2 * (1 - _kr) * redDifference);
            var blue = luma + (2 * (1 - _kb) * blueDifference);
            var green = (luma - (_kr * red) - (_kb * blue)) / (1 - _kr - _kb);
            var r = Round(red, outputMax);
            var g = Round(green, outputMax);
            var b = Round(blue, outputMax);
            if (_wide)
            {
                wide[x * 4] = (ushort)r;
                wide[(x * 4) + 1] = (ushort)g;
                wide[(x * 4) + 2] = (ushort)b;
                wide[(x * 4) + 3] = hasAlpha ? DpxRowReader.Scale(alpha, alphaMax) : ushort.MaxValue;
            }
            else
            {
                destination[x * channels] = (byte)r;
                destination[(x * channels) + 1] = (byte)g;
                destination[(x * channels) + 2] = (byte)b;
                if (hasAlpha)
                {
                    destination[(x * channels) + 3] = (byte)alpha;
                }
            }
        }
    }

    private static int Round(double value, double max) => (int)Math.Round(Math.Clamp(value, 0, 1) * max, MidpointRounding.AwayFromZero);

    private void GetComponents(ReadOnlySpan<uint> components, int x, int width, out uint y, out double cb, out double cr, out uint alpha)
    {
        switch (_descriptor)
        {
            case DpxDescriptor.CbYCr:
                cb = components[x * 3];
                y = components[(x * 3) + 1];
                cr = components[(x * 3) + 2];
                alpha = 0;
                return;

            case DpxDescriptor.CbYCrA:
                cb = components[x * 4];
                y = components[(x * 4) + 1];
                cr = components[(x * 4) + 2];
                alpha = components[(x * 4) + 3];
                return;

            case DpxDescriptor.CbYCrY:
                // Cb Y0 Cr Y1: the chroma of a pair is at its first column
                y = components[((x >> 1) * 4) + ((x & 1) == 0 ? 1 : 3)];
                GetSubsampledChroma(components, x, width, stride: 4, cbIndex: 0, crIndex: 2, out cb, out cr);
                alpha = 0;
                return;

            case DpxDescriptor.CbYACrYA:
                // Cb Y0 A0 Cr Y1 A1
                y = components[((x >> 1) * 6) + ((x & 1) == 0 ? 1 : 4)];
                alpha = components[((x >> 1) * 6) + ((x & 1) == 0 ? 2 : 5)];
                GetSubsampledChroma(components, x, width, stride: 6, cbIndex: 0, crIndex: 3, out cb, out cr);
                return;

            default:
                throw new InvalidOperationException("The DPX descriptor is not a Cb, Y, Cr descriptor.");
        }
    }

    private static void GetSubsampledChroma(ReadOnlySpan<uint> components, int x, int width, int stride, int cbIndex, int crIndex, out double cb, out double cr)
    {
        var pair = x >> 1;
        var start = pair * stride;
        if ((x & 1) == 0 || x + 1 >= width)
        {
            cb = components[start + cbIndex];
            cr = components[start + crIndex];
            return;
        }

        var next = start + stride;
        cb = (components[start + cbIndex] + components[next + cbIndex] + 1) / 2;
        cr = (components[start + crIndex] + components[next + crIndex] + 1) / 2;
    }
}
