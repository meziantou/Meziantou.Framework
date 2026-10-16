namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Places the contours of a flattened path on the canvas: every vertex goes through the transformation of the canvas
/// before it reaches the sink of the fill.
/// </summary>
/// <remarks>
/// The path is flattened before it is transformed, by chords that are closer to its curves by the factor of the
/// transformation (<see cref="PathSink.Scale"/>): on the canvas they stay within the tolerance of every curve.
/// </remarks>
internal sealed class TransformSink : PathSink
{
    private PathSink? _target;
    private Affine2D _transform;
    private double _originX;
    private double _originY;
    private bool _isChecked;

    /// <summary>Sets the sink the transformed vertices go to, and the transformation.</summary>
    /// <param name="target">The sink of the fill.</param>
    /// <param name="transform">The transformation.</param>
    /// <param name="scale">The largest factor by which the transformation enlarges a length.</param>
    /// <param name="originX">Where the frame of the vertices is on the canvas the largest coordinate is checked on, along x: not zero for the image of a layer of a rectangle.</param>
    /// <param name="originY">Where the frame of the vertices is on that canvas, along y.</param>
    /// <param name="isChecked"><see langword="false"/> when the vertices are those of the canvas, only moved to the image of a layer of a rectangle: the canvas does not check them.</param>
    public void Configure(PathSink target, in Affine2D transform, double scale, double originX, double originY, bool isChecked)
    {
        _target = target;
        _transform = transform;
        _originX = originX;
        _originY = originY;
        _isChecked = isChecked;
        Scale = scale;
    }

    public override void MoveTo(double x, double y)
    {
        Transform(ref x, ref y);
        _target!.MoveTo(x, y);
    }

    public override void LineTo(double x, double y, bool smooth)
    {
        Transform(ref x, ref y);
        _target!.LineTo(x, y, smooth);
    }

    public override void Close(bool smooth) => _target!.Close(smooth);

    public override void End() => _target!.End();

    /// <exception cref="ArgumentOutOfRangeException">A coordinate is beyond the largest one once it is transformed.</exception>
    [DoesNotReturn]
    public static void ThrowOutOfRange(double coordinate)
        => throw new ArgumentOutOfRangeException(nameof(coordinate), coordinate, "The magnitude of a coordinate exceeds 16777216 once the transformation of the canvas is applied.");

    private void Transform(ref double x, ref double y)
    {
        var px = _transform.TransformX(x, y);
        var py = _transform.TransformY(x, y);

        // Written so that a value that is not a number is rejected too
        if (_isChecked && !(Math.Abs(px + _originX) <= DrawingGuard.MaxCoordinate && Math.Abs(py + _originY) <= DrawingGuard.MaxCoordinate))
            ThrowOutOfRange(Math.Abs(px + _originX) <= DrawingGuard.MaxCoordinate ? py + _originY : px + _originX);

        x = px;
        y = py;
    }
}
