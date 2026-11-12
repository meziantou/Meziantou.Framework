namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A filter made of primitives that read the drawing of the element, its alpha, and each other's results, as an SVG
/// <c>filter</c> element whose units are the user space.
/// </summary>
/// <remarks>
/// <para>
/// The primitives are computed in order. Each one reads its inputs (<see cref="SceneFilterInput"/>: by default the
/// result of the primitive before it, or the drawing of the element for the first one) and computes its result in its
/// subregion, transparent outside it. The result of the last primitive is drawn, and nothing is drawn outside the region
/// of the filter.
/// </para>
/// <para>
/// The region, the subregions, and the lengths and positions of the primitives are in the coordinates of the parent of
/// the element, as the other filters, the masks and the clips of the groups: outside the transform of the element. The
/// SVG and image renderers draw the graphs; the PDF renderer throws <see cref="NotSupportedException"/> for a scene
/// that has one.
/// </para>
/// </remarks>
public sealed class SceneFilterGraph : SceneFilter
{
    private readonly SceneFilterPrimitive[] _primitives;

    /// <summary>Initializes a new instance of the <see cref="SceneFilterGraph"/> class.</summary>
    /// <param name="primitives">The primitives, in the order they are computed. They are copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primitives"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// There is no primitive, a primitive is <see langword="null"/>, or a primitive reads the result of a primitive that
    /// is not before it.
    /// </exception>
    public SceneFilterGraph(IEnumerable<SceneFilterPrimitive> primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        _primitives = [.. primitives];
        if (_primitives.Length is 0)
            throw new ArgumentException("A filter graph needs at least one primitive.", nameof(primitives));

        for (var index = 0; index < _primitives.Length; index++)
        {
            var primitive = _primitives[index] ?? throw new ArgumentException("The primitives cannot contain null.", nameof(primitives));
            foreach (var input in primitive.GetInputs())
            {
                if (input.Kind is SceneFilterInputKind.Result && input.ResultIndex >= index)
                    throw new ArgumentException($"The primitive at index {index} reads the result of the primitive at index {input.ResultIndex}, which is not before it.", nameof(primitives));
            }
        }
    }

    /// <summary>Gets the primitives, in the order they are computed.</summary>
    public IReadOnlyList<SceneFilterPrimitive> Primitives => _primitives;

    /// <summary>
    /// Gets the rectangle the filter draws in (the filter region of SVG), in the coordinates of the parent of the
    /// element, or <see langword="null"/> (the default) for the bounds of what the element draws, enlarged by how far the
    /// primitives move it (<see cref="GetRegion(SceneBounds)"/>).
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height is negative.</exception>
    public SceneBounds? Region
    {
        get;
        init => field = FilterValidation.Bounds(value, nameof(value));
    }

    /// <summary>Gets the rectangle the filter draws in, for an element whose drawing has given bounds.</summary>
    /// <param name="bounds">The bounds of what the element draws without the filter, in the coordinates of its parent.</param>
    /// <returns>
    /// <see cref="Region"/> when it is set; otherwise <paramref name="bounds"/> enlarged on each side by the sum of how far
    /// each primitive moves what it reads: 3 standard deviations for a blur, the offset of an offset or of a shadow, the
    /// radius of a morphology, half the scale of a displacement map, the size of a convolution matrix, 1 for a lighting.
    /// </returns>
    public SceneBounds GetRegion(SceneBounds bounds)
    {
        if (Region is { } region)
            return region;

        var reach = 0.0;
        foreach (var primitive in _primitives)
        {
            reach += primitive.GetReach();
        }

        return SceneGeometry.Inflate(bounds, reach);
    }
}
