using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors a <see cref="GradientBrush"/> paints on the pixels of a row, in the pixel format of the frame.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The color of a pixel is the color of the gradient at its center <c>(x + 1/2, y + 1/2)</c>. The
/// parameter is <c>((p - start) . (end - start)) / |end - start|^2</c> for a linear gradient and
/// <c>|p - center| / radius</c> for a radial one, computed for each pixel on its own (no running sum), then brought into
/// <c>[0, 1]</c>: clamped (pad), <c>t - floor(t)</c> (repeat), <c>u = t - 2 floor(t / 2)</c> folded as
/// <c>u &lt;= 1 ? u : 2 - u</c> (reflect), or left out when it is outside (transparent: the pixel has no color).
/// </para>
/// <para>
/// A radial gradient with a focal point <c>f</c> has, with <c>e = center - f</c>, <c>d = p - f</c> and
/// <c>a = radius^2 - |e|^2</c>, the parameter <c>(sqrt((d . e)^2 + a |d|^2) - (d . e)) / a</c>: the positive <c>t</c> for
/// which <c>p</c> is on the circle of center <c>f + t e</c> and radius <c>t radius</c>. With a focal radius <c>fr</c>,
/// <c>g = radius - fr</c>, <c>a = g^2 - |e|^2</c> and <c>b = (d . e) + fr g</c>, the parameter is
/// <c>(sqrt(b^2 + a (|d|^2 - fr^2)) - b) / a</c>: the <c>t</c> for which <c>p</c> is on the circle of center
/// <c>f + t e</c> and radius <c>fr + t g</c>, that radius being positive. When the circle of the focal point is not
/// inside the circle of the gradient, the parameter is the largest root of <c>a t^2 - 2 b t + c = 0</c> with
/// <c>a = |e|^2 - g^2</c>, <c>b = (d . e) + fr g</c> and <c>c = |d|^2 - fr^2</c> whose radius <c>fr + t g</c> is not negative
/// (see <see cref="GetConeParameter"/>); a position without such a root has no color. A sweep gradient has the
/// parameter <c>(angle(p - center) - start) mod 360 / (end - start)</c>, the angle being the one of
/// <see cref="DeterministicAngle.Atan2"/>. An elliptic radial gradient (<c>rx = Radius</c>, <c>ry = RadiusY</c>) is
/// the circular one of the focal point <c>(fx, cy + ((fy - cy) rx) / ry)</c>, at the position
/// <c>(px, cy + ((py - cy) rx) / ry)</c>; with one half-axis zero and not the other, it is degenerate. When the brush has a transformation, <c>p</c> is the center of the pixel
/// brought back through the inverse of that transformation; a transformation without inverse makes a degenerate gradient.
/// A projective transformation brings the center back to <c>(X / W, Y / W)</c>; where <c>W</c> is not positive (beyond
/// the horizon of the plane), the pixel has no color.
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
/// <b>Other color spaces.</b> With <see cref="GradientColorSpace.LinearSrgb"/>, <see cref="GradientColorSpace.Oklab"/> or <see cref="GradientColorSpace.Oklch"/>,
/// the stops are 16-bit sRGB colors, not converted to the pixel format: each is converted to the space
/// (<see cref="SrgbTransfer.Decode(double)"/>, then for Oklab the two matrices and the cube root of its definition),
/// and two stops are interpolated there with the same formulas, on values from 0 to 1. With
/// <see cref="GradientColorSpace.Oklch"/>, the two color axes of Oklab become a chroma and a hue: the hue is interpolated
/// along the shorter arc, not premultiplied, and the hue of a stop whose chroma is at most 0.000004 or whose alpha is 0 is that of the other
/// stop (<see cref="ColorValueMath.InterpolateHue"/>). The result is converted back,
/// brought into <c>[0, 1]</c>, and rounded to a 16-bit sRGB color as <c>floor(65535 v + 1/2 + 2^-20)</c>, which is
/// then converted to the pixel format like any brush color. Where the color is the color of a stop (before the first
/// one, from the last one, or between two stops of the same color), it is that 16-bit color itself.
/// </para>
/// </remarks>
internal sealed class GradientEvaluator : PaintEvaluator, IDisposable
{
    /// <summary>
    /// The values kept for each stop: offset, alpha, three color samples in the pixel format, then the alpha and the
    /// three components in the color space of the gradient, from 0 to 1, then the three color samples multiplied by the
    /// alpha, and the differences to the next stop: of the premultiplied samples, of the offset and of the alpha.
    /// </summary>
    private const int ValuesPerStop = 17;

    // Where the values computed from a stop and the next one are, among those of the stop
    private const int Premultiplied = 9;
    private const int PremultipliedStep = 12;
    private const int OffsetStep = 15;
    private const int AlphaStep = 16;

    /// <summary>The tie bias of the rounding rule shared with the resampler and the convolver.</summary>
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    // CSS Color 4 (OKLab_to_OKLCH): a chroma at most this small makes the hue of a stop powerless
    private const double OklchPowerlessChroma = 0.000004;

    private readonly AllocationScope _scope;
    private PooledBuffer? _stops;
    private int _stopCapacity;
    private PooledBuffer? _colors;
    private PooledBuffer? _samples;
    private PooledBuffer? _parameters;
    private GradientBrush? _brush;
    private ImageFrame? _frame;
    private IccPipeline? _paintPipeline;
    private GradientColorSpace _colorSpace;
    private int _stopCount;
    private bool _isTransparent;
    private GradientKind _kind;
    private bool _degenerate;
    private bool _hasTransform;
    private Affine2D _inverse;
    private bool _isElliptic;
    private double _ellipseCenterY;
    private double _ellipseRadiusX;
    private double _ellipseRadiusY;
    private bool _isProjective;
    private ProjectiveTransform _projectiveInverse;
    private GradientSpreadMode _spreadMode;
    private double _originX;
    private double _originY;
    private double _axisX;
    private double _axisY;
    private double _divisor;
    private double _focalRadius;
    private double _focalGrowth;
    private double _max;
    private int _width;
    private bool _hasRowColors;

    public GradientEvaluator(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets a value indicating whether every stop is fully transparent: the gradient paints nothing.</summary>
    public override bool IsTransparent => _isTransparent;

    private Span<double> Stops => unsafe(MemoryMarshal.Cast<byte, double>(_stops!.Span));

    /// <summary>Converts the stops of a brush to the pixel format of a frame and rents the row of colors.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    /// <param name="brush">The gradient.</param>
    /// <param name="transform">The transformation that places the gradient on the canvas: the one of the brush, then the one of the canvas.</param>
    /// <param name="frame">A frame that is drawn on.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="paintPipeline">The conversion that brings the colors of the stops to the color space of the image, or <see langword="null"/> when they are colors of the image.</param>
    public void Prepare(GradientBrush brush, AffineTransform transform, ImageFrame frame, int width, IccPipeline? paintPipeline = null)
    {
        _paintPipeline = paintPipeline;
        Prepare(brush, transform, frame, width, DrawingKernels.GetPaintMax(frame.PixelFormat));
    }

    /// <summary>Converts the stops of a brush placed by a projective transformation, and rents the row of colors.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    /// <param name="brush">The gradient.</param>
    /// <param name="transform">The transformation that places the gradient on the canvas: the one of the brush, then the one of the canvas.</param>
    /// <param name="frame">A frame that is drawn on.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="paintPipeline">The conversion that brings the colors of the stops to the color space of the image, or <see langword="null"/> when they are colors of the image.</param>
    public void Prepare(GradientBrush brush, in ProjectiveTransform transform, ImageFrame frame, int width, IccPipeline? paintPipeline = null)
    {
        if (transform.TryGetExactAffine(out var affine))
        {
            Prepare(brush, affine, frame, width, paintPipeline);
            return;
        }

        _paintPipeline = paintPipeline;
        Prepare(brush, AffineTransform.Identity, frame, width, DrawingKernels.GetPaintMax(frame.PixelFormat));
        _isProjective = true;
        if (!transform.TryInvert(out _projectiveInverse))
        {
            _degenerate = true;
        }
    }

    /// <summary>
    /// Prepares the evaluation of a gradient whose colors are not converted to the samples of a frame: 16-bit red,
    /// green, blue and alpha values, for what composes colors before it writes them to a frame.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public void PrepareColors(GradientBrush brush, AffineTransform transform, int width)
    {
        _paintPipeline = null;
        Prepare(brush, transform, frame: null, width, ushort.MaxValue);
    }

    private void Prepare(GradientBrush brush, AffineTransform transform, ImageFrame? frame, int width, int max)
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
        _parameters ??= ResampleWeights.Rent<double>(_scope, width);
        _colorSpace = brush.ColorSpace;
        _brush = brush;
        _frame = frame;
        _width = width;
        _hasRowColors = false;
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
            var color = frame is null ? new DrawingColor(stops[i].Color.R, stops[i].Color.G, stops[i].Color.B, stops[i].Color.A) : frame.ConvertDrawingColor(PaintColorConverter.Convert(_paintPipeline, stops[i].Color));
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

        // What the interpolation between two stops computes for every pixel, computed once: the same products and
        // differences
        for (var i = 0; i < stops.Length; i++)
        {
            var entry = values.Slice(i * ValuesPerStop, ValuesPerStop);
            entry[Premultiplied] = entry[2] * entry[1];
            entry[Premultiplied + 1] = entry[3] * entry[1];
            entry[Premultiplied + 2] = entry[4] * entry[1];
        }

        for (var i = 0; i + 1 < stops.Length; i++)
        {
            var entry = values.Slice(i * ValuesPerStop, ValuesPerStop);
            var next = values.Slice((i + 1) * ValuesPerStop, ValuesPerStop);
            entry[PremultipliedStep] = next[Premultiplied] - entry[Premultiplied];
            entry[PremultipliedStep + 1] = next[Premultiplied + 1] - entry[Premultiplied + 1];
            entry[PremultipliedStep + 2] = next[Premultiplied + 2] - entry[Premultiplied + 2];
            entry[OffsetStep] = next[0] - entry[0];
            entry[AlphaStep] = next[1] - entry[1];
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

            case RadialGradientBrush radial when !IsFirstCircleInside(radial):
                // Two circles anywhere: the cone of their circles, solved for the largest parameter
                _kind = GradientKind.Cone;
                _originX = radial.CircleFocalPoint.X;
                _originY = radial.CircleFocalPoint.Y;
                _axisX = radial.Center.X - radial.CircleFocalPoint.X;
                _axisY = radial.Center.Y - radial.CircleFocalPoint.Y;
                _focalRadius = radial.FocalRadius;
                _focalGrowth = radial.Radius - radial.FocalRadius;
                _divisor = ((_axisX * _axisX) + (_axisY * _axisY)) - (_focalGrowth * _focalGrowth);
                break;

            case RadialGradientBrush radial when radial.FocalRadius != 0:
                // The circles from the one of the focal point to the one of the gradient
                _kind = GradientKind.TwoCircles;
                _originX = radial.CircleFocalPoint.X;
                _originY = radial.CircleFocalPoint.Y;
                _axisX = radial.Center.X - radial.CircleFocalPoint.X;
                _axisY = radial.Center.Y - radial.CircleFocalPoint.Y;
                _focalRadius = radial.FocalRadius;
                _focalGrowth = radial.Radius - radial.FocalRadius;
                _divisor = (_focalGrowth * _focalGrowth) - ((_axisX * _axisX) + (_axisY * _axisY));
                break;

            case RadialGradientBrush radial when radial.CircleFocalPoint != radial.Center:
                _kind = GradientKind.Focal;
                _originX = radial.CircleFocalPoint.X;
                _originY = radial.CircleFocalPoint.Y;
                _axisX = radial.Center.X - radial.CircleFocalPoint.X;
                _axisY = radial.Center.Y - radial.CircleFocalPoint.Y;
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

        _degenerate = _kind != GradientKind.Cone && !(_divisor > 0);
        _isElliptic = false;
        if (brush is RadialGradientBrush { IsElliptic: true } ellipse)
        {
            // The circles are stretched along y around the center: a position is brought back to its circle first
            _isElliptic = true;
            _ellipseCenterY = ellipse.Center.Y;
            _ellipseRadiusX = ellipse.Radius;
            _ellipseRadiusY = ellipse.RadiusY;
            if (ellipse.Radius == 0 || ellipse.RadiusY == 0)
            {
                _degenerate = true;
            }
        }

        _isProjective = false;
        _hasTransform = !transform.IsIdentity;
        if (_hasTransform)
        {
            if (!new Affine2D(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY).TryInvert(out _inverse))
            {
                _degenerate = true;
            }
        }
    }

    /// <summary>Computes the colors of a run of pixels: four samples per pixel, laid out as <see cref="DrawingColor"/>.</summary>
    /// <remarks>
    /// A linear gradient without transformation whose axis is horizontal has the same colors on every row: its
    /// parameter is <c>((px ax) + (py 0)) / |a|^2</c>, where <c>py 0</c> is a zero for every finite <c>py</c> and the sign
    /// of a zero does not change any rounded sample. The colors of a whole row of the canvas are computed once, and every
    /// run of the canvas is a part of them: each pixel is computed on its own.
    /// </remarks>
    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        if (_kind == GradientKind.Linear && _axisY == 0 && !_hasTransform && !_isProjective && !_degenerate && x >= 0 && count <= _width - x)
        {
            if (!_hasRowColors)
            {
                EvaluateCore(y, 0, _width);
                _hasRowColors = true;
            }

            return unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span)).Slice(x * 4, count * 4);
        }

        _hasRowColors = false;
        return EvaluateCore(y, x, count);
    }

    private Span<ushort> EvaluateCore(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        var stops = Stops[..(_stopCount * ValuesPerStop)];
        var last = _stopCount - 1;
        if (_colorSpace != GradientColorSpace.Encoded && !_degenerate && last != 0)
        {
            EvaluateInColorSpace(y, x, count, colors);
            return colors;
        }

        // One stop is the same color everywhere, but where the gradient has no color: outside the range of a transparent
        // spread, beyond the horizon of a projective transformation, outside a cone
        if (_degenerate || (last == 0 && _spreadMode != GradientSpreadMode.Transparent && !_isProjective && _kind != GradientKind.Cone))
        {
            for (var i = 0; i < count; i++)
            {
                WriteStop(stops, last, colors.Slice(i * 4, 4));
            }

            return colors;
        }

        var parameters = unsafe(MemoryMarshal.Cast<byte, double>(_parameters!.Span))[..count];
        GetParameters(y, x, parameters);
        var firstOffset = stops[0];
        var index = 0;
        for (var i = 0; i < count; i++)
        {
            var t = parameters[i];
            var output = colors.Slice(i * 4, 4);
            if (double.IsNaN(t))
            {
                // A position without color: beyond the horizon of a plane seen in perspective
                output.Clear();
                continue;
            }

            if (t < firstOffset)
            {
                WriteStop(stops, 0, output);
                continue;
            }

            // The last stop whose offset is not greater than the parameter. The offsets increase: the search goes on
            // from the stop of the previous pixel when its offset is not greater either, and starts again otherwise.
            if (stops[index * ValuesPerStop] > t)
            {
                index = 0;
            }

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
            var u = (t - from[0]) / from[OffsetStep];
            var alpha = from[1] + (u * from[AlphaStep]);
            var roundedAlpha = Round(alpha);
            if (roundedAlpha == 0)
            {
                output.Clear();
                continue;
            }

            output[0] = Round((from[Premultiplied] + (u * from[PremultipliedStep])) / alpha);
            output[1] = Round((from[Premultiplied + 1] + (u * from[PremultipliedStep + 1])) / alpha);
            output[2] = Round((from[Premultiplied + 2] + (u * from[PremultipliedStep + 2])) / alpha);
            output[3] = roundedAlpha;
        }

        return colors;
    }

    /// <remarks>
    /// The color does not depend on the column for a degenerate gradient, a gradient of one stop that gives every pixel a
    /// color, and a linear gradient without transformation whose axis is vertical: its parameter is
    /// <c>((px 0) + (py ay)) / |a|^2</c>, where <c>px 0</c> is a zero for every finite <c>px</c> and the sign of a zero
    /// does not change any rounded sample; with a transparent spread, the whole row has a color or none. The color of the
    /// first pixel is then the color of the row.
    /// </remarks>
    public override bool TryGetRowColor(int y, int x, out DrawingColor color)
    {
        var isOneColor = _stopCount == 1 && _spreadMode != GradientSpreadMode.Transparent && !_isProjective && _kind != GradientKind.Cone;
        if (_degenerate || isOneColor || (_kind == GradientKind.Linear && !_hasTransform && !_isProjective && _axisX == 0))
        {
            var samples = Evaluate(y, x, 1);
            color = new DrawingColor(samples[0], samples[1], samples[2], samples[3]);
            return true;
        }

        color = default;
        return false;
    }

    public void Dispose()
    {
        _stops?.Dispose();
        _colors?.Dispose();
        _samples?.Dispose();
        _parameters?.Dispose();
        _parameters = null;
        _stops = null;
        _colors = null;
        _samples = null;
        _brush = null;
        _frame = null;
    }

    /// <summary>Gets the parameters of the centers of a run of pixels, brought into the range of the stops by the spread mode: those of <see cref="GetParameter"/>.</summary>
    private void GetParameters(int y, int x, Span<double> parameters)
    {
        var centerY = y + 0.5;
        if (_kind == GradientKind.Linear && !_hasTransform && !_isProjective)
        {
            var along = (centerY - _originY) * _axisY;
            for (var i = 0; i < parameters.Length; i++)
            {
                var px = (x + i + 0.5) - _originX;
                parameters[i] = ((px * _axisX) + along) / _divisor;
            }
        }
        else if (_kind == GradientKind.Radial && !_hasTransform && !_isProjective && !_isElliptic)
        {
            var py = centerY - _originY;
            var squared = py * py;
            for (var i = 0; i < parameters.Length; i++)
            {
                var px = (x + i + 0.5) - _originX;
                parameters[i] = Math.Sqrt((px * px) + squared) / _divisor;
            }
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                parameters[i] = GetParameter(x + i + 0.5, centerY);
            }

            return;
        }

        switch (_spreadMode)
        {
            case GradientSpreadMode.Repeat:
                foreach (ref var t in parameters)
                {
                    t -= Math.Floor(t);
                }

                break;

            case GradientSpreadMode.Reflect:
                foreach (ref var t in parameters)
                {
                    t -= 2 * Math.Floor(t / 2);
                    if (t > 1)
                    {
                        t = 2 - t;
                    }
                }

                break;

            case GradientSpreadMode.Transparent:
                // The parameters of these gradients are numbers: outside the range, the pixel has no color
                foreach (ref var t in parameters)
                {
                    if (t < 0 || t > 1)
                    {
                        t = double.NaN;
                    }
                }

                break;

            default:
                foreach (ref var t in parameters)
                {
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                }

                break;
        }
    }

    /// <summary>Gets the parameter of the center of a pixel, brought into the range of the stops by the spread mode; not a number where the gradient has no color.</summary>
    private double GetParameter(double centerX, double centerY)
    {
        var t = GetUnspreadParameter(centerX, centerY);
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

            case GradientSpreadMode.Transparent:
                // Written so that a value that is not a number stays one
                if (t < 0 || t > 1)
                    return double.NaN;

                break;

            default:
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                break;
        }

        return t;
    }

    /// <summary>Gets the parameter of the center of a pixel, before the spread mode; not a number where the gradient has no color.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetUnspreadParameter(double centerX, double centerY)
    {
        double x;
        var y = centerY;
        if (_isProjective)
        {
            var w = _projectiveInverse.TransformW(centerX, centerY);

            // Written so that a value that is not a number has no color: what is beyond the horizon is not painted
            if (!(w > 0))
                return double.NaN;

            x = _projectiveInverse.TransformX(centerX, centerY) / w;
            y = _projectiveInverse.TransformY(centerX, centerY) / w;
        }
        else if (_hasTransform)
        {
            x = _inverse.TransformX(centerX, centerY);
            y = _inverse.TransformY(centerX, centerY);
        }
        else
        {
            x = centerX;
        }

        if (_isElliptic)
        {
            // Back from the ellipse to the circle it is stretched from
            y = _ellipseCenterY + (((y - _ellipseCenterY) * _ellipseRadiusX) / _ellipseRadiusY);
        }

        var px = x - _originX;
        var py = y - _originY;

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

            case GradientKind.Cone:
                t = GetConeParameter(px, py);
                if (double.IsNaN(t))
                    return t;

                break;

            case GradientKind.TwoCircles:
                // The circles are inside each other: every position is on one of them, and the root is real. A
                // rounding that makes it slightly negative under the square root is the position where it is zero.
                var half = ((px * _axisX) + (py * _axisY)) + (_focalRadius * _focalGrowth);
                var discriminant = (half * half) + (_divisor * (((px * px) + (py * py)) - (_focalRadius * _focalRadius)));
                t = (Math.Sqrt(discriminant > 0 ? discriminant : 0) - half) / _divisor;
                break;

            default:
                var angle = DeterministicAngle.Atan2(py, px) - _axisX;
                angle -= 2 * Math.Floor(angle / 2);
                t = angle / _divisor;
                break;
        }

        return t;
    }

    /// <summary>
    /// Gets a value indicating whether the first circle of a radial gradient is inside the second one, as the gradients
    /// of the first versions of the library required: every position then has a parameter, given by the formulas of
    /// those versions.
    /// </summary>
    private static bool IsFirstCircleInside(RadialGradientBrush radial)
        => (radial.CircleFocalPoint == radial.Center && radial.FocalRadius == 0) || radial.CircleFocalPoint.DistanceTo(radial.Center) + radial.FocalRadius < radial.Radius;

    /// <summary>
    /// Gets the parameter of a position relative to the first circle of a cone: the largest <c>t</c> for which the
    /// position is on the circle of center <c>t e</c> and radius <c>r0 + t g</c>, that radius not being negative; not a
    /// number when there is none.
    /// </summary>
    /// <remarks>
    /// With <c>a = |e|^2 - g^2</c>, <c>b = p . e + r0 g</c> and <c>c = |p|^2 - r0^2</c>, the parameters are the roots of
    /// <c>a t^2 - 2 b t + c = 0</c>. Concentric circles have <c>t = (|p| - r0) / g</c>, or, with the same radius, 0
    /// strictly inside and 1 elsewhere (with the pad spread mode only). Circles that touch inside each other
    /// (<c>a = 0</c>) have the one root <c>c / (2 b)</c>.
    /// </remarks>
    private double GetConeParameter(double px, double py)
    {
        var distanceSquared = (px * px) + (py * py);
        var growth = _focalGrowth;
        if (_axisX == 0 && _axisY == 0)
        {
            if (growth == 0)
            {
                // The same circle twice: inside or not, and nothing without padding
                return _spreadMode == GradientSpreadMode.Pad && _focalRadius > 0 ? (distanceSquared < _focalRadius * _focalRadius ? 0 : 1) : double.NaN;
            }

            return (Math.Sqrt(distanceSquared) - _focalRadius) / growth;
        }

        var a = _divisor;
        var b = ((px * _axisX) + (py * _axisY)) + (_focalRadius * growth);
        var c = distanceSquared - (_focalRadius * _focalRadius);
        double t;
        if (a == 0)
        {
            if (b == 0)
                return double.NaN;

            t = c / (2 * b);
            return _focalRadius + (t * growth) >= 0 ? t : double.NaN;
        }

        var discriminant = (b * b) - (a * c);
        if (!(discriminant >= 0))
            return double.NaN;

        var root = Math.Sqrt(discriminant);
        var first = (b + root) / a;
        var second = (b - root) / a;
        var larger = Math.Max(first, second);
        var smaller = Math.Min(first, second);
        if (_focalRadius + (larger * growth) >= 0)
            return larger;

        return _focalRadius + (smaller * growth) >= 0 ? smaller : double.NaN;
    }

    /// <summary>Computes the colors of a run of pixels of a gradient that mixes its stops in linear light, in Oklab or in Oklch.</summary>
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
            if (double.IsNaN(t))
            {
                samples[i] = default;
                continue;
            }

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

            if (_colorSpace == GradientColorSpace.Oklch)
            {
                // A hue is an angle, not premultiplied: along the shorter arc, the hue of a gray or transparent stop
                // taking the other one, so that a transparent stop does not tint its neighbor
                var fromHue = from[7] <= OklchPowerlessChroma || from[5] == 0 ? to[8] : from[8];
                var toHue = to[7] <= OklchPowerlessChroma || to[5] == 0 ? from[8] : to[8];
                mixed[2] = ColorValueMath.InterpolateHue(fromHue, toHue, u, HueInterpolation.Shorter);
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

        if (_paintPipeline is { } pipeline)
        {
            // The colors that were mixed are colors of the profile of the paints: each one is brought to the image
            foreach (ref var sample in samples)
            {
                if (sample.A != 0)
                {
                    sample = PaintColorConverter.Convert(pipeline, sample);
                }
            }
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
        if (_colorSpace == GradientColorSpace.Oklch)
        {
            // Oklch: the chroma and the hue of the two color axes
            (values[2], values[3]) = ColorValueMath.ToPolar(values[2], values[3]);
        }
    }

    /// <summary>Converts a color of the color space of the gradient to a 16-bit sRGB color.</summary>
    private Rgba64 FromColorSpace(ReadOnlySpan<double> values, double alpha)
    {
        var red = values[0];
        var green = values[1];
        var blue = values[2];
        if (_colorSpace == GradientColorSpace.Oklch)
        {
            // The two color axes of the chroma and the hue
            (green, blue) = ColorValueMath.FromPolar(green, blue);
        }

        if (_colorSpace is GradientColorSpace.Oklab or GradientColorSpace.Oklch)
        {
            var l = red + (0.3963377774 * green) + (0.2158037573 * blue);
            var m = red - (0.1055613458 * green) - (0.0638541728 * blue);
            var s = red - (0.0894841775 * green) - (1.2914855480 * blue);
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
        TwoCircles,
        Cone,
        Sweep,
    }
}
