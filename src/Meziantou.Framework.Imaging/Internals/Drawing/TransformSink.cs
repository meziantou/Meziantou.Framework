namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Places the contours of a flattened path on the canvas: every vertex goes through the transformation of the canvas
/// before it reaches the sink of the fill.
/// </summary>
/// <remarks>
/// <para>
/// The path is flattened before it is transformed, by chords that are closer to its curves by the factor of the
/// transformation (<see cref="PathSink.Scale"/>): on the canvas they stay within the tolerance of every curve.
/// </para>
/// <para>
/// A vertex whose magnitude exceeds the largest coordinate once it is transformed (where the frame is on the canvas
/// the coordinates are checked on) is not rejected: from the first one, the contours of the path are cut by the range
/// of the coordinates (<see cref="RangeClipSink"/>), which keeps what the canvas shows; the contours before it, and
/// those of a path that stays in the range, reach the target as they are. A path that a pen strokes once it is placed
/// is not cut, since its dashes and joins depend on all of it: the pieces of its stroke are (<see cref="Stroker.ClipToRange"/>).
/// Only a coordinate that is not a finite number is rejected.
/// </para>
/// </remarks>
internal sealed class TransformSink : PathSink
{
    private PathSink? _target;
    private Affine2D _transform;
    private double _originX;
    private double _originY;
    private bool _isChecked;
    private Stroker? _stroker;
    private RangeClipSink? _clipper;
    private bool _isClipping;
    private bool _hasContour;
    private double _firstX;
    private double _firstY;
    private double _lastX;
    private double _lastY;

    /// <summary>Sets the sink the transformed vertices go to, and the transformation.</summary>
    /// <param name="target">The sink of the fill, or what strokes the path once it is placed.</param>
    /// <param name="transform">The transformation.</param>
    /// <param name="scale">The largest factor by which the transformation enlarges a length.</param>
    /// <param name="originX">Where the frame of the vertices is on the canvas the largest coordinate is checked on, along x: not zero for the image of a layer of a rectangle.</param>
    /// <param name="originY">Where the frame of the vertices is on that canvas, along y.</param>
    /// <param name="isChecked"><see langword="false"/> when the vertices are those of the canvas, only moved to the image of a layer of a rectangle: the canvas does not check them.</param>
    /// <param name="stroker">The stroker of a path that is stroked once it is placed: its pieces are cut by the range of the coordinates once a vertex leaves it, instead of the path. <see langword="null"/> for a fill, whose contours are cut.</param>
    public void Configure(PathSink target, in Affine2D transform, double scale, double originX, double originY, bool isChecked, Stroker? stroker = null)
    {
        _target = target;
        _transform = transform;
        _originX = originX;
        _originY = originY;
        _isChecked = isChecked;
        _stroker = stroker;
        _isClipping = false;
        _hasContour = false;
        Scale = scale;
    }

    public override void MoveTo(double x, double y)
    {
        if (Transform(ref x, ref y) && !_isClipping)
        {
            StartClipping(resume: false);
        }

        _hasContour = true;
        _firstX = _lastX = x;
        _firstY = _lastY = y;
        (_isClipping ? _clipper! : _target!).MoveTo(x, y);
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        if (Transform(ref x, ref y) && !_isClipping)
        {
            StartClipping(resume: _hasContour);
        }

        if (!_hasContour)
        {
            _hasContour = true;
            _firstX = x;
            _firstY = y;
        }

        _lastX = x;
        _lastY = y;
        (_isClipping ? _clipper! : _target!).LineTo(x, y, smooth);
    }

    public override void Close(bool smooth)
    {
        // A segment that follows starts at the first point of the contour
        _lastX = _firstX;
        _lastY = _firstY;
        (_isClipping ? _clipper! : _target!).Close(smooth);
    }

    public override void End()
    {
        (_isClipping ? _clipper! : _target!).End();
        _isClipping = false;
        _hasContour = false;
    }

    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a finite number once it is transformed.</exception>
    [DoesNotReturn]
    public static void ThrowOutOfRange(double coordinate)
        => throw new ArgumentOutOfRangeException(nameof(coordinate), coordinate, "A coordinate is not a finite number once the transformation of the canvas is applied.");

    /// <summary>Gets a value indicating whether a point of the frame, which the canvas checks, is beyond the largest coordinate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate of the point is not a finite number.</exception>
    public static bool IsOutOfRange(double x, double y, double originX, double originY)
    {
        // Written so that a value that is not a number is rejected too
        if (Math.Abs(x + originX) <= DrawingGuard.MaxCoordinate && Math.Abs(y + originY) <= DrawingGuard.MaxCoordinate)
            return false;

        if (!(double.IsFinite(x + originX) && double.IsFinite(y + originY)))
            ThrowOutOfRange(double.IsFinite(x + originX) ? y : x);

        return true;
    }

    /// <summary>Transforms a vertex.</summary>
    /// <returns><see langword="true"/> when the vertex is beyond the range of the coordinates.</returns>
    private bool Transform(ref double x, ref double y)
    {
        var px = _transform.TransformX(x, y);
        var py = _transform.TransformY(x, y);
        x = px;
        y = py;
        return _isChecked && IsOutOfRange(px, py, _originX, _originY);
    }

    /// <summary>Starts cutting the path by the range of the coordinates, from the current contour on.</summary>
    private void StartClipping(bool resume)
    {
        if (_stroker is not null)
        {
            // The path is stroked as it is: the pieces of the stroke are cut
            _stroker.ClipToRange();
            return;
        }

        var clipper = _clipper ??= new RangeClipSink();
        clipper.Configure(_target!, -DrawingGuard.MaxCoordinate - _originX, -DrawingGuard.MaxCoordinate - _originY, DrawingGuard.MaxCoordinate - _originX, DrawingGuard.MaxCoordinate - _originY);
        if (resume)
        {
            clipper.Resume(_firstX, _firstY, _lastX, _lastY);
        }

        _isClipping = true;
    }
}
