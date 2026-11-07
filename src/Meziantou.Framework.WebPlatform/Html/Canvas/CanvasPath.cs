using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A path of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#concept-path): a list of subpaths of straight lines
/// and Bézier curves, built by the methods of the <c>CanvasPath</c> mixin (shared by the 2D contexts and <c>Path2D</c>). The
/// points are stored transformed by the transform given to each method: the current transformation matrix for the current
/// default path of a context (https://html.spec.whatwg.org/multipage/canvas.html#current-default-path), the identity for a
/// <c>Path2D</c>. The arcs and the ellipses are converted to cubic Bézier curves (an affine transform maps them exactly).
/// </summary>
internal sealed class CanvasPath
{
    // The maximal angle of the arc of a cubic Bézier curve that approximates an ellipse
    private const double MaxBezierArc = Math.PI / 2;

    private readonly List<PathVerb> _verbs = [];
    private readonly List<PhysicalOffset> _points = [];

    // The first point of the current subpath and the last point, in the stored coordinates
    private PhysicalOffset _subpathStart;
    private PhysicalOffset _lastPoint;

    /// <summary>Whether the path has at least one subpath.</summary>
    public bool HasSubpaths { get; private set; }

    /// <summary>The commands of the path.</summary>
    public IReadOnlyList<PathVerb> Verbs => _verbs;

    /// <summary>The points of the commands, in the stored coordinates.</summary>
    public IReadOnlyList<PhysicalOffset> Points => _points;

    /// <summary>The last point of the current subpath, in the stored coordinates (meaningful when <see cref="HasSubpaths"/>).</summary>
    public PhysicalOffset LastPoint => _lastPoint;

    /// <summary>Removes all the subpaths (<c>beginPath()</c>).</summary>
    public void Clear()
    {
        _verbs.Clear();
        _points.Clear();
        HasSubpaths = false;
    }

    /// <summary>Returns a copy of the path.</summary>
    public CanvasPath Clone()
    {
        var clone = new CanvasPath { _subpathStart = _subpathStart, _lastPoint = _lastPoint, HasSubpaths = HasSubpaths };
        clone._verbs.AddRange(_verbs);
        clone._points.AddRange(_points);
        return clone;
    }

    /// <summary>
    /// Adds the subpaths of another path transformed by a matrix (https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d-addpath),
    /// (a <c>Path2D</c> has no stored transform).
    /// </summary>
    public void AddPath(CanvasPath path, DisplayMatrix transform)
    {
        if (!path.HasSubpaths)
            return;

        var index = 0;
        foreach (var verb in path._verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    _verbs.Add(PathVerb.MoveTo);
                    _subpathStart = _lastPoint = transform.Apply(path._points[index++]);
                    _points.Add(_lastPoint);
                    break;

                case PathVerb.Close:
                    _verbs.Add(PathVerb.Close);
                    _lastPoint = _subpathStart;
                    break;

                default:
                    var count = verb switch { PathVerb.LineTo => 1, PathVerb.QuadTo => 2, _ => 3 };
                    _verbs.Add(verb);
                    for (var i = 0; i < count; i++)
                    {
                        _points.Add(transform.Apply(path._points[index++]));
                    }

                    _lastPoint = _points[^1];
                    break;
            }
        }

        HasSubpaths = true;
    }

    /// <summary>
    /// Creates the subpaths of an SVG path string (https://html.spec.whatwg.org/multipage/canvas.html#dom-path2d): the commands
    /// up to the first error (https://svgwg.org/svg2-draft/paths.html#PathDataErrorHandling).
    /// </summary>
    public static CanvasPath FromSvgPathData(string data)
    {
        var path = new CanvasPath();
        var commands = SvgPathData.Parse(data);
        foreach (var command in SvgPathData.Normalize(commands))
        {
            var v = command.Values;
            switch (command.Type)
            {
                case 'M':
                    path.MoveTo(v[0], v[1], DisplayMatrix.Identity);
                    break;

                case 'L':
                    path.LineTo(v[0], v[1], DisplayMatrix.Identity);
                    break;

                case 'C':
                    path.BezierCurveTo(v[0], v[1], v[2], v[3], v[4], v[5], DisplayMatrix.Identity);
                    break;

                default:
                    path.ClosePath();
                    break;
            }
        }

        return path;
    }

    /// <summary>
    /// Returns the path as a path of the display list, with a fill rule, its points mapped by a transform. For a stroke, the
    /// zero-length segments are pruned (https://html.spec.whatwg.org/multipage/canvas.html#trace-a-path: "zero-length line
    /// segments must be pruned before stroking a path"): a subpath without other segment draws nothing.
    /// </summary>
    public DisplayPath ToDisplayPath(FillRule fillRule, DisplayMatrix? transform = null, bool pruneZeroLength = false)
    {
        var path = new DisplayPath { FillRule = fillRule };
        var index = 0;
        PhysicalOffset current = default, start = default;
        var pendingMove = false;
        foreach (var verb in _verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                {
                    current = start = _points[index++];
                    if (pruneZeroLength)
                    {
                        pendingMove = true;
                    }
                    else
                    {
                        var p = Map(current);
                        path.MoveTo(p.X, p.Y);
                    }

                    break;
                }

                case PathVerb.LineTo:
                {
                    var end = _points[index++];
                    if (pruneZeroLength && end == current)
                        break;

                    EmitPendingMove();
                    var p = Map(end);
                    path.LineTo(p.X, p.Y);
                    current = end;
                    break;
                }

                case PathVerb.QuadTo:
                {
                    var control = _points[index++];
                    var end = _points[index++];
                    if (pruneZeroLength && control == current && end == current)
                        break;

                    EmitPendingMove();
                    var c = Map(control);
                    var p = Map(end);
                    path.QuadTo(c.X, c.Y, p.X, p.Y);
                    current = end;
                    break;
                }

                case PathVerb.CubicTo:
                {
                    var control1 = _points[index++];
                    var control2 = _points[index++];
                    var end = _points[index++];
                    if (pruneZeroLength && control1 == current && control2 == current && end == current)
                        break;

                    EmitPendingMove();
                    var c1 = Map(control1);
                    var c2 = Map(control2);
                    var p = Map(end);
                    path.CubicTo(c1.X, c1.Y, c2.X, c2.Y, p.X, p.Y);
                    current = end;
                    break;
                }

                default:
                    // A closed subpath without segment draws nothing once its zero-length segments are pruned; a segment
                    // after the close starts a new subpath at the first point of the closed one
                    if (!pendingMove)
                    {
                        path.Close();
                    }

                    current = start;
                    pendingMove = pruneZeroLength;
                    break;
            }
        }

        return path;

        void EmitPendingMove()
        {
            if (!pendingMove)
                return;

            pendingMove = false;
            var p = Map(current);
            path.MoveTo(p.X, p.Y);
        }

        PhysicalOffset Map(PhysicalOffset point) => transform is { } matrix ? matrix.Apply(point) : point;
    }

    /// <summary>Returns the commands of the path for the SVG geometry (point in fill, point in stroke), with its points mapped by a transform.</summary>
    public List<SvgPathCommand> ToSvgCommands(DisplayMatrix? transform = null)
    {
        var commands = new List<SvgPathCommand>(_verbs.Count);
        var index = 0;
        foreach (var verb in _verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    commands.Add(new SvgPathCommand('M', Values(1)));
                    break;

                case PathVerb.LineTo:
                    commands.Add(new SvgPathCommand('L', Values(1)));
                    break;

                case PathVerb.QuadTo:
                    commands.Add(new SvgPathCommand('Q', Values(2)));
                    break;

                case PathVerb.CubicTo:
                    commands.Add(new SvgPathCommand('C', Values(3)));
                    break;

                default:
                    commands.Add(new SvgPathCommand('Z', []));
                    break;
            }
        }

        return commands;

        float[] Values(int count)
        {
            var values = new float[count * 2];
            for (var i = 0; i < count; i++)
            {
                var point = _points[index++];
                if (transform is { } matrix)
                {
                    point = matrix.Apply(point);
                }

                values[i * 2] = (float)point.X;
                values[(i * 2) + 1] = (float)point.Y;
            }

            return values;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The methods of the CanvasPath mixin (https://html.spec.whatwg.org/multipage/canvas.html#canvaspath): the coordinates are
    // in user space, mapped by the transform before they are stored
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-closepath</summary>
    public void ClosePath()
    {
        // "If the object's path has no subpaths, then return": else the subpath is closed and a new one starts at its first point
        if (!HasSubpaths)
            return;

        _verbs.Add(PathVerb.Close);
        _lastPoint = _subpathStart;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-moveto</summary>
    public void MoveTo(double x, double y, DisplayMatrix transform)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return;

        MoveToStored(transform.Apply(new PhysicalOffset(x, y)));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lineto</summary>
    public void LineTo(double x, double y, DisplayMatrix transform)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return;

        // "If the object's path has no subpaths, then ensure there is a subpath for (x, y)"
        var point = transform.Apply(new PhysicalOffset(x, y));
        if (!HasSubpaths)
        {
            MoveToStored(point);
            return;
        }

        LineToStored(point);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-quadraticcurveto</summary>
    public void QuadraticCurveTo(double cpx, double cpy, double x, double y, DisplayMatrix transform)
    {
        if (!double.IsFinite(cpx) || !double.IsFinite(cpy) || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        var control = transform.Apply(new PhysicalOffset(cpx, cpy));
        EnsureSubpath(control);
        _verbs.Add(PathVerb.QuadTo);
        _points.Add(control);
        _lastPoint = transform.Apply(new PhysicalOffset(x, y));
        _points.Add(_lastPoint);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beziercurveto</summary>
    public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y, DisplayMatrix transform)
    {
        if (!double.IsFinite(cp1x) || !double.IsFinite(cp1y) || !double.IsFinite(cp2x) || !double.IsFinite(cp2y) || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        var control1 = transform.Apply(new PhysicalOffset(cp1x, cp1y));
        EnsureSubpath(control1);
        _verbs.Add(PathVerb.CubicTo);
        _points.Add(control1);
        _points.Add(transform.Apply(new PhysicalOffset(cp2x, cp2y)));
        _lastPoint = transform.Apply(new PhysicalOffset(x, y));
        _points.Add(_lastPoint);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arcto</summary>
    /// <param name="inverse">The inverse of <paramref name="transform"/> (the last point is mapped back to user space), or null when it is not invertible.</param>
    public void ArcTo(double x1, double y1, double x2, double y2, double radius, DisplayMatrix transform, DisplayMatrix? inverse)
    {
        // 1. Non-finite arguments are ignored
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2) || !double.IsFinite(radius))
            return;

        // 2. Ensure there is a subpath for (x1, y1)
        EnsureSubpath(transform.Apply(new PhysicalOffset(x1, y1)));

        // 3. A negative radius is an error
        if (radius < 0)
            throw new DomException("The radius must not be negative", "IndexSizeError");

        // 4. The last point of the subpath, in user space
        if (inverse is not { } toUser)
            return;

        var p0 = toUser.Apply(_lastPoint);
        var p1 = new PhysicalOffset(x1, y1);
        var p2 = new PhysicalOffset(x2, y2);

        // 5-6. Coincident points, a zero radius or collinear points: a straight line to (x1, y1)
        var collinear = Math.Abs(((p1.X - p0.X) * (p2.Y - p0.Y)) - ((p2.X - p0.X) * (p1.Y - p0.Y))) <= 1e-12 * Math.Max(1, Math.Abs((p1.X - p0.X) * (p2.Y - p0.Y)));
        if (p0 == p1 || p1 == p2 || radius == 0 || collinear)
        {
            LineToStored(transform.Apply(p1));
            return;
        }

        // 7. The arc of the circle tangent to the lines (P0, P1) and (P1, P2)
        var v1 = new PhysicalOffset(p0.X - p1.X, p0.Y - p1.Y);
        var v2 = new PhysicalOffset(p2.X - p1.X, p2.Y - p1.Y);
        var length1 = Math.Sqrt((v1.X * v1.X) + (v1.Y * v1.Y));
        var length2 = Math.Sqrt((v2.X * v2.X) + (v2.Y * v2.Y));
        var cos = ((v1.X * v2.X) + (v1.Y * v2.Y)) / (length1 * length2);
        var angle = Math.Acos(Math.Clamp(cos, -1, 1));
        var tangentDistance = radius / Math.Tan(angle / 2);
        var start = new PhysicalOffset(p1.X + (v1.X / length1 * tangentDistance), p1.Y + (v1.Y / length1 * tangentDistance));
        var end = new PhysicalOffset(p1.X + (v2.X / length2 * tangentDistance), p1.Y + (v2.Y / length2 * tangentDistance));

        // The center is on the bisector, at radius / sin(angle / 2) from P1
        var bisector = new PhysicalOffset((v1.X / length1) + (v2.X / length2), (v1.Y / length1) + (v2.Y / length2));
        var bisectorLength = Math.Sqrt((bisector.X * bisector.X) + (bisector.Y * bisector.Y));
        var centerDistance = radius / Math.Sin(angle / 2);
        var center = new PhysicalOffset(p1.X + (bisector.X / bisectorLength * centerDistance), p1.Y + (bisector.Y / bisectorLength * centerDistance));

        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);

        // The arc turns clockwise when P0, P1, P2 turn clockwise (the y axis points down)
        var clockwise = (((p1.X - p0.X) * (p2.Y - p1.Y)) - ((p1.Y - p0.Y) * (p2.X - p1.X))) > 0;
        var sweep = endAngle - startAngle;
        if (clockwise && sweep < 0)
        {
            sweep += 2 * Math.PI;
        }
        else if (!clockwise && sweep > 0)
        {
            sweep -= 2 * Math.PI;
        }

        LineToStored(transform.Apply(start));
        AddArcBeziers(center, radius, radius, 0, startAngle, sweep, transform);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arc</summary>
    public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise, DisplayMatrix transform)
        => Ellipse(x, y, radius, radius, 0, startAngle, endAngle, counterclockwise, transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ellipse</summary>
    public void Ellipse(double x, double y, double radiusX, double radiusY, double rotation, double startAngle, double endAngle, bool counterclockwise, DisplayMatrix transform)
    {
        // 1. Non-finite arguments are ignored
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(radiusX) || !double.IsFinite(radiusY) || !double.IsFinite(rotation) || !double.IsFinite(startAngle) || !double.IsFinite(endAngle))
            return;

        // 2. Negative radii are errors
        if (radiusX < 0)
            throw new DomException("The radius must not be negative", "IndexSizeError");

        if (radiusY < 0)
            throw new DomException("The radius must not be negative", "IndexSizeError");

        // The sweep: the full ellipse when the difference is 2π or more in the direction of the arc, else the angle from the
        // start angle to the end angle in that direction, modulo 2π (as the browsers: an end angle 2π away is a full ellipse)
        var twoPi = 2 * Math.PI;
        var canonicalStart = startAngle % twoPi;
        if (canonicalStart < 0)
        {
            canonicalStart += twoPi;
        }

        endAngle += canonicalStart - startAngle;
        startAngle = canonicalStart;
        double sweep;
        if (!counterclockwise && endAngle - startAngle >= twoPi)
        {
            sweep = twoPi;
        }
        else if (counterclockwise && startAngle - endAngle >= twoPi)
        {
            sweep = -twoPi;
        }
        else if (!counterclockwise && startAngle > endAngle)
        {
            sweep = twoPi - ((startAngle - endAngle) % twoPi);
        }
        else if (counterclockwise && startAngle < endAngle)
        {
            sweep = -(twoPi - ((endAngle - startAngle) % twoPi));
        }
        else
        {
            sweep = endAngle - startAngle;
        }

        // 3. A line from the last point to the start point of the arc (or a new subpath at the start point)
        var center = new PhysicalOffset(x, y);
        var startPoint = transform.Apply(PointOnEllipse(center, radiusX, radiusY, rotation, startAngle));
        if (HasSubpaths)
        {
            LineToStored(startPoint);
        }
        else
        {
            MoveToStored(startPoint);
        }

        // 4. The arc (a degenerate ellipse is a line along its axis)
        AddArcBeziers(center, radiusX, radiusY, rotation, startAngle, sweep, transform);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rect</summary>
    public void Rect(double x, double y, double w, double h, DisplayMatrix transform)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h))
            return;

        // A closed subpath of the 4 corners, then a new subpath at (x, y)
        MoveToStored(transform.Apply(new PhysicalOffset(x, y)));
        LineToStored(transform.Apply(new PhysicalOffset(x + w, y)));
        LineToStored(transform.Apply(new PhysicalOffset(x + w, y + h)));
        LineToStored(transform.Apply(new PhysicalOffset(x, y + h)));
        ClosePath();
        MoveToStored(transform.Apply(new PhysicalOffset(x, y)));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect: <paramref name="radii"/> are the radii of
    /// the corners in the order upper left, upper right, lower right, lower left, already validated (see
    /// <see cref="NormalizeRoundRectRadii"/>).
    /// </summary>
    public void RoundRect(double x, double y, double w, double h, (double X, double Y)[] radii, DisplayMatrix transform)
    {
        var (upperLeft, upperRight, lowerRight, lowerLeft) = (radii[0], radii[1], radii[2], radii[3]);

        // 9. The corners are scaled down when they overlap
        var top = upperLeft.X + upperRight.X;
        var right = upperRight.Y + lowerRight.Y;
        var bottom = lowerRight.X + lowerLeft.X;
        var left = upperLeft.Y + lowerLeft.Y;
        var scale = Math.Min(Math.Min(Ratio(Math.Abs(w), top), Ratio(Math.Abs(h), right)), Math.Min(Ratio(Math.Abs(w), bottom), Ratio(Math.Abs(h), left)));
        if (scale < 1)
        {
            upperLeft = (upperLeft.X * scale, upperLeft.Y * scale);
            upperRight = (upperRight.X * scale, upperRight.Y * scale);
            lowerRight = (lowerRight.X * scale, lowerRight.Y * scale);
            lowerLeft = (lowerLeft.X * scale, lowerLeft.Y * scale);
        }

        // 10. A closed subpath from the end of the upper left corner (the corner at (x, y)): a negative width or height mirrors
        // the rectangle and its path
        var sx = w < 0 ? -1 : 1;
        var sy = h < 0 ? -1 : 1;
        MoveToStored(transform.Apply(new PhysicalOffset(x + (sx * upperLeft.X), y)));
        LineToStored(transform.Apply(new PhysicalOffset(x + w - (sx * upperRight.X), y)));
        Corner(new PhysicalOffset(x + w - (sx * upperRight.X), y + (sy * upperRight.Y)), upperRight, -Math.PI / 2);
        LineToStored(transform.Apply(new PhysicalOffset(x + w, y + h - (sy * lowerRight.Y))));
        Corner(new PhysicalOffset(x + w - (sx * lowerRight.X), y + h - (sy * lowerRight.Y)), lowerRight, 0);
        LineToStored(transform.Apply(new PhysicalOffset(x + (sx * lowerLeft.X), y + h)));
        Corner(new PhysicalOffset(x + (sx * lowerLeft.X), y + h - (sy * lowerLeft.Y)), lowerLeft, Math.PI / 2);
        LineToStored(transform.Apply(new PhysicalOffset(x, y + (sy * upperLeft.Y))));
        Corner(new PhysicalOffset(x + (sx * upperLeft.X), y + (sy * upperLeft.Y)), upperLeft, Math.PI);
        ClosePath();

        // 11. A new subpath at (x, y)
        MoveToStored(transform.Apply(new PhysicalOffset(x, y)));

        static double Ratio(double length, double sum) => sum > length ? length / sum : 1;

        void Corner(PhysicalOffset center, (double X, double Y) radius, double startAngle)
        {
            if (radius.X <= 0 || radius.Y <= 0)
                return;

            // The quarter of ellipse turns clockwise in the coordinates of the rectangle (counterclockwise when it is flipped once)
            var flipped = (sx < 0) != (sy < 0);
            var angle = startAngle;
            if (sx < 0)
            {
                angle = Math.PI - angle;
            }

            if (sy < 0)
            {
                angle = -angle;
            }

            AddArcBeziers(center, radius.X, radius.Y, 0, angle, flipped ? -Math.PI / 2 : Math.PI / 2, transform);
        }
    }

    /// <summary>
    /// Validates the radii of <c>roundRect()</c> (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect,
    /// steps 2-6): a number, a <see cref="DomPointInit"/> or a list of 1 to 4 of them. Returns null when a value is not finite
    /// (nothing is added), the radii of the 4 corners otherwise.
    /// </summary>
    /// <exception cref="RangeErrorException">The list is empty or has more than 4 values, or a radius is negative.</exception>
    public static (double X, double Y)[]? NormalizeRoundRectRadii(object? radii)
    {
        var list = radii switch
        {
            IReadOnlyList<object?> values => values,
            _ => [radii],
        };

        if (list.Count is < 1 or > 4)
            throw new RangeErrorException("The number of radii must be between 1 and 4");

        var normalized = new List<(double X, double Y)>(list.Count);
        var allFinite = true;
        foreach (var radius in list)
        {
            switch (radius)
            {
                case DomPointInit point:
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    {
                        allFinite = false;
                        continue;
                    }

                    if (point.X < 0 || point.Y < 0)
                        throw new RangeErrorException("The radii must not be negative");

                    normalized.Add((point.X, point.Y));
                    break;

                default:
                    var value = radius switch
                    {
                        double number => number,
                        float number => number,
                        int number => number,
                        null => 0,
                        _ => Convert.ToDouble(radius, System.Globalization.CultureInfo.InvariantCulture),
                    };

                    if (!double.IsFinite(value))
                    {
                        allFinite = false;
                        continue;
                    }

                    if (value < 0)
                        throw new RangeErrorException("The radii must not be negative");

                    normalized.Add((value, value));
                    break;
            }
        }

        if (!allFinite)
            return null;

        return normalized.Count switch
        {
            4 => [normalized[0], normalized[1], normalized[2], normalized[3]],
            3 => [normalized[0], normalized[1], normalized[2], normalized[1]],
            2 => [normalized[0], normalized[1], normalized[0], normalized[1]],
            _ => [normalized[0], normalized[0], normalized[0], normalized[0]],
        };
    }

    /// <summary>Starts a new subpath at a point (in the stored coordinates).</summary>
    private void MoveToStored(PhysicalOffset point)
    {
        _verbs.Add(PathVerb.MoveTo);
        _points.Add(point);
        _subpathStart = _lastPoint = point;
        HasSubpaths = true;
    }

    /// <summary>Adds a straight line to a point (in the stored coordinates).</summary>
    private void LineToStored(PhysicalOffset point)
    {
        if (!HasSubpaths)
        {
            MoveToStored(point);
            return;
        }

        _verbs.Add(PathVerb.LineTo);
        _points.Add(point);
        _lastPoint = point;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#ensure-there-is-a-subpath</summary>
    private void EnsureSubpath(PhysicalOffset point)
    {
        if (!HasSubpaths)
        {
            MoveToStored(point);
        }
    }

    private static PhysicalOffset PointOnEllipse(PhysicalOffset center, double radiusX, double radiusY, double rotation, double angle)
    {
        var (sin, cos) = Math.SinCos(angle);
        var (rotationSin, rotationCos) = Math.SinCos(rotation);
        var x = radiusX * cos;
        var y = radiusY * sin;
        return new PhysicalOffset(center.X + (x * rotationCos) - (y * rotationSin), center.Y + (x * rotationSin) + (y * rotationCos));
    }

    /// <summary>
    /// Adds the cubic Bézier curves of an arc of an ellipse from <paramref name="startAngle"/> (the last point is its start point)
    /// over <paramref name="sweep"/> radians (clockwise when positive), mapped by the transform.
    /// </summary>
    private void AddArcBeziers(PhysicalOffset center, double radiusX, double radiusY, double rotation, double startAngle, double sweep, DisplayMatrix transform)
    {
        if (sweep == 0)
            return;

        var segments = (int)Math.Ceiling(Math.Abs(sweep) / MaxBezierArc - 1e-9);
        segments = Math.Max(1, segments);
        var step = sweep / segments;

        // The distance of the control points along the tangents of a unit circle (https://pomax.github.io/bezierinfo/#circles_cubic)
        var k = 4.0 / 3.0 * Math.Tan(step / 4);
        var (rotationSin, rotationCos) = Math.SinCos(rotation);
        var angle = startAngle;
        for (var i = 0; i < segments; i++)
        {
            var (sin1, cos1) = Math.SinCos(angle);
            var (sin2, cos2) = Math.SinCos(angle + step);
            var c1 = Map(cos1 - (k * sin1), sin1 + (k * cos1));
            var c2 = Map(cos2 + (k * sin2), sin2 - (k * cos2));
            var end = Map(cos2, sin2);
            _verbs.Add(PathVerb.CubicTo);
            _points.Add(transform.Apply(c1));
            _points.Add(transform.Apply(c2));
            _lastPoint = transform.Apply(end);
            _points.Add(_lastPoint);
            angle += step;
        }

        PhysicalOffset Map(double unitX, double unitY)
        {
            var x = unitX * radiusX;
            var y = unitY * radiusY;
            return new PhysicalOffset(center.X + (x * rotationCos) - (y * rotationSin), center.Y + (x * rotationSin) + (y * rotationCos));
        }
    }
}
