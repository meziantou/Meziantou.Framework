using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The surface a color glyph is drawn on before it is composited onto the image: layers of colors, and the clips of the
/// operations of its display list.
/// </summary>
/// <remarks>
/// <para>
/// The surface covers the pixels of the canvas the glyph can change. A layer holds, for each pixel, a color multiplied
/// by its opacity, as four single precision numbers from 0 to 1; a clip holds the part of each pixel it lets through.
/// The paints are evaluated at the center of each pixel, in the space of the paint, and weighted by the exact coverage
/// of the outline they fill: the edge of a shape is as exact as the edge of any other shape of the drawing API, and a
/// gradient is sampled once per pixel.
/// </para>
/// <para>
/// Only additions, multiplications, divisions and square roots are used (the angle of a sweep gradient comes from
/// <see cref="DeterministicAngle"/>): the pixels are the same on every platform.
/// </para>
/// <para>The storage is rented from an allocation scope and kept: drawing another glyph allocates nothing once it has grown.</para>
/// </remarks>
internal sealed class ColorSurface : IDisposable
{
    private readonly AllocationScope _scope;
    private PooledBuffer? _layers;
    private long _layerCapacity;
    private PooledBuffer? _clips;
    private long _clipCapacity;
    private PooledBuffer? _colors;
    private PooledBuffer? _coverage;
    private int _rowCapacity;
    private int _layer;
    private int _clip;

    public ColorSurface(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets the left of the surface on the canvas.</summary>
    public int Left { get; private set; }

    public int Top { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Gets or sets the color of the text, not multiplied by its opacity, for the paints that use it.</summary>
    public (double Red, double Green, double Blue, double Alpha) Foreground { get; set; }

    /// <summary>Gets or sets the gradient of the text when its brush is one: it gives the color of the text at each pixel.</summary>
    public GradientEvaluator? ForegroundGradient { get; set; }

    /// <summary>Gets or sets what the alpha of the glyph is multiplied by when it is composited: the alpha of the text, as browsers fade the glyphs that have their own colors.</summary>
    public double Opacity { get; set; } = 1;

    private Span<float> Layers => unsafe(MemoryMarshal.Cast<byte, float>(_layers!.Span));

    private Span<float> Clips => unsafe(MemoryMarshal.Cast<byte, float>(_clips!.Span));

    private Span<float> Layer => Layers.Slice(_layer * Width * Height * 4, Width * Height * 4);

    private Span<float> Clip => Clips.Slice(_clip * Width * Height, Width * Height);

    /// <summary>
    /// Places the surface on the pixels a glyph can change and rents its storage. Nothing is drawn.
    /// </summary>
    /// <returns><see langword="false"/> when the glyph is outside the canvas.</returns>
    /// <exception cref="ImageResourceLimitException">The surface exceeds the allocation limit.</exception>
    public bool Reserve(ColorGlyph glyph, in Affine2D placement, int canvasWidth, int canvasHeight)
    {
        Width = Height = 0;
        if (glyph.IsEmpty)
            return false;

        // The box of the glyph, on the canvas.
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? glyph.XMin : glyph.XMax;
            var y = (corner & 2) == 0 ? glyph.YMin : glyph.YMax;
            var px = placement.TransformX(x, y);
            var py = placement.TransformY(x, y);
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        // Written so that a value that is not a number gives an empty surface.
        if (!(maxX > 0 && maxY > 0 && minX < canvasWidth && minY < canvasHeight))
            return false;

        var left = (int)Math.Max(0, Math.Floor(minX));
        var top = (int)Math.Max(0, Math.Floor(minY));
        var right = (int)Math.Min(canvasWidth, Math.Ceiling(maxX));
        var bottom = (int)Math.Min(canvasHeight, Math.Ceiling(maxY));
        if (right <= left || bottom <= top)
            return false;

        var area = (long)(right - left) * (bottom - top);
        var layerLength = area * 4 * glyph.LayerDepth;
        if (layerLength > _layerCapacity)
        {
            _layers?.Dispose();
            _layers = null;
            _layerCapacity = 0;
            _layers = ResampleWeights.Rent<float>(_scope, layerLength);
            _layerCapacity = layerLength;
        }

        var clipLength = area * glyph.ClipDepth;
        if (clipLength > _clipCapacity)
        {
            _clips?.Dispose();
            _clips = null;
            _clipCapacity = 0;
            _clips = ResampleWeights.Rent<float>(_scope, clipLength);
            _clipCapacity = clipLength;
        }

        if (canvasWidth > _rowCapacity)
        {
            _colors?.Dispose();
            _coverage?.Dispose();
            _colors = _coverage = null;
            _rowCapacity = 0;
            _colors = ResampleWeights.Rent<ushort>(_scope, 4L * canvasWidth);
            _coverage = ResampleWeights.Rent<uint>(_scope, canvasWidth);
            _rowCapacity = canvasWidth;
        }

        Left = left;
        Top = top;
        Width = right - left;
        Height = bottom - top;
        return true;
    }

    /// <summary>Starts drawing a glyph on the surface that was reserved for it: one empty layer and no clip.</summary>
    public void Begin()
    {
        _layer = 0;
        _clip = -1;
        Layer.Clear();
    }

    /// <summary>Paints a run of pixels of a row of the canvas with a paint, over the layer, weighted by a coverage and by the clip.</summary>
    public void Fill(int y, int x, ReadOnlySpan<uint> coverage, ColorGlyph glyph, in ColorPaint paint, in Affine2D inverse)
    {
        var row = y - Top;
        if ((uint)row >= (uint)Height)
            return;

        var layer = Layer.Slice(row * Width * 4, Width * 4);
        var clip = _clip < 0 ? default : Clip.Slice(row * Width, Width);
        var first = Math.Max(x, Left);
        var end = Math.Min(x + coverage.Length, Left + Width);
        for (var px = first; px < end; px++)
        {
            double weight = coverage[px - x];
            if (weight == 0)
                continue;

            weight /= CoverageRasterizer.FullCoverage;
            if (!clip.IsEmpty)
            {
                weight *= clip[px - Left];
                if (weight == 0)
                    continue;
            }

            if (!Evaluate(glyph, paint, inverse, px, y, out var red, out var green, out var blue, out var alpha))
                continue;

            // Source over, with a source that is the paint weighted by the coverage.
            alpha *= weight;
            var pixel = layer.Slice((px - Left) * 4, 4);
            var keep = 1 - alpha;
            pixel[0] = (float)((red * alpha) + (pixel[0] * keep));
            pixel[1] = (float)((green * alpha) + (pixel[1] * keep));
            pixel[2] = (float)((blue * alpha) + (pixel[2] * keep));
            pixel[3] = (float)(alpha + (pixel[3] * keep));
        }
    }

    /// <summary>Paints everything the clip lets through, or the whole surface without clip.</summary>
    public void FillClip(ColorGlyph glyph, in ColorPaint paint, in Affine2D inverse)
    {
        Span<uint> full = unsafe(MemoryMarshal.Cast<byte, uint>(_coverage!.Span))[..Width];
        full.Fill(CoverageRasterizer.FullCoverage);
        for (var row = 0; row < Height; row++)
        {
            Fill(Top + row, Left, full, glyph, paint, inverse);
        }
    }

    /// <summary>Starts a clip that lets nothing through: <see cref="AddClip"/> then opens it, within the clip under it.</summary>
    public void PushClip()
    {
        _clip++;
        Clip.Clear();
    }

    /// <summary>Opens the clip on a run of pixels of a row of the canvas, by their coverage.</summary>
    public void AddClip(int y, int x, ReadOnlySpan<uint> coverage)
    {
        var row = y - Top;
        if ((uint)row >= (uint)Height)
            return;

        var clip = Clip.Slice(row * Width, Width);
        var parent = _clip == 0 ? default : Clips.Slice(((_clip - 1) * Width * Height) + (row * Width), Width);
        var first = Math.Max(x, Left);
        var end = Math.Min(x + coverage.Length, Left + Width);
        for (var px = first; px < end; px++)
        {
            var weight = (double)coverage[px - x] / CoverageRasterizer.FullCoverage;
            clip[px - Left] = (float)(parent.IsEmpty ? weight : weight * parent[px - Left]);
        }
    }

    public void PopClip() => _clip--;

    /// <summary>Starts an empty layer over the current one.</summary>
    public void PushLayer()
    {
        _layer++;
        Layer.Clear();
    }

    /// <summary>Ends the current layer: it is the source of a composition whose backdrop, and result, is the layer under it.</summary>
    public void PopLayer(ColorCompositeMode mode)
    {
        var source = Layer;
        _layer--;
        var backdrop = Layer;
        for (var i = 0; i < backdrop.Length; i += 4)
        {
            Compose(mode, source.Slice(i, 4), backdrop.Slice(i, 4));
        }
    }

    /// <summary>Gets a row of the glyph as a paint of the drawing pipeline: a color per pixel, in the samples of a frame, and a coverage.</summary>
    /// <returns><see langword="false"/> when the row is empty.</returns>
    public bool GetRow(int row, ImageFrame frame, out ReadOnlySpan<uint> coverage, out ReadOnlySpan<ushort> colors)
    {
        var layer = Layers.Slice(row * Width * 4, Width * 4);
        var rowCoverage = unsafe(MemoryMarshal.Cast<byte, uint>(_coverage!.Span))[..Width];
        var rowColors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(Width * 4)];
        var any = false;
        for (var i = 0; i < Width; i++)
        {
            var pixel = layer.Slice(i * 4, 4);
            double alpha = pixel[3];
            if (!(alpha > 0))
            {
                rowCoverage[i] = 0;
                continue;
            }

            // The color of the layer is multiplied by its opacity; the pipeline takes it as it is, with its opacity.
            alpha = Math.Min(alpha, 1);
            var color = frame.ConvertDrawingColor(new Rgba64(ToSample(pixel[0] / alpha), ToSample(pixel[1] / alpha), ToSample(pixel[2] / alpha), ToSample(alpha * Opacity)));
            var entry = rowColors.Slice(i * 4, 4);
            entry[0] = color.C0;
            entry[1] = color.C1;
            entry[2] = color.C2;
            entry[3] = color.Alpha;
            rowCoverage[i] = color.Alpha == 0 ? 0 : CoverageRasterizer.FullCoverage;
            any |= color.Alpha != 0;
        }

        coverage = rowCoverage;
        colors = rowColors;
        return any;
    }

    public void Dispose()
    {
        _layers?.Dispose();
        _clips?.Dispose();
        _colors?.Dispose();
        _coverage?.Dispose();
        _layers = _clips = _colors = _coverage = null;
        _layerCapacity = _clipCapacity = 0;
        _rowCapacity = 0;
        Width = Height = 0;
    }

    private static ushort ToSample(double value)
    {
        var rounded = Math.Floor((value * 65535) + 0.5);
        return rounded <= 0 ? (ushort)0 : rounded >= 65535 ? (ushort)65535 : (ushort)rounded;
    }

    /// <summary>Gets the color of a paint at the center of a pixel, not multiplied by its opacity.</summary>
    /// <returns><see langword="false"/> when the paint draws nothing there.</returns>
    private bool Evaluate(ColorGlyph glyph, in ColorPaint paint, in Affine2D inverse, int px, int py, out double red, out double green, out double blue, out double alpha)
    {
        if (paint.Kind == ColorPaintKind.Image)
            return Sample(glyph.Image!, glyph.Numbers.AsSpan(paint.Geometry, 4), inverse.TransformX(px + 0.5, py + 0.5), inverse.TransformY(px + 0.5, py + 0.5), out red, out green, out blue, out alpha);

        var stops = glyph.Stops.AsSpan(paint.FirstStop, paint.StopCount);
        if (paint.Kind == ColorPaintKind.Solid)
        {
            Resolve(stops[0], px, py, out red, out green, out blue, out alpha);
            return alpha > 0;
        }

        // The center of the pixel, in the space of the paint.
        var x = inverse.TransformX(px + 0.5, py + 0.5);
        var y = inverse.TransformY(px + 0.5, py + 0.5);
        var geometry = glyph.Numbers.AsSpan(paint.Geometry);
        double t;
        int line;
        switch (paint.Kind)
        {
            case ColorPaintKind.Linear:
                t = ((x - geometry[0]) * geometry[2]) + ((y - geometry[1]) * geometry[3]);
                line = 4;
                break;

            case ColorPaintKind.Radial:
                if (!TryGetRadialPosition(x - geometry[0], y - geometry[1], geometry[2], geometry[3] - geometry[0], geometry[4] - geometry[1], geometry[5] - geometry[2], out t))
                {
                    red = green = blue = alpha = 0;
                    return false;
                }

                line = 6;
                break;

            default:
                {
                    // The angle of the pixel around the center, from the start of the sweep to its end.
                    var angle = DeterministicAngle.Atan2(y - geometry[1], x - geometry[0]);
                    var sweep = geometry[3] - geometry[2];
                    t = sweep != 0 ? (angle - geometry[2]) / sweep : angle < geometry[2] ? double.NegativeInfinity : double.PositiveInfinity;
                    line = 4;
                    break;
                }
        }

        // The position on the color line, whose stops go from 0 to 1.
        var scale = geometry[line + 1];
        t -= geometry[line];
        if (scale == 0)
        {
            // Every stop is at the same place: the first color before it, the last one from it.
            Resolve(t < 0 ? stops[0] : stops[^1], px, py, out red, out green, out blue, out alpha);
            return alpha > 0;
        }

        t *= scale;
        switch (paint.Extend)
        {
            case 1:
                t -= Math.Floor(t);
                break;

            case 2:
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

        // Written so that a position that is not a number is the first stop.
        var index = 0;
        while (index < stops.Length - 1 && stops[index + 1].Offset <= t)
        {
            index++;
        }

        Resolve(stops[index], px, py, out red, out green, out blue, out alpha);
        if (index == stops.Length - 1 || !(t > stops[index].Offset))
            return alpha > 0;

        // Between two stops: the colors multiplied by their opacity are interpolated, as every gradient of the library.
        Resolve(stops[index + 1], px, py, out var red2, out var green2, out var blue2, out var alpha2);
        var u = (t - stops[index].Offset) / (stops[index + 1].Offset - stops[index].Offset);
        var mixed = alpha + (u * (alpha2 - alpha));
        if (!(mixed > 0))
        {
            red = green = blue = alpha = 0;
            return false;
        }

        red = ((red * alpha) + (u * ((red2 * alpha2) - (red * alpha)))) / mixed;
        green = ((green * alpha) + (u * ((green2 * alpha2) - (green * alpha)))) / mixed;
        blue = ((blue * alpha) + (u * ((blue2 * alpha2) - (blue * alpha)))) / mixed;
        alpha = mixed;
        return true;
    }

    /// <summary>
    /// Gets the color of the image of a glyph at a point, in the units of the glyph: the four pixels around the point,
    /// interpolated with their colors multiplied by their opacity. Beyond the centers of the pixels of an edge, the
    /// image continues with these pixels.
    /// </summary>
    private static bool Sample(ColorImage image, ReadOnlySpan<double> geometry, double x, double y, out double red, out double green, out double blue, out double alpha)
    {
        // The position in pixels of the image, from the center of its top-left pixel; its rows go down.
        var u = ((x - geometry[0]) / geometry[2]) - 0.5;
        var v = ((geometry[1] - y) / geometry[3]) - 0.5;
        var column = Math.Floor(u);
        var row = Math.Floor(v);
        var fx = u - column;
        var fy = v - row;
        var x0 = (int)Math.Clamp(column, 0, image.Width - 1);
        var x1 = (int)Math.Clamp(column + 1, 0, image.Width - 1);
        var y0 = (int)Math.Clamp(row, 0, image.Height - 1);
        var y1 = (int)Math.Clamp(row + 1, 0, image.Height - 1);
        var pixels = image.Pixels;
        red = green = blue = alpha = 0;
        Add(pixels[(y0 * image.Width) + x0], (1 - fx) * (1 - fy), ref red, ref green, ref blue, ref alpha);
        Add(pixels[(y0 * image.Width) + x1], fx * (1 - fy), ref red, ref green, ref blue, ref alpha);
        Add(pixels[(y1 * image.Width) + x0], (1 - fx) * fy, ref red, ref green, ref blue, ref alpha);
        Add(pixels[(y1 * image.Width) + x1], fx * fy, ref red, ref green, ref blue, ref alpha);
        if (!(alpha > 0))
            return false;

        red /= alpha;
        green /= alpha;
        blue /= alpha;
        return true;

        static void Add(Rgba32 pixel, double weight, ref double red, ref double green, ref double blue, ref double alpha)
        {
            var opacity = weight * (pixel.A / 255.0);
            red += opacity * (pixel.R / 255.0);
            green += opacity * (pixel.G / 255.0);
            blue += opacity * (pixel.B / 255.0);
            alpha += opacity;
        }
    }

    /// <summary>Gets the color of a stop: its own, or the color of the text with the opacity of the stop.</summary>
    private void Resolve(in ColorStop stop, int px, int py, out double red, out double green, out double blue, out double alpha)
    {
        if (!stop.IsForeground)
        {
            (red, green, blue, alpha) = (stop.Red, stop.Green, stop.Blue, stop.Alpha);
            return;
        }

        if (ForegroundGradient is { } gradient)
        {
            var color = gradient.Evaluate(py, px, 1);
            (red, green, blue, alpha) = (color[0] / 65535.0, color[1] / 65535.0, color[2] / 65535.0, stop.Alpha * (color[3] / 65535.0));
            return;
        }

        (red, green, blue, alpha) = (Foreground.Red, Foreground.Green, Foreground.Blue, stop.Alpha * Foreground.Alpha);
    }

    /// <summary>
    /// Gets the position of a point on the color line of a gradient between two circles: the greatest t for which the
    /// circle of center c0 + t (c1 - c0) and of radius r0 + t (r1 - r0), which must be positive, goes through the
    /// point ("Radial gradients" of the COLR table, which follows the radial gradients of the HTML canvas).
    /// </summary>
    /// <param name="px">The horizontal distance from the center of the first circle to the point.</param>
    /// <param name="py">The vertical distance.</param>
    /// <param name="r0">The radius of the first circle.</param>
    /// <param name="cx">The horizontal distance from the center of the first circle to the center of the second one.</param>
    /// <param name="cy">The vertical distance.</param>
    /// <param name="dr">The difference of the radii.</param>
    /// <param name="t">The position.</param>
    /// <returns><see langword="false"/> when no circle of the gradient goes through the point.</returns>
    private static bool TryGetRadialPosition(double px, double py, double r0, double cx, double cy, double dr, out double t)
    {
        // |p - t c|^2 = (r0 + t dr)^2, a quadratic equation in t: a t^2 - 2 b t + c = 0.
        var a = (cx * cx) + (cy * cy) - (dr * dr);
        var b = (px * cx) + (py * cy) + (r0 * dr);
        var c = (px * px) + (py * py) - (r0 * r0);
        if (a == 0)
        {
            // The two circles have a common tangent point: the equation is linear.
            t = b != 0 ? c / (2 * b) : double.NaN;
            return double.IsFinite(t) && r0 + (t * dr) > 0;
        }

        var discriminant = (b * b) - (a * c);
        if (!(discriminant >= 0))
        {
            t = 0;
            return false;
        }

        var root = Math.Sqrt(discriminant);
        var first = (b + root) / a;
        var second = (b - root) / a;
        t = Math.Max(first, second);
        if (r0 + (t * dr) > 0)
            return true;

        t = Math.Min(first, second);
        return r0 + (t * dr) > 0;
    }

    /// <summary>
    /// Composes a source pixel with a backdrop pixel, in place of the backdrop. Both are colors multiplied by their
    /// opacity. The formulas are those of "Compositing and Blending Level 1" of the W3C.
    /// </summary>
    private static void Compose(ColorCompositeMode mode, ReadOnlySpan<float> source, Span<float> backdrop)
    {
        double sa = source[3];
        double da = backdrop[3];
        if (mode <= ColorCompositeMode.Plus)
        {
            // The Porter-Duff operators: each pixel is a part of the source and a part of the backdrop.
            double fs;
            double fd;
            switch (mode)
            {
                case ColorCompositeMode.Clear:
                    (fs, fd) = (0, 0);
                    break;

                case ColorCompositeMode.Source:
                    (fs, fd) = (1, 0);
                    break;

                case ColorCompositeMode.Destination:
                    return;

                case ColorCompositeMode.SourceOver:
                    (fs, fd) = (1, 1 - sa);
                    break;

                case ColorCompositeMode.DestinationOver:
                    (fs, fd) = (1 - da, 1);
                    break;

                case ColorCompositeMode.SourceIn:
                    (fs, fd) = (da, 0);
                    break;

                case ColorCompositeMode.DestinationIn:
                    (fs, fd) = (0, sa);
                    break;

                case ColorCompositeMode.SourceOut:
                    (fs, fd) = (1 - da, 0);
                    break;

                case ColorCompositeMode.DestinationOut:
                    (fs, fd) = (0, 1 - sa);
                    break;

                case ColorCompositeMode.SourceAtop:
                    (fs, fd) = (da, 1 - sa);
                    break;

                case ColorCompositeMode.DestinationAtop:
                    (fs, fd) = (1 - da, sa);
                    break;

                case ColorCompositeMode.Xor:
                    (fs, fd) = (1 - da, 1 - sa);
                    break;

                default:
                    (fs, fd) = (1, 1);
                    break;
            }

            for (var i = 0; i < 4; i++)
            {
                backdrop[i] = (float)Math.Min(1, (source[i] * fs) + (backdrop[i] * fd));
            }

            return;
        }

        // The blend modes: where the two pixels overlap, their colors are blended; elsewhere each one shows.
        var both = sa * da;
        Span<double> blended = [0, 0, 0];
        if (both > 0)
        {
            Span<double> s = [source[0] / sa, source[1] / sa, source[2] / sa];
            Span<double> d = [backdrop[0] / da, backdrop[1] / da, backdrop[2] / da];
            BlendFunctions.Blend(ToBlendMode(mode), s, d, blended);
        }

        for (var i = 0; i < 3; i++)
        {
            backdrop[i] = (float)((source[i] * (1 - da)) + (backdrop[i] * (1 - sa)) + (both * blended[i]));
        }

        backdrop[3] = (float)(sa + da - both);
    }

    /// <summary>Gets the blend mode of a composition mode of the COLR table that is not an operator of Porter and Duff.</summary>
    private static BlendMode ToBlendMode(ColorCompositeMode mode) => mode switch
    {
        ColorCompositeMode.Screen => BlendMode.Screen,
        ColorCompositeMode.Overlay => BlendMode.Overlay,
        ColorCompositeMode.Darken => BlendMode.Darken,
        ColorCompositeMode.Lighten => BlendMode.Lighten,
        ColorCompositeMode.ColorDodge => BlendMode.ColorDodge,
        ColorCompositeMode.ColorBurn => BlendMode.ColorBurn,
        ColorCompositeMode.HardLight => BlendMode.HardLight,
        ColorCompositeMode.SoftLight => BlendMode.SoftLight,
        ColorCompositeMode.Difference => BlendMode.Difference,
        ColorCompositeMode.Exclusion => BlendMode.Exclusion,
        ColorCompositeMode.Hue => BlendMode.Hue,
        ColorCompositeMode.Saturation => BlendMode.Saturation,
        ColorCompositeMode.Color => BlendMode.Color,
        ColorCompositeMode.Luminosity => BlendMode.Luminosity,
        _ => BlendMode.Multiply,
    };
}
