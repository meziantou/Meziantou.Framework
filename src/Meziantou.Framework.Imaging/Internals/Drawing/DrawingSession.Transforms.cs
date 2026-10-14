using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The transformation of the canvas: what places the drawings of a callback on the pixels.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> While a transformation lasts, a draw call draws what it would draw without it, transformed: the
/// shape, the stroke of a pen (stroked as the path is given, then transformed, so that the pen is transformed with the
/// path; a pen whose <see cref="Pen.ScalesWithTransform"/> is off strokes the transformed path instead), the gradient
/// or the image of a brush, each glyph of a text with its paints, an image, the path of a clip, and the offset and the blur of a shadow. A transformation that starts inside another one applies first. What has no
/// shape of its own is not transformed: filling or clearing the whole canvas, and drawing a layer when it ends.
/// </para>
/// <para>
/// A shape is flattened as it is given, by chords closer to its curves by the largest factor the transformation
/// enlarges a length by (<see cref="PathSink.Scale"/>), then its vertices are transformed: on the canvas, the chords
/// stay within the tolerance of every curve. A vertex that the transformation moves beyond the largest coordinate is
/// rejected, before any pixel of the call changes.
/// </para>
/// <para>
/// <b>Projective transformations.</b> A transformation whose <c>W</c> is not 1 everywhere places the drawing in
/// perspective. A shape is flattened by chords closer to its curves by a bound of what the transformation does on the
/// rectangle of each curve (<see cref="PathSink.GetScale"/>), then each contour, or each piece of a stroke, is cut
/// where it has no image or an image beyond the largest coordinate (<see cref="ProjectiveSink"/>): nothing is rejected.
/// The paints of the brushes and the images are read through the inverse of the transformation, and what is beyond
/// the horizon has no color. What has a size in the drawing that is not the size of a shape (the blur and the offset
/// of a shadow, a blur, the glyphs a font draws with its own colors) is drawn on an image of the plane of the drawing
/// (<see cref="BeginProjectedScope"/>), then that image is drawn in perspective.
/// </para>
/// <para>
/// <b>Transformations of space</b> (<see cref="Transform3D"/>) are combined with each other in three dimensions, so
/// that a transformation inside another one keeps what it does to z; a transformation of the plane inside one of them
/// is combined as the transformation of space it is. The plane of the drawing is then <c>z = 0</c>.
/// </para>
/// <para>Without transformation, nothing here is used: the draw calls are, bit for bit, what they are without this file.</para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private readonly List<TransformState> _transforms = [];
    private TransformSink? _transformSink;
    private ProjectiveSink? _projectiveSink;
    private ProjectiveSink? _strokeProjectiveSink;
    private bool _hasTransform;
    private AffineTransform _transform;
    private Affine2D _transformMatrix;
    private double _transformScale = 1;

    // A transformation that is not affine: _hasTransform is false, and every use of the transformation reads this one
    private bool _isProjective;
    private ProjectiveTransform _projection;
    private ProjectiveView _projectionView;

    // The transformation of space the current one comes from, which the next ones are combined with
    private bool _hasTransform3D;
    private Transform3D _transform3D;

    /// <summary>Starts a transformation, inside the current one: the draw calls that follow are transformed by it, then by the current one.</summary>
    /// <exception cref="ArgumentException">A coefficient of the combined transformation is not a finite number.</exception>
    public void PushTransform(AffineTransform transform)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_isProjective || _hasTransform3D)
        {
            PushTransform((ProjectiveTransform)transform);
            return;
        }

        var combined = transform.Then(_transform);
        _transforms.Add(GetState());
        SetTransform(combined);
    }

    /// <summary>Starts a projective transformation, inside the current one.</summary>
    /// <exception cref="ArgumentException">A coefficient of the combined transformation is not a finite number.</exception>
    public void PushTransform(ProjectiveTransform transform)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!_isProjective && !_hasTransform3D && transform.TryGetExactAffine(out var affine))
        {
            PushTransform(affine);
            return;
        }

        if (_hasTransform3D)
        {
            PushTransform((Transform3D)transform);
            return;
        }

        var combined = transform.Then(GetProjection());
        _transforms.Add(GetState());
        SetProjection(combined);
    }

    /// <summary>Starts a transformation of space, inside the current one: they are combined in three dimensions.</summary>
    /// <exception cref="ArgumentException">A coefficient of the combined transformation is not a finite number.</exception>
    public void PushTransform(Transform3D transform)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var current = _hasTransform3D ? _transform3D : (Transform3D)GetProjection();
        var combined = transform.Then(current);
        _transforms.Add(GetState());
        SetProjection(combined.ToProjectiveTransform());
        _hasTransform3D = true;
        _transform3D = combined;
    }

    /// <summary>Ends the transformation that was started last.</summary>
    public void PopTransform()
    {
        var previous = _transforms[^1];
        _transforms.RemoveAt(_transforms.Count - 1);
        if (previous.IsProjective)
        {
            SetProjection(previous.Projection);
        }
        else
        {
            SetTransform(previous.Affine);
        }

        _hasTransform3D = previous.Has3D;
        _transform3D = previous.Transform3D;
    }

    private TransformState GetState() => new(_transform, _isProjective, _projection, _hasTransform3D, _transform3D);

    /// <summary>Gets the current transformation, as a projective one.</summary>
    private ProjectiveTransform GetProjection() => _isProjective ? _projection : _transform;

    private void SetTransform(AffineTransform transform)
    {
        _isProjective = false;
        _projection = default;
        _transform = transform;
        _hasTransform = !transform.IsIdentity;
        _transformMatrix = new Affine2D(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY);
        _transformScale = _hasTransform ? PathFlattener.GetLargestRadius(transform.M11, transform.M12, transform.M21, transform.M22) : 1;
    }

    private void SetProjection(ProjectiveTransform transform)
    {
        // A transformation whose W is the same positive number everywhere is affine: the paths of an affine one are faster
        transform = transform.Normalize();
        if (transform.TryGetExactAffine(out var affine))
        {
            SetTransform(affine);
            return;
        }

        _isProjective = true;
        _projection = transform;

        // A pixel around the canvas is seen: what covers part of a pixel on the edge
        _projectionView = new ProjectiveView(transform, -1, -1, Width + 1, Height + 1, reach: 0);
        _hasTransform = false;
        _transform = default;
        _transformMatrix = default;
        _transformScale = GetLocalScale(transform);
    }

    /// <summary>
    /// Gets the factor a projective transformation enlarges the drawing by where <c>W</c> is the one of the origin: the
    /// largest singular value of its linear part divided by <c>|M33|</c>, brought between 1/1024 and 1024. It is the
    /// resolution of the images of the plane of the drawing (<see cref="BeginProjectedScope"/>), of the tiles of the patterns,
    /// and what a blur of the canvas is multiplied by.
    /// </summary>
    private static double GetLocalScale(in ProjectiveTransform transform)
    {
        var scale = PathFlattener.GetLargestRadius(transform.M11, transform.M12, transform.M21, transform.M22) / Math.Abs(transform.M33);
        return scale >= 1.0 / 1024 ? (scale <= 1024 ? scale : 1024) : 1.0 / 1024;
    }

    /// <summary>Gets the transformation that places on the canvas what a transformation of a brush or of an image places on the drawing.</summary>
    private AffineTransform ToCanvas(AffineTransform transform) => _hasTransform ? transform.Then(_transform) : transform;

    /// <summary>Gets the transformation that places on the canvas what a transformation of a brush or of an image places on the drawing, projective or not.</summary>
    private ProjectiveTransform ToCanvasProjection(AffineTransform transform) => _isProjective ? ((ProjectiveTransform)transform).Then(_projection) : ToCanvas(transform);

    /// <summary>Gets the transformation that places a glyph on the canvas, from the one that places it in the drawing.</summary>
    private Affine2D ToCanvas(in Affine2D placement)
        => _hasTransform ? _transformMatrix.Then([placement.A, placement.B, placement.C, placement.D, placement.Tx, placement.Ty]) : placement;

    /// <summary>Gets a rectangle of the canvas that contains what a rectangle of the drawing becomes.</summary>
    private RectangleD ToCanvasBounds(RectangleD bounds)
    {
        if (_isProjective)
            return RectangleD.FromLTRB(-DrawingGuard.MaxCoordinate, -DrawingGuard.MaxCoordinate, DrawingGuard.MaxCoordinate, DrawingGuard.MaxCoordinate);

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
        if (_isProjective)
        {
            var projected = _projectiveSink ??= new ProjectiveSink();
            projected.Configure(sink, _projectionView, isPolyline: false);
            return projected;
        }

        if (!_hasTransform)
            return sink;

        var transformed = _transformSink ??= new TransformSink();
        transformed.Configure(sink, _transformMatrix, _transformScale);
        return transformed;
    }

    /// <summary>Gives a sink that receives the coordinates of the drawing the factor its curves are cut finer by.</summary>
    /// <param name="sink">The sink.</param>
    /// <param name="reach">The distance in the drawing around a curve that what is drawn from it can reach.</param>
    private void SetDrawingScale(PathSink sink, double reach)
    {
        sink.Scale = _transformScale;
        sink.Projection = _isProjective ? _projectionView.Inflate(0, reach) : null;
    }

    /// <summary>A transformation of the stack, as it was before the one that follows it started.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TransformState(AffineTransform Affine, bool IsProjective, ProjectiveTransform Projection, bool Has3D, Transform3D Transform3D);
}
