using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

internal sealed partial class ContentRenderer
{
    // The segments of a path
    private const int MaxPathSegments = 1 << 20;

    private VectorPathBuilder? _path;
    private int _pathSegments;
    private bool _hasCurrentPoint;
    private double _currentX;
    private double _currentY;
    private double _startX;
    private double _startY;
    private FillRule? _pendingClip;

    private void MoveTo(double x, double y)
    {
        _path ??= new VectorPathBuilder();
        ChargeSegment();
        _path.MoveTo(x, y);
        (_currentX, _currentY, _startX, _startY) = (x, y, x, y);
        _hasCurrentPoint = true;
    }

    private void LineTo(double x, double y)
    {
        if (!_hasCurrentPoint)
        {
            // A segment without current point starts a subpath (as viewers do)
            MoveTo(x, y);
            return;
        }

        ChargeSegment();
        _path!.LineTo(x, y);
        (_currentX, _currentY) = (x, y);
    }

    private void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!_hasCurrentPoint)
        {
            MoveTo(x1, y1);
        }

        ChargeSegment();
        _path!.CubicBezierTo(x1, y1, x2, y2, x3, y3);
        (_currentX, _currentY) = (x3, y3);
    }

    private void ClosePath()
    {
        if (!_hasCurrentPoint)
            return;

        _path!.Close();
        (_currentX, _currentY) = (_startX, _startY);
    }

    private void Rectangle(double x, double y, double width, double height)
    {
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        ClosePath();
    }

    private void ChargeSegment()
    {
        if (++_pathSegments > MaxPathSegments)
            throw new PdfResourceLimitException("A path has too many segments");
    }

    /// <summary>Paints the current path, then makes it the clipping path when <c>W</c> came before, and ends it.</summary>
    private void PaintPath(DrawingCanvas canvas, FillRule? fill, bool stroke)
    {
        var builder = _path;
        var clip = _pendingClip;
        _path = null;
        _pathSegments = 0;
        _hasCurrentPoint = false;
        _pendingClip = null;
        if (builder is null)
            return;

        var path = builder.Build();
        if (path.IsEmpty || !_optionalContent.IsVisible && clip is null)
            return;

        if (_optionalContent.IsVisible)
        {
            if (fill is { } rule && stroke && IsKnockoutContext && _element is null)
            {
                // In a knockout group, the fill and the stroke of a path are one element: the stroke composites over
                // the fill instead of knocking it out (11.7.4.4). Their alphas are their own: the element has an
                // opacity of 1, and the alpha of what it draws is its shape
                PaintElement(canvas, 1, element =>
                {
                    FillUserPath(element, path, rule);
                    StrokeUserPath(element, path);
                });
            }
            else
            {
                if (fill is { } fillRule)
                {
                    FillUserPath(canvas, path, fillRule);
                }

                if (stroke)
                {
                    StrokeUserPath(canvas, path);
                }
            }
        }

        if (clip is { } clipRule && TryGetDeviceTransform(_state.Ctm, out var transform))
        {
            AddClip(path.Transform(transform), clipRule);
        }
    }

    /// <summary>Intersects the clipping region with a path in device space (8.5.4).</summary>
    private void AddClip(VectorPath devicePath, FillRule fillRule)
    {
        var previous = _state.Clip;
        if (previous is not null && previous.Depth >= MaxClipDepth)
        {
            // Too many nested clips: the new path is intersected with the innermost one
            try
            {
                var merged = previous.Path.Combine(devicePath, PathCombineMode.Intersect, fillRule);
                _state.Clip = new ClipPath(merged, FillRule.NonZero, previous.Previous);
                return;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
            }
        }

        _state.Clip = new ClipPath(devicePath, fillRule, previous);
    }

    private void FillUserPath(DrawingCanvas canvas, VectorPath path, FillRule fillRule)
    {
        if (!TryGetDeviceTransform(_state.Ctm, out var transform))
            return;

        _cache.Charge(path.Bounds.Width > 0 ? 4 : 1);
        var devicePath = path.Transform(transform);
        Paint(canvas, stroke: false, (target, brush) => target.Fill(brush, devicePath, fillRule), devicePath.Bounds);
    }

    private void StrokeUserPath(DrawingCanvas canvas, VectorPath path)
    {
        if (!TryGetDeviceTransform(_state.Ctm, out var transform))
            return;

        var pen = CreatePen(_state.LineWidth, transform, scalesWithTransform: true);

        // The half width of the line in device space, as far as the miters of its joins and the squares of its caps go
        var width = Math.Max(_state.LineWidth * (Math.Abs(transform.M11) + Math.Abs(transform.M12) + Math.Abs(transform.M21) + Math.Abs(transform.M22)), 1);
        var margin = width / 2 * Math.Max(_state.LineJoin == LineJoin.Miter ? _state.MiterLimit : 1, 1.5);
        Paint(canvas, stroke: true, (target, brush) => target.Transform(transform, c => c.Stroke(pen(brush), path)), path.Transform(transform).Bounds, margin);
    }

    /// <summary>Creates the pen of the current line parameters: a width of 0 is the thinnest line the device can draw, one pixel (8.4.3.2).</summary>
    private Func<Brush, Pen> CreatePen(double width, AffineTransform transform, bool scalesWithTransform)
    {
        var scale = Math.Sqrt(Math.Abs(transform.Determinant));
        var minimum = scale > 0 ? 1 / scale : 1;
        var penWidth = Math.Max(width, minimum);
        var dashes = _state.DashArray;
        var hasDashes = dashes.Length > 0 && dashes.Sum() * scale > 1.0 / 1024 && dashes.All(value => value <= 16_777_216 && double.IsFinite(value));
        var cap = _state.LineCap;
        var join = _state.LineJoin;
        var miterLimit = _state.MiterLimit;
        var phase = double.IsFinite(_state.DashPhase) ? _state.DashPhase : 0;
        return brush => hasDashes
            ? new Pen(brush, penWidth) { LineCap = cap, LineJoin = join, MiterLimit = miterLimit, ScalesWithTransform = scalesWithTransform, DashPattern = dashes, DashOffset = phase }
            : new Pen(brush, penWidth) { LineCap = cap, LineJoin = join, MiterLimit = miterLimit, ScalesWithTransform = scalesWithTransform };
    }

    /// <summary>Gets the transform of the canvas for a matrix to device space; <see langword="false"/> for a matrix that does not paint (not finite, or degenerate).</summary>
    internal static bool TryGetDeviceTransform(PdfMatrix matrix, out AffineTransform transform)
    {
        transform = default;
        if (!double.IsFinite(matrix.A) || !double.IsFinite(matrix.B) || !double.IsFinite(matrix.C) || !double.IsFinite(matrix.D) || !double.IsFinite(matrix.E) || !double.IsFinite(matrix.F))
            return false;

        var determinant = (matrix.A * matrix.D) - (matrix.B * matrix.C);
        if (Math.Abs(determinant) < 1e-12 || Math.Abs(matrix.E) > 1e9 || Math.Abs(matrix.F) > 1e9)
            return false;

        transform = new AffineTransform(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
        return true;
    }
}
