namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Places the outline of a glyph on the canvas: its control points are transformed from font units to pixels, then its
/// curves are flattened in pixels, with the tolerance of every other curve of the drawing API.
/// </summary>
/// <remarks>
/// Transforming before flattening keeps the tolerance independent of the size of the text, and makes the chords of a
/// glyph the ones of the same curves in a <see cref="Drawing.VectorPath"/>. Nothing is snapped to the pixel grid: there
/// is no hinting.
/// </remarks>
internal sealed class GlyphPathSink : OutlineSink
{
    private PathSink? _target;
    private Affine2D _transform;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private double _area;

    /// <summary>Gets twice the signed area of the contours emitted since <see cref="Begin"/>, in pixels: its sign is the orientation of the glyph.</summary>
    public double SignedArea => _area;

    /// <summary>Starts a glyph.</summary>
    public void Begin(PathSink target, in Affine2D transform)
    {
        _target = target;
        _transform = transform;
        _area = 0;
    }

    public override void MoveTo(double x, double y)
    {
        _startX = _x = _transform.TransformX(x, y);
        _startY = _y = _transform.TransformY(x, y);
        _target!.MoveTo(_x, _y);
    }

    public override void LineTo(double x, double y)
    {
        var endX = _transform.TransformX(x, y);
        var endY = _transform.TransformY(x, y);
        _target!.LineTo(endX, endY, smooth: false);
        Advance(endX, endY);
    }

    public override void QuadraticTo(double controlX, double controlY, double x, double y)
    {
        var cx = _transform.TransformX(controlX, controlY);
        var cy = _transform.TransformY(controlX, controlY);
        var endX = _transform.TransformX(x, y);
        var endY = _transform.TransformY(x, y);
        PathFlattener.Quadratic(_target!, _x, _y, cx, cy, endX, endY);

        // The orientation of a contour is the one of the polygon of its control points.
        Advance(cx, cy);
        Advance(endX, endY);
    }

    public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        var c1x = _transform.TransformX(control1X, control1Y);
        var c1y = _transform.TransformY(control1X, control1Y);
        var c2x = _transform.TransformX(control2X, control2Y);
        var c2y = _transform.TransformY(control2X, control2Y);
        var endX = _transform.TransformX(x, y);
        var endY = _transform.TransformY(x, y);
        PathFlattener.Cubic(_target!, _x, _y, c1x, c1y, c2x, c2y, endX, endY);
        Advance(c1x, c1y);
        Advance(c2x, c2y);
        Advance(endX, endY);
    }

    public override void Close()
    {
        _target!.Close(smooth: false);
        Advance(_startX, _startY);
    }

    private void Advance(double x, double y)
    {
        _area += (_x * y) - (x * _y);
        _x = x;
        _y = y;
    }
}
