using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A path that can be filled, stroked or used as a clip by the 2D contexts (https://html.spec.whatwg.org/multipage/canvas.html#path2d-objects):
/// its subpaths are in the coordinates of the drawing operations that use it (transformed by their current transformation matrix).
/// </summary>
[IdlInterface("Path2D", "https://html.spec.whatwg.org/multipage/canvas.html#path2d")]
public sealed class Path2D
{
    /// <summary>Creates an empty path (https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d).</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d")]
    public Path2D()
    {
        Path = new CanvasPath();
    }

    /// <summary>
    /// Creates a copy of a path, or the path of SVG path data (https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d): the
    /// commands up to the first error of the path data.
    /// </summary>
    /// <param name="path">A <see cref="Path2D"/> or a string of SVG path data.</param>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d")]
    public Path2D(object? path)
    {
        Path = path switch
        {
            Path2D other => other.Path.Clone(),
            null => new CanvasPath(),
            _ => CanvasPath.FromSvgPathData(path as string ?? Convert.ToString(path, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
        };
    }

    /// <summary>The subpaths.</summary>
    internal CanvasPath Path { get; }

    /// <summary>
    /// Adds the subpaths of a path, transformed by a matrix (https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d-addpath):
    /// a matrix with a non-finite component is ignored (nothing is added).
    /// </summary>
    /// <exception cref="TypeErrorException">The members of the dictionary are inconsistent.</exception>
    [IdlMember("addPath", "https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d-addpath")]
    public void AddPath(Path2D path, DomMatrix2DInit? transform = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (CanvasRenderingContext2DBase.ToMatrix(transform) is not { } matrix)
            return;

        // A path added to itself is copied first
        Path.AddPath(ReferenceEquals(path, this) ? path.Path.Clone() : path.Path, matrix);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-closepath</summary>
    [IdlMember("closePath", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-closepath")]
    public void ClosePath() => Path.ClosePath();

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-moveto</summary>
    [IdlMember("moveTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-moveto")]
    public void MoveTo(double x, double y) => Path.MoveTo(x, y, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lineto</summary>
    [IdlMember("lineTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lineto")]
    public void LineTo(double x, double y) => Path.LineTo(x, y, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-quadraticcurveto</summary>
    [IdlMember("quadraticCurveTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-quadraticcurveto")]
    public void QuadraticCurveTo(double cpx, double cpy, double x, double y) => Path.QuadraticCurveTo(cpx, cpy, x, y, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beziercurveto</summary>
    [IdlMember("bezierCurveTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beziercurveto")]
    public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y) => Path.BezierCurveTo(cp1x, cp1y, cp2x, cp2y, x, y, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arcto</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the radius is negative.</exception>
    [IdlMember("arcTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arcto")]
    public void ArcTo(double x1, double y1, double x2, double y2, double radius) => Path.ArcTo(x1, y1, x2, y2, radius, DisplayMatrix.Identity, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rect</summary>
    [IdlMember("rect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rect")]
    public void Rect(double x, double y, double w, double h) => Path.Rect(x, y, w, h, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect</summary>
    /// <param name="x">The x coordinate of the rectangle.</param>
    /// <param name="y">The y coordinate of the rectangle.</param>
    /// <param name="w">The width of the rectangle.</param>
    /// <param name="h">The height of the rectangle.</param>
    /// <param name="radii">A radius (a number or a <see cref="DomPointInit"/>), or a list of 1 to 4 radii.</param>
    /// <exception cref="RangeErrorException">The number of radii is not between 1 and 4, or a radius is negative.</exception>
    [IdlMember("roundRect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect")]
    public void RoundRect(double x, double y, double w, double h, object? radii = null)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h))
            return;

        if (CanvasPath.NormalizeRoundRectRadii(radii ?? 0d) is { } normalized)
        {
            Path.RoundRect(x, y, w, h, normalized, DisplayMatrix.Identity);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arc</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the radius is negative.</exception>
    [IdlMember("arc", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arc")]
    public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise = false) => Path.Arc(x, y, radius, startAngle, endAngle, counterclockwise, DisplayMatrix.Identity);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ellipse</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when a radius is negative.</exception>
    [IdlMember("ellipse", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ellipse")]
    public void Ellipse(double x, double y, double radiusX, double radiusY, double rotation, double startAngle, double endAngle, bool counterclockwise = false)
        => Path.Ellipse(x, y, radiusX, radiusY, rotation, startAngle, endAngle, counterclockwise, DisplayMatrix.Identity);
}
