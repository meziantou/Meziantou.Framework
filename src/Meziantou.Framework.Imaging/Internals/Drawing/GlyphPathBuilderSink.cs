using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Adds the outline of a glyph to a path: the control points are transformed from font units to pixels exactly as
/// <see cref="GlyphPathSink"/> transforms them, and the curves stay curves.
/// </summary>
internal sealed class GlyphPathBuilderSink : OutlineSink
{
    private readonly VectorPathBuilder _builder;
    private Affine2D _transform;

    public GlyphPathBuilderSink(VectorPathBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>Starts a glyph.</summary>
    public void Begin(in Affine2D transform) => _transform = transform;

    public override void MoveTo(double x, double y) => _builder.MoveTo(_transform.TransformX(x, y), _transform.TransformY(x, y));

    public override void LineTo(double x, double y) => _builder.LineTo(_transform.TransformX(x, y), _transform.TransformY(x, y));

    public override void QuadraticTo(double controlX, double controlY, double x, double y)
    {
        _builder.QuadraticBezierTo(
            _transform.TransformX(controlX, controlY),
            _transform.TransformY(controlX, controlY),
            _transform.TransformX(x, y),
            _transform.TransformY(x, y));
    }

    public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        _builder.CubicBezierTo(
            _transform.TransformX(control1X, control1Y),
            _transform.TransformY(control1X, control1Y),
            _transform.TransformX(control2X, control2Y),
            _transform.TransformY(control2X, control2Y),
            _transform.TransformX(x, y),
            _transform.TransformY(x, y));
    }

    public override void Close() => _builder.Close();
}
