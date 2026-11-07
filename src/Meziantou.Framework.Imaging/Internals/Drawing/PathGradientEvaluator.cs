using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors a <see cref="PathGradientBrush"/> paints on the pixels of a row, in the pixel format of the frame.</summary>
/// <remarks>
/// The contract is the one of <see cref="PathGradientBrush"/>: the center of the pixel, brought back through the
/// inverse of the transformation (<c>(X / W, Y / W)</c> for a projective one, no color where <c>W</c> is not positive),
/// the first triangle of the fan that contains it, and its barycentric weights <c>s</c> and <c>t</c>, computed for each
/// pixel on its own with <c>+ - * /</c> only. The colors are those of <see cref="GradientEvaluator"/>: premultiplied
/// values of the converted colors, rounded as <c>floor(v + 1/2 + 2^-20)</c>.
/// </remarks>
internal sealed class PathGradientEvaluator : PaintEvaluator, IDisposable
{
    /// <summary>
    /// The values kept for each triangle: the edges from the center to its two points, the cross product of those edges,
    /// its bounds, and the indexes of its two points.
    /// </summary>
    private const int ValuesPerTriangle = 11;

    /// <summary>The values kept for each vertex: alpha, then the three samples multiplied by alpha.</summary>
    private const int ValuesPerVertex = 4;

    /// <summary>The tie bias of the rounding rule shared with the gradients.</summary>
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    private readonly AllocationScope _scope;
    private PooledBuffer? _triangles;
    private PooledBuffer? _vertices;
    private PooledBuffer? _active;
    private int _capacity;
    private PooledBuffer? _colors;
    private int _triangleCount;
    private bool _isTransparent;
    private bool _hasTransform;
    private Affine2D _inverse;
    private bool _isProjective;
    private ProjectiveTransform _projectiveInverse;
    private double _centerX;
    private double _centerY;
    private double _max;

    public PathGradientEvaluator(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets a value indicating whether the paint has no opacity anywhere: every color is transparent, or no triangle contains anything.</summary>
    public override bool IsTransparent => _isTransparent;

    private Span<double> Triangles => unsafe(MemoryMarshal.Cast<byte, double>(_triangles!.Span));

    private Span<double> Vertices => unsafe(MemoryMarshal.Cast<byte, double>(_vertices!.Span));

    private Span<int> Active => unsafe(MemoryMarshal.Cast<byte, int>(_active!.Span));

    /// <summary>Converts the colors of a brush to the pixel format of a frame and rents the scratch of the evaluation.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    /// <param name="brush">The brush.</param>
    /// <param name="transform">The transformation that places the brush on the canvas: the one of the brush, then the one of the canvas.</param>
    /// <param name="frame">A frame that is drawn on.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="paintPipeline">The conversion that brings the colors to the color space of the image, or <see langword="null"/> when they are colors of the image.</param>
    public void Prepare(PathGradientBrush brush, in ProjectiveTransform transform, ImageFrame frame, int width, IccPipeline? paintPipeline)
    {
        var points = brush.PointSpan;
        var count = points.Length;
        if (count > _capacity)
        {
            _triangles?.Dispose();
            _vertices?.Dispose();
            _active?.Dispose();
            _triangles = null;
            _vertices = null;
            _active = null;
            _capacity = 0;
            _triangles = ResampleWeights.Rent<double>(_scope, (long)count * ValuesPerTriangle);
            _vertices = ResampleWeights.Rent<double>(_scope, (count + 1L) * ValuesPerVertex);
            _active = ResampleWeights.Rent<int>(_scope, count);
            _capacity = count;
        }

        _colors ??= ResampleWeights.Rent<ushort>(_scope, 4L * width);
        _max = DrawingKernels.GetPaintMax(frame.PixelFormat);

        // The center is the vertex 0, the points the vertices 1 to n
        var vertices = Vertices;
        var transparent = SetVertex(vertices, 0, frame.ConvertDrawingColor(PaintColorConverter.Convert(paintPipeline, brush.CenterColor)));
        var colors = brush.PointColorSpan;
        for (var i = 0; i < count; i++)
        {
            transparent &= SetVertex(vertices, i + 1, frame.ConvertDrawingColor(PaintColorConverter.Convert(paintPipeline, colors[i])));
        }

        _centerX = brush.Center.X;
        _centerY = brush.Center.Y;
        var triangles = Triangles;
        _triangleCount = 0;
        for (var i = 0; i < count; i++)
        {
            var next = i + 1 == count ? 0 : i + 1;
            var a = points[i];
            var b = points[next];
            var e1x = a.X - _centerX;
            var e1y = a.Y - _centerY;
            var e2x = b.X - _centerX;
            var e2y = b.Y - _centerY;
            var determinant = (e1x * e2y) - (e1y * e2x);
            if (determinant == 0)
                continue;

            var entry = triangles.Slice(_triangleCount * ValuesPerTriangle, ValuesPerTriangle);
            entry[0] = e1x;
            entry[1] = e1y;
            entry[2] = e2x;
            entry[3] = e2y;
            entry[4] = determinant;
            entry[5] = Math.Min(_centerX, Math.Min(a.X, b.X));
            entry[6] = Math.Max(_centerX, Math.Max(a.X, b.X));
            entry[7] = Math.Min(_centerY, Math.Min(a.Y, b.Y));
            entry[8] = Math.Max(_centerY, Math.Max(a.Y, b.Y));
            entry[9] = i + 1;
            entry[10] = next + 1;
            _triangleCount++;
        }

        _isProjective = false;
        _hasTransform = false;
        var degenerate = false;
        if (transform.TryGetExactAffine(out var affine))
        {
            _hasTransform = !affine.IsIdentity;
            if (_hasTransform && !new Affine2D(affine.M11, affine.M12, affine.M21, affine.M22, affine.OffsetX, affine.OffsetY).TryInvert(out _inverse))
            {
                degenerate = true;
            }
        }
        else
        {
            _isProjective = true;
            if (!transform.TryInvert(out _projectiveInverse))
            {
                degenerate = true;
            }
        }

        if (degenerate)
        {
            _triangleCount = 0;
        }

        _isTransparent = transparent || _triangleCount == 0;
    }

    /// <summary>Computes the colors of a run of pixels: four samples per pixel, laid out as <see cref="DrawingColor"/>.</summary>
    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        if (_triangleCount == 0)
        {
            colors.Clear();
            return colors;
        }

        var triangles = Triangles[..(_triangleCount * ValuesPerTriangle)];
        var vertices = Vertices;
        var centerY = y + 0.5;

        // Without transformation, the row is a line of the polygon: only the triangles it crosses can contain a pixel
        var active = Active;
        var activeCount = 0;
        var isRowAligned = !_hasTransform && !_isProjective;
        for (var i = 0; i < _triangleCount; i++)
        {
            if (!isRowAligned || (triangles[(i * ValuesPerTriangle) + 7] <= centerY && centerY <= triangles[(i * ValuesPerTriangle) + 8]))
            {
                active[activeCount++] = i;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var output = colors.Slice(i * 4, 4);
            var centerX = x + i + 0.5;
            double px;
            double py;
            if (_isProjective)
            {
                var w = _projectiveInverse.TransformW(centerX, centerY);

                // Written so that a value that is not a number has no color: what is beyond the horizon is not painted
                if (!(w > 0))
                {
                    output.Clear();
                    continue;
                }

                px = _projectiveInverse.TransformX(centerX, centerY) / w;
                py = _projectiveInverse.TransformY(centerX, centerY) / w;
            }
            else if (_hasTransform)
            {
                px = _inverse.TransformX(centerX, centerY);
                py = _inverse.TransformY(centerX, centerY);
            }
            else
            {
                px = centerX;
                py = centerY;
            }

            if (!TryGetColor(triangles, vertices, active[..activeCount], px, py, output))
            {
                output.Clear();
            }
        }

        return colors;
    }

    public void Dispose()
    {
        _triangles?.Dispose();
        _vertices?.Dispose();
        _active?.Dispose();
        _colors?.Dispose();
        _triangles = null;
        _vertices = null;
        _active = null;
        _colors = null;
        _capacity = 0;
    }

    /// <summary>Stores the values of a vertex.</summary>
    /// <returns><see langword="true"/> when the color is transparent.</returns>
    private static bool SetVertex(Span<double> vertices, int index, DrawingColor color)
    {
        var entry = vertices.Slice(index * ValuesPerVertex, ValuesPerVertex);
        entry[0] = color.Alpha;
        entry[1] = (double)color.C0 * color.Alpha;
        entry[2] = (double)color.C1 * color.Alpha;
        entry[3] = (double)color.C2 * color.Alpha;
        return color.Alpha == 0;
    }

    /// <summary>Gets the color of a position: the one of the first triangle that contains it.</summary>
    /// <returns><see langword="false"/> when no triangle contains the position, or the color has no opacity.</returns>
    private bool TryGetColor(ReadOnlySpan<double> triangles, ReadOnlySpan<double> vertices, ReadOnlySpan<int> active, double px, double py, Span<ushort> output)
    {
        var dx = px - _centerX;
        var dy = py - _centerY;
        foreach (var index in active)
        {
            var entry = triangles.Slice(index * ValuesPerTriangle, ValuesPerTriangle);

            // Written so that a position that is not a number is in no triangle
            if (!(px >= entry[5] && px <= entry[6] && py >= entry[7] && py <= entry[8]))
                continue;

            var s = ((dx * entry[3]) - (dy * entry[2])) / entry[4];
            var t = ((entry[0] * dy) - (entry[1] * dx)) / entry[4];
            if (!(s >= 0 && t >= 0 && s + t <= 1))
                continue;

            var center = vertices[..ValuesPerVertex];
            var a = vertices.Slice((int)entry[9] * ValuesPerVertex, ValuesPerVertex);
            var b = vertices.Slice((int)entry[10] * ValuesPerVertex, ValuesPerVertex);
            var alpha = center[0] + (s * (a[0] - center[0])) + (t * (b[0] - center[0]));
            var roundedAlpha = Round(alpha);
            if (roundedAlpha == 0)
                return false;

            output[0] = Round((center[1] + (s * (a[1] - center[1])) + (t * (b[1] - center[1]))) / alpha);
            output[1] = Round((center[2] + (s * (a[2] - center[2])) + (t * (b[2] - center[2]))) / alpha);
            output[2] = Round((center[3] + (s * (a[3] - center[3])) + (t * (b[3] - center[3]))) / alpha);
            output[3] = roundedAlpha;
            return true;
        }

        return false;
    }

    private ushort Round(double value)
    {
        var rounded = Math.Floor(value + RoundingOffset);
        return rounded <= 0 ? (ushort)0 : rounded >= _max ? (ushort)_max : (ushort)rounded;
    }
}
