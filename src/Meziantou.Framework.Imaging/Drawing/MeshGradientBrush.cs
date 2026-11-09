namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// A brush painting a mesh of patches, each a surface bounded by four Bézier curves with a color at each corner, mixed
/// smoothly across it: the free-form gradients of PDF (shading types 6 and 7) and of SVG 2 (<c>meshgradient</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Geometry.</b> Each <see cref="MeshGradientPatch"/> maps the square of its parameters <c>(u, v)</c> to a surface
/// whose sides are cubic Bézier curves (see <see cref="MeshGradientPatch"/>). A pixel takes the color of the parameters of
/// the position of its center; a position no patch covers has no color, and nothing is painted there, whatever the shape
/// that is painted: fill the outline of the mesh, or a shape inside it. The patches are painted in order, so a later patch
/// covers an earlier one where they overlap, and where a patch folds over itself the position takes the parameters with
/// the largest <c>u</c>, then the largest <c>v</c>, as PDF specifies.
/// </para>
/// <para>
/// <b>Colors.</b> The color of the parameters is the bilinear mix of the corner colors of the patch. The colors are
/// converted to the pixel format of the frame first, then mixed on alpha-premultiplied values, each rounded as
/// <c>floor(v + 1/2 + 2^-20)</c>, as the stops of a gradient: a patch whose corners have one color paints exactly what a
/// <see cref="SolidBrush"/> of that color paints.
/// </para>
/// <para>
/// <b>Precision and cost.</b> Before a shape is painted, every patch is cut into a grid of small quadrilaterals, flat
/// enough that they are less than a tenth of a pixel away from the surface on the canvas (at most 512 by 512 of them per
/// patch), and the parameters of a position are interpolated linearly across the two triangles of its quadrilateral. The
/// memory of the grids is charged to the allocation limit of the image. Each pixel then tests the quadrilaterals that a
/// uniform grid of buckets says may contain it, so the cost of a pixel does not grow with the number of patches. The
/// arithmetic uses <c>+ - * /</c> and square roots only: the pixels are the same on every platform.
/// </para>
/// </remarks>
public sealed class MeshGradientBrush : Brush
{
    private readonly MeshGradientPatch[] _patches;

    /// <summary>Initializes a new instance of the <see cref="MeshGradientBrush"/> class.</summary>
    /// <param name="patches">The patches, painted in order; at least one. The array is copied (the patches are immutable).</param>
    /// <exception cref="ArgumentException">There is no patch, or a patch is <see langword="null"/>.</exception>
    public MeshGradientBrush(ReadOnlySpan<MeshGradientPatch> patches)
    {
        if (patches.IsEmpty)
            throw new ArgumentException("A mesh gradient needs at least one patch.", nameof(patches));

        foreach (var patch in patches)
        {
            if (patch is null)
                throw new ArgumentException("A patch is null.", nameof(patches));
        }

        _patches = patches.ToArray();
    }

    /// <summary>Gets the patches, in the order they are painted.</summary>
    public ReadOnlyMemory<MeshGradientPatch> Patches => _patches;

    /// <summary>
    /// Gets the transformation that places the mesh on the canvas: the control points are given before it is applied.
    /// Defaults to the identity.
    /// </summary>
    /// <remarks>
    /// The color of a pixel is the color the brush has, before the transformation, at the position the center of the
    /// pixel comes from. With a transformation that flattens the plane to a line or a point, the brush paints nothing.
    /// </remarks>
    public AffineTransform Transform { get; init; }

    /// <summary>Gets the patches without copying them.</summary>
    internal ReadOnlySpan<MeshGradientPatch> PatchSpan => _patches;
}
