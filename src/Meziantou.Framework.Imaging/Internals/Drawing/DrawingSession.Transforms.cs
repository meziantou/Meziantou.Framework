using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The transformation of the canvas: what places the drawings of a callback on the pixels.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> While a transformation lasts, a draw call draws what it would draw without it, transformed: the
/// shape, the stroke of a pen (stroked as the path is given, then transformed, so that the pen is transformed with the
/// path), the gradient or the image of a brush, each glyph of a text with its paints, an image, the path of a clip,
/// and the offset and the blur of a shadow. A transformation that starts inside another one applies first. What has no
/// shape of its own is not transformed: filling or clearing the whole canvas, and drawing a layer when it ends.
/// </para>
/// <para>
/// A shape is flattened as it is given, by chords closer to its curves by the largest factor the transformation
/// enlarges a length by (<see cref="PathSink.Scale"/>), then its vertices are transformed: on the canvas, the chords
/// stay within the tolerance of every curve. A vertex that the transformation moves beyond the largest coordinate is
/// rejected, before any pixel of the call changes.
/// </para>
/// <para>Without transformation, nothing here is used: the draw calls are, bit for bit, what they are without this file.</para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private readonly List<AffineTransform> _transforms = [];
    private TransformSink? _transformSink;
    private bool _hasTransform;
    private AffineTransform _transform;
    private Affine2D _transformMatrix;
    private double _transformScale = 1;

    /// <summary>Starts a transformation, inside the current one: the draw calls that follow are transformed by it, then by the current one.</summary>
    /// <exception cref="ArgumentException">A coefficient of the combined transformation is not a finite number.</exception>
    public void PushTransform(AffineTransform transform)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var combined = transform.Then(_transform);
        _transforms.Add(_transform);
        SetTransform(combined);
    }

    /// <summary>Ends the transformation that was started last.</summary>
    public void PopTransform()
    {
        var previous = _transforms[^1];
        _transforms.RemoveAt(_transforms.Count - 1);
        SetTransform(previous);
    }

    private void SetTransform(AffineTransform transform)
    {
        _transform = transform;
        _hasTransform = !transform.IsIdentity;
        _transformMatrix = new Affine2D(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY);
        _transformScale = _hasTransform ? PathFlattener.GetLargestRadius(transform.M11, transform.M12, transform.M21, transform.M22) : 1;
    }

    /// <summary>Gets the transformation that places on the canvas what a transformation of a brush or of an image places on the drawing.</summary>
    private AffineTransform ToCanvas(AffineTransform transform) => _hasTransform ? transform.Then(_transform) : transform;

    /// <summary>Gets the transformation that places a glyph on the canvas, from the one that places it in the drawing.</summary>
    private Affine2D ToCanvas(in Affine2D placement)
        => _hasTransform ? _transformMatrix.Then([placement.A, placement.B, placement.C, placement.D, placement.Tx, placement.Ty]) : placement;

    /// <summary>Gets a rectangle of the canvas that contains what a rectangle of the drawing becomes.</summary>
    private RectangleD ToCanvasBounds(RectangleD bounds)
    {
        if (!_hasTransform)
            return bounds;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? bounds.Left : bounds.Right;
            var y = (corner & 2) == 0 ? bounds.Top : bounds.Bottom;
            var px = _transformMatrix.TransformX(x, y);
            var py = _transformMatrix.TransformY(x, y);
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        // Written so that a value that is not a number gives the largest rectangle: the edges say what is inside
        if (!(minX >= -DrawingGuard.MaxCoordinate && minY >= -DrawingGuard.MaxCoordinate && maxX <= DrawingGuard.MaxCoordinate && maxY <= DrawingGuard.MaxCoordinate))
            return RectangleD.FromLTRB(-DrawingGuard.MaxCoordinate, -DrawingGuard.MaxCoordinate, DrawingGuard.MaxCoordinate, DrawingGuard.MaxCoordinate);

        return RectangleD.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>Gets the sink of a fill whose shape is given in the coordinates of the drawing.</summary>
    private PathSink BeginFill()
    {
        var sink = BeginDeviceFill();
        if (!_hasTransform)
            return sink;

        var transformed = _transformSink ??= new TransformSink();
        transformed.Configure(sink, _transformMatrix, _transformScale);
        return transformed;
    }
}
