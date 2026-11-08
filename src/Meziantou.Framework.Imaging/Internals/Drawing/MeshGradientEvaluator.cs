using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors a <see cref="MeshGradientBrush"/> paints on the pixels of a row, in the pixel format of the frame.</summary>
/// <remarks>
/// <para>
/// Every patch is cut into a grid of <c>n</c> by <c>n</c> quadrilaterals of its parameters, whose corners are points of
/// the surface (in the coordinates of the brush, before its transformation). <c>n</c> bounds the distance between the
/// surface and its triangles on the canvas: with <c>d</c> the largest second difference of the control points along the
/// rows and the columns, and <c>m</c> the largest mixed difference, both measured on the canvas, the error of the linear
/// interpolation over a cell of side <c>1 / n</c> is at most <c>(0.75 d_u + 0.75 d_v + 2.25 m) / n^2</c>, which is kept
/// below a tenth of a pixel.
/// </para>
/// <para>
/// The quadrilaterals are numbered in the order they are painted: patch by patch, and inside a patch by increasing
/// <c>u</c>, then <c>v</c>. A uniform grid of buckets over the bounds of the mesh lists, for each bucket, the
/// quadrilaterals whose bounds meet it, in that order; a position tests the quadrilaterals of its bucket from the last
/// one, and the first that contains it gives its parameters.
/// </para>
/// </remarks>
internal sealed class MeshGradientEvaluator : PaintEvaluator, IDisposable
{
    /// <summary>The largest number of quadrilaterals along each side of a patch.</summary>
    internal const int MaxSubdivisions = 512;

    /// <summary>The largest distance, in pixels, between the surface of a patch and its triangles.</summary>
    private const double Tolerance = 0.1;

    /// <summary>The largest number of buckets of the grid.</summary>
    private const int MaxBuckets = 1 << 20;

    /// <summary>The values kept for each corner of a patch: alpha, then the three samples multiplied by alpha.</summary>
    private const int ValuesPerCorner = 4;

    /// <summary>The tie bias of the rounding rule shared with the gradients.</summary>
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    /// <summary>How far outside a triangle, in barycentric weight, a position still is inside it: the shared edges have no gap.</summary>
    private const double EdgeTolerance = 1e-9;

    private readonly AllocationScope _scope;
    private PooledBuffer? _vertices;
    private PooledBuffer? _bucketStarts;
    private PooledBuffer? _bucketCells;
    private PooledBuffer? _colors;
    private int[] _patchCellStarts = [];
    private int[] _patchVertexStarts = [];
    private int[] _patchSubdivisions = [];
    private double[] _cornerValues = [];
    private int _cellCount;
    private bool _isTransparent;
    private bool _hasTransform;
    private Affine2D _inverse;
    private bool _isProjective;
    private ProjectiveTransform _projectiveInverse;
    private double _minX;
    private double _minY;
    private double _maxX;
    private double _maxY;
    private int _bucketsX;
    private int _bucketsY;
    private double _bucketScaleX;
    private double _bucketScaleY;
    private double _max;

    public MeshGradientEvaluator(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets a value indicating whether the paint has no opacity anywhere: every color is transparent, or no patch covers anything.</summary>
    public override bool IsTransparent => _isTransparent;

    private Span<double> Vertices => unsafe(MemoryMarshal.Cast<byte, double>(_vertices!.Span));

    private Span<int> BucketStarts => unsafe(MemoryMarshal.Cast<byte, int>(_bucketStarts!.Span));

    private Span<int> BucketCells => unsafe(MemoryMarshal.Cast<byte, int>(_bucketCells!.Span));

    /// <summary>Gets the number of quadrilaterals along each side of a patch for the control points it has on the canvas.</summary>
    internal static int GetSubdivisions(ReadOnlySpan<PointD> points)
    {
        var rows = 0.0;
        var columns = 0.0;
        var mixed = 0.0;
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                var p = points[(4 * r) + c];
                if (c is 1 or 2)
                {
                    rows = Math.Max(rows, Length(points[(4 * r) + c - 1], p, points[(4 * r) + c + 1]));
                }

                if (r is 1 or 2)
                {
                    columns = Math.Max(columns, Length(points[(4 * (r - 1)) + c], p, points[(4 * (r + 1)) + c]));
                }

                if (r < 3 && c < 3)
                {
                    var q = points[(4 * r) + c + 1];
                    var s = points[(4 * (r + 1)) + c];
                    var t = points[(4 * (r + 1)) + c + 1];
                    var x = t.X - q.X - s.X + p.X;
                    var y = t.Y - q.Y - s.Y + p.Y;
                    mixed = Math.Max(mixed, Math.Sqrt((x * x) + (y * y)));
                }
            }
        }

        var error = (0.75 * (rows + columns)) + (2.25 * mixed);

        // A bound that is not a number (control points beyond the horizon of a projection) gives the largest grid
        var subdivisions = Math.Ceiling(Math.Sqrt(error / Tolerance));
        if (double.IsNaN(subdivisions) || subdivisions > MaxSubdivisions)
            return MaxSubdivisions;

        return Math.Max((int)subdivisions, 1);

        static double Length(PointD a, PointD b, PointD c)
        {
            var x = a.X - (2 * b.X) + c.X;
            var y = a.Y - (2 * b.Y) + c.Y;
            return Math.Sqrt((x * x) + (y * y));
        }
    }

    /// <summary>Converts the colors of a brush to the pixel format of a frame, cuts its patches and rents the scratch of the evaluation.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    /// <param name="brush">The brush.</param>
    /// <param name="transform">The transformation that places the brush on the canvas: the one of the brush, then the one of the canvas.</param>
    /// <param name="frame">A frame that is drawn on.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="paintPipeline">The conversion that brings the colors to the color space of the image, or <see langword="null"/> when they are colors of the image.</param>
    public void Prepare(MeshGradientBrush brush, in ProjectiveTransform transform, ImageFrame frame, int width, IccPipeline? paintPipeline)
    {
        ReleaseMesh();
        _colors ??= ResampleWeights.Rent<ushort>(_scope, 4L * width);
        _max = DrawingKernels.GetPaintMax(frame.PixelFormat);

        var patches = brush.PatchSpan;
        _cornerValues = new double[patches.Length * 4 * ValuesPerCorner];
        var transparent = true;
        for (var i = 0; i < patches.Length; i++)
        {
            var colors = patches[i].CornerColorSpan;
            for (var corner = 0; corner < 4; corner++)
            {
                var color = frame.ConvertDrawingColor(PaintColorConverter.Convert(paintPipeline, colors[corner]));
                var entry = _cornerValues.AsSpan(((4 * i) + corner) * ValuesPerCorner, ValuesPerCorner);
                entry[0] = color.Alpha;
                entry[1] = (double)color.C0 * color.Alpha;
                entry[2] = (double)color.C1 * color.Alpha;
                entry[3] = (double)color.C2 * color.Alpha;
                transparent &= color.Alpha == 0;
            }
        }

        _isProjective = false;
        _hasTransform = false;
        var degenerate = false;
        Affine2D forward = default;
        if (transform.TryGetExactAffine(out var affine))
        {
            forward = new Affine2D(affine.M11, affine.M12, affine.M21, affine.M22, affine.OffsetX, affine.OffsetY);
            _hasTransform = !affine.IsIdentity;
            if (_hasTransform && !forward.TryInvert(out _inverse))
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

        if (degenerate || transparent)
        {
            _isTransparent = true;
            return;
        }

        // The size of the grid of each patch, from its control points on the canvas
        _patchCellStarts = new int[patches.Length + 1];
        _patchVertexStarts = new int[patches.Length + 1];
        _patchSubdivisions = new int[patches.Length];
        Span<PointD> canvasPoints = stackalloc PointD[16];
        long cells = 0;
        long vertices = 0;
        for (var i = 0; i < patches.Length; i++)
        {
            var points = patches[i].ControlPointSpan;
            for (var k = 0; k < 16; k++)
            {
                var p = points[k];
                canvasPoints[k] = _isProjective
                    ? new PointD(transform.TransformX(p.X, p.Y) / transform.TransformW(p.X, p.Y), transform.TransformY(p.X, p.Y) / transform.TransformW(p.X, p.Y))
                    : new PointD(forward.TransformX(p.X, p.Y), forward.TransformY(p.X, p.Y));
            }

            var n = GetSubdivisions(canvasPoints);
            _patchSubdivisions[i] = n;
            _patchCellStarts[i] = (int)Math.Min(cells, int.MaxValue);
            _patchVertexStarts[i] = (int)Math.Min(vertices, int.MaxValue);
            cells += (long)n * n;
            vertices += (n + 1L) * (n + 1L);
        }

        if (cells > int.MaxValue || 2 * vertices > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, _scope.Limit, 2 * vertices * sizeof(double));

        _patchCellStarts[^1] = (int)cells;
        _patchVertexStarts[^1] = (int)vertices;
        _cellCount = (int)cells;

        // The points of the grids, in the coordinates of the brush
        _vertices = ResampleWeights.Rent<double>(_scope, 2 * vertices);
        var grid = Vertices;
        _minX = double.PositiveInfinity;
        _minY = double.PositiveInfinity;
        _maxX = double.NegativeInfinity;
        _maxY = double.NegativeInfinity;
        Span<double> bu = stackalloc double[4];
        Span<double> bv = stackalloc double[4];
        Span<double> rowX = stackalloc double[4];
        Span<double> rowY = stackalloc double[4];
        for (var i = 0; i < patches.Length; i++)
        {
            var points = patches[i].ControlPointSpan;
            var n = _patchSubdivisions[i];
            var offset = 2 * _patchVertexStarts[i];
            for (var iv = 0; iv <= n; iv++)
            {
                // The rows of control points combined for this v: a cubic Bézier curve in u
                MeshGradientPatch.Bernstein(iv / (double)n, bv);
                for (var c = 0; c < 4; c++)
                {
                    rowX[c] = (bv[0] * points[c].X) + (bv[1] * points[4 + c].X) + (bv[2] * points[8 + c].X) + (bv[3] * points[12 + c].X);
                    rowY[c] = (bv[0] * points[c].Y) + (bv[1] * points[4 + c].Y) + (bv[2] * points[8 + c].Y) + (bv[3] * points[12 + c].Y);
                }

                for (var iu = 0; iu <= n; iu++)
                {
                    MeshGradientPatch.Bernstein(iu / (double)n, bu);
                    var x = (bu[0] * rowX[0]) + (bu[1] * rowX[1]) + (bu[2] * rowX[2]) + (bu[3] * rowX[3]);
                    var y = (bu[0] * rowY[0]) + (bu[1] * rowY[1]) + (bu[2] * rowY[2]) + (bu[3] * rowY[3]);
                    var index = offset + (2 * ((iv * (n + 1)) + iu));
                    grid[index] = x;
                    grid[index + 1] = y;
                    _minX = Math.Min(_minX, x);
                    _minY = Math.Min(_minY, y);
                    _maxX = Math.Max(_maxX, x);
                    _maxY = Math.Max(_maxY, y);
                }
            }
        }

        BuildBuckets();
        _isTransparent = false;
    }

    /// <summary>Computes the colors of a run of pixels: four samples per pixel, laid out as <see cref="DrawingColor"/>.</summary>
    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        if (_isTransparent)
        {
            colors.Clear();
            return colors;
        }

        var centerY = y + 0.5;
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

            if (!TryGetColor(px, py, output))
            {
                output.Clear();
            }
        }

        return colors;
    }

    public void Dispose()
    {
        ReleaseMesh();
        _colors?.Dispose();
        _colors = null;
    }

    private void ReleaseMesh()
    {
        _vertices?.Dispose();
        _bucketStarts?.Dispose();
        _bucketCells?.Dispose();
        _vertices = null;
        _bucketStarts = null;
        _bucketCells = null;
        _isTransparent = true;
    }

    /// <summary>Lists the quadrilaterals whose bounds meet each bucket of a uniform grid over the bounds of the mesh, in the order they are painted.</summary>
    private void BuildBuckets()
    {
        var width = _maxX - _minX;
        var height = _maxY - _minY;
        var buckets = Math.Clamp(_cellCount, 1, MaxBuckets);

        // As many buckets as quadrilaterals, about square in the coordinates of the brush
        var aspect = width > 0 && height > 0 ? width / height : 1;
        _bucketsX = (int)Math.Clamp(Math.Ceiling(Math.Sqrt(buckets * aspect)), 1, buckets);
        _bucketsY = Math.Clamp((buckets + _bucketsX - 1) / _bucketsX, 1, buckets);
        _bucketScaleX = width > 0 ? _bucketsX / width : 0;
        _bucketScaleY = height > 0 ? _bucketsY / height : 0;

        _bucketStarts = ResampleWeights.Rent<int>(_scope, ((long)_bucketsX * _bucketsY) + 1);
        var starts = BucketStarts;

        // Counted in a first pass, then written in a second one at the start of each bucket
        long total = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            var bucketCells = pass == 0 ? default : BucketCells;
            for (var cell = 0; cell < _cellCount; cell++)
            {
                GetCellBounds(cell, out var left, out var top, out var right, out var bottom);
                var x0 = GetBucketX(left);
                var x1 = GetBucketX(right);
                var y0 = GetBucketY(top);
                var y1 = GetBucketY(bottom);
                for (var by = y0; by <= y1; by++)
                {
                    for (var bx = x0; bx <= x1; bx++)
                    {
                        var bucket = (by * _bucketsX) + bx;
                        if (pass == 0)
                        {
                            starts[bucket + 1]++;
                            total++;
                        }
                        else
                        {
                            bucketCells[starts[bucket]++] = cell;
                        }
                    }
                }
            }

            if (pass == 0)
            {
                if (total > int.MaxValue)
                    throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, _scope.Limit, total * sizeof(int));

                // The start of each bucket; the second pass moves it to the end of the bucket
                for (var bucket = 0; bucket < starts.Length - 1; bucket++)
                {
                    starts[bucket + 1] += starts[bucket];
                }

                _bucketCells = ResampleWeights.Rent<int>(_scope, total);
            }
        }

        // The second pass moved every start to the start of the next bucket
        starts[..^1].CopyTo(starts[1..]);
        starts[0] = 0;
    }

    private int GetBucketX(double x) => Math.Clamp((int)((x - _minX) * _bucketScaleX), 0, _bucketsX - 1);

    private int GetBucketY(double y) => Math.Clamp((int)((y - _minY) * _bucketScaleY), 0, _bucketsY - 1);

    /// <summary>Gets the patch, the subdivisions and the grid positions of the corners of a quadrilateral.</summary>
    private int GetCell(int cell, out int n, out int iu, out int iv)
    {
        var patch = Array.BinarySearch(_patchCellStarts, cell);
        if (patch < 0)
        {
            // Every patch has at least one quadrilateral: the starts increase strictly
            patch = ~patch - 1;
        }

        n = _patchSubdivisions[patch];
        var local = cell - _patchCellStarts[patch];
        iu = local / n;
        iv = local % n;
        return patch;
    }

    private void GetCellBounds(int cell, out double left, out double top, out double right, out double bottom)
    {
        var patch = GetCell(cell, out var n, out var iu, out var iv);
        var grid = Vertices;
        var origin = 2 * (_patchVertexStarts[patch] + (iv * (n + 1)) + iu);
        var stride = 2 * (n + 1);
        left = Math.Min(Math.Min(grid[origin], grid[origin + 2]), Math.Min(grid[origin + stride], grid[origin + stride + 2]));
        right = Math.Max(Math.Max(grid[origin], grid[origin + 2]), Math.Max(grid[origin + stride], grid[origin + stride + 2]));
        top = Math.Min(Math.Min(grid[origin + 1], grid[origin + 3]), Math.Min(grid[origin + stride + 1], grid[origin + stride + 3]));
        bottom = Math.Max(Math.Max(grid[origin + 1], grid[origin + 3]), Math.Max(grid[origin + stride + 1], grid[origin + stride + 3]));
    }

    /// <summary>Gets the color of a position: the one of the last quadrilateral that contains it.</summary>
    /// <returns><see langword="false"/> when no quadrilateral contains the position, or the color has no opacity.</returns>
    private bool TryGetColor(double px, double py, Span<ushort> output)
    {
        // Written so that a position that is not a number is in no bucket
        if (!(px >= _minX && px <= _maxX && py >= _minY && py <= _maxY))
            return false;

        var bucket = (GetBucketY(py) * _bucketsX) + GetBucketX(px);
        var starts = BucketStarts;
        var cells = BucketCells[starts[bucket]..starts[bucket + 1]];
        var grid = Vertices;
        for (var i = cells.Length - 1; i >= 0; i--)
        {
            var patch = GetCell(cells[i], out var n, out var iu, out var iv);
            var origin = 2 * (_patchVertexStarts[patch] + (iv * (n + 1)) + iu);
            var stride = 2 * (n + 1);
            var ax = grid[origin];
            var ay = grid[origin + 1];
            var bx = grid[origin + 2];
            var by = grid[origin + 3];
            var cx = grid[origin + stride + 2];
            var cy = grid[origin + stride + 3];
            var dx = grid[origin + stride];
            var dy = grid[origin + stride + 1];

            // The triangles (a, b, c) and (a, c, d): a is (iu, iv), b is (iu + 1, iv), c is (iu + 1, iv + 1), d is (iu, iv + 1)
            double u;
            double v;
            if (TryLocate(ax, ay, bx, by, cx, cy, px, py, out var s, out var t))
            {
                u = iu + s + t;
                v = iv + t;
            }
            else if (TryLocate(ax, ay, cx, cy, dx, dy, px, py, out s, out t))
            {
                u = iu + s;
                v = iv + s + t;
            }
            else
            {
                continue;
            }

            u = Math.Clamp(u / n, 0, 1);
            v = Math.Clamp(v / n, 0, 1);
            return SetColor(patch, u, v, output);
        }

        return false;
    }

    /// <summary>Gets the barycentric weights of the second and third vertices of a triangle at a position, when the triangle contains it.</summary>
    private static bool TryLocate(double ax, double ay, double bx, double by, double cx, double cy, double px, double py, out double s, out double t)
    {
        var e1x = bx - ax;
        var e1y = by - ay;
        var e2x = cx - ax;
        var e2y = cy - ay;
        var determinant = (e1x * e2y) - (e1y * e2x);
        if (determinant == 0)
        {
            s = 0;
            t = 0;
            return false;
        }

        var qx = px - ax;
        var qy = py - ay;
        s = ((qx * e2y) - (qy * e2x)) / determinant;
        t = ((e1x * qy) - (e1y * qx)) / determinant;
        return s >= -EdgeTolerance && t >= -EdgeTolerance && s + t <= 1 + EdgeTolerance;
    }

    /// <summary>Sets the color of parameters of a patch: the bilinear mix of its premultiplied corner colors.</summary>
    /// <returns><see langword="false"/> when the color has no opacity.</returns>
    private bool SetColor(int patch, double u, double v, Span<ushort> output)
    {
        var corners = _cornerValues.AsSpan(patch * 4 * ValuesPerCorner, 4 * ValuesPerCorner);
        var w0 = (1 - u) * (1 - v);
        var w1 = u * (1 - v);
        var w2 = u * v;
        var w3 = (1 - u) * v;
        var alpha = (w0 * corners[0]) + (w1 * corners[4]) + (w2 * corners[8]) + (w3 * corners[12]);
        var roundedAlpha = Round(alpha);
        if (roundedAlpha == 0)
            return false;

        for (var k = 1; k < ValuesPerCorner; k++)
        {
            output[k - 1] = Round(((w0 * corners[k]) + (w1 * corners[4 + k]) + (w2 * corners[8 + k]) + (w3 * corners[12 + k])) / alpha);
        }

        output[3] = roundedAlpha;
        return true;
    }

    private ushort Round(double value)
    {
        var rounded = Math.Floor(value + RoundingOffset);
        return rounded <= 0 ? (ushort)0 : rounded >= _max ? (ushort)_max : (ushort)rounded;
    }
}
