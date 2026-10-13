using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors a <see cref="GradientBrush"/> paints on the pixels of a row, in the pixel format of the frame.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The color of a pixel is the color of the gradient at its center <c>(x + 1/2, y + 1/2)</c>. The
/// parameter is <c>((p - start) . (end - start)) / |end - start|^2</c> for a linear gradient and
/// <c>|p - center| / radius</c> for a radial one, computed for each pixel on its own (no running sum), then brought into
/// <c>[0, 1]</c>: clamped (pad), <c>t - floor(t)</c> (repeat), or <c>u = t - 2 floor(t / 2)</c> folded as
/// <c>u &lt;= 1 ? u : 2 - u</c> (reflect).
/// </para>
/// <para>
/// A radial gradient with a focal point <c>f</c> has, with <c>e = center - f</c>, <c>d = p - f</c> and
/// <c>a = radius^2 - |e|^2</c>, the parameter <c>(sqrt((d . e)^2 + a |d|^2) - (d . e)) / a</c>: the positive <c>t</c> for
/// which <c>p</c> is on the circle of center <c>f + t e</c> and radius <c>t radius</c>. A sweep gradient has the
/// parameter <c>(angle(p - center) - start) mod 360 / (end - start)</c>, the angle being the one of
/// <see cref="DeterministicAngle.Atan2"/>. When the brush has a transformation, <c>p</c> is the center of the pixel
/// brought back through the inverse of that transformation; a transformation without inverse makes a degenerate gradient.
/// </para>
/// <para>
/// With <c>i</c> the last stop whose offset is not greater than the parameter: before the first stop the color is the
/// first stop, at or after the last stop it is the last stop, otherwise stops <c>i</c> and <c>i + 1</c> are interpolated
/// at <c>u = (t - o[i]) / (o[i + 1] - o[i])</c> on premultiplied values:
/// <c>a = a0 + u (a1 - a0)</c> and <c>c = (c0 a0 + u (c1 a1 - c0 a0)) / a</c>, each rounded as
/// <c>floor(v + 1/2 + 2^-20)</c>. The stops are converted to the pixel format first, so equal stops give exactly the
/// color a solid brush has. A degenerate gradient (no length, no radius) has the color of the last stop everywhere.
/// </para>
/// <para>
/// <b>Other color spaces.</b> With <see cref="GradientColorSpace.LinearSrgb"/> or <see cref="GradientColorSpace.Oklab"/>,
/// the stops are 16-bit sRGB colors, not converted to the pixel format: each is converted to the space
/// (<see cref="SrgbTransfer.Decode(double)"/>, then for Oklab the two matrices and the cube root of its definition),
/// and two stops are interpolated there with the same formulas, on values from 0 to 1. The result is converted back,
/// brought into <c>[0, 1]</c>, and rounded to a 16-bit sRGB color as <c>floor(65535 v + 1/2 + 2^-20)</c>, which is
/// then converted to the pixel format like any brush color. Where the color is the color of a stop (before the first
/// one, from the last one, or between two stops of the same color), it is that 16-bit color itself.
/// </para>
/// </remarks>
internal sealed class GradientEvaluator : PaintEvaluator, IDisposable
{
    /// <summary>
    /// The values kept for each stop: offset, alpha, three color samples in the pixel format, then the alpha and the
    /// three components in the color space of the gradient, from 0 to 1.
    /// </summary>
    private const int ValuesPerStop = 9;

    /// <summary>The tie bias of the rounding rule shared with the resampler and the convolver.</summary>
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    private readonly AllocationScope _scope;
    private PooledBuffer? _stops;
    private int _stopCapacity;
    private PooledBuffer? _colors;
    private PooledBuffer? _samples;
    private GradientBrush? _brush;
    private ImageFrame? _frame;
    private GradientColorSpace _colorSpace;
    private int _stopCount;
    private bool _isTransparent;
    private GradientKind _kind;
    private bool _degenerate;
    private bool _hasTransform;
    private Affine2D _inverse;
    private GradientSpreadMode _spreadMode;
    private double _originX;
    private double _originY;
    private double _axisX;
    private double _axisY;
    private double _divisor;
    private double _max;

    public GradientEvaluator(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets a value indicating whether every stop is fully transparent: the gradient paints nothing.</summary>
    public override bool IsTransparent => _isTransparent;

    private Span<double> Stops => unsafe(MemoryMarshal.Cast<byte, double>(_stops!.Span));

    /// <summary>Converts the stops of a brush to the pixel format of a frame and rents the row of colors.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public void Prepare(GradientBrush brush, ImageFrame frame, int width)
        => Prepare(brush, frame, width, (1 << PixelFormats.GetBitsPerComponent(frame.PixelFormat)) - 1);

    /// <summary>
    /// Prepares the evaluation of a gradient whose colors are not converted to the samples of a frame: 16-bit red,
    /// green, blue and alpha values, for what composes colors before it writes them to a frame.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public void PrepareColors(GradientBrush brush, int width) => Prepare(brush, frame: null, width, ushort.MaxValue);

    private void Prepare(GradientBrush brush, ImageFrame? frame, int width, int max)
    {
        var stops = brush.StopSpan;
        if (stops.Length > _stopCapacity)
        {
            _stops?.Dispose();
            _stops = null;
            _stopCapacity = 0;
            _stops = ResampleWeights.Rent<double>(_scope, (long)stops.Length * ValuesPerStop);
            _stopCapacity = stops.Length;
        }

        _colors ??= ResampleWeights.Rent<ushort>(_scope, 4L * width);
        _colorSpace = brush.ColorSpace;
        _brush = brush;
        _frame = frame;
        if (_colorSpace != GradientColorSpace.Encoded)
        {
            // The colors are computed as 16-bit colors, then converted as those of any brush
            _samples ??= ResampleWeights.Rent<Rgba64>(_scope, width);
        }

        _stopCount = stops.Length;
        _spreadMode = brush.SpreadMode;
        _max = max;
        var values = Stops;
        var transparent = true;
        for (var i = 0; i < stops.Length; i++)
        {
            var color = frame is null ? new DrawingColor(stops[i].Color.R, stops[i].Color.G, stops[i].Color.B, stops[i].Color.A) : frame.ConvertDrawingColor(stops[i].Color);
            var entry = values.Slice(i * ValuesPerStop, ValuesPerStop);
            entry[0] = stops[i].Offset;
            entry[1] = color.Alpha;
            entry[2] = color.C0;
            entry[3] = color.C1;
            entry[4] = color.C2;
            transparent &= color.Alpha == 0;
            if (_colorSpace != GradientColorSpace.Encoded)
            {
                ToColorSpace(stops[i].Color, entry[5..]);
            }
        }

        _isTransparent = transparent;
        switch (brush)
        {
            case LinearGradientBrush linear:
                _kind = GradientKind.Linear;
                _originX = linear.Start.X;
                _originY = linear.Start.Y;
                _axisX = linear.End.X - linear.Start.X;
                _axisY = linear.End.Y - linear.Start.Y;
                _divisor = (_axisX * _axisX) + (_axisY * _axisY);
                break;

            case RadialGradientBrush radial when radial.FocalPoint != radial.Center:
                _kind = GradientKind.Focal;
                _originX = radial.FocalPoint.X;
                _originY = radial.FocalPoint.Y;
                _axisX = radial.Center.X - radial.FocalPoint.X;
                _axisY = radial.Center.Y - radial.FocalPoint.Y;
                _divisor = (radial.Radius * radial.Radius) - ((_axisX * _axisX) + (_axisY * _axisY));
                break;

            case RadialGradientBrush radial:
                _kind = GradientKind.Radial;
                _originX = radial.Center.X;
                _originY = radial.Center.Y;
                _divisor = radial.Radius;
                break;

            default:
                // Angles in half turns, the unit of the deterministic functions
                var sweep = (SweepGradientBrush)brush;
                _kind = GradientKind.Sweep;
                _originX = sweep.Center.X;
                _originY = sweep.Center.Y;
                _axisX = sweep.StartAngle / 180;
                _divisor = (sweep.EndAngle - sweep.StartAngle) / 180;
                break;
        }

        _degenerate = !(_divisor > 0);
        _hasTransform = !brush.Transform.IsIdentity;
        if (_hasTransform)
        {
            var transform = brush.Transform;
            if (!new Affine2D(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY).TryInvert(out _inverse))
            {
                _degenerate = true;
            }
        }
    }

    /// <summary>Computes the colors of a run of pixels: four samples per pixel, laid out as <see cref="DrawingColor"/>.</summary>
    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        var stops = Stops[..(_stopCount * ValuesPerStop)];
        var last = _stopCount - 1;
        if (_colorSpace != GradientColorSpace.Encoded && !_degenerate && last != 0)
        {
            EvaluateInColorSpace(y, x, count, colors);
            return colors;
        }

        if (_degenerate || last == 0)
        {
            for (var i = 0; i < count; i++)
            {
                WriteStop(stops, last, colors.Slice(i * 4, 4));
            }

            return colors;
        }

        var centerY = y + 0.5;
        for (var i = 0; i < count; i++)
        {
            var t = GetParameter(x + i + 0.5, centerY);
            var output = colors.Slice(i * 4, 4);
            if (t < stops[0])
            {
                WriteStop(stops, 0, output);
                continue;
            }

            var index = 0;
            while (index < last && stops[(index + 1) * ValuesPerStop] <= t)
            {
                index++;
            }

            if (index == last)
            {
                WriteStop(stops, last, output);
                continue;
            }

            var from = stops.Slice(index * ValuesPerStop, ValuesPerStop);
            var to = stops.Slice((index + 1) * ValuesPerStop, ValuesPerStop);
            var u = (t - from[0]) / (to[0] - from[0]);
            var alpha = from[1] + (u * (to[1] - from[1]));
            var roundedAlpha = Round(alpha);
            if (roundedAlpha == 0)
            {
                output.Clear();
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                var start = from[2 + c] * from[1];
                output[c] = Round((start + (u * ((to[2 + c] * to[1]) - start))) / alpha);
            }

            output[3] = roundedAlpha;
        }

        return colors;
    }

    public void Dispose()
    {
        _stops?.Dispose();
        _colors?.Dispose();
        _samples?.Dispose();
        _stops = null;
        _colors = null;
        _samples = null;
        _brush = null;
        _frame = null;
    }

    /// <summary>Gets the parameter of the center of a pixel, brought into the range of the stops by the spread mode.</summary>
    private double GetParameter(double centerX, double centerY)
    {
        double px;
        var py = centerY - _originY;
        if (_hasTransform)
        {
            px = _inverse.TransformX(centerX, centerY) - _originX;
            py = _inverse.TransformY(centerX, centerY) - _originY;
        }
        else
        {
            px = centerX - _originX;
        }

        double t;
        switch (_kind)
        {
            case GradientKind.Linear:
                t = ((px * _axisX) + (py * _axisY)) / _divisor;
                break;

            case GradientKind.Radial:
                t = Math.Sqrt((px * px) + (py * py)) / _divisor;
                break;

            case GradientKind.Focal:
                var along = (px * _axisX) + (py * _axisY);
                t = (Math.Sqrt((along * along) + (_divisor * ((px * px) + (py * py)))) - along) / _divisor;
                break;

            default:
                var angle = DeterministicAngle.Atan2(py, px) - _axisX;
                angle -= 2 * Math.Floor(angle / 2);
                t = angle / _divisor;
                break;
        }

        switch (_spreadMode)
        {
            case GradientSpreadMode.Repeat:
                t -= Math.Floor(t);
                break;

            case GradientSpreadMode.Reflect:
                t -= 2 * Math.Floor(t / 2);
                if (t > 1)
                {
                    t = 2 - t;
                }

                break;

            default:
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                break;
        }

        return t;
    }

    /// <summary>Computes the colors of a run of pixels of a gradient that mixes its stops in linear light or in Oklab.</summary>
    private void EvaluateInColorSpace(int y, int x, int count, Span<ushort> colors)
    {
        var stops = Stops[..(_stopCount * ValuesPerStop)];
        var colorStops = _brush!.StopSpan;
        var samples = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_samples!.Span))[..count];
        var last = _stopCount - 1;
        var centerY = y + 0.5;
        Span<double> mixed = stackalloc double[3];
        for (var i = 0; i < count; i++)
        {
            var t = GetParameter(x + i + 0.5, centerY);
            if (t < stops[0])
            {
                samples[i] = colorStops[0].Color;
                continue;
            }

            var index = 0;
            while (index < last && stops[(index + 1) * ValuesPerStop] <= t)
            {
                index++;
            }

            if (index == last || colorStops[index].Color == colorStops[index + 1].Color)
            {
                samples[i] = colorStops[index].Color;
                continue;
            }

            var from = stops.Slice(index * ValuesPerStop, ValuesPerStop);
            var to = stops.Slice((index + 1) * ValuesPerStop, ValuesPerStop);
            var u = (t - from[0]) / (to[0] - from[0]);
            var alpha = from[5] + (u * (to[5] - from[5]));
            if (!(alpha > 0))
            {
                samples[i] = default;
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                var start = from[6 + c] * from[5];
                mixed[c] = (start + (u * ((to[6 + c] * to[5]) - start))) / alpha;
            }

            samples[i] = FromColorSpace(mixed, alpha);
        }

        if (_frame is null)
        {
            // 16-bit red, green, blue and alpha values, as they are
            for (var i = 0; i < count; i++)
            {
                var output = colors.Slice(i * 4, 4);
                output[0] = samples[i].R;
                output[1] = samples[i].G;
                output[2] = samples[i].B;
                output[3] = samples[i].A;
            }

            return;
        }

        _frame.ConvertDrawingColors(samples, colors);
    }

    /// <summary>Converts a 16-bit sRGB color to the color space of the gradient: its alpha, then its three components.</summary>
    private void ToColorSpace(Rgba64 color, Span<double> values)
    {
        var red = SrgbTransfer.Decode16[color.R];
        var green = SrgbTransfer.Decode16[color.G];
        var blue = SrgbTransfer.Decode16[color.B];
        values[0] = color.A / 65535.0;
        if (_colorSpace == GradientColorSpace.LinearSrgb)
        {
            values[1] = red;
            values[2] = green;
            values[3] = blue;
            return;
        }

        // Oklab: the response of the three kinds of cones, their cube roots, then a lightness and two color axes
        var l = Math.Cbrt((0.4122214708 * red) + (0.5363325363 * green) + (0.0514459929 * blue));
        var m = Math.Cbrt((0.2119034982 * red) + (0.6806995451 * green) + (0.1073969566 * blue));
        var s = Math.Cbrt((0.0883024619 * red) + (0.2817188376 * green) + (0.6299787005 * blue));
        values[1] = (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s);
        values[2] = (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s);
        values[3] = (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s);
    }

    /// <summary>Converts a color of the color space of the gradient to a 16-bit sRGB color.</summary>
    private Rgba64 FromColorSpace(ReadOnlySpan<double> values, double alpha)
    {
        var red = values[0];
        var green = values[1];
        var blue = values[2];
        if (_colorSpace == GradientColorSpace.Oklab)
        {
            var l = values[0] + (0.3963377774 * values[1]) + (0.2158037573 * values[2]);
            var m = values[0] - (0.1055613458 * values[1]) - (0.0638541728 * values[2]);
            var s = values[0] - (0.0894841775 * values[1]) - (1.2914855480 * values[2]);
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;
            red = (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s);
            green = (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s);
            blue = (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s);
        }

        return new Rgba64(ToSample(Encode(red)), ToSample(Encode(green)), ToSample(Encode(blue)), ToSample(alpha));

        // Written so that a value that is not a number is zero
        static double Encode(double value) => value > 0 ? SrgbTransfer.Encode(value < 1 ? value : 1) : 0;

        static ushort ToSample(double value)
        {
            var rounded = Math.Floor((value * 65535) + RoundingOffset);
            return rounded <= 0 ? (ushort)0 : rounded >= 65535 ? ushort.MaxValue : (ushort)rounded;
        }
    }

    private static void WriteStop(ReadOnlySpan<double> stops, int index, Span<ushort> output)
    {
        var entry = stops.Slice(index * ValuesPerStop, ValuesPerStop);
        output[0] = (ushort)entry[2];
        output[1] = (ushort)entry[3];
        output[2] = (ushort)entry[4];
        output[3] = (ushort)entry[1];
    }

    private ushort Round(double value)
    {
        var rounded = Math.Floor(value + RoundingOffset);
        return rounded <= 0 ? (ushort)0 : rounded >= _max ? (ushort)_max : (ushort)rounded;
    }

    private enum GradientKind : byte
    {
        Linear,
        Radial,
        Focal,
        Sweep,
    }
}
