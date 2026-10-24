using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>Collects the outline of a glyph, in the units of its font program, as a path.</summary>
internal sealed class GlyphPathSink : OutlineSink
{
    private VectorPathBuilder? _builder;
    private bool _isOpen;

    public override void MoveTo(double x, double y)
    {
        _builder ??= new VectorPathBuilder();
        if (_isOpen)
        {
            _builder.Close();
        }

        _builder.MoveTo(x, y);
        _isOpen = true;
    }

    public override void LineTo(double x, double y)
    {
        EnsureOpen(x, y);
        _builder!.LineTo(x, y);
    }

    public override void QuadraticTo(double controlX, double controlY, double x, double y)
    {
        EnsureOpen(controlX, controlY);
        _builder!.QuadraticBezierTo(controlX, controlY, x, y);
    }

    public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        EnsureOpen(control1X, control1Y);
        _builder!.CubicBezierTo(control1X, control1Y, control2X, control2Y, x, y);
    }

    public override void Close()
    {
        if (_isOpen)
        {
            _builder!.Close();
            _isOpen = false;
        }
    }

    /// <summary>Gets the path, or <see langword="null"/> for a glyph without outline.</summary>
    public VectorPath? ToPath()
    {
        Close();
        var path = _builder?.Build();
        return path is null || path.IsEmpty ? null : path;
    }

    private void EnsureOpen(double x, double y)
    {
        if (!_isOpen)
        {
            MoveTo(x, y);
        }
    }
}
