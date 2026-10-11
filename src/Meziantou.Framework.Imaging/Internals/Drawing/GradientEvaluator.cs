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
/// With <c>i</c> the last stop whose offset is not greater than the parameter: before the first stop the color is the
/// first stop, at or after the last stop it is the last stop, otherwise stops <c>i</c> and <c>i + 1</c> are interpolated
/// at <c>u = (t - o[i]) / (o[i + 1] - o[i])</c> on premultiplied values:
/// <c>a = a0 + u (a1 - a0)</c> and <c>c = (c0 a0 + u (c1 a1 - c0 a0)) / a</c>, each rounded as
/// <c>floor(v + 1/2 + 2^-20)</c>. The stops are converted to the pixel format first, so equal stops give exactly the
/// color a solid brush has. A degenerate gradient (no length, no radius) has the color of the last stop everywhere.
/// </para>
/// </remarks>
internal sealed class GradientEvaluator : IDisposable
{
    /// <summary>The values kept for each stop: offset, alpha, three color samples.</summary>
    private const int ValuesPerStop = 5;

    /// <summary>The tie bias of the rounding rule shared with the resampler and the convolver.</summary>
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    private readonly AllocationScope _scope;
    private PooledBuffer? _stops;
    private int _stopCapacity;
    private PooledBuffer? _colors;
    private int _stopCount;
    private bool _radial;
    private bool _degenerate;
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
    public bool IsTransparent { get; private set; }

    private Span<double> Stops => unsafe(MemoryMarshal.Cast<byte, double>(_stops!.Span));

    /// <summary>Converts the stops of a brush to the pixel format of a frame and rents the row of colors.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public void Prepare(GradientBrush brush, ImageFrame frame, int width)
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
        _stopCount = stops.Length;
        _spreadMode = brush.SpreadMode;
        _max = (1 << PixelFormats.GetBitsPerComponent(frame.PixelFormat)) - 1;
        var values = Stops;
        var transparent = true;
        for (var i = 0; i < stops.Length; i++)
        {
            var color = frame.ConvertDrawingColor(stops[i].Color);
            var entry = values.Slice(i * ValuesPerStop, ValuesPerStop);
            entry[0] = stops[i].Offset;
            entry[1] = color.Alpha;
            entry[2] = color.C0;
            entry[3] = color.C1;
            entry[4] = color.C2;
            transparent &= color.Alpha == 0;
        }

        IsTransparent = transparent;
        if (brush is LinearGradientBrush linear)
        {
            _radial = false;
            _originX = linear.Start.X;
            _originY = linear.Start.Y;
            _axisX = linear.End.X - linear.Start.X;
            _axisY = linear.End.Y - linear.Start.Y;
            _divisor = (_axisX * _axisX) + (_axisY * _axisY);
        }
        else
        {
            var radial = (RadialGradientBrush)brush;
            _radial = true;
            _originX = radial.Center.X;
            _originY = radial.Center.Y;
            _divisor = radial.Radius;
        }

        _degenerate = !(_divisor > 0);
    }

    /// <summary>Computes the colors of a run of pixels: four samples per pixel, laid out as <see cref="DrawingColor"/>.</summary>
    public ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        var stops = Stops[..(_stopCount * ValuesPerStop)];
        var last = _stopCount - 1;
        if (_degenerate || last == 0)
        {
            for (var i = 0; i < count; i++)
            {
                WriteStop(stops, last, colors.Slice(i * 4, 4));
            }

            return colors;
        }

        var py = y + 0.5 - _originY;
        for (var i = 0; i < count; i++)
        {
            var px = x + i + 0.5 - _originX;
            var t = _radial ? Math.Sqrt((px * px) + (py * py)) / _divisor : ((px * _axisX) + (py * _axisY)) / _divisor;
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
        _stops = null;
        _colors = null;
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
}
